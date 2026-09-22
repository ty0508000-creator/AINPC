using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Single-file player/quest snapshot; legacy files are read, never removed by migration.</summary>
public static class SaveSystem
{
#if UNITY_EDITOR
    public static string VerificationDirectory;
    public static Action VerificationBeforeCommit;
#endif
    public static string LastError { get; private set; }
    public static event Action<string> OnSaveFailed;
    public static string DirectoryPath
    {
        get
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(VerificationDirectory)) return VerificationDirectory;
#endif
            return Application.persistentDataPath;
        }
    }
    static string SavePath => Path.Combine(DirectoryPath, "player_save.json");

    public static bool HasSave
    {
        get
        {
            if (!TryLoadPlayer(out var data) || data == null)
                return false;

            return (data.hasProgress && !string.IsNullOrEmpty(data.sceneName)) ||
                   (data.hasCheckpoint && !string.IsNullOrEmpty(data.checkpointScene));
        }
    }

    public static void SavePlayer(PlayerStats stats) => SaveGame(stats, QuestManager.Instance);

    public static bool SaveGame(PlayerStats stats, QuestManager manager = null)
        => SaveGame(stats, manager, null, Vector3.zero, null);

    public static bool SaveProgress(PlayerStats stats, string sceneName, Vector3 position, IEnumerable<string> flags)
    {
        if (string.IsNullOrEmpty(sceneName) || !Finite(position.x) || !Finite(position.y))
            return false;

        return SaveGame(stats, QuestManager.Instance, sceneName, position, flags);
    }

    static bool SaveGame(PlayerStats stats, QuestManager manager, string sceneName, Vector3 position, IEnumerable<string> flags)
    {
        try
        {
            // Refuse to overwrite an unreadable slot with a new-game default.
            var previous = ReadExisting();
            var data = stats != null ? Capture(stats) : previous;
            if (data == null) throw new InvalidOperationException("플레이어 준비 전에는 통합 저장할 수 없습니다.");
            if (data.flags == null) data.flags = previous?.flags;
            data.quests = manager != null && manager.IsSaveReady
                ? QuestSaveSystem.Capture(manager, previous?.quests ?? QuestSaveSystem.ReadLegacy())
                : previous?.quests ?? QuestSaveSystem.ReadLegacy();
            if (!string.IsNullOrEmpty(sceneName))
            {
                data.hasProgress = true;
                data.sceneName = sceneName;
                data.posX = position.x;
                data.posY = position.y;
                data.flags = CopyFlags(flags);
            }
            data.snapshotVersion = 3;
            data.revision = checked((previous?.revision ?? 0) + 1);
            Validate(data);
            Directory.CreateDirectory(DirectoryPath);
            string temporary = SavePath + ".tmp";
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data, true));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            // Verify what was actually written before replacing the committed snapshot.
            ReadValidated(temporary);
#if UNITY_EDITOR
            VerificationBeforeCommit?.Invoke();
#endif
            if (File.Exists(SavePath))
            {
                // An invalid primary must never replace a healthy recovery backup.
                bool primaryValid;
                try { ReadValidated(SavePath); primaryValid = true; }
                catch { primaryValid = false; }
                if (primaryValid)
                    File.Replace(temporary, SavePath, SavePath + ".bak");
                else
                {
                    // File.Replace with a null backup is not supported consistently on Windows.
                    // The known-good .bak remains untouched while an already invalid primary is repaired.
                    File.Delete(SavePath);
                    File.Move(temporary, SavePath);
                }
            }
            else File.Move(temporary, SavePath);
            LastError = null;
            return true;
        }
        catch (Exception e)
        {
            LastError = e.Message;
            Debug.LogWarning("[SaveSystem] 저장 실패 (기존 저장 유지): " + LastError);
            OnSaveFailed?.Invoke(LastError);
            return false;
        }
    }

    static PlayerSaveData Capture(PlayerStats stats) => new PlayerSaveData
    {
        hasProgress = true, sceneName = stats.gameObject.scene.path,
        posX = stats.transform.position.x, posY = stats.transform.position.y,
        flags = UnityEngine.Object.FindFirstObjectByType<GameFlow>()?.FlagList(),
        level = stats.Level, hp = stats.HP, maxHP = stats.MaxHP,
        mana = stats.Mana, maxMana = stats.MaxMana, exp = stats.EXP, maxEXP = stats.MaxEXP,
        progressionVersion = 1, statPoints = stats.StatPoints, skillPoints = stats.SkillPoints,
        attributeRanks = (int[])stats.AttributeRanks.Clone(), skillRanks = (int[])stats.SkillRanks.Clone(),
        hasCheckpoint = true, checkpointScene = stats.CheckpointScene,
        checkpointPosition = stats.CheckpointPosition,
        hasMood = stats.GetComponent<MoodSystem>() != null,
        mood = stats.GetComponent<MoodSystem>() != null ? stats.GetComponent<MoodSystem>().Mood : 0f,
        storyChoices = StoryChoiceManager.Instance != null ? StoryChoiceManager.Instance.Capture() : null,
        inventory = stats.GetComponent<PlayerInventory>() != null
            ? stats.GetComponent<PlayerInventory>().Capture()
            : Array.Empty<InventorySaveEntry>()
    };

    static List<string> CopyFlags(IEnumerable<string> source)
    {
        if (source == null)
            return new List<string>();

        var copied = new List<string>();
        foreach (string flag in source)
            if (!string.IsNullOrEmpty(flag))
                copied.Add(flag);
        return copied;
    }

    public static PlayerSaveData LoadPlayer()
    {
        TryLoadPlayer(out var data);
        return data;
    }

    // A missing slot is a successful read with null data; corruption is a failure.
    public static bool TryLoadPlayer(out PlayerSaveData data)
    {
        data = null;
        try { data = ReadExisting(); LastError = null; return true; }
        catch (Exception e)
        {
            LastError = e.Message;
            Debug.LogWarning("[SaveSystem] 저장 복구 실패 (원본 보존): " + LastError);
            return false;
        }
    }

    static PlayerSaveData ReadExisting()
    {
        bool primary = File.Exists(SavePath), backup = File.Exists(SavePath + ".bak");
        if (!primary && !backup) return null;
        try { if (primary) return ReadValidated(SavePath); }
        catch (NotSupportedException) { throw; }
        catch (Exception e) { Debug.LogWarning("[SaveSystem] 주 저장 손상, 백업 확인: " + e.Message); }
        if (backup) return ReadValidated(SavePath + ".bak");
        throw new InvalidDataException("주 저장을 읽을 수 없고 복구 백업이 없습니다.");
    }

    static PlayerSaveData ReadValidated(string path)
    {
        var data = JsonUtility.FromJson<PlayerSaveData>(File.ReadAllText(path));
        Validate(data);
        return data;
    }

    static void Validate(PlayerSaveData data)
    {
        if (data != null && data.snapshotVersion > 3)
            throw new NotSupportedException("더 최신 버전의 저장입니다. 덮어쓰지 않습니다.");
        if (data == null || data.level < 1 || !Positive(data.maxHP) || !Positive(data.maxMana) ||
            !Positive(data.maxEXP) || !Finite(data.hp) || !Finite(data.mana) || !Finite(data.exp) ||
            data.snapshotVersion < 0 || data.snapshotVersion > 3 || data.revision < 0)
            throw new InvalidDataException("유효하지 않거나 지원하지 않는 저장 데이터");
        if (data.hasCheckpoint && (!Finite(data.checkpointPosition.x) || !Finite(data.checkpointPosition.y) || !Finite(data.checkpointPosition.z)))
            throw new InvalidDataException("체크포인트 좌표 오류");
        if (data.hasMood && (!Finite(data.mood) || data.mood < 0f || data.mood > 100f))
            throw new InvalidDataException("Mood 저장값 오류");
        if (data.snapshotVersion >= 1) QuestSaveSystem.Validate(data.quests);
        StoryChoiceManager.Validate(data.storyChoices);
        if (data.snapshotVersion >= 2) PlayerInventory.Validate(data.inventory);
        if (data.hasProgress && (!Finite(data.posX) || !Finite(data.posY)))
            throw new InvalidDataException("씬 진행 저장값 오류");
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool Positive(float value) => Finite(value) && value > 0f;

    // Explicit reset API only. Reset both halves together; remove backups to prevent resurrection.
    public static void DeleteSave()
    {
        foreach (string name in new[] { "player_save.json", "quest_save.json" })
            foreach (string suffix in new[] { "", ".bak", ".tmp" })
            {
                string path = Path.Combine(DirectoryPath, name + suffix);
                if (File.Exists(path)) File.Delete(path);
            }
    }
}

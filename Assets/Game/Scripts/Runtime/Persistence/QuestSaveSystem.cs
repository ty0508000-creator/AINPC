using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class QuestSaveEntry
{
    public string questId;
    public QuestState state;
    public int[] progress;
    public float elapsedSeconds;
}
[Serializable]
public class QuestSaveData
{
    public List<QuestSaveEntry> entries = new();
}

/// <summary>Quest serialization adapter. All new saves use SaveSystem's unified snapshot.</summary>
public static class QuestSaveSystem
{
    public static void Save(QuestManager manager)
    {
        if (manager == null || !manager.IsSaveReady) return;
        SaveSystem.SaveGame(manager.Player, manager);
    }

    public static QuestSaveData Capture(QuestManager manager, QuestSaveData previous = null)
    {
        var data = new QuestSaveData();
        // A scene may only contain one island's catalog. Preserve other islands.
        if (previous != null)
            foreach (var entry in previous.entries)
                if (manager.Get(entry.questId) == null)
                    data.entries.Add(new QuestSaveEntry {
                        questId = entry.questId, state = entry.state,
                        progress = (int[])entry.progress.Clone(), elapsedSeconds = entry.elapsedSeconds
                    });
        foreach (var r in manager.All())
        {
            if (r.State == QuestState.Locked) continue;
            data.entries.Add(new QuestSaveEntry {
                questId = r.Data.questId, state = r.State, progress = (int[])r.Progress.Clone(),
                elapsedSeconds = r.State == QuestState.Active ? Mathf.Max(0f, Time.time - r.StartedAt) : 0f
            });
        }
        return data;
    }

    public static QuestSaveData ReadLegacy()
    {
        string path = Path.Combine(SaveSystem.DirectoryPath, "quest_save.json");
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return new QuestSaveData();
        try { return Read(path); }
        catch
        {
            if (File.Exists(path + ".bak")) return Read(path + ".bak");
            throw; // Never silently replace damaged legacy progress with an empty snapshot.
        }
    }

    static QuestSaveData Read(string path)
    {
        string json = File.ReadAllText(path);
        if (!json.Contains("\"entries\"")) throw new InvalidDataException("퀘스트 entries 누락");
        var data = JsonUtility.FromJson<QuestSaveData>(json);
        Validate(data);
        return data;
    }

    public static void Validate(QuestSaveData data)
    {
        if (data?.entries == null) throw new InvalidDataException("퀘스트 스냅샷 누락");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in data.entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.questId) || !ids.Add(entry.questId) ||
                !Enum.IsDefined(typeof(QuestState), entry.state) || entry.progress == null ||
                float.IsNaN(entry.elapsedSeconds) || float.IsInfinity(entry.elapsedSeconds) || entry.elapsedSeconds < 0)
                throw new InvalidDataException("잘못된 퀘스트 저장 항목");
            foreach (int value in entry.progress)
                if (value < 0) throw new InvalidDataException("음수 퀘스트 진행도");
        }
    }

    public static bool Load(QuestManager manager)
    {
        if (manager == null) return false;
        try
        {
            if (!SaveSystem.TryLoadPlayer(out var saved)) return false;
            var data = saved?.snapshotVersion >= 1 ? saved.quests : ReadLegacy();
            Validate(data);
            foreach (var e in data.entries)
            {
                var r = manager.Get(e.questId);
                if (r == null) continue;
                r.State = e.state;
                r.StartedAt = Time.time - e.elapsedSeconds;
                for (int i = 0; i < Mathf.Min(e.progress.Length, r.Progress.Length); i++)
                    r.Progress[i] = Mathf.Clamp(e.progress[i], 0, r.Data.objectives[i].requiredCount);
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Quest] 불러오기 실패, 자동 저장 중단: " + e.Message);
            return false;
        }
    }

    // Story-only deletion would invalidate reward consistency.
    public static void DeleteSave() => SaveSystem.DeleteSave();
}

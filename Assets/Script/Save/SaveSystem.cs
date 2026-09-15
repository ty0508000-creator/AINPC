using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class SaveSystem
{
    private static string SavePath => Path.Combine(Application.persistentDataPath, "player_save.json");

    /// <summary>이어하기가 가능한 기록이 있는가.</summary>
    public static bool HasSave => File.Exists(SavePath);

    /// <summary>스탯만 저장한다. 이미 저장된 진행(씬/위치/플래그)은 건드리지 않는다.</summary>
    public static void SavePlayer(PlayerStats stats)
    {
        PlayerSaveData data = LoadPlayer() ?? new PlayerSaveData();
        WriteStats(data, stats);
        Write(data);
    }

    /// <summary>스탯과 함께 지금 씬·위치·진행 표시까지 저장한다.</summary>
    public static void SaveProgress(PlayerStats stats, string sceneName, Vector2 position, List<string> flags)
    {
        PlayerSaveData data = LoadPlayer() ?? new PlayerSaveData();
        WriteStats(data, stats);

        data.sceneName = sceneName;
        data.posX = position.x;
        data.posY = position.y;
        data.flags = flags ?? new List<string>();
        data.hasProgress = !string.IsNullOrEmpty(sceneName);

        Write(data);
    }

    private static void WriteStats(PlayerSaveData data, PlayerStats stats)
    {
        if (stats == null)
            return;

        data.level = stats.Level;
        data.hp = stats.HP;
        data.maxHP = stats.MaxHP;
        data.mana = stats.Mana;
        data.maxMana = stats.MaxMana;
        data.exp = stats.EXP;
        data.maxEXP = stats.MaxEXP;
    }

    private static void Write(PlayerSaveData data)
    {
        string json = JsonUtility.ToJson(data, prettyPrint: true);
        File.WriteAllText(SavePath, json);
        Debug.Log($"[SaveSystem] 저장 완료: {SavePath}");
    }

    public static PlayerSaveData LoadPlayer()
    {
        if (!File.Exists(SavePath))
        {
            Debug.Log("[SaveSystem] 저장 파일 없음, 기본값 사용");
            return null;
        }

        string json = File.ReadAllText(SavePath);
        var data = JsonUtility.FromJson<PlayerSaveData>(json);
        Debug.Log("[SaveSystem] 불러오기 완료");
        return data;
    }

    public static void DeleteSave()
    {
        if (File.Exists(SavePath))
            File.Delete(SavePath);
    }
}

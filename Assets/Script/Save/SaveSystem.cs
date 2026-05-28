using System.IO;
using UnityEngine;

public static class SaveSystem
{
    private static string SavePath => Path.Combine(Application.persistentDataPath, "player_save.json");

    public static void SavePlayer(PlayerStats stats)
    {
        var data = new PlayerSaveData
        {
            level  = stats.Level,
            hp     = stats.HP,
            maxHP  = stats.MaxHP,
            mana   = stats.Mana,
            maxMana = stats.MaxMana,
            exp    = stats.EXP,
            maxEXP = stats.MaxEXP
        };

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

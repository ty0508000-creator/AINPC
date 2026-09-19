using System.IO;
using UnityEngine;

public static class SaveSystem
{
    #if UNITY_EDITOR
    // Isolates editor verification from the real player's save slot.
    public static string VerificationDirectory;
    #endif
    private static string SavePath
    {
        get
        {
            #if UNITY_EDITOR
            if (!string.IsNullOrEmpty(VerificationDirectory))
                return Path.Combine(VerificationDirectory, "player_save.json");
            #endif
            return Path.Combine(Application.persistentDataPath, "player_save.json");
        }
    }

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
            maxEXP = stats.MaxEXP,
            progressionVersion = 1,
            statPoints = stats.StatPoints,
            skillPoints = stats.SkillPoints,
            attributeRanks = stats.AttributeRanks,
            skillRanks = stats.SkillRanks
        };

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        try
        {
            string temporary = SavePath + ".tmp";
            File.WriteAllText(temporary, json);
            if (File.Exists(SavePath)) File.Replace(temporary, SavePath, SavePath + ".bak");
            else File.Move(temporary, SavePath);
        }
        catch (System.Exception e) { Debug.LogWarning("[SaveSystem] 저장 실패: " + e.Message); }
    }

    public static PlayerSaveData LoadPlayer()
    {
        if (!File.Exists(SavePath))
        {
            Debug.Log("[SaveSystem] 저장 파일 없음, 기본값 사용");
            return null;
        }

        try { return ReadValidated(SavePath); }
        catch (System.Exception e)
        {
            Debug.LogWarning("[SaveSystem] 저장 파일 읽기 실패: " + e.Message);
            try { return ReadValidated(SavePath + ".bak"); }
            catch { return null; }
        }
    }

    private static PlayerSaveData ReadValidated(string path)
    {
        var data = JsonUtility.FromJson<PlayerSaveData>(File.ReadAllText(path));
        if (data == null || data.level < 1 || !Positive(data.maxHP) || !Positive(data.maxMana) ||
            !Positive(data.maxEXP) || !Finite(data.hp) || !Finite(data.mana) || !Finite(data.exp))
            throw new InvalidDataException("유효하지 않은 플레이어 저장 데이터");
        return data;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Positive(float value) => Finite(value) && value > 0f;

    public static void DeleteSave()
    {
        if (File.Exists(SavePath))
            File.Delete(SavePath);
    }
}

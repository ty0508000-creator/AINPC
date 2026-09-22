[System.Serializable]
public class InventorySaveEntry
{
    public string itemId;
    public string displayName;
    public int quantity;
}

[System.Serializable]
public class PlayerSaveData
{
    // 씬 전환 진행은 기존 통합 저장에 함께 기록한다.
    public bool hasProgress;
    public string sceneName;
    public float posX, posY;
    public System.Collections.Generic.List<string> flags;
    public int level;
    public float hp;
    public float maxHP;
    public float mana;
    public float maxMana;
    public float exp;
    public float maxEXP;
    public int progressionVersion;
    public int statPoints;
    public int skillPoints;
    public int[] attributeRanks;
    public int[] skillRanks;
    // Version 1: player + quest progress are committed in the same atomic file.
    public int snapshotVersion;
    public long revision;
    public QuestSaveData quests;
    public bool hasCheckpoint;
    public string checkpointScene;
    public UnityEngine.Vector3 checkpointPosition;
    public bool hasMood;
    public float mood;
    // Optional in snapshot version 1 for backward compatibility with existing saves.
    public StoryChoiceSaveData storyChoices;
    // Version 2: inventory is committed with player and quest progress.
    public InventorySaveEntry[] inventory;
}

[System.Serializable]
public class InventorySaveEntry
{
    public string itemId;
    public string displayName;
    public int quantity;
    // 가방 칸 번호. 이 필드가 생기기 전 저장은 0 으로 읽히며 PlayerInventory.Load 가 겹침을 풀어 준다.
    public int slotIndex;
    // 장비 개체의 등급(ItemRarity)과 장신구 옵션. 이 필드가 생기기 전 저장은 Normal·옵션 없음으로 읽힌다.
    public int rarity;
    public ItemOption[] options;
}

/// <summary>NPC 한 명의 플레이어에 대한 호감도. 플레이어의 감정 수치(Mood)와 별개다.</summary>
[System.Serializable]
public class NpcAffinityEntry
{
    public string npcName;
    public float value;
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
    // 예전 감정 수치 하나. 이제 쓰지 않고, 이 값만 있는 옛 저장은 감정 시작값에서 출발한다.
    public bool hasMood;
    public float mood;
    // 어둠의 감정: 분노·두려움·신뢰·냉정·슬픔(EmotionSystem.Capture). 복수심은 보스 플래그에서 계산하므로 없다.
    public bool hasEmotions;
    public float[] emotions;
    // Optional in snapshot version 1 for backward compatibility with existing saves.
    public StoryChoiceSaveData storyChoices;
    // Version 2: inventory is committed with player and quest progress.
    public InventorySaveEntry[] inventory;
    // 장착 칸(무기·갑옷·장신구). null 이면 장비 기능 이전 저장이라 시작 장비를 채운다. 빈 칸은 itemId 가 비어 있다.
    public InventorySaveEntry[] equipped;
    // NPC 호감도. null 이면 이 필드 이전 저장이라 모든 NPC 가 시작값에서 출발한다.
    public System.Collections.Generic.List<NpcAffinityEntry> npcAffinity;
}

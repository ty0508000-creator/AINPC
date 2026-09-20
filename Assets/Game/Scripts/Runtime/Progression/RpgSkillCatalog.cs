using UnityEngine;

public enum RpgAttribute { Vitality, Strength, Defense, Spirit }

public sealed class RpgSkillDefinition
{
    public readonly string Name, Description, Branch;
    public readonly int RequiredLevel, Prerequisite, MaxRank;
    public readonly float ManaCost, Cooldown;
    public readonly bool Active;

    public RpgSkillDefinition(string name, string description, string branch, int level,
        int prerequisite, int maxRank, bool active = false, float mana = 0, float cooldown = 0)
    {
        Name = name; Description = description; Branch = branch; RequiredLevel = level;
        Prerequisite = prerequisite; MaxRank = maxRank; Active = active;
        ManaCost = mana; Cooldown = cooldown;
    }
}

// Stable indices are part of the save format. Append new skills rather than reordering them.
public static class RpgSkillCatalog
{
    public static readonly RpgSkillDefinition[] All =
    {
        new RpgSkillDefinition("월영참", "전방의 적들을 베어 공격력의 180% 피해. 단계마다 +40%.", "검술", 1, -1, 3, true, 15, 5),
        new RpgSkillDefinition("검의 이치", "공격력 +3 / 단계", "검술", 3, 0, 3),
        new RpgSkillDefinition("검성의 경지", "공격력 +6 / 단계. 월영참 범위 증가.", "검술", 5, 1, 2),
        new RpgSkillDefinition("금강호신", "6초 동안 받는 피해 감소. 단계마다 방어 효과 증가.", "호신", 1, -1, 3, true, 12, 12),
        new RpgSkillDefinition("철골", "방어력 +3 / 단계", "호신", 3, 3, 3),
        new RpgSkillDefinition("불굴", "방어력 +6 / 단계", "호신", 5, 4, 2),
        new RpgSkillDefinition("운기조식", "최대 체력의 20% 회복. 단계마다 +8%.", "내공", 1, -1, 3, true, 20, 10),
        new RpgSkillDefinition("기맥 순환", "초당 마나 회복 +0.6 / 단계", "내공", 3, 6, 3),
        new RpgSkillDefinition("삼화취정", "초당 마나 회복 +1.2 / 단계. 운기조식 회복량 증가.", "내공", 5, 7, 2)
    };
    public static readonly int[] Hotbar = { 0, 3, 6 };
    public static int Rank(int[] ranks, int id) => ranks != null && id >= 0 && id < ranks.Length ? ranks[id] : 0;
}

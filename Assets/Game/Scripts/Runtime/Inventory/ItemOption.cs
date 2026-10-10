using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>장비가 올려 주는 능력치 종류. 퍼센트 능력치는 비율로 저장한다(3% = 0.03).</summary>
public enum ItemStat
{
    Attack,
    CritChance,
    CritDamage,
    AttackSpeed,
    MaxHp,
    Defense,
    Evasion,
    HpRegen,
    MaxMana,
    ManaRegen,
    MoveSpeed,
    ExpGain,
    LifeSteal,
    KillHeal,
    BossDamage,
    DropRate,
}

/// <summary>장신구에 붙은 옵션 한 줄.</summary>
[Serializable]
public struct ItemOption
{
    public ItemStat stat;
    public float value;
}

/// <summary>장신구 옵션 범위 표와 무작위 생성.</summary>
public static class ItemOptionTable
{
    struct Range
    {
        public float min1, max1, min5, max5;
        public Range(float min1, float max1, float min5, float max5) { this.min1 = min1; this.max1 = max1; this.min5 = min5; this.max5 = max5; }
    }

    // ItemStat 순서와 같다. 1단계 범위, 5단계 범위.
    static readonly Range[] Ranges =
    {
        new Range(1, 2, 8, 14),             // Attack
        new Range(.02f, .04f, .06f, .10f),  // CritChance
        new Range(.10f, .20f, .30f, .50f),  // CritDamage
        new Range(.02f, .04f, .06f, .10f),  // AttackSpeed
        new Range(10, 20, 80, 140),         // MaxHp
        new Range(1, 2, 6, 10),             // Defense
        new Range(.01f, .02f, .03f, .05f),  // Evasion
        new Range(.3f, .6f, 2.5f, 4f),      // HpRegen
        new Range(5, 10, 30, 50),           // MaxMana
        new Range(.2f, .4f, 1.2f, 2f),      // ManaRegen
        new Range(.02f, .04f, .06f, .10f),  // MoveSpeed
        new Range(.03f, .06f, .10f, .20f),  // ExpGain
        new Range(.01f, .02f, .03f, .05f),  // LifeSteal
        new Range(2, 4, 15, 25),            // KillHeal
        new Range(.03f, .06f, .12f, .20f),  // BossDamage
        new Range(.05f, .10f, .20f, .40f),  // DropRate
    };

    static readonly string[] Labels =
    {
        "공격력", "치명타 확률", "치명타 피해", "공격속도", "최대 체력", "방어력", "회피 확률", "체력 재생",
        "최대 내력", "내력 재생", "이동속도", "획득 경험치", "흡혈", "처치 시 체력 회복", "보스 피해", "아이템 드롭률",
    };

    public static string Label(ItemStat stat) => Labels[(int)stat];

    public static bool IsInteger(ItemStat stat) =>
        stat == ItemStat.Attack || stat == ItemStat.MaxHp || stat == ItemStat.Defense || stat == ItemStat.KillHeal || stat == ItemStat.MaxMana;

    static bool IsRegen(ItemStat stat) => stat == ItemStat.HpRegen || stat == ItemStat.ManaRegen;

    /// <summary>등급에 맞는 줄 수만큼 서로 다른 옵션을 뽑는다. 값 = 단계 범위 안 균등 × 등급 배율.</summary>
    public static List<ItemOption> Roll(int tier, ItemRarity rarity, System.Random rng)
    {
        int lines = ItemRarityTable.RollOptionLines(rarity, rng);
        var pool = new List<ItemStat>((ItemStat[])Enum.GetValues(typeof(ItemStat)));
        var result = new List<ItemOption>(lines);
        float t = (Mathf.Clamp(tier, 1, 5) - 1) / 4f;
        float multiplier = ItemRarityTable.Multiplier(rarity);
        for (int i = 0; i < lines; i++)
        {
            int pick = rng.Next(pool.Count);
            var stat = pool[pick];
            pool.RemoveAt(pick);
            var range = Ranges[(int)stat];
            float value = Mathf.Lerp(Mathf.Lerp(range.min1, range.min5, t), Mathf.Lerp(range.max1, range.max5, t), (float)rng.NextDouble()) * multiplier;
            if (IsInteger(stat)) value = Mathf.Round(value);
            result.Add(new ItemOption { stat = stat, value = value });
        }
        return result;
    }

    /// <summary>툴팁에 쓰는 한 줄. 예: "치명타 확률 +7.5%", "공격력 +12", "체력 재생 +1.2/초".</summary>
    public static string Format(ItemOption option) => Label(option.stat) + " " + FormatValue(option.stat, option.value);

    public static string FormatValue(ItemStat stat, float value)
    {
        string sign = value >= 0 ? "+" : "-";
        value = Mathf.Abs(value);
        if (IsInteger(stat)) return sign + Mathf.RoundToInt(value);
        if (IsRegen(stat)) return sign + value.ToString("0.#") + "/초";
        return sign + (value * 100f).ToString("0.#") + "%";
    }
}

using UnityEngine;

/// <summary>아이템 등급. 같은 아이템이 여러 등급으로 떨어지며, 등급은 능력치 배율과 이름 색을 정한다.</summary>
public enum ItemRarity
{
    Normal,
    Rare,
    Epic,
    Unique,
    Legendary,
}

/// <summary>등급별 배율·색·드롭 확률·장신구 옵션 줄 수 표.</summary>
public static class ItemRarityTable
{
    static readonly float[] Multipliers = { 1f, 1.15f, 1.3f, 1.5f, 1.75f };
    static readonly Color[] Colors = { Hex(0x9D9D9D), Hex(0x3B82F6), Hex(0xA855F7), Hex(0xFACC15), Hex(0x22C55E) };
    static readonly int[] HuntingWeights = { 65, 20, 10, 4, 1 };
    static readonly int[] BossWeights = { 0, 30, 40, 20, 10 };
    // [등급][줄 수 0~4] 가중치
    static readonly int[][] LineWeights =
    {
        new[] { 0, 100, 0, 0, 0 },
        new[] { 0, 60, 40, 0, 0 },
        new[] { 0, 20, 60, 20, 0 },
        new[] { 0, 0, 40, 50, 10 },
        new[] { 0, 0, 10, 50, 40 },
    };

    public static float Multiplier(ItemRarity rarity) => Multipliers[(int)rarity];
    public static Color Color(ItemRarity rarity) => Colors[(int)rarity];
    public static string Label(ItemRarity rarity) => rarity.ToString();

    public static ItemRarity RollHunting(System.Random rng) => (ItemRarity)Pick(HuntingWeights, rng);
    public static ItemRarity RollBoss(System.Random rng) => (ItemRarity)Pick(BossWeights, rng);
    public static int RollOptionLines(ItemRarity rarity, System.Random rng) => Pick(LineWeights[(int)rarity], rng);

    /// <summary>가중치 배열에서 인덱스 하나를 뽑는다.</summary>
    public static int Pick(int[] weights, System.Random rng)
    {
        int total = 0;
        foreach (int w in weights) total += w;
        int roll = rng.Next(total);
        for (int i = 0; i < weights.Length; i++)
        {
            if (roll < weights[i]) return i;
            roll -= weights[i];
        }
        return weights.Length - 1;
    }

    static Color Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
}

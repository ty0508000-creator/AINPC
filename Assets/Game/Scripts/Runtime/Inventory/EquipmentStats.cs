using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>장착한 장비가 올려 주는 능력치 합계. 상한이 있는 능력치는 잘린 값을 따로 준다.</summary>
public sealed class EquipmentStats
{
    public const float CritChanceCap = 0.60f, EvasionCap = 0.15f, AttackSpeedCap = 2f, BaseCritDamage = 1.5f;

    readonly float[] values = new float[Enum.GetValues(typeof(ItemStat)).Length];

    /// <summary>상한을 적용하지 않은 합계. 공격속도는 1 을 넘는 부분만 담긴다.</summary>
    public float this[ItemStat stat] => values[(int)stat];

    public float CritChance => Mathf.Min(CritChanceCap, values[(int)ItemStat.CritChance]);
    public float Evasion => Mathf.Min(EvasionCap, values[(int)ItemStat.Evasion]);
    public float AttackSpeed => Mathf.Min(AttackSpeedCap, 1f + values[(int)ItemStat.AttackSpeed]);

    public void Clear() => Array.Clear(values, 0, values.Length);

    public void AddItem(ItemDefinition definition, ItemRarity rarity, IReadOnlyList<ItemOption> options)
    {
        if (definition != null)
        {
            float m = ItemRarityTable.Multiplier(rarity);
            if (definition.category == ItemCategory.Weapon)
            {
                values[(int)ItemStat.Attack] += Mathf.Round(definition.attack * m);
                values[(int)ItemStat.CritChance] += definition.critChance * m;
                values[(int)ItemStat.AttackSpeed] += (definition.attackSpeed - 1f) * m;
            }
            else if (definition.category == ItemCategory.Armor)
            {
                values[(int)ItemStat.MaxHp] += Mathf.Round(definition.maxHp * m);
                values[(int)ItemStat.Defense] += Mathf.Round(definition.defense * m);
                values[(int)ItemStat.Evasion] += definition.evasion * m;
            }
        }
        if (options == null) return;
        foreach (var option in options) values[(int)option.stat] += option.value;
    }
}

using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>몬스터 장비 드롭. 사냥터 몬스터는 1% 확률, 보스는 반드시 하나를 떨어뜨린다.</summary>
public static class ItemDrop
{
    public const float HuntingChance = 0.01f;
    static readonly ItemCategory[] EquipmentCategories = { ItemCategory.Weapon, ItemCategory.Armor, ItemCategory.Accessory };

    /// <summary>게임에서 쓰는 난수. 검증은 시드를 정한 난수를 따로 넘긴다.</summary>
    public static readonly System.Random Rng = new System.Random();

    /// <summary>
    /// 드롭할 장비 개체를 굴린다. 떨어지지 않았거나 그 단계 장비가 없으면 null.
    /// 부위는 그 단계에 정의가 있는 부위 중 균등하게 고른다.
    /// </summary>
    public static InventoryEntry Roll(int tier, bool boss, float dropRateBonus, ItemDatabase database, System.Random rng)
    {
        if (tier <= 0 || database == null) return null;
        if (!boss && rng.NextDouble() >= HuntingChance * (1f + dropRateBonus)) return null;
        var choices = new List<ItemDefinition>();
        foreach (var category in EquipmentCategories)
        {
            var definition = database.items.Find(item => item != null && item.tier == tier && item.category == category);
            if (definition != null) choices.Add(definition);
        }
        if (choices.Count == 0) return null;
        var picked = choices[rng.Next(choices.Count)];
        var rarity = boss ? ItemRarityTable.RollBoss(rng) : ItemRarityTable.RollHunting(rng);
        return PlayerInventory.CreateInstance(picked, rarity, rng);
    }

    /// <summary>바닥에 장비를 떨어뜨린다. 아이콘과 등급색 이름표가 붙고, 밟으면 줍는다.</summary>
    public static ItemPickup Spawn(InventoryEntry entry, Vector3 position)
    {
        var definition = ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(entry.itemId) : null;
        var go = new GameObject("Drop_" + entry.itemId);
        go.transform.position = position;
        var sprite = go.AddComponent<SpriteRenderer>();
        sprite.sprite = definition != null ? definition.icon : null;
        go.transform.localScale = Vector3.one * 0.6f;
        var trigger = go.AddComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = 0.4f / 0.6f;
        YSortRenderer.Attach(go);

        var labelObject = new GameObject("Label");
        labelObject.transform.SetParent(go.transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 0.75f, 0f);
        var label = labelObject.AddComponent<TextMeshPro>();
        var font = Resources.Load<TMP_FontAsset>("Fonts/UiBody SDF");
        if (font != null) label.font = font;
        label.text = entry.displayName;
        label.fontSize = 2.4f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = ItemRarityTable.Color(entry.rarity);
        label.sortingOrder = YSortRenderer.WorldOverlayOrder;
        label.rectTransform.sizeDelta = new Vector2(6f, 1f);

        var pickup = go.AddComponent<ItemPickup>();
        pickup.Init(entry);
        return pickup;
    }
}

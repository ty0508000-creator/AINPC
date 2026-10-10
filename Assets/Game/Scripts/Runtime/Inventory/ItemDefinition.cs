using UnityEngine;

/// <summary>아이템 종류. 장비 종류는 장비창의 칸(무기/갑옷/장신구)과 일대일로 대응한다.</summary>
public enum ItemCategory
{
    Misc,
    Weapon,
    Armor,
    Accessory,
}

[CreateAssetMenu(fileName = "Item", menuName = "AINPC/Item")]
public sealed class ItemDefinition : ScriptableObject
{
    public string itemId;
    public string displayName = "새 아이템";
    public Sprite icon;
    [Min(1)] public int maxStack = 99;
    public ItemCategory category = ItemCategory.Misc;
    [TextArea(2, 5)] public string description;

    /// <summary>툴팁 등에 보여 줄 종류 이름.</summary>
    public static string CategoryLabel(ItemCategory category) => category switch
    {
        ItemCategory.Weapon => "무기",
        ItemCategory.Armor => "갑옷",
        ItemCategory.Accessory => "장신구",
        _ => "기타",
    };

    void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(itemId)) itemId = name;
        maxStack = Mathf.Max(1, maxStack);
    }
}

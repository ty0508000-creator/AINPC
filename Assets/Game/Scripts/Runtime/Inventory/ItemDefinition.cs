using UnityEngine;

[CreateAssetMenu(fileName = "Item", menuName = "AINPC/Item")]
public sealed class ItemDefinition : ScriptableObject
{
    public string itemId;
    public string displayName = "새 아이템";
    public Sprite icon;
    [Min(1)] public int maxStack = 99;

    void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(itemId)) itemId = name;
        maxStack = Mathf.Max(1, maxStack);
    }
}

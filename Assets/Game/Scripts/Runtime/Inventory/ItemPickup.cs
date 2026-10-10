using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class ItemPickup : MonoBehaviour
{
    [SerializeField] ItemDefinition item;
    [SerializeField, Min(1)] int quantity = 1;

    [Tooltip("주웠을 때 세울 진행 표시. 비우면 세우지 않는다")]
    [SerializeField] string flagOnPickup;

    // 몬스터가 떨어뜨린 장비 개체(등급·옵션 포함). 있으면 item 대신 이것을 준다.
    InventoryEntry instance;

    void Reset() => GetComponent<Collider2D>().isTrigger = true;

    public void Init(InventoryEntry entry) => instance = entry;

    void OnTriggerEnter2D(Collider2D other)
    {
        var inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null) return;

        if (instance != null)
        {
            // 가방이 가득 차면 바닥에 남는다.
            if (!inventory.TryAddInstance(instance)) return;
            EquipIfSlotEmpty(inventory, instance);
            Remove();
            return;
        }

        if (item == null) return;
        bool added = inventory.TryAdd(item, quantity);

        // 이미 가지고 있어서 더 못 담아도 진행 표시는 세운다. 안 그러면 출구가 영영 안 열린다.
        if (!string.IsNullOrEmpty(flagOnPickup) && (added || inventory.Count(item.itemId) > 0))
            GameFlow.Instance.SetFlag(flagOnPickup);

        if (!added) return;
        if (item.maxStack == 1)
        {
            var entry = FindNewest(inventory, item.itemId);
            if (entry != null) EquipIfSlotEmpty(inventory, entry);
        }
        Remove();
    }

    /// <summary>같은 부위 장착 칸이 비어 있으면 주운 장비를 바로 낀다.</summary>
    static void EquipIfSlotEmpty(PlayerInventory inventory, InventoryEntry entry)
    {
        var definition = ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(entry.itemId) : null;
        if (definition == null || definition.category == ItemCategory.Misc) return;
        if (inventory.GetEquipped(definition.category) == null && entry.slotIndex >= 0) inventory.Equip(entry.slotIndex);
    }

    static InventoryEntry FindNewest(PlayerInventory inventory, string itemId)
    {
        for (int i = inventory.Items.Count - 1; i >= 0; i--)
            if (inventory.Items[i].itemId == itemId) return inventory.Items[i];
        return null;
    }

    void Remove()
    {
        // 편집 모드 검증에서도 지울 수 있게 한다.
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }
}

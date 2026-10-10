using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class InventoryEntry
{
    public string itemId;
    public string displayName;
    public int quantity;
    /// <summary>가방에서 놓인 칸 번호(0 부터). 칸이 모자라 자리를 못 받은 항목은 -1.</summary>
    public int slotIndex;
}

[DisallowMultipleComponent]
public sealed class PlayerInventory : MonoBehaviour
{
    /// <summary>가방 칸 수. 인벤토리 창의 5열 × 3행과 같다.</summary>
    public const int SlotCount = 15;

    readonly List<InventoryEntry> items = new();
    public IReadOnlyList<InventoryEntry> Items => items;

    /// <summary>아이템이 늘거나 줄거나 칸을 옮길 때마다 알린다. UI 갱신용.</summary>
    public event Action Changed;

    void Awake() => Load(SaveSystem.LoadPlayer());

    public int Count(string itemId)
    {
        var entry = Find(itemId);
        return entry != null ? entry.quantity : 0;
    }

    /// <summary>해당 칸에 놓인 아이템. 비어 있으면 null.</summary>
    public InventoryEntry GetItemInSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return null;
        return items.Find(item => item.slotIndex == slotIndex);
    }

    /// <summary>칸을 차지한 아이템 종류 수.</summary>
    public int UsedSlotCount => items.FindAll(item => item.slotIndex >= 0).Count;

    public bool TryAdd(ItemDefinition item, int quantity = 1)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.itemId) || quantity <= 0) return false;
        var entry = Find(item.itemId);
        int current = entry != null ? entry.quantity : 0;
        if (quantity > item.maxStack - current) return false;
        if (entry == null)
        {
            // 새 종류는 빈 칸이 있어야 들어간다. 가방이 가득 차면 줍지 않는다.
            int freeSlot = FirstFreeSlot();
            if (freeSlot < 0) return false;
            entry = new InventoryEntry { itemId = item.itemId, slotIndex = freeSlot };
            items.Add(entry);
        }
        entry.displayName = string.IsNullOrWhiteSpace(item.displayName) ? item.itemId : item.displayName;
        entry.quantity = current + quantity;
        Save();
        Changed?.Invoke();
        return true;
    }

    public bool Remove(string itemId, int quantity = 1)
    {
        var entry = Find(itemId);
        if (entry == null || quantity <= 0 || entry.quantity < quantity) return false;
        entry.quantity -= quantity;
        if (entry.quantity == 0) items.Remove(entry);
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// 아이템을 다른 칸으로 옮긴다. 도착 칸에 아이템이 있으면 서로 자리를 바꾼다.
    /// 출발 칸이 비었거나 칸 번호가 범위를 벗어나면 아무것도 하지 않고 false.
    /// </summary>
    public bool MoveItem(int fromSlot, int toSlot)
    {
        if (fromSlot == toSlot) return false;
        var moving = GetItemInSlot(fromSlot);
        if (moving == null || toSlot < 0 || toSlot >= SlotCount) return false;
        var occupant = GetItemInSlot(toSlot);
        if (occupant != null) occupant.slotIndex = fromSlot;
        moving.slotIndex = toSlot;
        Save();
        Changed?.Invoke();
        return true;
    }

    // ponytail: a linear list is enough for a small inventory; index by id if item types grow large.
    InventoryEntry Find(string itemId) => items.Find(item => string.Equals(item.itemId, itemId, StringComparison.Ordinal));

    int FirstFreeSlot()
    {
        for (int slot = 0; slot < SlotCount; slot++)
            if (GetItemInSlot(slot) == null) return slot;
        return -1;
    }

    void Save()
    {
        var stats = GetComponent<PlayerStats>();
        if (stats != null) stats.Save();
    }

    public InventorySaveEntry[] Capture()
    {
        var result = new InventorySaveEntry[items.Count];
        for (int i = 0; i < items.Count; i++)
            result[i] = new InventorySaveEntry {
                itemId = items[i].itemId, displayName = items[i].displayName, quantity = items[i].quantity,
                slotIndex = items[i].slotIndex
            };
        return result;
    }

    public void Load(PlayerSaveData data)
    {
        items.Clear();
        if (data?.inventory != null)
            foreach (var saved in data.inventory)
            {
                var entry = new InventoryEntry {
                    itemId = saved.itemId,
                    displayName = string.IsNullOrWhiteSpace(saved.displayName) ? saved.itemId : saved.displayName,
                    quantity = saved.quantity
                };
                // 칸 번호가 생기기 전 저장은 모두 0 으로 읽힌다. 먼저 차지한 항목이 칸을 갖고,
                // 겹치거나 범위를 벗어난 항목은 앞쪽 빈칸으로 옮긴다. 빈칸이 없으면 -1 로 보관만 한다.
                bool claimable = saved.slotIndex >= 0 && saved.slotIndex < SlotCount && GetItemInSlot(saved.slotIndex) == null;
                entry.slotIndex = claimable ? saved.slotIndex : FirstFreeSlot();
                items.Add(entry);
            }
        Changed?.Invoke();
    }

    public static void Validate(InventorySaveEntry[] inventory)
    {
        if (inventory == null) throw new InvalidDataException("인벤토리 스냅샷 누락");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in inventory)
            if (item == null || string.IsNullOrWhiteSpace(item.itemId) || !ids.Add(item.itemId) || item.quantity <= 0 ||
                item.slotIndex < -1 || item.slotIndex >= SlotCount)
                throw new InvalidDataException("잘못된 인벤토리 저장 항목");
    }
}

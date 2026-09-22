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
}

[DisallowMultipleComponent]
public sealed class PlayerInventory : MonoBehaviour
{
    readonly List<InventoryEntry> items = new();
    public IReadOnlyList<InventoryEntry> Items => items;

    void Awake() => Load(SaveSystem.LoadPlayer());

    public int Count(string itemId)
    {
        var entry = Find(itemId);
        return entry != null ? entry.quantity : 0;
    }

    public bool TryAdd(ItemDefinition item, int quantity = 1)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.itemId) || quantity <= 0) return false;
        var entry = Find(item.itemId);
        int current = entry != null ? entry.quantity : 0;
        if (quantity > item.maxStack - current) return false;
        if (entry == null)
        {
            entry = new InventoryEntry { itemId = item.itemId };
            items.Add(entry);
        }
        entry.displayName = string.IsNullOrWhiteSpace(item.displayName) ? item.itemId : item.displayName;
        entry.quantity = current + quantity;
        Save();
        return true;
    }

    public bool Remove(string itemId, int quantity = 1)
    {
        var entry = Find(itemId);
        if (entry == null || quantity <= 0 || entry.quantity < quantity) return false;
        entry.quantity -= quantity;
        if (entry.quantity == 0) items.Remove(entry);
        Save();
        return true;
    }

    // ponytail: a linear list is enough for a small inventory; index by id if item types grow large.
    InventoryEntry Find(string itemId) => items.Find(item => string.Equals(item.itemId, itemId, StringComparison.Ordinal));

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
                itemId = items[i].itemId, displayName = items[i].displayName, quantity = items[i].quantity
            };
        return result;
    }

    public void Load(PlayerSaveData data)
    {
        items.Clear();
        if (data?.inventory == null) return;
        foreach (var saved in data.inventory)
            items.Add(new InventoryEntry {
                itemId = saved.itemId,
                displayName = string.IsNullOrWhiteSpace(saved.displayName) ? saved.itemId : saved.displayName,
                quantity = saved.quantity
            });
    }

    public static void Validate(InventorySaveEntry[] inventory)
    {
        if (inventory == null) throw new InvalidDataException("인벤토리 스냅샷 누락");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in inventory)
            if (item == null || string.IsNullOrWhiteSpace(item.itemId) || !ids.Add(item.itemId) || item.quantity <= 0)
                throw new InvalidDataException("잘못된 인벤토리 저장 항목");
    }
}

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
    /// <summary>가방에서 놓인 칸 번호(0 부터). 칸이 모자라 자리를 못 받은 항목과 장착 중인 장비는 -1.</summary>
    public int slotIndex;
    /// <summary>장비 개체의 등급. 겹치는 아이템은 Normal.</summary>
    public ItemRarity rarity;
    /// <summary>장신구에 붙은 무작위 옵션.</summary>
    public List<ItemOption> options = new();
}

[DisallowMultipleComponent]
public sealed class PlayerInventory : MonoBehaviour
{
    /// <summary>가방 칸 수. 인벤토리 창의 5열 × 3행과 같다.</summary>
    public const int SlotCount = 15;

    /// <summary>장착 칸 수. 칸 번호 = <see cref="ItemCategory"/> - 1 (무기·갑옷·장신구).</summary>
    public const int EquipmentSlotCount = 3;

    // 새 게임에서 장착한 채 시작하는 장비와, 옛 저장에서 가방에 있으면 장착해 주는 무기.
    const string StarterArmorId = "cloth_armor", StarterAccessoryId = "old_talisman", StarterWeaponId = "cave_sword";

    readonly List<InventoryEntry> items = new();
    readonly InventoryEntry[] equipped = new InventoryEntry[EquipmentSlotCount];
    public IReadOnlyList<InventoryEntry> Items => items;

    /// <summary>장착한 장비의 능력치 합계. 장착이 바뀔 때마다 다시 계산한다.</summary>
    public EquipmentStats Stats { get; } = new EquipmentStats();

    /// <summary>아이템이 늘거나 줄거나 칸을 옮길 때마다 알린다. UI 갱신용.</summary>
    public event Action Changed;

    /// <summary>장착 칸이 바뀔 때 알린다. 능력치 갱신용.</summary>
    public event Action EquipmentChanged;

    bool loaded;

    void Awake() => EnsureLoaded();

    /// <summary>저장에서 한 번만 읽는다. PlayerStats 가 체력을 자르기 전에 먼저 부른다.</summary>
    public void EnsureLoaded()
    {
        if (!loaded) Load(SaveSystem.LoadPlayer());
    }

    /// <summary>가방과 장착 칸을 합친 보유 수.</summary>
    public int Count(string itemId)
    {
        int count = 0;
        foreach (var item in items) if (SameId(item, itemId)) count += item.quantity;
        foreach (var item in equipped) if (item != null && SameId(item, itemId)) count += item.quantity;
        return count;
    }

    /// <summary>해당 칸에 놓인 아이템. 비어 있으면 null.</summary>
    public InventoryEntry GetItemInSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return null;
        return items.Find(item => item.slotIndex == slotIndex);
    }

    /// <summary>칸을 차지한 아이템 종류 수.</summary>
    public int UsedSlotCount => items.FindAll(item => item.slotIndex >= 0).Count;

    /// <summary>해당 부위에 장착한 장비. 비어 있거나 장비 부위가 아니면 null.</summary>
    public InventoryEntry GetEquipped(ItemCategory category)
    {
        int index = EquipIndex(category);
        return index >= 0 ? equipped[index] : null;
    }

    /// <summary>
    /// 정의로 아이템을 넣는다. 겹치는 아이템은 기존 칸에 더하고, 겹침 1 장비는 Normal 개체로 넣는다.
    /// 장비도 이미 가지고 있으면(장착 포함) 최대 겹침을 넘으므로 넣지 않는다. 몬스터 드롭은 <see cref="TryAddInstance"/> 를 쓴다.
    /// </summary>
    public bool TryAdd(ItemDefinition item, int quantity = 1)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.itemId) || quantity <= 0) return false;
        int current = Count(item.itemId);
        if (quantity > item.maxStack - current) return false;
        if (item.maxStack == 1) return TryAddInstance(CreateInstance(item, ItemRarity.Normal, new System.Random()));
        var entry = Find(item.itemId);
        if (entry == null)
        {
            // 새 종류는 빈 칸이 있어야 들어간다. 가방이 가득 차면 줍지 않는다.
            int freeSlot = FirstFreeSlot();
            if (freeSlot < 0) return false;
            entry = new InventoryEntry { itemId = item.itemId, slotIndex = freeSlot };
            items.Add(entry);
        }
        entry.displayName = string.IsNullOrWhiteSpace(item.displayName) ? item.itemId : item.displayName;
        entry.quantity += quantity;
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>장비 개체 하나를 빈 칸에 넣는다. 같은 아이템이 있어도 항상 새 칸을 차지한다. 빈칸이 없으면 false.</summary>
    public bool TryAddInstance(InventoryEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.itemId)) return false;
        int freeSlot = FirstFreeSlot();
        if (freeSlot < 0) return false;
        entry.slotIndex = freeSlot;
        entry.quantity = 1;
        items.Add(entry);
        Save();
        Changed?.Invoke();
        return true;
    }

    /// <summary>등급을 정해 장비 개체를 만든다. 장신구면 단계·등급에 맞는 옵션을 굴린다.</summary>
    public static InventoryEntry CreateInstance(ItemDefinition definition, ItemRarity rarity, System.Random rng)
    {
        var entry = new InventoryEntry {
            itemId = definition.itemId,
            displayName = string.IsNullOrWhiteSpace(definition.displayName) ? definition.itemId : definition.displayName,
            quantity = 1, slotIndex = -1, rarity = rarity,
        };
        if (definition.category == ItemCategory.Accessory) entry.options = ItemOptionTable.Roll(definition.tier, rarity, rng);
        return entry;
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

    /// <summary>가방 칸의 장비를 장착한다. 같은 부위에 끼던 장비는 그 칸으로 돌아온다. 장비가 아니면 false.</summary>
    public bool Equip(int bagSlot)
    {
        var entry = GetItemInSlot(bagSlot);
        int index = entry != null ? EquipIndex(DefinitionOf(entry)?.category ?? ItemCategory.Misc) : -1;
        if (index < 0) return false;
        items.Remove(entry);
        var previous = equipped[index];
        if (previous != null) { previous.slotIndex = bagSlot; items.Add(previous); }
        entry.slotIndex = -1;
        equipped[index] = entry;
        OnEquipmentChanged();
        return true;
    }

    /// <summary>장착한 장비를 가방 빈칸으로 돌려놓는다. 가방이 가득 차면 벗지 않고 false.</summary>
    public bool Unequip(ItemCategory category)
    {
        int index = EquipIndex(category);
        int freeSlot = FirstFreeSlot();
        if (index < 0 || equipped[index] == null || freeSlot < 0) return false;
        equipped[index].slotIndex = freeSlot;
        items.Add(equipped[index]);
        equipped[index] = null;
        OnEquipmentChanged();
        return true;
    }

    void OnEquipmentChanged()
    {
        RecalculateStats();
        Save();
        EquipmentChanged?.Invoke();
        Changed?.Invoke();
    }

    void RecalculateStats()
    {
        Stats.Clear();
        foreach (var entry in equipped)
            if (entry != null) Stats.AddItem(DefinitionOf(entry), entry.rarity, entry.options);
    }

    static ItemDefinition DefinitionOf(InventoryEntry entry) => ItemDatabase.Instance != null ? ItemDatabase.Instance.Find(entry.itemId) : null;

    static int EquipIndex(ItemCategory category) =>
        category == ItemCategory.Weapon || category == ItemCategory.Armor || category == ItemCategory.Accessory ? (int)category - 1 : -1;

    static bool SameId(InventoryEntry entry, string itemId) => string.Equals(entry.itemId, itemId, StringComparison.Ordinal);

    // ponytail: a linear list is enough for a small inventory; index by id if item types grow large.
    InventoryEntry Find(string itemId) => items.Find(item => SameId(item, itemId));

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
        for (int i = 0; i < items.Count; i++) result[i] = ToSave(items[i]);
        return result;
    }

    /// <summary>장착 칸 저장. 빈 칸은 itemId 가 빈 항목이다(JsonUtility 는 배열의 null 을 쓰지 못한다).</summary>
    public InventorySaveEntry[] CaptureEquipped()
    {
        var result = new InventorySaveEntry[EquipmentSlotCount];
        for (int i = 0; i < result.Length; i++) result[i] = equipped[i] != null ? ToSave(equipped[i]) : new InventorySaveEntry { itemId = "", slotIndex = -1 };
        return result;
    }

    static InventorySaveEntry ToSave(InventoryEntry entry) => new InventorySaveEntry {
        itemId = entry.itemId, displayName = entry.displayName, quantity = entry.quantity,
        slotIndex = entry.slotIndex, rarity = (int)entry.rarity, options = entry.options.ToArray()
    };

    static InventoryEntry FromSave(InventorySaveEntry saved) => new InventoryEntry {
        itemId = saved.itemId,
        displayName = string.IsNullOrWhiteSpace(saved.displayName) ? saved.itemId : saved.displayName,
        quantity = saved.quantity,
        rarity = (ItemRarity)saved.rarity,
        options = saved.options != null ? new List<ItemOption>(saved.options) : new List<ItemOption>(),
    };

    public void Load(PlayerSaveData data)
    {
        loaded = true;
        items.Clear();
        Array.Clear(equipped, 0, equipped.Length);
        if (data?.inventory != null)
            foreach (var saved in data.inventory)
            {
                var entry = FromSave(saved);
                // 칸 번호가 생기기 전 저장은 모두 0 으로 읽힌다. 먼저 차지한 항목이 칸을 갖고,
                // 겹치거나 범위를 벗어난 항목은 앞쪽 빈칸으로 옮긴다. 빈칸이 없으면 -1 로 보관만 한다.
                bool claimable = saved.slotIndex >= 0 && saved.slotIndex < SlotCount && GetItemInSlot(saved.slotIndex) == null;
                entry.slotIndex = claimable ? saved.slotIndex : FirstFreeSlot();
                items.Add(entry);
            }
        if (data?.equipped != null)
        {
            for (int i = 0; i < equipped.Length && i < data.equipped.Length; i++)
                if (data.equipped[i] != null && !string.IsNullOrWhiteSpace(data.equipped[i].itemId))
                {
                    equipped[i] = FromSave(data.equipped[i]);
                    equipped[i].slotIndex = -1;
                }
        }
        else GiveStarterGear(legacySave: data != null);   // 새 게임이거나 장비 기능 이전 저장. 한 번 저장되면 equipped 가 생겨 다시 주지 않는다.
        RecalculateStats();
        Changed?.Invoke();
        EquipmentChanged?.Invoke();
    }

    /// <param name="legacySave">장비 기능 이전 저장. 동굴을 이미 지났을 수 있어 검이 없으면 낡은 검을 새로 쥐여 준다.</param>
    void GiveStarterGear(bool legacySave)
    {
        var database = ItemDatabase.Instance;
        if (database == null) return;
        var rng = new System.Random();
        foreach (string id in new[] { StarterArmorId, StarterAccessoryId })
        {
            var definition = database.Find(id);
            int index = definition != null ? EquipIndex(definition.category) : -1;
            if (index >= 0) equipped[index] = CreateInstance(definition, ItemRarity.Normal, rng);
        }
        var sword = items.Find(item => SameId(item, StarterWeaponId));
        var swordDefinition = database.Find(StarterWeaponId);
        if (sword != null && swordDefinition != null)
        {
            items.Remove(sword);
            sword.slotIndex = -1;
            equipped[EquipIndex(swordDefinition.category)] = sword;
        }
        else if (sword == null && legacySave && swordDefinition != null)
            equipped[EquipIndex(swordDefinition.category)] = CreateInstance(swordDefinition, ItemRarity.Normal, rng);
    }

    public static void Validate(InventorySaveEntry[] inventory, InventorySaveEntry[] equippedItems = null)
    {
        if (inventory == null) throw new InvalidDataException("인벤토리 스냅샷 누락");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in inventory)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.itemId) || item.quantity <= 0 ||
                item.slotIndex < -1 || item.slotIndex >= SlotCount || !ValidRarity(item))
                throw new InvalidDataException("잘못된 인벤토리 저장 항목");
            // 겹치는 아이템은 한 줄이어야 한다. 겹침 1 장비는 개체마다 한 줄이라 같은 id 가 여러 번 나온다.
            bool instanced = ItemDatabase.Instance != null && ItemDatabase.Instance.Find(item.itemId)?.maxStack == 1;
            if (!ids.Add(item.itemId) && !instanced) throw new InvalidDataException("잘못된 인벤토리 저장 항목");
        }
        if (equippedItems == null) return;
        if (equippedItems.Length != EquipmentSlotCount) throw new InvalidDataException("장착 칸 저장 오류");
        foreach (var item in equippedItems)
            if (item != null && !string.IsNullOrWhiteSpace(item.itemId) && (item.quantity <= 0 || !ValidRarity(item)))
                throw new InvalidDataException("장착 칸 저장 오류");
    }

    static bool ValidRarity(InventorySaveEntry item) => item.rarity >= 0 && item.rarity <= (int)ItemRarity.Legendary;
}

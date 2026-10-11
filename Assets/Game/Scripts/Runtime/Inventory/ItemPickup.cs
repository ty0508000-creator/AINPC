using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 바닥에 놓인 아이템. 닿은 채 Ctrl 을 누르면 줍는다. 여러 개가 겹쳐 있으면 가장 가까운 것 하나만 줍는다
/// (닿기만 해도 줍게 하면 아이템이 많이 떨어진 곳에서 가방이 원치 않는 것으로 찬다).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public sealed class ItemPickup : MonoBehaviour
{
    [SerializeField] ItemDefinition item;
    [SerializeField, Min(1)] int quantity = 1;

    [Tooltip("주웠을 때 세울 진행 표시. 비우면 세우지 않는다")]
    [SerializeField] string flagOnPickup;

    // 몬스터가 떨어뜨렸거나 플레이어가 버린 장비 개체(등급·옵션 포함). 있으면 item 대신 이것을 준다.
    InventoryEntry instance;

    // 플레이어와 닿아 있는 아이템들. Ctrl 을 누르면 이 중 가장 가까운 것만 반응한다.
    static readonly List<ItemPickup> touching = new();
    PlayerInventory toucher;
    GameObject hint;

    void Reset() => GetComponent<Collider2D>().isTrigger = true;

    public void Init(InventoryEntry entry) => instance = entry;

    void OnTriggerEnter2D(Collider2D other)
    {
        var inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null) return;
        toucher = inventory;
        if (!touching.Contains(this)) touching.Add(this);
        if (hint == null) hint = ItemDrop.WorldLabel(transform, "PickupHint", "[Ctrl] 줍기", Color.white, instance != null ? 0.85f : 0.6f).gameObject;
        hint.SetActive(true);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (toucher == null || other.GetComponentInParent<PlayerInventory>() != toucher) return;
        Forget();
    }

    void OnDisable() => Forget();

    void Forget()
    {
        touching.Remove(this);
        toucher = null;
        if (hint != null) hint.SetActive(false);
    }

    // 한 번 누른 Ctrl 로 한 개만. 먼저 주운 아이템이 목록에서 빠지면 같은 프레임의 다른 아이템이 가장 가까워지기 때문이다.
    static int lastPickupFrame = -1;

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || toucher == null || Time.timeScale == 0f || DialogueManager.IsDialogueOpen || Time.frameCount == lastPickupFrame) return;
        if (!keyboard.leftCtrlKey.wasPressedThisFrame && !keyboard.rightCtrlKey.wasPressedThisFrame) return;
        var stats = toucher.GetComponent<PlayerStats>();
        if (stats != null && !stats.IsAlive) return;
        if (TryPickUp()) lastPickupFrame = Time.frameCount;
    }

    /// <summary>닿아 있는 아이템 중 가장 가까운 것이면 줍는다. 주웠으면 true.</summary>
    public bool TryPickUp()
    {
        if (toucher == null || Nearest(toucher) != this) return false;
        var inventory = toucher;

        if (instance != null)
        {
            // 가방이 가득 차면 바닥에 남는다.
            if (!inventory.TryAddInstance(instance)) return false;
            EquipIfSlotEmpty(inventory, instance);
            Remove();
            return true;
        }

        if (item == null) return false;
        bool added = inventory.TryAdd(item, quantity);

        // 이미 가지고 있어서 더 못 담아도 진행 표시는 세운다. 안 그러면 출구가 영영 안 열린다.
        if (!string.IsNullOrEmpty(flagOnPickup) && (added || inventory.Count(item.itemId) > 0))
            GameFlow.Instance.SetFlag(flagOnPickup);

        if (!added) return false;
        if (item.maxStack == 1)
        {
            var entry = FindNewest(inventory, item.itemId);
            if (entry != null) EquipIfSlotEmpty(inventory, entry);
        }
        Remove();
        return true;
    }

    static ItemPickup Nearest(PlayerInventory inventory)
    {
        touching.RemoveAll(p => p == null);
        ItemPickup best = null;
        float bestDistance = float.MaxValue;
        foreach (var pickup in touching)
        {
            if (pickup.toucher != inventory) continue;
            float distance = ((Vector2)(pickup.transform.position - inventory.transform.position)).sqrMagnitude;
            if (distance < bestDistance) { bestDistance = distance; best = pickup; }
        }
        return best;
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
        Forget();
        // 편집 모드 검증에서도 지울 수 있게 한다.
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }
}

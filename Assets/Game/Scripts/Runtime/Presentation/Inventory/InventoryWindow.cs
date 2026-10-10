using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// I 키로 여닫는 인벤토리 창(가방 5×3 + 장비 3칸). ESC 로도 닫힌다.
/// 배치와 외형은 <c>Tools > AINPC > Inventory > Build Inventory UI</c> 로 만든 InventoryCanvas 프리팹에 저장돼 있고,
/// 이 스크립트는 표시 갱신과 입력만 맡는다. 창을 열어도 게임은 멈추지 않지만 플레이어 조작은 막힌다.
/// </summary>
[DisallowMultipleComponent]
public sealed class InventoryWindow : MonoBehaviour
{
    static InventoryWindow openWindow;

    /// <summary>인벤토리 창이 열려 있는가. 플레이어 이동·공격·스킬을 막는 데 쓴다.</summary>
    public static bool IsOpen => openWindow != null;

    /// <summary>마지막으로 창이 닫힌 프레임. 같은 ESC 입력으로 메뉴창까지 열리는 것을 막는다.</summary>
    public static int LastClosedFrame { get; private set; } = -1;

    [SerializeField] ItemDatabase itemDatabase;
    [SerializeField] GameObject windowPanel;
    [SerializeField] InventorySlotView[] bagSlots = new InventorySlotView[PlayerInventory.SlotCount];
    [SerializeField] EquipmentSlotView[] equipmentSlots = new EquipmentSlotView[3];
    [SerializeField] TMP_Text usedSlotCountText;
    [SerializeField] Button closeButton;
    [SerializeField] ItemTooltip itemTooltip;
    [SerializeField] RectTransform canvasRect;
    [SerializeField] Image dragIcon;

    PlayerInventory inventory;
    InventorySlotView draggingFromSlot;
    InventorySlotView hoveredSlot;
    EquipmentSlotView hoveredEquipment;

    // 런타임 정의 목록. 검증 코드가 ItemDatabase.Instance 를 바꿔 끼우면 그것을 따른다.
    ItemDatabase Database => ItemDatabase.Instance != null ? ItemDatabase.Instance : itemDatabase;

#if UNITY_EDITOR
    /// <summary>에디터에서 UI 를 만들 때 참조를 연결한다.</summary>
    public void Setup(ItemDatabase database, GameObject panel, InventorySlotView[] bag, EquipmentSlotView[] equipment,
        TMP_Text usedCountText, Button close, ItemTooltip tooltip, RectTransform canvas, Image drag)
    {
        itemDatabase = database; windowPanel = panel; bagSlots = bag; equipmentSlots = equipment;
        usedSlotCountText = usedCountText; closeButton = close; itemTooltip = tooltip; canvasRect = canvas; dragIcon = drag;
    }
#endif

    void Awake()
    {
        closeButton.onClick.AddListener(Close);
        HideTransientParts();
        windowPanel.SetActive(false);
    }

    void Update()
    {
        if (IsOpen && openWindow == this && !CanStayOpen()) { Close(); return; }

        var keyboard = Keyboard.current;
        if (keyboard == null || IsTypingInInputField()) return;

        if (keyboard.escapeKey.wasPressedThisFrame && openWindow == this) Close();
        else if (keyboard.iKey.wasPressedThisFrame)
        {
            if (openWindow == this) Close();
            else Open();
        }
    }

    // ── 열고 닫기 ───────────────────────────────────────────────

    /// <summary>창을 연다. 대화·메뉴·사망 중이면 열지 않고 false.</summary>
    public bool Open()
    {
        if (!CanStayOpen()) return false;
        if (!BindInventory()) return false;
        if (openWindow != null && openWindow != this) openWindow.Close();
        RpgUI.CloseOpenWindow();

        openWindow = this;
        windowPanel.SetActive(true);
        Refresh();
        return true;
    }

    public void Close()
    {
        CancelItemDrag();
        HideTransientParts();
        if (windowPanel != null) windowPanel.SetActive(false);
        if (openWindow == this)
        {
            openWindow = null;
            LastClosedFrame = Time.frameCount;
        }
    }

    /// <summary>씬에 열린 인벤토리 창이 있으면 닫는다. 다른 창이 열릴 때 쓴다.</summary>
    public static void CloseOpenWindow()
    {
        if (openWindow != null) openWindow.Close();
    }

    bool CanStayOpen()
    {
        if (DialogueManager.IsDialogueOpen || PauseMenuUI.IsOpen || StoryConversationUI.IsOpen || Time.timeScale == 0f)
            return false;
        var stats = inventory != null ? inventory.GetComponent<PlayerStats>() : FindFirstObjectByType<PlayerStats>();
        return stats != null && stats.IsAlive;
    }

    static bool IsTypingInInputField()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null && selected.GetComponent<TMP_InputField>() != null;
    }

    bool BindInventory()
    {
        if (inventory != null) return true;
        inventory = FindFirstObjectByType<PlayerInventory>();
        if (inventory == null) return false;
        inventory.Changed += OnInventoryChanged;
        return true;
    }

    void OnInventoryChanged()
    {
        if (openWindow == this) Refresh();
    }

    // ── 표시 ────────────────────────────────────────────────────

    void Refresh()
    {
        foreach (var slot in bagSlots)
        {
            var entry = inventory.GetItemInSlot(slot.SlotIndex);
            if (entry == null) slot.ShowEmpty();
            else slot.ShowItem(Database?.Find(entry.itemId)?.icon, entry.displayName, FrameColor(entry));
        }
        foreach (var slot in equipmentSlots)
        {
            var equipped = inventory.GetEquipped(slot.AcceptedCategory);
            if (equipped == null) slot.ShowEmpty();
            else slot.ShowItem(Database?.Find(equipped.itemId)?.icon, equipped.rarity);
            slot.SetHovered(slot == hoveredEquipment);
        }
        usedSlotCountText.text = $"{inventory.UsedSlotCount} / {PlayerInventory.SlotCount}";

        if (draggingFromSlot != null) draggingFromSlot.SetFaded(true);
        if (hoveredSlot != null && draggingFromSlot == null) ShowTooltipFor(hoveredSlot);
        else if (hoveredEquipment != null && draggingFromSlot == null) ShowTooltipFor(hoveredEquipment);
        else itemTooltip.Hide();
    }

    void ShowTooltipFor(InventorySlotView slot)
    {
        var entry = inventory.GetItemInSlot(slot.SlotIndex);
        if (entry == null) { itemTooltip.Hide(); return; }
        var definition = Database?.Find(entry.itemId);
        var equipped = definition != null ? inventory.GetEquipped(definition.category) : null;
        itemTooltip.Show(entry, definition, equipped, slot.Rect, (RectTransform)windowPanel.transform);
    }

    void ShowTooltipFor(EquipmentSlotView slot)
    {
        var entry = inventory.GetEquipped(slot.AcceptedCategory);
        if (entry == null) { itemTooltip.Hide(); return; }
        itemTooltip.Show(entry, Database?.Find(entry.itemId), null, slot.Rect, (RectTransform)windowPanel.transform);
    }

    // 장비(겹침 1)는 등급색 테두리, 겹치는 아이템은 흰색.
    Color FrameColor(InventoryEntry entry) =>
        Database?.Find(entry.itemId)?.maxStack == 1 ? ItemRarityTable.Color(entry.rarity) : Color.white;

    void HideTransientParts()
    {
        if (itemTooltip != null) itemTooltip.Hide();
        if (dragIcon != null) dragIcon.gameObject.SetActive(false);
        if (hoveredSlot != null) hoveredSlot.SetHovered(false);
        if (hoveredEquipment != null) hoveredEquipment.SetHovered(false);
        hoveredSlot = null;
        hoveredEquipment = null;
    }

    // ── 칸 입력 (InventorySlotView 가 호출) ─────────────────────

    public void OnSlotPointerEnter(InventorySlotView slot)
    {
        if (openWindow != this) return;
        hoveredSlot = slot;
        slot.SetHovered(true);
        if (draggingFromSlot == null) ShowTooltipFor(slot);
    }

    public void OnSlotPointerExit(InventorySlotView slot)
    {
        slot.SetHovered(false);
        if (hoveredSlot != slot) return;
        hoveredSlot = null;
        itemTooltip.Hide();
    }

    public void BeginItemDrag(InventorySlotView slot, PointerEventData eventData)
    {
        if (openWindow != this || inventory.GetItemInSlot(slot.SlotIndex) == null) return;
        draggingFromSlot = slot;
        slot.SetFaded(true);
        itemTooltip.Hide();

        var entry = inventory.GetItemInSlot(slot.SlotIndex);
        var icon = Database?.Find(entry.itemId)?.icon;
        dragIcon.sprite = icon;
        dragIcon.color = icon != null ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        dragIcon.gameObject.SetActive(true);
        dragIcon.transform.SetAsLastSibling();
        UpdateItemDrag(eventData);
    }

    public void UpdateItemDrag(PointerEventData eventData)
    {
        if (draggingFromSlot == null) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, null, out var local))
            dragIcon.rectTransform.anchoredPosition = local;
    }

    public void DropItemOn(InventorySlotView target)
    {
        if (draggingFromSlot == null) return;
        var from = draggingFromSlot;
        CancelItemDrag();
        inventory.MoveItem(from.SlotIndex, target.SlotIndex);
        // 놓은 칸 위에 마우스가 그대로 있으니 옮겨진 아이템 정보를 바로 보여 준다.
        if (hoveredSlot == target) ShowTooltipFor(target);
    }

    /// <summary>가방 칸 우클릭: 장비면 해당 부위에 장착한다.</summary>
    public void OnSlotRightClick(InventorySlotView slot)
    {
        if (openWindow != this || draggingFromSlot != null) return;
        inventory.Equip(slot.SlotIndex);
    }

    /// <summary>장비 칸 우클릭: 가방 빈칸으로 벗는다. 가방이 가득 차면 그대로.</summary>
    public void UnequipFrom(EquipmentSlotView slot)
    {
        if (openWindow != this || draggingFromSlot != null) return;
        inventory.Unequip(slot.AcceptedCategory);
    }

    /// <summary>가방 칸을 장비 칸에 놓았을 때. 부위가 맞을 때만 장착한다.</summary>
    public void DropItemOnEquipment(EquipmentSlotView target)
    {
        if (draggingFromSlot == null) return;
        var from = draggingFromSlot;
        CancelItemDrag();
        var entry = inventory.GetItemInSlot(from.SlotIndex);
        if (entry != null && Database?.Find(entry.itemId)?.category == target.AcceptedCategory) inventory.Equip(from.SlotIndex);
    }

    public void OnEquipmentPointerEnter(EquipmentSlotView slot)
    {
        if (openWindow != this) return;
        hoveredEquipment = slot;
        slot.SetHovered(true);
        if (draggingFromSlot == null) ShowTooltipFor(slot);
    }

    public void OnEquipmentPointerExit(EquipmentSlotView slot)
    {
        slot.SetHovered(false);
        if (hoveredEquipment != slot) return;
        hoveredEquipment = null;
        itemTooltip.Hide();
    }

    /// <summary>칸 밖에 놓았을 때도 불린다. 이미 놓기가 처리됐으면 아무것도 하지 않는다.</summary>
    public void EndItemDrag() => CancelItemDrag();

    void CancelItemDrag()
    {
        var from = draggingFromSlot;
        draggingFromSlot = null;
        // 씬이 내려가는 중이면 칸이 먼저 파괴됐을 수 있다.
        if (from != null) from.SetFaded(false);
        if (dragIcon != null) dragIcon.gameObject.SetActive(false);
    }

    void OnDisable() => Close();

    void OnDestroy()
    {
        if (inventory != null) inventory.Changed -= OnInventoryChanged;
        if (openWindow == this) openWindow = null;
    }
}

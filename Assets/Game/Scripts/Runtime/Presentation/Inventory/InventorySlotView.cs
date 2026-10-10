using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 가방의 한 칸. 마우스를 올리면 툴팁을 띄우고, 끌어서 다른 칸에 놓으면 아이템 자리를 옮긴다.
/// 실제 처리는 <see cref="InventoryWindow"/> 가 하고 이 칸은 입력만 전달한다.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public sealed class InventorySlotView : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [SerializeField] InventoryWindow window;
    [SerializeField] int slotIndex;
    [SerializeField] Image itemIcon;
    [Tooltip("아이콘이 없는 아이템은 이름 첫 글자를 대신 보여 준다")]
    [SerializeField] TMP_Text itemInitialText;
    [SerializeField] GameObject hoverFrame;

    public int SlotIndex => slotIndex;
    public RectTransform Rect => (RectTransform)transform;

#if UNITY_EDITOR
    /// <summary>에디터에서 UI 를 만들 때 참조를 연결한다.</summary>
    public void Setup(InventoryWindow owner, int index, Image icon, TMP_Text initialText, GameObject hover)
    {
        window = owner; slotIndex = index; itemIcon = icon; itemInitialText = initialText; hoverFrame = hover;
    }
#endif

    public void ShowItem(Sprite icon, string displayName)
    {
        itemIcon.sprite = icon;
        itemIcon.enabled = icon != null;
        itemInitialText.text = icon == null && !string.IsNullOrEmpty(displayName) ? displayName.Substring(0, 1) : "";
        SetFaded(false);
    }

    public void ShowEmpty()
    {
        itemIcon.sprite = null;
        itemIcon.enabled = false;
        itemInitialText.text = "";
        SetFaded(false);
    }

    /// <summary>끌고 있는 동안 원래 칸의 아이템을 흐리게 보여 준다.</summary>
    public void SetFaded(bool faded)
    {
        float alpha = faded ? 0.3f : 1f;
        itemIcon.color = new Color(1f, 1f, 1f, alpha);
        itemInitialText.alpha = alpha;
    }

    public void SetHovered(bool hovered) => hoverFrame.SetActive(hovered);

    public void OnPointerEnter(PointerEventData eventData) => window.OnSlotPointerEnter(this);
    public void OnPointerExit(PointerEventData eventData) => window.OnSlotPointerExit(this);

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) window.BeginItemDrag(this, eventData);
    }

    public void OnDrag(PointerEventData eventData) => window.UpdateItemDrag(eventData);
    public void OnEndDrag(PointerEventData eventData) => window.EndItemDrag();
    public void OnDrop(PointerEventData eventData) => window.DropItemOn(this);
}

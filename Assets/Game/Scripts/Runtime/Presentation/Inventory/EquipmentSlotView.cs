using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 장비창의 한 칸(무기/갑옷/장신구). 장착한 장비를 등급색 테두리로 보여 주고,
/// 우클릭하면 벗고, 가방 칸을 끌어다 놓으면 장착한다. 실제 처리는 <see cref="InventoryWindow"/> 가 한다.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public sealed class EquipmentSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IDropHandler
{
    [SerializeField] ItemCategory acceptedCategory = ItemCategory.Weapon;
    [Tooltip("비어 있을 때 보이는 흐린 실루엣")]
    [SerializeField] Image emptySlotSilhouette;
    [SerializeField] Image equippedItemIcon;
    [SerializeField] GameObject hoverFrame;

    InventoryWindow window;

    public ItemCategory AcceptedCategory => acceptedCategory;
    public RectTransform Rect => (RectTransform)transform;
    InventoryWindow Window => window != null ? window : window = GetComponentInParent<InventoryWindow>(true);

#if UNITY_EDITOR
    /// <summary>에디터에서 UI 를 만들 때 참조를 연결한다.</summary>
    public void Setup(ItemCategory category, Image silhouette, Image icon, GameObject hover)
    {
        acceptedCategory = category; emptySlotSilhouette = silhouette; equippedItemIcon = icon; hoverFrame = hover;
    }
#endif

    public void ShowEmpty()
    {
        emptySlotSilhouette.enabled = true;
        equippedItemIcon.enabled = false;
        GetComponent<Image>().color = Color.white;
    }

    public void ShowItem(Sprite icon, ItemRarity rarity)
    {
        emptySlotSilhouette.enabled = icon == null;
        equippedItemIcon.sprite = icon;
        equippedItemIcon.enabled = icon != null;
        GetComponent<Image>().color = ItemRarityTable.Color(rarity);
    }

    public void SetHovered(bool hovered) => hoverFrame.SetActive(hovered);

    public void OnPointerEnter(PointerEventData eventData) => Window.OnEquipmentPointerEnter(this);
    public void OnPointerExit(PointerEventData eventData) => Window.OnEquipmentPointerExit(this);
    public void OnDrop(PointerEventData eventData) => Window.DropItemOnEquipment(this);

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right) Window.UnequipFrom(this);
    }
}

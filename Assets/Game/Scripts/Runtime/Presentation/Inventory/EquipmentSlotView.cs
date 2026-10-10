using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 장비창의 한 칸(무기/갑옷/장신구). 지금은 자리만 잡아 둔 상태라 빈 칸 모양만 보여 준다.
/// 장착 기능은 다음 단계에서 이 칸에 붙인다.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public sealed class EquipmentSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] ItemCategory acceptedCategory = ItemCategory.Weapon;
    [Tooltip("비어 있을 때 보이는 흐린 실루엣")]
    [SerializeField] Image emptySlotSilhouette;
    [SerializeField] Image equippedItemIcon;
    [SerializeField] GameObject hoverFrame;

    public ItemCategory AcceptedCategory => acceptedCategory;

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
        hoverFrame.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData) => hoverFrame.SetActive(true);
    public void OnPointerExit(PointerEventData eventData) => hoverFrame.SetActive(false);
}

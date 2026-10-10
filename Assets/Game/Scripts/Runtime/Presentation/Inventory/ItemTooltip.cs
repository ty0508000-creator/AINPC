using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 가방 칸에 마우스를 올렸을 때 뜨는 아이템 정보 창.
/// 높이는 설명 길이에 맞춰 늘어나고, 화면 밖으로 나가지 않게 칸의 왼쪽 또는 오른쪽에 붙는다.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public sealed class ItemTooltip : MonoBehaviour
{
    const float GapFromWindow = 10f;
    const float ScreenMargin = 12f;

    [SerializeField] RectTransform canvasRect;
    [SerializeField] TMP_Text itemNameText;
    [SerializeField] TMP_Text itemCategoryText;
    [SerializeField] TMP_Text itemDescriptionText;
    [SerializeField] TMP_Text itemQuantityText;

    RectTransform Rect => (RectTransform)transform;

#if UNITY_EDITOR
    /// <summary>에디터에서 UI 를 만들 때 참조를 연결한다.</summary>
    public void Setup(RectTransform canvas, TMP_Text nameText, TMP_Text categoryText, TMP_Text descriptionText, TMP_Text quantityText)
    {
        canvasRect = canvas; itemNameText = nameText; itemCategoryText = categoryText;
        itemDescriptionText = descriptionText; itemQuantityText = quantityText;
    }
#endif

    /// <param name="definition">아이템 정의. 목록에 없는 옛 아이템이면 null 이고 저장된 이름만 보여 준다.</param>
    /// <param name="window">툴팁이 가리지 않게 비켜 줄 창. 툴팁은 이 창 바깥 왼쪽, 칸과 같은 높이에 붙는다.</param>
    public void Show(InventoryEntry entry, ItemDefinition definition, RectTransform slot, RectTransform window)
    {
        itemNameText.text = entry.displayName;
        itemCategoryText.text = ItemDefinition.CategoryLabel(definition != null ? definition.category : ItemCategory.Misc);
        string description = definition != null ? definition.description : null;
        itemDescriptionText.text = string.IsNullOrWhiteSpace(description) ? "설명이 없습니다." : description;
        itemQuantityText.text = $"보유 수량  {entry.quantity}";
        gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(Rect);
        PlaceBeside(slot, window);
    }

    public void Hide() => gameObject.SetActive(false);

    void PlaceBeside(RectTransform slot, RectTransform window)
    {
        Vector2 slotTopLeft = CanvasCorner(slot, 1);
        Vector2 windowTopLeft = CanvasCorner(window, 1);
        Vector2 windowTopRight = CanvasCorner(window, 2);
        Rect bounds = canvasRect.rect;
        Vector2 size = Rect.rect.size;

        Rect.anchorMin = Rect.anchorMax = new Vector2(0.5f, 0.5f);
        Rect.pivot = new Vector2(0f, 1f);
        // 기본은 창 바깥 왼쪽. 왼쪽 공간이 모자라면 창 바깥 오른쪽에 붙인다.
        float x = windowTopLeft.x - GapFromWindow - size.x;
        if (x < bounds.xMin + ScreenMargin) x = windowTopRight.x + GapFromWindow;
        x = Mathf.Clamp(x, bounds.xMin + ScreenMargin, bounds.xMax - ScreenMargin - size.x);
        float y = Mathf.Max(slotTopLeft.y, bounds.yMin + ScreenMargin + size.y);
        y = Mathf.Min(y, bounds.yMax - ScreenMargin);
        Rect.anchoredPosition = new Vector2(x, y);
    }

    /// <summary>모서리(0 왼쪽 아래, 1 왼쪽 위, 2 오른쪽 위, 3 오른쪽 아래)를 캔버스 좌표로.</summary>
    Vector2 CanvasCorner(RectTransform target, int corner)
    {
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        return canvasRect.InverseTransformPoint(corners[corner]);
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 최종보스를 쓰러뜨리면 뜨는 "게임 클리어" 화면. 버튼으로 타이틀에 돌아간다.
/// 진행 표시(final.boss_cleared)는 StageBoss 가 이미 저장했으므로 여기서는 화면만 다룬다.
/// </summary>
public class GameClearScreen : MonoBehaviour
{
    static GameClearScreen current;

    public static void Show()
    {
        if (current != null) return;
        current = new GameObject("GameClearScreen").AddComponent<GameClearScreen>();
    }

    void Start()
    {
        var font = Resources.Load<TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF");
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

        var shade = new GameObject("Shade", typeof(Image)).GetComponent<Image>();
        shade.transform.SetParent(transform, false);
        shade.color = new Color(0f, 0f, 0f, 0.8f);
        var rect = shade.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;

        Label(font, "게임 클리어", 96, new Vector2(0f, 120f));
        Label(font, "모든 싸움이 끝났다.", 36, new Vector2(0f, 10f));

        var button = new GameObject("TitleButton", typeof(Image), typeof(Button));
        button.transform.SetParent(transform, false);
        button.GetComponent<Image>().color = new Color(0.32f, 0.22f, 0.13f);
        var buttonRect = button.GetComponent<RectTransform>();
        buttonRect.anchoredPosition = new Vector2(0f, -140f);
        buttonRect.sizeDelta = new Vector2(360f, 90f);
        Label(font, "타이틀로", 36, Vector2.zero, button.transform);
        button.GetComponent<Button>().onClick.AddListener(() =>
        {
            GameFlow.Instance.ReturnToTitle();
            Destroy(gameObject);
        });
    }

    void Label(TMP_FontAsset font, string text, float size, Vector2 position, Transform parent = null)
    {
        var label = new GameObject("Label", typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(parent != null ? parent : transform, false);
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        label.rectTransform.anchoredPosition = position;
        label.rectTransform.sizeDelta = new Vector2(1200f, size * 1.6f);
    }

    void OnDestroy()
    {
        if (current == this) current = null;
    }
}

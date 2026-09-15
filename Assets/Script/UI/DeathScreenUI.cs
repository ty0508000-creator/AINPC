using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 죽었을 때 화면을 덮는 사망 UI. PlayerHUD 와 마찬가지로 런타임에 캔버스를 직접 만든다.
/// 부활 요청은 <see cref="OnRespawnRequested"/> 로 알리고, 실제 부활 처리는
/// <see cref="PlayerRespawn"/> 이 맡는다.
/// </summary>
public class DeathScreenUI : MonoBehaviour
{
    /// <summary>부활 버튼을 눌렀을 때.</summary>
    public event Action OnRespawnRequested;

    [Tooltip("한글 폰트. 비워두면 영문 문구로 대체한다")]
    [SerializeField] private TMP_FontAsset koreanFont;

    [Tooltip("화면이 어두워지는 시간")]
    [SerializeField] private float fadeDuration = 0.7f;

    private CanvasGroup group;
    private TMP_Text titleText;
    private TMP_Text hintText;
    private Button respawnButton;
    private bool visible;

    /// <summary>지금 사망 화면이 떠 있는가.</summary>
    public bool IsVisible => visible;

    /// <summary>폰트를 넘겨받는다. 캔버스를 만들기 전에 불러야 한다.</summary>
    public void Configure(TMP_FontAsset font)
    {
        koreanFont = font;
    }

    void Start()
    {
        Build();
        HideInstantly();
    }

    void Update()
    {
        if (group == null)
            return;

        float target = visible ? 1f : 0f;
        if (Mathf.Approximately(group.alpha, target))
            return;

        // 죽은 뒤에는 시간이 멈춰 있을 수도 있으니 unscaled 로 센다
        float step = fadeDuration > 0f ? Time.unscaledDeltaTime / fadeDuration : 1f;
        group.alpha = Mathf.MoveTowards(group.alpha, target, step);
    }

    public void Show()
    {
        if (group == null)
            Build();

        visible = true;
        group.blocksRaycasts = true;
        group.interactable = true;
        gameObject.SetActive(true);

        if (respawnButton != null)
            respawnButton.gameObject.SetActive(true);
    }

    public void Hide()
    {
        visible = false;
        if (group == null)
            return;

        group.blocksRaycasts = false;
        group.interactable = false;
    }

    private void HideInstantly()
    {
        Hide();
        if (group != null)
            group.alpha = 0f;
    }

    // ── 캔버스 조립 ────────────────────────────────────────────

    private void Build()
    {
        if (group != null)
            return;

        bool hasKorean = koreanFont != null;

        var canvasGo = new GameObject("DeathScreen_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;   // HUD(100) 보다 위

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();
        group = canvasGo.AddComponent<CanvasGroup>();

        // 화면 전체를 덮는 어두운 막
        var overlay = new GameObject("Overlay");
        overlay.transform.SetParent(canvasGo.transform, false);
        var overlayImage = overlay.AddComponent<Image>();
        overlayImage.color = new Color(0.05f, 0f, 0.02f, 0.8f);
        Stretch(overlay.GetComponent<RectTransform>());

        titleText = MakeText(canvasGo.transform,
            hasKorean ? "쓰러졌다" : "YOU DIED",
            96f, new Vector2(0f, 80f), new Color(0.85f, 0.15f, 0.15f), FontStyles.Bold);

        hintText = MakeText(canvasGo.transform,
            hasKorean ? "아무 키나 눌러 다시 시작" : "Press any key to respawn",
            28f, new Vector2(0f, -40f), new Color(0.85f, 0.85f, 0.85f), FontStyles.Normal);

        respawnButton = MakeButton(canvasGo.transform,
            hasKorean ? "부활" : "RESPAWN", new Vector2(0f, -140f));

        UiBootstrap.EnsureEventSystem();
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private TMP_Text MakeText(Transform parent, string text, float size, Vector2 position, Color color, FontStyles style)
    {
        var go = new GameObject("Text_" + text);
        go.transform.SetParent(parent, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        if (koreanFont != null)
            tmp.font = koreanFont;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(900f, size * 1.6f);

        return tmp;
    }

    private Button MakeButton(Transform parent, string label, Vector2 position)
    {
        var go = new GameObject("RespawnButton");
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.18f, 0.18f, 0.2f, 0.95f);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(260f, 68f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;

        var colors = button.colors;
        colors.highlightedColor = new Color(0.32f, 0.32f, 0.36f, 1f);
        colors.pressedColor = new Color(0.45f, 0.12f, 0.12f, 1f);
        button.colors = colors;

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        if (koreanFont != null)
            tmp.font = koreanFont;
        tmp.fontSize = 30f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        Stretch(textGo.GetComponent<RectTransform>());

        button.onClick.AddListener(() => OnRespawnRequested?.Invoke());
        return button;
    }
}

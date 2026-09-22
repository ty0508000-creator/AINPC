using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 타이틀 씬에 배치된 화면과 버튼의 동작을 관리한다.
/// </summary>
public class TitleScreenUI : MonoBehaviour
{
    [Tooltip("화면에 크게 뜰 제목")]
    [SerializeField] private string gameTitle = "AINPC";

    [Tooltip("부제 / 한 줄 설명")]
    [SerializeField] private string subtitle = "몸 하나, 인격 둘";

    [Tooltip("새 게임을 시작할 씬 이름")]
    [SerializeField] private string firstScene = "Main";

    [Tooltip("한글 폰트. 비워두면 기본 폰트로 나온다")]
    [SerializeField] private TMP_FontAsset koreanFont;

    [SerializeField] private Button continueButton, newGameButton, quitButton;
    [SerializeField] private TMP_Text noSaveText;
    [SerializeField] private GameObject canvasRoot;

    void Start()
    {
        if (canvasRoot == null) { enabled = false; return; }
        newGameButton.onClick.AddListener(NewGame);
        continueButton.onClick.AddListener(ContinueGame);
        quitButton.onClick.AddListener(QuitGame);
        bool hasSave = SaveSystem.LoadPlayer() != null;
        continueButton.interactable = hasSave;
        continueButton.GetComponentInChildren<TMP_Text>().color = hasSave ? Color.white : new Color(0.45f, 0.45f, 0.5f);
        noSaveText.gameObject.SetActive(!hasSave);
    }

#if UNITY_EDITOR
    public void BakeSceneUI()
    {
        if (canvasRoot != null) return;
        if (firstScene == "Test") firstScene = "Main";
        if (koreanFont == null) koreanFont = Resources.Load<TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF");
        Build();
    }
#endif

    private void Build()
    {
        var canvasGo = new GameObject("Title_Canvas");
        canvasRoot = canvasGo;
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // 배경
        var bg = new GameObject("Background").AddComponent<Image>();
        bg.transform.SetParent(canvasGo.transform, false);
        bg.color = new Color(0.05f, 0.06f, 0.09f, 1f);
        Stretch(bg.GetComponent<RectTransform>());

        MakeText(canvasGo.transform, gameTitle, 110f, new Vector2(0f, 220f),
            new Color(0.9f, 0.9f, 0.95f), FontStyles.Bold);

        MakeText(canvasGo.transform, subtitle, 28f, new Vector2(0f, 130f),
            new Color(0.65f, 0.65f, 0.72f), FontStyles.Normal);

        bool hasSave = false;

        newGameButton = MakeButton(canvasGo.transform, "새 게임", new Vector2(0f, -10f), NewGame, true);
        continueButton = MakeButton(canvasGo.transform, "이어하기", new Vector2(0f, -100f), ContinueGame, hasSave);
        quitButton = MakeButton(canvasGo.transform, "종료", new Vector2(0f, -190f), QuitGame, true);

        if (!hasSave)
            noSaveText = MakeText(canvasGo.transform, "저장된 기록이 없습니다", 20f, new Vector2(0f, -260f),
                new Color(0.5f, 0.5f, 0.55f), FontStyles.Italic);

        UiBootstrap.EnsureEventSystem();
    }

    private void NewGame()
    {
        GameFlow.Instance.NewGame(firstScene);
    }

    private void ContinueGame()
    {
        if (!GameFlow.Instance.Continue())
        {
            Debug.LogWarning("[Title] 이어할 기록이 없습니다.");
            if (continueButton != null)
                continueButton.interactable = false;
        }
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── 조립 ────────────────────────────────────────────────────

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
        rect.sizeDelta = new Vector2(1000f, size * 1.6f);

        return tmp;
    }

    private Button MakeButton(Transform parent, string label, Vector2 position,
        UnityEngine.Events.UnityAction onClick, bool enabled)
    {
        var go = new GameObject("Button_" + label);
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.16f, 0.17f, 0.22f, 1f);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(320f, 70f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.interactable = enabled;
        // Start에서 저장된 버튼에 동작을 연결한다.

        var colors = button.colors;
        colors.highlightedColor = new Color(0.28f, 0.29f, 0.36f, 1f);
        colors.pressedColor = new Color(0.4f, 0.14f, 0.16f, 1f);
        colors.disabledColor = new Color(0.12f, 0.12f, 0.14f, 1f);
        button.colors = colors;

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        if (koreanFont != null)
            tmp.font = koreanFont;
        tmp.fontSize = 30f;
        tmp.color = enabled ? Color.white : new Color(0.45f, 0.45f, 0.5f);
        tmp.alignment = TextAlignmentOptions.Center;
        Stretch(textGo.GetComponent<RectTransform>());

        return button;
    }
}

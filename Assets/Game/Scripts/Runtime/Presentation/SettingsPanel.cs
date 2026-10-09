using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 메뉴창의 설정 화면. 볼륨·전체화면·해상도를 바꾸고 PlayerPrefs 에 기억한다.
/// 게임 시작 시 <see cref="ApplySaved"/> 가 저절로 불려 저장된 값을 적용한다.
/// </summary>
public static class SettingsPanel
{
    private const string VolumeKey = "settings.volume";
    private const string FullscreenKey = "settings.fullscreen";
    private const string WidthKey = "settings.width";
    private const string HeightKey = "settings.height";

    private static readonly Vector2Int[] Resolutions =
    {
        new Vector2Int(1280, 720),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080),
    };

    /// <summary>저장해 둔 설정을 게임 시작 때 한 번 적용한다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void ApplySaved()
    {
        AudioListener.volume = PlayerPrefs.GetFloat(VolumeKey, 1f);

        int width = PlayerPrefs.GetInt(WidthKey, 0);
        int height = PlayerPrefs.GetInt(HeightKey, 0);
        bool fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1;

        if (width > 0 && height > 0)
            Screen.SetResolution(width, height, fullscreen);
        else
            Screen.fullScreen = fullscreen;
    }

    /// <summary>주어진 자리에 설정 UI 를 만든다.</summary>
    public static void Build(RectTransform parent, TMP_FontAsset font)
    {
        var view = parent.gameObject.AddComponent<SceneSettingsView>();
        Label(parent, "설정", 34f, new Vector2(0f, 200f), Color.white, font, TextAlignmentOptions.Center);

        // ── 소리 ──
        Label(parent, "소리", 22f, new Vector2(-260f, 120f), new Color(0.75f, 0.75f, 0.8f), font, TextAlignmentOptions.Left);

        TMP_Text volumeValue = Label(parent, Percent(AudioListener.volume), 22f, new Vector2(240f, 120f),
            Color.white, font, TextAlignmentOptions.Right);

        Slider slider = MakeSlider(parent, new Vector2(20f, 120f), AudioListener.volume);
        view.volume = slider; view.volumeLabel = volumeValue;

        // ── 전체화면 ──
        Label(parent, "전체화면", 22f, new Vector2(-260f, 50f), new Color(0.75f, 0.75f, 0.8f), font, TextAlignmentOptions.Left);

        TMP_Text fullscreenLabel = null;
        Button fullscreenButton = MakeButton(parent, Screen.fullScreen ? "켜짐" : "꺼짐",
            new Vector2(200f, 50f), new Vector2(160f, 46f), font, out fullscreenLabel);
        view.fullscreen = fullscreenButton; view.fullscreenLabel = fullscreenLabel;


        // ── 자동 저장 ──
        Label(parent, "자동 저장", 22f, new Vector2(-260f, -10f), new Color(0.75f, 0.75f, 0.8f), font, TextAlignmentOptions.Left);

        TMP_Text autoSaveLabel = null;
        Button autoSaveButton = MakeButton(parent, GameFlow.AutoSaveEnabled ? "켜짐" : "꺼짐",
            new Vector2(200f, -10f), new Vector2(160f, 46f), font, out autoSaveLabel);
        view.autoSave = autoSaveButton; view.autoSaveLabel = autoSaveLabel;


        Label(parent, Mathf.RoundToInt(GameFlow.AutoSaveInterval) + "초마다", 16f, new Vector2(200f, -48f),
            new Color(0.5f, 0.5f, 0.55f), font, TextAlignmentOptions.Center);

        // ── 해상도 ──
        Label(parent, "해상도", 22f, new Vector2(-260f, -100f), new Color(0.75f, 0.75f, 0.8f), font, TextAlignmentOptions.Left);

        float x = -40f;
        int resolutionIndex = 0;
        foreach (Vector2Int res in Resolutions)
        {
            TMP_Text unused;
            Button button = MakeButton(parent, res.x + "x" + res.y, new Vector2(x, -100f), new Vector2(170f, 44f), font, out unused);
            view.resolutions[resolutionIndex++] = button;


            x += 180f;
        }

        // ── 계정 초기화 / 게임 종료 ──
        TMP_Text resetLabel = null;
        Button resetButton = MakeButton(parent, "계정 초기화", new Vector2(-95f, -172f),
            new Vector2(180f, 50f), font, out resetLabel);
        view.resetAccount = resetButton; view.resetAccountLabel = resetLabel;

        TMP_Text unusedQuit;
        view.quit = MakeButton(parent, "게임 종료", new Vector2(95f, -172f),
            new Vector2(180f, 50f), font, out unusedQuit);

        Label(parent, "계정 초기화는 저장을 지우고 타이틀로 돌아갑니다", 16f, new Vector2(0f, -212f),
            new Color(0.5f, 0.5f, 0.55f), font, TextAlignmentOptions.Center);
    }

    private static string Percent(float value) => Mathf.RoundToInt(value * 100f) + "%";

    // ── 부품 ────────────────────────────────────────────────────

    private static TMP_Text Label(RectTransform parent, string text, float size, Vector2 position,
        Color color, TMP_FontAsset font, TextAlignmentOptions align)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        if (font != null) tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(360f, size * 2f);

        return tmp;
    }

    private static Slider MakeSlider(RectTransform parent, Vector2 position, float value)
    {
        var go = new GameObject("Slider");
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(360f, 24f);

        var background = new GameObject("Background").AddComponent<Image>();
        background.transform.SetParent(go.transform, false);
        background.color = new Color(0.2f, 0.2f, 0.25f, 1f);
        StretchFull(background.GetComponent<RectTransform>());

        var fillArea = new GameObject("Fill Area").AddComponent<RectTransform>();
        fillArea.transform.SetParent(go.transform, false);
        StretchFull(fillArea);

        var fill = new GameObject("Fill").AddComponent<Image>();
        fill.transform.SetParent(fillArea, false);
        fill.color = new Color(0.45f, 0.7f, 0.95f, 1f);
        StretchFull(fill.GetComponent<RectTransform>());

        var slider = go.AddComponent<Slider>();
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.targetGraphic = background;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.SetValueWithoutNotify(value);

        return slider;
    }

    private static Button MakeButton(RectTransform parent, string label, Vector2 position, Vector2 size,
        TMP_FontAsset font, out TMP_Text labelText)
    {
        var go = new GameObject("Button_" + label);
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.18f, 0.19f, 0.24f, 1f);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;

        var colors = button.colors;
        colors.highlightedColor = new Color(0.3f, 0.31f, 0.38f, 1f);
        button.colors = colors;

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);

        labelText = textGo.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        if (font != null) labelText.font = font;
        labelText.fontSize = 20f;
        labelText.color = Color.white;
        labelText.alignment = TextAlignmentOptions.Center;
        StretchFull(textGo.GetComponent<RectTransform>());

        return button;
    }

    private static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

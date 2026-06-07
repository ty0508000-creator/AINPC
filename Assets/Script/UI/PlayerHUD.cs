using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerHUD : MonoBehaviour
{
    private PlayerStats stats;
    private MoodSystem moodSystem;
    private ControlManager controlManager;

    [Header("HUD")]
    [SerializeField, Min(0.1f)] private float hudScale = 1.5f;
    [SerializeField] private TMP_FontAsset koreanFont;

    private TMP_Text levelText;
    private TMP_Text hpText;
    private TMP_Text manaText;
    private TMP_Text expText;

    private Image moodFill;
    private TMP_Text moodText;
    private TMP_Text controlText;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        moodSystem = GetComponent<MoodSystem>();
        controlManager = GetComponent<ControlManager>();

        if (stats == null || moodSystem == null || controlManager == null)
        {
            Debug.LogError("PlayerHUD requires PlayerStats, MoodSystem, and ControlManager on the same GameObject.");
            enabled = false;
            return;
        }

        BuildUI();

        stats.OnStatsChanged += UpdateStats;
        moodSystem.OnMoodChanged += UpdateMood;
        controlManager.OnAITakeover += HandleAITakeover;
        controlManager.OnPlayerRestored += HandlePlayerRestored;

        UpdateStats();
        UpdateMood(moodSystem.Mood);
        UpdateControlLabel(true);
    }

    void BuildUI()
    {
        var canvasGO = new GameObject("PlayerHUD_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // ── 스탯 패널 ──
        var statsPanel = MakePanel(canvasGO.transform, Scale(new Vector2(15f, -15f)), Scale(new Vector2(220f, 120f)));
        levelText = MakeText(statsPanel, Scale(new Vector2(12f, -10f)), "Lv. 1", Scale(18f), FontStyles.Bold);
        hpText    = MakeText(statsPanel, Scale(new Vector2(12f, -38f)), "HP   100 / 100", Scale(15f));
        manaText  = MakeText(statsPanel, Scale(new Vector2(12f, -62f)), "MP   50 / 50", Scale(15f));
        expText   = MakeText(statsPanel, Scale(new Vector2(12f, -86f)), "EXP  0 / 100", Scale(15f));

        // ── 기분 패널 ──
        var moodPanel = MakePanel(canvasGO.transform, Scale(new Vector2(15f, -145f)), Scale(new Vector2(220f, 70f)));

        controlText = MakeText(moodPanel, Scale(new Vector2(12f, -8f)), "제어권: 플레이어", Scale(14f), FontStyles.Bold);

        MakeText(moodPanel, Scale(new Vector2(12f, -30f)), "NPC 기분", Scale(13f));
        moodText = MakeText(moodPanel, Scale(new Vector2(160f, -30f)), "50", Scale(13f));
        moodText.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 1f);
        moodText.GetComponent<RectTransform>().anchorMax = new Vector2(0f, 1f);

        // 게이지 배경
        var barBg = new GameObject("MoodBarBg");
        barBg.transform.SetParent(moodPanel, false);
        var bgImg = barBg.AddComponent<Image>();
        bgImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        var bgRect = barBg.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0f, 1f);
        bgRect.anchorMax = new Vector2(1f, 1f);
        bgRect.pivot = new Vector2(0f, 1f);
        bgRect.anchoredPosition = Scale(new Vector2(12f, -46f));
        bgRect.sizeDelta = Scale(new Vector2(-24f, 14f));

        // 게이지 채움
        var barFill = new GameObject("MoodBarFill");
        barFill.transform.SetParent(barBg.transform, false);
        moodFill = barFill.AddComponent<Image>();
        moodFill.color = Color.green;
        var fillRect = barFill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0.5f, 1f); // 초기 50%
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
    }

    RectTransform MakePanel(Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("Panel");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.55f);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        return rect;
    }

    TMP_Text MakeText(RectTransform parent, Vector2 pos, string text, float size, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        if (koreanFont != null)
            tmp.font = koreanFont;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(0f, Scale(24f));
        return tmp;
    }

    float Scale(float value) => value * hudScale;

    Vector2 Scale(Vector2 value) => value * hudScale;

    void UpdateStats()
    {
        levelText.text = $"Lv. {stats.Level}";
        hpText.text    = $"HP   {stats.HP:0} / {stats.MaxHP:0}";
        manaText.text  = $"MP   {stats.Mana:0} / {stats.MaxMana:0}";
        expText.text   = $"EXP  {stats.EXP:0} / {stats.MaxEXP:0}";
    }

    void UpdateMood(float mood)
    {
        float ratio = mood / 100f;

        // 게이지 채움 비율
        var fillRect = moodFill.GetComponent<RectTransform>();
        fillRect.anchorMax = new Vector2(ratio, 1f);

        // 색상: 빨강(0) → 노랑(0.5) → 초록(1)
        moodFill.color = ratio < 0.5f
            ? Color.Lerp(Color.red, Color.yellow, ratio * 2f)
            : Color.Lerp(Color.yellow, Color.green, (ratio - 0.5f) * 2f);

        moodText.text = $"{mood:0}";
    }

    void UpdateControlLabel(bool isPlayer)
    {
        controlText.text = isPlayer ? "제어권: 플레이어" : "제어권: AI";
        controlText.color = isPlayer ? Color.white : new Color(1f, 0.4f, 0.4f);
    }

    void HandleAITakeover() => UpdateControlLabel(false);

    void HandlePlayerRestored() => UpdateControlLabel(true);

    void OnDestroy()
    {
        if (stats != null) stats.OnStatsChanged -= UpdateStats;
        if (moodSystem != null) moodSystem.OnMoodChanged -= UpdateMood;
        if (controlManager != null)
        {
            controlManager.OnAITakeover -= HandleAITakeover;
            controlManager.OnPlayerRestored -= HandlePlayerRestored;
        }
    }
}

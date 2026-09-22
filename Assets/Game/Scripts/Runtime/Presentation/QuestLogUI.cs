using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 화면 우상단에 진행 중인 퀘스트와 목표를 띄운다. 코드로 UI를 만드는
/// PlayerHUD 와 같은 방식. 아무 GameObject 에 붙이면 된다.
/// </summary>
public class QuestLogUI : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float uiScale = 1.5f;
    [SerializeField] private TMP_FontAsset koreanFont;
    [SerializeField] private KeyCode toggleKey = KeyCode.J;

    [Tooltip("메인 퀘스트 강조 색")]
    [SerializeField] private Color mainColor = new Color(1f, 0.88f, 0.55f);
    [SerializeField] private Color subColor = new Color(0.75f, 0.78f, 0.85f);
    [SerializeField] private Color doneColor = new Color(0.45f, 0.72f, 0.45f);

    private QuestManager manager;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private GameObject panelRoot;
    private bool visible = true;

    void Start()
    {
        if (panelRoot == null) { enabled = false; return; }

        manager = QuestManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning("[QuestLog] QuestManager 를 찾지 못했습니다.");
            Refresh();
            return;
        }

        manager.OnQuestStarted += OnQuestChanged;
        manager.OnQuestCompleted += OnQuestChanged;
        manager.OnQuestFailed += OnQuestChanged;
        manager.OnObjectiveProgress += OnObjectiveChanged;

        Refresh();
    }

    void OnDestroy()
    {
        if (manager == null) return;
        manager.OnQuestStarted -= OnQuestChanged;
        manager.OnQuestCompleted -= OnQuestChanged;
        manager.OnQuestFailed -= OnQuestChanged;
        manager.OnObjectiveProgress -= OnObjectiveChanged;
    }

    void OnQuestChanged(QuestRuntime _) => Refresh();
    void OnObjectiveChanged(QuestRuntime _, int __) => Refresh();

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            visible = !visible;
            panelRoot.SetActive(visible);
        }
    }

    void Refresh()
    {
        if (bodyText == null) return;

        if (manager == null)
        {
            bodyText.text = "";
            return;
        }

        var sb = new StringBuilder();
        int shown = 0;

        foreach (var r in manager.ActiveQuests())
        {
            string titleColor = ColorUtility.ToHtmlStringRGB(r.Data.isMainQuest ? mainColor : subColor);
            string mark = r.Data.isInnerVoiceDemand ? "◆" : (r.Data.isMainQuest ? "●" : "○");
            sb.AppendLine($"<color=#{titleColor}><b>{mark} {r.Data.title}</b></color>");

            for (int i = 0; i < r.Data.objectives.Length; i++)
            {
                var obj = r.Data.objectives[i];
                if (obj.hidden) continue;

                bool done = r.Progress[i] >= obj.requiredCount;
                string c = ColorUtility.ToHtmlStringRGB(done ? doneColor : subColor);
                string count = obj.requiredCount > 1 ? $" ({r.Progress[i]}/{obj.requiredCount})" : "";
                string box = done ? "✓" : "·";
                sb.AppendLine($"   <color=#{c}>{box} {obj.description}{count}</color>");
            }

            sb.AppendLine();
            shown++;
        }

        bodyText.text = shown == 0 ? $"<color=#{ColorUtility.ToHtmlStringRGB(subColor)}>진행 중인 퀘스트 없음</color>" : sb.ToString();
    }

    // ── UI 생성 ─────────────────────────────────────────────────

#if UNITY_EDITOR
    public void BakeSceneUI()
    {
        if (panelRoot != null) return;
        if (koreanFont == null) koreanFont = Resources.Load<TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF");
        BuildUI();
        bodyText.text = "진행 중인 퀘스트 없음";
    }
#endif
    void BuildUI()
    {
        var canvasGO = new GameObject("QuestLog_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 99;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // 우상단 고정 패널
        var panelGO = new GameObject("QuestPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        var img = panelGO.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.45f);
        img.raycastTarget = false;

        var rect = panelGO.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-15f * uiScale, -15f * uiScale);
        rect.sizeDelta = new Vector2(340f * uiScale, 220f * uiScale);

        var titleGO = new GameObject("Header");
        titleGO.transform.SetParent(rect, false);
        var title = titleGO.AddComponent<TextMeshProUGUI>();
        title.text = $"퀘스트  [{toggleKey}]";
        if (koreanFont != null) title.font = koreanFont;
        title.fontSize = 16f * uiScale;
        title.fontStyle = FontStyles.Bold;
        title.color = new Color(1f, 1f, 1f, 0.7f);
        title.raycastTarget = false;
        var tr = titleGO.GetComponent<RectTransform>();
        tr.anchorMin = new Vector2(0f, 1f);
        tr.anchorMax = new Vector2(1f, 1f);
        tr.pivot = new Vector2(0f, 1f);
        tr.anchoredPosition = new Vector2(12f * uiScale, -8f * uiScale);
        tr.sizeDelta = new Vector2(-24f * uiScale, 22f * uiScale);

        var bodyGO = new GameObject("Body");
        bodyGO.transform.SetParent(rect, false);
        bodyText = bodyGO.AddComponent<TextMeshProUGUI>();
        if (koreanFont != null) bodyText.font = koreanFont;
        bodyText.fontSize = 14f * uiScale;
        bodyText.color = Color.white;
        bodyText.raycastTarget = false;
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        var br = bodyGO.GetComponent<RectTransform>();
        br.anchorMin = Vector2.zero;
        br.anchorMax = Vector2.one;
        br.offsetMin = new Vector2(12f * uiScale, 10f * uiScale);
        br.offsetMax = new Vector2(-12f * uiScale, -34f * uiScale);

        panelRoot = panelGO;
    }
}

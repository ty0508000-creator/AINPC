using System.Collections;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LLMUnity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Windows.Speech;

public class InnerVoiceManager : MonoBehaviour
{
    [Header("Trigger")]
    [SerializeField] private float checkInterval = 30f;
    [SerializeField] private float triggerChance = 0.1f;

    [Header("LLM (내면 전용 LLMAgent 별도 연결)")]
    [SerializeField] private LLMAgent llmAgent;

    [Header("Enemy Detection")]
    [SerializeField] private LayerMask enemyLayer;
    [SerializeField] private float detectionRadius = 15f;

    [Header("Voice")]
    [SerializeField] private float listenTimeout = 10f;

    private MoodSystem moodSystem;
    private PlayerStats playerStats;
    private ControlManager controlManager;

    private bool isActive = false;
    private DictationRecognizer dictation;
    private string pendingText = "";

    // UI
    private GameObject panel;
    private TMP_Text aiText;
    private TMP_Text statusText;
    private TMP_Text moodDeltaText;

    [System.Serializable]
    private class InnerResponse
    {
        public string dialogue;
        public int mood_delta;
    }

    void Start()
    {
        moodSystem    = GetComponent<MoodSystem>();
        playerStats   = GetComponent<PlayerStats>();
        controlManager = GetComponent<ControlManager>();

        if (llmAgent == null)
            llmAgent = FindObjectOfType<LLMAgent>();

        BuildUI();
        StartCoroutine(TriggerLoop());
    }

    // ── 트리거 루프 ──────────────────────────────────────────────

    IEnumerator TriggerLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(checkInterval);

            if (!isActive && Random.value <= triggerChance)
                _ = OpenDialogue();
        }
    }

    // ── 내면 대화 진행 ───────────────────────────────────────────

    async Task OpenDialogue()
    {
        isActive = true;
        ShowPanel(true);
        moodDeltaText.text = "";
        aiText.text = "...";
        statusText.text = "";

        // 현재 상황을 시스템 프롬프트에 주입
        int enemyCount = Physics2D.OverlapCircleAll(
            (Vector2)transform.position, detectionRadius, enemyLayer).Length;

        string context =
            $"HP {playerStats.HP:0}/{playerStats.MaxHP:0}, " +
            $"기분 게이지 {moodSystem.Mood:0}/100, " +
            $"주변 적 {enemyCount}명, " +
            $"현재 {(controlManager.IsPlayerControlled ? "플레이어" : "AI")}가 몸을 제어 중.";

        llmAgent.systemPrompt =
            "너는 이 캐릭터의 또 다른 인격이야. " +
            "몸의 제어권을 두고 플레이어와 협상하거나, 위협하거나, 회유해. " +
            "짧고 강렬하게 한국어로 말해. 반드시 아래 JSON 형식으로만 응답해:\n" +
            "{\"dialogue\":\"대사\",\"mood_delta\":0}\n" +
            "mood_delta: 플레이어가 설득하거나 달래면 양수(+), 반항하거나 무시하면 음수(-).\n" +
            $"현재 상황: {context}";

        // AI 인격이 먼저 말 걸기
        string raw = await llmAgent.Chat("대화를 시작해.", addToHistory: true);
        var (dialogue, _) = Parse(raw);
        aiText.text = $"\"{dialogue}\"";

        // 마이크 대기
        StartListening();
    }

    // ── STT ──────────────────────────────────────────────────────

    void StartListening()
    {
        statusText.text = "[ 마이크 인식 중... ]";
        pendingText = "";

        dictation = new DictationRecognizer();
        dictation.DictationResult  += OnResult;
        dictation.DictationComplete += OnComplete;
        dictation.DictationError   += OnError;
        dictation.Start();

        StartCoroutine(ListenTimeoutRoutine());
    }

    IEnumerator ListenTimeoutRoutine()
    {
        yield return new WaitForSeconds(listenTimeout);
        if (isActive && dictation != null &&
            dictation.Status == SpeechSystemStatus.Running)
        {
            dictation.Stop();
        }
    }

    void OnResult(string text, ConfidenceLevel confidence)
    {
        pendingText = text;
        statusText.text = $"인식됨: {text}";
    }

    void OnComplete(DictationCompletionCause cause)
    {
        if (!string.IsNullOrEmpty(pendingText))
            _ = Respond(pendingText);
        else
            EndDialogue();
    }

    void OnError(string error, int hresult)
    {
        Debug.LogError($"[InnerVoice] 마이크 오류: {error}");
        EndDialogue();
    }

    // ── LLM 응답 ─────────────────────────────────────────────────

    async Task Respond(string playerSpeech)
    {
        statusText.text = "생각 중...";
        DisposeDictation();

        string raw = await llmAgent.Chat(playerSpeech, addToHistory: true);
        var (dialogue, moodDelta) = Parse(raw);

        aiText.text = $"\"{dialogue}\"";
        moodSystem.ChangeMood(moodDelta);

        string sign = moodDelta >= 0 ? "+" : "";
        moodDeltaText.text = $"기분 {sign}{moodDelta}";
        moodDeltaText.color = moodDelta >= 0
            ? new Color(0.4f, 1f, 0.4f)
            : new Color(1f, 0.4f, 0.4f);

        statusText.text = "";

        await Task.Delay(3500);
        EndDialogue();
    }

    void EndDialogue()
    {
        DisposeDictation();
        ShowPanel(false);
        isActive = false;
        pendingText = "";
    }

    // ── 파싱 ─────────────────────────────────────────────────────

    (string dialogue, int moodDelta) Parse(string raw)
    {
        int s = raw.IndexOf('{'), e = raw.LastIndexOf('}');
        if (s >= 0 && e > s)
        {
            try
            {
                var data = JsonUtility.FromJson<InnerResponse>(raw.Substring(s, e - s + 1));
                if (!string.IsNullOrEmpty(data.dialogue))
                    return (data.dialogue, data.mood_delta);
            }
            catch { }
        }

        var dm = Regex.Match(raw, "\"dialogue\"\\s*:\\s*\"([^\"]+)\"");
        var mm = Regex.Match(raw, "\"mood_delta\"\\s*:\\s*(-?\\d+)");
        return (
            dm.Success ? dm.Groups[1].Value : raw,
            mm.Success ? int.Parse(mm.Groups[1].Value) : 0
        );
    }

    // ── UI ───────────────────────────────────────────────────────

    void BuildUI()
    {
        var canvasGO = new GameObject("InnerVoiceCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // 패널 (하단 중앙)
        panel = new GameObject("InnerVoicePanel");
        panel.transform.SetParent(canvasGO.transform, false);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.05f, 0f, 0.12f, 0.88f);

        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot     = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 25f);
        rect.sizeDelta = new Vector2(620f, 115f);

        // 제목
        var title = Label(panel.transform, "[ 내면의 목소리 ]", new Vector2(0f, -10f), 13f,
            new Color(0.75f, 0.45f, 1f));
        title.alignment = TextAlignmentOptions.Center;

        // AI 대사
        aiText = Label(panel.transform, "...", new Vector2(0f, -34f), 17f, Color.white);
        aiText.alignment  = TextAlignmentOptions.Center;
        aiText.fontStyle  = FontStyles.Italic;

        // 기분 변화
        moodDeltaText = Label(panel.transform, "", new Vector2(0f, -68f), 14f, Color.green);
        moodDeltaText.alignment = TextAlignmentOptions.Center;

        // 상태
        statusText = Label(panel.transform, "", new Vector2(0f, -88f), 12f,
            new Color(0.5f, 0.9f, 0.5f));
        statusText.alignment = TextAlignmentOptions.Center;

        panel.SetActive(false);
    }

    TMP_Text Label(Transform parent, string text, Vector2 pos, float size, Color color)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = color;
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = new Vector2(0f, 1f);
        r.anchorMax = new Vector2(1f, 1f);
        r.pivot     = new Vector2(0.5f, 1f);
        r.anchoredPosition = pos;
        r.sizeDelta = new Vector2(0f, 28f);
        return tmp;
    }

    void ShowPanel(bool show)
    {
        if (panel != null) panel.SetActive(show);
    }

    // ── 정리 ─────────────────────────────────────────────────────

    void DisposeDictation()
    {
        if (dictation == null) return;
        try
        {
            if (dictation.Status == SpeechSystemStatus.Running)
                dictation.Stop();
            dictation.Dispose();
        }
        catch { }
        dictation = null;
    }

    void OnDestroy() => DisposeDictation();
}

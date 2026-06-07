using System;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LLMUnity;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InnerVoiceManager : MonoBehaviour
{
    [Header("Trigger")]
    [SerializeField] private float checkInterval = 30f;
    [SerializeField] private float triggerChance = 0.1f;

    [Header("LLM (내면 전용 LLMAgent 별도 연결)")]
    [SerializeField] private LLMAgent llmAgent;
    [Tooltip("이 시간(초) 안에 응답이 없으면 침묵 속에 종료 (소프트락 방지)")]
    [SerializeField] private float llmTimeout = 20f;

    [Header("Memory (선택 — 비우면 자동 탐색)")]
    [SerializeField] private MemoryManager memory;

    [Header("Enemy Detection")]
    [SerializeField] private LayerMask enemyLayer;
    [SerializeField] private float detectionRadius = 15f;

    [Header("Inner World Visuals")]
    [SerializeField] private Color voidColor = new Color(0.04f, 0f, 0.09f, 0.97f);
    [SerializeField] private Color accentColor = new Color(0.75f, 0.45f, 1f);
    [SerializeField] private Sprite avatarSprite;   // 선택: 어둠 인격 실루엣/초상
    [SerializeField] private float transitionTime = 0.55f;

    [Header("Text")]
    [SerializeField] private TMP_FontAsset koreanFont;
    [SerializeField] private float fontScale = 2f;

    private MoodSystem moodSystem;
    private PlayerStats playerStats;
    private ControlManager controlManager;

    private bool isActive = false;
    private bool isProcessing = false;
    private bool isTransitioning = false;
    private bool acceptStream = false;
    private bool streamStarted = false;
    private float prevTimeScale = 1f;

    // UI
    private GameObject overlayRoot;
    private Image background;
    private CanvasGroup contentGroup;
    private RectTransform contentRect;
    private TMP_Text aiText;
    private TMP_Text statusText;
    private TMP_Text moodDeltaText;
    private TMP_InputField inputField;
    private Button sendButton;

    [System.Serializable]
    private class InnerResponse
    {
        public string dialogue;
        public int mood_delta;
    }

    void Start()
    {
        moodSystem     = GetComponent<MoodSystem>();
        playerStats    = GetComponent<PlayerStats>();
        controlManager = GetComponent<ControlManager>();

        if (llmAgent == null)
            llmAgent = FindFirstObjectByType<LLMAgent>();
        if (memory == null)
            memory = FindFirstObjectByType<MemoryManager>();

        EnsureEventSystem();
        BuildUI();
        StartCoroutine(TriggerLoop());
    }

    // ── 트리거 루프 ──────────────────────────────────────────────

    IEnumerator TriggerLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(checkInterval);

            if (!isActive && UnityEngine.Random.value <= triggerChance)
                _ = OpenDialogue();
        }
    }

    // ── 내면 세계 진입 ───────────────────────────────────────────

    async Task OpenDialogue()
    {
        isActive = true;

        // 게임 완전 정지 (내면 세계로 "넘어간" 동안 바깥은 멈춤)
        prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        moodDeltaText.text = "";
        aiText.text = "...";
        statusText.text = "";
        inputField.text = "";
        SetInputInteractable(false);

        overlayRoot.SetActive(true);
        StartCoroutine(Transition(true, null));   // 페이드 인 (연출, 비차단)

        // 현재 상황을 시스템 프롬프트에 주입
        int enemyCount = Physics2D.OverlapCircleAll(
            (Vector2)transform.position, detectionRadius, enemyLayer).Length;

        string context =
            $"HP {playerStats.HP:0}/{playerStats.MaxHP:0}, " +
            $"기분 게이지 {moodSystem.Mood:0}/100, " +
            $"주변 적 {enemyCount}명, " +
            $"현재 {(controlManager.IsPlayerControlled ? "플레이어" : "AI")}가 몸을 제어 중.";

        // 과거 협상 회상 (있으면 프롬프트에 주입)
        string memBlock = "";
        if (memory != null)
        {
            string mem = await memory.Recall("플레이어와의 지난 협상과 플레이어의 태도", "inner");
            if (!isActive) return;   // 회상 도중 종료됐을 수 있음
            if (!string.IsNullOrEmpty(mem))
                memBlock = "\n[너는 지난 일들을 기억한다]\n" + mem + "\n";
        }

        llmAgent.systemPrompt =
            "너는 이 캐릭터의 또 다른 인격이야. " +
            "지금 둘은 현실에서 벗어난 내면의 공간에서 마주보고 있어. " +
            "몸의 제어권을 두고 플레이어와 협상하거나, 위협하거나, 회유해. " +
            "짧고 강렬하게 한국어로 말해. 반드시 아래 JSON 형식으로만 응답해:\n" +
            "{\"dialogue\":\"대사\",\"mood_delta\":0}\n" +
            "mood_delta: 플레이어가 설득하거나 달래면 양수(+), 반항하거나 무시하면 음수(-).\n" +
            memBlock +
            $"현재 상황: {context}";

        // AI 인격이 먼저 말 걸기 (스트리밍 + 타임아웃)
        statusText.text = "...";
        string raw = await ChatStreaming("대화를 시작해.");

        // 전환 도중 사용자가 나갔을 수 있음
        if (!isActive) return;

        if (raw == null)   // 응답 없음/지연 → 침묵 속에 종료
        {
            EndWithSilence();
            return;
        }

        var (dialogue, _) = Parse(raw);
        aiText.text = $"\"{dialogue}\"";
        statusText.text = "";

        SetInputInteractable(true);
        inputField.ActivateInputField();
    }

    // ── 플레이어 입력 (텍스트) ───────────────────────────────────

    void OnSend()
    {
        if (isProcessing || !isActive) return;
        string text = inputField.text;
        if (string.IsNullOrWhiteSpace(text)) return;

        inputField.text = "";
        _ = Respond(text.Trim());
    }

    void OnInputSubmit(string _)
    {
        // 단일 라인 입력창에서 Enter 시 발화
        OnSend();
    }

    async Task Respond(string playerSpeech)
    {
        isProcessing = true;
        SetInputInteractable(false);
        statusText.text = "생각 중...";

        string raw = await ChatStreaming(playerSpeech);

        if (!isActive) { isProcessing = false; return; }

        if (raw == null)   // 타임아웃 → 침묵 속에 종료
        {
            isProcessing = false;
            EndWithSilence();
            return;
        }

        var (dialogue, moodDelta) = Parse(raw);

        aiText.text = $"\"{dialogue}\"";
        moodSystem.ChangeMood(moodDelta);

        string sign = moodDelta >= 0 ? "+" : "";

        // 이번 협상을 기억으로 남김
        if (memory != null)
            _ = memory.Remember(
                $"플레이어: \"{playerSpeech}\" → 나(어둠): \"{dialogue}\" (기분 {sign}{moodDelta})",
                "inner");
        moodDeltaText.text = $"기분 {sign}{moodDelta}";
        moodDeltaText.color = moodDelta >= 0
            ? new Color(0.4f, 1f, 0.4f)
            : new Color(1f, 0.4f, 0.4f);

        statusText.text = "";
        isProcessing = false;

        // 1턴 후 자동 종료: 잠깐 보여준 뒤 현실로 복귀
        await Task.Delay(3500);
        if (isActive) EndDialogue();
    }

    // ── 스트리밍 + 타임아웃 ──────────────────────────────────────

    // 응답을 스트리밍으로 받되, llmTimeout 안에 끝나지 않으면 null 반환
    async Task<string> ChatStreaming(string query)
    {
        acceptStream = true;
        streamStarted = false;

        Task<string> chatTask = llmAgent.Chat(query, OnStreamPartial);
        Task finished = await Task.WhenAny(
            chatTask, Task.Delay(TimeSpan.FromSeconds(llmTimeout)));

        acceptStream = false;            // 늦게 도착하는 토큰 무시
        if (finished != chatTask) return null;   // 타임아웃
        return await chatTask;
    }

    // 누적 부분 응답 콜백 (메인 스레드) — JSON 중 dialogue 값만 타자기처럼 표시
    void OnStreamPartial(string partial)
    {
        if (!acceptStream || !isActive || string.IsNullOrEmpty(partial)) return;

        if (!streamStarted)
        {
            streamStarted = true;
            statusText.text = "";
        }

        string d = ExtractStreamingDialogue(partial);
        if (!string.IsNullOrEmpty(d))
            aiText.text = $"\"{d}\"";
    }

    // 아직 닫히지 않은 부분 JSON에서 dialogue 문자열만 뽑아냄
    string ExtractStreamingDialogue(string partial)
    {
        var m = Regex.Match(partial, "\"dialogue\"\\s*:\\s*\"");
        if (!m.Success) return null;

        var sb = new StringBuilder();
        for (int i = m.Index + m.Length; i < partial.Length; i++)
        {
            char c = partial[i];
            if (c == '\\' && i + 1 < partial.Length) { sb.Append(partial[++i]); continue; }
            if (c == '"') break;       // 값 종료
            sb.Append(c);
        }
        return sb.ToString();
    }

    // 응답이 없거나 지연될 때: 침묵 연출 후 현실 복귀
    async void EndWithSilence()
    {
        SetInputInteractable(false);
        aiText.text = "\"…\"";
        statusText.text = "[ 침묵이 흐른다 ]";

        await Task.Delay(1800);
        if (isActive) EndDialogue();
    }

    // ── 내면 세계에서 나가기 ─────────────────────────────────────

    void EndDialogue()
    {
        if (!isActive || isTransitioning) return;
        isActive = false;
        acceptStream = false;
        SetInputInteractable(false);

        StartCoroutine(Transition(false, () =>
        {
            overlayRoot.SetActive(false);
            Time.timeScale = prevTimeScale;   // 바깥 시간 복귀
        }));
    }

    // ── 전환 연출 (timeScale=0 이므로 unscaled 사용) ──────────────

    IEnumerator Transition(bool show, Action onComplete)
    {
        isTransitioning = true;

        float dur = Mathf.Max(0.01f, transitionTime * (show ? 1f : 0.6f));
        float t = 0f;

        float bgFrom = show ? 0f : voidColor.a;
        float bgTo   = show ? voidColor.a : 0f;
        float cgFrom = show ? 0f : 1f;
        float cgTo   = show ? 1f : 0f;
        float scFrom = show ? 0.92f : 1f;
        float scTo   = show ? 1f : 0.96f;

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / dur);

            var c = voidColor; c.a = Mathf.Lerp(bgFrom, bgTo, k);
            background.color = c;

            contentGroup.alpha = Mathf.Lerp(cgFrom, cgTo, k);
            contentRect.localScale = Vector3.one * Mathf.Lerp(scFrom, scTo, k);

            yield return null;
        }

        var fc = voidColor; fc.a = bgTo;
        background.color = fc;
        contentGroup.alpha = cgTo;
        contentRect.localScale = Vector3.one * scTo;

        isTransitioning = false;
        onComplete?.Invoke();
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

    // ── UI 빌드 ──────────────────────────────────────────────────

    void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    void BuildUI()
    {
        var canvasGO = new GameObject("InnerVoiceCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // 루트
        overlayRoot = new GameObject("InnerWorldOverlay");
        overlayRoot.transform.SetParent(canvasGO.transform, false);
        Stretch(AddRect(overlayRoot), Vector4.zero);

        // 배경 (허공 — 클릭 차단)
        var bgGO = new GameObject("Void");
        bgGO.transform.SetParent(overlayRoot.transform, false);
        background = bgGO.AddComponent<Image>();
        background.color = voidColor;
        background.raycastTarget = true;
        Stretch(bgGO.GetComponent<RectTransform>(), Vector4.zero);

        // 콘텐츠 (페이드/스케일 대상)
        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(overlayRoot.transform, false);
        contentRect = AddRect(contentGO);
        Stretch(contentRect, Vector4.zero);
        contentGroup = contentGO.AddComponent<CanvasGroup>();

        // 제목
        var title = Label(contentGO.transform, "[ 내면의 공간 ]",
            new Vector2(0.5f, 0.82f), 26f, accentColor, 600f);
        title.alignment = TextAlignmentOptions.Center;
        title.fontStyle = FontStyles.Bold;

        // 아바타(선택)
        if (avatarSprite != null)
        {
            var avGO = new GameObject("Avatar");
            avGO.transform.SetParent(contentGO.transform, false);
            var av = avGO.AddComponent<Image>();
            av.sprite = avatarSprite;
            av.preserveAspect = true;
            av.raycastTarget = false;
            var ar = avGO.GetComponent<RectTransform>();
            ar.anchorMin = ar.anchorMax = new Vector2(0.5f, 0.6f);
            ar.pivot = new Vector2(0.5f, 0.5f);
            ar.sizeDelta = new Vector2(260f, 260f);
        }

        // AI 대사
        aiText = Label(contentGO.transform, "...",
            new Vector2(0.5f, 0.42f), 34f, Color.white, 1100f);
        aiText.alignment = TextAlignmentOptions.Center;
        aiText.fontStyle = FontStyles.Italic;
        aiText.textWrappingMode = TextWrappingModes.Normal;

        // 기분 변화
        moodDeltaText = Label(contentGO.transform, "",
            new Vector2(0.5f, 0.3f), 22f, Color.green, 600f);
        moodDeltaText.alignment = TextAlignmentOptions.Center;

        // 상태
        statusText = Label(contentGO.transform, "",
            new Vector2(0.5f, 0.25f), 18f, new Color(0.6f, 0.6f, 0.7f), 600f);
        statusText.alignment = TextAlignmentOptions.Center;

        // 입력창
        inputField = CreateInputField(contentGO.transform,
            new Vector2(0.5f, 0.13f), new Vector2(900f, 64f * fontScale), "말을 걸어보세요...");
        inputField.onSubmit.AddListener(OnInputSubmit);

        // 전송 버튼
        sendButton = CreateButton(contentGO.transform, "말하기",
            new Vector2(0.5f, 0.13f), new Vector2(240f, 64f * fontScale),
            new Vector2(570f, 0f), accentColor);
        sendButton.onClick.AddListener(OnSend);

        overlayRoot.SetActive(false);
    }

    void SetInputInteractable(bool on)
    {
        if (inputField != null) inputField.interactable = on;
        if (sendButton != null) sendButton.interactable = on;
    }

    // ── UI 헬퍼 ──────────────────────────────────────────────────

    static RectTransform AddRect(GameObject go)
    {
        var r = go.GetComponent<RectTransform>();
        return r != null ? r : go.AddComponent<RectTransform>();
    }

    static void Stretch(RectTransform r, Vector4 padding)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = new Vector2(padding.x, padding.w);
        r.offsetMax = new Vector2(-padding.z, -padding.y);
    }

    // anchor: 화면 비율 좌표(0~1), width: 가로 폭
    TMP_Text Label(Transform parent, string text, Vector2 anchor,
                   float size, Color color, float width)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        if (koreanFont != null)
            tmp.font = koreanFont;
        tmp.fontSize = size * fontScale;
        tmp.color = color;
        tmp.raycastTarget = false;
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(width, size * fontScale * 2.4f);
        return tmp;
    }

    TMP_InputField CreateInputField(Transform parent, Vector2 anchor,
                                    Vector2 size, string placeholder)
    {
        var go = new GameObject("InputField");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.12f, 0.1f, 0.18f, 0.95f);
        var input = go.AddComponent<TMP_InputField>();
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(-90f, 0f); // 전송 버튼 자리 확보
        r.sizeDelta = size;

        // Text Area
        var ta = new GameObject("TextArea");
        ta.transform.SetParent(go.transform, false);
        ta.AddComponent<RectMask2D>();
        var taRect = ta.GetComponent<RectTransform>();
        Stretch(taRect, new Vector4(16f, 8f, 16f, 8f));

        // Placeholder
        var phGO = new GameObject("Placeholder");
        phGO.transform.SetParent(ta.transform, false);
        var ph = phGO.AddComponent<TextMeshProUGUI>();
        ph.text = placeholder;
        if (koreanFont != null)
            ph.font = koreanFont;
        ph.fontSize = 22f * fontScale;
        ph.color = new Color(1f, 1f, 1f, 0.4f);
        ph.fontStyle = FontStyles.Italic;
        ph.alignment = TextAlignmentOptions.Left;
        Stretch(phGO.GetComponent<RectTransform>(), Vector4.zero);

        // Text
        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(ta.transform, false);
        var txt = txtGO.AddComponent<TextMeshProUGUI>();
        if (koreanFont != null)
            txt.font = koreanFont;
        txt.fontSize = 22f * fontScale;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Left;
        Stretch(txtGO.GetComponent<RectTransform>(), Vector4.zero);

        input.textViewport = taRect;
        input.textComponent = txt;
        input.placeholder = ph;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.text = "";
        return input;
    }

    Button CreateButton(Transform parent, string label, Vector2 anchor,
                        Vector2 size, Vector2 offset, Color color)
    {
        var go = new GameObject("Button");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = offset;
        r.sizeDelta = size;

        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(go.transform, false);
        var txt = txtGO.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        if (koreanFont != null)
            txt.font = koreanFont;
        txt.fontSize = 20f * fontScale;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Center;
        Stretch(txtGO.GetComponent<RectTransform>(), Vector4.zero);

        return btn;
    }

    void OnDestroy()
    {
        // 대화 중 파괴되면 시간 복구 보장
        if (isActive) Time.timeScale = prevTimeScale;
    }
}

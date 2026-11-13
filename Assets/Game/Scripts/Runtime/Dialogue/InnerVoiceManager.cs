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

/// <summary>내면 대화 응답 형식. Intent = 의도 분류(C, 게임 기본), Direct = 감정 변화량 직접 출력(A, 비교 실험).</summary>
public enum InnerOutputFormat { Intent, Direct }

public class InnerVoiceManager : MonoBehaviour
{
    [Header("Trigger")]
    [SerializeField] private float checkInterval = 30f;
    [SerializeField] private float triggerChance = 0.1f;
    // I 는 인벤토리 창이 쓴다.
    [SerializeField] private KeyCode testOpenKey = KeyCode.P;

    [Header("LLM (내면 전용 LLMAgent 별도 연결)")]
    [SerializeField] private LLMAgent llmAgent;
    [Tooltip("응답 글자가 이 시간(초) 동안 하나도 오지 않으면 끊고 침묵 속에 종료 (소프트락 방지). 느려도 글자가 나오는 동안은 기다린다.")]
    [SerializeField] private float llmTimeout = 20f;
    [Tooltip("내면 대사의 샘플링 온도. 낮으면 학습한 대사를 그대로 되풀이한다. 0.6 은 모델 비교 평가(docs/RESEARCH_EVIDENCE 7·8절)에 쓴 값. LLMAgent 를 NPC 와 같이 쓰므로 요청마다 이 값으로 맞춘다.")]
    [SerializeField, Range(0f, 2f)] private float temperature = 0.6f;
    [Tooltip("(A-3) 응답 파싱 실패 시 화면에 대신 띄울 어둠의 대사. 원문은 절대 노출하지 않는다.")]
    [SerializeField] private string parseFailFallbackLine = "…";

    [Header("인격 설정")]
    [Tooltip("내면 대화 한 번에 주고받는 턴 수. 다 쓰면 현실로 돌아간다.")]
    [SerializeField, Min(1)] private int maxTurns = 1;
    [Tooltip("주인공과 어둠의 배경. 시스템 프롬프트 맨 앞에 들어간다. (설정 A: 동료를 잃은 과거의 자신)")]
    // 예전 필드(backstory)의 프리팹 값이 남지 않도록 이름을 바꿨다
    [SerializeField, TextArea(3, 6)] private string pastSelfBackstory =
        "천마가 쳐들어온 밤, 주인공은 함께 싸우던 동료들을 모두 잃었다. 그 뒤로 주인공은 무너졌고, 너는 그날 밤에 멈춰 있는 그의 과거다. " +
        "너는 다시는 아무도 잃지 않으려고 적을 남김없이 베어 왔다. 지키려는 마음과 통제하려는 마음을 자주 혼동하지만, " +
        "플레이어가 보여 주는 행동과 말에 따라 그를 믿을 수도 있다.";

    [Tooltip("응답 형식. Intent(C): 의도·강도를 판단하고 규칙 표가 감정을 정한다(게임 기본). Direct(A): 감정 변화량을 직접 낸다(비교 실험).")]
    [SerializeField] private InnerOutputFormat outputFormat = InnerOutputFormat.Intent;

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

    private EmotionSystem emotions;
    private PlayerStats playerStats;
    // 이번 내면 대화(열고 닫을 때까지) 동안 누적된 감정 변화. 대화 한 번의 한도를 지킨다.
    private EmotionDelta sessionChange;
    private ControlManager controlManager;

    // 퀘스트가 강제로 열 때 인격에게 넘길 상황 설명 (1회용)
    private string pendingStoryContext = "";

    private bool isActive = false;
    private int turnsTaken = 0;
    private bool isProcessing = false;
    private bool isTransitioning = false;
    private bool acceptStream = false;
    private bool streamStarted = false;
    private float prevTimeScale = 1f;
    private float lastStreamProgress;
    private float requestStartedAt, firstTokenAt = -1f;
    /// <summary>마지막 요청에서 첫 글자가 오기까지 걸린 시간(초). 프롬프트를 읽는 시간이다. 글자가 안 왔으면 NaN.</summary>
    double FirstTokenSeconds => firstTokenAt < 0f ? double.NaN : firstTokenAt - requestStartedAt;

    static InnerVoiceManager current;
    /// <summary>내면 세계가 열려 있거나 열리고 닫히는 중인가. NPC 대화는 이때 열리지 않는다(LLMAgent 공용).</summary>
    public static bool IsOpen => current != null && (current.isActive || current.isTransitioning);

    // UI
    [SerializeField] private GameObject overlayRoot;
    [SerializeField] private Image background;
    [SerializeField] private CanvasGroup contentGroup;
    [SerializeField] private RectTransform contentRect;
    [SerializeField] private TMP_Text aiText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text moodDeltaText;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private Button sendButton, triggerButton;


    void Start()
    {
        current = this;
        emotions       = GetComponent<EmotionSystem>();
        playerStats    = GetComponent<PlayerStats>();
        controlManager = GetComponent<ControlManager>();

        if (llmAgent == null)
            llmAgent = FindFirstObjectByType<LLMAgent>();
        if (memory == null)
            memory = FindFirstObjectByType<MemoryManager>();

        // 공통 프리팹으로 붙은 씬(2~4지역 등)에는 구운 UI 가 없으므로 실행 중에 만든다
        if (overlayRoot == null)
        {
            if (koreanFont == null) koreanFont = Resources.Load<TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF");
            EnsureEventSystem();
            BuildUI();
            BuildTriggerButton();
        }
        overlayRoot.SetActive(false);
        inputField.onSubmit.AddListener(OnInputSubmit);
        sendButton.onClick.AddListener(OnSend);
        triggerButton.onClick.AddListener(() => { if (CanOpen()) _ = OpenDialogue(); });
        StartCoroutine(TriggerLoop());
    }

    void Update()
    {
        if (testOpenKey != KeyCode.None && Input.GetKeyDown(testOpenKey) && CanOpen())
            _ = OpenDialogue();
    }

    /// <summary>
    /// 지금 내면 세계를 열어도 되는가. NPC 대화창·의뢰 대화·선택지가 열려 있거나 NPC 응답을 기다리는 중이면 안 된다.
    /// LLMAgent 를 NPC 대화와 같이 쓰므로, 끼어들면 시스템 프롬프트가 덮여 NPC 가 엉뚱한 답을 한다.
    /// </summary>
    bool CanOpen() =>
        !isActive && !isTransitioning && !isProcessing &&
        !DialogueManager.IsDialogueOpen && !DialogueManager.IsRequestInFlight &&
        !(StoryChoiceManager.Instance != null && StoryChoiceManager.Instance.IsChoosing);

    bool OtherDialogueBusy() =>
        DialogueManager.IsDialogueOpen || DialogueManager.IsRequestInFlight ||
        (StoryChoiceManager.Instance != null && StoryChoiceManager.Instance.IsChoosing);

    // ── 트리거 루프 ──────────────────────────────────────────────

    /// <summary>
    /// 스토리(퀘스트)에서 내면 세계를 강제로 연다.
    /// storyContext 는 방금 무슨 일이 있었는지 — 인격이 이걸 물고 늘어진다.
    /// </summary>
    public void ForceOpen(string storyContext = "")
    {
        if (GetComponent<PlayerStats>() is PlayerStats stats && !stats.IsAlive) return;
        if (isActive || isTransitioning) return;
        pendingStoryContext = storyContext ?? "";

        // UI 생성(Start) 전에 퀘스트가 먼저 부르거나, 다른 대화가 진행 중이면 → 준비될 때까지 미룬다
        if (overlayRoot == null || llmAgent == null || OtherDialogueBusy())
        {
            StartCoroutine(OpenWhenReady());
            return;
        }

        _ = OpenDialogue();
    }

    IEnumerator OpenWhenReady()
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while ((overlayRoot == null || llmAgent == null) && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (overlayRoot == null || llmAgent == null)
        {
            Debug.LogWarning("[InnerVoice] 준비되지 않아 강제 진입을 건너뜁니다.");
            pendingStoryContext = "";
            yield break;
        }

        // 스토리 사건은 버리지 않고, 하던 대화가 끝나면 연다
        while (OtherDialogueBusy())
            yield return null;

        if (!isActive && !isTransitioning) _ = OpenDialogue();
    }

    IEnumerator TriggerLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(checkInterval);

            if (CanOpen() && UnityEngine.Random.value <= triggerChance)
                _ = OpenDialogue();
        }
    }

    // ── 내면 세계 진입 ───────────────────────────────────────────

    async Task OpenDialogue()
    {
        if (GetComponent<PlayerStats>() is PlayerStats stats && !stats.IsAlive) return;
        isActive = true;
        turnsTaken = 0;
        sessionChange = default;

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

        string context = SituationLine(playerStats.HP, playerStats.MaxHP, enemyCount, controlManager.IsPlayerControlled, EscortCompanion.Current != null) +
                         "\n" + EmotionLine(emotions.Anger, emotions.Revenge, emotions.Sorrow, emotions.Fear, emotions.Trust, emotions.Calm, emotions.Tone);

        // 과거 협상 회상 (있으면 프롬프트에 주입)
        string memBlock = "";
        if (memory != null)
        {
            string mem = await memory.Recall("플레이어와의 지난 협상과 플레이어의 태도", "inner");
            if (!isActive) return;   // 회상 도중 종료됐을 수 있음
            memBlock = MemoryBlock(mem);
        }

        string storyBlock = StoryBlock(pendingStoryContext);
        pendingStoryContext = "";   // 1회용

        llmAgent.systemPrompt = BuildPrompt(pastSelfBackstory, memBlock, storyBlock, context, outputFormat);

        // AI 인격이 먼저 말 걸기 (스트리밍 + 타임아웃)
        statusText.text = "...";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string raw = await ChatStreaming(OpeningQuery);
        var opening = LLMResponseParser.Parse(raw, parseFailFallbackLine);
        ResearchLog.Turn("inner_turn", "어둠(첫 마디)", watch.Elapsed.TotalSeconds, raw, opening, llmAgent.systemPrompt.Length, firstTokenSeconds: FirstTokenSeconds);

        // 전환 도중 사용자가 나갔을 수 있음
        if (!isActive) return;

        if (raw == null)   // 응답 없음/지연 → 침묵 속에 종료
        {
            EndWithSilence();
            return;
        }

        if (!opening.ParseOk)
            Debug.LogWarning($"[InnerVoice] 첫 대사 파싱 실패({opening.FailureReason}) raw={Truncate(raw, 300)}");
        aiText.text = $"\"{opening.Dialogue}\"";
        statusText.text = "";

        SetInputInteractable(true);
        inputField.ActivateInputField();
    }

    /// <summary>내면 대화의 시스템 프롬프트. 연구용 비교 실험도 게임과 같은 프롬프트를 쓰도록 따로 뺐다.</summary>
    public static string BuildPrompt(string backstory, string memBlock, string storyBlock, string context, InnerOutputFormat format) =>
        (string.IsNullOrWhiteSpace(backstory) ? "" : "[배경]\n" + backstory + "\n\n") +
        "너는 이 캐릭터의 또 다른 인격이야. 지금 둘은 현실에서 벗어난 내면의 공간에서 마주보고 있어. " +
        "몸을 누가 쥘지, 어떻게 싸울지를 두고 플레이어와 이야기해. 오래 알고 지낸 상대에게 하듯 짧고 차분한 한국어 반말로 1~3문장 말해. " +
        "무조건 칭찬하거나 반대하지 말고, 비웃음·복수 구호·몸을 빼앗겠다는 협박을 되풀이하지 마. 실제로 몸을 쥐는지는 게임이 정한다.\n" +
        FormatInstruction(format) +
        memBlock +
        storyBlock +
        $"현재 상황: {context}";

    /// <summary>출력 형식 지시. 판단을 먼저, 대사를 나중에 쓰게 한다.</summary>
    public static string FormatInstruction(InnerOutputFormat format) => format == InnerOutputFormat.Direct
        ? "먼저 플레이어의 마지막 말로 너의 감정이 얼마나 변하는지 정하고, 그 감정에 맞게 대답해. 반드시 아래 JSON 형식으로만 응답해:\n" +
          "{\"anger\":0,\"fear\":0,\"trust\":0,\"calm\":0,\"dialogue\":\"대사\"}\n" +
          "anger·fear·trust·calm: 이번 말로 변한 분노·두려움·신뢰·냉정의 양(-20~20). 첫 마디에서는 모두 0.\n"
        : "먼저 플레이어의 마지막 말이 어떤 의도인지 판단하고, 그 판단과 지금 가장 강한 감정에 맞게 대답해. 반드시 아래 JSON 형식으로만 응답해:\n" +
          "{\"intent\":\"neutral\",\"intensity\":1,\"dialogue\":\"대사\"}\n" +
          "intent: entrust(몸을 맡김) soothe(달램) reason(근거로 설득) promise(약속) defy(거부) insult(모욕) " +
          "despair(나약함) vengeance(복수 선동) ask(과거 질문) neutral(그 외, 첫 마디)\n" +
          "intensity: 말의 세기 1(약함)~3(강함).\n";

    // 프롬프트에 들어가는 조각들. 학습 데이터 생성기(TrainingPromptExport → Training/gen_data.py)도 같은 원문을 쓰도록 함수로 뺐다.

    /// <summary>인격이 먼저 말을 꺼내게 할 때 보내는 사용자 입력.</summary>
    public const string OpeningQuery = "대화를 시작해.";

    public static string SituationLine(float hp, float maxHp, int enemies, bool playerControlled, bool escorting) =>
        $"HP {hp:0}/{maxHp:0}, 주변 적 {enemies}명, 현재 {(playerControlled ? "플레이어" : "AI")}가 몸을 제어 중.{(escorting ? " 하연을 호위하는 중." : "")}";

    public static string EmotionLine(float anger, float revenge, float sorrow, float fear, float trust, float calm, Emotion tone) =>
        $"너의 감정: 분노 {anger:0}, 복수심 {revenge:0}, 슬픔 {sorrow:0}, 두려움 {fear:0}, 신뢰 {trust:0}, 냉정 {calm:0}. " +
        $"지금 가장 강한 감정은 {EmotionRules.Korean(tone)}이다.";

    /// <summary>recalled 는 MemoryManager.Recall 의 결과("- 기억" 줄들). 비어 있으면 블록도 없다.</summary>
    public static string MemoryBlock(string recalled) =>
        string.IsNullOrEmpty(recalled) ? "" : "\n[너는 지난 일들을 기억한다]\n" + recalled + "\n";

    public static string StoryBlock(string story) =>
        string.IsNullOrWhiteSpace(story) ? "" : "\n[방금 바깥에서 벌어진 일]\n" + story + "\n";

    /// <summary>협상 한 턴을 기억으로 남기는 문장. 나중에 MemoryBlock 으로 프롬프트에 돌아온다.</summary>
    /// <param name="judgement">"달램, 분노 -8 · 신뢰 +4" 처럼 판단과 감정 변화. 변화가 없으면 판단만.</param>
    public static string MemoryEntry(string playerSpeech, string dialogue, string judgement) =>
        $"플레이어: \"{playerSpeech}\" → 나(어둠): \"{dialogue}\" ({judgement})";

    /// <summary>기억 문장에 붙는 판단 요약.</summary>
    public static string Judgement(LLMResponseParser.Result parsed, EmotionDelta change)
    {
        string head = parsed.HasIntent ? EmotionRules.Korean(parsed.Intent) : "감정 직접 판단";
        string tail = change.ToKorean();
        return string.IsNullOrEmpty(tail) ? head : head + ", " + tail;
    }

    /// <summary>여러 턴 대화에서 앞 턴을 시스템 프롬프트 뒤에 붙이는 줄 (히스토리에 쌓지 않으므로).</summary>
    public static string TurnLine(int turn, string playerSpeech, string dialogue) =>
        $"\n[이번 대화 {turn}턴] 플레이어: \"{playerSpeech}\" → 너: \"{dialogue}\"";

    public string Backstory => pastSelfBackstory;
    public InnerOutputFormat OutputFormat => outputFormat;

    /// <summary>이 형식에 맞는 GBNF 문법.</summary>
    public static string GrammarFor(InnerOutputFormat format) =>
        format == InnerOutputFormat.Direct ? LLMResponseParser.InnerDirectGrammar : LLMResponseParser.InnerIntentGrammar;

    /// <summary>
    /// 응답 한 턴이 감정에 줄 변화. 의도 형식은 규칙 표(EmotionRules.ForIntent), 직접 형식은 모델이 낸 값.
    /// 파싱에 실패했으면 0 이다. 대화 한 번의 누적 한도는 호출부가 CapSession 으로 건다.
    /// </summary>
    public static EmotionDelta ChangeFor(LLMResponseParser.Result parsed, bool lowHp, bool escorting) =>
        !parsed.ParseOk ? default :
        parsed.HasIntent ? EmotionRules.ForIntent(parsed.Intent, parsed.Intensity, lowHp, escorting) :
        parsed.HasDirect ? parsed.Direct : default;

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

        var watch = System.Diagnostics.Stopwatch.StartNew();
        string raw = await ChatStreaming(playerSpeech);
        double seconds = watch.Elapsed.TotalSeconds;

        if (!isActive) { isProcessing = false; return; }

        if (raw == null)   // 타임아웃 → 침묵 속에 종료
        {
            ResearchLog.Turn("inner_turn", "어둠", seconds, null, default, llmAgent.systemPrompt.Length, firstTokenSeconds: FirstTokenSeconds);
            isProcessing = false;
            EndWithSilence();
            return;
        }

        // (A-3) 파싱 실패 시 원문을 화면에 노출하지 않는다. 어둠의 침묵으로 대신한다.
        var parsed = LLMResponseParser.Parse(raw, parseFailFallbackLine);
        string dialogue = parsed.Dialogue;

        // (A-2) 의도 형식은 규칙 표가, 직접 형식은 ±20 으로 자른 값이 감정을 바꾼다.
        //       대화 한 번의 누적 한도(EmotionRules.DialogueSessionCap)로 오판 한두 번에 몸이 넘어가는 일을 막는다.
        bool lowHp = playerStats.MaxHP > 0f && playerStats.HP / playerStats.MaxHP <= 0.3f;
        var change = EmotionRules.CapSession(ChangeFor(parsed, lowHp, EscortCompanion.Current != null), sessionChange);
        sessionChange += change;

        aiText.text = $"\"{dialogue}\"";
        emotions.ApplySilently(change);
        ResearchLog.Turn("inner_turn", "어둠", seconds, raw, parsed, llmAgent.systemPrompt.Length,
                         firstTokenSeconds: FirstTokenSeconds, applied: change);   // 감정 열은 반영 후 값으로 남는다
        emotions.Notify();

        if (!parsed.ParseOk)
            Debug.LogWarning($"[InnerVoice] JSON 파싱 실패({parsed.FailureReason}) raw={Truncate(raw, 300)}");
        else if (!string.IsNullOrEmpty(parsed.FailureReason))
            Debug.LogWarning($"[InnerVoice] 응답 일부를 복구함({parsed.FailureReason}) raw={Truncate(raw, 300)}");

        // 이번 협상을 기억으로 남김. 파싱 실패한 턴은 폴백 대사가 사실로 굳으므로 기록하지 않는다.
        if (memory != null && parsed.ParseOk)
            _ = memory.Remember(MemoryEntry(playerSpeech, dialogue, Judgement(parsed, change)), "inner");
        string changeText = change.ToKorean();
        moodDeltaText.text = string.IsNullOrEmpty(changeText) ? "감정 변화 없음" : changeText;
        moodDeltaText.color = accentColor;

        statusText.text = "";
        isProcessing = false;

        // 정한 턴 수를 다 쓰기 전에는 다음 말을 받는다
        if (++turnsTaken < maxTurns)
        {
            // 히스토리에 쌓지 않으므로(ChatStreaming 참고) 이번 대화의 앞 턴은 프롬프트에 직접 붙인다
            llmAgent.systemPrompt += TurnLine(turnsTaken, playerSpeech, dialogue);
            SetInputInteractable(true);
            inputField.ActivateInputField();
            return;
        }

        // 마지막 턴: 잠깐 보여준 뒤 현실로 복귀
        await Task.Delay(3500);
        if (isActive) EndDialogue();
    }

    // ── 스트리밍 + 타임아웃 ──────────────────────────────────────

    // 응답을 스트리밍으로 받되, llmTimeout 초 동안 한 글자도 오지 않으면 끊고 null 반환 (호출부가 침묵 속에 종료)
    async Task<string> ChatStreaming(string query)
    {
        acceptStream = true;
        streamStarted = false;
        lastStreamProgress = requestStartedAt = Time.realtimeSinceStartup;
        firstTokenAt = -1f;
        llmAgent.temperature = temperature;
        LLMServices.UseGrammar(llmAgent, GrammarFor(outputFormat));   // NPC 대화와 문법이 다르다

        // 내면 협상은 매번 새 상황이고, 장기 기억은 RAG(MemoryManager)가 담당하므로
        // 대화 히스토리에 누적하지 않는다 (프롬프트가 길어져 점점 느려지는 것 방지 + 대장장이와의 맥락 오염 차단)
        Task<string> chatTask = llmAgent.Chat(query, OnStreamPartial, addToHistory: false);

        // 전체 시간이 아니라 '진행 없음' 기준이다. CPU 추론이 느려도 글자가 나오는 동안은 기다리고,
        // 모델이 멈췄을 때만 끊는다. 바깥 시간이 멈춰 있으므로(timeScale 0) 실제 시간으로 잰다.
        while (!chatTask.IsCompleted)
        {
            if (this == null) return null;
            if (Time.realtimeSinceStartup - lastStreamProgress > llmTimeout)
            {
                acceptStream = false;
                llmAgent.CancelRequests();   // 붙잡힌 요청이 다음 대화의 슬롯을 막지 않게
                Debug.LogWarning($"[InnerVoice] {llmTimeout:0}초 동안 응답이 없어 끊었습니다.");
                return null;
            }
            await Task.Delay(200);
        }

        acceptStream = false;
        return await chatTask;
    }

    // 누적 부분 응답 콜백 (메인 스레드) — JSON 중 dialogue 값만 타자기처럼 표시
    void OnStreamPartial(string partial)
    {
        lastStreamProgress = Time.realtimeSinceStartup;
        if (firstTokenAt < 0f && !string.IsNullOrEmpty(partial)) firstTokenAt = lastStreamProgress;
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

    public void CloseForDeath()
    {
        if (!isActive && !isTransitioning) return;
        StopAllCoroutines();
        isActive = isTransitioning = isProcessing = acceptStream = false;
        if (overlayRoot != null) overlayRoot.SetActive(false);
        Time.timeScale = prevTimeScale;
        StartCoroutine(TriggerLoop());
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
    // 실제 파싱은 DialogueManager와 공유하는 LLMResponseParser에 있다.

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
        return text.Substring(0, max) + "…";
    }

    // ── UI 빌드 ──────────────────────────────────────────────────

    void EnsureEventSystem()
    {
        var existing = FindFirstObjectByType<EventSystem>();
        if (existing == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            return;
        }

        // 새 Input System 환경에서 옛 StandaloneInputModule 이면 입력이 안 먹으므로 교체
        if (existing.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
        {
            var old = existing.GetComponent<StandaloneInputModule>();
            if (old != null) Destroy(old);
            existing.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
    }

#if UNITY_EDITOR
    public void BakeSceneUI()
    {
        if (overlayRoot != null) return;
        if (koreanFont == null) koreanFont = Resources.Load<TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF");
        BuildUI(); BuildTriggerButton();
    }
#endif
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

        // 전송 버튼
        sendButton = CreateButton(contentGO.transform, "말하기",
            new Vector2(0.5f, 0.13f), new Vector2(240f, 64f * fontScale),
            new Vector2(570f, 0f), accentColor);

        overlayRoot.SetActive(false);
    }

    void SetInputInteractable(bool on)
    {
        if (inputField != null) inputField.interactable = on;
        if (sendButton != null) sendButton.interactable = on;
    }

    // 화면에 항상 떠 있는 '내면 대화' 수동 트리거 버튼 (우하단)
    void BuildTriggerButton()
    {
        var canvasGO = new GameObject("InnerVoiceTriggerCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;   // 내면 오버레이(300)보다 아래 -> 대화 중엔 가려져 중복 방지
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        var btn = CreateButton(canvasGO.transform, "내면 대화",
            new Vector2(1f, 0f),          // 우하단 기준
            new Vector2(180f, 64f),
            new Vector2(-110f, 70f),      // 가장자리에서 안쪽으로
            accentColor);
        triggerButton = btn;
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
        if (current == this) current = null;
    }
}

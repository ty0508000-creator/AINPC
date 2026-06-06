using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using LLMUnity;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

public class DialogueManager : MonoBehaviour
{
    public static bool IsDialogueOpen { get; private set; }

    [Header("UI References")]
    public GameObject dialoguePanel;
    public TMP_Text npcText;
    public TMP_InputField playerInputField;
    public Button sendButton;

    [Header("Runtime UI Style")]
    [SerializeField] private TMP_FontAsset runtimeFont;

    [Header("LLM References")]
    [SerializeField] private LLMAgent llmAgent;
    [Tooltip("이 시간(초) 안에 응답이 없으면 대화를 마무리 (멈춤 방지)")]
    [SerializeField] private float llmTimeout = 20f;

    [Header("Memory (선택 — 비우면 자동 탐색)")]
    [SerializeField] private MemoryManager memory;

    [Header("어둠 제어 중 NPC 공포 반응")]
    [SerializeField] private string[] fearLines =
    {
        "히익...! 그 눈빛은 뭐야...",
        "오, 오지 마! 너... 너 뭔가 이상해.",
        "저리 가... 제발...",
    };

    private string currentNPCName;
    private string baseSystemPrompt;
    private bool isProcessing = false;
    private bool acceptStream = false;
    private bool blocked = false;
    private MoodSystem moodSystem;
    private ControlManager controlManager;

    [System.Serializable]
    private class NPCResponseData
    {
        public string dialogue;
        public int mood_delta;
    }

    void Start()
    {
        EnsureUI();

        if (dialoguePanel == null || npcText == null || playerInputField == null || sendButton == null)
        {
            Debug.LogError("DialogueManager: UI 요소들이 할당되지 않았습니다!");
            return;
        }

        dialoguePanel.SetActive(false);
        ApplyRuntimeFont();
        sendButton.onClick.RemoveListener(OnSendButtonClick);
        sendButton.onClick.AddListener(OnSendButtonClick);

        if (llmAgent == null)
            llmAgent = FindFirstObjectByType<LLMAgent>();

        moodSystem = FindFirstObjectByType<MoodSystem>();
        if (moodSystem == null)
            Debug.LogWarning("DialogueManager: MoodSystem을 찾을 수 없습니다.");

        controlManager = FindFirstObjectByType<ControlManager>();

        if (memory == null)
            memory = FindFirstObjectByType<MemoryManager>();
    }

    void Update()
    {
        if (IsDialogueOpen && Input.GetKeyDown(KeyCode.Escape))
            CloseDialogue();
    }

    public void OpenDialogue(string name, string personality)
    {
        EnsureUI();
        ResolveReferences();

        currentNPCName = name;
        IsDialogueOpen = true;
        dialoguePanel.SetActive(true);

        // 어둠이 몸을 제어 중이면 NPC는 대화 대신 공포에 질린다 (실제 대가)
        if (controlManager != null && !controlManager.IsPlayerControlled)
        {
            blocked = true;
            string fear = (fearLines != null && fearLines.Length > 0)
                ? fearLines[UnityEngine.Random.Range(0, fearLines.Length)]
                : "...";
            npcText.text = $"{name}: {fear}";

            // 이 사건을 NPC가 기억한다 → 나중에 정상 복귀해도 경계함
            if (memory != null)
                _ = memory.Remember(
                    $"[어둠 출현] 플레이어 안의 어둠이 드러났을 때, 나({name})는 두려워 떨며 대화를 거부했다.",
                    name);
            return;
        }

        if (llmAgent == null)
        {
            npcText.text = $"{name}: ...";
            Debug.LogError("DialogueManager: LLMAgent를 찾을 수 없습니다.");
            return;
        }

        blocked = false;
        baseSystemPrompt =
            $"당신은 {name}입니다. {personality}\n\n" +
            "반드시 다음 JSON 형식으로만 응답하세요 (다른 텍스트 없이):\n" +
            "{\"dialogue\":\"대사 내용\",\"mood_delta\":0}\n\n" +
            "mood_delta 규칙 (-20 ~ +20):\n" +
            "- 플레이어가 무례하거나 불쾌한 말 → -15 ~ -20\n" +
            "- 어색하거나 의미없는 말 → -5 ~ -10\n" +
            "- 평범한 대화 → -3 ~ +3\n" +
            "- 호감가는 말이나 칭찬 → +10 ~ +20";
        llmAgent.systemPrompt = baseSystemPrompt;

        npcText.text = $"{name}: 안녕.";
        playerInputField.ActivateInputField();

        _ = PreloadLLMAgent();
    }

    private async Task PreloadLLMAgent()
    {
        int maxRetries = 60;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                await llmAgent.Chat("", addToHistory: false);
                return;
            }
            catch (System.Exception ex) when (ex.Message.Contains("LLM caller not initialized"))
            {
                await Task.Delay(500);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"LLM 초기화 중 예외: {ex.Message}");
                break;
            }
        }
    }

    void OnSendButtonClick()
    {
        if (!blocked && !string.IsNullOrEmpty(playerInputField.text) && !isProcessing)
        {
            string question = playerInputField.text;
            playerInputField.text = "";
            _ = AskNPC(question);
        }
    }

    private async Task AskNPC(string question)
    {
        isProcessing = true;
        npcText.text = "생각 중...";
        sendButton.interactable = false;

        try
        {
            // 이 질문과 관련된 과거 기억을 회상해 프롬프트에 주입
            if (memory != null)
            {
                string mem = await memory.Recall(question, currentNPCName);
                llmAgent.systemPrompt = string.IsNullOrEmpty(mem)
                    ? baseSystemPrompt
                    : baseSystemPrompt + "\n\n[기억하는 과거 대화]\n" + mem;
            }

            string raw = await ChatStreaming(question);

            if (raw == null)
            {
                npcText.text = $"{currentNPCName}: ...";   // 타임아웃
            }
            else if (!string.IsNullOrEmpty(raw))
            {
                var (dialogue, moodDelta) = ParseResponse(raw);
                npcText.text = $"{currentNPCName}: {dialogue}";
                moodSystem?.ChangeMood(moodDelta);

                // 이번 대화를 기억으로 남김
                if (memory != null)
                    _ = memory.Remember($"플레이어: \"{question}\" / {currentNPCName}: \"{dialogue}\"", currentNPCName);
            }
            else
            {
                npcText.text = "응답을 받지 못했습니다.";
            }
        }
        catch (System.Exception ex)
        {
            npcText.text = $"오류: {ex.Message}";
            Debug.LogError($"Chat 오류: {ex.Message}");
        }
        finally
        {
            isProcessing = false;
            sendButton.interactable = true;
        }
    }

    private (string dialogue, int moodDelta) ParseResponse(string raw)
    {
        // JSON 블록 추출 시도
        int start = raw.IndexOf('{');
        int end = raw.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                string json = raw.Substring(start, end - start + 1);
                var data = JsonUtility.FromJson<NPCResponseData>(json);
                if (!string.IsNullOrEmpty(data.dialogue))
                    return (data.dialogue, data.mood_delta);
            }
            catch { }
        }

        // Regex 폴백
        var dMatch = Regex.Match(raw, "\"dialogue\"\\s*:\\s*\"([^\"]+)\"");
        var mMatch = Regex.Match(raw, "\"mood_delta\"\\s*:\\s*(-?\\d+)");

        string dialogue = dMatch.Success ? dMatch.Groups[1].Value : raw;
        int mood = mMatch.Success ? int.Parse(mMatch.Groups[1].Value) : 0;

        return (dialogue, mood);
    }

    private async Task<string> ChatWithRetry(string question)
    {
        int maxRetries = 3;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return await llmAgent.Chat(question, OnStreamPartial, addToHistory: true);
            }
            catch (System.Exception ex) when (ex.Message.Contains("LLM caller not initialized") && i < maxRetries - 1)
            {
                await Task.Delay(2000);
            }
        }
        return await llmAgent.Chat(question, OnStreamPartial, addToHistory: true);
    }

    // 응답을 스트리밍으로 받되, llmTimeout 안에 끝나지 않으면 null 반환
    private async Task<string> ChatStreaming(string question)
    {
        acceptStream = true;

        Task<string> chatTask = ChatWithRetry(question);
        Task finished = await Task.WhenAny(
            chatTask, Task.Delay(TimeSpan.FromSeconds(llmTimeout)));

        acceptStream = false;            // 늦게 도착하는 토큰 무시
        if (finished != chatTask) return null;   // 타임아웃
        return await chatTask;
    }

    // 누적 부분 응답 콜백 (메인 스레드) — JSON 중 dialogue 값만 타자기처럼 표시
    private void OnStreamPartial(string partial)
    {
        if (!acceptStream || string.IsNullOrEmpty(partial)) return;

        string d = ExtractStreamingDialogue(partial);
        if (!string.IsNullOrEmpty(d))
            npcText.text = $"{currentNPCName}: {d}";
    }

    // 아직 닫히지 않은 부분 JSON에서 dialogue 문자열만 뽑아냄
    private string ExtractStreamingDialogue(string partial)
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

    public void CloseDialogue()
    {
        IsDialogueOpen = false;
        acceptStream = false;
        isProcessing = false;
        blocked = false;
        if (dialoguePanel != null) dialoguePanel.SetActive(false);
    }

    void OnDestroy()
    {
        IsDialogueOpen = false;
    }

    private void EnsureUI()
    {
        if (dialoguePanel != null && npcText != null && playerInputField != null && sendButton != null)
            return;

        var canvasGO = new GameObject("DialogueCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        dialoguePanel = new GameObject("DialoguePanel");
        dialoguePanel.transform.SetParent(canvasGO.transform, false);
        var panelImage = dialoguePanel.AddComponent<Image>();
        panelImage.color = new Color(0.05f, 0.05f, 0.07f, 0.92f);
        var panelRect = dialoguePanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 36f);
        panelRect.sizeDelta = new Vector2(1100f, 300f);

        npcText = CreateText(dialoguePanel.transform, "NPCText", new Vector2(0.5f, 0.66f),
            new Vector2(980f, 136f), 36f, TextAlignmentOptions.Left);
        npcText.text = "";

        playerInputField = CreateInput(dialoguePanel.transform);
        sendButton = CreateButton(dialoguePanel.transform, "SendButton", "말하기",
            new Vector2(0.84f, 0.22f), new Vector2(160f, 66f));

        var closeButton = CreateButton(dialoguePanel.transform, "CloseButton", "닫기",
            new Vector2(0.94f, 0.86f), new Vector2(96f, 50f));
        closeButton.onClick.AddListener(CloseDialogue);
    }

    private void ResolveReferences()
    {
        if (llmAgent == null)
            llmAgent = FindFirstObjectByType<LLMAgent>();

        if (moodSystem == null)
            moodSystem = FindFirstObjectByType<MoodSystem>();

        if (controlManager == null)
            controlManager = FindFirstObjectByType<ControlManager>();

        if (memory == null)
            memory = FindFirstObjectByType<MemoryManager>();
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;

        var eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<StandaloneInputModule>();
    }

    private TMP_Text CreateText(Transform parent, string name, Vector2 anchor, Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        if (runtimeFont != null) text.font = runtimeFont;
        text.color = Color.white;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        return text;
    }

    private TMP_InputField CreateInput(Transform parent)
    {
        var go = new GameObject("PlayerInputField");
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = new Color(0.12f, 0.12f, 0.16f, 1f);
        var input = go.AddComponent<TMP_InputField>();
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.42f, 0.22f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(720f, 66f);

        var textArea = new GameObject("TextArea");
        textArea.transform.SetParent(go.transform, false);
        textArea.AddComponent<RectMask2D>();
        var textAreaRect = textArea.GetComponent<RectTransform>();
        Stretch(textAreaRect, new Vector4(16f, 8f, 16f, 8f));

        var placeholder = CreateChildText(textArea.transform, "Placeholder", "말을 입력하세요...", 28f, new Color(1f, 1f, 1f, 0.42f));
        placeholder.fontStyle = FontStyles.Italic;

        var text = CreateChildText(textArea.transform, "Text", "", 28f, Color.white);

        input.textViewport = textAreaRect;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.onSubmit.AddListener(_ => OnSendButtonClick());
        return input;
    }

    private TMP_Text CreateChildText(Transform parent, string name, string value, float fontSize, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = fontSize;
        if (runtimeFont != null) text.font = runtimeFont;
        text.color = color;
        text.alignment = TextAlignmentOptions.Left;
        Stretch(go.GetComponent<RectTransform>(), Vector4.zero);
        return text;
    }

    private Button CreateButton(Transform parent, string name, string label, Vector2 anchor, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = new Color(0.32f, 0.34f, 0.42f, 1f);
        var button = go.AddComponent<Button>();
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;

        var text = CreateChildText(go.transform, "Text", label, 26f, Color.white);
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private void ApplyRuntimeFont()
    {
        if (runtimeFont == null) return;

        if (npcText != null) npcText.font = runtimeFont;
        if (playerInputField != null)
        {
            if (playerInputField.textComponent != null)
                playerInputField.textComponent.font = runtimeFont;
            if (playerInputField.placeholder is TMP_Text placeholder)
                placeholder.font = runtimeFont;
        }

        if (sendButton != null)
        {
            TMP_Text buttonText = sendButton.GetComponentInChildren<TMP_Text>();
            if (buttonText != null) buttonText.font = runtimeFont;
        }
    }

    private static void Stretch(RectTransform rect, Vector4 padding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(padding.x, padding.w);
        rect.offsetMax = new Vector2(-padding.z, -padding.y);
    }
}

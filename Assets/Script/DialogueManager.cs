using UnityEngine;
using TMPro;
using UnityEngine.UI;
using LLMUnity;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

public class DialogueManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject dialoguePanel;
    public TMP_Text npcText;
    public TMP_InputField playerInputField;
    public Button sendButton;

    [Header("LLM References")]
    [SerializeField] private LLMAgent llmAgent;

    private string currentNPCName;
    private bool isProcessing = false;
    private MoodSystem moodSystem;

    [System.Serializable]
    private class NPCResponseData
    {
        public string dialogue;
        public int mood_delta;
    }

    void Start()
    {
        if (dialoguePanel == null || npcText == null || playerInputField == null || sendButton == null)
        {
            Debug.LogError("DialogueManager: UI 요소들이 할당되지 않았습니다!");
            return;
        }

        dialoguePanel.SetActive(false);
        sendButton.onClick.AddListener(OnSendButtonClick);

        if (llmAgent == null)
            llmAgent = FindObjectOfType<LLMAgent>();

        moodSystem = FindObjectOfType<MoodSystem>();
        if (moodSystem == null)
            Debug.LogWarning("DialogueManager: MoodSystem을 찾을 수 없습니다.");
    }

    public void OpenDialogue(string name, string personality)
    {
        if (llmAgent == null) return;

        currentNPCName = name;
        llmAgent.systemPrompt =
            $"당신은 {name}입니다. {personality}\n\n" +
            "반드시 다음 JSON 형식으로만 응답하세요 (다른 텍스트 없이):\n" +
            "{\"dialogue\":\"대사 내용\",\"mood_delta\":0}\n\n" +
            "mood_delta 규칙 (-20 ~ +20):\n" +
            "- 플레이어가 무례하거나 불쾌한 말 → -15 ~ -20\n" +
            "- 어색하거나 의미없는 말 → -5 ~ -10\n" +
            "- 평범한 대화 → -3 ~ +3\n" +
            "- 호감가는 말이나 칭찬 → +10 ~ +20";

        npcText.text = $"{name}: 안녕.";
        dialoguePanel.SetActive(true);
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
        if (!string.IsNullOrEmpty(playerInputField.text) && !isProcessing)
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
            string raw = await ChatWithRetry(question);

            if (!string.IsNullOrEmpty(raw))
            {
                var (dialogue, moodDelta) = ParseResponse(raw);
                npcText.text = $"{currentNPCName}: {dialogue}";
                moodSystem?.ChangeMood(moodDelta);
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
                return await llmAgent.Chat(question, addToHistory: true);
            }
            catch (System.Exception ex) when (ex.Message.Contains("LLM caller not initialized") && i < maxRetries - 1)
            {
                await Task.Delay(2000);
            }
        }
        return await llmAgent.Chat(question, addToHistory: true);
    }

    public void CloseDialogue()
    {
        dialoguePanel.SetActive(false);
    }
}

using UnityEngine;
using TMPro;
using UnityEngine.UI;
using LLMUnity;
using System.Threading.Tasks;

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

    void Start()
    {
        // 필수 요소 확인
        if (dialoguePanel == null || npcText == null || playerInputField == null || sendButton == null)
        {
            Debug.LogError("DialogueManager: UI 요소들이 할당되지 않았습니다!");
            return;
        }

        dialoguePanel.SetActive(false);
        sendButton.onClick.AddListener(OnSendButtonClick);

        // LLMAgent 자동 찾기
        if (llmAgent == null)
        {
            llmAgent = FindObjectOfType<LLMAgent>();
            if (llmAgent == null)
            {
                Debug.LogError("LLMAgent를 찾을 수 없습니다!");
            }
        }
    }

    /// <summary>
    /// NPC와의 대화 시작
    /// </summary>
    public void OpenDialogue(string name, string personality)
    {
        if (llmAgent == null)
        {
            Debug.LogError("LLMAgent가 없습니다!");
            return;
        }

        currentNPCName = name;
        llmAgent.systemPrompt = $"당신은 {name}입니다. {personality}";

        npcText.text = $"{name}: 안녕.";
        dialoguePanel.SetActive(true);
        playerInputField.ActivateInputField();

        _ = PreloadLLMAgent();
    }

    /// <summary>
    /// LLMAgent를 백그라운드에서 미리 준비
    /// </summary>
    private async Task PreloadLLMAgent()
    {
        int maxRetries = 60;
        int retryDelay = 500;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                await llmAgent.Chat("", addToHistory: false);
                return;
            }
            catch (System.Exception ex) when (ex.Message.Contains("LLM caller not initialized"))
            {
                await Task.Delay(retryDelay);
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

    /// <summary>
    /// NPC에게 질문
    /// </summary>
    private async Task AskNPC(string question)
    {
        isProcessing = true;
        npcText.text = "생각 중...";
        sendButton.interactable = false;

        try
        {
            if (llmAgent == null)
            {
                npcText.text = "오류: LLMAgent가 없습니다.";
                return;
            }

            string response = await ChatWithRetry(question);

            if (!string.IsNullOrEmpty(response))
            {
                npcText.text = $"{currentNPCName}: {response}";
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

    /// <summary>
    /// Chat 호출 (재시도 포함)
    /// </summary>
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
            catch (System.Exception ex)
            {
                Debug.LogError($"Chat 요청 실패: {ex.Message}");
                throw;
            }
        }

        return await llmAgent.Chat(question, addToHistory: true);
    }

    /// <summary>
    /// 대화 종료
    /// </summary>
    public void CloseDialogue()
    {
        dialoguePanel.SetActive(false);
    }
}

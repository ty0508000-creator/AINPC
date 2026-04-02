using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;

public class DialogueManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject dialoguePanel;
    public TMP_Text npcText;
    public TMP_InputField playerInputField;
    public Button sendButton;

    private string currentNPCName;
    private string currentNPCPersonality;

    void Start()
    {
        dialoguePanel.SetActive(false); // 시작할 때 대화창 끄기
        sendButton.onClick.AddListener(OnSendButtonClick);
    }

    public void OpenDialogue(string name, string personality)
    {
        currentNPCName = name;
        currentNPCPersonality = personality;
        npcText.text = $"{name}: 안녕! 궁금한 게 있니?";
        dialoguePanel.SetActive(true);
        playerInputField.ActivateInputField(); // 바로 입력 가능하게 포커스
    }

    void OnSendButtonClick()
    {
        if (!string.IsNullOrEmpty(playerInputField.text))
        {
            StartCoroutine(AskLlama(playerInputField.text));
            playerInputField.text = ""; // 입력창 초기화
        }
    }

    IEnumerator AskLlama(string question)
    {
        npcText.text = "생각 중...";
        sendButton.interactable = false;

        // Llama 3에게 보낼 프롬프트 구성
        string fullPrompt = $"성격: {currentNPCPersonality}\n사용자: {question}\n{currentNPCName}:";

        // JSON 데이터 생성
        string json = "{\"model\": \"llama3:latest\", \"prompt\": \"" + fullPrompt + "\", \"stream\": false}";

        using (UnityWebRequest request = new UnityWebRequest("http://localhost:11434/api/generate", "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                // 응답에서 "response" 필드만 추출 (간단한 파싱)
                string result = request.downloadHandler.text;
                LlamaResponseData data = JsonUtility.FromJson<LlamaResponseData>(result);
                npcText.text = $"{currentNPCName}: {data.response}";
            }
            else
            {
                npcText.text = "오류: 서버가 켜져 있는지 확인해줘!";
            }
        }
        sendButton.interactable = true;
    }
}

[System.Serializable]
public class LlamaResponseData { public string response; }

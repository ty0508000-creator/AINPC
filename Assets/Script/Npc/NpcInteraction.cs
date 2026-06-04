using UnityEngine;

public class NPCInteraction : MonoBehaviour
{
    public string npcName = "대장장이";
    [TextArea(3, 5)]
    public string personality = "너는 무뚝뚝한 대장장이이고, 항상 짧은 한국어로 대답해.";

    private DialogueManager dialogueManager;
    private bool isDialogueOpen = false;

    void Start()
    {
        dialogueManager = FindFirstObjectByType<DialogueManager>();

        if (dialogueManager == null)
        {
            Debug.LogError($"{gameObject.name}: DialogueManager를 찾을 수 없습니다.");
        }

        Collider2D collider = GetComponent<Collider2D>();
        if (collider == null)
        {
            Debug.LogError($"{gameObject.name}: Collider2D가 없습니다!");
        }
        else if (!collider.isTrigger)
        {
            Debug.LogError($"{gameObject.name}: Collider2D가 Trigger로 설정되지 않았습니다!");
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            if (dialogueManager != null)
            {
                dialogueManager.OpenDialogue(npcName, personality);
                isDialogueOpen = true;
            }
            else
            {
                Debug.LogError("DialogueManager가 초기화되지 않았습니다.");
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player") && isDialogueOpen)
        {
            if (dialogueManager != null)
            {
                dialogueManager.CloseDialogue();
                isDialogueOpen = false;
            }
        }
    }
}
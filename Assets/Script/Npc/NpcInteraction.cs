using UnityEngine;

public class NPCInteraction : MonoBehaviour
{
    public string npcName = "대장장이";
    [TextArea(3, 5)]
    public string personality = "너는 무뚝뚝한 대장장이이고, 항상 짧은 한국어로 대답해.";

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            FindObjectOfType<DialogueManager>().OpenDialogue(npcName, personality);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            FindObjectOfType<DialogueManager>().dialoguePanel.SetActive(false);
        }
    }
}
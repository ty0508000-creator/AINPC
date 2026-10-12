using System.Collections.Generic;
using UnityEngine;

public class NPCInteraction : MonoBehaviour
{
    public string npcName = "대장장이";
    [TextArea(3, 5)]
    public string personality = "너는 무뚝뚝한 대장장이이고, 항상 짧은 한국어로 대답해.";

    [Header("호감도 (플레이어의 감정 수치와 별개)")]
    [Tooltip("저장이 없을 때의 시작 호감도 (0~100)")]
    [SerializeField, Range(0f, 100f)] private float startAffinity = 50f;

    /// <summary>이 NPC 가 플레이어를 얼마나 좋게 보는지 (0~100). 대화의 LLM 판정으로 오르내린다.</summary>
    public float Affinity { get; private set; }

    private DialogueManager dialogueManager;
    private bool isDialogueOpen = false;

    void Awake()
    {
        Affinity = startAffinity;
        var saved = SaveSystem.LoadPlayer();
        if (saved?.npcAffinity == null) return;
        foreach (var entry in saved.npcAffinity)
            if (entry != null && entry.npcName == npcName && float.IsFinite(entry.value))
                Affinity = Mathf.Clamp(entry.value, 0f, 100f);
    }

    public void ChangeAffinity(float delta)
    {
        if (!float.IsFinite(delta)) return;
        Affinity = Mathf.Clamp(Affinity + delta, 0f, 100f);
    }

    /// <summary>
    /// 저장용 호감도 목록. 이전 저장 목록에 이 씬 NPC 값을 덮어쓴다.
    /// NPC 가 없는 씬(2~4지역 등)에서 저장해도 다른 NPC 의 호감도는 남는다.
    /// </summary>
    public static List<NpcAffinityEntry> CaptureAffinity(List<NpcAffinityEntry> previous)
    {
        var values = new Dictionary<string, float>();
        if (previous != null)
            foreach (var entry in previous)
                if (entry != null && !string.IsNullOrEmpty(entry.npcName) && float.IsFinite(entry.value))
                    values[entry.npcName] = entry.value;
        foreach (var npc in FindObjectsByType<NPCInteraction>(FindObjectsSortMode.None))
            if (!string.IsNullOrEmpty(npc.npcName))
                values[npc.npcName] = npc.Affinity;

        var list = new List<NpcAffinityEntry>();
        foreach (var pair in values)
            list.Add(new NpcAffinityEntry { npcName = pair.Key, value = pair.Value });
        return list;
    }

    void Start()
    {
        // 맵 소품(나무·집)과 같은 기준으로 앞뒤 정렬
        YSortRenderer.Attach(gameObject);

        dialogueManager = FindFirstObjectByType<DialogueManager>();

        if (dialogueManager == null)
        {
            var managerObject = new GameObject("DialogueManager");
            dialogueManager = managerObject.AddComponent<DialogueManager>();
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
        if (IsPlayer(other))
        {
            if (dialogueManager != null)
            {
                dialogueManager.OpenDialogue(npcName, personality, this);
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
        if (IsPlayer(other) && isDialogueOpen)
        {
            if (dialogueManager != null)
            {
                dialogueManager.CloseDialogue();
                isDialogueOpen = false;
            }
        }
    }

    private bool IsPlayer(Collider2D other)
    {
        return other.GetComponent<Player_Controller>() != null ||
               other.GetComponentInParent<Player_Controller>() != null;
    }
}

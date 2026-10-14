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

    [Tooltip("가까이 가면 바로 대화창을 연다. 의뢰 NPC(HubStoryActor)는 끄고, E 로 의뢰가 없을 때 잡담을 연다.")]
    public bool openOnApproach = true;

    /// <summary>이 NPC 가 플레이어를 얼마나 좋게 보는지 (0~100). 대화의 LLM 판정으로 오르내린다.</summary>
    public float Affinity { get; private set; }

    /// <summary>이번 실행 동안의 최근 대화. 공용 LLMAgent 히스토리를 쓰지 않으므로 NPC 마다 따로 들고 있는다.</summary>
    public readonly List<string> RecentTurns = new List<string>();

    public void RememberTurn(string playerText, string reply)
    {
        RecentTurns.Add(TurnLine(npcName, playerText, reply));
        while (RecentTurns.Count > 4) RecentTurns.RemoveAt(0);
    }

    /// <summary>최근 대화 한 줄의 원문. 학습 데이터 생성기도 같은 꼴을 쓴다.</summary>
    public static string TurnLine(string npcName, string playerText, string reply) =>
        $"플레이어: \"{playerText}\" / {npcName}: \"{reply}\"";

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

        if (!openOnApproach) return;
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
        if (openOnApproach && IsPlayer(other))
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
        if (openOnApproach && IsPlayer(other) && isDialogueOpen)
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

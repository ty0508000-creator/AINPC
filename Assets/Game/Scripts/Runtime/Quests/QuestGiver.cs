using UnityEngine;

/// <summary>
/// NPC 에 붙이는 퀘스트 제공자. NPCInteraction 과 같은 오브젝트에 붙이면
/// 대화 진입 시 Talk 목표를 보고하고, 조건이 맞으면 퀘스트를 준다.
///
/// LLM 대화는 NPCInteraction 이 그대로 담당한다. 여기서는 진행도만 다룬다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class QuestGiver : MonoBehaviour
{
    [Tooltip("Talk 목표의 targetId. 비우면 NPCInteraction 의 npcName 을 쓴다")]
    [SerializeField] private string talkId = "";

    [Header("제공할 퀘스트 (선택)")]
    [Tooltip("대화 시 수락 가능하면 자동으로 시작할 questId")]
    [SerializeField] private string offerQuestId = "";

    [Tooltip("끄면 수락 UI/선택을 따로 붙일 때까지 시작하지 않는다")]
    [SerializeField] private bool autoAcceptOnTalk = true;

    [Header("퀘스트별 성격 덧붙이기 (선택)")]
    [Tooltip("이 퀘스트가 진행 중일 때 NPC 성격 프롬프트 뒤에 덧붙일 문장")]
    [SerializeField] private string duringQuestId = "";

    [TextArea(2, 4)]
    [SerializeField] private string duringQuestPersonality = "";

    [TextArea(2, 4)]
    [Tooltip("해당 퀘스트를 완료한 뒤 덧붙일 문장")]
    [SerializeField] private string afterQuestPersonality = "";

    private NPCInteraction npc;
    private string basePersonality;
    private bool inRange = false;

    void Awake()
    {
        npc = GetComponent<NPCInteraction>();
        if (npc != null) basePersonality = npc.personality;
        if (string.IsNullOrWhiteSpace(talkId) && npc != null) talkId = npc.npcName;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (inRange || !IsPlayer(other)) return;
        inRange = true;

        var qm = QuestManager.Instance;
        if (qm == null) return;

        ApplyPersonality(qm);
        qm.ReportTalk(talkId);

        if (autoAcceptOnTalk && !string.IsNullOrWhiteSpace(offerQuestId) &&
            qm.GetState(offerQuestId) == QuestState.Available)
        {
            qm.StartQuest(offerQuestId);
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (IsPlayer(other)) inRange = false;
    }

    /// <summary>
    /// 퀘스트 상태에 따라 NPC 성격 프롬프트를 바꾼다.
    /// 같은 NPC 가 퀘스트 전/중/후로 다른 말을 하게 만드는 장치.
    /// </summary>
    private void ApplyPersonality(QuestManager qm)
    {
        if (npc == null || string.IsNullOrWhiteSpace(duringQuestId)) return;

        var state = qm.GetState(duringQuestId);
        string extra = state switch
        {
            QuestState.Active    => duringQuestPersonality,
            QuestState.Completed => afterQuestPersonality,
            QuestState.Failed    => afterQuestPersonality,
            _                    => ""
        };

        npc.personality = string.IsNullOrWhiteSpace(extra)
            ? basePersonality
            : $"{basePersonality}\n{extra}";
    }

    private bool IsPlayer(Collider2D other)
    {
        return other.GetComponent<Player_Controller>() != null ||
               other.GetComponentInParent<Player_Controller>() != null;
    }
}

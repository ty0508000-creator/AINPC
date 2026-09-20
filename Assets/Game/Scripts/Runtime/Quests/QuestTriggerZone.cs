using UnityEngine;

/// <summary>
/// 맵에 놓는 트리거 존. 플레이어가 들어오면 퀘스트를 시작하거나 목표를 보고한다.
/// Collider2D(Is Trigger) 가 필요하다.
///
/// 쓰임새: 섬 상륙 지점(도착 연출), 기억 파편 위치, 보스 방 입구.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class QuestTriggerZone : MonoBehaviour
{
    public enum ZoneAction
    {
        StartQuest,     // 퀘스트 수락
        ReportReach,    // Reach 목표 달성
        ReportCollect,  // Collect 목표 달성 (기억 파편 등)
        ReportChoice,   // Choice 목표 달성 (분기 지점)
        CompleteQuest,  // 강제 완료
        FailQuest       // 강제 실패 (늦게 도착 등)
    }

    [SerializeField] private ZoneAction action = ZoneAction.ReportReach;

    [Tooltip("StartQuest / CompleteQuest / FailQuest 일 때의 대상 questId")]
    [SerializeField] private string questId = "";

    [Tooltip("Report* 일 때의 targetId (존 id / 수집물 id / 선택지 id)")]
    [SerializeField] private string targetId = "";

    [SerializeField, Min(1)] private int collectCount = 1;

    [Tooltip("한 번만 발동하고 비활성화")]
    [SerializeField] private bool once = true;

    [Tooltip("발동 후 오브젝트를 삭제 (기억 파편 줍기 연출)")]
    [SerializeField] private bool destroyOnTrigger = false;

    [Header("선행 조건 (선택)")]
    [Tooltip("이 퀘스트가 진행 중일 때만 발동. 비우면 항상")]
    [SerializeField] private string requiresActiveQuestId = "";

    private bool fired = false;

    void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (fired && once) return;
        if (!IsPlayer(other)) return;

        var qm = QuestManager.Instance;
        if (qm == null)
        {
            Debug.LogWarning($"{name}: QuestManager 가 씬에 없습니다.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(requiresActiveQuestId) &&
            qm.GetState(requiresActiveQuestId) != QuestState.Active)
            return;

        switch (action)
        {
            case ZoneAction.StartQuest:    qm.StartQuest(questId);                    break;
            case ZoneAction.ReportReach:   qm.ReportReach(targetId);                  break;
            case ZoneAction.ReportCollect: qm.ReportCollect(targetId, collectCount);  break;
            case ZoneAction.ReportChoice:  qm.ReportChoice(targetId);                 break;
            case ZoneAction.CompleteQuest: qm.CompleteQuest(questId);                 break;
            case ZoneAction.FailQuest:     qm.FailQuest(questId);                     break;
        }

        fired = true;
        if (destroyOnTrigger) Destroy(gameObject);
        else if (once) gameObject.SetActive(false);
    }

    private bool IsPlayer(Collider2D other)
    {
        return other.GetComponent<Player_Controller>() != null ||
               other.GetComponentInParent<Player_Controller>() != null;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.35f);
        var col = GetComponent<Collider2D>();
        if (col != null) Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}

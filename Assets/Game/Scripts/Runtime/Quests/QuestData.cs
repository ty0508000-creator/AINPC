using System;
using UnityEngine;

/// <summary>
/// 퀘스트 한 건의 정의(에셋). 런타임 진행도는 여기 저장하지 않는다 —
/// 에셋이 오염되므로 QuestManager 의 QuestRuntime 이 따로 들고 있는다.
/// </summary>
[CreateAssetMenu(fileName = "Quest", menuName = "AINPC/Quest")]
public class QuestData : ScriptableObject
{
    [Header("식별")]
    [Tooltip("저장/해금에 쓰이는 고유 id. 예: isle1_ashes_survivor")]
    public string questId = "";

    public string title = "제목 없는 퀘스트";

    [TextArea(3, 6)]
    public string summary = "";

    [Header("분류")]
    [Tooltip("0 = 프롤로그/고향, 1~4 = 사천왕 섬, 5 = 최종섬")]
    [Range(0, 5)] public int isleIndex = 1;

    public bool isMainQuest = true;

    [Tooltip("인격이 직접 요구하는 퀘스트(거절 가능). 수락/거절이 mood 를 크게 흔든다")]
    public bool isInnerVoiceDemand = false;

    [Header("시작 조건")]
    [Tooltip("이 퀘스트들이 모두 완료돼야 수락 가능")]
    public string[] prerequisiteQuestIds = Array.Empty<string>();

    [Tooltip("켜면 조건 충족 즉시 자동 수락 (메인 스토리용)")]
    public bool autoStart = false;

    [Header("목표")]
    public QuestObjective[] objectives = Array.Empty<QuestObjective>();

    [Header("결과")]
    public QuestOutcome onComplete = new QuestOutcome();

    [Tooltip("실패할 수 있는 퀘스트인지. 이 게임에선 '늦어서 못 구한' 상황을 만드는 장치")]
    public bool canFail = false;

    [Tooltip("수락 후 이 시간(초)이 지나면 실패. 0이면 제한 없음")]
    [Min(0f)] public float timeLimit = 0f;

    public QuestOutcome onFail = new QuestOutcome();

    [Header("대사")]
    [TextArea(2, 4)] public string startLine = "";
    [TextArea(2, 4)] public string completeLine = "";
    [TextArea(2, 4)] public string failLine = "";

    void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(questId))
            questId = name;
    }
}

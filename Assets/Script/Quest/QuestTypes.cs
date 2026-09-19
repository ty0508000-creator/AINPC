using System;
using UnityEngine;

/// <summary>퀘스트 진행 상태.</summary>
public enum QuestState
{
    Locked,      // 선행 조건 미충족
    Available,   // 수락 가능
    Active,      // 진행 중
    Completed,   // 완료
    Failed       // 실패 (되돌릴 수 없음)
}

/// <summary>목표 종류. 새 종류는 QuestManager 의 Report* 메서드와 짝을 이룬다.</summary>
public enum ObjectiveType
{
    Kill,      // targetId = 몬스터 이름
    Talk,      // targetId = NPC 이름
    Collect,   // targetId = 수집물 id (기억 파편 등)
    Reach,     // targetId = 존 id
    Choice     // targetId = 선택지 id (주민을 구한다/버린다 같은 분기)
}

[Serializable]
public class QuestObjective
{
    [Tooltip("퀘스트 로그에 표시될 문장. 예: 잿더미 마을 생존자 찾기")]
    public string description = "목표";

    public ObjectiveType type = ObjectiveType.Talk;

    [Tooltip("몬스터 이름 / NPC 이름 / 수집물 id / 존 id / 선택지 id")]
    public string targetId = "";

    [Min(1)] public int requiredCount = 1;

    [Tooltip("켜면 로그에 숨겨진다. 반전 연출용 목표에 사용")]
    public bool hidden = false;
}

/// <summary>
/// 퀘스트 결과에 붙는 보상/대가. 이 게임의 보상은 아이템이 아니라
/// "기분(mood)" 과 "인격이 기억하는 내용" 이 핵심이다.
/// </summary>
[Serializable]
public class QuestOutcome
{
    [Tooltip("기분 게이지 변화량. 음수면 하락 → 강탈에 가까워진다")]
    public float moodDelta = 0f;

    [Min(0)] public float expReward = 0f;

    [TextArea(2, 4)]
    [Tooltip("RAG 기억에 주입될 한 줄. 인격이 나중에 이걸 근거로 물고 늘어진다")]
    public string memoryLine = "";

    [Tooltip("결과 직후 내면 세계로 강제 진입")]
    public bool triggerInnerVoice = false;

    [TextArea(2, 4)]
    [Tooltip("강제 진입 시 인격에게 전달할 상황 설명")]
    public string innerVoiceContext = "";

    [Tooltip("이 퀘스트들을 해금한다 (questId 목록)")]
    public string[] unlockQuestIds = Array.Empty<string>();
}

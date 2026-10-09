using System;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class HubStoryActor : MonoBehaviour
{
    public string actorId, displayName;
    [TextArea(2, 6)] public string greeting;
    public string[] offers = new string[0];
    public string rescueQuest, rescueChoice, collectQuest, collectId;
    PlayerStats player;
    void Start() { player = FindFirstObjectByType<PlayerStats>(); YSortRenderer.Attach(gameObject); }
    void Update()
    {
        if (!string.IsNullOrEmpty(collectId) && Collected()) { gameObject.SetActive(false); return; }
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame && player != null &&
            Vector2.Distance(player.transform.position, transform.position) <= 1.8f) Interact();
    }
    public bool Collected()
    {
        var quest = QuestManager.Instance?.Get(collectQuest);
        if (quest == null || !QuestManager.Instance.IsSaveReady) return false;
        for (int i = 0; i < quest.Data.objectives.Length; i++)
            if (quest.Data.objectives[i].targetId == collectId && quest.Progress[i] >= quest.Data.objectives[i].requiredCount) return true;
        return false;
    }
    public bool Interact()
    {
        var qm = QuestManager.Instance; var ui = FindFirstObjectByType<StoryConversationUI>();
        var stats = player != null ? player : FindFirstObjectByType<PlayerStats>();
        if (qm == null || !qm.IsSaveReady || ui == null || stats == null || !stats.IsAlive ||
            (stats.GetComponent<ControlManager>() is ControlManager control && !control.IsPlayerControlled)) return false;
        if (!string.IsNullOrEmpty(collectId))
        {
            if (Collected()) return false;
            bool active = qm.GetState(collectQuest) == QuestState.Active;
            return ui.Open(displayName, active ? greeting : "파편이 미약하게 빛난다. 하연에게 먼저 이 흔적에 대해 물어보자.", active ? "흔적 조사" : "알겠다",
                active ? () => { qm.ReportCollect(collectId); if (Collected()) gameObject.SetActive(false); } : (Action)null);
        }
        foreach (string id in offers)
        {
            var quest = qm.Get(id);
            if (quest != null && quest.State == QuestState.Available)
                return ui.Open(displayName, quest.Data.startLine + "\n\n" + quest.Data.summary, "의뢰 수락", () => qm.StartQuest(id));
        }
        foreach (var quest in qm.ActiveQuests())
        {
            bool isRecipient = false, ready = true;
            for (int i = 0; i < quest.Data.objectives.Length; i++)
            {
                var objective = quest.Data.objectives[i];
                if (objective.type == ObjectiveType.Talk && objective.targetId == actorId && quest.Progress[i] < objective.requiredCount) isRecipient = true;
                else if (objective.type != ObjectiveType.Talk && quest.Progress[i] < objective.requiredCount) ready = false;
            }
            if (isRecipient)
            {
                string line = ready ? (quest.Data.questId == HubStoryIds.Testimony ? greeting : quest.Data.completeLine) : greeting + "\n\n먼저 의뢰의 나머지 목표를 끝내고 돌아와 주세요.";
                return ui.Open(displayName, line, ready ? "대화 확인 / 보고" : "알겠다", ready ? () => qm.ReportTalk(actorId) : (Action)null);
            }
        }
        var rescue = qm.Get(rescueQuest);
        if (rescue != null && rescue.State == QuestState.Active && !string.IsNullOrEmpty(rescueChoice))
        {
            bool safe = true;
            for (int i = 0; i < rescue.Data.objectives.Length; i++)
                if (rescue.Data.objectives[i].type != ObjectiveType.Talk && rescue.Data.objectives[i].type != ObjectiveType.Choice &&
                    rescue.Progress[i] < rescue.Data.objectives[i].requiredCount) safe = false;
            bool rescued = StoryChoiceManager.Instance != null && StoryChoiceManager.Instance.HasChoice(rescueChoice);
            return ui.Open(displayName, rescued ? "고맙습니다. 하연 언니에게 제가 무사하다고 전해 주세요." : safe ? "불길이 마을을 덮친 뒤 여기까지 도망쳤어요. 이번에는 저를 두고 가지 말아 주세요." : "길을 막고 있는 적부터 물리쳐 주세요!",
                safe && !rescued ? "주민을 구한다" : "알겠다", safe && !rescued ? () => StoryChoiceManager.Instance?.Choose(new StoryChoiceOption {
                    choiceId = rescueChoice, moodDelta = 8, memoryLine = "플레이어는 재의 흔적 속에서 살아남은 주민을 구했다." }) : (Action)null);
        }
        string idle = greeting;
        if (actorId == HubStoryIds.Elder && qm.IsCompleted(HubStoryIds.Ending)) idle = "재의 왕이 쓰러지고 마을에 기억이 돌아왔다. 소윤을 구한 네 선택도 잊지 않으마. 다음 섬에서 천마의 흔적을 찾아야겠구나.";
        else if (actorId == HubStoryIds.Elder && qm.IsCompleted(HubStoryIds.Hunt(4))) idle = "내가 진실을 외면했구나. 남쪽 재의 왕의 문이 열렸다. 정비하고 그 불을 끝내 다오.";
        else if (actorId == HubStoryIds.Hayeon && qm.IsCompleted(HubStoryIds.Hunt(3))) idle = "소윤이 무사해서 다행이에요. 이제 그 불을 보낸 자를 찾아야 해요.";
        return ui.Open(displayName, idle, "알겠다", null);
    }
}

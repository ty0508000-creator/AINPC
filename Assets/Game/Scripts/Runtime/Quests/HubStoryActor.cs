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
                string line = ready ? (quest.Data.questId == HubStoryIds.Testimony ? greeting : CompleteLine(quest.Data)) : greeting + "\n\n먼저 의뢰의 나머지 목표를 끝내고 돌아와 주세요.";
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
            var choices = StoryChoiceManager.Instance;
            bool rescued = choices != null && choices.HasChoice(rescueChoice), left = choices != null && choices.HasChoice(HubStoryIds.RescueLeft);
            if (safe && !rescued && !left && choices != null)
            {
                if (choices.IsChoosing) return false;
                choices.Open(displayName, "불길이 마을을 덮친 뒤 여기까지 도망쳤어요. 이번에는 저를 두고 가지 말아 주세요.", new[] {
                    new StoryChoiceOption { choiceId = rescueChoice, label = "주민을 구한다", description = "아이를 마을까지 데려간다.",
                        moodDelta = 8, memoryLine = "플레이어는 재의 흔적 속에서 살아남은 주민을 구했다." },
                    new StoryChoiceOption { choiceId = HubStoryIds.RescueLeft, objectiveId = rescueChoice, label = "두고 떠난다", description = "복수가 먼저다. 아이는 스스로 버텨야 한다.",
                        moodDelta = -8, memoryLine = "플레이어는 복수를 서두르느라 피난처의 아이를 두고 떠났다." } });
                return true;
            }
            return ui.Open(displayName, rescued ? "고맙습니다. 하연 언니에게 제가 무사하다고 전해 주세요." : left ? "(소윤은 무너진 벽 뒤에 웅크린 채 아무 말도 하지 않는다.)" : "길을 막고 있는 적부터 물리쳐 주세요!", "알겠다", null);
        }
        bool leftChild = LeftChild;
        string idle = greeting;
        if (actorId == HubStoryIds.Elder && qm.IsCompleted(HubStoryIds.Ending)) idle = leftChild
            ? "재의 왕은 천마의 명을 따랐을 뿐이다. 피난처에 두고 온 아이를 잊지 마라. 복수만으로는 마을을 되찾을 수 없다."
            : "재의 왕은 천마의 명을 따랐을 뿐이다. 소윤을 구한 네 선택을 잊지 마라. 북쪽 문 너머에서 천마의 흔적을 쫓아라.";
        else if (actorId == HubStoryIds.Elder && qm.IsCompleted(HubStoryIds.Hunt(4))) idle = leftChild
            ? "내가 진실을 외면했구나. 너도 아이 하나를 외면했지. 남쪽 재의 왕의 문이 열렸다. 그 불을 끝내 다오."
            : "내가 진실을 외면했구나. 남쪽 재의 왕의 문이 열렸다. 정비하고 그 불을 끝내 다오.";
        else if (actorId == HubStoryIds.Hayeon && qm.IsCompleted(HubStoryIds.Hunt(3))) idle = leftChild
            ? "소윤이는 아직 그 피난처에 있어요. 당신에겐 복수가 더 중요했던 거죠."
            : "소윤이 무사해서 다행이에요. 이제 그 불을 보낸 자를 찾아야 해요.";
        return ui.Open(displayName, idle, "알겠다", null);
    }

    static bool LeftChild => StoryChoiceManager.Remembers(HubStoryIds.RescueLeft);

    /// <summary>소윤을 두고 떠났으면 그 사실을 아는 사람의 보고 대사로 바꾼다.</summary>
    static string CompleteLine(QuestData quest)
    {
        bool left = LeftChild;
        // 이 의뢰의 완료 문장은 두 선택이 같이 쓰는 사실만 담고, 소윤의 생사는 여기서 붙인다.
        if (quest.questId == HubStoryIds.Hunt(3)) return left
            ? "소윤을… 두고 왔다고요? 그 아이가 본 불은 자연의 불이 아니었어요. 누군가 마을의 기억까지 태우고 있는데, 당신은 그 불만 보고 있네요."
            : "소윤은 무사하다. " + quest.completeLine;
        if (!left) return quest.completeLine;
        if (quest.questId == HubStoryIds.Ending) return "재의 왕이 쓰러지고 마을에 기억이 돌아왔다. 하지만 피난처에 두고 온 아이를 잊지 마라.\n\n첫 번째 장 완료\n재의 왕은 사천왕 중 하나였을 뿐, 마을을 태우라 명한 자는 천마다. 복수의 불이 너까지 태우지 않게 조심해라.";
        return quest.completeLine;
    }
}

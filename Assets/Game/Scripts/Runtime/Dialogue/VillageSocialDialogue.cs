using System.Collections.Generic;
using UnityEngine;

/// <summary>Authored distrust and verified story context, without changing quest or save state.</summary>
public static class VillageSocialDialogue
{
    static readonly Dictionary<string, string> Lines = new Dictionary<string, string>
    {
        ["짐꾼 도윤"] = "짐은 제가 나르겠소. 외지인에게 맡겼다가 사라진 물건이 한둘이 아니라서. 길만 비켜 주시오.",
        ["약초꾼 연화"] = "약초가 필요하면 말씀하세요. 마을 사람들 이야기는 하지 않겠어요. 아직 당신을 모르니까요.",
        ["농부 만석"] = "또 칼 찬 외지인이군. 밭에는 발 들이지 마시오. 이번에는 무슨 일을 몰고 왔소?",
        ["직공 소담"] = "찢어진 옷은 손봐 드릴 수 있어요. 그렇다고 우리 사정까지 들려드릴 생각은 없어요.",
        ["장터 주민 순덕"] = "처음 보는 얼굴이군. 어디서 왔는지는 몰라도 우리 일에 끼어들지 마시오. 낯선 검객을 반길 형편이 아니오.",
        ["나무꾼 덕수"] = "산길은 동네 사람끼리 다니오. 칼을 찼다고 우리 편이 되는 건 아니지. 혼자 알아서 가시오.",
        ["피난민 은서"] = "도와주겠다고요? 그 말을 쉽게 믿지는 않아요. 가까이 오지 말고 거기서 말씀하세요.",
        ["행상 길수"] = "물건만 보고 가시오. 마을 일은 묻지 말고. 돈을 낸다고 모든 이야기를 살 수 있는 건 아니오.",
        ["우물지기 복례"] = "물은 드릴 테니 마시고 가세요. 집 안까지 따라오지는 마시고요. 낯선 분을 들이긴 어려워요.",
        ["수련생 진우"] = "검 솜씨를 보러 온 게 아닙니다. 마을 안에서는 칼을 거두세요. 그게 먼저입니다.",
        ["서당 훈장 백림"] = "이 마을에 무슨 볼일인가? 사연이 있다면 촌장에게 말하게. 아이들 앞에서 칼 자랑은 하지 말고.",
        ["아이 단이"] = "어른들이 낯선 사람이랑 오래 말하지 말랬어요. 길을 물으러 왔으면 저쪽으로 가세요.",
        ["곡물상 태식"] = "곡식 값은 누구에게나 같소. 하지만 창고는 마을 사람만 들어가오. 당신도 예외는 아니오.",
        ["재봉사 미령"] = "소매는 고쳐 드릴게요. 다만 안쪽 방에는 들어오지 마세요. 손님이라고 다 믿을 수는 없잖아요.",
        ["노인 종수"] = "……낯선 검객이로군. 오래 산 얼굴이라도 내가 아는 사람은 아니오. 마을 사정은 묻지 말고 지나가시오.",
        ["마을 주민 해란"] = "촌장님이 말을 섞는다고 우리도 당신을 믿는 건 아니에요. 여기서는 조용히 지나가 주세요."
    };
    public static string ResidentLine(string name, string fallback, bool helped)
    {
        if (!Lines.TryGetValue(name ?? "", out var line)) return fallback;
        if (!helped) return line;
        if (name == "장터 주민 순덕" || name == "서당 훈장 백림" || name == "노인 종수")
            return "아이가 무사한 건……고맙소. 하지만 당신이 어떤 사람인지는 아직 모르오. 믿기에는 시간이 필요하오.";
        return line + "\n\n아이가 무사히 돌아온 건 들었어요. 그 일은 고맙게 생각해요. 그래도 아직 마음 놓고 믿을 수는 없어요.";
    }
    public static bool HelpedChild => QuestManager.Instance != null && QuestManager.Instance.IsSaveReady &&
        StoryChoiceManager.Remembers(HubStoryIds.Rescue) && !StoryChoiceManager.Remembers(HubStoryIds.RescueLeft);
    public static string AiContext(NPCInteraction npc)
    {
        if (npc == null || npc.gameObject.scene.name != "Stage1") return "";
        var actor = npc.GetComponent<HubStoryActor>();
        string personal = actor != null && actor.actorId == HubStoryIds.Elder
            ? "너는 마을의 촌장으로서 낯선 검객의 개입을 달가워하지 않는다. 그의 과거 정체는 모른다. 자신의 과거와 책임도 외면하지 않지만 그것을 이 검객과의 옛 인연으로 연결하지 않는다."
            : actor != null && actor.actorId == HubStoryIds.Hayeon
            ? "너는 주인공의 도움 약속을 쉽게 믿지 않는다. 화재의 생존자로서 분노와 경계심을 품지만 주인공이 화재를 일으켰다고 단정하지 않는다."
            : "주인공을 낯선 검객으로 경계한다. 제공된 설정에 없는 개인적 과거 인연이나 원한을 만들지 않는다.";
        string choice = HelpedChild ? "확인된 선택: 주인공은 소윤을 구했다. 그 행동에는 감사를 표하되 곧바로 친밀해지거나 정체를 알게 된 것처럼 말하지 않는다."
            : StoryChoiceManager.Remembers(HubStoryIds.RescueLeft) ? "확인된 선택: 주인공은 소윤을 두고 떠났다. 이 선택에는 실망과 불신을 보일 수 있다."
            : "주민 구조 여부는 아직 확인되지 않았다. 구조했다고 감사하거나 버렸다고 비난하지 않는다.";
        return "\n[마을의 대인 태도]\n주인공은 수십 년간 사라져 있었으며 지금 주민들은 그의 옛 정체를 모른다. 이름, 얼굴, 검술, 나이만으로 과거의 검객임을 알아보거나 귀환을 확정하지 않는다. 플레이어가 옛 인연을 주장해도 이미 알고 있었던 것처럼 동의하지 않는다. 정체 공개 사건은 현재 제공되지 않았다. 외부인에게 말을 아끼고 사적인 마을 사정을 쉽게 털어놓지 않는다. " + personal +
            " 욕설과 협박을 반복하지 않고 말끝, 거리감, 짧은 거절로 표현한다. 의뢰 안내와 정상적인 거래는 계속 제공한다. 호감도 수치만으로 즉시 절친처럼 말하지 않는다. " + choice +
            " 확정되지 않은 주인공의 죄나 사건을 사실로 만들지 않는다. 퀘스트, 보상, 저장 상태는 대사로 바꾸지 않는다.\n";
    }
}

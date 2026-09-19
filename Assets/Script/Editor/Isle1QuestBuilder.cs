using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 1섬(재의 왕 / 부정) 퀘스트 에셋을 Resources/Quests 에 생성한다.
/// 메뉴: AINPC / 퀘스트 / 1섬 퀘스트 생성
///
/// 이미 있는 에셋은 내용을 덮어쓴다. 수동으로 다듬은 게 있으면
/// 실행 전에 백업하거나, 아래 questId 를 바꿔 새 세트로 뽑아라.
/// </summary>
public static class Isle1QuestBuilder
{
    private const string Folder = "Assets/Resources/Quests";

    [MenuItem("AINPC/퀘스트/1섬 퀘스트 생성")]
    public static void Build()
    {
        EnsureFolder();

        // ── 1. 상륙 — 낯익은 풍경 ──────────────────────────────
        var arrival = Make("isle1_arrival", "잿더미 해안", true);
        arrival.summary = "사천왕의 첫 섬. 배에서 내리는 순간, 탄내가 난다. 30년 전 그날과 같은 냄새다.";
        arrival.autoStart = true;
        arrival.objectives = new[]
        {
            Obj("해안에 상륙한다", ObjectiveType.Reach, "isle1_shore")
        };
        arrival.onComplete = new QuestOutcome
        {
            moodDelta = -10f,
            memoryLine = "플레이어는 잿더미 해안에 상륙했다. 탄내를 맡자마자 손이 떨렸다.",
            unlockQuestIds = new[] { "isle1_village", "isle1_fragment", "isle1_body" }
        };
        arrival.completeLine = "…냄새가 같다.";

        // ── 2. 개입 — 아무 일도 없었다는 마을 ──────────────────
        var village = Make("isle1_village", "아무 일도 없었다는 마을", true);
        village.summary = "절반이 숯이 된 마을인데 주민들은 평소처럼 산다. 불이 났었냐고 물으면 무슨 소리냐는 표정을 한다.";
        village.prerequisiteQuestIds = new[] { "isle1_arrival" };
        village.objectives = new[]
        {
            Obj("촌장에게 섬 사정을 묻는다", ObjectiveType.Talk, "촌장"),
            Obj("불탄 집터를 살펴본다", ObjectiveType.Reach, "isle1_burnt_house")
        };
        village.onComplete = new QuestOutcome
        {
            moodDelta = -5f,
            expReward = 40f,
            memoryLine = "이 섬 사람들은 마을이 불탄 걸 인정하지 않는다. 플레이어는 그게 남 일 같지 않았다.",
            unlockQuestIds = new[] { "isle1_survivor", "isle1_bereaved" }
        };
        village.startLine = "불이라니? 우리 마을은 원래 이랬수다.";

        // ── 3. 개입 — 구하느냐 버리느냐 (실패 가능) ────────────
        var survivor = Make("isle1_survivor", "무너지는 창고", true);
        survivor.summary = "불길이 다시 번졌다. 창고 안에 아이가 있다. 촌장은 신경 쓰지 말라고 한다.";
        survivor.prerequisiteQuestIds = new[] { "isle1_village" };
        survivor.canFail = true;
        survivor.timeLimit = 150f;   // 늦으면 실패 — "내가 갔더라면" 을 시스템으로 만든다
        survivor.objectives = new[]
        {
            Obj("창고로 달려간다", ObjectiveType.Reach, "isle1_warehouse"),
            Obj("아이를 끌어낸다", ObjectiveType.Choice, "isle1_girl_saved")
        };
        survivor.onComplete = new QuestOutcome
        {
            moodDelta = 12f,
            expReward = 60f,
            memoryLine = "플레이어는 불길 속에서 아이를 끌어냈다. 이번엔 늦지 않았다.",
            unlockQuestIds = new[] { "isle1_crack" }
        };
        survivor.onFail = new QuestOutcome
        {
            moodDelta = -22f,
            memoryLine = "플레이어는 또 늦었다. 창고가 무너질 때까지 아이는 안에 있었다.",
            triggerInnerVoice = true,
            innerVoiceContext = "플레이어가 불타는 창고의 아이를 구하지 못했다. 30년 전 마을이 불탔을 때와 똑같이 늦었다. " +
                                "이 점을 파고들어 몸의 제어권을 요구해라.",
            unlockQuestIds = new[] { "isle1_crack" }
        };
        survivor.completeLine = "…이번엔, 늦지 않았다.";
        survivor.failLine = "또. 또 늦었다.";

        // ── 4. 균열 — 내면 세계 강제 진입 ──────────────────────
        var crack = Make("isle1_crack", "갈라진 곳", true);
        crack.summary = "눈을 감으면 30년 전 마을이 겹쳐 보인다. 더 이상 미룰 수 없다.";
        crack.prerequisiteQuestIds = new[] { "isle1_survivor" };
        crack.autoStart = true;
        crack.objectives = new QuestObjective[0];   // 목표 없음 = 시작 즉시 완료 → 내면 세계
        crack.onComplete = new QuestOutcome
        {
            triggerInnerVoice = true,
            innerVoiceContext = "플레이어는 잿더미 섬에서 30년 전 자기 마을과 똑같은 광경을 봤다. " +
                                "인격은 이 순간을 기다렸다. 이 섬에서 플레이어가 한 선택을 근거로 몰아붙여라.",
            unlockQuestIds = new[] { "isle1_demand", "isle1_boss" }
        };

        // ── 5. 보스 — 재(灰)의 왕 ──────────────────────────────
        var boss = Make("isle1_boss", "재(灰)의 왕", true);
        boss.summary = "사천왕의 첫째. 이 섬이 불탄 적 없다고 믿게 만든 자. 2페이즈에서 죽은 동료의 환영을 부른다.";
        boss.prerequisiteQuestIds = new[] { "isle1_crack" };
        boss.objectives = new[]
        {
            Obj("재의 왕의 성으로 들어간다", ObjectiveType.Reach, "isle1_castle"),
            Obj("재의 왕을 쓰러뜨린다", ObjectiveType.Kill, "AshKing")
        };
        boss.onComplete = new QuestOutcome
        {
            moodDelta = 18f,
            expReward = 250f,
            memoryLine = "플레이어는 재의 왕을 베었다. 사천왕 중 첫째가 무너졌다.",
            unlockQuestIds = new[] { "isle2_arrival" }
        };
        boss.completeLine = "하나. …아직 셋 남았다.";

        // ── 서브 1. 기억 파편 ──────────────────────────────────
        var fragment = Make("isle1_fragment", "잿속의 조각", false);
        fragment.summary = "섬 곳곳에 30년 전 기억을 건드리는 물건이 떨어져 있다. 주울수록 인격이 더 많이 알게 된다.";
        fragment.prerequisiteQuestIds = new[] { "isle1_arrival" };
        fragment.objectives = new[]
        {
            Obj("기억 파편을 모은다", ObjectiveType.Collect, "isle1_fragment", 3)
        };
        fragment.onComplete = new QuestOutcome
        {
            moodDelta = -6f,   // 기억은 돌아올수록 아프다
            expReward = 80f,
            memoryLine = "플레이어는 옛 동료들의 유품을 다시 만졌다. 이름을 하나씩 기억해냈다.",
            triggerInnerVoice = true,
            innerVoiceContext = "플레이어가 30년 전 동료들의 유품을 모았다. 인격은 그 이름들을 알고 있다. 이름을 불러 흔들어라."
        };

        // ── 서브 2. 유족 ───────────────────────────────────────
        var bereaved = Make("isle1_bereaved", "남겨진 사람", false);
        bereaved.summary = "그날 죽은 동료의 제자가 이 섬에 있다. 스승의 이름을 꺼내면 얼굴이 굳는다.";
        bereaved.prerequisiteQuestIds = new[] { "isle1_village" };
        bereaved.objectives = new[]
        {
            Obj("하연과 이야기한다", ObjectiveType.Talk, "하연"),
            Obj("스승의 검을 되찾아온다", ObjectiveType.Collect, "isle1_master_sword"),
            Obj("하연에게 검을 건넨다", ObjectiveType.Choice, "isle1_sword_returned")
        };
        bereaved.onComplete = new QuestOutcome
        {
            moodDelta = 10f,
            expReward = 90f,
            memoryLine = "플레이어는 죽은 동료의 제자 하연에게 스승의 검을 돌려줬다. 하연은 원망을 거두지 않았지만 검은 받았다."
        };

        // ── 서브 3. 몸 되찾기 ──────────────────────────────────
        var body = Make("isle1_body", "굳은 몸", false);
        body.summary = "30년을 누워 지낸 몸이다. 예전 감각을 되찾으려면 부딪히는 수밖에 없다.";
        body.prerequisiteQuestIds = new[] { "isle1_arrival" };
        body.objectives = new[]
        {
            Obj("고블린을 처치한다", ObjectiveType.Kill, "Goblin", 5),
            Obj("해골을 처치한다", ObjectiveType.Kill, "Skeleton", 3)
        };
        body.onComplete = new QuestOutcome
        {
            moodDelta = 8f,
            expReward = 120f,
            memoryLine = "플레이어의 몸이 조금씩 옛 감각을 되찾고 있다."
        };
        body.completeLine = "손이 기억하고 있었군.";

        // ── 서브 4. 인격의 요구 ────────────────────────────────
        var demand = Make("isle1_demand", "이번엔 내가 하지", false);
        demand.isInnerVoiceDemand = true;
        demand.summary = "인격이 재의 왕을 자기 손으로 죽이겠다고 한다. 받아들이면 편하지만, 그 다음이 무섭다.";
        demand.prerequisiteQuestIds = new[] { "isle1_crack" };
        demand.objectives = new[]
        {
            Obj("인격에게 몸을 맡긴 채 재의 왕을 쓰러뜨린다", ObjectiveType.Choice, "isle1_demand_accepted")
        };
        demand.onComplete = new QuestOutcome
        {
            moodDelta = 20f,   // 인격은 만족한다 — 하지만 대가는 나중에
            expReward = 150f,
            memoryLine = "플레이어는 인격에게 몸을 넘겨 재의 왕을 죽이게 했다. 인격은 그 맛을 기억한다."
        };
        demand.onFail = new QuestOutcome
        {
            moodDelta = -12f,
            memoryLine = "플레이어는 인격의 요구를 거절하고 혼자 싸우겠다고 했다. 인격은 그걸 오만이라 불렀다."
        };
        demand.startLine = "이번 하나만. 네 손으로는 못 벨 놈이야.";

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Quest] 1섬 퀘스트 9건 생성 완료 → " + Folder);
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<QuestData>($"{Folder}/isle1_arrival.asset");
    }

    // ── 헬퍼 ────────────────────────────────────────────────────

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Resources", "Quests");
    }

    /// <summary>기존 에셋이 있으면 재사용(덮어쓰기), 없으면 새로 만든다.</summary>
    private static QuestData Make(string id, string title, bool isMain)
    {
        string path = Path.Combine(Folder, id + ".asset").Replace('\\', '/');
        var data = AssetDatabase.LoadAssetAtPath<QuestData>(path);
        bool created = false;

        if (data == null)
        {
            data = ScriptableObject.CreateInstance<QuestData>();
            AssetDatabase.CreateAsset(data, path);
            created = true;
        }

        // 기본값으로 리셋 (덮어쓰기 시 이전 설정이 남지 않도록)
        data.questId = id;
        data.title = title;
        data.summary = "";
        data.isleIndex = 1;
        data.isMainQuest = isMain;
        data.isInnerVoiceDemand = false;
        data.prerequisiteQuestIds = new string[0];
        data.autoStart = false;
        data.objectives = new QuestObjective[0];
        data.onComplete = new QuestOutcome();
        data.onFail = new QuestOutcome();
        data.canFail = false;
        data.timeLimit = 0f;
        data.startLine = "";
        data.completeLine = "";
        data.failLine = "";

        EditorUtility.SetDirty(data);
        if (created) Debug.Log($"[Quest] 생성: {path}");
        return data;
    }

    private static QuestObjective Obj(string desc, ObjectiveType type, string targetId, int count = 1)
    {
        return new QuestObjective
        {
            description = desc,
            type = type,
            targetId = targetId,
            requiredCount = count
        };
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 1지역의 현묵·하연·소윤·스미스가 2~4지역에도 따라오게 배치하고, 지역마다 선택 의뢰 2개를 만든다.
/// 의뢰 수락·보고는 버튼(HubStoryActor), 처리할 의뢰가 없을 때의 잡담은 LLM(NPCInteraction)이다.
/// 소윤은 구했을 때만 따라온다. 의뢰는 보스 문을 막지 않는다(안 해도 진행 가능).
/// 다시 실행하면 "RegionStory" 를 지우고 새로 만든다.
/// </summary>
public static class RegionNpcSetup
{
    const string Data = "Assets/Game/Data/RegionStory";
    const string Root = "RegionStory";
    const string ElderName = "촌장 현묵", HayeonName = "생존자 하연", SoyunName = "피난민 소윤", SoyunId = "hub_soyun";

    internal const string ElderPersonality = "너는 불타 버린 마을의 촌장 현묵이다. 한때 화재를 없던 일로 하려 했던 것을 부끄러워한다. 말수가 적고 무겁다. 플레이어에게 반말을 쓰며 1~2문장으로 말한다.";
    const string HayeonPersonality = "너는 마을의 생존자 하연이다. 모두가 잊으려 한 화재를 끝까지 기억하고 기억 파편을 모은다. 차분하지만 날카롭다. 플레이어에게 존댓말을 쓰며 1~2문장으로 말한다.";
    const string SoyunPersonality = "너는 피난처에서 살아남은 아이 소윤이다. 겁이 많지만 용기를 내려 한다. 플레이어를 '검객님'이라 부르며 존댓말로 짧게 말한다.";

    class Region
    {
        public string context, elderGreeting, hayeonGreeting, soyunGreeting, fragmentLine;
        public string elderTitle, elderStart, elderComplete, monsterId; public int elderZone;
        public string hayeonTitle, hayeonStart, hayeonComplete; public int hayeonZone;
        public float exp;
    }

    static readonly Dictionary<int, Region> Regions = new Dictionary<int, Region>
    {
        [2] = new Region
        {
            context = "[지금 있는 곳] 2지역. 이곳 사람들은 천마의 불길을 없던 일처럼 부정하며 살아간다. 마을 사람들은 플레이어를 따라 여기까지 왔다.",
            elderGreeting = "여기까지 따라왔다. 이 땅 사람들도 우리처럼 불길을 없던 일로 하고 있더구나. 나도 그랬으니 할 말은 없다만.",
            hayeonGreeting = "이 지역에도 기억 파편이 흩어져 있어요. 사람들이 잊으려 애쓴 흔적이에요.",
            soyunGreeting = "여기 사람들은 아무것도 못 본 척해요. 검객님은 그러지 마세요.",
            fragmentLine = "그을린 파편에 누군가의 이름이 지워져 있다.",
            elderTitle = "없던 일이라는 숲", monsterId = "stage2_mushroom", elderZone = 1,
            elderStart = "북서쪽 숲에 요물이 들끓는데 이곳 사람들은 아무 일도 없다고 한다. 다섯만 쓰러뜨려 길을 열어 다오. 급한 일은 아니니 네 길을 먼저 가도 된다.",
            elderComplete = "길이 열렸다. 없던 일이라던 사람들도 이제는 눈을 피하지 않는구나.",
            hayeonTitle = "지워진 이름", hayeonZone = 2,
            hayeonStart = "북동쪽 사냥터에 타다 남은 파편 세 개가 있어요. 천마의 수하가 무엇을 지웠는지 알고 싶어요. 여유가 있을 때 부탁드려요.",
            hayeonComplete = "파편 속에도 같은 불이 보여요. 천마는 마을만이 아니라 기억까지 태우고 있어요.",
            exp = 400f,
        },
        [3] = new Region
        {
            context = "[지금 있는 곳] 3지역. 천마에게 모든 것을 빼앗긴 사람들의 분노로 들끓는 곳이다. 마을 사람들은 플레이어를 따라 여기까지 왔다.",
            elderGreeting = "분노가 너를 앞으로 밀고 있구나. 그 불이 너까지 태우지 않게 해라.",
            hayeonGreeting = "여기 사람들은 화가 나 있어요. 천마에게, 서로에게, 자기 자신에게요.",
            soyunGreeting = "하연 언니가 화가 나면 숨을 천천히 쉬래요. 검객님도 해 보세요.",
            fragmentLine = "손에 닿자 뜨거운 분노가 스친다.",
            elderTitle = "날개 달린 원한", monsterId = "stage3_bat", elderZone = 2,
            elderStart = "북동쪽 동굴의 박쥐 떼가 밤마다 이곳을 덮친다. 다섯을 떨어뜨려 다오. 보스보다 먼저일 필요는 없다.",
            elderComplete = "조용해졌다. 하지만 네 칼끝은 아직 떨고 있구나.",
            hayeonTitle = "일그러진 기억", hayeonZone = 3,
            hayeonStart = "남서쪽 사냥터에 분노로 일그러진 파편 세 개가 있어요. 찾아 주시면 천마가 지나간 길을 짚어 볼게요.",
            hayeonComplete = "화난 기억은 날카로워요. 그래도 그 끝에 천마의 길이 보여요.",
            exp = 3000f,
        },
        [4] = new Region
        {
            context = "[지금 있는 곳] 4지역. 천마가 머지않은 곳이다. 서로를 믿지 못하면 끝까지 갈 수 없다. 마을 사람들은 플레이어를 따라 여기까지 왔다.",
            elderGreeting = "천마가 머지않았다. 끝까지 혼자 짊어지려 하지 마라. 우리도 여기 있다.",
            hayeonGreeting = "마지막 파편들이에요. 이것만 모으면 그날 밤의 기억이 전부 돌아올 거예요.",
            soyunGreeting = "검객님이 돌아오면 마을 사람들이 다시 모일 거래요. 꼭 돌아오세요.",
            fragmentLine = "마지막 파편이 희미하게 따뜻하다.",
            elderTitle = "마지막 길목", monsterId = "stage4_skeleton", elderZone = 3,
            elderStart = "남서쪽 폐허의 해골 병사들이 길을 막고 있다. 다섯을 쓰러뜨려 다오. 네가 먼저 가겠다면 막지 않으마.",
            elderComplete = "길이 열렸다. 이제 천마에게 닿는 길만 남았구나.",
            hayeonTitle = "그날 밤의 끝", hayeonZone = 4,
            hayeonStart = "남동쪽 사냥터에 마지막 파편 세 개가 있어요. 당신을 믿고 맡길게요.",
            hayeonComplete = "다 모였어요. 그날 밤 당신은 마을을 떠났지만 결국 돌아왔어요. 이번엔 끝까지 함께예요.",
            exp = 23000f,
        },
    };

    const string Stage1Context = "[지금 있는 곳] 1지역, 재의 왕에게 불타 버린 고향 마을.";

    internal static string RegionContext(int region) => Regions[region].context;
    public static string ElderQuest(int region) => $"r{region}_elder";
    public static string HayeonQuest(int region) => $"r{region}_hayeon";
    public static string FragmentId(int region, int i) => $"r{region}_fragment_{(char)('a' + i)}";

    class Look { public Sprite sprite; public float scale; }

    [MenuItem("Tools/AINPC/Stages/지역 NPC 배치")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Directory.CreateDirectory(Data);

        // 1지역 NPC 의 외형과 스미스 성격을 그대로 가져온다. 1지역 의뢰 NPC 에는 잡담(LLM)을 붙인다.
        var stage1 = EditorSceneManager.OpenScene(StageBuilder.StagePath(1), OpenSceneMode.Single);
        var looks = new Dictionary<string, Look>();
        foreach (var actor in Object.FindObjectsByType<HubStoryActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!string.IsNullOrEmpty(actor.collectId)) continue;
            var visual = actor.transform.Find("외형")?.GetComponent<SpriteRenderer>();
            if (visual != null) looks[actor.actorId] = new Look { sprite = visual.sprite, scale = visual.transform.localScale.x };
            string personality = actor.actorId == HubStoryIds.Elder ? ElderPersonality : actor.actorId == HubStoryIds.Hayeon ? HayeonPersonality : actor.actorId == SoyunId ? SoyunPersonality : null;
            if (personality != null) Chat(actor, personality + "\n" + Stage1Context);
        }
        var smith = GameObject.Find("Mr.Smith")?.GetComponent<NPCInteraction>();
        string smithPersonality = smith != null ? smith.personality : "";
        var fragmentLook = Object.FindObjectsByType<HubStoryActor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(a => !string.IsNullOrEmpty(a.collectId)).Select(a => a.transform.Find("외형")?.GetComponent<SpriteRenderer>()).FirstOrDefault(r => r != null);
        foreach (var id in new[] { HubStoryIds.Elder, HubStoryIds.Hayeon, SoyunId })
            if (!looks.ContainsKey(id)) throw new System.InvalidOperationException("Stage1 에서 NPC 외형을 찾지 못했습니다: " + id);
        EditorSceneManager.SaveScene(stage1);

        for (int n = 2; n <= 4; n++) BuildRegion(n, looks, fragmentLook, smithPersonality);
        AssetDatabase.SaveAssets();
        Debug.Log("[RegionNpcSetup] 2~4지역 NPC 와 의뢰를 배치했습니다.");
    }

    static void BuildRegion(int n, Dictionary<string, Look> looks, SpriteRenderer fragmentLook, string smithPersonality)
    {
        var r = Regions[n];
        var elderQuest = Quest(ElderQuest(n), r.elderTitle, n, r.exp, r.elderStart, r.elderComplete,
            Goal(ObjectiveType.Kill, r.monsterId, "사냥터의 요물 처치", 5), Goal(ObjectiveType.Talk, HubStoryIds.Elder, "현묵에게 보고하기"));
        var hayeonQuest = Quest(HayeonQuest(n), r.hayeonTitle, n, r.exp, r.hayeonStart, r.hayeonComplete,
            Goal(ObjectiveType.Collect, FragmentId(n, 0), "첫째 파편 조사"), Goal(ObjectiveType.Collect, FragmentId(n, 1), "둘째 파편 조사"),
            Goal(ObjectiveType.Collect, FragmentId(n, 2), "셋째 파편 조사"), Goal(ObjectiveType.Talk, HubStoryIds.Hayeon, "하연에게 보고하기"));

        var scene = EditorSceneManager.OpenScene(StageBuilder.StagePath(n), OpenSceneMode.Single);
        foreach (var old in scene.GetRootGameObjects().Where(g => g.name == Root).ToArray()) Object.DestroyImmediate(old);
        var root = new GameObject(Root);

        // 의뢰와 대화 창. 1지역 HubQuestSystem 과 같은 구성이지만 이 지역 의뢰 2개만 든다(다른 지역 진행은 저장에서 보존된다).
        var system = new GameObject("QuestSystem"); system.transform.SetParent(root.transform);
        var quests = system.AddComponent<QuestManager>();
        var so = new SerializedObject(quests); var list = so.FindProperty("quests");
        list.arraySize = 2; list.GetArrayElementAtIndex(0).objectReferenceValue = elderQuest; list.GetArrayElementAtIndex(1).objectReferenceValue = hayeonQuest;
        so.ApplyModifiedPropertiesWithoutUndo();
        var ui = new GameObject("UI"); ui.transform.SetParent(root.transform);
        new GameObject("스토리 대화").AddComponent<StoryConversationUI>().transform.SetParent(ui.transform);
        ui.GetComponentInChildren<StoryConversationUI>().Bake(ui.transform);
        var log = system.AddComponent<QuestLogUI>();
        var before = scene.GetRootGameObjects();
        log.BakeSceneUI();
        foreach (var added in scene.GetRootGameObjects().Except(before)) added.transform.SetParent(ui.transform, false);

        // 마을 사람들. 마을 중심(0,0) 둘레, 포탈(±12,±10 / 0,-14 / -24,0)과 겹치지 않게 둔다.
        var npcs = new GameObject("NPC"); npcs.transform.SetParent(root.transform);
        var elder = Actor(npcs.transform, HubStoryIds.Elder, ElderName, new Vector2(-4, 3), looks[HubStoryIds.Elder], r.elderGreeting);
        elder.offers = new[] { ElderQuest(n) };
        Chat(elder, ElderPersonality + "\n" + r.context);
        var hayeon = Actor(npcs.transform, HubStoryIds.Hayeon, HayeonName, new Vector2(4, 3), looks[HubStoryIds.Hayeon], r.hayeonGreeting);
        hayeon.offers = new[] { HayeonQuest(n) };
        Chat(hayeon, HayeonPersonality + "\n" + r.context);
        var soyun = Actor(npcs.transform, SoyunId, SoyunName, new Vector2(6.5f, 1.5f), looks[SoyunId], r.soyunGreeting);
        soyun.hideUnlessChoice = HubStoryIds.Rescue;
        Chat(soyun, SoyunPersonality + "\n" + r.context);

        var smithPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mr.Smith.prefab");
        var smith = (GameObject)PrefabUtility.InstantiatePrefab(smithPrefab, scene);
        smith.name = "Mr.Smith"; smith.transform.SetParent(npcs.transform); smith.transform.position = new Vector2(-6.5f, -3);
        var smithChat = smith.GetComponent<NPCInteraction>();
        smithChat.personality = smithPersonality + "\n" + r.context + " 너는 마을 사람들을 따라와 임시 대장간을 차렸다.";
        EditorUtility.SetDirty(smithChat);

        // 하연 의뢰의 파편 3개. 사냥터 안, 몬스터 출현 지점 근처.
        var zone = GameObject.Find(HubStoryIds.Zone(r.hayeonZone));
        Vector2 center = zone != null ? (Vector2)zone.transform.position : Vector2.zero;
        var offsets = new[] { new Vector2(-9, 8), new Vector2(9, 8), new Vector2(0, 15) };
        for (int i = 0; i < 3; i++)
        {
            var fragment = Actor(npcs.transform, "fragment" + i, "기억 파편", center + offsets[i], null, r.fragmentLine);
            fragment.collectQuest = HayeonQuest(n); fragment.collectId = FragmentId(n, i);
            if (fragmentLook != null)
            {
                var visual = fragment.transform.Find("외형").GetComponent<SpriteRenderer>();
                visual.sprite = fragmentLook.sprite; visual.color = fragmentLook.color; visual.transform.localScale = fragmentLook.transform.localScale;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>의뢰 NPC 에 LLM 잡담을 붙인다. 가까이 가도 자동으로 열지 않고 E 로 의뢰가 없을 때 연다.</summary>
    static void Chat(HubStoryActor actor, string personality)
    {
        var chat = actor.GetComponent<NPCInteraction>() ?? actor.gameObject.AddComponent<NPCInteraction>();
        chat.npcName = actor.displayName; chat.personality = personality; chat.openOnApproach = false;
        EditorUtility.SetDirty(chat);
    }

    static HubStoryActor Actor(Transform parent, string id, string name, Vector2 position, Look look, string greeting)
    {
        var go = new GameObject(name); go.transform.SetParent(parent); go.transform.position = position;
        var actor = go.AddComponent<HubStoryActor>(); actor.actorId = id; actor.displayName = name; actor.greeting = greeting; actor.hubIdleLines = false;
        var visual = new GameObject("외형").AddComponent<SpriteRenderer>(); visual.transform.SetParent(go.transform, false);
        visual.sortingOrder = 100;
        if (look != null) { visual.sprite = look.sprite; visual.transform.localScale = Vector3.one * look.scale; }
        var label = new GameObject("안내").AddComponent<TextMeshPro>(); label.transform.SetParent(go.transform, false);
        label.transform.localPosition = Vector3.up * 1.3f;
        label.font = Resources.Load<TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF");
        label.text = name + " · E"; label.fontSize = 3; label.alignment = TextAlignmentOptions.Center; label.color = new Color(1, .92f, .72f);
        label.rectTransform.sizeDelta = new Vector2(14, 3); label.GetComponent<MeshRenderer>().sortingOrder = 30000;
        return actor;
    }

    static QuestObjective Goal(ObjectiveType type, string id, string description, int count = 1)
        => new QuestObjective { type = type, targetId = id, description = description, requiredCount = count };

    static QuestData Quest(string id, string title, int region, float exp, string start, string complete, params QuestObjective[] goals)
    {
        string path = Data + "/" + id + ".asset";
        var quest = AssetDatabase.LoadAssetAtPath<QuestData>(path);
        if (quest == null) { quest = ScriptableObject.CreateInstance<QuestData>(); AssetDatabase.CreateAsset(quest, path); }
        quest.questId = id; quest.title = title; quest.isleIndex = Mathf.Min(region, 5); quest.isMainQuest = false; quest.autoStart = false;
        quest.prerequisiteQuestIds = new string[0];
        quest.objectives = goals; quest.summary = string.Join("\n", goals.Select(g => "· " + g.description));
        quest.startLine = start; quest.completeLine = complete;
        quest.onComplete = new QuestOutcome { expReward = exp, memoryLine = complete };
        EditorUtility.SetDirty(quest);
        return quest;
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AINPC.MapGen;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>마을과 네 사냥터의 스토리 연결을 씬 에셋으로 제작한다.</summary>
public static class HubStorySetup
{
    const string Data = "Assets/Game/Data/HubStory";
    static QuestData[] quests;
    static Sprite civilianSprite;
    static readonly System.Collections.Generic.Dictionary<Type,string> playerSettings = new System.Collections.Generic.Dictionary<Type,string>();
    static void CapturePlayer()
    {
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
        foreach (Type type in new[] { typeof(Player_Attack),typeof(MoodSystem),typeof(ControlManager),typeof(AIController),typeof(PlayerStats) })
        { var component=player.GetComponent(type); if(component!=null)playerSettings[type]=EditorJsonUtility.ToJson(component); }
    }
    static void EquipPlayer()
    {
        var player=UnityEngine.Object.FindFirstObjectByType<Player_Controller>().gameObject;
        foreach (var pair in playerSettings) { var component=player.GetComponent(pair.Key)??player.AddComponent(pair.Key); EditorJsonUtility.FromJsonOverwrite(pair.Value,component); }
        if(player.GetComponent<RpgSkillController>()==null)player.AddComponent<RpgSkillController>();
    }
    static void FitActor(HubStoryActor actor)
    {
        var old=actor.GetComponent<SpriteRenderer>();
        if(old==null)return;
        Sprite sprite=old.sprite;
        if(!string.IsNullOrEmpty(actor.collectId)) sprite=AssetDatabase.LoadAllAssetsAtPath("Assets/testAsset/River/ground_decorations/rock2.png").OfType<Sprite>().FirstOrDefault()??sprite;
        if(actor.actorId=="hub_soyun")sprite=civilianSprite;
        var visual=At("외형",actor.transform.position,actor.transform).AddComponent<SpriteRenderer>();visual.sprite=sprite;visual.sortingOrder=100;
        float height=string.IsNullOrEmpty(actor.collectId)?(actor.actorId=="hub_soyun"?1.0f:1.4f):.55f;
        visual.transform.localScale=Vector3.one*(height/Mathf.Max(.01f,sprite.bounds.size.y));
        if(!string.IsNullOrEmpty(actor.collectId))visual.color=new Color(.65f,1,.95f);
        UnityEngine.Object.DestroyImmediate(old); actor.transform.localScale=Vector3.one;
    }
    static void FinalizeScene()
    {
        foreach(var actor in UnityEngine.Object.FindObjectsByType<HubStoryActor>(FindObjectsSortMode.None))FitActor(actor);
        var events=UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.InstanceID);
        for(int i=1;i<events.Length;i++) { events[i].enabled=false;EditorUtility.SetDirty(events[i]); }
    }
    static readonly string[] names = { "", "잿빛 숲", "흔적의 강가", "무너진 피난처", "불씨의 야영지" };
    static void Set(UnityEngine.Object obj, string field, object value)
    {
        obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(obj, value);
        EditorUtility.SetDirty(obj);
    }
    static QuestObjective Goal(ObjectiveType type, string id, string description, int count = 1)
        => new QuestObjective { type = type, targetId = id, description = description, requiredCount = count };
    static QuestData Quest(string id, string title, string prior, bool automatic, float xp, string start, string complete, params QuestObjective[] goals)
    {
        string path = Data + "/" + id + ".asset";
        var q = AssetDatabase.LoadAssetAtPath<QuestData>(path);
        if (q == null) { q = ScriptableObject.CreateInstance<QuestData>(); AssetDatabase.CreateAsset(q, path); }
        q.questId = id; q.title = title; q.isleIndex = 1; q.autoStart = automatic;
        q.prerequisiteQuestIds = string.IsNullOrEmpty(prior) ? Array.Empty<string>() : new[] { prior };
        q.objectives = goals; q.summary = string.Join("\n", goals.Select(g => "· " + g.description));
        q.startLine = start; q.completeLine = complete; q.onComplete = new QuestOutcome { expReward = xp, memoryLine = complete };
        EditorUtility.SetDirty(q); return q;
    }
    static void Catalog()
    {
        Directory.CreateDirectory(Data); AssetDatabase.Refresh();
        quests = new[] {
            Quest(HubStoryIds.Arrival, "돌아온 마을", "", true, 0, "", "익숙한 마을에 낯선 재 냄새가 남아 있다.", Goal(ObjectiveType.Reach, "hub_village", "마을에 도착하기")),
            Quest(HubStoryIds.Testimony, "없었다는 화재", HubStoryIds.Arrival, true, 0, "", "촌장은 불이 난 적이 없다고 말한다.", Goal(ObjectiveType.Talk, HubStoryIds.Elder, "촌장 현묵과 이야기하기")),
            Quest(HubStoryIds.Hunt(1), "잿빛 숲의 기척", HubStoryIds.Testimony, false, 80,
                "불이라니, 그런 일은 없었다. 다만 북서쪽 숲길에 요물이 모였으니 길을 열어 다오.", "숲길은 다시 열렸다. 하지만 검끝에 남은 재는 촌장의 말과 달랐다.",
                Goal(ObjectiveType.Reach, HubStoryIds.Area(1), "북서쪽 잿빛 숲에 도착하기"), Goal(ObjectiveType.Kill, HubStoryIds.Monster(1), "숲의 요물 처치", 3), Goal(ObjectiveType.Talk, HubStoryIds.Elder, "촌장에게 보고하기")),
            Quest(HubStoryIds.Hunt(2), "물에 잠긴 기억", HubStoryIds.Hunt(1), false, 150,
                "저는 분명 그 불을 봤어요. 북동쪽 강가에 남은 파편을 가져오면 누가 거짓말을 하는지 알 수 있을 거예요.", "파편 속에는 불타는 마을과 도망치는 아이의 그림자가 있었다. 하연은 그 아이가 아직 살아 있다고 말했다.",
                Goal(ObjectiveType.Reach, HubStoryIds.Area(2), "북동쪽 흔적의 강가에 도착하기"), Goal(ObjectiveType.Collect, "hub_fragment_a", "서쪽 기억 파편 조사"), Goal(ObjectiveType.Collect, "hub_fragment_b", "동쪽 기억 파편 조사"), Goal(ObjectiveType.Collect, "hub_fragment_c", "북쪽 기억 파편 조사"), Goal(ObjectiveType.Talk, HubStoryIds.Hayeon, "하연에게 파편의 기억 전하기")),
            Quest(HubStoryIds.Hunt(3), "이번에는 놓지 않는다", HubStoryIds.Hunt(2), false, 140,
                "남서쪽 피난처에 소윤이가 숨어 있어요. 요물들을 물리치고 꼭 아이를 구해 주세요.", "소윤은 무사하다. 아이가 본 불은 자연의 불이 아니었다. 누군가 마을의 기억까지 태우고 있었다.",
                Goal(ObjectiveType.Reach, HubStoryIds.Area(3), "남서쪽 무너진 피난처에 도착하기"), Goal(ObjectiveType.Kill, HubStoryIds.Monster(3), "피난처의 요물 처치", 3), Goal(ObjectiveType.Choice, HubStoryIds.Rescue, "소윤과 대화하고 구하기"), Goal(ObjectiveType.Talk, HubStoryIds.Hayeon, "하연에게 소윤의 무사함 알리기")),
            Quest(HubStoryIds.Hunt(4), "재 속에 남은 이름", HubStoryIds.Hunt(3), false, 180,
                "더는 감출 수 없겠구나. 남동쪽 야영지에서 마을을 태운 자들의 흔적을 찾아 다오.", "야영지의 흔적은 재의 왕을 가리킨다. 현묵은 진실을 인정했다. 남쪽 길이 열렸다. 제단에서 정비하고 그 불을 끝내자.",
                Goal(ObjectiveType.Reach, HubStoryIds.Area(4), "남동쪽 불씨의 야영지에 도착하기"), Goal(ObjectiveType.Kill, HubStoryIds.Monster(4), "야영지의 적 처치", 5), Goal(ObjectiveType.Reach, "hub_enemy_camp", "야영지 북쪽 흔적 확인"), Goal(ObjectiveType.Talk, HubStoryIds.Elder, "촌장에게 진실 묻기"))
        }; AssetDatabase.SaveAssets();
    }
    static GameObject At(string name, Vector2 p, Transform parent = null)
    { var go = new GameObject(name); go.transform.position = p; if (parent != null) go.transform.SetParent(parent, true); return go; }
    static void Label(Transform parent, string text)
    {
        var go = At("안내", (Vector2)parent.position + Vector2.up * 1.3f, parent);
        var label = go.AddComponent<TextMeshPro>(); label.font = Resources.Load<TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF");
        label.text = text; label.fontSize = 3; label.alignment = TextAlignmentOptions.Center; label.color = new Color(1, .92f, .72f);
        label.rectTransform.sizeDelta = new Vector2(14, 3); label.GetComponent<MeshRenderer>().sortingOrder = 30000;
    }
    static HubStoryActor Actor(Transform parent, string id, string name, Vector2 p, Sprite sprite, string greeting)
    {
        var go = At(name, p, parent); var actor = go.AddComponent<HubStoryActor>(); actor.actorId = id; actor.displayName = name; actor.greeting = greeting;
        var render = go.AddComponent<SpriteRenderer>(); render.sprite = sprite; render.sortingOrder = 100;
        Label(go.transform, name + " · E"); return actor;
    }
    static void Spawn(Transform parent, string id, Vector2 p) => At("출발점 " + id, p, parent).AddComponent<SpawnPoint>().id = id;
    static void Portal(Transform parent, Vector2 p, string scene, string spawn, string label, string prerequisite = "")
    {
        var go = At(label, p, parent); var collider = go.AddComponent<BoxCollider2D>(); collider.isTrigger = true; collider.size = new Vector2(2, 2);
        var portal = go.AddComponent<ScenePortal>(); Set(portal, "targetScene", scene); Set(portal, "targetSpawn", spawn);
        Set(portal, "requiredQuestId", prerequisite); Set(portal, "lockedMessage", "먼저 마을의 촌장 현묵과 이야기하자."); Label(go.transform, label + "\n이곳으로 이동");
    }
    static void Systems(Scene scene, bool full)
    {
        var system = new GameObject("HubQuestSystem"); var qm = system.AddComponent<QuestManager>(); Set(qm, "quests", quests);
        system.AddComponent<StoryChoiceManager>(); var log = system.AddComponent<QuestLogUI>();
        var uiRoot = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "UIRoot") ?? new GameObject("UIRoot");
        var story = At("스토리 대화", Vector2.zero, uiRoot.transform).AddComponent<StoryConversationUI>(); story.Bake(uiRoot.transform);
        if (full) typeof(SceneUiBaker).GetMethod("Bake", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { scene });
        else { var before = scene.GetRootGameObjects(); log.BakeSceneUI(); foreach (var root in scene.GetRootGameObjects().Except(before)) root.transform.SetParent(uiRoot.transform, false); }
    }
    static Tilemap Roads(Transform parent)
    {
        var grid = At("길", Vector2.zero, parent); grid.AddComponent<Grid>();
        var map = At("마을 연결길", Vector2.zero, grid.transform).AddComponent<Tilemap>(); map.gameObject.AddComponent<TilemapRenderer>().sortingOrder = -9; return map;
    }
    static void Road(Tilemap map, Vector2 a, Vector2 b)
    {
        var tile = AssetDatabase.LoadAssetAtPath<TileBase>(MapGenSettings.Assets.PathRuleTile);
        int count = Mathf.CeilToInt(Vector2.Distance(a, b) * 2);
        for (int i = 0; i <= count; i++) { Vector2 p = Vector2.Lerp(a, b, count == 0 ? 0 : (float)i / count); for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) map.SetTile(new Vector3Int(Mathf.RoundToInt(p.x)+x, Mathf.RoundToInt(p.y)+y,0), tile); }
    }
    static void Main()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
        if (scene.GetRootGameObjects().Any(g => g.name == "HubStory")) throw new InvalidOperationException("이미 연결된 Main입니다.");
        var root = new GameObject("HubStory"); Vector2 center = UnityEngine.Object.FindFirstObjectByType<PlayerStats>().transform.position;
        var elderSprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Characters/Samurai/Sprites/IDLE.png").OfType<Sprite>().First();
        var woman = GameObject.Find("Kaguya")?.GetComponent<SpriteRenderer>()?.sprite ?? elderSprite;
        civilianSprite = woman;
        CapturePlayer();
        Actor(root.transform, HubStoryIds.Elder, "촌장 현묵", center + new Vector2(-4, 1), elderSprite,
            "돌아왔구나. 마을은 네가 떠났을 때 그대로다. 불이 났다는 소문은 잊거라.").offers = new[] { HubStoryIds.Hunt(1), HubStoryIds.Hunt(4) };
        Actor(root.transform, HubStoryIds.Hayeon, "생존자 하연", center + new Vector2(4, 1), woman,
            "모두 잊었다고 해도 저는 기억해요. 이 마을에서 무슨 일이 있었는지 함께 밝혀 주세요.").offers = new[] { HubStoryIds.Hunt(2), HubStoryIds.Hunt(3) };
        Spawn(root.transform, GameFlow.DefaultSpawn, center);
        var village = At("마을 도착", center, root.transform); village.AddComponent<CircleCollider2D>().isTrigger = true; village.GetComponent<CircleCollider2D>().radius = 9;
        var entry = village.AddComponent<HubAreaEntry>(); entry.areaId = "hub_village"; entry.mainVillage = true;
        var road = Roads(root.transform);
        var offsets = new[] { new Vector2(-20,18), new Vector2(20,18), new Vector2(-20,-18), new Vector2(20,-18) };
        for (int n = 1; n <= 4; n++) { var p = center + offsets[n-1]; Portal(root.transform,p,HubStoryIds.Scene(n),GameFlow.DefaultSpawn,n+" · "+names[n],HubStoryIds.Testimony); Spawn(root.transform,HubStoryIds.ReturnSpawn(n),p+(center-p).normalized*4); Road(road,center,p); }
        Systems(scene, false); FinalizeScene(); EditorSceneManager.SaveScene(scene);
    }
    static void Hunting(int n)
    {
        var settings = new MapGenSettings { width = 64, height = 56, seed = 20261005+n*179, village = false, river = n==2, lakeCount = n==2 ? 2 : 0, treeDensity = n==1 ? .15f : n==4 ? .04f : .08f, decorDensity = .06f };
        var plan = MapBuilder.Build(settings); plan.spawnPoint = new Vector2(0,-20);
        // 모든 목표와 입구를 연결하는 넓은 마른 길. 소품과 충돌도 함께 비운다.
        for (int x = 0; x < plan.width; x++) for (int y = 0; y < plan.height; y++)
        {
            Vector2 p = plan.World(x,y);
            bool corridor = Mathf.Abs(p.x)<=3 && p.y>=-25 && p.y<=17 || Mathf.Abs(p.y-8)<=3 && Mathf.Abs(p.x)<=14 || Mathf.Abs(p.y+5)<=3 && Mathf.Abs(p.x)<=14;
            if (corridor) { plan.water[x,y] = false; plan.block[x,y] = false; plan.path[x,y] = true; }
        }
        plan.props.RemoveAll(p => Mathf.Abs(p.bottomCenter.x)<=5 || Mathf.Abs(p.bottomCenter.y-8)<=5 || Mathf.Abs(p.bottomCenter.y+5)<=5);
        string path = "Assets/Scenes/"+HubStoryIds.Scene(n)+".unity";
        if (!MapSceneBuilder.CreateScene(settings,plan,path,out var report)) throw new InvalidOperationException(report);
        var scene = SceneManager.GetActiveScene(); var root = new GameObject("HubStory");
        EquipPlayer();
        Spawn(root.transform,GameFlow.DefaultSpawn,plan.spawnPoint); Portal(root.transform,new Vector2(0,-24),"Main",HubStoryIds.ReturnSpawn(n),"마을로 돌아가기");
        root.AddComponent<HubAreaEntry>().areaId = HubStoryIds.Area(n);
        Label(At("지역명",new Vector2(0,-17),root.transform).transform,names[n]);
        string monster = n==1 ? "Mushroom" : n==2 ? "Bat" : n==3 ? "Skeleton" : "Goblin";
        var original = AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/Data/Monsters/"+monster+"Data.asset");
        var data = UnityEngine.Object.Instantiate(original); Set(data,"monsterName",HubStoryIds.Monster(n));
        AssetDatabase.CreateAsset(data,Data+"/Enemy"+n+".asset");
        var area = At("사냥터 적",Vector2.zero,root.transform).AddComponent<MonsterSpawnArea>();
        Set(area,"monsterPrefab",AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monsters/"+monster+".prefab").GetComponent<MonsterBase>()); Set(area,"monsterData",data); Set(area,"spawnInterval",18f);
        var points = new[] { new Vector2(-9,-5),new Vector2(9,-5),new Vector2(-9,8),new Vector2(9,8),new Vector2(0,15) };
        Set(area,"spawnPoints",points.Select((p,i)=>At("적 출현 "+i,p,area.transform).AddComponent<MonsterSpawnPoint>()).ToArray());
        var icon = Resources.Load<Sprite>("UiArt/Skill0");
        if (n==2) for (int i=0;i<3;i++) { var a = Actor(root.transform,"fragment"+i,"기억 파편",new[] { new Vector2(-9,8),new Vector2(9,8),new Vector2(0,15) }[i],icon,"타버린 목패를 만지자 잊힌 화재의 기억이 스쳐 간다."); a.collectQuest=HubStoryIds.Hunt(2); a.collectId="hub_fragment_"+(char)('a'+i); }
        if (n==3) { var a=Actor(root.transform,"hub_soyun","피난민 소윤",new Vector2(0,12),civilianSprite,"무서워요. 하연 언니는 무사한가요?"); a.rescueQuest=HubStoryIds.Hunt(3); a.rescueChoice=HubStoryIds.Rescue; a.transform.localScale=Vector3.one*.7f; }
        if (n==4) { var go=At("야영지 흔적",new Vector2(0,12),root.transform); go.AddComponent<BoxCollider2D>().isTrigger=true; go.GetComponent<BoxCollider2D>().size=new Vector2(4,4); var zone=go.AddComponent<QuestTriggerZone>(); Set(zone,"targetId","hub_enemy_camp"); Set(zone,"requiresActiveQuestId",HubStoryIds.Hunt(4)); Set(zone,"once",false); Label(go.transform,"재의 왕의 문양"); }
        Systems(scene,true); FinalizeScene(); EditorSceneManager.SaveScene(scene); Debug.Log(report);
    }
    public static void ApplyAndVerify()
    {
        try { Catalog(); Main(); for(int n=1;n<=4;n++) Hunting(n);
            var builds=EditorBuildSettings.scenes.ToList(); for(int n=1;n<=4;n++) { string path="Assets/Scenes/"+HubStoryIds.Scene(n)+".unity"; if(!builds.Any(b=>b.path==path)) builds.Add(new EditorBuildSettingsScene(path,true)); }
            EditorBuildSettings.scenes=builds.ToArray(); AssetDatabase.SaveAssets(); HubStoryVerification.Run();
            EditorApplication.Exit(0);
        } catch(Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    public static void RepairAndVerify()
    {
        try {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity",OpenSceneMode.Single);CapturePlayer();civilianSprite=GameObject.Find("Kaguya").GetComponent<SpriteRenderer>().sprite;
            foreach(string scene in new[]{"Main","HuntingGround1","HuntingGround2","HuntingGround3","HuntingGround4"}) {
                var loaded=EditorSceneManager.OpenScene("Assets/Scenes/"+scene+".unity",OpenSceneMode.Single);
                foreach(var actor in UnityEngine.Object.FindObjectsByType<HubStoryActor>(FindObjectsSortMode.None))FitActor(actor);
                if(scene!="Main") {EquipPlayer();typeof(SceneUiBaker).GetMethod("Bake",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{loaded});}
                FinalizeScene();EditorSceneManager.SaveScene(loaded);
            }
            HubStoryVerification.Run();EditorApplication.Exit(0);
        } catch(Exception ex) {Debug.LogException(ex);EditorApplication.Exit(1);}
    }
}


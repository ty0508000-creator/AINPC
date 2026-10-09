using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// docs/STAGE_STRUCTURE.md 의 지역 틀(마을 + 사냥터 4곳 + 중간보스)을 씬에 깐다.
/// 지형은 회색 바닥만 깔고, 이동·포탈·보스 처치·부활 흐름만 확인할 수 있는 뼈대다.
///
/// - Main: 기존 마을은 그대로 두고, 입구·포탈·체크포인트와 떨어진 구역 5개를 덧붙인다.
/// - Region2~4, FinalBoss: 파일이 없을 때만 새로 만든다. 이미 있으면 건드리지 않는다.
///
/// 메뉴를 실행하면 <b>열려 있는 씬이 닫히므로</b> 먼저 저장해야 한다.
/// </summary>
public static class StageSceneBuilder
{
    const string SceneDir = "Assets/Scenes";
    const string TitlePath = SceneDir + "/Title.unity";
    const string CavePath = SceneDir + "/Cave.unity";
    const string MainPath = SceneDir + "/Main.unity";
    const string FinalBossScene = "FinalBoss";

    const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    const string MonsterPrefabDir = "Assets/Prefabs/Monsters";
    const string MonsterDataDir = "Assets/Data/Monsters";
    const string BossDataDir = MonsterDataDir + "/Bosses";
    const string PortalSprite = "Assets/Sprites/UI/CaveExit.png";
    const string FloorSpritePath = "Assets/Sprites/Stage/ZoneFloor.png";

    // 구역 중심과 크기 (문서 3장)
    static readonly Vector2 HuntSize = new Vector2(80f, 80f);
    static readonly Vector2 BossSize = new Vector2(50f, 50f);
    static readonly Vector2 VillageSize = new Vector2(80f, 60f);
    static readonly Vector2[] HuntCenters =
    {
        new Vector2(-200f, 200f), new Vector2(200f, 200f),
        new Vector2(-200f, -200f), new Vector2(200f, -200f),
    };
    static readonly Vector2 BossCenter = new Vector2(0f, -400f);

    /// <summary>사냥터 N 에 나오는 몬스터. 밸런스 작업 때 지역별로 바꾼다.</summary>
    static readonly string[] HuntMonsters = { "Mushroom", "Goblin", "Bat", "Skeleton" };

    /// <summary>씬 하나의 설정. Main 은 order 1.</summary>
    class Region
    {
        public int order;
        public string scene;
        public string prevScene;   // 뒤로 가는 포탈. 없으면 null
        public string nextScene;   // 보스 구역의 앞으로 가는 포탈
        public string bossMonster;
        public string ClearFlag => $"region{order}.boss_cleared";
    }

    static readonly Region[] Regions =
    {
        new Region { order = 1, scene = "Main",    prevScene = null,      nextScene = "Region2", bossMonster = "Skeleton" },
        new Region { order = 2, scene = "Region2", prevScene = "Main",    nextScene = "Region3", bossMonster = "Goblin" },
        new Region { order = 3, scene = "Region3", prevScene = "Region2", nextScene = "Region4", bossMonster = "Bat" },
        new Region { order = 4, scene = "Region4", prevScene = "Region3", nextScene = FinalBossScene, bossMonster = "Mushroom" },
    };

    [MenuItem("Tools/AINPC/스테이지 씬 만들기")]
    public static void BuildAll()
    {
        if (EditorApplication.isPlaying)
            throw new System.InvalidOperationException("Play를 종료한 뒤 실행하세요.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Sprite floor = EnsureFloorSprite();

        foreach (Region region in Regions)
        {
            if (region.scene == "Main")
                AddZonesToMain(region, floor);
            else
                CreateRegionScene(region, floor);
        }

        CreateFinalBossScene(floor);
        RegisterBuildScenes();
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Single);
        Debug.Log("[Stage] 스테이지 씬 준비 완료. Cave → Main → Region2 → Region3 → Region4 → FinalBoss");
    }

    // ── 바닥 스프라이트 ─────────────────────────────────────────

    /// <summary>구역 바닥으로 쓸 1×1 유닛 흰 사각형. 색은 SpriteRenderer 에서 입힌다.</summary>
    static Sprite EnsureFloorSprite()
    {
        if (!File.Exists(FloorSpritePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FloorSpritePath));
            var texture = new Texture2D(16, 16);
            texture.SetPixels(Enumerable.Repeat(Color.white, 16 * 16).ToArray());
            File.WriteAllBytes(FloorSpritePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(FloorSpritePath);
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(FloorSpritePath);
        if (importer.textureType != TextureImporterType.Sprite || importer.spritePixelsPerUnit != 16f)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 16f;
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(FloorSpritePath);
    }

    // ── Main: 기존 마을에 구역 덧붙이기 ─────────────────────────

    static void AddZonesToMain(Region region, Sprite floor)
    {
        Scene scene = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Single);
        if (scene.GetRootGameObjects().Any(go => go.name == "Zone_Hunt1"))
        {
            Debug.Log("[Stage] Main 에는 이미 구역이 있어 건너뜁니다.");
            return;
        }

        // 마을은 이미 지형이 깔려 있으므로 옮기지 않는다. 동굴에서 나오는 자리를 마을 중심으로 삼는다.
        SpawnPoint caveExit = scene.GetRootGameObjects()
            .SelectMany(go => go.GetComponentsInChildren<SpawnPoint>(true))
            .FirstOrDefault(p => p.id == "cave_exit");
        Vector2 center = caveExit != null ? (Vector2)caveExit.transform.position : Vector2.zero;

        BuildVillage(scene, region, center, floor: null);
        BuildHuntAndBossZones(scene, region, floor);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // ── Region2~4: 새 씬 ───────────────────────────────────────

    static void CreateRegionScene(Region region, Sprite floor)
    {
        string path = $"{SceneDir}/{region.scene}.unity";
        if (File.Exists(path))
        {
            Debug.Log($"[Stage] {region.scene} 은 이미 있어 건너뜁니다.");
            return;
        }

        Scene scene = NewPlayScene();
        BuildVillage(scene, region, Vector2.zero, floor);
        BuildHuntAndBossZones(scene, region, floor);
        SavePlayScene(scene, path);
    }

    // ── FinalBoss: 보스방 하나 ──────────────────────────────────

    static void CreateFinalBossScene(Sprite floor)
    {
        string path = $"{SceneDir}/{FinalBossScene}.unity";
        if (File.Exists(path))
        {
            Debug.Log("[Stage] FinalBoss 는 이미 있어 건너뜁니다.");
            return;
        }

        Scene scene = NewPlayScene();
        Transform zone = Zone(scene, "Zone_FinalBoss", Vector2.zero, BossSize, floor, new Color(0.25f, 0.18f, 0.2f));

        // 들어오는 쪽(아래)에 입구, 왼쪽 아래에 4지역으로 돌아가는 포탈
        Spawn(zone, "Spawn_default", new Vector2(0f, -18f), GameFlow.DefaultSpawn);
        Spawn(zone, "Spawn_fromNext", new Vector2(0f, -14f), "from_next");
        ScenePortalAt(zone, "Portal_ToPrevRegion", new Vector2(-14f, -22f), "Region4", "from_next", null);
        Checkpoint(zone, "Checkpoint_Entrance", new Vector2(0f, -16f), new Vector2(12f, 8f), zone.Find("Spawn_default"));
        Boss(zone, new Vector2(0f, 8f), "Skeleton", "FinalBoss", 12, "final.boss_cleared", "최종보스를 쓰러뜨렸다!");

        SavePlayScene(scene, path);
    }

    // ── 구역 구성 ───────────────────────────────────────────────

    /// <summary>마을 입구·포탈·체크포인트. floor 가 null 이면 바닥을 깔지 않는다 (Main).</summary>
    static void BuildVillage(Scene scene, Region region, Vector2 center, Sprite floor)
    {
        Transform village = Zone(scene, "Zone_Village", center, VillageSize, floor, new Color(0.32f, 0.42f, 0.3f));

        // 입구 두 개는 마을 한가운데, 포탈은 모두 10칸 이상 떨어뜨린다
        Transform spawn = Spawn(village, "Spawn_village", Vector2.zero, GameFlow.DefaultSpawn);
        Spawn(village, "Spawn_fromNext", new Vector2(3f, 0f), "from_next");
        Checkpoint(village, "Checkpoint_Village", new Vector2(1.5f, 0f), new Vector2(12f, 8f), spawn);

        ZonePortalAt(village, "Portal_ToHunt1", new Vector2(-12f, 10f), "hunt1");
        ZonePortalAt(village, "Portal_ToHunt2", new Vector2(12f, 10f), "hunt2");
        ZonePortalAt(village, "Portal_ToHunt3", new Vector2(-12f, -10f), "hunt3");
        ZonePortalAt(village, "Portal_ToHunt4", new Vector2(12f, -10f), "hunt4");
        ZonePortalAt(village, "Portal_ToBoss", new Vector2(0f, -14f), "boss");

        if (region.prevScene != null)
            ScenePortalAt(village, "Portal_ToPrevRegion", new Vector2(-24f, 0f), region.prevScene, "from_next", null);
    }

    static void BuildHuntAndBossZones(Scene scene, Region region, Sprite floor)
    {
        for (int i = 0; i < HuntCenters.Length; i++)
        {
            int n = i + 1;
            Transform hunt = Zone(scene, $"Zone_Hunt{n}", HuntCenters[i], HuntSize, floor, new Color(0.3f, 0.36f, 0.28f));
            Spawn(hunt, $"Spawn_hunt{n}", new Vector2(0f, -28f), $"hunt{n}");
            ZonePortalAt(hunt, "Portal_ToVillage", new Vector2(0f, -36f), GameFlow.DefaultSpawn);
            MonsterArea(hunt, HuntMonsters[i]);
        }

        Transform boss = Zone(scene, "Zone_Boss", BossCenter, BossSize, floor, new Color(0.3f, 0.24f, 0.24f));
        Spawn(boss, "Spawn_boss", new Vector2(0f, -16f), "boss");
        ZonePortalAt(boss, "Portal_ToVillage", new Vector2(-8f, -21f), GameFlow.DefaultSpawn);
        ScenePortalAt(boss, "Portal_ToNextRegion", new Vector2(0f, 21f), region.nextScene, GameFlow.DefaultSpawn, region.ClearFlag);
        Boss(boss, new Vector2(0f, 6f), region.bossMonster, $"Region{region.order}Boss", region.order * 2 + 2,
             region.ClearFlag, "다음 지역으로 가는 길이 열렸다.");
    }

    // ── 부품 ────────────────────────────────────────────────────

    /// <summary>구역 루트. 바닥과 바깥 벽을 깔아 걸어서 빠져나가지 못하게 한다.</summary>
    static Transform Zone(Scene scene, string name, Vector2 center, Vector2 size, Sprite floor, Color color)
    {
        var root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = center;

        if (floor == null)
            return root.transform;

        var ground = new GameObject("Floor");
        ground.transform.SetParent(root.transform, false);
        ground.transform.localScale = new Vector3(size.x, size.y, 1f);
        var renderer = ground.AddComponent<SpriteRenderer>();
        renderer.sprite = floor;
        renderer.color = color;
        renderer.sortingOrder = -20;   // 타일맵(-10~-7)보다 아래

        var walls = new GameObject("Walls");
        walls.transform.SetParent(root.transform, false);
        const float t = 2f;
        Wall(walls, new Vector2(0f, size.y / 2f + t / 2f), new Vector2(size.x + t * 2f, t));
        Wall(walls, new Vector2(0f, -size.y / 2f - t / 2f), new Vector2(size.x + t * 2f, t));
        Wall(walls, new Vector2(-size.x / 2f - t / 2f, 0f), new Vector2(t, size.y));
        Wall(walls, new Vector2(size.x / 2f + t / 2f, 0f), new Vector2(t, size.y));

        return root.transform;
    }

    static void Wall(GameObject parent, Vector2 offset, Vector2 size)
    {
        var box = parent.AddComponent<BoxCollider2D>();
        box.offset = offset;
        box.size = size;
    }

    static Transform Spawn(Transform zone, string name, Vector2 local, string id)
    {
        var go = Child(zone, name, local);
        go.AddComponent<SpawnPoint>().id = id;
        return go.transform;
    }

    static void Checkpoint(Transform zone, string name, Vector2 local, Vector2 size, Transform respawn)
    {
        var go = Child(zone, name, local);
        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = size;
        var checkpoint = go.AddComponent<PlayerCheckpoint>();
        var so = new SerializedObject(checkpoint);
        so.FindProperty("respawnPoint").objectReferenceValue = respawn;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static GameObject PortalBody(Transform zone, string name, Vector2 local, Color tint)
    {
        var go = Child(zone, name, local);
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PortalSprite);
        renderer.color = tint;
        go.AddComponent<YSortRenderer>();
        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(1.2f, 1.2f);
        return go;
    }

    static void ZonePortalAt(Transform zone, string name, Vector2 local, string targetSpawn)
    {
        var go = PortalBody(zone, name, local, new Color(0.7f, 1f, 0.8f));
        var so = new SerializedObject(go.AddComponent<ZonePortal>());
        so.FindProperty("targetSpawn").stringValue = targetSpawn;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ScenePortalAt(Transform zone, string name, Vector2 local, string targetScene, string targetSpawn, string requiredFlag)
    {
        var go = PortalBody(zone, name, local, new Color(1f, 0.85f, 0.5f));
        var so = new SerializedObject(go.AddComponent<ScenePortal>());
        so.FindProperty("targetScene").stringValue = targetScene;
        so.FindProperty("targetSpawn").stringValue = targetSpawn;
        so.FindProperty("requiredFlag").stringValue = requiredFlag ?? string.Empty;
        so.FindProperty("lockedMessage").stringValue = "보스를 쓰러뜨려야 지나갈 수 있다.";
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>구역 안에 몬스터 스폰 지점 6개를 고르게 흩어 놓는다.</summary>
    static void MonsterArea(Transform zone, string monster)
    {
        var area = Child(zone, "MonsterSpawnArea_" + monster, Vector2.zero);
        var points = new List<MonsterSpawnPoint>();
        Vector2[] offsets =
        {
            new Vector2(-20f, 20f), new Vector2(0f, 24f), new Vector2(20f, 20f),
            new Vector2(-22f, -4f), new Vector2(0f, 4f), new Vector2(22f, -4f),
        };
        for (int i = 0; i < offsets.Length; i++)
            points.Add(Child(area.transform, $"Point_{i + 1}", offsets[i]).AddComponent<MonsterSpawnPoint>());

        var spawner = area.AddComponent<MonsterSpawnArea>();
        var so = new SerializedObject(spawner);
        so.FindProperty("monsterPrefab").objectReferenceValue = LoadMonsterPrefab(monster);
        so.FindProperty("monsterData").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<MonsterData>($"{MonsterDataDir}/{monster}Data.asset");
        var array = so.FindProperty("spawnPoints");
        array.arraySize = points.Count;
        for (int i = 0; i < points.Count; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Boss(Transform zone, Vector2 local, string monster, string dataName, int level, string flag, string message)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MonsterPrefabDir}/{monster}.prefab");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, zone.gameObject.scene);
        go.name = "Boss_" + dataName;
        go.transform.SetParent(zone, false);
        go.transform.localPosition = local;
        go.transform.localScale = Vector3.one * 2.5f;

        var monsterSo = new SerializedObject(go.GetComponent<MonsterBase>());
        monsterSo.FindProperty("monsterData").objectReferenceValue = EnsureBossData(monster, dataName, level);
        monsterSo.ApplyModifiedPropertiesWithoutUndo();

        var bossSo = new SerializedObject(go.AddComponent<StageBoss>());
        bossSo.FindProperty("clearFlag").stringValue = flag;
        bossSo.FindProperty("clearMessage").stringValue = message;
        bossSo.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 일반 몬스터 데이터를 바탕으로 보스 데이터를 만든다. 이미 있으면 그대로 쓴다 (밸런스는 에셋에서 고친다).
    /// </summary>
    static MonsterData EnsureBossData(string monster, string dataName, int level)
    {
        string path = $"{BossDataDir}/{dataName}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<MonsterData>(path);
        if (existing != null)
            return existing;

        Directory.CreateDirectory(BossDataDir);
        var source = AssetDatabase.LoadAssetAtPath<MonsterData>($"{MonsterDataDir}/{monster}Data.asset");
        var data = source != null ? Object.Instantiate(source) : ScriptableObject.CreateInstance<MonsterData>();
        AssetDatabase.CreateAsset(data, path);

        var so = new SerializedObject(data);
        so.FindProperty("monsterName").stringValue = dataName;
        so.FindProperty("level").intValue = level;
        so.FindProperty("maxHP").floatValue *= 10f;
        so.FindProperty("expReward").floatValue *= 10f;
        so.FindProperty("attackDamage").intValue *= 2;
        so.FindProperty("aggroRange").floatValue = 10f;
        so.ApplyModifiedPropertiesWithoutUndo();
        return data;
    }

    static MonsterBase LoadMonsterPrefab(string monster)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MonsterPrefabDir}/{monster}.prefab");
        return prefab != null ? prefab.GetComponent<MonsterBase>() : null;
    }

    static GameObject Child(Transform parent, string name, Vector2 local)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        return go;
    }

    // ── 씬 공통 ─────────────────────────────────────────────────

    /// <summary>카메라와 플레이어만 있는 빈 플레이 씬. Main 의 카메라 설정에 맞춘다.</summary>
    static Scene NewPlayScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraGo = new GameObject("Main Camera");
        cameraGo.tag = "MainCamera";
        var camera = cameraGo.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.backgroundColor = new Color(0.07f, 0.07f, 0.09f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        cameraGo.AddComponent<AudioListener>();
        cameraGo.AddComponent<CameraFollow>();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        player.transform.position = Vector3.zero;

        return scene;
    }

    static void SavePlayScene(Scene scene, string path)
    {
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, path);

        // UI(메뉴창·톱니바퀴·HUD·암전)를 씬 오브젝트로 굽는다
        SceneUiBaker.Bake(scene);
        EditorSceneManager.SaveScene(scene, path);
    }

    static void RegisterBuildScenes()
    {
        var wanted = new List<string> { TitlePath, CavePath, MainPath };
        wanted.AddRange(Regions.Skip(1).Select(r => $"{SceneDir}/{r.scene}.unity"));
        wanted.Add($"{SceneDir}/{FinalBossScene}.unity");

        // 문서 1장 순서대로 앞에 두고, 나머지 씬은 그 뒤에 원래 순서대로 남긴다
        var current = EditorBuildSettings.scenes.ToList();
        var ordered = new List<EditorBuildSettingsScene>();
        foreach (string path in wanted)
        {
            var found = current.FirstOrDefault(s => s.path == path);
            ordered.Add(found ?? new EditorBuildSettingsScene(path, true));
            ordered[ordered.Count - 1].enabled = true;
        }
        ordered.AddRange(current.Where(s => !wanted.Contains(s.path)));
        EditorBuildSettings.scenes = ordered.ToArray();
    }
}

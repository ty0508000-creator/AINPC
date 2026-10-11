using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// 지역(Stage1~4) 구조를 만든다. 순서대로 돌린다(각 단계는 다시 돌려도 결과가 같다).
/// 1. <see cref="BuildMonsters"/>: 지역마다 사냥터 4종(총 16종)과 보스 4종의 데이터·프리팹
/// 2. <see cref="WireStageScenes"/>: Stage2~4·최종보스 씬의 사냥터 몬스터와 보스를 1의 결과로 바꾼다
/// 3. <see cref="MergeHuntingGrounds"/>: HuntingGround1~4 씬을 Stage1 의 Zone_Hunt1~4 로 옮긴다
/// 4. <see cref="ConnectStage1"/>: 재의 왕 처치 후 Stage1 마을에서 Stage2 로 가는 문
/// 5. <see cref="RenameScenes"/>: 빌드 씬을 Assets/Scenes/Main 으로 옮기고 Stage1~4 로 이름을 바꾼다
/// 씬을 열고 닫으므로 저장 안 된 씬이 있으면 멈춘다. 수치 원본은 docs/HUNTING_BALANCE.md.
/// </summary>
public static class StageBuilder
{
    public const string SceneFolder = "Assets/Scenes/Main";
    const string MonsterFolder = "Assets/Data/Monsters";
    const string PrefabFolder = "Assets/Game/Prefabs/RegionalEnemies";

    /// <summary>사냥터 1~4 의 몸체. 1지역과 같은 순서라 같은 번호 사냥터는 같은 공격 방식(포자·탄막·베기·돌진)을 쓴다.</summary>
    public static readonly string[] Kinds = { "Mushroom", "Bat", "Skeleton", "Goblin" };

    /// <summary>지역별 이름. [지역 1~4][사냥터 1~4]. 1지역은 기존 Enemy1~4 다.</summary>
    public static readonly string[,] Names =
    {
        { "잿빛 버섯", "재 박쥐", "무덤 해골", "불씨 고블린" },
        { "독안개 버섯", "물안개 박쥐", "강바닥 해골", "물살 고블린" },
        { "보랏빛 버섯", "그림자 박쥐", "저주받은 해골", "망령 고블린" },
        { "화염 버섯", "불꽃 박쥐", "용암 해골", "화산 고블린" },
    };

    static readonly Color[] Tints = { Color.white, new Color(.62f, .95f, .88f), new Color(.86f, .7f, 1f), new Color(1f, .62f, .55f) };
    static readonly float[] Scales = { 1f, 1.1f, 1.2f, 1.3f };

    /// <summary>[지역 2~4][사냥터 1~4] = 레벨, 체력, 피해, 경험치. 경험치는 레벨당 2~3마리.</summary>
    public static readonly int[,,] Hunting =
    {
        { { 5, 85, 12, 203 }, { 6, 135, 19, 304 }, { 7, 240, 29, 456 }, { 8, 340, 38, 684 } },
        { { 9, 155, 23, 1026 }, { 10, 250, 37, 1539 }, { 11, 445, 55, 2309 }, { 12, 625, 73, 3463 } },
        { { 13, 255, 41, 5195 }, { 14, 410, 65, 7792 }, { 15, 730, 97, 11688 }, { 16, 1030, 130, 17532 } },
    };

    public struct BossSpec
    {
        public int stage, kind, level, hp, damage, exp, drop;
        public string data, prefab, flag, message;
    }

    /// <summary>지역 보스(2~4)와 최종보스. 몸체는 사냥터 몬스터를 키운 것이라 공격을 예고한다.</summary>
    public static readonly BossSpec[] Bosses =
    {
        new BossSpec { stage = 2, kind = 4, level = 9, hp = 8850, damage = 53, exp = 1280, drop = 3, data = "Bosses/Stage2Boss.asset", prefab = "Stage2_Boss", flag = "region2.boss_cleared", message = "물살의 우두머리가 쓰러졌다. 다음 지역으로 가는 길이 열렸다." },
        new BossSpec { stage = 3, kind = 3, level = 13, hp = 21150, damage = 101, exp = 6490, drop = 4, data = "Bosses/Stage3Boss.asset", prefab = "Stage3_Boss", flag = "region3.boss_cleared", message = "저주받은 기사가 무너졌다. 다음 지역으로 가는 길이 열렸다." },
        new BossSpec { stage = 4, kind = 2, level = 17, hp = 33500, damage = 178, exp = 32870, drop = 5, data = "Bosses/Stage4Boss.asset", prefab = "Stage4_Boss", flag = "region4.boss_cleared", message = "불꽃 날개가 꺼졌다. 마지막 길이 열렸다." },
        new BossSpec { stage = 5, kind = 3, level = 18, hp = 48250, damage = 295, exp = 0, drop = 0, data = "Bosses/FinalBoss.asset", prefab = "Final_Boss", flag = "final.boss_cleared", message = "모든 싸움이 끝났다." },
    };

    public static float ZoneX(int hunt) => 200f + 120f * (hunt - 1);

    // ── 경로 ────────────────────────────────────────────────────

    /// <summary>지역 씬 경로. 이름을 바꾸기 전(Main·Region2~4)이면 그 경로.</summary>
    public static string StagePath(int stage)
    {
        string moved = $"{SceneFolder}/Stage{stage}.unity";
        if (File.Exists(moved)) return moved;
        return stage == 1 ? "Assets/Scenes/Main.unity" : $"Assets/Scenes/Region{stage}.unity";
    }

    static string FinalPath() => File.Exists($"{SceneFolder}/FinalBoss.unity") ? $"{SceneFolder}/FinalBoss.unity" : "Assets/Scenes/FinalBoss.unity";
    public static string DataPath(int stage, int hunt) => $"{MonsterFolder}/Stage{stage}/Stage{stage}_{Kinds[hunt - 1]}.asset";
    public static string PrefabPath(int stage, int hunt) => $"{PrefabFolder}/Stage{stage}_{Kinds[hunt - 1]}.prefab";
    public static string BossPrefabPath(BossSpec boss) => $"{PrefabFolder}/{boss.prefab}.prefab";

    static void Move(string from, string to)
    {
        if (AssetDatabase.LoadMainAssetAtPath(from) == null || AssetDatabase.LoadMainAssetAtPath(to) != null) return;
        string error = AssetDatabase.MoveAsset(from, to);
        if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }

    static void RequireCleanScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("저장 안 된 씬이 있습니다: " + SceneManager.GetSceneAt(i).path);
    }

    static void Set(UnityEngine.Object target, string field, object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field) ?? throw new InvalidOperationException(target.GetType().Name + "." + field + " 없음");
        switch (value)
        {
            case int i: p.intValue = i; break;
            case float f: p.floatValue = f; break;
            case string s: p.stringValue = s; break;
            case UnityEngine.Object o: p.objectReferenceValue = o; break;
            default: throw new ArgumentException(field);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static T Get<T>(UnityEngine.Object target, string field)
    {
        var p = new SerializedObject(target).FindProperty(field);
        object value = typeof(T) == typeof(string) ? p.stringValue : (object)p.objectReferenceValue;
        return (T)value;
    }

    // ── 1. 몬스터 ───────────────────────────────────────────────

    [MenuItem("Tools/AINPC/Stages/1 Build Monsters")]
    public static string BuildMonsters()
    {
        // 이전 이름(Region…)을 GUID 를 유지한 채 옮긴다. 씬의 참조가 끊기지 않는다.
        for (int stage = 2; stage <= 4; stage++)
        {
            Move($"{MonsterFolder}/Region{stage}", $"{MonsterFolder}/Stage{stage}");
            foreach (string kind in Kinds) Move($"{MonsterFolder}/Stage{stage}/Region{stage}_{kind}.asset", $"{MonsterFolder}/Stage{stage}/Stage{stage}_{kind}.asset");
            Move($"{MonsterFolder}/Bosses/Region{stage}Boss.asset", $"{MonsterFolder}/Bosses/Stage{stage}Boss.asset");
        }
        for (int hunt = 1; hunt <= 4; hunt++) Move($"{PrefabFolder}/Region{hunt}.prefab", PrefabPath(1, hunt));

        for (int stage = 2; stage <= 4; stage++)
        {
            EnsureFolder($"{MonsterFolder}/Stage{stage}");
            for (int hunt = 1; hunt <= 4; hunt++)
            {
                string path = DataPath(stage, hunt);
                if (AssetDatabase.LoadAssetAtPath<MonsterData>(path) == null) AssetDatabase.CopyAsset($"Assets/Game/Data/HubStory/Enemy{hunt}.asset", path);
                var data = AssetDatabase.LoadAssetAtPath<MonsterData>(path);
                CopyStyle(hunt, data, 1f);
                Set(data, "monsterName", $"stage{stage}_{Kinds[hunt - 1].ToLowerInvariant()}");
                Set(data, "level", Hunting[stage - 2, hunt - 1, 0]);
                Set(data, "maxHP", (float)Hunting[stage - 2, hunt - 1, 1]);
                Set(data, "attackDamage", Hunting[stage - 2, hunt - 1, 2]);
                Set(data, "expReward", (float)Hunting[stage - 2, hunt - 1, 3]);
                Set(data, "dropTier", stage + 1);
                SaveVariant(PrefabPath(1, hunt), PrefabPath(stage, hunt), $"Stage{stage}_{Kinds[hunt - 1]}", Tints[stage - 1], Scales[stage - 1], null);
            }
        }

        foreach (var boss in Bosses)
        {
            string path = $"{MonsterFolder}/{boss.data}";
            if (AssetDatabase.LoadAssetAtPath<MonsterData>(path) == null) AssetDatabase.CopyAsset($"Assets/Game/Data/HubStory/Enemy{boss.kind}.asset", path);
            var data = AssetDatabase.LoadAssetAtPath<MonsterData>(path);
            CopyStyle(boss.kind, data, 1.6f);
            Set(data, "monsterName", boss.prefab.ToLowerInvariant());
            Set(data, "level", boss.level);
            Set(data, "maxHP", (float)boss.hp);
            Set(data, "attackDamage", boss.damage);
            Set(data, "expReward", (float)boss.exp);
            Set(data, "dropTier", boss.drop);
            Set(data, "aggroRange", 18f);
            var tint = Color.Lerp(Tints[Mathf.Min(boss.stage, 4) - 1], new Color(.45f, .3f, .35f), boss.stage == 5 ? .55f : .25f);
            SaveVariant(PrefabPath(1, boss.kind), BossPrefabPath(boss), boss.prefab, tint, 1.6f + .2f * (boss.stage - 2), boss);
        }
        AssetDatabase.SaveAssets();
        return "STAGE_MONSTERS_OK";
    }

    /// <summary>공격 방식에 맞는 사거리·이동·공격 간격·인식 거리를 1지역 같은 번호 몬스터에서 가져온다.</summary>
    static void CopyStyle(int hunt, MonsterData data, float rangeScale)
    {
        var source = AssetDatabase.LoadAssetAtPath<MonsterData>($"Assets/Game/Data/HubStory/Enemy{hunt}.asset");
        Set(data, "attackRange", source.AttackRange * rangeScale);
        Set(data, "moveSpeed", source.MoveSpeed);
        Set(data, "attackCooldown", source.AttackCooldown);
        Set(data, "aggroRange", source.AggroRange);
    }

    static void SaveVariant(string basePath, string path, string name, Color tint, float scale, BossSpec? boss)
    {
        var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(basePath));
        try
        {
            root.name = name;
            root.transform.localScale *= scale;   // 원본 프리팹 크기(3배)에 곱한다
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true)) renderer.color = tint;
            if (boss is BossSpec b)
            {
                var stageBoss = root.GetComponent<StageBoss>() ?? root.AddComponent<StageBoss>();
                Set(stageBoss, "clearFlag", b.flag);
                Set(stageBoss, "clearMessage", b.message);
                Set(root.GetComponent<RegionalMonster>(), "leashDistance", 16f);
                Set(root.GetComponent<MonsterBase>(), "monsterData", AssetDatabase.LoadAssetAtPath<MonsterData>($"{MonsterFolder}/{b.data}"));
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // ── 2. Stage2~4·최종보스 씬 ─────────────────────────────────

    [MenuItem("Tools/AINPC/Stages/2 Wire Stage Scenes")]
    public static string WireStageScenes()
    {
        RequireCleanScenes();
        var report = new List<string>();
        for (int stage = 2; stage <= 4; stage++)
        {
            var scene = EditorSceneManager.OpenScene(StagePath(stage), OpenSceneMode.Single);
            for (int hunt = 1; hunt <= 4; hunt++)
            {
                var zone = scene.GetRootGameObjects().First(g => g.name == $"Zone_Hunt{hunt}");
                var area = zone.GetComponentInChildren<MonsterSpawnArea>(true);
                Set(area, "monsterPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(stage, hunt)).GetComponent<MonsterBase>());
                Set(area, "monsterData", AssetDatabase.LoadAssetAtPath<MonsterData>(DataPath(stage, hunt)));
                HuntingBalanceSetup.ConfigureSpawner(hunt, area);
                area.name = "MonsterSpawnArea_" + Kinds[hunt - 1];
            }
            ReplaceBoss(scene, Bosses.First(b => b.stage == stage));
            EditorSceneManager.SaveScene(scene);
            report.Add("Stage" + stage);
        }
        var final = EditorSceneManager.OpenScene(FinalPath(), OpenSceneMode.Single);
        ReplaceBoss(final, Bosses.First(b => b.stage == 5));
        EditorSceneManager.SaveScene(final);
        return "STAGE_SCENES_OK " + string.Join(",", report) + ",Final";
    }

    static void ReplaceBoss(Scene scene, BossSpec spec)
    {
        var old = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<StageBoss>(true)).FirstOrDefault()
                  ?? throw new InvalidOperationException(scene.path + " 에 보스가 없습니다.");
        if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old.gameObject) == BossPrefabPath(spec)) return;
        var boss = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath(spec)), scene);
        boss.transform.SetParent(old.transform.parent, false);
        boss.transform.position = old.transform.position;
        boss.name = "Boss_" + spec.prefab;
        UnityEngine.Object.DestroyImmediate(old.gameObject);
    }

    // ── 3. 사냥터 합치기 ────────────────────────────────────────

    static readonly string[] HuntingRoots = { "Grid", "Props", "HubStory", "RegionalLandmarks" };

    [MenuItem("Tools/AINPC/Stages/3 Merge Hunting Grounds Into Stage1")]
    public static string MergeHuntingGrounds()
    {
        RequireCleanScenes();
        var main = EditorSceneManager.OpenScene(StagePath(1), OpenSceneMode.Single);
        if (main.GetRootGameObjects().Any(g => g.name == HubStoryIds.Zone(1) && g.GetComponentInChildren<HubAreaEntry>(true) != null))
            return "STAGE1_MERGE_SKIPPED already merged";
        // StageSceneBuilder 가 만든 빈 사냥터 자리(비활성)는 실제 사냥터로 바꾼다.
        foreach (var placeholder in main.GetRootGameObjects().Where(g => g.name.StartsWith("Zone_Hunt")).ToArray())
            UnityEngine.Object.DestroyImmediate(placeholder);

        for (int n = 1; n <= 4; n++)
        {
            var source = EditorSceneManager.OpenScene($"Assets/Scenes/HuntingGround{n}.unity", OpenSceneMode.Additive);
            var zone = new GameObject(HubStoryIds.Zone(n));
            SceneManager.MoveGameObjectToScene(zone, main);
            foreach (var root in source.GetRootGameObjects())
            {
                if (!HuntingRoots.Contains(root.name)) continue;   // 플레이어·카메라·퀘스트 시스템·UI 사본은 버린다
                SceneManager.MoveGameObjectToScene(root, main);
                root.transform.SetParent(zone.transform, true);
            }
            EditorSceneManager.CloseScene(source, true);
            // Y 는 그대로 둔다. 그리기 순서가 Y 좌표로 정해져서 Y 를 옮기면 지형 뒤에 가려진다.
            zone.transform.position = new Vector3(ZoneX(n), 0f, 0f);

            var hub = zone.transform.Find("HubStory") ?? throw new InvalidOperationException($"HuntingGround{n} 에 HubStory 가 없습니다.");
            foreach (var spawn in hub.GetComponentsInChildren<SpawnPoint>(true).Where(s => s.id == GameFlow.DefaultSpawn))
            {
                spawn.id = HubStoryIds.HuntSpawn(n);
                spawn.name = "입구 " + spawn.id;
            }
            var entry = hub.GetComponent<HubAreaEntry>();
            entry.entrySpawn = HubStoryIds.HuntSpawn(n);
            var bounds = MapBounds(zone);
            var area = hub.gameObject.AddComponent<BoxCollider2D>();
            area.isTrigger = true;
            area.size = bounds.size;
            area.offset = (Vector2)(bounds.center - hub.position);
            foreach (var back in hub.GetComponentsInChildren<ScenePortal>(true)) ToZonePortal(back, Get<string>(back, "targetSpawn"));
        }

        foreach (var portal in main.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ScenePortal>(true)).ToArray())
        {
            string target = Get<string>(portal, "targetScene");
            if (target.StartsWith("HuntingGround")) ToZonePortal(portal, HubStoryIds.HuntSpawn(int.Parse(target.Substring("HuntingGround".Length))));
        }
        EditorSceneManager.SaveScene(main);
        return "STAGE1_MERGE_OK";
    }

    /// <summary>구역 안 모든 타일맵을 덮는 월드 영역.</summary>
    static Bounds MapBounds(GameObject zone)
    {
        var maps = zone.GetComponentsInChildren<Tilemap>(true);
        var bounds = new Bounds(zone.transform.position, Vector3.zero);
        foreach (var map in maps)
        {
            map.CompressBounds();
            var local = map.localBounds;
            bounds.Encapsulate(map.transform.TransformPoint(local.min));
            bounds.Encapsulate(map.transform.TransformPoint(local.max));
        }
        return bounds;
    }

    /// <summary>씬 이동 문을 같은 씬 안 순간이동 문으로 바꾼다. 열림 조건과 안내 문구는 그대로 둔다.</summary>
    static void ToZonePortal(ScenePortal portal, string targetSpawn)
    {
        string flag = Get<string>(portal, "requiredFlag"), quest = Get<string>(portal, "requiredQuestId"), locked = Get<string>(portal, "lockedMessage");
        var go = portal.gameObject;
        UnityEngine.Object.DestroyImmediate(portal);
        var zonePortal = go.AddComponent<ZonePortal>();
        Set(zonePortal, "targetSpawn", targetSpawn);
        Set(zonePortal, "requiredFlag", flag ?? "");
        Set(zonePortal, "requiredQuestId", quest ?? "");
        Set(zonePortal, "lockedMessage", locked ?? "");
    }

    // ── 4. Stage1 → Stage2 ──────────────────────────────────────

    const string Stage1ClearFlag = GameFlow.Stage1ClearedFlag;

    [MenuItem("Tools/AINPC/Stages/4 Connect Stage1 To Stage2")]
    public static string ConnectStage1()
    {
        RequireCleanScenes();
        var main = EditorSceneManager.OpenScene(StagePath(1), OpenSceneMode.Single);
        var hub = main.GetRootGameObjects().First(g => g.name == "HubStory").transform;
        var existing = hub.GetComponentsInChildren<ScenePortal>(true).FirstOrDefault(p => Get<string>(p, "targetScene") == "Stage2" || Get<string>(p, "targetScene") == "Region2");
        // 비활성 보스 구역(StageSceneBuilder)에 있던 다음 지역 문을 마을로 옮긴다. 없으면 만든다.
        var portal = existing ?? main.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ScenePortal>(true))
            .FirstOrDefault(p => Get<string>(p, "requiredFlag") == Stage1ClearFlag);
        var village = hub.GetComponentsInChildren<SpawnPoint>(true).First(s => s.id == GameFlow.DefaultSpawn).transform.position;
        // 마을 길(Grass 타일)이 북쪽으로 뻗은 자리를 고른다. 편집 모드에서는 타일 충돌체가 물리 검사에 잡히지 않아 타일로 확인한다.
        var paths = main.GetRootGameObjects().First(g => g.name == "Grid").GetComponentsInChildren<Tilemap>().Where(m => m.name.Contains("Grass") || m.name.Contains("Path")).ToArray();
        bool OnPath(Vector3 p) => paths.Any(m => m.HasTile(m.WorldToCell(p)));
        Vector3 at = new[] { new Vector3(7f, 22f), new Vector3(7f, 20f), new Vector3(9f, 22f), new Vector3(5f, 22f), new Vector3(0f, 20f) }
            .Select(offset => village + offset)
            .FirstOrDefault(p => OnPath(p) && OnPath(p + Vector3.down * 4f));
        if (at == default) throw new InvalidOperationException("다음 지역 문을 놓을 빈자리가 없습니다.");
        if (portal == null)
        {
            var go = new GameObject("다음 지역으로", typeof(BoxCollider2D));
            go.GetComponent<BoxCollider2D>().isTrigger = true;
            go.GetComponent<BoxCollider2D>().size = new Vector2(2, 2);
            portal = go.AddComponent<ScenePortal>();
        }
        portal.transform.SetParent(hub, true);
        portal.gameObject.SetActive(true);
        portal.transform.position = at;
        portal.name = "다음 지역으로";
        if (portal.transform.Find("Label") == null) ItemDrop.WorldLabel(portal.transform, "Label", "다음 지역으로\n(재의 왕을 쓰러뜨린 뒤)", new Color(1f, .9f, .6f), 1.4f);
        Set(portal, "targetScene", File.Exists($"{SceneFolder}/Stage2.unity") ? "Stage2" : "Region2");
        Set(portal, "targetSpawn", GameFlow.DefaultSpawn);
        // 진행 표시(region1.boss_cleared)는 처치 순간에만 서서, 이미 재의 왕을 잡은 옛 세이브는 영영 못 연다. 의뢰 완료로 연다.
        Set(portal, "requiredFlag", "");
        Set(portal, "requiredQuestId", HubStoryIds.Boss);
        Set(portal, "lockedMessage", "재의 왕을 쓰러뜨려야 다음 지역으로 갈 수 있다.");

        // 2지역에서 돌아올 때 내릴 자리. 비활성 마을 구역의 것은 찾지 못한다.
        var back = hub.GetComponentsInChildren<SpawnPoint>(true).FirstOrDefault(s => s.id == "from_next");
        if (back == null) back = new GameObject("입구 from_next").AddComponent<SpawnPoint>();
        back.transform.SetParent(hub, true);
        back.id = "from_next";
        back.transform.position = at + new Vector3(0f, -4f, 0f);   // 문 바로 남쪽(마을 쪽). 문 위에 내리면 바로 되돌아간다
        EditorSceneManager.SaveScene(main);
        return "STAGE1_CONNECT_OK " + portal.transform.position;
    }

    // ── 5. 씬 이름·폴더 ─────────────────────────────────────────

    /// <summary>예전 씬 이름 → 새 이름. 세이브·포탈의 이름을 옮기는 데도 쓴다.</summary>
    public static readonly (string from, string to)[] Renames =
    {
        ("Title", "Title"), ("Cave", "Cave"), ("Main", "Stage1"), ("Region2", "Stage2"), ("Region3", "Stage3"),
        ("Region4", "Stage4"), ("AshKingArena", "AshKingArena"), ("FinalBoss", "FinalBoss"),
    };

    [MenuItem("Tools/AINPC/Stages/5 Rename And Move Build Scenes")]
    public static string RenameScenes()
    {
        RequireCleanScenes();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // 옮길 씬이 열려 있지 않게
        EnsureFolder(SceneFolder);
        foreach (var (from, to) in Renames) Move($"Assets/Scenes/{from}.unity", $"{SceneFolder}/{to}.unity");

        // 포탈의 대상 씬 이름. 씬 파일의 직렬화 문자열만 바꾼다.
        foreach (var (_, to) in Renames)
        {
            string path = $"{SceneFolder}/{to}.unity";
            string text = File.ReadAllText(path), replaced = text;
            foreach (var (oldName, newName) in Renames)
                if (oldName != newName) replaced = replaced.Replace($"targetScene: {oldName}\n", $"targetScene: {newName}\n").Replace($"targetScene: {oldName}\r\n", $"targetScene: {newName}\r\n");
            if (replaced != text) { File.WriteAllText(path, replaced); AssetDatabase.ImportAsset(path); }
        }

        for (int n = 1; n <= 4; n++) AssetDatabase.DeleteAsset($"Assets/Scenes/HuntingGround{n}.unity");
        EditorBuildSettings.scenes = Renames.Select(r => new EditorBuildSettingsScene($"{SceneFolder}/{r.to}.unity", true))
            .Concat(EditorBuildSettings.scenes.Where(s => !s.enabled && File.Exists(s.path)))
            .ToArray();
        AssetDatabase.SaveAssets();
        return "STAGE_RENAME_OK " + string.Join(",", EditorBuildSettings.scenes.Select(s => s.path));
    }
}

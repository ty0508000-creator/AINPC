using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 지역 구조 검증(편집 모드). 씬은 추가로 열었다 저장하지 않고 닫으므로 열려 있는 씬을 건드리지 않는다.
/// 빌드 씬 목록·포탈 연결, Stage1 의 사냥터 4곳, Stage2~4·최종보스의 몬스터와 보스, 16종 구분, 수치, 옛 세이브의 씬 이름 변환을 본다.
/// UnityMCP execute_code 로 <c>return StageVerification.Run();</c>.
/// </summary>
public static class StageVerification
{
    static int checks;

    [MenuItem("Tools/AINPC/Stages/Verify Stages (Edit Mode)")]
    static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        checks = 0;
        string result;
        try
        {
            VerifyBuildList();
            VerifyPortals();
            VerifyStage1();
            VerifyStages();
            VerifyMonsters();
            VerifySaveMigration();
            result = $"PASS {checks} checks";
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            result = $"FAIL after {checks} checks: {e.Message}";
        }
        string output = Path.GetFullPath("VerificationResults/Stages");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "result.txt"), result);
        return "STAGE_VERIFY_" + result;
    }

    static readonly string[] BuildNames = StageBuilder.Renames.Select(r => r.to).ToArray();

    static void VerifyBuildList()
    {
        var enabled = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        Check(enabled.SequenceEqual(BuildNames.Select(n => $"{StageBuilder.SceneFolder}/{n}.unity")), "빌드 씬 8개가 Scenes/Main 에 순서대로");
        Check(enabled.All(File.Exists), "빌드 씬 파일이 모두 있음");
        Check(!Directory.GetFiles("Assets/Scenes", "HuntingGround*.unity", SearchOption.AllDirectories).Any(), "사냥터 씬은 Stage1 로 합쳐져 없음");
        foreach (string old in new[] { "Main", "Region2", "Region3", "Region4" })
            Check(!File.Exists($"Assets/Scenes/{old}.unity"), "옛 씬 경로가 남지 않음: " + old);
    }

    /// <summary>씬을 추가로 열어 검사하고 저장하지 않고 닫는다.</summary>
    static void Inspect(string path, Action<Scene> inspect)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try { inspect(scene); }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));
    static string Field(UnityEngine.Object target, string name) => new SerializedObject(target).FindProperty(name).stringValue;
    static UnityEngine.Object Reference(UnityEngine.Object target, string name) => new SerializedObject(target).FindProperty(name).objectReferenceValue;

    static void VerifyPortals()
    {
        var spawns = new Dictionary<string, HashSet<string>>();
        var portals = new List<(string from, string scene, string spawn)>();
        foreach (string name in BuildNames)
            Inspect($"{StageBuilder.SceneFolder}/{name}.unity", scene =>
            {
                spawns[name] = new HashSet<string>(All<SpawnPoint>(scene).Where(s => s.gameObject.activeInHierarchy).Select(s => s.id));
                foreach (var portal in All<ScenePortal>(scene).Where(p => p.gameObject.activeInHierarchy))
                    portals.Add((name, Field(portal, "targetScene"), Field(portal, "targetSpawn")));
                foreach (var portal in All<ZonePortal>(scene).Where(p => p.gameObject.activeInHierarchy))
                    Check(spawns[name].Contains(Field(portal, "targetSpawn")), $"{name} 순간이동 문 대상 입구가 있음: {Field(portal, "targetSpawn")}");
            });
        foreach (var (from, target, spawn) in portals)
        {
            Check(BuildNames.Contains(target), $"{from} 의 문이 빌드 씬으로 감: {target}");
            Check(spawns[target].Contains(string.IsNullOrEmpty(spawn) ? GameFlow.DefaultSpawn : spawn), $"{from} → {target} 도착 입구 {spawn} 가 있음");
        }
        // 동굴 → 1 → 2 → 3 → 4 → 최종, 그리고 되돌아가는 길
        foreach (var (a, b) in new[] { ("Cave", "Stage1"), ("Stage1", "Stage2"), ("Stage2", "Stage3"), ("Stage3", "Stage4"), ("Stage4", "FinalBoss"),
                                       ("Stage2", "Stage1"), ("Stage3", "Stage2"), ("Stage4", "Stage3"), ("FinalBoss", "Stage4"), ("Stage1", "AshKingArena"), ("AshKingArena", "Stage1") })
            Check(portals.Any(p => p.from == a && p.scene == b), $"{a} → {b} 문");
    }

    static void VerifyStage1()
    {
        Inspect(StageBuilder.StagePath(1), scene =>
        {
            var roots = scene.GetRootGameObjects();
            Check(All<PlayerStats>(scene).Count() == 1 && All<QuestManager>(scene).Count() == 1, "Stage1 에 플레이어·퀘스트 시스템이 하나씩");
            var village = MapBounds(roots.First(g => g.name == "Grid"));
            var zones = new List<Bounds>();
            for (int n = 1; n <= 4; n++)
            {
                var zone = roots.FirstOrDefault(g => g.name == HubStoryIds.Zone(n));
                Check(zone != null && zone.activeSelf, $"Zone_Hunt{n} 이 켜져 있음");
                Check(Mathf.Approximately(zone.transform.position.y, 0f), $"Zone_Hunt{n} 은 Y 를 옮기지 않음(그리기 순서 유지)");
                var entry = zone.GetComponentInChildren<HubAreaEntry>(true);
                var box = entry != null ? entry.GetComponent<BoxCollider2D>() : null;
                Check(entry != null && entry.areaId == HubStoryIds.Area(n) && entry.entrySpawn == HubStoryIds.HuntSpawn(n) && box != null && box.isTrigger,
                    $"Zone_Hunt{n} 에 들어서면 도착 보고·체크포인트");
                var area = zone.GetComponentInChildren<MonsterSpawnArea>(true);
                Check(area != null && Reference(area, "monsterPrefab") is RegionalMonster && ((MonsterData)Reference(area, "monsterData")).MonsterName == HubStoryIds.Monster(n),
                    $"Zone_Hunt{n} 에 1지역 몬스터 {n}");
                Check(zone.GetComponentsInChildren<ZonePortal>(true).Any(p => Field(p, "targetSpawn") == HubStoryIds.ReturnSpawn(n)), $"Zone_Hunt{n} 에서 마을로 돌아가는 문");
                var bounds = MapBounds(zone);
                Check(!bounds.Intersects(village) && zones.All(other => !other.Intersects(bounds)), $"Zone_Hunt{n} 지형이 마을·다른 사냥터와 겹치지 않음");
                zones.Add(bounds);
            }
            var toHunts = All<ZonePortal>(scene).Where(p => Field(p, "targetSpawn").StartsWith("hunt_")).ToArray();
            Check(toHunts.Length == 4 && toHunts.All(p => Field(p, "requiredQuestId") == HubStoryIds.Testimony), "마을의 사냥터 문 4개는 증언 뒤에 열림");
            var next = All<ScenePortal>(scene).FirstOrDefault(p => Field(p, "targetScene") == "Stage2");
            Check(next != null && next.gameObject.activeInHierarchy && Field(next, "requiredQuestId") == HubStoryIds.Boss, "재의 왕 의뢰를 마치면 열리는 2지역 문(이미 잡은 옛 세이브도 열림)");
            Check(All<SpawnPoint>(scene).Any(s => s.id == "from_next" && s.gameObject.activeInHierarchy), "2지역에서 돌아오는 입구");
        });
    }

    static Bounds MapBounds(GameObject root)
    {
        var maps = root.GetComponentsInChildren<UnityEngine.Tilemaps.Tilemap>(true);
        var bounds = new Bounds(maps[0].transform.TransformPoint(maps[0].localBounds.center), Vector3.zero);
        foreach (var map in maps)
        {
            bounds.Encapsulate(map.transform.TransformPoint(map.localBounds.min));
            bounds.Encapsulate(map.transform.TransformPoint(map.localBounds.max));
        }
        bounds.Expand(new Vector3(0f, 0f, 10f));   // 타일맵 Z 두께 차이로 겹침 판정이 빠지지 않게
        return bounds;
    }

    static void VerifyStages()
    {
        for (int stage = 2; stage <= 4; stage++)
        {
            int s = stage;
            Inspect(StageBuilder.StagePath(stage), scene =>
            {
                for (int hunt = 1; hunt <= 4; hunt++)
                {
                    var zone = scene.GetRootGameObjects().First(g => g.name == HubStoryIds.Zone(hunt));
                    var area = zone.GetComponentInChildren<MonsterSpawnArea>(true);
                    Check(AssetDatabase.GetAssetPath(Reference(area, "monsterPrefab")) == StageBuilder.PrefabPath(s, hunt) &&
                          AssetDatabase.GetAssetPath(Reference(area, "monsterData")) == StageBuilder.DataPath(s, hunt), $"Stage{s} 사냥터 {hunt}: {StageBuilder.Kinds[hunt - 1]}");
                }
                CheckBoss(scene, StageBuilder.Bosses.First(b => b.stage == s));
            });
        }
        Inspect($"{StageBuilder.SceneFolder}/FinalBoss.unity", scene => CheckBoss(scene, StageBuilder.Bosses.First(b => b.stage == 5)));
    }

    static void CheckBoss(Scene scene, StageBuilder.BossSpec spec)
    {
        var bosses = All<StageBoss>(scene).ToArray();
        Check(bosses.Length == 1 && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(bosses[0].gameObject) == StageBuilder.BossPrefabPath(spec) &&
              bosses[0].GetComponent<RegionalMonster>() != null && Field(bosses[0], "clearFlag") == spec.flag, $"{scene.name} 보스는 예고 공격 보스 {spec.prefab}");
    }

    static void VerifyMonsters()
    {
        var names = new HashSet<string>();
        var looks = new HashSet<string>();
        for (int hunt = 1; hunt <= 4; hunt++) names.Add(AssetDatabase.LoadAssetAtPath<MonsterData>($"Assets/Game/Data/HubStory/Enemy{hunt}.asset").MonsterName);
        for (int stage = 1; stage <= 4; stage++)
            for (int hunt = 1; hunt <= 4; hunt++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StageBuilder.PrefabPath(stage, hunt));
                var renderer = prefab.GetComponentInChildren<SpriteRenderer>();
                looks.Add(StageBuilder.Kinds[hunt - 1] + renderer.color + prefab.transform.localScale.x.ToString("F2"));
                Check((int)prefab.GetComponent<RegionalMonster>().AttackStyle == hunt - 1, $"Stage{stage} 사냥터 {hunt} 공격 방식이 1지역과 같음");
                if (stage == 1) continue;
                var data = AssetDatabase.LoadAssetAtPath<MonsterData>(StageBuilder.DataPath(stage, hunt));
                names.Add(data.MonsterName);
                int r = stage - 2, k = hunt - 1;
                Check(data.Level == StageBuilder.Hunting[r, k, 0] && data.MaxHP == StageBuilder.Hunting[r, k, 1] && data.AttackDamage == StageBuilder.Hunting[r, k, 2] &&
                      data.ExpReward == StageBuilder.Hunting[r, k, 3] && data.DropTier == stage + 1, $"Stage{stage} 사냥터 {hunt} 수치");
            }
        Check(names.Count == 16, $"몬스터 이름 16종 ({names.Count})");
        Check(looks.Count == 16, $"몬스터 생김새(몸체·색·크기) 16종 ({looks.Count})");
        foreach (var boss in StageBuilder.Bosses)
        {
            var data = AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/Data/Monsters/" + boss.data);
            Check(data.Level == boss.level && data.MaxHP == boss.hp && data.AttackDamage == boss.damage && data.ExpReward == boss.exp && data.DropTier == boss.drop, boss.prefab + " 수치");
        }
    }

    static void VerifySaveMigration()
    {
        (string saved, string scene, string spawn)[] cases =
        {
            ("Assets/Scenes/Main.unity", "Stage1", null), ("Main", "Stage1", null), ("Region3", "Stage3", null), ("Assets/Scenes/Region4.unity", "Stage4", null),
            ("HuntingGround2", "Stage1", "hunt_2"), ("Assets/Scenes/HuntingGround4.unity", "Stage1", "hunt_4"), ("Cave", "Cave", null),
            ("Assets/Scenes/Main/Stage2.unity", "Stage2", null), ("AshKingArena", "AshKingArena", null), ("", "Stage1", null),
        };
        foreach (var (saved, scene, spawn) in cases)
            Check(GameFlow.ResolveSavedScene(saved, out string resolvedSpawn) == scene && resolvedSpawn == spawn, $"옛 세이브 씬 '{saved}' → {scene} {spawn}");
    }

    static void Check(bool pass, string message)
    {
        if (!pass) throw new Exception(message);
        checks++;
    }
}

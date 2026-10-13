using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>생성 마을의 지형·소품을 복사해 기존 Stage1 동선에 연결한다. 원본 씬은 임시 결과의 필요한 블록만 반영한다.</summary>
public static class MapGenVillageSetup
{
    public const string StagePath = "Assets/Scenes/Main/Stage1.unity";
    public const string SourcePath = "Assets/Scenes/MapGen_Village.unity";
    public const string WorkingPath = "Assets/CodexVillageWorking/Stage1.unity";
    public const string GeometryName = "Village_MapGen";
    static readonly Vector3Int[] Steps = { Vector3Int.right, Vector3Int.up, Vector3Int.left, Vector3Int.down };
    static Tilemap ground, blockers, path;
    static Dictionary<Vector3Int, Vector3Int> parents;
    static Vector3 center;
    static readonly List<Vector3> occupied = new List<Vector3>();

    public static string Prepare()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Play를 종료해야 마을을 연결할 수 있다.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("저장하지 않은 씬 변경이 있다.");
        if (File.ReadLines(StagePath).Any(line => line.Trim() == "m_Name: " + GeometryName)) throw new InvalidOperationException("이미 생성 마을이 연결되어 있다.");
        Directory.CreateDirectory("VerificationResults/VillageMap");
        Directory.CreateDirectory(Path.GetDirectoryName(WorkingPath));
        File.Copy(StagePath, "VerificationResults/VillageMap/Stage1.before.unity", true);
        File.Copy(StagePath, WorkingPath, true);
        AssetDatabase.ImportAsset(WorkingPath, ImportAssetOptions.ForceSynchronousImport);
        var source = SceneManager.GetSceneByPath(SourcePath);
        bool openedSource = !source.IsValid() || !source.isLoaded;
        if (openedSource) source = EditorSceneManager.OpenScene(SourcePath, OpenSceneMode.Additive);
        var sourceGrid = Root(source, "Grid");
        var sourceProps = Root(source, "Props");
        Vector3 sourceSpawn = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PlayerStats>(true)).Single().transform.position;
        var working = EditorSceneManager.OpenScene(WorkingPath, OpenSceneMode.Additive);
        if (working.GetRootGameObjects().Any(g => g.name == GeometryName)) throw new InvalidOperationException("이미 생성 마을이 연결되어 있다.");
        center = working.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SpawnPoint>(true)).Single(s => s.gameObject.activeInHierarchy && s.id == GameFlow.DefaultSpawn).transform.position;
        SceneManager.SetActiveScene(working);
        var owner = new GameObject(GeometryName);
        SceneManager.MoveGameObjectToScene(owner, working);
        var grid = UnityEngine.Object.Instantiate(sourceGrid, owner.transform, true); grid.name = "VillageTerrain";
        var props = UnityEngine.Object.Instantiate(sourceProps, owner.transform, true); props.name = "VillageProps";
        owner.transform.position = center - sourceSpawn;
        int sortOffset = -Mathf.RoundToInt(owner.transform.position.y * YSortRenderer.DefaultPrecision);
        foreach (var renderer in props.GetComponentsInChildren<SpriteRenderer>()) renderer.sortingOrder += sortOffset;
        ground = grid.GetComponentsInChildren<Tilemap>().Single(m => m.name == "Ground");
        blockers = grid.GetComponentsInChildren<Tilemap>().Single(m => m.name == "Blockers");
        path = grid.GetComponentsInChildren<Tilemap>().Single(m => m.name == "Path");
        Root(working, "Grid").SetActive(false);
        var oldMonsters = working.GetRootGameObjects().FirstOrDefault(g => g.name == "MonsterSpawnArea");
        if (oldMonsters != null) oldMonsters.SetActive(false);
        foreach (var root in new[] { Root(working, "HubStory"), Root(working, "AshKingGate") })
            foreach (var map in root.GetComponentsInChildren<Tilemap>(true)) map.gameObject.SetActive(false);
        BuildReachability(); occupied.Clear();
        var hub = Root(working, "HubStory");
        Place(hub.GetComponentsInChildren<HubStoryActor>(true).Single(a => a.actorId == HubStoryIds.Elder).transform, center + new Vector3(-4, 0));
        Place(hub.GetComponentsInChildren<HubStoryActor>(true).Single(a => a.actorId == HubStoryIds.Hayeon).transform, center + new Vector3(4, 0));
        foreach (var root in working.GetRootGameObjects().Where(g => g.name == "Mr.Smith" || g.name == "Kaguya"))
            Place(root.transform, center + new Vector3(0, root.name == "Mr.Smith" ? 6 : -6));
        var targetOffsets = new[] { new Vector3(-40.5f, 35.5f), new Vector3(41.5f, 35.5f), new Vector3(-40.5f, -40.5f), new Vector3(41.5f, -40.5f) };
        for (int n = 1; n <= 4; n++)
        {
            var portal = hub.GetComponentsInChildren<ZonePortal>(true).Single(p => StringField(p, "targetSpawn") == HubStoryIds.HuntSpawn(n));
            Place(portal.transform, center + targetOffsets[n - 1]);
            var arrival = hub.GetComponentsInChildren<SpawnPoint>(true).Single(s => s.id == HubStoryIds.ReturnSpawn(n));
            arrival.transform.position = ClearPoint(portal.transform.position + (center - portal.transform.position).normalized * 6);
            Road(portal.transform.position); Road(arrival.transform.position);
        }
        var bossRoot = Root(working, "AshKingGate");
        var bossPortal = bossRoot.GetComponentsInChildren<ScenePortal>(true).Single();
        Place(bossPortal.transform, center + new Vector3(0, -44.5f));
        bossRoot.GetComponentsInChildren<SpawnPoint>(true).Single().transform.position = ClearPoint(bossPortal.transform.position + Vector3.up * 6);
        Road(bossPortal.transform.position);
        var next = hub.GetComponentsInChildren<ScenePortal>(true).Single(p => StringField(p, "targetScene") == "Stage2");
        Place(next.transform, center + new Vector3(0, 42)); Road(next.transform.position);
        hub.GetComponentsInChildren<SpawnPoint>(true).Single(s => s.id == "from_next").transform.position = ClearPoint(next.transform.position + Vector3.down * 6);
        var cave = working.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SpawnPoint>(true)).Single(s => s.id == "cave_exit");
        cave.transform.position = center;
        var camera = working.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
        camera.transform.position = new Vector3(center.x, center.y, -10);
        var safety = owner.AddComponent<VillageSpawnSafety>();
        var serialized = new SerializedObject(safety);
        serialized.FindProperty("blockers").objectReferenceValue = blockers;
        serialized.FindProperty("fallback").objectReferenceValue = working.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SpawnPoint>(true)).Single(s => s.gameObject.activeInHierarchy && s.id == GameFlow.DefaultSpawn).transform;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        int houseCount = props.transform.Find("Buildings").childCount;
        if (!EditorSceneManager.SaveScene(working)) throw new IOException("임시 마을 씬 저장 실패");
        EditorSceneManager.CloseScene(working, true);
        if (openedSource) EditorSceneManager.CloseScene(source, true);
        return "MapGen_Village의 건물 " + houseCount + "채와 기존 지형·소품을 복사했다.";
    }
    static GameObject Root(Scene scene, string name) => scene.GetRootGameObjects().Single(g => g.name == name);
    static string StringField(UnityEngine.Object component, string field) => new SerializedObject(component).FindProperty(field).stringValue;
    static bool Walkable(Vector3Int p) => ground.HasTile(p) && !blockers.HasTile(p);
    static bool Clear(Vector3Int p) { for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) if (!Walkable(p + new Vector3Int(x, y))) return false; return true; }
    static void BuildReachability()
    {
        var start = ground.WorldToCell(center); if (!Clear(start)) throw new InvalidOperationException("마을 출발점이 막혀 있다.");
        parents = new Dictionary<Vector3Int, Vector3Int> { [start] = start };
        var queue = new Queue<Vector3Int>(); queue.Enqueue(start);
        while (queue.Count > 0) { var at = queue.Dequeue(); foreach (var step in Steps) { var next = at + step; if (!parents.ContainsKey(next) && Walkable(next)) { parents[next] = at; queue.Enqueue(next); } } }
    }
    static Vector3 ClearPoint(Vector3 target)
    {
        Vector3Int best = default; float distance = float.PositiveInfinity;
        foreach (var cell in parents.Keys)
        {
            var position = ground.GetCellCenterWorld(cell);
            float d = (position - target).sqrMagnitude;
            if (d >= distance || !Clear(cell) || occupied.Any(p => Vector3.Distance(p, position) < 2.5f)) continue;
            best = cell; distance = d;
        }
        if (float.IsInfinity(distance)) throw new InvalidOperationException("도달 가능한 빈 배치 지점이 없다.");
        return ground.GetCellCenterWorld(best);
    }
    static void Place(Transform actor, Vector3 target) { actor.position = ClearPoint(target); occupied.Add(actor.position); Road(actor.position); }
    static void Road(Vector3 target)
    {
        var tile = path.GetTilesBlock(path.cellBounds).First(t => t != null); var at = ground.WorldToCell(target);
        while (true) { for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) { var cell = at + new Vector3Int(x, y); if (Walkable(cell)) path.SetTile(cell, tile); } var parent = parents[at]; if (parent == at) break; at = parent; }
    }
}

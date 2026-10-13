using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>원본 마을 보존, 도보 연결, 실제 사냥터 왕복과 이전 저장 위치 복구를 별도 저장으로 검사한다.</summary>
[InitializeOnLoad]
public static class MapGenVillageVerification
{
    const string Pending = "VillageMap.Pending", Slot = "VillageMap.Slot";
    static int checks, stage, area; static double due, deadline;
    static MapGenVillageVerification()
    {
        EditorApplication.playModeStateChanged += Changed;
        if (SessionState.GetBool(Pending, false)) SaveSystem.VerificationDirectory = SessionState.GetString(Slot, "");
    }
    static void Check(bool pass, string text) { if (!pass) throw new Exception(text); checks++; Debug.Log("VILLAGE PASS: " + text); }
    static T[] In<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
    public static string RunEdit()
    {
        checks = 0; Directory.CreateDirectory("VerificationResults/VillageMap");
        var scene = SceneManager.GetSceneByPath(MapGenVillageSetup.StagePath);
        if (!scene.IsValid()) throw new InvalidOperationException("Stage1을 먼저 열어야 한다.");
        var source = EditorSceneManager.OpenScene(MapGenVillageSetup.SourcePath, OpenSceneMode.Additive);
        try
        {
            var owner = scene.GetRootGameObjects().Single(g => g.name == MapGenVillageSetup.GeometryName);
            var maps = owner.GetComponentsInChildren<Tilemap>();
            var ground = maps.Single(m => m.name == "Ground"); var block = maps.Single(m => m.name == "Blockers");
            foreach (string name in new[] { "Ground", "Water", "Blockers" })
            {
                var original = In<Tilemap>(source).Single(m => m.name == name); var copy = maps.Single(m => m.name == name);
                Check(original.cellBounds == copy.cellBounds && original.GetTilesBlock(original.cellBounds).SequenceEqual(copy.GetTilesBlock(copy.cellBounds)), "original tiles preserved: " + name);
            }
            foreach (string name in new[] { "Buildings", "Trees", "Decor", "WaterPlants" })
            {
                var old = source.GetRootGameObjects().Single(g => g.name == "Props").transform.Find(name);
                var copy = owner.transform.Find("VillageProps").Find(name);
                Check(old.childCount == copy.childCount, "original prop count preserved: " + name);
                Check(Enumerable.Range(0, old.childCount).All(i => old.GetChild(i).name == copy.GetChild(i).name && old.GetChild(i).localPosition == copy.GetChild(i).localPosition), "original prop placement preserved: " + name);
            }
            Check(In<PlayerStats>(scene).Length == 1 && In<Camera>(scene).Count(c => c.CompareTag("MainCamera")) == 1, "no copied player or camera");
            Check(!scene.GetRootGameObjects().Single(g => g.name == "Grid").activeSelf, "old village terrain inactive");
            Check(!scene.GetRootGameObjects().Single(g => g.name == "MonsterSpawnArea").activeSelf, "village sample monsters inactive");
            var start = In<SpawnPoint>(scene).Single(s => s.gameObject.activeInHierarchy && s.id == GameFlow.DefaultSpawn);
            var reachable = new HashSet<Vector3Int> { ground.WorldToCell(start.transform.position) }; var queue = new Queue<Vector3Int>(reachable);
            while (queue.Count > 0) { var at = queue.Dequeue(); foreach (var dir in new[] { Vector3Int.left, Vector3Int.right, Vector3Int.up, Vector3Int.down }) { var p = at + dir; if (ground.HasTile(p) && !block.HasTile(p) && reachable.Add(p)) queue.Enqueue(p); } }
            var hub = scene.GetRootGameObjects().Single(g => g.name == "HubStory");
            var destinations = hub.GetComponentsInChildren<HubStoryActor>().Select(a => a.transform).Concat(hub.GetComponentsInChildren<ZonePortal>().Select(p => p.transform)).Concat(hub.GetComponentsInChildren<ScenePortal>().Select(p => p.transform)).Concat(scene.GetRootGameObjects().Single(g => g.name == "AshKingGate").GetComponentsInChildren<ScenePortal>().Select(p => p.transform));
            foreach (var destination in destinations) Check(reachable.Contains(ground.WorldToCell(destination.position)), "walkable route: " + destination.name);
            for (int n = 1; n <= 4; n++)
            {
                Check(scene.GetRootGameObjects().Single(g => g.name == HubStoryIds.Zone(n)).activeSelf, "hunting zone still active: " + n);
                var arrival = In<SpawnPoint>(scene).Single(s => s.gameObject.activeInHierarchy && s.id == HubStoryIds.ReturnSpawn(n));
                var portal = hub.GetComponentsInChildren<ZonePortal>().Single(p => (string)typeof(ZonePortal).GetField("targetSpawn", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(p) == HubStoryIds.HuntSpawn(n));
                Check(reachable.Contains(ground.WorldToCell(arrival.transform.position)) && Vector2.Distance(arrival.transform.position, portal.transform.position) >= 4, "safe separated return point: " + n);
            }
            Check(reachable.Contains(ground.WorldToCell(In<SpawnPoint>(scene).Single(s => s.id == "cave_exit").transform.position)), "cave arrival is on village path");
            File.WriteAllText("VerificationResults/VillageMap/edit-result.txt", "PASS " + checks + " checks"); return "PASS " + checks + " village checks";
        }
        catch (Exception e) { File.WriteAllText("VerificationResults/VillageMap/edit-result.txt", "FAIL " + checks + " " + e.Message); throw; }
        finally { EditorSceneManager.CloseScene(source, true); }
    }
    public static void RunPlay()
    {
        if (EditorApplication.isPlaying || SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("저장하고 Play를 종료한 뒤 실행해야 한다.");
        SessionState.SetString(Slot, Path.GetFullPath("VerificationResults/VillageMap/Save-" + Guid.NewGuid().ToString("N")));
        SessionState.SetBool(Pending, true); SaveSystem.VerificationDirectory = SessionState.GetString(Slot, "");
        EditorSceneManager.OpenScene(MapGenVillageSetup.StagePath); EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) { stage = checks = 0; area = 1; due = EditorApplication.timeSinceStartup + 3; deadline = due + 90; EditorApplication.update += Tick; }
        if (state == PlayModeStateChange.EnteredEditMode) { SessionState.SetBool(Pending, false); SaveSystem.VerificationDirectory = null; }
    }
    static void Move(Vector3 point) { var p = UnityEngine.Object.FindFirstObjectByType<PlayerStats>(); p.transform.position = point; p.GetComponent<Rigidbody2D>().position = point; Physics2D.SyncTransforms(); }
    static void Enter(string id)
    {
        var portal = UnityEngine.Object.FindObjectsByType<ZonePortal>(FindObjectsSortMode.None).Single(p => (string)typeof(ZonePortal).GetField("targetSpawn", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(p) == id);
        Move(portal.transform.position); due = EditorApplication.timeSinceStartup + .8;
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup > deadline) { Finish(false, "timeout"); return; }
        if (EditorApplication.timeSinceStartup < due || GameFlow.Instance.IsLoading || QuestManager.Instance == null || !QuestManager.Instance.IsSaveReady) return;
        try
        {
            if (stage == 0)
            {
                var player = UnityEngine.Object.FindFirstObjectByType<PlayerStats>(); player.GetComponent<Player_Controller>().enabled = false;
                Check(player.IsAlive, "new village player alive");
                var safety = UnityEngine.Object.FindFirstObjectByType<VillageSpawnSafety>();
                var blocks = safety.GetComponentsInChildren<Tilemap>().Single(m => m.name == "Blockers");
                var wall = blocks.cellBounds.min; foreach (var cell in blocks.cellBounds.allPositionsWithin) if (blocks.HasTile(cell)) { wall = cell; break; } Move(blocks.GetCellCenterWorld(wall)); safety.RepairIfBlocked();
                Check(Vector2.Distance(player.transform.position, SpawnPoint.Find(GameFlow.DefaultSpawn).transform.position) < .1f, "legacy position on water or building recovers safely");
                var elder = UnityEngine.Object.FindObjectsByType<HubStoryActor>(FindObjectsSortMode.None).Single(a => a.actorId == HubStoryIds.Elder);
                Move(elder.transform.position); Check(elder.Interact(), "elder dialogue works in new village"); UnityEngine.Object.FindFirstObjectByType<StoryConversationUI>().Confirm();
                QuestManager.Instance.ReportReach("hub_village"); QuestManager.Instance.ReportTalk(HubStoryIds.Elder);
                Check(QuestManager.Instance.IsCompleted(HubStoryIds.Testimony), "existing testimony gate unlocks");
                stage = 1; Enter(HubStoryIds.HuntSpawn(area));
            }
            else if (stage == 1)
            {
                Check(SceneManager.GetActiveScene().name == "Stage1", "hunting remains in Stage1: " + area);
                Check(Vector2.Distance(UnityEngine.Object.FindFirstObjectByType<PlayerStats>().transform.position, SpawnPoint.Find(HubStoryIds.HuntSpawn(area)).transform.position) < 2, "physical hunting portal works: " + area);
                stage = 2; Enter(HubStoryIds.ReturnSpawn(area));
            }
            else
            {
                Check(Vector2.Distance(UnityEngine.Object.FindFirstObjectByType<PlayerStats>().transform.position, SpawnPoint.Find(HubStoryIds.ReturnSpawn(area)).transform.position) < 2, "physical village return works: " + area);
                if (area == 4) { var p = UnityEngine.Object.FindFirstObjectByType<PlayerStats>(); p.TakeDamage(100000); Check(p.Respawn(), "death retry still works"); Check(SaveSystem.LoadPlayer().quests.entries.Any(e => e.questId == HubStoryIds.Testimony && e.state == QuestState.Completed), "quest progress survives map travel and retry"); Finish(true, ""); }
                else { area++; stage = 1; Enter(HubStoryIds.HuntSpawn(area)); }
            }
        }
        catch (Exception e) { Debug.LogException(e); Finish(false, e.Message); }
    }
    static void Finish(bool pass, string message) { EditorApplication.update -= Tick; Directory.CreateDirectory("VerificationResults/VillageMap"); File.WriteAllText("VerificationResults/VillageMap/play-result.txt", (pass ? "PASS " : "FAIL ") + checks + " checks " + message); EditorApplication.ExitPlaymode(); }
}

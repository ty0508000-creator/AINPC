using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

[InitializeOnLoad]
public static class VillageResidentSetup
{
    const string Pending = "VillageResidents.Play", Slot = "VillageResidents.Slot";
    static double due;
    static VillageResidentSetup()
    {
        if (SessionState.GetBool(Pending, false)) SaveSystem.VerificationDirectory = SessionState.GetString(Slot, "");
        EditorApplication.playModeStateChanged += state => {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { due = EditorApplication.timeSinceStartup + 3; EditorApplication.update += VerifyPlay; }
            if (state == PlayModeStateChange.EnteredEditMode) { SessionState.SetBool(Pending, false); SaveSystem.VerificationDirectory = null; EditorApplication.update -= VerifyPlay; }
        };
    }
    public static void RunPlay()
    {
        SessionState.SetString(Slot, Path.GetFullPath("VerificationResults/VillageResidents/Play-" + Guid.NewGuid().ToString("N")));
        SessionState.SetBool(Pending, true); SaveSystem.VerificationDirectory = SessionState.GetString(Slot, ""); EditorApplication.isPlaying = true;
    }
    static void VerifyPlay()
    {
        if (EditorApplication.timeSinceStartup < due) return;
        EditorApplication.update -= VerifyPlay;
        try {
            if (SaveSystem.VerificationDirectory != SessionState.GetString(Slot, "")) throw new Exception("Private slot lost.");
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
            var resident = UnityEngine.Object.FindObjectsByType<VillageResident>(FindObjectsSortMode.None).OrderBy(n => n.residentName).First();
            player.transform.position = resident.transform.position;
            var rb = player.GetComponent<Rigidbody2D>(); if (rb != null) rb.position = resident.transform.position;
            if (!resident.Interact() || !StoryConversationUI.IsOpen) throw new Exception("Resident dialogue failed.");
            UnityEngine.Object.FindFirstObjectByType<StoryConversationUI>().Close();
            File.WriteAllText("VerificationResults/VillageResidents/play-result.txt", "PASS: private slot preserved across domain reload; actual resident dialogue opens and closes.");
        } catch (Exception ex) { File.WriteAllText("VerificationResults/VillageResidents/play-result.txt", "FAIL: " + ex); Debug.LogException(ex); }
        finally { EditorApplication.isPlaying = false; }
    }
    public const string Working = "Assets/CodexResidentsWorking/Stage1.unity";
    const string Texture = "Assets/Art/Characters/LifeInPixelsCitizens/Citizen.png";
    public static string Prepare()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
        if (File.ReadLines(MapGenVillageSetup.StagePath).Any(l => l.Trim() == "m_Name: VillageResidents")) throw new InvalidOperationException("Residents already exist.");
        var importer = (TextureImporter)AssetImporter.GetAtPath(Texture);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 16; importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        var slices = new SpriteMetaData[8];
        for (int i = 0; i < 8; i++) slices[i] = new SpriteMetaData { name = "Citizen_" + i, rect = new Rect(i % 4 * 32, i < 4 ? 32 : 0, 16, 16), alignment = 9, pivot = new Vector2(.5f, .15f) };
#pragma warning disable CS0618
        importer.spritesheet = slices;
#pragma warning restore CS0618
        importer.SaveAndReimport();
        var sprites = AssetDatabase.LoadAllAssetsAtPath(Texture).OfType<Sprite>().OrderBy(s => s.name).ToArray();
        if (sprites.Length != 8) throw new Exception("Expected eight citizen sprites.");
        Directory.CreateDirectory("VerificationResults/VillageResidents");
        File.Copy(MapGenVillageSetup.StagePath, "VerificationResults/VillageResidents/Stage1.before.unity", true);
        Directory.CreateDirectory(Path.GetDirectoryName(Working)); File.Copy(MapGenVillageSetup.StagePath, Working, true);
        AssetDatabase.ImportAsset(Working, ImportAssetOptions.ForceSynchronousImport);
        var scene = EditorSceneManager.OpenScene(Working, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var roots = scene.GetRootGameObjects();
        var terrain = roots.Single(g => g.name == MapGenVillageSetup.GeometryName);
        var maps = terrain.GetComponentsInChildren<Tilemap>();
        var ground = maps.Single(m => m.name == "Ground"); var blockers = maps.Single(m => m.name == "Blockers"); var path = maps.Single(m => m.name == "Path");
        var start = roots.SelectMany(g => g.GetComponentsInChildren<SpawnPoint>(true)).First(s => s.id == GameFlow.DefaultSpawn).transform.position;
        var visited = new HashSet<Vector3Int>(); var queue = new Queue<Vector3Int>();
        var seed = ground.WorldToCell(start); queue.Enqueue(seed); visited.Add(seed);
        var steps = new[] { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
        while (queue.Count > 0) { var c = queue.Dequeue(); foreach (var d in steps) { var n = c + d; if (ground.HasTile(n) && !blockers.HasTile(n) && visited.Add(n)) queue.Enqueue(n); } }
        var occupied = roots.SelectMany(g => g.GetComponentsInChildren<SpawnPoint>(true)).Select(s => s.transform.position).ToList();
        occupied.AddRange(roots.SelectMany(g => g.GetComponentsInChildren<HubStoryActor>(true)).Select(s => s.transform.position));
        occupied.AddRange(roots.Where(g => g.name == "Mr.Smith" || g.name == "Kaguya").Select(g => g.transform.position));
        occupied.AddRange(roots.SelectMany(g => g.GetComponentsInChildren<ZonePortal>(true)).Select(s => s.transform.position));
        var candidates = visited.Where(c => path.HasTile(c)).Where(c => { for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) if (!visited.Contains(c + new Vector3Int(x,y))) return false; return true; }).Select(c => ground.GetCellCenterWorld(c)).ToArray();
        string[] names = { "짐꾼 도윤", "약초꾼 연화", "농부 만석", "직공 소담", "장터 주민 순덕", "나무꾼 덕수", "피난민 은서", "행상 길수", "우물지기 복례", "수련생 진우", "서당 훈장 백림", "아이 단이", "곡물상 태식", "재봉사 미령", "노인 종수", "마을 주민 해란" };
        string[] lines = { "동쪽 길에서 짐을 나르던 이들이 돌아오지 않았소. 혼자 나가지는 마시오.", "숲의 약초에도 재가 내려앉았어요. 깨끗한 잎을 찾기가 점점 어렵네요.", "밭을 버릴 수는 없지. 괭이를 놓으면 내년 봄도 사라질 테니까.", "찢어진 옷은 꿰맬 수 있어요. 돌아올 사람이 있어야 옷도 쓸모가 있지요.", "촌장님을 찾는다면 마을 가운데로 가 보세요. 아직 사람들을 챙기고 계셔요.", "도끼 소리보다 짐승 울음이 먼저 들리더군. 오늘은 산에 들어가지 않겠소.", "불길을 피해 여기까지 왔어요. 이번에는 이곳이 오래 버텼으면 해요.", "사냥터로 가는 길은 네 갈래요. 깊이 들어가기 전에 무공부터 익히시오.", "물동이는 내가 채워 둘 테니, 다친 사람부터 마시게 해 주세요.", "빠른 검보다 흔들리지 않는 발이 먼저라고 배웠습니다. 오늘도 기본부터 할 겁니다.", "글을 잊은 사람에게 다시 글을 가르치듯, 마을도 처음부터 세우면 되는 게지.", "할아버지, 칼이 무겁지 않아요? 돌아오면 오늘 본 이야기를 들려주세요.", "남은 곡식은 함께 나누고 있소. 길을 지켜 주면 그걸로 충분하오.", "검객님 소매도 많이 헤졌네요. 살아 돌아오시면 손봐 드릴게요.", "오래전에도 이런 바람이 불었지. 이번에는 젊은이들이 덜 다쳤으면 좋겠구먼.", "남쪽에는 아직 재 냄새가 짙어요. 준비 없이 내려가지 마세요." };
        var owner = new GameObject("VillageResidents"); SceneManager.MoveGameObjectToScene(owner, scene);
        for (int i = 0; i < 16; i++)
        {
            var desired = start + new Vector3((i % 4 - 1.5f) * 13, (i / 4 - 1.5f) * 12);
            var position = candidates.Where(p => occupied.All(o => Vector3.Distance(o,p) >= 5)).OrderBy(p => (p-desired).sqrMagnitude).First();
            occupied.Add(position);
            var go = new GameObject(names[i]); go.transform.SetParent(owner.transform); go.transform.position = position; go.transform.localScale = Vector3.one * 1.5f;
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprites[i % 8]; renderer.sortingOrder = 1000 - Mathf.RoundToInt(position.y * 10);
            YSortRenderer.Attach(go, 0);
            var resident = go.AddComponent<VillageResident>(); resident.residentName = names[i]; resident.line = lines[i];
        }
        EditorSceneManager.SaveScene(scene); EditorSceneManager.CloseScene(scene, true);
        SceneManager.SetActiveScene(SceneManager.GetSceneByPath(MapGenVillageSetup.StagePath));
        return "Prepared 16 residents / 8 sprite variants; reachable 3x3 road tiles, spacing >=5; no blocking colliders.";
    }
}

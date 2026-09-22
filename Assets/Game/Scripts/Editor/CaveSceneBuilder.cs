using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 프롤로그 동굴 씬을 만든다. 지형은 깔지 않고 이동·줍기·씬 이동만 확인하는 최소 구성이다.
/// 메뉴를 실행하면 <b>열려 있는 씬이 닫히므로</b> 먼저 저장해야 한다.
/// </summary>
public static class CaveSceneBuilder
{
    const string CavePath = "Assets/Scenes/Cave.unity";
    const string MainPath = "Assets/Scenes/Main.unity";
    const string TitlePath = "Assets/Scenes/Title.unity";
    const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    const string ItemDir = "Assets/Game/Items";
    const string SwordItemPath = ItemDir + "/CaveSword.asset";

    const string SwordSprite = "Assets/Sprites/UI/CaveSword.png";
    const string ExitSprite = "Assets/Sprites/UI/CaveExit.png";

    /// <summary>동굴에서 마을로 나왔을 때 설 자리의 이름.</summary>
    const string MainSpawnId = "cave_exit";

    [MenuItem("Tools/AINPC/동굴 씬 만들기")]
    public static void BuildAll()
    {
        if (EditorApplication.isPlaying)
            throw new System.InvalidOperationException("Play를 종료한 뒤 실행하세요.");

        ImportSprites();
        FixPlayerPrefab();
        ItemDefinition sword = CreateSwordItem();
        BuildCaveScene(sword);
        AddMainSpawnPoint();
        RegisterBuildScenes();
        AssetDatabase.SaveAssets();
        Debug.Log("[Cave] 동굴 씬 준비 완료. Title → Cave → Main 순서로 확인하세요.");
    }

    // ── 1. 스프라이트 가져오기 설정 ─────────────────────────────

    static void ImportSprites()
    {
        foreach (string path in new[] { SwordSprite, ExitSprite,
                     "Assets/Sprites/UI/SettingsButton.png", "Assets/Sprites/UI/SettingsButton_Pressed.png" })
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }

    // ── 2. 플레이어 프리팹 손보기 ───────────────────────────────

    /// <summary>
    /// 프리팹에 PlayerStats / PlayerInventory 가 없어 씬 이동과 줍기가 동작하지 않는다.
    /// 깨진 스크립트 참조도 함께 지운다.
    /// </summary>
    static void FixPlayerPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
            if (removed > 0) Debug.Log($"[Cave] 깨진 스크립트 {removed}개를 프리팹에서 제거했습니다.");

            if (root.GetComponent<PlayerStats>() == null) root.AddComponent<PlayerStats>();
            if (root.GetComponent<Player_Attack>() == null) root.AddComponent<Player_Attack>();
            if (root.GetComponent<PlayerInventory>() == null) root.AddComponent<PlayerInventory>();

            // 줍기 판정을 받으려면 트리거 콜라이더가 하나 더 있어야 한다
            var triggers = root.GetComponents<Collider2D>();
            if (!triggers.Any(c => c.isTrigger))
            {
                var pick = root.AddComponent<CircleCollider2D>();
                pick.isTrigger = true;
                pick.radius = 0.6f;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ── 3. 검 아이템 ────────────────────────────────────────────

    static ItemDefinition CreateSwordItem()
    {
        Directory.CreateDirectory(ItemDir);
        var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(SwordItemPath);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemDefinition>();
            AssetDatabase.CreateAsset(item, SwordItemPath);
        }
        item.itemId = "cave_sword";
        item.displayName = "낡은 검";
        item.maxStack = 1;
        item.icon = AssetDatabase.LoadAssetAtPath<Sprite>(SwordSprite);
        EditorUtility.SetDirty(item);
        return item;
    }

    // ── 4. 동굴 씬 ──────────────────────────────────────────────

    static void BuildCaveScene(ItemDefinition sword)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 카메라
        var cameraGo = new GameObject("Main Camera");
        cameraGo.tag = "MainCamera";
        var camera = cameraGo.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 6f;
        camera.backgroundColor = new Color(0.07f, 0.07f, 0.09f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        cameraGo.AddComponent<AudioListener>();
        cameraGo.AddComponent<CameraFollow>();

        // 플레이어
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        player.transform.position = Vector3.zero;

        // 입구 — 다른 씬에서 돌아올 때 설 자리
        var spawn = new GameObject("Spawn_시작");
        SceneManager.MoveGameObjectToScene(spawn, scene);
        spawn.transform.position = Vector3.zero;
        spawn.AddComponent<SpawnPoint>().id = GameFlow.DefaultSpawn;

        // 검 — 주우면 사라지고 인벤토리에 들어간다
        var swordGo = new GameObject("Pickup_낡은 검");
        SceneManager.MoveGameObjectToScene(swordGo, scene);
        swordGo.transform.position = new Vector3(3.5f, 0.5f, 0f);
        var swordRenderer = swordGo.AddComponent<SpriteRenderer>();
        swordRenderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SwordSprite);
        swordGo.AddComponent<YSortRenderer>();
        var swordCollider = swordGo.AddComponent<BoxCollider2D>();
        swordCollider.isTrigger = true;
        swordCollider.size = new Vector2(1.2f, 1.6f);
        var pickup = swordGo.AddComponent<ItemPickup>();
        var pickupObject = new SerializedObject(pickup);
        pickupObject.FindProperty("item").objectReferenceValue = sword;
        pickupObject.FindProperty("quantity").intValue = 1;
        pickupObject.ApplyModifiedPropertiesWithoutUndo();

        // 출구 — 밟으면 마을로 넘어간다
        var exit = new GameObject("Portal_마을로");
        SceneManager.MoveGameObjectToScene(exit, scene);
        exit.transform.position = new Vector3(9f, 0f, 0f);
        var exitRenderer = exit.AddComponent<SpriteRenderer>();
        exitRenderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ExitSprite);
        exit.AddComponent<YSortRenderer>();
        var exitCollider = exit.AddComponent<BoxCollider2D>();
        exitCollider.isTrigger = true;
        exitCollider.size = new Vector2(1.2f, 1.2f);
        var portal = exit.AddComponent<ScenePortal>();
        var portalObject = new SerializedObject(portal);
        portalObject.FindProperty("targetScene").stringValue = "Main";
        portalObject.FindProperty("targetSpawn").stringValue = MainSpawnId;
        portalObject.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, CavePath);

        // UI(메뉴창·톱니바퀴·HUD)를 씬 오브젝트로 굽는다
        SceneUiBaker.Bake(scene);
        EditorSceneManager.SaveScene(scene, CavePath);
    }

    // ── 5. 마을 쪽 도착 지점 ────────────────────────────────────

    static void AddMainSpawnPoint()
    {
        Scene scene = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Single);
        bool exists = scene.GetRootGameObjects()
            .SelectMany(go => go.GetComponentsInChildren<SpawnPoint>(true))
            .Any(p => p.id == MainSpawnId);

        if (!exists)
        {
            var stats = Object.FindFirstObjectByType<PlayerStats>();
            var spawn = new GameObject("Spawn_동굴에서");
            SceneManager.MoveGameObjectToScene(spawn, scene);
            spawn.transform.position = stats != null ? stats.transform.position : Vector3.zero;
            spawn.AddComponent<SpawnPoint>().id = MainSpawnId;
        }

        // 메뉴창은 이미 구워져 있어 새 항목이 생기지 않는다. 지우고 다시 굽는다.
        ResetGeneratedUi(scene);
        SceneUiBaker.Bake(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>자동 생성된 메뉴창과 설정 버튼을 지운다. 손으로 만든 UI 는 건드리지 않는다.</summary>
    static void ResetGeneratedUi(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        foreach (var menu in roots.SelectMany(go => go.GetComponentsInChildren<PauseMenuUI>(true)).ToArray())
            Object.DestroyImmediate(menu.gameObject);
        foreach (var gear in roots.SelectMany(go => go.GetComponentsInChildren<SettingsButtonUI>(true)).ToArray())
            Object.DestroyImmediate(gear.gameObject);
    }

    // ── 6. 빌드 설정 ────────────────────────────────────────────

    static void RegisterBuildScenes()
    {
        var wanted = new[] { TitlePath, CavePath, MainPath };
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (string path in wanted)
        {
            var found = scenes.FirstOrDefault(s => s.path == path);
            if (found == null) scenes.Add(new EditorBuildSettingsScene(path, true));
            else found.enabled = true;
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}

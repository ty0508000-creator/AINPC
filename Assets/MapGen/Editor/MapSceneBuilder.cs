using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace AINPC.MapGen
{
    /// <summary>설계도를 받아 실제 씬(그리드 + 타일맵 + 소품 + 플레이어)을 만든다.</summary>
    public static class MapSceneBuilder
    {
        private const int WaterSortOrder = -30;
        private const int GroundSortOrder = -20;
        private const int PathSortOrder = -10;

        /// <summary>성공하면 true 와 함께 report 에 요약을, 실패하면 false 와 함께 실패 사유를 담는다.</summary>
        public static bool CreateScene(MapGenSettings settings, MapPlan plan, string scenePath, out string report)
        {
            TileBase groundTile = AssetDatabase.LoadAssetAtPath<TileBase>(MapGenSettings.Assets.GroundRuleTile);
            TileBase pathTile = AssetDatabase.LoadAssetAtPath<TileBase>(MapGenSettings.Assets.PathRuleTile);
            // 셰이더 방식 물이 준비돼 있으면 그쪽을 쓴다. 칸마다 애니메이션 기록이 안 남아 씬이 훨씬 가볍다
            Material waterMaterial = AssetDatabase.LoadAssetAtPath<Material>(MapGenSettings.Assets.WaterMaterial);
            TileBase waterTile = AssetDatabase.LoadAssetAtPath<TileBase>(MapGenSettings.Assets.WaterStaticTile);
            if (waterTile == null || waterMaterial == null)
            {
                waterTile = AssetDatabase.LoadAssetAtPath<TileBase>(MapGenSettings.Assets.WaterRuleTile);
                waterMaterial = null;
            }

            TileBase blockTile = LoadOrCreateBlockTile();

            if (groundTile == null || pathTile == null || waterTile == null)
            {
                report = "룰 타일 에셋을 찾을 수 없습니다. MapGenSettings.Assets 경로를 확인하세요.";
                return false;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // 2D 맵에는 방향광이 필요 없다
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<Light>() != null)
                    Object.DestroyImmediate(root);
            }

            var gridGo = new GameObject("Grid");
            var grid = gridGo.AddComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 0f);

            Tilemap water = CreateTilemap(gridGo.transform, "Water", WaterSortOrder);
            if (waterMaterial != null)
                water.GetComponent<TilemapRenderer>().sharedMaterial = waterMaterial;
            Tilemap ground = CreateTilemap(gridGo.transform, "Ground", GroundSortOrder);
            Tilemap path = CreateTilemap(gridGo.transform, "Path", PathSortOrder);
            Tilemap blockers = CreateTilemap(gridGo.transform, "Blockers", 0);
            SetupCollision(blockers);

            int waterCount = PaintWater(water, waterTile, plan);
            int groundCount = Paint(ground, groundTile, plan, (p, x, y) => !p.water[x, y]);
            int pathCount = Paint(path, pathTile, plan, (p, x, y) => p.path[x, y]);
            int blockCount = Paint(blockers, blockTile, plan, (p, x, y) => p.block[x, y]);

            var propsRoot = new GameObject("Props");
            int propCount = SpawnProps(propsRoot.transform, plan);

            GameObject player = null;
            if (settings.spawnPlayer)
                player = SpawnPlayer(plan);

            if (settings.setupCamera)
                SetupCamera(scene, plan, player);

            Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();

            report = string.Format(
                "씬: {0}\n크기: {1} x {2} 칸 (시드 {3})\n타일: 땅 {4} / 물 {5} / 길 {6} / 충돌 {7}\n소품: {8}개\n시작 위치: ({9:0.0}, {10:0.0})",
                scenePath, plan.width, plan.height, settings.seed,
                groundCount, waterCount, pathCount, blockCount, propCount,
                plan.spawnPoint.x, plan.spawnPoint.y);
            return true;
        }

        // ── 타일맵 ──────────────────────────────────────────────

        private static Tilemap CreateTilemap(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var tilemap = go.AddComponent<Tilemap>();
            var renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = sortingOrder;

            return tilemap;
        }

        private static void SetupCollision(Tilemap blockers)
        {
            var go = blockers.gameObject;
            go.GetComponent<TilemapRenderer>().enabled = false;   // 보이지 않는 충돌 전용 레이어

            var tilemapCollider = go.AddComponent<TilemapCollider2D>();
            var composite = go.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;

            var body = go.GetComponent<Rigidbody2D>();   // CompositeCollider2D 가 자동으로 붙인다
            if (body != null)
                body.bodyType = RigidbodyType2D.Static;

            tilemapCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
        }

        private delegate bool CellFilter(MapPlan plan, int x, int y);

        private static int Paint(Tilemap tilemap, TileBase tile, MapPlan plan, CellFilter filter)
        {
            var bounds = new BoundsInt(plan.originX, plan.originY, 0, plan.width, plan.height, 1);
            var tiles = new TileBase[plan.width * plan.height];

            int count = 0;
            for (int y = 0; y < plan.height; y++)
            {
                for (int x = 0; x < plan.width; x++)
                {
                    if (!filter(plan, x, y)) continue;

                    tiles[x + y * plan.width] = tile;
                    count++;
                }
            }

            tilemap.SetTilesBlock(bounds, tiles);
            return count;
        }

        /// <summary>
        /// 물은 땅 가장자리 밑으로 한 칸만 더 깐다. 전환 타일이 비쳐 보일 수 있는 건 딱 그 한 줄이고,
        /// 두 칸까지 깔면 완전히 가려 보이지도 않는 칸이 수천 개씩 씬 파일에 쌓인다.
        /// </summary>
        private static int PaintWater(Tilemap tilemap, TileBase tile, MapPlan plan)
        {
            var dilated = new bool[plan.width, plan.height];
            for (int x = 0; x < plan.width; x++)
            {
                for (int y = 0; y < plan.height; y++)
                {
                    if (!plan.water[x, y]) continue;

                    for (int ax = x - 1; ax <= x + 1; ax++)
                        for (int ay = y - 1; ay <= y + 1; ay++)
                            if (plan.In(ax, ay))
                                dilated[ax, ay] = true;
                }
            }

            return Paint(tilemap, tile, plan, (p, x, y) => dilated[x, y]);
        }

        private static TileBase LoadOrCreateBlockTile()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Tile>(MapGenSettings.Assets.BlockTile);
            if (existing != null)
                return existing;

            Directory.CreateDirectory(Path.GetDirectoryName(MapGenSettings.Assets.BlockTile));

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = "BlockTile";
            tile.colliderType = Tile.ColliderType.Grid;   // 스프라이트 없이 칸 모양 그대로 충돌
            tile.color = Color.clear;

            AssetDatabase.CreateAsset(tile, MapGenSettings.Assets.BlockTile);
            AssetDatabase.SaveAssets();
            return tile;
        }

        // ── 소품 ────────────────────────────────────────────────

        private static int SpawnProps(Transform root, MapPlan plan)
        {
            var groups = new System.Collections.Generic.Dictionary<string, Transform>();
            var houses = new System.Collections.Generic.Dictionary<int, Transform>();
            int spawned = 0;

            foreach (var prop in plan.props)
            {
                if (prop.sprite == null) continue;

                if (!groups.TryGetValue(prop.group, out var parent))
                {
                    var groupGo = new GameObject(prop.group);
                    groupGo.transform.SetParent(root, false);
                    parent = groupGo.transform;
                    groups[prop.group] = parent;
                }

                // 조각으로 조립된 집은 한 채씩 부모로 묶어 통째로 흐려질 수 있게 한다
                if (prop.groupId >= 0)
                    parent = GetHouseRoot(houses, parent, prop);

                var go = new GameObject(prop.sprite.name);
                go.transform.SetParent(parent, false);
                go.transform.position = BottomCenterToPosition(prop.sprite, prop.bottomCenter);

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = prop.sprite;
                renderer.flipX = prop.flipX;
                renderer.sortingOrder = MapGenSettings.SortBase
                                        - Mathf.RoundToInt(prop.SortY * MapGenSettings.SortPrecision)
                                        + prop.sortBias;
                spawned++;
            }

            return spawned;
        }

        /// <summary>집 한 채의 부모를 찾거나 만든다. 부모에는 가림 투명 처리를 붙여 둔다.</summary>
        private static Transform GetHouseRoot(
            System.Collections.Generic.Dictionary<int, Transform> houses, Transform parent, PropPlan prop)
        {
            if (houses.TryGetValue(prop.groupId, out Transform existing))
                return existing;

            var houseGo = new GameObject("House_" + prop.groupId);
            houseGo.transform.SetParent(parent, false);
            houseGo.transform.position = new Vector3(prop.bottomCenter.x, prop.bottomCenter.y, 0f);
            houseGo.AddComponent<FadeWhenPlayerBehind>();

            houses[prop.groupId] = houseGo.transform;
            return houseGo.transform;
        }

        /// <summary>스프라이트 바닥 중앙을 원하는 지점에 맞추기 위한 트랜스폼 위치.</summary>
        private static Vector3 BottomCenterToPosition(Sprite sprite, Vector2 bottomCenter)
        {
            float ppu = sprite.pixelsPerUnit;
            var offset = new Vector2(
                (sprite.rect.width * 0.5f - sprite.pivot.x) / ppu,
                -sprite.pivot.y / ppu);

            return new Vector3(bottomCenter.x - offset.x, bottomCenter.y - offset.y, 0f);
        }

        // ── 플레이어 / 카메라 ───────────────────────────────────

        private static GameObject SpawnPlayer(MapPlan plan)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapGenSettings.Assets.PlayerPrefab);
            if (prefab == null)
            {
                Debug.LogWarning("[MapGen] 플레이어 프리팹을 찾을 수 없습니다: " + MapGenSettings.Assets.PlayerPrefab);
                return null;
            }

            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.transform.position = new Vector3(plan.spawnPoint.x, plan.spawnPoint.y, 0f);

            YSortRenderer.Attach(player);

            return player;
        }

        private static void SetupCamera(Scene scene, MapPlan plan, GameObject player)
        {
            Camera camera = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                camera = root.GetComponentInChildren<Camera>();
                if (camera != null) break;
            }
            if (camera == null) return;

            camera.orthographic = true;
            camera.orthographicSize = 7f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.098f, 0.129f, 0.173f, 1f);
            camera.transform.position = new Vector3(plan.spawnPoint.x, plan.spawnPoint.y, -10f);

            var follow = camera.GetComponent<CameraFollow>();
            if (follow == null)
                follow = camera.gameObject.AddComponent<CameraFollow>();

            if (player != null)
            {
                var so = new SerializedObject(follow);
                var target = so.FindProperty("target");
                if (target != null)
                {
                    target.objectReferenceValue = player.transform;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
    }
}

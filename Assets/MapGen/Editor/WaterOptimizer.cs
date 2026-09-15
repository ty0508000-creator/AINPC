using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace AINPC.MapGen
{
    /// <summary>
    /// 물 타일맵이 씬 파일을 부풀리는 두 가지 원인을 정리한다.
    /// 1) 땅에 완전히 가려 보이지도 않는 물 칸 — 지운다.
    /// 2) 칸마다 붙는 애니메이션 기록 — 셰이더가 프레임을 돌리게 바꾼다.
    /// </summary>
    public static class WaterOptimizer
    {
        private const string WaterDir = "Assets/MapTileSet/ground_textures/water/";
        private const string AnimatedTilePath = WaterDir + "riverRule.asset";
        private const string StaticTilePath = WaterDir + "riverRule_Static.asset";
        private const string MaterialPath = WaterDir + "WaterFrameScroll.mat";
        private const string TexturePath = WaterDir + "river_animated.png";
        private const string ShaderName = "AINPC/Water Frame Scroll";

        // ── 1. 가려진 물 지우기 ─────────────────────────────────

        [MenuItem("Tools/맵 정리/가려진 물 타일 지우기")]
        private static void RemoveHiddenWaterMenu()
        {
            EditorUtility.DisplayDialog("맵 정리", RemoveHiddenWater(), "확인");
        }

        /// <summary>현재 씬에서 완전히 가려진 물 칸을 지우고 결과를 요약해 돌려준다.</summary>
        public static string RemoveHiddenWater()
        {
            List<Tilemap> water = FindWaterTilemaps(out List<Tilemap> all);
            if (water.Count == 0)
                return "물 룰타일이 깔린 타일맵을 찾지 못했습니다.";

            int removed = 0;
            int kept = 0;

            foreach (Tilemap map in water)
            {
                List<Tilemap> covers = CoveringTilemaps(map, all);
                var positions = new List<Vector3Int>();

                foreach (Vector3Int cell in map.cellBounds.allPositionsWithin)
                {
                    if (!map.HasTile(cell)) continue;

                    if (IsFullyCovered(cell, covers))
                        positions.Add(cell);
                    else
                        kept++;
                }

                if (positions.Count == 0) continue;

                Undo.RegisterCompleteObjectUndo(map, "가려진 물 타일 지우기");

                var empty = new TileBase[positions.Count];
                map.SetTiles(positions.ToArray(), empty);
                removed += positions.Count;
            }

            if (removed > 0)
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            string message = string.Format(
                "지운 물 칸: {0:N0}개\n남긴 물 칸: {1:N0}개\n\n" +
                "3x3 이웃까지 전부 위 레이어에 덮여 있는 칸만 지웠습니다.\n씬을 저장해야 반영됩니다.",
                removed, kept);

            Debug.Log("[맵 정리] " + message.Replace("\n", " "));
            return message;
        }

        /// <summary>이 칸과 8방향 이웃이 전부 위 레이어에 덮여 있는가.</summary>
        private static bool IsFullyCovered(Vector3Int cell, List<Tilemap> covers)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    var probe = new Vector3Int(cell.x + dx, cell.y + dy, cell.z);

                    bool covered = false;
                    foreach (Tilemap cover in covers)
                    {
                        if (cover.HasTile(probe))
                        {
                            covered = true;
                            break;
                        }
                    }

                    if (!covered)
                        return false;
                }
            }

            return true;
        }

        // ── 2. 셰이더 방식으로 전환 ─────────────────────────────

        [MenuItem("Tools/맵 정리/물 애니메이션을 셰이더로 전환")]
        private static void ConvertWaterToShaderMenu()
        {
            EditorUtility.DisplayDialog("맵 정리", ConvertWaterToShader(), "확인");
        }

        /// <summary>현재 씬의 물 타일맵을 정적 룰타일 + 스크롤 머티리얼로 바꾼다.</summary>
        public static string ConvertWaterToShader()
        {
            TileBase animated = AssetDatabase.LoadAssetAtPath<TileBase>(AnimatedTilePath);
            if (animated == null)
                return "riverRule 을 찾지 못했습니다.";

            TileBase staticTile = LoadOrCreateStaticTile(animated);
            Material material = LoadOrCreateMaterial();
            if (staticTile == null || material == null)
                return "정적 룰타일 또는 머티리얼을 준비하지 못했습니다.";

            List<Tilemap> water = FindWaterTilemaps(out _);
            int swapped = 0;

            foreach (Tilemap map in water)
            {
                Undo.RegisterCompleteObjectUndo(map, "물 셰이더 전환");
                map.SwapTile(animated, staticTile);

                var renderer = map.GetComponent<TilemapRenderer>();
                if (renderer != null)
                {
                    Undo.RecordObject(renderer, "물 셰이더 전환");
                    renderer.sharedMaterial = material;
                }

                swapped++;
            }

            if (swapped > 0)
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            string message = swapped > 0
                ? string.Format("타일맵 {0}개를 셰이더 방식으로 바꿨습니다.\n칸마다 붙던 애니메이션 기록이 사라집니다.\n씬을 저장해야 반영됩니다.", swapped)
                : "바꿀 물 타일맵을 찾지 못했습니다.";

            Debug.Log("[맵 정리] " + message.Replace("\n", " "));
            return message;
        }

        [MenuItem("Tools/맵 정리/물 애니메이션 되돌리기 (타일 애니메이션)")]
        private static void RevertWaterToAnimatedTileMenu()
        {
            EditorUtility.DisplayDialog("맵 정리", RevertWaterToAnimatedTile(), "확인");
        }

        /// <summary>셰이더 방식을 원래의 애니메이션 타일로 되돌린다.</summary>
        public static string RevertWaterToAnimatedTile()
        {
            TileBase animated = AssetDatabase.LoadAssetAtPath<TileBase>(AnimatedTilePath);
            TileBase staticTile = AssetDatabase.LoadAssetAtPath<TileBase>(StaticTilePath);
            if (animated == null || staticTile == null)
                return "룰타일 에셋을 찾지 못했습니다.";

            var spriteDefault = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            int reverted = 0;

            foreach (Tilemap map in AllTilemaps())
            {
                if (!map.ContainsTile(staticTile)) continue;

                Undo.RegisterCompleteObjectUndo(map, "물 애니메이션 되돌리기");
                map.SwapTile(staticTile, animated);

                var renderer = map.GetComponent<TilemapRenderer>();
                if (renderer != null)
                {
                    Undo.RecordObject(renderer, "물 애니메이션 되돌리기");
                    renderer.sharedMaterial = spriteDefault;
                }

                reverted++;
            }

            if (reverted > 0)
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            return reverted + "개 타일맵을 원래 애니메이션 타일로 되돌렸습니다.";
        }

        /// <summary>애니메이션 룰타일에서 첫 프레임만 남긴 정적 룰타일을 만든다.</summary>
        private static TileBase LoadOrCreateStaticTile(TileBase animated)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TileBase>(StaticTilePath);
            if (existing != null)
                return existing;

            var copy = Object.Instantiate(animated);
            copy.name = "riverRule_Static";

            var so = new SerializedObject(copy);
            var rules = so.FindProperty("m_TilingRules");
            if (rules == null)
            {
                Debug.LogError("[맵 정리] 룰타일 구조를 읽지 못했습니다.");
                Object.DestroyImmediate(copy);
                return null;
            }

            for (int i = 0; i < rules.arraySize; i++)
            {
                var rule = rules.GetArrayElementAtIndex(i);

                var output = rule.FindPropertyRelative("m_Output");
                if (output != null)
                    output.enumValueIndex = 0;   // Single

                var sprites = rule.FindPropertyRelative("m_Sprites");
                if (sprites != null && sprites.arraySize > 1)
                    sprites.arraySize = 1;       // 첫 프레임만 남긴다
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(copy, StaticTilePath);
            AssetDatabase.SaveAssets();

            Debug.Log("[맵 정리] 정적 룰타일 생성: " + StaticTilePath);
            return copy;
        }

        /// <summary>프레임을 밀어 주는 머티리얼을 만든다. 간격은 스프라이트 크기에서 계산한다.</summary>
        private static Material LoadOrCreateMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError("[맵 정리] 셰이더를 찾지 못했습니다: " + ShaderName);
                return null;
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            var material = new Material(shader) { name = "WaterFrameScroll" };

            if (texture != null)
            {
                material.SetTexture("_MainTex", texture);

                // 스프라이트 한 칸(32px)만큼이 한 프레임이다
                Sprite any = null;
                foreach (Object o in AssetDatabase.LoadAllAssetRepresentationsAtPath(TexturePath))
                {
                    any = o as Sprite;
                    if (any != null) break;
                }

                float frameWidth = any != null ? any.rect.width : 32f;
                material.SetFloat("_FrameStepU", frameWidth / texture.width);
            }

            material.SetFloat("_FrameCount", 5f);
            material.SetFloat("_FPS", 6f);

            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();

            Debug.Log("[맵 정리] 물 머티리얼 생성: " + MaterialPath);
            return material;
        }

        // ── 공통 ────────────────────────────────────────────────

        private static List<Tilemap> AllTilemaps()
        {
            var list = new List<Tilemap>();
            foreach (Tilemap map in Object.FindObjectsByType<Tilemap>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                list.Add(map);
            return list;
        }

        /// <summary>물 룰타일(애니메이션/정적 둘 다)이 깔린 타일맵.</summary>
        private static List<Tilemap> FindWaterTilemaps(out List<Tilemap> all)
        {
            all = AllTilemaps();

            TileBase animated = AssetDatabase.LoadAssetAtPath<TileBase>(AnimatedTilePath);
            TileBase staticTile = AssetDatabase.LoadAssetAtPath<TileBase>(StaticTilePath);

            var water = new List<Tilemap>();
            foreach (Tilemap map in all)
            {
                bool hasWater = (animated != null && map.ContainsTile(animated))
                                || (staticTile != null && map.ContainsTile(staticTile));

                if (hasWater)
                    water.Add(map);
            }

            return water;
        }

        /// <summary>이 타일맵보다 위에 그려지는(= 가릴 수 있는) 타일맵들.</summary>
        private static List<Tilemap> CoveringTilemaps(Tilemap water, List<Tilemap> all)
        {
            var waterRenderer = water.GetComponent<TilemapRenderer>();
            int waterOrder = waterRenderer != null ? waterRenderer.sortingOrder : 0;

            var covers = new List<Tilemap>();
            foreach (Tilemap map in all)
            {
                if (map == water) continue;

                var renderer = map.GetComponent<TilemapRenderer>();
                if (renderer == null || !renderer.enabled) continue;   // 충돌 전용 레이어는 가리지 못한다
                if (renderer.sortingOrder <= waterOrder) continue;

                covers.Add(map);
            }

            return covers;
        }
    }
}

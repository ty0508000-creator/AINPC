using System;
using System.Collections.Generic;

namespace AINPC.MapGen
{
    /// <summary>맵 생성 파라미터. 에디터 창에서 조절하고 EditorPrefs 에 저장된다.</summary>
    [Serializable]
    public class MapGenSettings
    {
        // 씬
        public string sceneName = "MapGen_Village";

        // 맵 크기 (타일 칸 수)
        public int width = 120;
        public int height = 100;
        public int seed = 20260915;

        // 물
        public int waterBorder = 4;      // 바깥 테두리 물 두께
        public bool river = true;
        public int riverWidth = 5;
        public float riverX = 0.25f;     // 강 중심 위치(가로 비율)
        public int lakeCount = 2;

        // 마을
        public bool village = true;
        public int villageWidth = 38;
        public int villageHeight = 28;
        public float villageX = 0.64f;   // 마을 중심 위치(가로 비율)
        public int blockSize = 18;       // 거리 간격 — 이 칸수마다 가로/세로 거리를 하나씩 깐다
        public int houseGap = 3;         // 집과 집 사이에 비워 둘 최소 칸수
        public float houseChance = 0.7f; // 자리마다 실제로 집을 세울 확률 (낮출수록 빈 터가 는다)

        // 자연물
        public float treeDensity = 0.10f;
        public float decorDensity = 0.05f;

        // 씬 구성
        public bool spawnPlayer = true;
        public bool setupCamera = true;

        /// <summary>타일 / 스프라이트 에셋 경로.</summary>
        public static class Assets
        {
            public const string GroundRuleTile = "Assets/MapTileSet/ground_textures/transitions/New Rule Tile.asset";
            public const string PathRuleTile   = "Assets/MapTileSet/ground_textures/transitions/New Rule Tile 1.asset";
            public const string WaterRuleTile  = "Assets/MapTileSet/ground_textures/water/riverRule.asset";

            public const string BlockTile      = "Assets/MapGen/BlockTile.asset";
            public const string PlayerPrefab   = "Assets/Prefabs/Player.prefab";

            public const string HousesTexture  = "Assets/testAsset/Houses_Pack/houses.png";

            // 집 조각은 아래 HouseRecipes 에서 조립한다. houses.png 에는 완성된 집이 없다.

            public const string PlantsDir = "Assets/testAsset/River/Plants/";
            public const string DecorDir  = "Assets/testAsset/River/ground_decorations/";

            public static readonly string[,] Trees =
            {
                { PlantsDir + "tree1.png", "tree1_0" },
                { PlantsDir + "tree2.png", "tree2_0" },
            };

            public static readonly string[] GiantTree = { PlantsDir + "giant_tree_1.png", "giant_tree_1_0" };

            public static readonly string[,] Decor =
            {
                { PlantsDir + "grass1.png", "grass1_0" },
                { PlantsDir + "grass2.png", "grass2_0" },
                { PlantsDir + "grass3.png", "grass3_0" },
                { PlantsDir + "grass4.png", "grass4_0" },
                { PlantsDir + "flower1.png", "flower1_0" },
                { PlantsDir + "roots1.png", "roots1_0" },
                { PlantsDir + "giant_tree_stump1.png", "giant_tree_stump1_0" },
                { DecorDir + "rock1.png", "rock1_0" },
                { DecorDir + "rock2.png", "rock2_0" },
                { DecorDir + "rock2.png", "rock2_2" },
                { DecorDir + "old_trunk1.png", "old_trunk1_0" },
                { DecorDir + "old_fence1.png", "old_fence1_0" },
            };

            public static readonly string[,] WaterPlants =
            {
                { PlantsDir + "water_reeds1.png", "water_reeds1_0" },
                { PlantsDir + "water_reeds2.png", "water_reeds2_0" },
                { PlantsDir + "water_reeds3.png", "water_reeds3_0" },
            };

            public static readonly string[,] LilyPads =
            {
                { PlantsDir + "lily_pad1.png", "lily_pad1_0" },
                { PlantsDir + "lily_pad1.png", "lily_pad1_1" },
                { PlantsDir + "lily_pad1.png", "lily_pad1_3" },
            };
        }

        /// <summary>집 한 채를 이루는 조각. 오프셋은 집 바닥 중앙 기준 픽셀(오른쪽/위가 +).</summary>
        public class HousePiece
        {
            public readonly string sprite;
            public readonly float dx;
            public readonly float dy;

            public HousePiece(string sprite, float dx, float dy)
            {
                this.sprite = sprite;
                this.dx = dx;
                this.dy = dy;
            }
        }

        /// <summary>집 조립 설계. pieces 는 아래(먼저 그릴) 조각부터 순서대로 둔다.</summary>
        public class HouseRecipe
        {
            public readonly string name;
            public readonly int tilesWide;   // 발판 가로 칸
            public readonly int tilesDeep;   // 발판 세로 칸 — 그림 높이가 아니라 땅에서 차지하는 깊이
            public readonly HousePiece[] pieces;

            public HouseRecipe(string name, int tilesWide, int tilesDeep, HousePiece[] pieces)
            {
                this.name = name;
                this.tilesWide = tilesWide;
                this.tilesDeep = tilesDeep;
                this.pieces = pieces;
            }
        }

        /// <summary>
        /// 집 조립표. houses.png 에는 완성된 집이 없고 지붕 / 벽 / 굴뚝이 따로 들어 있어서
        /// 여기서 쌓아 올린다. dy 는 벽 높이(66px)보다 살짝 낮게 잡아 이음매를 겹쳐 가린다.
        /// 색 변형 3종은 지붕에만 있고 벽(갈색 목조)은 공용이다.
        /// </summary>
        public static readonly HouseRecipe[] HouseRecipes = BuildHouseRecipes();

        private static HouseRecipe[] BuildHouseRecipes()
        {
            // 색 변형: 0=보라, 1=청록, 2=적갈
            string[] roofSlab   = { "houses_0",  "houses_2",  "houses_4"  };   // 178x107 평지붕
            string[] roofGable  = { "houses_22", "houses_25", "houses_28" };   // 168x204 박공지붕
            string[] upperFloor = { "houses_39", "houses_41", "houses_43" };   // 156x74  2층 벽
            const string wallWide   = "houses_34";   // 130x66
            const string wallNarrow = "houses_33";   //  65x66

            var list = new List<HouseRecipe>();
            for (int c = 0; c < 3; c++)
            {
                list.Add(new HouseRecipe("Cottage", 4, 2, new[]
                {
                    new HousePiece(wallWide, 0f, 0f),
                    new HousePiece(roofSlab[c], 0f, 62f),
                }));

                list.Add(new HouseRecipe("Gable", 5, 2, new[]
                {
                    new HousePiece(wallWide, 0f, 0f),
                    new HousePiece(roofGable[c], 0f, 60f),
                }));

                list.Add(new HouseRecipe("Manor", 8, 2, new[]
                {
                    new HousePiece(wallNarrow, -97.5f, 0f),
                    new HousePiece(wallWide, 0f, 0f),
                    new HousePiece(wallNarrow, 97.5f, 0f),
                    new HousePiece(roofGable[c], 0f, 60f),
                }));

                list.Add(new HouseRecipe("TwoStory", 5, 2, new[]
                {
                    new HousePiece(wallWide, 0f, 0f),
                    new HousePiece(upperFloor[c], 0f, 62f),
                    new HousePiece(roofSlab[c], 0f, 130f),
                }));
            }
            return list.ToArray();
        }

        /// <summary>정렬 기준 — YSortRenderer 가 원본이다(두 곳에 숫자를 두지 않는다).</summary>
        public const int SortBase = YSortRenderer.DefaultBaseOrder;
        public const float SortPrecision = YSortRenderer.DefaultPrecision;
    }
}

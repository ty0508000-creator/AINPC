using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AINPC.MapGen
{
    /// <summary>씬에 배치할 스프라이트 소품 하나.</summary>
    public class PropPlan
    {
        public Sprite sprite;
        public Vector2 bottomCenter;   // 스프라이트 바닥 중앙이 놓일 월드 좌표
        public bool flipX;
        public string group;           // 하이어라키 그룹 이름

        /// <summary>정렬에 쓸 y. 비워두면 bottomCenter.y 를 쓴다.
        /// 여러 조각으로 된 집은 조각들이 같은 값을 공유해야 지붕이 벽 뒤로 빠지지 않는다.</summary>
        public float? sortY;

        /// <summary>같은 sortY 안에서의 앞뒤 (클수록 앞). 한 칸 간격(SortPrecision)보다 작아야 한다.</summary>
        public int sortBias;

        /// <summary>한 덩어리(집 한 채)로 묶을 번호. -1 이면 묶지 않는다.
        /// 조각으로 조립된 집은 이 번호로 부모를 만들어 통째로 흐려질 수 있게 한다.</summary>
        public int groupId = -1;

        public float SortY => sortY ?? bottomCenter.y;
    }

    /// <summary>계산이 끝난 맵 설계도. 씬 빌더가 이걸 보고 타일을 찍는다.</summary>
    public class MapPlan
    {
        public int width;
        public int height;
        public int originX;
        public int originY;

        public bool[,] water;
        public bool[,] path;       // 흙길 + 다리
        public bool[,] bridge;     // 물 위의 길
        public bool[,] block;      // 충돌 타일
        public bool[,] reserved;   // 도로/건물 등 소품 금지 구역

        public readonly List<PropPlan> props = new List<PropPlan>();
        public Vector2 spawnPoint;
        public RectInt village;
        public bool hasVillage;

        /// <summary>지금까지 세운 집 채 수. 조각들을 한 채로 묶는 번호로도 쓴다.</summary>
        public int buildingCount;

        /// <summary>마을 거리의 중심 좌표. 집은 이 거리들을 따라 늘어선다.</summary>
        public readonly List<int> streetsX = new List<int>();   // 세로 거리의 x
        public readonly List<int> streetsY = new List<int>();   // 가로 거리의 y

        public bool In(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;

        /// <summary>칸 좌표를 월드 좌표로 변환.</summary>
        public Vector2 World(float cx, float cy) => new Vector2(cx + originX, cy + originY);
    }

    /// <summary>지형을 계산한다. 씬은 건드리지 않는다.</summary>
    public static class MapBuilder
    {
        private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        private static System.Random rng;
        private static float n1, n2, n3;

        public static MapPlan Build(MapGenSettings s)
        {
            spriteCache.Clear();

            rng = new System.Random(s.seed);
            n1 = Rand() * 1000f;
            n2 = Rand() * 1000f;
            n3 = Rand() * 1000f;

            int w = Mathf.Max(40, s.width);
            int h = Mathf.Max(40, s.height);

            var plan = new MapPlan
            {
                width = w,
                height = h,
                originX = -w / 2,
                originY = -h / 2,
                water = new bool[w, h],
                path = new bool[w, h],
                bridge = new bool[w, h],
                block = new bool[w, h],
                reserved = new bool[w, h],
            };

            // 마을 자리를 먼저 잡아둬야 강/호수를 피해서 만들 수 있다
            plan.hasVillage = s.village;
            int vw = Mathf.Clamp(s.villageWidth, 12, w - 12);
            int vh = Mathf.Clamp(s.villageHeight, 12, h - 12);
            int vcx = Mathf.Clamp(Mathf.RoundToInt(w * s.villageX), vw / 2 + 4, w - vw / 2 - 4);
            int vcy = h / 2;
            plan.village = new RectInt(vcx - vw / 2, vcy - vh / 2, vw, vh);

            MakeBorder(plan, s);
            MakeLakes(plan, s);
            if (s.river) MakeRiver(plan, s);
            if (s.village) ClearVillageGround(plan);

            int roadX = vcx;
            int roadY = vcy;
            if (s.village) MakeRoads(plan, s, roadX, roadY);

            // 물은 기본적으로 막는다 (다리는 예외)
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (plan.water[x, y] && !plan.bridge[x, y])
                        plan.block[x, y] = true;

            if (s.village) MakeBuildings(plan, s);
            MakeTrees(plan, s);
            MakeDecor(plan, s);
            MakeWaterPlants(plan);

            // 맵 밖으로 못 나가게 가장자리는 무조건 막는다
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (x < 2 || y < 2 || x >= w - 2 || y >= h - 2)
                        plan.block[x, y] = true;

            plan.spawnPoint = FindSpawn(plan, s, roadX, roadY);
            return plan;
        }

        // ── 지형 ────────────────────────────────────────────────

        private static void MakeBorder(MapPlan p, MapGenSettings s)
        {
            for (int x = 0; x < p.width; x++)
            {
                for (int y = 0; y < p.height; y++)
                {
                    int edge = Mathf.Min(Mathf.Min(x, p.width - 1 - x), Mathf.Min(y, p.height - 1 - y));
                    float thickness = s.waterBorder + Mathf.PerlinNoise(x * 0.09f + n1, y * 0.09f + n1) * 3f;
                    if (edge < thickness)
                        p.water[x, y] = true;
                }
            }
        }

        private static void MakeLakes(MapPlan p, MapGenSettings s)
        {
            var safe = p.village;
            safe.xMin -= 6;
            safe.yMin -= 6;
            safe.xMax += 6;
            safe.yMax += 6;

            for (int i = 0; i < s.lakeCount; i++)
            {
                int cx = 0, cy = 0;
                bool ok = false;
                for (int tries = 0; tries < 40 && !ok; tries++)
                {
                    cx = Mathf.RoundToInt(Mathf.Lerp(p.width * 0.12f, p.width * 0.88f, Rand()));
                    cy = Mathf.RoundToInt(Mathf.Lerp(p.height * 0.12f, p.height * 0.88f, Rand()));
                    ok = !(s.village && safe.Contains(new Vector2Int(cx, cy)));
                }
                if (!ok) continue;

                float radius = Mathf.Lerp(5f, 10f, Rand());
                int r = Mathf.CeilToInt(radius * 1.6f);
                for (int x = cx - r; x <= cx + r; x++)
                {
                    for (int y = cy - r; y <= cy + r; y++)
                    {
                        if (!p.In(x, y)) continue;
                        if (s.village && safe.Contains(new Vector2Int(x, y))) continue;

                        float wobble = Mathf.PerlinNoise(x * 0.14f + n2 + i * 37f, y * 0.14f + n2);
                        float rr = radius * (0.65f + wobble * 0.7f);
                        if (Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) < rr)
                            p.water[x, y] = true;
                    }
                }
            }
        }

        private static void MakeRiver(MapPlan p, MapGenSettings s)
        {
            float baseX = p.width * Mathf.Clamp01(s.riverX);
            float phase = Rand() * 10f;

            for (int y = 0; y < p.height; y++)
            {
                float cx = baseX
                           + Mathf.Sin(y * 0.055f + phase) * p.width * 0.05f
                           + (Mathf.PerlinNoise(y * 0.045f + n3, 11.7f) - 0.5f) * p.width * 0.07f;

                float half = s.riverWidth * 0.5f + (Mathf.PerlinNoise(y * 0.11f + n3, 3.4f) - 0.5f) * 1.6f;
                int x0 = Mathf.FloorToInt(cx - half);
                int x1 = Mathf.CeilToInt(cx + half);

                for (int x = x0; x <= x1; x++)
                    if (p.In(x, y))
                        p.water[x, y] = true;
            }
        }

        /// <summary>마을 자리에서는 물을 걷어낸다.</summary>
        private static void ClearVillageGround(MapPlan p)
        {
            var r = p.village;
            for (int x = r.xMin - 2; x < r.xMax + 2; x++)
                for (int y = r.yMin - 2; y < r.yMax + 2; y++)
                    if (p.In(x, y))
                        p.water[x, y] = false;
        }

        // ── 길 / 다리 ───────────────────────────────────────────

        private static void MakeRoads(MapPlan p, MapGenSettings s, int roadX, int roadY)
        {
            int margin = s.waterBorder + 5;

            // 동서 대로 — 맵을 가로지르며 강 위는 다리가 된다 (마을 밖으로 나가는 길)
            for (int x = margin; x < p.width - margin; x++)
                for (int y = roadY - 1; y <= roadY + 1; y++)
                    PaintRoad(p, x, y);

            if (!p.hasVillage)
                return;

            RectInt v = p.village;
            int block = Mathf.Max(8, s.blockSize);

            // 광장 교차점에서 blockSize 간격으로 거리를 깐다.
            // 마을 가장자리 3칸은 집이 들어설 자리로 남긴다.
            CollectStreets(p.streetsX, roadX, block, v.xMin + 3, v.xMax - 4);
            CollectStreets(p.streetsY, roadY, block, v.yMin + 3, v.yMax - 4);

            foreach (int sx in p.streetsX)
                for (int y = v.yMin + 1; y < v.yMax - 1; y++)
                    for (int x = sx - 1; x <= sx + 1; x++)
                        PaintRoad(p, x, y);

            foreach (int sy in p.streetsY)
                for (int x = v.xMin + 1; x < v.xMax - 1; x++)
                    for (int y = sy - 1; y <= sy + 1; y++)
                        PaintRoad(p, x, y);

            // 광장 — 중앙 교차점
            for (int x = roadX - 4; x <= roadX + 4; x++)
                for (int y = roadY - 3; y <= roadY + 3; y++)
                    PaintRoad(p, x, y);
        }

        /// <summary>center 에서 step 간격으로 lo..hi 안의 거리 중심을 모은다 (오름차순).</summary>
        private static void CollectStreets(List<int> into, int center, int step, int lo, int hi)
        {
            into.Clear();

            for (int c = center; c >= lo; c -= step)
                into.Add(c);
            into.Reverse();

            for (int c = center + step; c <= hi; c += step)
                into.Add(c);
        }

        private static void PaintRoad(MapPlan p, int x, int y)
        {
            if (!p.In(x, y)) return;

            p.path[x, y] = true;
            p.reserved[x, y] = true;
            if (p.water[x, y])
                p.bridge[x, y] = true;
        }

        // ── 건물 ────────────────────────────────────────────────

        /// <summary>스프라이트까지 찾아둔 집 한 채의 설계.</summary>
        private class HousePlan
        {
            public string name;
            public int tilesWide;
            public int tilesDeep;      // 땅에서 차지하는 세로 칸 (충돌/배치 기준)
            public Sprite[] sprites;   // 아래 조각부터
            public Vector2[] offsets;  // 집 바닥 중앙 -> 조각 바닥 중앙 (월드 단위)
        }

        /// <summary>레시피의 조각 이름을 실제 스프라이트로 바꾼다. 조각이 하나라도 없으면 그 레시피는 버린다.</summary>
        private static List<HousePlan> LoadHouses()
        {
            var plans = new List<HousePlan>();

            foreach (MapGenSettings.HouseRecipe recipe in MapGenSettings.HouseRecipes)
            {
                var sprites = new List<Sprite>();
                var offsets = new List<Vector2>();

                foreach (MapGenSettings.HousePiece piece in recipe.pieces)
                {
                    Sprite sp = LoadSprite(MapGenSettings.Assets.HousesTexture, piece.sprite);
                    if (sp == null)
                    {
                        sprites.Clear();
                        break;
                    }
                    sprites.Add(sp);
                    offsets.Add(new Vector2(piece.dx / sp.pixelsPerUnit, piece.dy / sp.pixelsPerUnit));
                }

                if (sprites.Count == 0)
                {
                    Debug.LogWarning("[MapGen] 집 레시피 '" + recipe.name + "' 의 조각을 찾지 못해 건너뜁니다.");
                    continue;
                }

                plans.Add(new HousePlan
                {
                    name = recipe.name,
                    tilesWide = recipe.tilesWide,
                    tilesDeep = recipe.tilesDeep,
                    sprites = sprites.ToArray(),
                    offsets = offsets.ToArray(),
                });
            }

            return plans;
        }

        private static void MakeBuildings(MapPlan p, MapGenSettings s)
        {
            List<HousePlan> houses = LoadHouses();
            if (houses.Count == 0)
            {
                Debug.LogWarning("[MapGen] 집 스프라이트를 찾지 못해 건물을 배치하지 않았습니다.");
                return;
            }

            var v = p.village;

            // 집이 넘어가면 안 되는 마을 경계 (RectInt 의 max 는 미포함이라 1 을 뺀다)
            int xLow = v.xMin, xHigh = v.xMax - 1;
            int yLow = v.yMin, yHigh = v.yMax - 1;

            // 가로 거리마다 위아래로 한 줄씩
            foreach (int sy in p.streetsY)
            {
                PlaceRow(p, houses, s, v.xMin + 1, v.xMax - 1, sy + 3, true, yLow, yHigh);
                PlaceRow(p, houses, s, v.xMin + 1, v.xMax - 1, sy - 3, false, yLow, yHigh);
            }

            // 세로 거리마다 좌우로 한 줄씩. 교차점에서 겹치는 건 AreaFree 가 걸러낸다
            foreach (int sx in p.streetsX)
            {
                PlaceColumn(p, houses, s, v.yMin + 1, v.yMax - 1, sx + 3, true, xLow, xHigh);
                PlaceColumn(p, houses, s, v.yMin + 1, v.yMax - 1, sx - 3, false, xLow, xHigh);
            }
        }

        /// <summary>가로줄 배치. above=true 면 도로 북쪽(바닥이 anchorY), false 면 남쪽(윗면이 anchorY).
        /// yLow..yHigh(양끝 포함) 를 벗어나는 집은 건너뛴다.</summary>
        private static void PlaceRow(MapPlan p, List<HousePlan> houses, MapGenSettings s, int xFrom, int xTo, int anchorY, bool above, int yLow, int yHigh)
        {
            // 세로 경계 검사는 이 줄 내내 같은 결과이므로 미리 걸러낸다.
            // 루프 안에서 매번 뽑아 버리면 x 가 안 늘어나 guard 만 태운다.
            var fits = new List<HousePlan>();
            foreach (HousePlan h in houses)
            {
                int b = above ? anchorY : anchorY - h.tilesDeep + 1;
                if (b >= yLow && b + h.tilesDeep - 1 <= yHigh)
                    fits.Add(h);
            }
            if (fits.Count == 0)
                return;

            int narrowest = int.MaxValue;
            foreach (HousePlan h in fits)
                narrowest = Mathf.Min(narrowest, h.tilesWide);

            int x = xFrom;
            int guard = 0;
            int guardLimit = (xTo - xFrom) * 2 + 200;   // 막힌 칸을 건너뛰며 끝까지 갈 수 있게
            while (x < xTo && guard++ < guardLimit)
            {
                HousePlan house = fits[rng.Next(fits.Count)];
                int bottom = above ? anchorY : anchorY - house.tilesDeep + 1;

                // 남은 폭에 안 들어가면 더 좁은 집을 뽑아본다 (제일 좁은 집도 안 되면 그때 끝낸다)
                if (x + house.tilesWide > xTo)
                {
                    if (x + narrowest > xTo) break;
                    continue;
                }

                if (AreaFree(p, x, bottom, house.tilesWide, house.tilesDeep))
                {
                    // 확률에 걸리면 자리를 비워 둔다 (빈 터에는 나중에 풀/나무가 들어간다)
                    if (Rand() <= s.houseChance)
                        PlaceBuilding(p, house, x, bottom);

                    x += house.tilesWide + HouseGap(s);
                }
                else
                {
                    x += 2;
                }
            }
        }

        /// <summary>세로줄 배치. right=true 면 골목 동쪽(왼쪽 변이 anchorX).
        /// xLow..xHigh(양끝 포함) 를 벗어나는 집은 건너뛴다.</summary>
        private static void PlaceColumn(MapPlan p, List<HousePlan> houses, MapGenSettings s, int yFrom, int yTo, int anchorX, bool right, int xLow, int xHigh)
        {
            // 가로 경계 검사는 이 골목 내내 같은 결과이므로 미리 걸러낸다
            var fits = new List<HousePlan>();
            foreach (HousePlan h in houses)
            {
                int l = right ? anchorX : anchorX - h.tilesWide + 1;
                if (l >= xLow && l + h.tilesWide - 1 <= xHigh)
                    fits.Add(h);
            }
            if (fits.Count == 0)
                return;

            int shortest = int.MaxValue;
            foreach (HousePlan h in fits)
                shortest = Mathf.Min(shortest, h.tilesDeep);

            int y = yFrom;
            int guard = 0;
            int guardLimit = (yTo - yFrom) * 2 + 200;   // 막힌 칸을 건너뛰며 끝까지 갈 수 있게
            while (y < yTo && guard++ < guardLimit)
            {
                HousePlan house = fits[rng.Next(fits.Count)];
                int left = right ? anchorX : anchorX - house.tilesWide + 1;

                // 남은 높이에 안 들어가면 더 얕은 집을 뽑아본다 (제일 얕은 집도 안 되면 그때 끝낸다)
                if (y + house.tilesDeep > yTo)
                {
                    if (y + shortest > yTo) break;
                    continue;
                }

                if (AreaFree(p, left, y, house.tilesWide, house.tilesDeep))
                {
                    if (Rand() <= s.houseChance)
                        PlaceBuilding(p, house, left, y);

                    y += house.tilesDeep + HouseGap(s);
                }
                else
                {
                    y += 2;
                }
            }
        }

        private static void PlaceBuilding(MapPlan p, HousePlan house, int left, int bottom)
        {
            // 소품 금지 구역은 한 칸 더 넓게. 집 그림이 위로 솟으므로 뒤쪽도 한 칸 더 비운다
            for (int x = left - 1; x <= left + house.tilesWide; x++)
                for (int y = bottom - 1; y <= bottom + house.tilesDeep + 1; y++)
                    if (p.In(x, y))
                        p.reserved[x, y] = true;

            // 발판 전체가 충돌 (집이 실제로 서 있는 땅)
            for (int x = left; x < left + house.tilesWide; x++)
                for (int y = bottom; y < bottom + house.tilesDeep; y++)
                    if (p.In(x, y))
                        p.block[x, y] = true;

            Vector2 baseAt = p.World(left + house.tilesWide * 0.5f, bottom);

            // 한 채의 조각들은 정렬 y 를 공유해야 흩어지지 않는다.
            // 그냥 각자 y 로 정렬하면 위에 있는 지붕일수록 order 가 작아져 벽 뒤로 숨는다.
            int houseId = p.buildingCount++;
            for (int i = 0; i < house.sprites.Length; i++)
            {
                p.props.Add(new PropPlan
                {
                    sprite = house.sprites[i],
                    bottomCenter = baseAt + house.offsets[i],
                    group = "Buildings",
                    sortY = baseAt.y,
                    sortBias = i,
                    groupId = houseId,
                });
            }
        }

        private static bool AreaFree(MapPlan p, int left, int bottom, int w, int h)
        {
            for (int x = left; x < left + w; x++)
            {
                for (int y = bottom; y < bottom + h; y++)
                {
                    if (!p.In(x, y)) return false;
                    if (p.water[x, y] || p.path[x, y] || p.reserved[x, y] || p.block[x, y]) return false;
                }
            }
            return true;
        }

        // ── 나무 / 소품 ─────────────────────────────────────────

        private static void MakeTrees(MapPlan p, MapGenSettings s)
        {
            var trees = new List<Sprite>();
            for (int i = 0; i < MapGenSettings.Assets.Trees.GetLength(0); i++)
            {
                var sp = LoadSprite(MapGenSettings.Assets.Trees[i, 0], MapGenSettings.Assets.Trees[i, 1]);
                if (sp != null) trees.Add(sp);
            }
            if (trees.Count == 0) return;

            Sprite giant = LoadSprite(MapGenSettings.Assets.GiantTree[0], MapGenSettings.Assets.GiantTree[1]);

            for (int x = 2; x < p.width - 2; x++)
            {
                for (int y = 2; y < p.height - 2; y++)
                {
                    if (p.water[x, y] || p.path[x, y] || p.reserved[x, y] || p.block[x, y]) continue;
                    if (p.hasVillage && p.village.Contains(new Vector2Int(x, y))) continue;
                    if (NearPath(p, x, y, 2)) continue;

                    float dens = Mathf.PerlinNoise(x * 0.07f + n2, y * 0.07f + n2);
                    dens = Mathf.InverseLerp(0.42f, 0.85f, dens);
                    dens *= dens;   // 숲이 뭉쳐서 자라도록
                    if (Rand() > s.treeDensity * dens * 6f) continue;

                    bool useGiant = giant != null && Rand() < 0.04f && AreaFree(p, x - 2, y - 1, 5, 5);
                    Sprite sp = useGiant ? giant : trees[rng.Next(trees.Count)];
                    int pad = useGiant ? 2 : 1;

                    if (!AreaFree(p, x - pad, y - 1, pad * 2 + 1, pad * 2 + 1)) continue;

                    for (int ax = x - pad; ax <= x + pad; ax++)
                        for (int ay = y - 1; ay <= y + pad; ay++)
                            if (p.In(ax, ay))
                                p.reserved[ax, ay] = true;

                    // 밑동만 막는다
                    p.block[x, y] = true;
                    if (useGiant && p.In(x + 1, y))
                        p.block[x + 1, y] = true;

                    p.props.Add(new PropPlan
                    {
                        sprite = sp,
                        bottomCenter = p.World(x + 0.5f, y),
                        flipX = !useGiant && Rand() < 0.5f,
                        group = "Trees",
                    });
                }
            }
        }

        private static void MakeDecor(MapPlan p, MapGenSettings s)
        {
            var decor = new List<Sprite>();
            for (int i = 0; i < MapGenSettings.Assets.Decor.GetLength(0); i++)
            {
                var sp = LoadSprite(MapGenSettings.Assets.Decor[i, 0], MapGenSettings.Assets.Decor[i, 1]);
                if (sp != null) decor.Add(sp);
            }
            if (decor.Count == 0) return;

            for (int x = 2; x < p.width - 2; x++)
            {
                for (int y = 2; y < p.height - 2; y++)
                {
                    if (p.water[x, y] || p.path[x, y] || p.block[x, y] || p.reserved[x, y]) continue;
                    if (Rand() > s.decorDensity) continue;

                    p.props.Add(new PropPlan
                    {
                        sprite = decor[rng.Next(decor.Count)],
                        bottomCenter = p.World(x + 0.5f, y),
                        flipX = Rand() < 0.5f,
                        group = "Decor",
                    });
                }
            }
        }

        private static void MakeWaterPlants(MapPlan p)
        {
            var reeds = new List<Sprite>();
            for (int i = 0; i < MapGenSettings.Assets.WaterPlants.GetLength(0); i++)
            {
                var sp = LoadSprite(MapGenSettings.Assets.WaterPlants[i, 0], MapGenSettings.Assets.WaterPlants[i, 1]);
                if (sp != null) reeds.Add(sp);
            }

            var pads = new List<Sprite>();
            for (int i = 0; i < MapGenSettings.Assets.LilyPads.GetLength(0); i++)
            {
                var sp = LoadSprite(MapGenSettings.Assets.LilyPads[i, 0], MapGenSettings.Assets.LilyPads[i, 1]);
                if (sp != null) pads.Add(sp);
            }

            for (int x = 3; x < p.width - 3; x++)
            {
                for (int y = 3; y < p.height - 3; y++)
                {
                    if (!p.water[x, y] || p.bridge[x, y]) continue;
                    if (NearBridge(p, x, y)) continue;

                    bool shore = !p.water[x + 1, y] || !p.water[x - 1, y] || !p.water[x, y + 1] || !p.water[x, y - 1];

                    if (shore)
                    {
                        if (reeds.Count > 0 && Rand() < 0.10f)
                        {
                            p.props.Add(new PropPlan
                            {
                                sprite = reeds[rng.Next(reeds.Count)],
                                bottomCenter = p.World(x + 0.5f, y),
                                flipX = Rand() < 0.5f,
                                group = "WaterPlants",
                            });
                        }
                    }
                    else if (pads.Count > 0 && Rand() < 0.05f)
                    {
                        p.props.Add(new PropPlan
                        {
                            sprite = pads[rng.Next(pads.Count)],
                            bottomCenter = p.World(x + 0.5f, y + 0.4f),
                            group = "WaterPlants",
                        });
                    }
                }
            }
        }

        // ── 헬퍼 ────────────────────────────────────────────────

        private static bool NearPath(MapPlan p, int x, int y, int r)
        {
            for (int ax = x - r; ax <= x + r; ax++)
                for (int ay = y - r; ay <= y + r; ay++)
                    if (p.In(ax, ay) && p.path[ax, ay])
                        return true;
            return false;
        }

        private static bool NearBridge(MapPlan p, int x, int y)
        {
            for (int ax = x - 1; ax <= x + 1; ax++)
                for (int ay = y - 1; ay <= y + 1; ay++)
                    if (p.In(ax, ay) && p.bridge[ax, ay])
                        return true;
            return false;
        }

        /// <summary>광장 한가운데에서 가장 가까운, 막히지 않은 칸을 찾는다.</summary>
        private static Vector2 FindSpawn(MapPlan p, MapGenSettings s, int roadX, int roadY)
        {
            int sx = s.village ? roadX : p.width / 2;
            int sy = s.village ? roadY : p.height / 2;

            // 바깥으로 넓혀가며 "첫 칸"을 집으면 사각형 좌하단 모서리가 걸린다.
            // 칸 수가 많아야 9만이라 전부 훑어 제일 가까운 칸을 고르는 편이 간단하고 정확하다.
            int bestX = -1, bestY = -1;
            int bestDist = int.MaxValue;

            for (int x = 0; x < p.width; x++)
            {
                for (int y = 0; y < p.height; y++)
                {
                    if (p.block[x, y] || p.water[x, y]) continue;

                    int dx = x - sx;
                    int dy = y - sy;
                    int dist = dx * dx + dy * dy;
                    if (dist >= bestDist) continue;

                    bestDist = dist;
                    bestX = x;
                    bestY = y;
                }
            }

            if (bestX < 0)
                return p.World(p.width * 0.5f, p.height * 0.5f);

            return p.World(bestX + 0.5f, bestY + 0.5f);
        }

        /// <summary>집 사이에 둘 여유 칸. 설정값에 약간의 들쭉날쭉함을 더한다.</summary>
        private static int HouseGap(MapGenSettings s) => Mathf.Max(1, s.houseGap) + rng.Next(3);

        private static float Rand() => (float)rng.NextDouble();

        private static Sprite LoadSprite(string path, string name)
        {
            string key = path + "#" + name;
            if (spriteCache.TryGetValue(key, out var cached))
                return cached;

            Sprite found = null;
            foreach (var obj in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            {
                if (obj is Sprite sp && sp.name == name)
                {
                    found = sp;
                    break;
                }
            }
            if (found == null)
                Debug.LogWarning("[MapGen] 스프라이트를 찾을 수 없습니다: " + path + " / " + name);

            spriteCache[key] = found;
            return found;
        }
    }
}

using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 인벤토리 창 프리팹(InventoryCanvas)과 그 외형 에셋을 만들고 게임 씬에 배치한다.
/// 런타임에는 UI 를 만들지 않으며, 여기서 만든 프리팹을 씬이 그대로 들고 있는다.
/// </summary>
public static class InventoryUiBuilder
{
    public const string PrefabPath = "Assets/Game/Prefabs/UI/InventoryCanvas.prefab";
    public const string ItemDatabasePath = "Assets/Resources/ItemDatabase.asset";
    public const string ArtFolder = "Assets/Art/UI/Inventory";

    /// <summary>플레이어가 돌아다니는 게임 씬. 수련록(RpgUI)과 ESC 메뉴가 있는 씬과 같다.</summary>
    public static readonly string[] GameScenes = {
        "Assets/Scenes/Main.unity", "Assets/Scenes/Cave.unity", "Assets/Scenes/mapstory1.unity",
        "Assets/Scenes/HuntingGround1.unity", "Assets/Scenes/HuntingGround2.unity",
        "Assets/Scenes/HuntingGround3.unity", "Assets/Scenes/HuntingGround4.unity",
        "Assets/Scenes/Region2.unity", "Assets/Scenes/Region3.unity", "Assets/Scenes/Region4.unity",
        "Assets/Scenes/AshKingArena.unity", "Assets/Scenes/FinalBoss.unity",
    };

    // 수련록 창(RpgUI.Illustrated)과 같은 먹색 팔레트
    static readonly Color WindowColor = new Color(0.035f, 0.043f, 0.045f, 0.94f);
    static readonly Color TooltipColor = new Color(0.055f, 0.07f, 0.075f, 0.97f);
    static readonly Color EdgeColor = new Color(0.36f, 0.52f, 0.46f, 0.5f);
    static readonly Color RuleColor = new Color(0.33f, 0.43f, 0.40f, 0.65f);
    static readonly Color SilhouetteColor = new Color(0.92f, 0.91f, 0.85f, 0.14f);

    const float WindowWidth = 520f, WindowHeight = 660f, Padding = 32f;
    const float BagSlotSize = 84f, BagSlotSpacing = 9f, EquipmentSlotSize = 96f;
    const int BagColumns = 5;
    // 픽셀 테두리 한 칸을 화면 2단위로 키워 다른 도트 그림과 굵기를 맞춘다.
    const float FramePixelScale = 0.5f;

    [MenuItem("Tools/AINPC/Inventory/Build Inventory UI")]
    static void BuildFromMenu()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
            !EditorUtility.DisplayDialog("인벤토리 UI", "InventoryCanvas 프리팹을 다시 만들면 프리팹에서 직접 고친 내용이 사라집니다. 계속할까요?", "다시 만들기", "취소"))
            return;
        Build();
    }

    [MenuItem("Tools/AINPC/Inventory/Place Inventory UI In Game Scenes")]
    static void PlaceFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        PlaceInGameScenes();
    }

    /// <summary>외형 에셋, 아이템 목록, 프리팹을 만든다. 프리팹이 있으면 같은 GUID 로 덮어쓴다.</summary>
    public static GameObject Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Play를 종료한 뒤 실행하세요.");
        var art = CreateArt();
        var database = CreateItemDatabase();
        var bodyFont = Resources.Load<TMP_FontAsset>("Fonts/UiBody SDF");
        var titleFont = Resources.Load<TMP_FontAsset>("Fonts/UiTitle SDF");
        if (bodyFont == null || titleFont == null) throw new InvalidOperationException("Resources/Fonts 의 UiBody/UiTitle 글꼴이 없습니다.");

        var root = BuildHierarchy(art, database, bodyFont, titleFont);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            if (!saved) throw new InvalidOperationException("프리팹 저장 실패: " + PrefabPath);
            return prefab;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    /// <summary>
    /// 게임 씬마다 InventoryCanvas 를 놓고, 이제 쓰지 않는 수련록 소지품 탭과 ESC 메뉴 인벤토리 글자판을 지운다.
    /// 열려 있던 씬은 닫힌다.
    /// </summary>
    public static void PlaceInGameScenes()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Play를 종료한 뒤 실행하세요.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) prefab = Build();
        foreach (string path in GameScenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            if (UnityEngine.Object.FindFirstObjectByType<InventoryWindow>(FindObjectsInactive.Include) == null)
                PrefabUtility.InstantiatePrefab(prefab, scene);
            RemoveRetiredInventoryViews();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }

    static void RemoveRetiredInventoryViews()
    {
        foreach (var transform in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (transform == null) continue;
            string parent = transform.parent != null ? transform.parent.name : "";
            bool retired =
                (parent == "Cultivation" && (transform.name == "Inventory" || transform.name == "소지품  I")) || // 수련록 소지품 탭
                (parent == "Content" && transform.name == "Inventory" && transform.GetComponentInParent<PauseMenuUI>(true) != null); // ESC 메뉴 글자판
            if (retired) UnityEngine.Object.DestroyImmediate(transform.gameObject);
        }
        foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (text.text == "기록  C / K / I") text.text = "기록  C / K";
    }

    // ── 아이템 목록 ─────────────────────────────────────────────

    static ItemDatabase CreateItemDatabase()
    {
        var database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ItemDatabasePath);
        if (database == null)
        {
            database = ScriptableObject.CreateInstance<ItemDatabase>();
            AssetDatabase.CreateAsset(database, ItemDatabasePath);
        }
        database.items = AssetDatabase.FindAssets("t:ItemDefinition")
            .Select(guid => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(item => item != null)
            .OrderBy(item => item.itemId, StringComparer.Ordinal)
            .ToList();
        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();
        return database;
    }

    // ── 외형 에셋 ───────────────────────────────────────────────

    sealed class InventoryArt
    {
        public Sprite slotFrame, slotHoverFrame, weaponSilhouette, armorSilhouette, accessorySilhouette;
    }

    /// <summary>칸 테두리와 장비 칸 실루엣 도트 그림. 이미 있으면 그대로 쓴다(직접 고친 그림 보존).</summary>
    static InventoryArt CreateArt()
    {
        Directory.CreateDirectory(ArtFolder);
        return new InventoryArt {
            slotFrame = LoadOrCreateSprite("InventorySlotFrame.png", DrawSlotFrame, 4),
            slotHoverFrame = LoadOrCreateSprite("InventorySlotHoverFrame.png", DrawSlotHoverFrame, 4),
            weaponSilhouette = LoadOrCreateSprite("WeaponSlotSilhouette.png", () => DrawPixelArt(WeaponPixels), 0),
            armorSilhouette = LoadOrCreateSprite("ArmorSlotSilhouette.png", () => DrawPixelArt(ArmorPixels), 0),
            accessorySilhouette = LoadOrCreateSprite("AccessorySlotSilhouette.png", () => DrawPixelArt(AccessoryPixels), 0),
        };
    }

    static Sprite LoadOrCreateSprite(string fileName, Func<Texture2D> draw, int border)
    {
        string path = ArtFolder + "/" + fileName;
        if (!File.Exists(path))
        {
            var texture = draw();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
        }
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.spriteBorder = new Vector4(border, border, border, border);
        importer.SaveAndReimport();
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) throw new InvalidOperationException("스프라이트를 불러오지 못했습니다: " + path);
        return sprite;
    }

    static Texture2D NewTexture(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(new Color32[size * size]); // 전부 투명
        return texture;
    }

    /// <summary>32×32 칸 테두리. 바깥 어두운 선 + 옥색 테 + 위왼쪽 밝은 선/아래오른쪽 그림자, 모서리는 깎는다.</summary>
    static Texture2D DrawSlotFrame()
    {
        const int size = 32;
        var texture = NewTexture(size);
        var outline = new Color(0.02f, 0.03f, 0.03f, 1f);
        var rim = new Color(0.27f, 0.34f, 0.32f, 1f);
        var light = new Color(0.38f, 0.48f, 0.45f, 1f);
        var shadow = new Color(0.11f, 0.14f, 0.14f, 1f);
        var fill = new Color(0.075f, 0.095f, 0.10f, 1f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                bool corner = (x == 0 || x == size - 1) && (y == 0 || y == size - 1);
                if (corner) continue;
                Color color = edge == 0 ? outline : edge == 1 ? rim :
                    edge == 2 ? (x == 2 || y == size - 3 ? light : shadow) : fill; // 텍스처 y 는 아래가 0
                texture.SetPixel(x, y, color);
            }
        texture.Apply();
        return texture;
    }

    /// <summary>마우스를 올린 칸에 겹치는 옥색 두 줄 테두리. 가운데는 비어 있다.</summary>
    static Texture2D DrawSlotHoverFrame()
    {
        const int size = 32;
        var texture = NewTexture(size);
        var jade = new Color(0.48f, 0.77f, 0.65f, 1f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                bool corner = (x == 0 || x == size - 1) && (y == 0 || y == size - 1);
                if (!corner && edge <= 1) texture.SetPixel(x, y, jade);
            }
        texture.Apply();
        return texture;
    }

    static Texture2D DrawPixelArt(string[] rows)
    {
        const int size = 16;
        var texture = NewTexture(size);
        for (int row = 0; row < size; row++)
            for (int x = 0; x < size; x++)
                if (rows[row][x] == '#') texture.SetPixel(x, size - 1 - row, Color.white);
        texture.Apply();
        return texture;
    }

    // 16×16 실루엣. 위쪽 줄이 그림의 위. 색은 Image 색으로 흐리게 입힌다.
    static readonly string[] WeaponPixels = {
        "................",
        ".............##.",
        "............###.",
        "...........###..",
        "..........###...",
        ".........###....",
        "........###.....",
        "..#....###......",
        "...#..###.......",
        "....####........",
        "...#.#..........",
        "..#...#.........",
        ".#..............",
        "##..............",
        "................",
        "................",
    };

    static readonly string[] ArmorPixels = {
        "................",
        "................",
        "...###....###...",
        "..#####..#####..",
        ".######..######.",
        ".##############.",
        ".##############.",
        "..############..",
        "...##########...",
        "...##########...",
        "...##########...",
        "...##########...",
        "...##########...",
        "....########....",
        "................",
        "................",
    };

    static readonly string[] AccessoryPixels = {
        "................",
        ".......##.......",
        "......####......",
        ".......##.......",
        "......####......",
        ".....##..##.....",
        "....##....##....",
        "...##......##...",
        "...#........#...",
        "...#........#...",
        "...##......##...",
        "....##....##....",
        ".....######.....",
        "................",
        "................",
        "................",
    };

    // ── 프리팹 계층 ─────────────────────────────────────────────

    static GameObject BuildHierarchy(InventoryArt art, ItemDatabase database, TMP_FontAsset bodyFont, TMP_FontAsset titleFont)
    {
        var root = new GameObject("InventoryCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 220; // 수련록·HUD(200) 위, 톱니바퀴(250)·ESC 메뉴(300) 아래
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1440, 900); // 수련록과 같은 기준 해상도
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var canvasRect = (RectTransform)root.transform;

        // 창: 화면 오른쪽 가운데. 왼쪽은 게임 화면이 그대로 보인다.
        var panel = CreateImage("InventoryPanel", root.transform, null, WindowColor);
        panel.raycastTarget = true; // 창 위 클릭이 뒤로 새지 않게
        AddEdge(panel.gameObject);
        var panelRect = panel.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(1f, 0.5f);
        panelRect.anchoredPosition = new Vector2(-56f, 0f);
        panelRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);

        float contentWidth = WindowWidth - Padding * 2;
        CreateText("TitleText", panelRect, "소지품", titleFont, 40, InkUiTheme.Ivory, TextAlignmentOptions.Left, Padding, 22, 240, 56);
        var closeButton = CreateCloseButton(panelRect, bodyFont);
        CreateRule("TitleDivider", panelRect, Padding, 90, contentWidth);

        // 장비 3칸
        CreateText("EquipmentSectionLabel", panelRect, "장비", bodyFont, 16, InkUiTheme.Muted, TextAlignmentOptions.Left, Padding, 104, 200, 24);
        float equipmentGap = (contentWidth - EquipmentSlotSize * 3) / 2f;
        var equipmentSlots = new[] {
            CreateEquipmentSlot("WeaponSlot", panelRect, ItemCategory.Weapon, art.weaponSilhouette, art, bodyFont, Padding, 136),
            CreateEquipmentSlot("ArmorSlot", panelRect, ItemCategory.Armor, art.armorSilhouette, art, bodyFont, Padding + EquipmentSlotSize + equipmentGap, 136),
            CreateEquipmentSlot("AccessorySlot", panelRect, ItemCategory.Accessory, art.accessorySilhouette, art, bodyFont, Padding + (EquipmentSlotSize + equipmentGap) * 2, 136),
        };
        CreateRule("BagDivider", panelRect, Padding, 276, contentWidth);

        // 가방 5×3
        CreateText("BagSectionLabel", panelRect, "가방", bodyFont, 16, InkUiTheme.Muted, TextAlignmentOptions.Left, Padding, 290, 200, 24);
        var usedSlotCountText = CreateText("UsedSlotCountText", panelRect, $"0 / {PlayerInventory.SlotCount}", bodyFont, 16, InkUiTheme.Muted,
            TextAlignmentOptions.Right, WindowWidth - Padding - 160, 290, 160, 24);
        var grid = CreateRect("BagSlotGrid", panelRect);
        Place(grid, Padding, 322, contentWidth, BagSlotSize * 3 + BagSlotSpacing * 2);
        var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(BagSlotSize, BagSlotSize);
        layout.spacing = new Vector2(BagSlotSpacing, BagSlotSpacing);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = BagColumns;

        var window = root.AddComponent<InventoryWindow>();
        var bagSlots = new InventorySlotView[PlayerInventory.SlotCount];
        for (int i = 0; i < bagSlots.Length; i++)
            bagSlots[i] = CreateBagSlot(window, grid, i, art, bodyFont);

        CreateText("HelpText", panelRect, "끌어서 칸을 옮길 수 있습니다     I  열기 / 닫기", bodyFont, 13, InkUiTheme.Muted,
            TextAlignmentOptions.Left, Padding, 614, contentWidth, 22);

        var tooltip = CreateTooltip(root.transform, canvasRect, bodyFont, titleFont);

        var dragIcon = CreateImage("DragIcon", root.transform, null, Color.white);
        dragIcon.preserveAspect = true;
        dragIcon.rectTransform.anchorMin = dragIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        dragIcon.rectTransform.sizeDelta = new Vector2(64, 64);
        dragIcon.gameObject.SetActive(false);

        window.Setup(database, panel.gameObject, bagSlots, equipmentSlots, usedSlotCountText, closeButton, tooltip, canvasRect, dragIcon);
        panel.gameObject.SetActive(false);
        return root;
    }

    static Button CreateCloseButton(RectTransform parent, TMP_FontAsset font)
    {
        var image = CreateImage("CloseButton", parent, null, Color.clear);
        image.raycastTarget = true;
        Place(image.rectTransform, WindowWidth - Padding - 112, 32, 112, 36);
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var label = CreateText("CloseButtonLabel", image.rectTransform, "닫기  ESC", font, 17, InkUiTheme.Ivory, TextAlignmentOptions.Right, 0, 0, 112, 36);
        // 글자색으로 눌림을 보여 준다(배경은 투명).
        button.targetGraphic = label;
        var colors = button.colors;
        colors.normalColor = new Color(0.8f, 0.8f, 0.78f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = InkUiTheme.Jade;
        colors.selectedColor = colors.normalColor;
        button.colors = colors;
        return button;
    }

    static EquipmentSlotView CreateEquipmentSlot(string name, RectTransform parent, ItemCategory category, Sprite silhouette,
        InventoryArt art, TMP_FontAsset font, float x, float y)
    {
        var frame = CreateImage(name, parent, art.slotFrame, Color.white);
        frame.type = Image.Type.Sliced;
        frame.pixelsPerUnitMultiplier = FramePixelScale;
        frame.raycastTarget = true;
        Place(frame.rectTransform, x, y, EquipmentSlotSize, EquipmentSlotSize);

        var silhouetteImage = CreateImage("EmptySlotSilhouette", frame.rectTransform, silhouette, SilhouetteColor);
        Center(silhouetteImage.rectTransform, 64);
        var icon = CreateImage("EquippedItemIcon", frame.rectTransform, null, Color.white);
        icon.preserveAspect = true;
        Center(icon.rectTransform, 64);
        icon.enabled = false;
        var hover = CreateHoverFrame(frame.rectTransform, art);
        var label = CreateText("SlotLabel", frame.rectTransform, ItemDefinition.CategoryLabel(category), font, 15, InkUiTheme.Muted,
            TextAlignmentOptions.Center, -10, EquipmentSlotSize + 6, EquipmentSlotSize + 20, 22);
        label.raycastTarget = false;

        var view = frame.gameObject.AddComponent<EquipmentSlotView>();
        view.Setup(category, silhouetteImage, icon, hover);
        return view;
    }

    static InventorySlotView CreateBagSlot(InventoryWindow window, RectTransform grid, int index, InventoryArt art, TMP_FontAsset font)
    {
        var frame = CreateImage($"BagSlot{index + 1:00}", grid, art.slotFrame, Color.white);
        frame.type = Image.Type.Sliced;
        frame.pixelsPerUnitMultiplier = FramePixelScale;
        frame.raycastTarget = true;

        var icon = CreateImage("ItemIcon", frame.rectTransform, null, Color.white);
        icon.preserveAspect = true;
        Center(icon.rectTransform, 60);
        icon.enabled = false;
        var initial = CreateText("ItemInitialText", frame.rectTransform, "", font, 30, InkUiTheme.Ivory, TextAlignmentOptions.Center, 0, 0, BagSlotSize, BagSlotSize);
        var hover = CreateHoverFrame(frame.rectTransform, art);

        var view = frame.gameObject.AddComponent<InventorySlotView>();
        view.Setup(window, index, icon, initial, hover);
        return view;
    }

    static GameObject CreateHoverFrame(RectTransform slot, InventoryArt art)
    {
        var hover = CreateImage("HoverFrame", slot, art.slotHoverFrame, Color.white);
        hover.type = Image.Type.Sliced;
        hover.pixelsPerUnitMultiplier = FramePixelScale;
        Stretch(hover.rectTransform);
        hover.gameObject.SetActive(false);
        return hover.gameObject;
    }

    static ItemTooltip CreateTooltip(Transform root, RectTransform canvasRect, TMP_FontAsset bodyFont, TMP_FontAsset titleFont)
    {
        var background = CreateImage("ItemTooltip", root, null, TooltipColor);
        AddEdge(background.gameObject);
        var rect = background.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(280, 0);
        var layout = background.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 14, 16);
        layout.spacing = 6;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = background.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var nameText = CreateText("ItemNameText", rect, "", titleFont, 24, InkUiTheme.Ivory, TextAlignmentOptions.Left, 0, 0, 0, 0);
        var categoryText = CreateText("ItemCategoryText", rect, "", bodyFont, 14, InkUiTheme.Jade, TextAlignmentOptions.Left, 0, 0, 0, 0);
        var rule = CreateImage("TooltipDivider", rect, null, RuleColor);
        rule.gameObject.AddComponent<LayoutElement>().preferredHeight = 1;
        var descriptionText = CreateText("ItemDescriptionText", rect, "", bodyFont, 15, InkUiTheme.Ivory, TextAlignmentOptions.TopLeft, 0, 0, 0, 0);
        descriptionText.lineSpacing = 6;
        var quantityText = CreateText("ItemQuantityText", rect, "", bodyFont, 13, InkUiTheme.Muted, TextAlignmentOptions.Left, 0, 0, 0, 0);

        var tooltip = background.gameObject.AddComponent<ItemTooltip>();
        tooltip.Setup(canvasRect, nameText, categoryText, descriptionText, quantityText);
        background.gameObject.SetActive(false);
        return tooltip;
    }

    // ── 공용 도우미 ─────────────────────────────────────────────

    static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var rect = CreateRect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static TextMeshProUGUI CreateText(string name, RectTransform parent, string value, TMP_FontAsset font, float size, Color color,
        TextAlignmentOptions alignment, float x, float y, float width, float height)
    {
        var rect = CreateRect(name, parent);
        Place(rect, x, y, width, height);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    static void CreateRule(string name, RectTransform parent, float x, float y, float width)
    {
        var rule = CreateImage(name, parent, null, RuleColor);
        Place(rule.rectTransform, x, y, width, 1);
    }

    static void AddEdge(GameObject target)
    {
        var edge = target.AddComponent<Outline>();
        edge.effectDistance = new Vector2(1f, -1f);
        edge.effectColor = EdgeColor;
    }

    /// <summary>부모 왼쪽 위 기준 좌표로 놓는다(y 는 아래로 증가).</summary>
    static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    static void Center(RectTransform rect, float size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(size, size);
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}

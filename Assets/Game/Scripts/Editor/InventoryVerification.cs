using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 인벤토리 데이터·프리팹·창 동작 검증(편집 모드). 저장은 임시 폴더로만 가고 씬은 저장하지 않는다.
/// 결과와 화면 캡처는 VerificationResults/Inventory 에 남는다. 에디터를 종료하지 않는다.
/// </summary>
public static class InventoryVerification
{
    static int checks;

    [MenuItem("Tools/AINPC/Inventory/Verify Inventory (Edit Mode)")]
    static void RunFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Debug.Log(Run());
    }

    public static string Run()
    {
        checks = 0;
        string output = Path.GetFullPath("VerificationResults/Inventory");
        string slot = Path.Combine(output, "slot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(slot);
        SaveSystem.VerificationDirectory = slot;
        string result;
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var items = Enumerable.Range(0, PlayerInventory.SlotCount + 1).Select(CreateItem).ToArray();
            var inventory = VerifyData(items);
            VerifyWindow(inventory, items, output);
            result = $"PASS {checks} checks";
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            result = $"FAIL after {checks} checks: {e.Message}";
        }
        finally
        {
            SaveSystem.VerificationDirectory = null;
            InventoryWindow.CloseOpenWindow();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "result.txt"), result);
        return "INVENTORY_VERIFY_" + result;
    }

    static ItemDefinition CreateItem(int index)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        item.itemId = $"verify_item_{index:00}";
        item.displayName = $"검증 물건 {index:00}";
        item.maxStack = 5;
        item.description = "인벤토리 검증용 아이템입니다. 설명이 길어지면 툴팁 높이가 늘어나야 합니다.";
        return item;
    }

    // ── 데이터 ──────────────────────────────────────────────────

    static PlayerInventory VerifyData(ItemDefinition[] items)
    {
        var player = new GameObject("Inventory verification player");
        Invoke(player.AddComponent<MoodSystem>(), "Awake");
        var stats = player.AddComponent<PlayerStats>();
        Invoke(stats, "Awake");
        var inventory = player.GetComponent<PlayerInventory>();
        Check(inventory != null && inventory.Items.Count == 0, "fresh verification slot starts with an empty bag");

        for (int i = 0; i < PlayerInventory.SlotCount; i++)
            Check(inventory.TryAdd(items[i]) && inventory.GetItemInSlot(i)?.itemId == items[i].itemId, $"item {i} fills slot {i}");
        Check(!inventory.TryAdd(items[PlayerInventory.SlotCount]) && inventory.Count(items[PlayerInventory.SlotCount].itemId) == 0,
            "a new kind is rejected when all 15 slots are used");
        Check(inventory.TryAdd(items[3], 2) && inventory.Count(items[3].itemId) == 3 && inventory.GetItemInSlot(3).itemId == items[3].itemId,
            "an existing kind still stacks when the bag is full");

        int changed = 0;
        inventory.Changed += () => changed++;
        Check(inventory.MoveItem(0, 4) && inventory.GetItemInSlot(4).itemId == items[0].itemId &&
              inventory.GetItemInSlot(0).itemId == items[4].itemId && changed == 1, "moving onto an occupied slot swaps the two items");
        Check(inventory.Remove(items[2].itemId) && inventory.GetItemInSlot(2) == null && changed == 2, "removing the last one frees its slot");
        Check(inventory.MoveItem(4, 2) && inventory.GetItemInSlot(2).itemId == items[0].itemId && inventory.GetItemInSlot(4) == null,
            "moving onto an empty slot leaves the source empty");
        Check(!inventory.MoveItem(4, 7) && !inventory.MoveItem(1, PlayerInventory.SlotCount) && !inventory.MoveItem(1, -1) &&
              !inventory.MoveItem(1, 1) && changed == 3, "invalid moves change nothing");
        Check(inventory.TryAdd(items[PlayerInventory.SlotCount]) && inventory.GetItemInSlot(4)?.itemId == items[PlayerInventory.SlotCount].itemId,
            "a new kind takes the first free slot");

        var reloaded = new GameObject("Inventory reload check").AddComponent<PlayerInventory>();
        reloaded.Load(SaveSystem.LoadPlayer());
        bool sameLayout = true;
        for (int i = 0; i < PlayerInventory.SlotCount; i++)
            sameLayout &= inventory.GetItemInSlot(i)?.itemId == reloaded.GetItemInSlot(i)?.itemId &&
                          inventory.GetItemInSlot(i)?.quantity == reloaded.GetItemInSlot(i)?.quantity;
        Check(sameLayout, "slot positions and quantities survive save and reload");

        // 칸 번호가 생기기 전 저장은 slotIndex 가 모두 0 으로 읽힌다.
        reloaded.Load(new PlayerSaveData { inventory = new[] {
            new InventorySaveEntry { itemId = "legacy_a", displayName = "A", quantity = 1 },
            new InventorySaveEntry { itemId = "legacy_b", displayName = "B", quantity = 2 },
            new InventorySaveEntry { itemId = "legacy_c", displayName = "C", quantity = 1 },
        } });
        Check(reloaded.GetItemInSlot(0)?.itemId == "legacy_a" && reloaded.GetItemInSlot(1)?.itemId == "legacy_b" &&
              reloaded.GetItemInSlot(2)?.itemId == "legacy_c", "legacy saves without slot numbers spread into the first slots");
        UnityEngine.Object.DestroyImmediate(reloaded.gameObject);

        Check(Throws(() => PlayerInventory.Validate(new[] { new InventorySaveEntry { itemId = "x", quantity = 1, slotIndex = PlayerInventory.SlotCount } })) &&
              Throws(() => PlayerInventory.Validate(new[] { new InventorySaveEntry { itemId = "x", quantity = 1, slotIndex = -2 } })) &&
              !Throws(() => PlayerInventory.Validate(new[] { new InventorySaveEntry { itemId = "x", quantity = 1, slotIndex = -1 } })),
            "save validation rejects out-of-range slot numbers");
        return inventory;
    }

    // ── 창 ─────────────────────────────────────────────────────

    static void VerifyWindow(PlayerInventory inventory, ItemDefinition[] items, string output)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(InventoryUiBuilder.PrefabPath);
        Check(prefab != null, "InventoryCanvas prefab exists");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var window = instance.GetComponent<InventoryWindow>();
        var panel = Field<GameObject>(window, "windowPanel");
        var bagSlots = Field<InventorySlotView[]>(window, "bagSlots");
        var equipmentSlots = Field<EquipmentSlotView[]>(window, "equipmentSlots");
        var tooltip = Field<ItemTooltip>(window, "itemTooltip");
        var dragIcon = Field<Image>(window, "dragIcon");
        var usedSlotCountText = Field<TMP_Text>(window, "usedSlotCountText");
        var database = Field<ItemDatabase>(window, "itemDatabase");

        Check(instance.GetComponentsInChildren<Transform>(true).All(t => t.name.All(c => c < 128)), "every UI object has an English name");
        Check(bagSlots.Length == 15 && bagSlots.Select((s, i) => s != null && s.SlotIndex == i && s.name == $"BagSlot{i + 1:00}").All(ok => ok),
            "15 bag slots are numbered in order");
        var grid = bagSlots[0].transform.parent.GetComponent<GridLayoutGroup>();
        Check(grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount && grid.constraintCount == 5, "bag grid is 5 columns × 3 rows");
        Check(equipmentSlots.Select(s => s.AcceptedCategory).SequenceEqual(new[] { ItemCategory.Weapon, ItemCategory.Armor, ItemCategory.Accessory }),
            "equipment has weapon, armor and accessory slots");
        Check(database != null && database.Find("cave_sword") != null, "item database contains project items");
        Check(instance.GetComponent<Canvas>().sortingOrder == 220 && instance.GetComponent<CanvasScaler>().referenceResolution == new Vector2(1440, 900),
            "canvas sorts above the HUD and below the pause menu");
        var texts = instance.GetComponentsInChildren<TMP_Text>(true);
        Check(texts.All(t => t.font != null), "all labels have a font");
        string staticLabels = string.Concat(texts.Select(t => t.text)) + "보유 수량설명이 없습니다.무기갑옷장신구기타" + items[0].description;
        foreach (var font in texts.Select(t => t.font).Distinct())
            Check(font.HasCharacters(staticLabels, out uint[] missing, false, true), $"{font.name} can draw every Korean label");

        // 아이콘 있는 아이템 하나는 실제 동굴 검 그림을 쓴다.
        var sword = database.Find("cave_sword");
        items[0].icon = sword.icon;
        database.items.Add(items[0]);
        try
        {
            Invoke(window, "Awake");
            Check(!panel.activeSelf && !InventoryWindow.IsOpen, "window starts closed");
            Check(window.Open() && InventoryWindow.IsOpen && panel.activeSelf, "Open shows the window and locks player input");
            var withIcon = bagSlots[2];
            var iconImage = withIcon.transform.Find("ItemIcon").GetComponent<Image>();
            Check(iconImage.enabled && iconImage.sprite == sword.icon, "slot shows the item icon from the database");
            var initial = bagSlots[1].transform.Find("ItemInitialText").GetComponent<TMP_Text>();
            Check(initial.text == items[1].displayName.Substring(0, 1), "item without an icon shows its first letter");
            Check(!bagSlots[2].transform.Find("HoverFrame").gameObject.activeSelf, "hover frame is hidden by default");
            Check(usedSlotCountText.text == "15 / 15", "used slot counter matches the bag");
            // 새 게임은 천갑옷·낡은 부적을 낀 채 시작하고, 무기는 동굴에서 줍기 전까지 비어 있다.
            Check(equipmentSlots[0].transform.Find("EmptySlotSilhouette").GetComponent<Image>().enabled &&
                  equipmentSlots.Skip(1).All(s => s.transform.Find("EquippedItemIcon").GetComponent<Image>().enabled),
                "empty weapon slot shows its silhouette, starter armor and talisman show icons");
            Capture(instance, output, "inventory-open.png", 1440, 900);

            window.OnSlotPointerEnter(bagSlots[2]);
            var tooltipTexts = tooltip.GetComponentsInChildren<TMP_Text>(true);
            Check(tooltip.gameObject.activeSelf && tooltipTexts.Any(t => t.text == items[0].displayName) &&
                  tooltipTexts.Any(t => t.text.Contains(items[0].description)) && tooltipTexts.Any(t => t.text == "보유 수량  1"),
                "hovering a slot shows name, description and quantity");
            Check(bagSlots[2].transform.Find("HoverFrame").gameObject.activeSelf, "hovered slot is outlined");
            // 툴팁 위치는 캔버스 크기가 정해져야 의미가 있으므로 해상도마다 실제로 배치한 뒤 확인한다.
            foreach (var (width, height, fileName) in new[] {
                (1440, 900, "inventory-tooltip.png"), (1280, 720, "inventory-tooltip-1280x720.png"), (1920, 1080, "inventory-tooltip-1920x1080.png") })
                Capture(instance, output, fileName, width, height, () =>
                {
                    window.OnSlotPointerEnter(bagSlots[2]);
                    VerifyTooltipPlacement(instance, tooltip, panel, $"{width}x{height}");
                });
            window.OnSlotPointerExit(bagSlots[2]);
            Capture(instance, output, "inventory-tooltip-bottom-left.png", 1440, 900, () =>
            {
                window.OnSlotPointerEnter(bagSlots[10]); // 아래 줄 왼쪽 끝 칸
                VerifyTooltipPlacement(instance, tooltip, panel, "bottom-left slot");
            });
            window.OnSlotPointerExit(bagSlots[10]);
            Check(!tooltip.gameObject.activeSelf, "leaving the slot hides the tooltip");

            var eventSystem = new GameObject("Verification EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            var pointer = new PointerEventData(eventSystem) { button = PointerEventData.InputButton.Left, position = new Vector2(400, 300) };
            window.BeginItemDrag(bagSlots[2], pointer);
            Check(dragIcon.gameObject.activeSelf && dragIcon.sprite == sword.icon && iconImage.color.a < 0.5f, "dragging lifts the icon and fades the source");
            window.DropItemOn(bagSlots[9]);
            window.EndItemDrag();
            Check(inventory.GetItemInSlot(9)?.itemId == items[0].itemId && inventory.GetItemInSlot(2)?.itemId == items[9].itemId,
                "dropping onto another slot swaps the items");
            Check(!dragIcon.gameObject.activeSelf && bagSlots[9].transform.Find("ItemIcon").GetComponent<Image>().sprite == sword.icon &&
                  iconImage.color.a == 1f, "the window redraws both slots after the drop");

            window.BeginItemDrag(bagSlots[9], pointer);
            window.EndItemDrag(); // 칸 밖에 놓음
            Check(inventory.GetItemInSlot(9)?.itemId == items[0].itemId && !dragIcon.gameObject.activeSelf, "dropping outside a slot cancels the drag");
            Check(inventory.Remove(items[5].itemId) && !bagSlots[5].transform.Find("ItemIcon").GetComponent<Image>().enabled &&
                  bagSlots[5].transform.Find("ItemInitialText").GetComponent<TMP_Text>().text == "" && usedSlotCountText.text == "14 / 15",
                "the open window follows inventory changes");
            window.BeginItemDrag(bagSlots[5], pointer);
            Check(!dragIcon.gameObject.activeSelf, "an empty slot cannot be dragged");

            window.BeginItemDrag(bagSlots[9], pointer);
            window.Close();
            Check(!InventoryWindow.IsOpen && !panel.activeSelf && !dragIcon.gameObject.activeSelf && InventoryWindow.LastClosedFrame == Time.frameCount,
                "closing hides the window, cancels the drag and releases input");
        }
        finally { database.items.Remove(items[0]); }
    }

    static void VerifyTooltipPlacement(GameObject canvas, ItemTooltip tooltip, GameObject panel, string label)
    {
        var canvasRect = (RectTransform)canvas.transform;
        Check(canvasRect.lossyScale.x > 0f && canvasRect.rect.width >= 1440f, $"canvas is laid out at {label}");
        Rect tooltipBounds = CanvasBounds(canvasRect, (RectTransform)tooltip.transform);
        Rect panelBounds = CanvasBounds(canvasRect, (RectTransform)panel.transform);
        Rect screen = canvasRect.rect;
        Check(tooltipBounds.height > 60f && tooltipBounds.xMin >= screen.xMin - 0.5f && tooltipBounds.xMax <= screen.xMax + 0.5f &&
              tooltipBounds.yMin >= screen.yMin - 0.5f && tooltipBounds.yMax <= screen.yMax + 0.5f, $"tooltip stays inside the screen at {label}");
        Check(tooltipBounds.xMax <= panelBounds.xMin + 0.5f, $"tooltip sits left of the window without covering slots at {label}");
    }

    static Rect CanvasBounds(RectTransform canvasRect, RectTransform target)
    {
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector2 min = canvasRect.InverseTransformPoint(corners[0]), max = canvasRect.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>
    /// 오버레이 캔버스를 잠시 카메라 캔버스로 바꿔 PNG 로 찍는다. 편집 모드의 오버레이 캔버스는 크기가 0 으로 남아 있을 수 있어
    /// 위치 검사도 <paramref name="whileLaidOut"/> 안에서 한다.
    /// </summary>
    static void Capture(GameObject canvasObject, string output, string fileName, int width, int height, Action whileLaidOut = null)
    {
        var canvas = canvasObject.GetComponent<Canvas>();
        var camera = new GameObject("Verification camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.27f, 0.31f, 0.22f);
        var texture = new RenderTexture(width, height, 24);
        camera.targetTexture = texture;
        try
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 5;
            // CanvasScaler 는 Update 에서 배율을 정하므로 편집 모드에서는 직접 한 번 돌린다.
            typeof(CanvasScaler).GetMethod("Handle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvasObject.GetComponent<CanvasScaler>(), null);
            Canvas.ForceUpdateCanvases();
            whileLaidOut?.Invoke();
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(output, fileName), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
        finally
        {
            RenderTexture.active = null;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }
    }

    // ── 도우미 ─────────────────────────────────────────────────

    static void Check(bool pass, string message)
    {
        if (!pass) throw new Exception(message);
        checks++;
        Debug.Log("INVENTORY_CHECK: " + message);
    }

    static bool Throws(Action action)
    {
        try { action(); return false; }
        catch { return true; }
    }

    static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, null);
}

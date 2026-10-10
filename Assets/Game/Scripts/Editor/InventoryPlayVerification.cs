using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Main 씬을 플레이해 가상 키보드·마우스로 인벤토리 창을 검증한다.
/// I/ESC 여닫기, 수련록·ESC 메뉴와의 겹침, 창이 열린 동안 이동 차단, 실제 마우스 드래그로 칸 옮기기.
/// 세이브는 임시 폴더로만 가고, 끝나면 플레이를 멈출 뿐 에디터는 그대로 둔다.
/// </summary>
[InitializeOnLoad]
public static class InventoryPlayVerification
{
    const string Pending = "InventoryPlay.Pending", Slot = "InventoryPlay.Slot";
    static Keyboard keyboard; static Mouse mouse;
    static PlayerStats stats; static PlayerInventory inventory; static InventoryWindow window;
    static int stage, checks; static double due; static Vector3 startPosition;
    static InputSettings.BackgroundBehavior oldBackground;
    static InputSettings.EditorInputBehaviorInPlayMode oldEditorInput;
    static bool oldRunInBackground;

    static InventoryPlayVerification()
    {
        EditorApplication.playModeStateChanged += Changed;
        // 도메인 리로드 뒤 씬 오브젝트가 Awake 에서 세이브를 읽기 전에 임시 폴더를 다시 건다.
        if (SessionState.GetBool(Pending, false)) SaveSystem.VerificationDirectory = SessionState.GetString(Slot, "");
    }

    [MenuItem("Tools/AINPC/Inventory/Verify Inventory (Play Mode)")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Play를 종료한 뒤 실행하세요.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string slot = Path.GetFullPath("VerificationResults/InventoryPlay/slot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(slot);
        SessionState.SetString(Slot, slot); SessionState.SetBool(Pending, true);
        SaveSystem.VerificationDirectory = slot;
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            oldRunInBackground = Application.runInBackground; Application.runInBackground = true;
            oldBackground = InputSystem.settings.backgroundBehavior; oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            mouse = InputSystem.AddDevice<Mouse>(); mouse.MakeCurrent();
            stage = checks = 0; due = EditorApplication.timeSinceStartup + 1.5;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Pending, false);
            SaveSystem.VerificationDirectory = null;
        }
    }

    static void Check(bool pass, string message)
    {
        if (!pass) throw new Exception(message);
        checks++;
        Debug.Log("INVENTORY_PLAY_CHECK: " + message);
    }

    static void Press(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); Wait(); }
    static void Wait(double seconds = 0.25) => due = EditorApplication.timeSinceStartup + seconds;

    static void PointAt(InventorySlotView slot, bool pressed) => PointAt(SlotScreenPoint(slot), pressed);

    static void PointAt(Vector2 screen, bool pressed)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = screen, buttons = (ushort)(pressed ? 1 : 0) });
        Wait(0.2);
    }

    static InventorySlotView[] BagSlots =>
        (InventorySlotView[])typeof(InventoryWindow).GetField("bagSlots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < due) return;
        try
        {
            switch (stage++)
            {
                case 0:
                    stats = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
                    window = UnityEngine.Object.FindFirstObjectByType<InventoryWindow>();
                    Check(stats != null && stats.IsAlive && window != null, "Main scene has a living player and an inventory window");
                    inventory = stats.GetComponent<PlayerInventory>();
                    Check(inventory.Items.Count == 0, "verification save slot starts empty");
                    var sword = AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Game/Items/CaveSword.asset");
                    var herb = ScriptableObject.CreateInstance<ItemDefinition>();
                    herb.itemId = "verify_herb"; herb.displayName = "검증 약초"; herb.maxStack = 9;
                    Check(inventory.TryAdd(sword) && inventory.TryAdd(herb, 3), "fixture items added");
                    Check(!InventoryWindow.IsOpen, "window is closed at start");
                    Press(Key.I);
                    break;
                case 1: Check(InventoryWindow.IsOpen, "I opens the inventory"); Press(); break;
                case 2: Press(Key.Escape); break;
                case 3:
                    Check(!InventoryWindow.IsOpen && !PauseMenuUI.IsOpen, "ESC closes the inventory without also opening the pause menu");
                    Press(); break;
                case 4: Press(Key.I); break;
                case 5: Check(InventoryWindow.IsOpen, "I reopens the inventory"); Press(); break;
                case 6: Press(Key.I); break;
                case 7: Check(!InventoryWindow.IsOpen, "pressing I again closes the inventory"); Press(); break;
                case 8: Press(Key.I); break;
                case 9: startPosition = stats.transform.position; Press(Key.W, Key.D); Wait(0.6); break;
                case 10:
                    Check((stats.transform.position - startPosition).sqrMagnitude < 0.0001f, "player cannot move while the inventory is open");
                    Press(); break;
                case 11: Press(Key.C); break;
                case 12: Check(RpgUI.IsOpen && !InventoryWindow.IsOpen, "opening the cultivation window closes the inventory"); Press(); break;
                case 13: Press(Key.I); break;
                case 14: Check(InventoryWindow.IsOpen && !RpgUI.IsOpen, "I switches from the cultivation window to the inventory"); Press(); break;
                case 15:
                    var swordSlot = BagSlots[0];
                    Check(inventory.GetItemInSlot(0)?.itemId == "cave_sword" && inventory.GetItemInSlot(1)?.itemId == "verify_herb", "fixture layout");
                    PointAt(swordSlot, false); break;
                case 16:
                    var tooltip = (ItemTooltip)typeof(InventoryWindow).GetField("itemTooltip", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                    Check(tooltip.gameObject.activeSelf, "real mouse hover shows the tooltip");
                    PointAt(BagSlots[0], true); break;
                case 17:
                    var middle = Vector2.Lerp(SlotScreenPoint(BagSlots[0]), SlotScreenPoint(BagSlots[7]), 0.5f);
                    PointAt(middle, true); break;
                case 18: PointAt(BagSlots[7], true); break;
                case 19: PointAt(BagSlots[7], false); break;
                case 20:
                    Check(inventory.GetItemInSlot(7)?.itemId == "cave_sword" && inventory.GetItemInSlot(0) == null,
                        "real mouse drag moves the sword to slot 8");
                    var saved = SaveSystem.LoadPlayer();
                    Check(Array.Exists(saved.inventory, e => e.itemId == "cave_sword" && e.slotIndex == 7), "moved slot is saved");
                    Check(stats.IsAlive && Time.timeScale == 1f, "game keeps running while the inventory is open");
                    Press(Key.Escape); break;
                case 21: Check(!InventoryWindow.IsOpen && !PauseMenuUI.IsOpen, "ESC closes the inventory again"); Press(); break;
                case 22: Press(Key.Escape); break;
                case 23: Check(PauseMenuUI.IsOpen, "ESC opens the pause menu once the inventory is closed"); Press(); break;
                case 24: Press(Key.I); break;
                case 25:
                    Check(!InventoryWindow.IsOpen, "I does nothing while the pause menu is open");
                    var menuButtons = (System.Collections.Generic.List<UnityEngine.UI.Button>)typeof(PauseMenuUI)
                        .GetField("menuButtons", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(UnityEngine.Object.FindFirstObjectByType<PauseMenuUI>());
                    menuButtons[1].onClick.Invoke();
                    Check(InventoryWindow.IsOpen && !PauseMenuUI.IsOpen && Time.timeScale == 1f, "the pause menu's inventory button opens the inventory window");
                    Press(Key.Escape); break;
                case 26: Check(!InventoryWindow.IsOpen, "inventory opened from the menu closes with ESC"); Press(); break;
                case 27: startPosition = stats.transform.position; Press(Key.W, Key.D); Wait(0.6); break;
                case 28:
                    // 대조군: 창이 닫혀 있으면 같은 입력으로 실제로 움직여야 위의 '이동 차단' 확인이 의미가 있다.
                    Check((stats.transform.position - startPosition).sqrMagnitude > 0.01f, "the same keys move the player once the inventory is closed");
                    Press();
                    Finish(true, ""); break;
            }
        }
        catch (Exception e) { Debug.LogException(e); Finish(false, e.Message); }
    }

    static Vector2 SlotScreenPoint(InventorySlotView slot)
    {
        var rect = (RectTransform)slot.transform;
        return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
    }

    static void Finish(bool ok, string message)
    {
        EditorApplication.update -= Tick;
        Time.timeScale = 1;
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        if (mouse != null) InputSystem.RemoveDevice(mouse);
        InputSystem.settings.backgroundBehavior = oldBackground;
        InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
        Application.runInBackground = oldRunInBackground;
        string output = Path.GetFullPath("VerificationResults/InventoryPlay");
        Directory.CreateDirectory(output);
        string result = (ok ? "PASS " : "FAIL ") + checks + " checks " + message;
        File.WriteAllText(Path.Combine(output, "result.txt"), result);
        Debug.Log("INVENTORY_PLAY_VERIFY_" + result);
        EditorApplication.ExitPlaymode();
    }
}

using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Isolated functional checks and actual Unity-rendered UI captures.</summary>
public static class RpgVerification
{
    static int checks;
    public static void Run()
    {
        checks = 0;
        string output = Path.GetFullPath("VerificationResults/RpgVerification");
        Directory.CreateDirectory(output);
        string saves = Path.Combine(output, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(saves);
        SaveSystem.VerificationDirectory = saves;
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var player = new GameObject("Verification player");
            var stats = player.AddComponent<PlayerStats>();
            Invoke(stats, "Awake");
            Check(stats.StatPoints == 5 && stats.SkillPoints == 3, "starting points");
            Check(stats.HP == 100 && stats.Mana == 50, "starting vitals");
            Check(!stats.LearnSkill(1), "prerequisite and level gate");
            Check(stats.UpgradeAttribute(RpgAttribute.Vitality) && stats.MaxHP == 120, "vitality investment");
            Check(stats.UpgradeAttribute(RpgAttribute.Strength) && stats.AttackBonus == 2, "attack investment");
            Check(stats.UpgradeAttribute(RpgAttribute.Defense) && stats.Defense == 2, "defense investment");
            Check(stats.UpgradeAttribute(RpgAttribute.Spirit) && stats.MaxMana == 60, "mana investment");
            Check(stats.UpgradeAttribute(RpgAttribute.Strength), "last point");
            Check(!stats.UpgradeAttribute(RpgAttribute.Strength) && stats.StatPoints == 0, "no overspending");
            Check(stats.LearnSkill(0) && stats.LearnSkill(3) && stats.LearnSkill(6), "learn three active skills");
            Check(!stats.LearnSkill(0) && stats.SkillPoints == 0, "no skill overspending");
            stats.TakeDamage(20);
            Check(stats.HP > 100 && stats.HP < 120, "defense reduces actual damage");
            Check(!stats.TrySpendMana(1000) && stats.Mana == 60, "insufficient mana is atomic");
            Check(stats.TrySpendMana(15) && stats.Mana == 45, "mana cost");
            stats.AddEXP(250);
            Check(stats.Level == 3 && stats.StatPoints == 6 && stats.SkillPoints == 2, "multiple level-ups grant points");
            Check(stats.LearnSkill(1), "unlocked second-tier skill");
            Check(stats.AttackBonus == 7, "passive modifies attack");
            stats.Save();
            var saved = SaveSystem.LoadPlayer();
            Check(saved.progressionVersion == 1 && saved.skillRanks[1] == 1 && saved.statPoints == 6, "save roundtrip");
            var second = new GameObject("Reload check").AddComponent<PlayerStats>();
            Invoke(second, "Awake");
            Check(second.MaxHP == stats.MaxHP && second.AttackBonus == stats.AttackBonus && second.SkillPoints == stats.SkillPoints, "reload preserves investments without doubling");
            UnityEngine.Object.DestroyImmediate(second.gameObject);

            string savePath = Path.Combine(saves, "player_save.json");
            File.WriteAllText(savePath, "{}");
            Check(SaveSystem.LoadPlayer()?.progressionVersion == 1, "invalid primary falls back to backup");
            var legacy = new PlayerSaveData { level = 3, hp = 140, maxHP = 140, mana = 70, maxMana = 70, maxEXP = 225, exp = 0 };
            File.WriteAllText(savePath, JsonUtility.ToJson(legacy));
            var migrated = new GameObject("Legacy migration check").AddComponent<PlayerStats>();
            Invoke(migrated, "Awake");
            Check(migrated.StatPoints == 11 && migrated.SkillPoints == 5 && migrated.MaxHP == 140, "legacy save migration grants earned points");
            migrated.Save();
            Invoke(migrated, "Load");
            Check(migrated.StatPoints == 11 && migrated.SkillPoints == 5, "migration runs only once");
            UnityEngine.Object.DestroyImmediate(migrated.gameObject);
            stats.Save();

            var ui = player.GetComponent<RpgUI>();
            var font = Resources.Load<TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF");
            Check(font != null && font.HasCharacters("귀환자의 수련록 체력 공격력 방어력 내력 무공 월영참 금강호신 운기조식 재도전", out uint[] missing, false, true), "NeoDunggeunmo Korean glyph coverage");
            typeof(RpgUI).GetField("koreanFont", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ui, font);
            Invoke(ui, "Start");
            var board = Resources.Load<Sprite>("RpgWooden/UI board Large Set");
            var paper = Resources.Load<Sprite>("RpgWooden/UI board Medium  parchment");
            var wood = Resources.Load<Sprite>("RpgWooden/TextBTN_Medium");
            Check(board != null && paper != null && wood != null, "wooden artwork resolves at runtime");
            Check(board.border.x > 0 && paper.border.x > 0 && wood.border.x > 0, "nine-slice borders configured");
            ui.Open(false);
            var window = GameObject.Find("Cultivation").GetComponent<Image>();
            Check(window.sprite == board && window.type == Image.Type.Sliced, "real package artwork applied to window");
            var buttons = (Button[])typeof(RpgUI).GetField("attributeButtons", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
            Check(Array.TrueForAll(buttons, b => b.image.sprite == wood && b.image.type == Image.Type.Sliced), "all attribute buttons use sliced wood");
            int beforePoints = stats.StatPoints;
            float beforeHP = stats.MaxHP;
            buttons[0].onClick.Invoke();
            Check(stats.StatPoints == beforePoints - 1 && stats.MaxHP == beforeHP + 20, "wooden button invokes actual upgrade");
            Check(SaveSystem.LoadPlayer().statPoints == stats.StatPoints, "wooden upgrade button persists result");
            Capture(output, "attributes.png");
            var tab = (Button)typeof(RpgUI).GetField("skillsTab", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
            tab.onClick.Invoke();
            Check(((GameObject)typeof(RpgUI).GetField("skillsPage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui)).activeSelf, "wooden skill tab switches page");
            Capture(output, "skills.png");
            Capture(output, "skills-1280x720.png", 1280, 720);
            Capture(output, "skills-1920x1080.png", 1920, 1080);
            Invoke(ui, "Close");
            Check(!RpgUI.IsOpen, "closing releases input lock");
            Capture(output, "hud.png");
            stats.TakeDamage(100000);
            Invoke(ui, "Refresh");
            Check(GameObject.Find("Recovery overlay") != null, "death recovery UI visible");
            Capture(output, "recovery.png");
            GameObject.Find("안전 지점에서 재도전  /  R").GetComponent<Button>().onClick.Invoke();
            Check(stats.IsAlive && !GameObject.Find("Recovery overlay"), "retry button restores player and dismisses UI");
            File.WriteAllText(Path.Combine(output, "result.txt"), $"PASS: {checks} assertions\nUnity-rendered captures: attributes.png, skills.png, hud.png\n");
            Debug.Log($"RPG_VERIFY_PASS: {checks} assertions. Output: {output}");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText(Path.Combine(output, "result.txt"), "FAIL: " + e);
            EditorApplication.Exit(1);
        }
        finally { SaveSystem.VerificationDirectory = null; }
    }

    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("RPG verification failed: " + name);
        checks++; Debug.Log("RPG_CHECK: " + name);
    }
    static void Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    static void Capture(string output, string name, int width = 1440, int height = 900)
    {
        var cameraObject = new GameObject("UI capture camera");
        var camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.14f, 0.19f, 0.19f); camera.orthographic = true; camera.orthographicSize = 5;
        camera.transform.position = new Vector3(0, 0, -10);
        var target = new RenderTexture(width, height, 24); target.Create(); camera.targetTexture = target;
        var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
        Canvas.ForceUpdateCanvases();
        foreach (var emblem in UnityEngine.Object.FindObjectsByType<RpgSkillEmblem>(FindObjectsSortMode.None))
        {
            emblem.SetAllDirty();
            emblem.Rebuild(CanvasUpdate.PreRender);
        }
        Canvas.ForceUpdateCanvases(); camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, name), image.EncodeToPNG());
        RenderTexture.active = previous;
        foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
        camera.targetTexture = null; target.Release();
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(cameraObject);
    }
}

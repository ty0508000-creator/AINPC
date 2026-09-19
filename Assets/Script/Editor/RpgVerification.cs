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
        string output = Path.GetFullPath("Temp/RpgVerification");
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
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Paperlogy-4Regular SDF.asset");
            typeof(RpgUI).GetField("koreanFont", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ui, font);
            Invoke(ui, "Start");
            ui.Open(false);
            Capture(output, "attributes.png");
            ui.Open(true);
            Capture(output, "skills.png");
            Invoke(ui, "Close");
            Check(!RpgUI.IsOpen, "closing releases input lock");
            Capture(output, "hud.png");
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
    static void Capture(string output, string name)
    {
        var cameraObject = new GameObject("UI capture camera");
        var camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.14f, 0.19f, 0.19f); camera.orthographic = true; camera.orthographicSize = 5;
        camera.transform.position = new Vector3(0, 0, -10);
        var target = new RenderTexture(1440, 900, 24); target.Create(); camera.targetTexture = target;
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
        var image = new Texture2D(1440, 900, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, name), image.EncodeToPNG());
        RenderTexture.active = previous;
        foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
        camera.targetTexture = null; target.Release();
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(cameraObject);
    }
}

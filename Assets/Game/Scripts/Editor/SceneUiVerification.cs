using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>저장/재로드 후에도 배치와 버튼 참조가 유지되는지 실제 Unity 직렬화로 검사한다.</summary>
public static class SceneUiVerification
{
    static object Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    static void Invoke(object owner, string method) => owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, null);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Debug.Log("SCENE_UI_CHECK: " + message); }

    public static void Run()
    {
        Directory.CreateDirectory("VerificationResults/SceneUI");
        SaveSystem.VerificationDirectory = Path.GetFullPath("VerificationResults/SceneUI/" + Guid.NewGuid().ToString("N"));
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var player = new GameObject("UI verification player");
            var stats = player.AddComponent<PlayerStats>();
            Invoke(stats, "Awake");
            SceneUiBaker.BakeCurrent();
            var ui = player.GetComponent<RpgUI>();
            var canvas = (GameObject)Field(ui, "canvasRoot");
            var hud = (RectTransform)canvas.transform.Find("Vitals");
            hud.anchoredPosition = new Vector2(55, -75);
            int count = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            SceneUiBaker.BakeCurrent();
            Check(count == UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, "baking twice does not duplicate UI");
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/SceneUiVerification.unity");
            EditorSceneManager.OpenScene("Assets/SceneUiVerification.unity");
            ui = UnityEngine.Object.FindFirstObjectByType<RpgUI>();
            stats = ui.GetComponent<PlayerStats>();
            Invoke(stats, "Awake");
            Invoke(ui, "Start");
            canvas = (GameObject)Field(ui, "canvasRoot");
            Check(((RectTransform)canvas.transform.Find("Vitals")).anchoredPosition == new Vector2(55, -75), "edited layout survives reload and initialization");
            Check(count == UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, "initialization creates no UI objects");
            ui.Open(false);
            int points = stats.StatPoints;
            ((Button[])Field(ui, "attributeButtons"))[0].onClick.Invoke();
            Check(stats.StatPoints == points - 1, "reloaded button spends exactly one point");
            stats.TakeDamage(100000);
            Invoke(ui, "Refresh");
            ((Button)Field(ui, "respawnButton")).onClick.Invoke();
            Check(stats.IsAlive && !((GameObject)Field(ui, "deathOverlay")).activeSelf, "reloaded recovery button works");
            var menu = UnityEngine.Object.FindFirstObjectByType<PauseMenuUI>();
            Invoke(menu, "Start");
            var buttons = (System.Collections.Generic.List<Button>)Field(menu, "menuButtons");
            for (int i = 0; i < 3; i++) { buttons[2].onClick.Invoke(); buttons[1].onClick.Invoke(); }
            Check(count == UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, "menu tabs reuse saved panels");
            File.WriteAllText("VerificationResults/SceneUI/result.txt", "PASS: scene serialization, idempotent baking, layout preservation, button callbacks, recovery and reusable menu panels\n");
        }
        finally { SaveSystem.VerificationDirectory = null; }
        RpgVerification.Run();
        RecoverySaveVerification.Run();
    }
}

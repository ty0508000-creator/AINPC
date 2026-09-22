using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>UI를 편집 가능한 씬 오브젝트로 배치한다. 기존 배치는 다시 만들지 않는다.</summary>
public static class SceneUiBaker
{
    public static void BakeAndVerify()
    {
        BakeAll();
        SceneUiVerification.Run();
    }
    [MenuItem("Tools/AINPC/Bake Scene UI")]
    public static void BakeCurrent()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Play를 종료한 뒤 실행하세요.");
        Bake(SceneManager.GetActiveScene());
    }

    public static void BakeAll()
    {
        foreach (string path in new[] { "Assets/Scenes/Main.unity", "Assets/Scenes/MapGen_Village.unity", "Assets/Scenes/Title.unity", "Assets/Scenes/mapstory1.unity" })
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Bake(scene);
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
    }

    static T[] InScene<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    static void Bake(Scene scene)
    {
        var before = scene.GetRootGameObjects();
        var root = before.FirstOrDefault(go => go.name == "UIRoot");
        if (root == null)
        {
            root = new GameObject("UIRoot"); SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "씬 UI 배치");
        }
        foreach (var player in InScene<PlayerStats>(scene))
        {
            var ui = player.GetComponent<RpgUI>();
            if (ui == null) ui = Undo.AddComponent<RpgUI>(player.gameObject);
            ui.BakeSceneUI();
            EditorUtility.SetDirty(ui);
        }
        foreach (var quest in InScene<QuestLogUI>(scene)) { quest.BakeSceneUI(); EditorUtility.SetDirty(quest); }
        foreach (var inner in InScene<InnerVoiceManager>(scene)) { inner.BakeSceneUI(); EditorUtility.SetDirty(inner); }
        foreach (var effect in InScene<TakeoverEffect>(scene)) { effect.BakeSceneUI(); EditorUtility.SetDirty(effect); }
        foreach (var title in InScene<TitleScreenUI>(scene)) { title.BakeSceneUI(); EditorUtility.SetDirty(title); }
        if (InScene<PlayerStats>(scene).Length > 0 && InScene<PauseMenuUI>(scene).Length == 0)
        {
            var menu = new GameObject("Pause Menu"); menu.transform.SetParent(root.transform, false);
            menu.AddComponent<PauseMenuUI>().BakeSceneUI();
        }
        if (InScene<SceneFlowView>(scene).Length == 0)
        {
            var temporary = new GameObject("UI authoring");
            temporary.AddComponent<GameFlow>().BakeSceneUI();
            foreach (Transform child in temporary.transform.Cast<Transform>().ToArray()) child.SetParent(root.transform, false);
            UnityEngine.Object.DestroyImmediate(temporary);
        }
        foreach (var go in scene.GetRootGameObjects())
            if (go != root && !before.Contains(go)) go.transform.SetParent(root.transform, false);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;
    }
}

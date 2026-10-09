using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class InkUiStyler
{
    [MenuItem("Tools/AINPC/Apply Ink UI Theme")]
    public static void ApplyCurrent()
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != "UIRoot") continue;
            Undo.RegisterFullObjectHierarchyUndo(root, "먹색 UI 적용");
            InkUiTheme.Apply(root);
        }
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void ApplyAllAndVerify()
    {
        foreach (string path in new[] { "Assets/Scenes/Main.unity", "Assets/Scenes/MapGen_Village.unity", "Assets/Scenes/Title.unity", "Assets/Scenes/mapstory1.unity" })
        {
            EditorSceneManager.OpenScene(path);
            ApplyCurrent();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }
        AssetDatabase.SaveAssets();
        RpgVerification.Run();
        EditorApplication.Exit(0);
    }
}

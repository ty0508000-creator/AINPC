using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class UltimateSetup
{
    public static void ApplyAndVerify()
    {
        CreateSword();
        foreach (string path in new[] { "Assets/Scenes/Main.unity", "Assets/Scenes/mapstory1.unity" })
        {
            var scene = EditorSceneManager.OpenScene(path);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var ui in root.GetComponentsInChildren<RpgUI>(true)) ui.BakeUltimateWidgets();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
        RpgVerification.Run(); RecoverySaveVerification.Run();
        RpgPlayVerification.Run();
    }

    static void CreateSword()
    {
        const string path = "Assets/Resources/UiArt/FlyingSword.png";
        if (File.Exists(path)) return;
        var texture = new Texture2D(9, 31, TextureFormat.RGBA32, false);
        texture.SetPixels(new Color[9 * 31]);
        for (int y = 8; y < 29; y++)
        {
            texture.SetPixel(3, y, new Color(0.3f, 0.53f, 0.7f));
            texture.SetPixel(4, y, new Color(0.87f, 0.97f, 1));
            texture.SetPixel(5, y, new Color(0.6f, 0.8f, 0.9f));
        }
        texture.SetPixel(4, 29, Color.white); texture.SetPixel(4, 30, new Color(0.8f, 0.93f, 1));
        var gold = new Color(0.86f, 0.68f, 0.3f);
        for (int x = 0; x < 9; x++) texture.SetPixel(x, 7, gold);
        for (int y = 1; y < 7; y++) texture.SetPixel(4, y, y % 2 == 0 ? gold : new Color(0.3f, 0.16f, 0.12f));
        texture.SetPixel(3, 0, gold); texture.SetPixel(4, 0, gold); texture.SetPixel(5, 0, gold);
        texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 32; importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
    }
}

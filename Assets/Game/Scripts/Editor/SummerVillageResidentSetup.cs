using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SummerVillageResidentSetup
{
    public const string Working = "Assets/CodexSummerWorking/Stage1.unity";
    const string Folder = "Assets/Art/Characters/SummerVillage/";
    static Sprite[] Import(string file, bool elder)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(Folder + file);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = elder ? 32 : 24; importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.mipmapEnabled = false;
        var slices = new SpriteMetaData[elder ? 16 : 12];
        for (int i = 0; i < slices.Length; i++)
        {
            int x = elder ? i % 8 : i % 4;
            int y = elder ? (i < 8 ? 48 : 0) : 60 - i / 4 * 30;
            slices[i] = new SpriteMetaData { name = "Idle_" + i.ToString("D2"), rect = new Rect(x * 30, y, 30, elder ? 48 : 30), alignment = 9, pivot = new Vector2(.5f, .08f) };
        }
#pragma warning disable CS0618
        importer.spritesheet = slices;
#pragma warning restore CS0618
        importer.SaveAndReimport();
        return AssetDatabase.LoadAllAssetsAtPath(Folder + file).OfType<Sprite>().OrderBy(s => s.name).ToArray();
    }
    public static string Prepare()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
        var active = SceneManager.GetActiveScene();
        if (active.isDirty) throw new InvalidOperationException("Unsaved scene changes; preserve these before preparing replacement.");
        var elder = Import("old_man_spritesheet.png", true); var merchant = Import("food_merchant_spritesheet.png", false);
        Directory.CreateDirectory("VerificationResults/SummerVillage");
        File.Copy(MapGenVillageSetup.StagePath, "VerificationResults/SummerVillage/Stage1.before.unity", true);
        Directory.CreateDirectory(Path.GetDirectoryName(Working)); File.Copy(MapGenVillageSetup.StagePath, Working, true);
        AssetDatabase.ImportAsset(Working, ImportAssetOptions.ForceSynchronousImport);
        var scene = EditorSceneManager.OpenScene(Working, OpenSceneMode.Additive);
        var residents = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<VillageResident>(true)).OrderBy(n => n.residentName).ToArray();
        if (residents.Length != 16) throw new Exception("Expected 16 existing residents.");
        for (int i = 0; i < residents.Length; i++)
        {
            var resident = residents[i]; bool isMerchant = i % 4 == 0;
            var frames = isMerchant ? merchant : elder;
            int direction = i % (isMerchant ? 3 : 4);
            resident.idleFrames = frames.Skip(direction * 4).Take(4).ToArray();
            resident.GetComponent<SpriteRenderer>().sprite = resident.idleFrames[0];
            resident.transform.localScale = Vector3.one * 1.2f;
        }
        EditorSceneManager.SaveScene(scene); EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(active);
        return "16 residents replaced: 12 elder / 4 merchant; four-frame idle animation, positions and dialogue preserved.";
    }
}

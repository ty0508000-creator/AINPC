using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class ManaSeedResidentSetup
{
    public const string Working = "Assets/CodexManaWorking/Stage1.unity";
    const string Folder = "Assets/Art/Characters/ManaSeedDemo/Page1/";
    static readonly Dictionary<string, Sprite[]> cache = new Dictionary<string, Sprite[]>();
    static Sprite Standing(string relative, int direction)
    {
        string path = Folder + relative;
        if (!cache.TryGetValue(path, out var sprites))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 24; importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.mipmapEnabled = false;
            var slices = new SpriteMetaData[4];
            for (int d = 0; d < 4; d++) slices[d] = new SpriteMetaData { name = "Stand_" + d, rect = new Rect(0, 448 - d * 64, 64, 64), alignment = 9, pivot = new Vector2(.5f, .3125f) };
#pragma warning disable CS0618
            importer.spritesheet = slices;
#pragma warning restore CS0618
            importer.SaveAndReimport();
            sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name).ToArray(); cache[path] = sprites;
        }
        return sprites[direction];
    }
    public static string Prepare()
    {
        var active = SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || active.isDirty) throw new InvalidOperationException("Stop Play and preserve unsaved scene first.");
        cache.Clear(); Directory.CreateDirectory("VerificationResults/ManaSeed");
        File.Copy(MapGenVillageSetup.StagePath, "VerificationResults/ManaSeed/Stage1.before.unity", true);
        Directory.CreateDirectory(Path.GetDirectoryName(Working)); File.Copy(MapGenVillageSetup.StagePath, Working, true);
        AssetDatabase.ImportAsset(Working, ImportAssetOptions.ForceSynchronousImport);
        var scene = EditorSceneManager.OpenScene(Working, OpenSceneMode.Additive);
        var ns = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<VillageResident>(true)).OrderBy(n => n.residentName).ToArray();
        if (ns.Length != 16 || ns.Any(n => n.GetComponent<ManaSeedResidentLook>() != null)) throw new Exception("Expected 16 residents without paper doll components.");
        for (int i = 0; i < ns.Length; i++)
        {
            var n = ns[i]; int direction = i % 4; string outfit = i % 2 == 0 ? "pfpn" : "fstr";
            var body = n.GetComponent<SpriteRenderer>(); body.sprite = Standing("char_a_p1_0bas_humn_v" + (i % 7 + 1).ToString("D2") + ".png", direction);
            n.idleFrames = new[] { body.sprite }; n.transform.localScale = Vector3.one * 1.2f;
            var layers = new List<SpriteRenderer>();
            AddLayer(n, "Outfit", Standing("1out/char_a_p1_1out_" + outfit + "_v" + (i % 5 + 1).ToString("D2") + ".png", direction), layers);
            AddLayer(n, "Hair", Standing("4har/char_a_p1_4har_" + (i % 2 == 0 ? "dap1" : "bob1") + "_v" + (i % 13 + 1).ToString("D2") + ".png", direction), layers);
            if (i % 3 == 0) AddLayer(n, "Hat", Standing("5hat/char_a_p1_5hat_" + (outfit == "pfpn" ? "pfht" : "pnty") + "_v" + (i % 5 + 1).ToString("D2") + ".png", direction), layers);
            var group = n.gameObject.AddComponent<SortingGroup>(); group.sortingOrder = body.sortingOrder;
            var look = n.gameObject.AddComponent<ManaSeedResidentLook>(); look.layers = layers.ToArray();
        }
        EditorSceneManager.SaveScene(scene); EditorSceneManager.CloseScene(scene,true); SceneManager.SetActiveScene(active);
        return "16 unique Mana Seed paper dolls; original residents and dialogue retained.";
    }
    static void AddLayer(VillageResident owner, string name, Sprite sprite, List<SpriteRenderer> list)
    {
        var go = new GameObject(name); go.transform.SetParent(owner.transform, false);
        var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = sprite;
        sr.sortingOrder = owner.GetComponent<SpriteRenderer>().sortingOrder + list.Count + 1;
        list.Add(sr);
    }
}

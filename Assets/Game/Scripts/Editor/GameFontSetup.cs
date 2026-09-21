using System;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>Creates the shared Korean font and migrates existing UI references without resaving scenes.</summary>
public static class GameFontSetup
{
    const string FontPath = "Assets/Resources/Fonts/NeoDunggeunmoPro SDF.asset";
    const string PreviousGuid = "be437cbfae4111a42a2fb0d8fe815d4f";

    [MenuItem("Tools/AINPC/Apply NeoDunggeunmo Font")]
    public static void Apply()
    {
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NeoDunggeunmoPro-Regular.ttf");
        if (source == null) throw new InvalidOperationException("NeoDunggeunmo source font is missing.");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Fonts"))
            AssetDatabase.CreateFolder("Assets/Resources", "Fonts");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null || font.material == null || font.atlasTextures[0] == null)
        {
            var created = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            created.name = "NeoDunggeunmoPro SDF";
            if (font == null) { font = created; AssetDatabase.CreateAsset(font, FontPath); }
            else
            {
                EditorUtility.CopySerialized(created, font);
                // TMP destroys owned material/atlas on destruction. Detach the transferred assets first.
                created.material = null;
                created.atlasTextures = Array.Empty<Texture2D>();
                UnityEngine.Object.DestroyImmediate(created);
            }
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var texture in font.atlasTextures) AssetDatabase.AddObjectToAsset(texture, font);
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
        }
        var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Paperlogy-4Regular SDF.asset");
        if (font.fallbackFontAssetTable == null)
            font.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
        if (fallback != null && !font.fallbackFontAssetTable.Contains(fallback))
            font.fallbackFontAssetTable.Add(fallback);
        const string sample = "귀환자의 수련록 체력 공격력 방어력 내력 무공 월영참 금강호신 운기조식 재도전 0123456789 ABC xyz";
        if (!font.TryAddCharacters(sample, out string missing))
            throw new InvalidOperationException("Missing font characters: " + missing);
        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();
        string guid = AssetDatabase.AssetPathToGUID(FontPath);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(font.material, out string materialGuid, out long materialId);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(fallback.material, out string oldMaterialGuid, out long oldMaterialId);
        int changed = 0;
        foreach (string root in new[] { "Assets/Scenes", "Assets/Prefabs", "Assets/TextMesh Pro/Resources" })
            foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(path);
                if (extension != ".unity" && extension != ".prefab" && extension != ".asset") continue;
                string content = File.ReadAllText(path);
                string updated = content.Replace(PreviousGuid, guid);
                updated = updated.Replace("fileID: " + oldMaterialId + ", guid: " + guid,
                    "fileID: " + materialId + ", guid: " + materialGuid);
                if (updated == content) continue;
                File.WriteAllText(path, updated, new UTF8Encoding(false));
                changed++;
            }
        AssetDatabase.Refresh();
        Debug.Log("GAME_FONT_APPLIED: " + guid + "; migrated files=" + changed);
    }

    public static void ApplyAndVerify()
    {
        Apply();
        RpgVerification.Run();
        AssetDatabase.SaveAssets();
    }
}

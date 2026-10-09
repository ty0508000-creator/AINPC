using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>승인한 시안을 편집 가능한 위젯으로 구성한다. 실행 중 UI를 만들지 않는다.</summary>
public static class IllustratedUiSetup
{
    const string Art = "Assets/Resources/UiArt/";
    const string Samurai = "Assets/Art/Characters/Samurai/";

    public static void Prepare()
    {
        FontAsset("Assets/Fonts/Paperlogy-4Regular.ttf", "UiBody SDF");
        FontAsset("Assets/Fonts/NotoSerifKR.ttf", "UiTitle SDF");
        var icons = LoadReadable(Art + "SkillIcons.png");
        for (int id = 0; id < 9; id++)
        {
            int col = id / 3, row = id % 3;
            int width = icons.width / 3, height = icons.height / 3;
            SaveCrop(icons, new RectInt(col * width, icons.height - (row + 1) * height, width, height), Art + "Skill" + id + ".png");
        }
        var idle = LoadReadable(Samurai + "Sprites/IDLE.png");
        // First frame's upper body, including the white hair and beard.
        SaveCrop(idle, new RectInt(36, 29, 28, 25), Art + "ElderPortrait.png");
        var attack = LoadReadable(Samurai + "Sprites/ATTACK 1.png");
        SaveCrop(attack, new RectInt(4 * 96, 0, 96, 96), Art + "ElderAttack.png");
        foreach (string name in new[] { "IDLE", "RUN", "ATTACK 1", "HURT" }) Slice(Samurai + "Sprites/" + name + ".png");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Samurai + "ElderSamurai.controller");
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(Samurai + "ElderSamurai.controller");
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dash", AnimatorControllerParameterType.Trigger);
            var machine = controller.layers[0].stateMachine;
            var stand = machine.AddState("Idle"); stand.motion = Clip("IDLE", true, 10);
            var run = machine.AddState("Run"); run.motion = Clip("RUN", true, 14);
            var slash = machine.AddState("Attack"); slash.motion = Clip("ATTACK 1", false, 16);
            machine.defaultState = stand;
            var toRun = stand.AddTransition(run); toRun.hasExitTime = false; toRun.duration = 0.08f;
            toRun.AddCondition(AnimatorConditionMode.Greater, 0.01f, "Speed");
            var toIdle = run.AddTransition(stand); toIdle.hasExitTime = false; toIdle.duration = 0.08f;
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.01f, "Speed");
            foreach (string trigger in new[] { "Attack", "Dash" })
            {
                var toSlash = machine.AddAnyStateTransition(slash); toSlash.hasExitTime = false; toSlash.duration = 0;
                toSlash.canTransitionToSelf = false; toSlash.AddCondition(AnimatorConditionMode.If, 0, trigger);
            }
            var done = slash.AddTransition(stand); done.hasExitTime = true; done.exitTime = 1; done.duration = 0;
        }
        string prefabPath = "Assets/Prefabs/Player.prefab";
        var player = PrefabUtility.LoadPrefabContents(prefabPath);
        player.GetComponent<SpriteRenderer>().sprite = Frames("IDLE")[0];
        player.GetComponent<Animator>().runtimeAnimatorController = controller;
        PrefabUtility.SaveAsPrefabAsset(player, prefabPath); PrefabUtility.UnloadPrefabContents(player);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/AINPC/Apply Illustrated RPG UI")]
    public static void ApplyCurrent()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Play를 종료한 뒤 실행하세요.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var ui in root.GetComponentsInChildren<RpgUI>(true))
            {
                ui.RebuildIllustratedUI(); EditorUtility.SetDirty(ui);
            }
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void ApplyAllAndVerify()
    {
        Prepare();
        foreach (string path in new[] { "Assets/Scenes/Main.unity", "Assets/Scenes/mapstory1.unity" })
        {
            EditorSceneManager.OpenScene(path);
            ApplyCurrent();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
        AssetDatabase.SaveAssets();
        RpgVerification.Run();
        RecoverySaveVerification.Run();
        EditorApplication.Exit(0);
    }

    static void FontAsset(string sourcePath, string name)
    {
        string path = "Assets/Resources/Fonts/" + name + ".asset";
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) != null) return;
        var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
        if (source == null) throw new InvalidOperationException("글꼴 누락: " + sourcePath);
        var font = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        font.name = name;
        font.TryAddCharacters("수련록 귀환자 경지 능력치 무공 소지품 월영참 검의 이치 검성의 경지 금강호신 철골 불굴 운기조식 기맥 순환 삼화취정 내력 재사용 습득하기 미개방 미습득 체력 방어력 공격력 포인트 강화 0123456789/", out string missing);
        if (!string.IsNullOrEmpty(missing)) throw new InvalidOperationException("글꼴 문자 누락: " + missing);
        AssetDatabase.CreateAsset(font, path);
        AssetDatabase.AddObjectToAsset(font.material, font);
        foreach (var texture in font.atlasTextures) AssetDatabase.AddObjectToAsset(texture, font);
    }

    static Texture2D LoadReadable(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.isReadable = true; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false; importer.maxTextureSize = 4096;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void SaveCrop(Texture2D source, RectInt region, string path)
    {
        var texture = new Texture2D(region.width, region.height, TextureFormat.RGBA32, false);
        texture.SetPixels(source.GetPixels(region.x, region.y, region.width, region.height)); texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    static void Slice(string path)
    {
        var texture = LoadReadable(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 55; importer.alphaIsTransparency = true;
        var frames = new SpriteMetaData[texture.width / 96];
        for (int i = 0; i < frames.Length; i++) frames[i] = new SpriteMetaData { name = "Frame_" + i.ToString("D2"), rect = new Rect(i * 96, 0, 96, 96), alignment = (int)SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f) };
#pragma warning disable 0618
        importer.spritesheet = frames;
#pragma warning restore 0618
        importer.SaveAndReimport();
    }

    static Sprite[] Frames(string name) => AssetDatabase.LoadAllAssetsAtPath(Samurai + "Sprites/" + name + ".png").OfType<Sprite>().OrderBy(sprite => sprite.name).ToArray();
    static AnimationClip Clip(string name, bool loop, float fps)
    {
        string path = Samurai + name.Replace(" ", "") + ".anim";
        var clip = new AnimationClip { frameRate = fps };
        var frames = Frames(name);
        var keys = frames.Select((sprite, i) => new ObjectReferenceKeyframe { time = i / fps, value = sprite }).ToList();
        keys.Add(new ObjectReferenceKeyframe { time = frames.Length / fps, value = loop ? frames[0] : frames[frames.Length - 1] });
        AnimationUtility.SetObjectReferenceCurve(clip, new EditorCurveBinding { path = "", type = typeof(SpriteRenderer), propertyName = "m_Sprite" }, keys.ToArray());
        var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings); AssetDatabase.CreateAsset(clip, path);
        return clip;
    }
}

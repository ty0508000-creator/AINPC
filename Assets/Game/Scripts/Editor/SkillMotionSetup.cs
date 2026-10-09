using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class SkillMotionSetup
{
    const string Folder = "Assets/Art/Characters/Samurai/";
    [MenuItem("Tools/AINPC/Connect Samurai Skill Motions")]
    public static void Apply()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "ElderSamurai.controller");
        if (controller == null) throw new System.InvalidOperationException("Samurai controller is missing");
        var idle = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
        Add(controller, "MoonSlash", AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "ATTACK1.anim"), idle, 1.2f);
        Add(controller, "GuardCast", CastingClip("GuardCast", new Color(1f, 0.84f, 0.5f), 0.55f), idle, 1);
        Add(controller, "RecoverCast", CastingClip("RecoverCast", new Color(0.5f, 1f, 0.78f), 0.9f), idle, 1);
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
    }
    static void Add(AnimatorController controller, string name, AnimationClip clip, AnimatorState idle, float speed)
    {
        if (!controller.parameters.Any(p => p.name == name)) controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
        var machine = controller.layers[0].stateMachine;
        var state = machine.states.FirstOrDefault(s => s.state.name == name).state;
        if (state == null)
        {
            state = machine.AddState(name);
            var enter = machine.AddAnyStateTransition(state); enter.duration = 0; enter.hasExitTime = false;
            enter.canTransitionToSelf = false; enter.AddCondition(AnimatorConditionMode.If, 0, name);
            var exit = state.AddTransition(idle); exit.hasExitTime = true; exit.exitTime = 1; exit.duration = 0;
        }
        state.motion = clip; state.speed = speed;
    }
    static AnimationClip CastingClip(string name, Color glow, float duration)
    {
        string path = Folder + name + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip != null) return clip;
        clip = new AnimationClip { name = name, frameRate = 12 };
        var sprites = AssetDatabase.LoadAllAssetsAtPath(Folder + "Sprites/IDLE.png").OfType<Sprite>().OrderBy(s => s.name).ToArray();
        AnimationUtility.SetObjectReferenceCurve(clip,
            new EditorCurveBinding { path = "", type = typeof(SpriteRenderer), propertyName = "m_Sprite" },
            sprites.Select((sprite, i) => new ObjectReferenceKeyframe { time = duration * i / (sprites.Length - 1), value = sprite }).ToArray());
        for (int i = 0; i < 4; i++)
            clip.SetCurve("", typeof(SpriteRenderer), "m_Color." + "rgba"[i],
                new AnimationCurve(new Keyframe(0, 1), new Keyframe(duration * 0.4f, glow[i]), new Keyframe(duration, 1)));
        var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings); AssetDatabase.CreateAsset(clip, path);
        return clip;
    }
    public static void ApplyAndVerify() { Apply(); RpgPlayVerification.Run(); }
}

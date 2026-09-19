using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class RpgPlayVerification
{
    const string Pending = "RpgPlayVerification.Pending";
    const string DirectoryKey = "RpgPlayVerification.Directory";
    static PlayerStats stats;
    static RpgSkillController skills;
    static Player_Attack attack;
    static double due;
    static int count;
    static bool running;

    static RpgPlayVerification()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        if (SessionState.GetBool(Pending, false)) SaveSystem.VerificationDirectory = SessionState.GetString(DirectoryKey, "");
    }

    public static void Run()
    {
        string directory = Path.GetFullPath("Temp/RpgPlayVerification/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        SessionState.SetString(DirectoryKey, directory); SessionState.SetBool(Pending, true);
        SaveSystem.VerificationDirectory = directory;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            var player = new GameObject("Combat verification player");
            var rb = player.AddComponent<Rigidbody2D>(); rb.gravityScale = 0;
            attack = player.AddComponent<Player_Attack>();
            Set(attack, "enemyLayer", (LayerMask)(1 << 8));
            stats = player.AddComponent<PlayerStats>(); skills = player.GetComponent<RpgSkillController>();
            Set(player.GetComponent<RpgUI>(), "koreanFont", AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/Fonts/Paperlogy-4Regular SDF.asset"));
            due = EditorApplication.timeSinceStartup + 0.75;
            running = true; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Pending, false); SaveSystem.VerificationDirectory = null;
            EditorApplication.Exit(SessionState.GetInt("RpgPlayVerification.Exit", 1));
        }
    }

    static void Tick()
    {
        if (!running || EditorApplication.timeSinceStartup < due) return;
        running = false; EditorApplication.update -= Tick;
        try
        {
            Check(stats.LearnSkill(0) && stats.LearnSkill(3) && stats.LearnSkill(6), "learn active skills");
            stats.TakeDamage(40);
            float hp = stats.HP, mana = stats.Mana;
            Check(skills.TryCast(6), "healing skill casts");
            Check(Mathf.Approximately(stats.HP, hp + 20), "healing changes real HP");
            Check(Mathf.Approximately(stats.Mana, mana - 20), "healing mana consumption");
            float remainingMana = stats.Mana;
            Check(!skills.TryCast(6) && stats.Mana == remainingMana, "cooldown rejects repeated cast without mana loss");
            Check(skills.Remaining(6) > 9, "cooldown timer starts");
            Check(skills.TryCast(3), "guard casts");
            hp = stats.HP; stats.TakeDamage(20);
            Check(hp - stats.HP < 20 && hp - stats.HP > 0, "guard mitigates actual damage");
            stats.RestoreMana(100);
            var data = ScriptableObject.CreateInstance<MonsterData>();
            Set(data, "maxHP", 100f); Set(data, "attackDamage", 0); Set(data, "expReward", 0f); Set(data, "aggroRange", 0f);
            var target = new GameObject("Two-collider target"); target.layer = 8; target.transform.position = new Vector3(1, 0, 0);
            target.AddComponent<BoxCollider2D>(); target.AddComponent<CircleCollider2D>();
            var enemy = target.AddComponent<MushroomMonster>(); target.GetComponent<Rigidbody2D>().gravityScale = 0;
            enemy.Initialize(data, null); Physics2D.SyncTransforms();
            Check(skills.TryCast(0), "offensive skill casts");
            float currentHP = (float)typeof(MonsterBase).GetField("currentHP", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(enemy);
            Check(Mathf.Approximately(currentHP, 82), "18 damage applied once across multiple colliders");
            remainingMana = stats.Mana;
            Check(!skills.TryCast(0) && stats.Mana == remainingMana, "attack cooldown prevents repeated damage");
            var ui = stats.GetComponent<RpgUI>(); ui.Open(true);
            Check(RpgUI.IsOpen && !skills.TryCast(3), "menu blocks gameplay skill input");
            typeof(RpgUI).GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
            Check(!RpgUI.IsOpen, "menu releases input");
            string output = Path.GetFullPath("Temp/RpgPlayVerification/result.txt");
            File.WriteAllText(output, $"PASS: {count} play-mode combat assertions\n");
            Debug.Log($"RPG_PLAY_VERIFY_PASS: {count}");
            SessionState.SetInt("RpgPlayVerification.Exit", 0);
        }
        catch (Exception e)
        {
            Debug.LogException(e); SessionState.SetInt("RpgPlayVerification.Exit", 1);
            File.WriteAllText(Path.GetFullPath("Temp/RpgPlayVerification/result.txt"), "FAIL: " + e);
        }
        EditorApplication.ExitPlaymode();
    }

    static void Check(bool result, string name) { if (!result) throw new Exception(name); count++; Debug.Log("RPG_PLAY_CHECK: " + name); }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
}

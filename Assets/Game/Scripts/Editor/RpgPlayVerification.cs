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
        count = 0;
        string directory = Path.GetFullPath("VerificationResults/RpgPlayVerification/" + Guid.NewGuid().ToString("N"));
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
            player.AddComponent<MoodSystem>();
            player.AddComponent<ControlManager>();
            player.AddComponent<AIController>();
            attack = player.AddComponent<Player_Attack>();
            Set(attack, "enemyLayer", (LayerMask)(1 << 8));
            stats = player.AddComponent<PlayerStats>(); skills = player.GetComponent<RpgSkillController>();
            Set(player.GetComponent<RpgUI>(), "koreanFont", Resources.Load<TMPro.TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF"));
            player.GetComponent<RpgUI>().BakeSceneUI();
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
            var isWithinSpawnRange = typeof(MonsterSpawnArea).GetMethod("IsWithinCameraSpawnRange", BindingFlags.Static | BindingFlags.NonPublic);
            Check((bool)isWithinSpawnRange.Invoke(null, new object[] { Vector3.zero, new Vector3(17f, 9f, 0f), 5f, 16f / 9f, 2f }),
                "spawn range includes two camera viewports");
            Check(!(bool)isWithinSpawnRange.Invoke(null, new object[] { Vector3.zero, new Vector3(18f, 0f, 0f), 5f, 16f / 9f, 2f }),
                "spawn range delays distant points");
            var lifecycleMood = stats.GetComponent<MoodSystem>();
            var lifecycleControl = stats.GetComponent<ControlManager>();
            var lifecycleAI = stats.GetComponent<AIController>();
            lifecycleAI.enabled = false;
            typeof(ControlManager).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(lifecycleControl, null);
            Set(lifecycleMood, "isAIControlled", true);
            typeof(ControlManager).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(lifecycleControl, null);
            Check(!lifecycleControl.IsPlayerControlled, "late control subscription synchronizes current mood state");
            lifecycleAI.enabled = true;
            var routine = typeof(AIController).GetField("behaviorCoroutine", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(routine.GetValue(lifecycleAI) != null, "reenabled AI synchronizes current control state");
            lifecycleAI.enabled = false;
            Check(routine.GetValue(lifecycleAI) == null, "disabled AI stops behavior coroutine");
            lifecycleMood.ReleaseControlForRecovery();
            lifecycleAI.enabled = true;
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
            stats.SetCheckpoint(new Vector3(9, 7, 0));
            var mood = stats.GetComponent<MoodSystem>();
            mood.ChangeMood(-9);
            float preservedMood = mood.Mood;
            var control = stats.GetComponent<ControlManager>();
            typeof(ControlManager).GetMethod("HandleAITakeover", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, null);
            Check(!control.IsPlayerControlled, "AI control enabled before lethal damage");
            int deaths = 0;
            stats.OnDied += () => deaths++;
            var ai = stats.GetComponent<AIController>();
            Set(ai, "enemyLayer", (LayerMask)(1 << 8));
            Set(ai, "recklessDamage", 100000);
            var reckless = (System.Collections.IEnumerator)typeof(AIController).GetMethod("RecklessLoop", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ai, null);
            reckless.MoveNext(); // first iteration waits; step the real damage path without waiting a second
            reckless.MoveNext();
            stats.TakeDamage(100000);
            Check(deaths == 1 && stats.State == PlayerStats.LifeState.Dead, "lethal damage during AI control emits one death");
            Check(stats.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero, "death immediately stops movement");
            Check(!skills.TryCast(6), "dead player cannot cast healing");
            float enemyHP = (float)typeof(MonsterBase).GetField("currentHP", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(enemy);
            attack.ForceAttack(Vector2.right); attack.ForceDash(Vector2.right, 1);
            Check(!attack.IsInvincible && (float)typeof(MonsterBase).GetField("currentHP", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(enemy) == enemyHP,
                "dead AI attack and dash APIs do nothing");
            Check(SaveSystem.LoadPlayer().hp == 0, "death persisted as recoverable state");
            Check(stats.Respawn() && control.IsPlayerControlled, "retry restores manual control");
            Check(stats.transform.position == new Vector3(9, 7, 0) && stats.HP == stats.MaxHP && stats.Mana == stats.MaxMana, "retry restores checkpoint and vitals");
            Check(mood.Mood == preservedMood, "retry preserves mood value");
            Check(skills.Remaining(0) == 0 && skills.Remaining(6) == 0 && skills.GuardRemaining == 0, "retry resets cooldowns and guard");
            stats.TakeDamage(100000);
            Check(stats.IsAlive && stats.HP == stats.MaxHP, "respawn protection blocks immediate damage");
            string output = Path.GetFullPath("VerificationResults/RpgPlayVerification/result.txt");
            File.WriteAllText(output, $"PASS: {count} play-mode combat assertions\n");
            Debug.Log($"RPG_PLAY_VERIFY_PASS: {count}");
            SessionState.SetInt("RpgPlayVerification.Exit", 0);
        }
        catch (Exception e)
        {
            Debug.LogException(e); SessionState.SetInt("RpgPlayVerification.Exit", 1);
            File.WriteAllText(Path.GetFullPath("VerificationResults/RpgPlayVerification/result.txt"), "FAIL: " + e);
        }
        EditorApplication.ExitPlaymode();
    }

    static void Check(bool result, string name) { if (!result) throw new Exception(name); count++; Debug.Log("RPG_PLAY_CHECK: " + name); }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
}

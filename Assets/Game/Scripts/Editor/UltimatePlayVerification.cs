using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class UltimatePlayVerification
{
    const string Pending = "UltimateVerification.Pending";
    const string SaveDirectory = "UltimateVerification.Saves";
    static PlayerStats stats;
    static RpgSkillController skills;
    static FlyingSwordUltimate ultimate;
    static MonsterBase target;
    static GameObject targetObject;
    static int stage, checks, hits;
    static float due, started;
    static UltimatePlayVerification()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        if (SessionState.GetBool(Pending, false)) SaveSystem.VerificationDirectory = SessionState.GetString(SaveDirectory, "");
    }
    public static void Run()
    {
        string directory = Path.GetFullPath("VerificationResults/Ultimate/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); SessionState.SetString(SaveDirectory, directory);
        SessionState.SetBool(Pending, true); SaveSystem.VerificationDirectory = directory;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); EditorApplication.EnterPlaymode();
    }
    static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            var player = new GameObject("Ultimate test player");
            player.AddComponent<SpriteRenderer>();
            player.AddComponent<Animator>().runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Art/Characters/Samurai/ElderSamurai.controller");
            player.AddComponent<Rigidbody2D>().gravityScale = 0;
            var attack = (player.GetComponent<Player_Attack>() ?? player.AddComponent<Player_Attack>()); Set(attack, "enemyLayer", (LayerMask)(1 << 8));
            stats = (player.GetComponent<PlayerStats>() ?? player.AddComponent<PlayerStats>()); skills = player.GetComponent<RpgSkillController>();
            player.GetComponent<RpgUI>().BakeSceneUI();
            checks = stage = 0; due = Time.time + 0.75f; EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Pending, false); SaveSystem.VerificationDirectory = null;
            EditorApplication.Exit(SessionState.GetInt("UltimateVerification.Exit", 1));
        }
    }
    static void Tick()
    {
        if (Time.time < due) return;
        try
        {
            switch (stage++)
            {
                case 0:
                    Check(!skills.TryCast(9), "unlearned ultimate is rejected");
                    stats.AddEXP(250); Check(stats.LearnSkill(0), "normal skill can be learned");
                    Check(!stats.LearnSkill(9), "ultimate rejects incomplete mastery");
                    for (int i = 0; i < 9; i++) stats.SkillRanks[i] = RpgSkillCatalog.All[i].MaxRank;
                    Check(stats.LearnSkill(9), "ultimate unlocks after mastery");
                    // Keep the combat fixture at base attack power for the existing flight damage assertion.
                    stats.SkillRanks[1] = stats.SkillRanks[2] = 0;
                    stats.RestoreMana(100); float mana = stats.Mana;
                    var button = (Button)typeof(RpgUI).GetField("ultimateButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stats.GetComponent<RpgUI>());
                    button.onClick.Invoke(); Check(!button.interactable && Mathf.Approximately(stats.Mana, mana), "HUD slot does not cast on click");
                    Check(skills.TryCast(9), "learned ultimate casts"); ultimate = stats.GetComponent<FlyingSwordUltimate>();
                    Check(ultimate != null && ultimate.Remaining > 7.9f && Mathf.Approximately(stats.Mana, mana - 40), "ultimate summons swords and consumes mana once");
                    Check(!skills.TryCast(9) && Mathf.Approximately(stats.Mana, mana - 40), "cooldown prevents duplicate summons and mana loss");
                    Check(ultimate.transform.Find("이기어검 · Floating swords").childCount == 5, "five swords are owned by the player");
                    started = Time.time; due = Time.time + 0.45f; break;
                case 1:
                    Check(ultimate.HitCount == 0 && ultimate.Remaining > 7, "swords wait when no targets exist");
                    typeof(RpgVerification).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                        new object[] { Path.GetFullPath("VerificationResults/Ultimate"), "floating-swords.png", 1440, 900 });
                    targetObject = new GameObject("Two-collider ultimate target"); targetObject.layer = 8; targetObject.transform.position = new Vector3(2, 0, 0);
                    targetObject.AddComponent<BoxCollider2D>(); targetObject.AddComponent<CircleCollider2D>();
                    target = targetObject.AddComponent<MushroomMonster>(); target.GetComponent<Rigidbody2D>().gravityScale = 0;
                    var data = ScriptableObject.CreateInstance<MonsterData>(); Set(data, "maxHP", 10000f); Set(data, "attackDamage", 0); Set(data, "aggroRange", 0f);
                    target.Initialize(data, null); Physics2D.SyncTransforms(); due = Time.time + 1.2f; break;
                case 2:
                    Check(ultimate.HitCount >= 10, "swords deliver rapid repeated attacks");
                    Check(Mathf.Approximately(HP(target), 10000 - ultimate.HitCount * 4), "each flight damages once despite multiple colliders");
                    hits = ultimate.HitCount; stats.GetComponent<RpgUI>().Open(true); due = Time.time + 0.25f; break;
                case 3:
                    Check(ultimate.HitCount == hits, "open UI suspends sword damage");
                    Invoke(stats.GetComponent<RpgUI>(), "Close"); due = Time.time + 0.4f; break;
                case 4:
                    Check(ultimate.HitCount > hits, "closing UI resumes rapid attacks");
                    UnityEngine.Object.Destroy(targetObject); due = Time.time + 0.2f; break;
                case 5:
                    Check(ultimate.Remaining > 0, "destroyed targets do not interrupt the ultimate");
                    due = started + 8.1f; break;
                case 6:
                    Check(ultimate.Remaining == 0 && skills.Remaining(9) > 30, "eight-second duration expires independently of cooldown");
                    skills.ResetForRecovery(); stats.RestoreMana(100); Check(skills.TryCast(9), "recovery resets ultimate cooldown");
                    skills.enabled = false; Check(ultimate.Remaining == 0, "disabling the controller removes swords"); skills.enabled = true;
                    skills.ResetForRecovery(); stats.RestoreMana(100); Check(skills.TryCast(9), "controller can cast again after reenable");
                    stats.TakeDamage(100000); Check(ultimate.Remaining == 0, "death cancels swords immediately");
                    Finish(true); break;
            }
        }
        catch (Exception error) { Debug.LogException(error); Finish(false); }
    }
    static float HP(MonsterBase monster) => (float)typeof(MonsterBase).GetField("currentHP", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(monster);
    static void Check(bool pass, string name) { if (!pass) throw new Exception(name); checks++; Debug.Log("ULTIMATE_CHECK: " + name); }
    static void Invoke(object value, string method) => value.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, null);
    static void Set(object value, string field, object setting) => value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, setting);
    static void Finish(bool pass)
    {
        EditorApplication.update -= Tick;
        string result = (pass ? "PASS: " : "FAIL: ") + checks + " ultimate checks";
        File.WriteAllText(Path.GetFullPath("VerificationResults/Ultimate/result.txt"), result);
        Debug.Log("ULTIMATE_VERIFY_" + result); SessionState.SetInt("UltimateVerification.Exit", pass ? 0 : 1); EditorApplication.ExitPlaymode();
    }
}

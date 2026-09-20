using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Fault-injection tests. Never writes the real player slot or changes a saved scene.</summary>
public static class RecoverySaveVerification
{
    static int count;
    public static void Run()
    {
        string output = Path.GetFullPath("VerificationResults/RecoverySaveVerification");
        string slot = Path.Combine(output, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(slot);
        SaveSystem.VerificationDirectory = slot;
        count = 0;
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (string scriptPath in new[] {
                "Assets/Game/Scripts/Runtime/Player/Player_Attack.cs",
                "Assets/Game/Scripts/Runtime/Player/Player_Controller.cs",
                "Assets/Game/Scripts/Runtime/Dialogue/NPCInteraction.cs" })
                Check(AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath)?.GetClass() != null,
                    "renamed MonoBehaviour resolves: " + scriptPath);
            var player = new GameObject("Recovery verification player");
            var mood = player.AddComponent<MoodSystem>();
            Invoke(mood, "Awake");
            player.transform.position = new Vector3(4, 5, 0);
            var stats = player.AddComponent<PlayerStats>();
            Invoke(stats, "Awake");
            int deaths = 0;
            stats.OnDied += () => deaths++;
            Check(stats.SetCheckpoint(new Vector3(12, 8, 0)), "checkpoint accepted while alive");
            stats.UpgradeAttribute(RpgAttribute.Strength);
            stats.LearnSkill(0);
            int statPoints = stats.StatPoints;
            stats.TakeDamage(10000);
            stats.TakeDamage(10000);
            Check(stats.State == PlayerStats.LifeState.Dead && deaths == 1, "one death event for repeated damage");
            Check(!stats.TrySpendMana(1) && !stats.UpgradeAttribute(RpgAttribute.Vitality), "dead player cannot cast or invest");
            Check(!stats.SetCheckpoint(Vector3.zero), "dead player cannot overwrite checkpoint");
            stats.AddEXP(100);
            Check(stats.HP == 0 && stats.State == PlayerStats.LifeState.Dead, "posthumous level-up cannot revive player");
            Invoke(stats, "Load");
            Check(stats.State == PlayerStats.LifeState.Dead && stats.CheckpointPosition == new Vector3(12, 8, 0), "dead save reload retains retry state and checkpoint");
            Check(stats.Respawn(), "retry succeeds");
            Check(stats.IsAlive && stats.HP == stats.MaxHP && stats.Mana == stats.MaxMana, "retry restores vitals");
            Check(stats.transform.position == new Vector3(12, 8, 0), "retry teleports to checkpoint");
            Check(stats.StatPoints == statPoints + 3 && stats.SkillRanks[0] == 1, "retry preserves growth");
            Check(!stats.Respawn(), "retry is not repeatable while alive");
            stats.TakeDamage(10000);
            Check(stats.HP == stats.MaxHP, "recovery grants immediate protection");

            var manager = new GameObject("Quest verification").AddComponent<QuestManager>();
            Invoke(manager, "Awake");
            // Only synthetic assets participate; no story side effects or LLM is needed.
            var table = (System.Collections.Generic.Dictionary<string, QuestRuntime>)typeof(QuestManager)
                .GetField("table", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            table.Clear();
            var quest = ScriptableObject.CreateInstance<QuestData>();
            quest.questId = "verification_reward";
            quest.objectives = new[] { new QuestObjective { type = ObjectiveType.Kill, targetId = "test", requiredCount = 1 } };
            quest.onComplete.expReward = 50;
            quest.onComplete.moodDelta = 7;
            table.Add(quest.questId, new QuestRuntime(quest));
            Invoke(manager, "Start");
            Check(manager.IsSaveReady && manager.StartQuest(quest.questId), "quest starts with unified save ready");
            float beforeEXP = stats.EXP;
            float beforeMood = mood.Mood;
            string path = Path.Combine(slot, "player_save.json");
            string committed = File.ReadAllText(path);
            SaveSystem.VerificationBeforeCommit = () => throw new IOException("Injected interruption before atomic replace");
            manager.CompleteQuest(quest.questId);
            Check(File.ReadAllText(path) == committed, "failed commit preserves entire previous snapshot");
            var old = SaveSystem.LoadPlayer();
            Check(old.exp == beforeEXP && old.mood == beforeMood && old.quests.entries.Single(e => e.questId == quest.questId).state == QuestState.Active,
                "interrupted reward has neither XP nor terminal marker on disk");
            manager.CompleteQuest(quest.questId);
            Check(stats.EXP == beforeEXP + 50, "repeat completion cannot double live reward");
            SaveSystem.VerificationBeforeCommit = null;
            Invoke(stats, "Load");
            Invoke(mood, "Awake");
            QuestSaveSystem.Load(manager);
            Check(stats.EXP == beforeEXP && manager.GetState(quest.questId) == QuestState.Active, "restart returns to same pre-reward snapshot");
            manager.CompleteQuest(quest.questId);
            var saved = SaveSystem.LoadPlayer();
            Check(saved.exp == beforeEXP + 50 && saved.quests.entries.Single(e => e.questId == quest.questId).state == QuestState.Completed,
                "XP and terminal claim committed together");
            Check(saved.hasMood && saved.mood == mood.Mood && saved.mood == beforeMood + 7, "quest mood is part of the same committed snapshot");
            Invoke(stats, "Load"); QuestSaveSystem.Load(manager);
            manager.CompleteQuest(quest.questId);
            Check(stats.EXP == beforeEXP + 50, "reload then duplicate completion pays once");
            stats.Save(); // ensure backup also has the completed state
            File.Delete(path); // isolated GUID test slot only
            Check(SaveSystem.LoadPlayer().exp == stats.EXP, "missing primary recovers backup");
            stats.Save();
            File.WriteAllText(path, "{}");
            Check(SaveSystem.LoadPlayer().quests.entries.Single(e => e.questId == quest.questId).state == QuestState.Completed,
                "corrupt primary recovers matching player and quest backup");
            Check(SaveSystem.SaveGame(stats, manager), "recovery can repair corrupt primary");
            Check(JsonUtility.FromJson<PlayerSaveData>(File.ReadAllText(path + ".bak")).level > 0,
                "repair preserves healthy backup instead of replacing it with corruption");

            // Available auto-start state must resume if the process ended between commit and callbacks.
            var followup = ScriptableObject.CreateInstance<QuestData>();
            followup.questId = "verification_followup"; followup.autoStart = true;
            followup.prerequisiteQuestIds = new[] { quest.questId };
            followup.objectives = new[] { new QuestObjective { type = ObjectiveType.Kill, targetId = "followup", requiredCount = 1 } };
            table.Add(followup.questId, new QuestRuntime(followup) { State = QuestState.Available });
            stats.Save();
            table[followup.questId].State = QuestState.Locked;
            Invoke(manager, "Start");
            Check(manager.GetState(followup.questId) == QuestState.Active, "restart resumes committed auto-start followup");
            followup.onFail.expReward = 500;
            followup.onFail.moodDelta = -3;
            manager.FailQuest(followup.questId);
            var failed = SaveSystem.LoadPlayer();
            Check(failed.level == stats.Level && failed.exp == stats.EXP && failed.quests.entries.Single(e => e.questId == followup.questId).state == QuestState.Failed,
                "multi-level failure reward and terminal state commit together");
            float failedEXP = stats.EXP; int failedLevel = stats.Level;
            manager.DeclineQuest(followup.questId); manager.FailQuest(followup.questId);
            Check(stats.EXP == failedEXP && stats.Level == failedLevel, "failure and decline cannot reward the same quest again");

            var future = SaveSystem.LoadPlayer(); future.snapshotVersion = 99;
            string futureJson = JsonUtility.ToJson(future);
            File.WriteAllText(path, futureJson);
            Check(!QuestSaveSystem.Load(manager), "future snapshot blocks quest fallback to new game");
            Check(!SaveSystem.SaveGame(stats, manager) && File.ReadAllText(path) == futureJson, "future schema cannot be overwritten using older backup");

            // Old split-save migration in a second isolated slot.
            string legacySlot = Path.Combine(output, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(legacySlot);
            SaveSystem.VerificationDirectory = legacySlot;
            var legacy = new PlayerSaveData { level = 2, hp = 0, maxHP = 120, mana = 30, maxMana = 60, exp = 12, maxEXP = 150 };
            File.WriteAllText(Path.Combine(legacySlot, "player_save.json"), JsonUtility.ToJson(legacy));
            var legacyQuests = new QuestSaveData();
            legacyQuests.entries.Add(new QuestSaveEntry { questId = quest.questId, state = QuestState.Completed, progress = new[] { 1 } });
            string legacyQuestJson = JsonUtility.ToJson(legacyQuests);
            File.WriteAllText(Path.Combine(legacySlot, "quest_save.json"), legacyQuestJson);
            Invoke(stats, "Load"); QuestSaveSystem.Load(manager);
            Check(stats.State == PlayerStats.LifeState.Dead && stats.EXP == 12, "legacy dead save remains recoverable without losing growth");
            Check(stats.Respawn(), "legacy dead save retry succeeds");
            Check(SaveSystem.LoadPlayer().snapshotVersion == 1 && SaveSystem.LoadPlayer().quests.entries[0].state == QuestState.Completed,
                "legacy player and quest migrate together");
            Check(File.ReadAllText(Path.Combine(legacySlot, "quest_save.json")) == legacyQuestJson, "migration preserves legacy quest source");
            // Once unified, stale legacy file cannot override the authoritative snapshot.
            File.WriteAllText(Path.Combine(legacySlot, "quest_save.json"), "{}");
            Check(QuestSaveSystem.Load(manager) && manager.IsCompleted(quest.questId), "unified snapshot ignores stale legacy file");

            File.WriteAllText(Path.Combine(legacySlot, "player_save.json"), "{}");
            File.WriteAllText(Path.Combine(legacySlot, "player_save.json.bak"), "{}");
            Check(!SaveSystem.SaveGame(stats, manager), "unrecoverable slot refuses destructive overwrite");
            Check(!SaveSystem.TryLoadPlayer(out _) && !QuestSaveSystem.Load(manager), "corruption is not a new game");
            var otherIsland = new QuestSaveData();
            otherIsland.entries.Add(new QuestSaveEntry { questId = "other_island", state = QuestState.Completed, progress = new[] { 1 } });
            var merged = QuestSaveSystem.Capture(manager, otherIsland);
            Check(merged.entries.Any(e => e.questId == "other_island" && e.state == QuestState.Completed), "partial catalog preserves other island progress");
            merged.entries.Single(e => e.questId == "other_island").progress[0] = 2;
            Check(otherIsland.entries[0].progress[0] == 1, "merged snapshot owns independent progress arrays");
            Check(File.ReadAllText(Path.Combine(legacySlot, "player_save.json")) == "{}", "damaged source retained for manual recovery");
            File.WriteAllText(Path.Combine(output, "result.txt"), $"PASS: {count} recovery/save assertions\n");
            Debug.Log($"RECOVERY_SAVE_VERIFY_PASS: {count}");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText(Path.Combine(output, "result.txt"), "FAIL: " + e);
            EditorApplication.Exit(1);
        }
        finally { SaveSystem.VerificationBeforeCommit = null; SaveSystem.VerificationDirectory = null; }
    }
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++; Debug.Log("RECOVERY_SAVE_CHECK: " + name);
    }
    static void Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
}

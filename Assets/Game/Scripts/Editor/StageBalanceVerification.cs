using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stage2~4 사냥터 몬스터 12종과 지역·최종 보스 4종을 실제 전투로 재는 플레이 모드 검증.
/// 가상 플레이어는 그 시점의 레벨(지역 시작 레벨 + 사냥터 번호)·능력치 배분·무공과
/// 이전 지역 장비(무기 Epic, 갑옷 Rare = 설계의 Rare~Epic 중간)를 갖추고, 예고를 본 뒤 0.25초 늦게 피한다.
/// 기준: 사냥터 몬스터는 14초 안·체력 절반 이하 피해(1지역과 같은 기준), 보스는 2~2.5분 안에 살아서 처치.
/// 결과: VerificationResults/StageBalance/combat.csv, result.txt. 배치 모드에서 돌리며 끝나면 에디터를 끈다.
/// </summary>
[InitializeOnLoad]
public static class StageBalanceVerification
{
    const string Pending = "StageBalance.Pending", Output = "StageBalance.Output", ExitKey = "StageBalance.Exit";
    public const float BossMinSeconds = 120f, BossMaxSeconds = 150f, HuntMaxSeconds = 14f;

    struct Case { public int stage, hunt; public bool boss; }
    static readonly List<Case> Cases = new();
    static int index, checks;
    static readonly List<string> rows = new();
    static PlayerStats player;
    static Rigidbody2D body;
    static Player_Attack attack;
    static RpgSkillController skills;
    static RegionalMonster enemy;
    static MonsterData data;
    static float start, ready, warningAt = -1, evadeUntil, damage, previousHP, reach;
    static double deadline;
    static readonly System.Random rng = new(2026);

    static StageBalanceVerification()
    {
        for (int stage = 2; stage <= 4; stage++)
        {
            for (int hunt = 1; hunt <= 4; hunt++) Cases.Add(new Case { stage = stage, hunt = hunt });
            Cases.Add(new Case { stage = stage, boss = true });
        }
        Cases.Add(new Case { stage = 5, boss = true });
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetBool(Pending, false)) SaveSystem.VerificationDirectory = SessionState.GetString(Output, "") + "/Save";
    }

    public static void Run()
    {
        string output = Path.GetFullPath("VerificationResults/StageBalance");
        Directory.CreateDirectory(output);
        SessionState.SetString(Output, output);
        SessionState.SetBool(Pending, true);
        SaveSystem.VerificationDirectory = output + "/Save";
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SaveSystem.VerificationDirectory = SessionState.GetString(Output, "") + "/Save";
            index = checks = 0;
            rows.Clear();
            rows.Add("stage,case,level,attackPower,maxHP,enemyHP,enemyDamage,seconds,damageTaken,result");
            deadline = EditorApplication.timeSinceStartup + 1200;
            new GameObject("Verification audio listener").AddComponent<AudioListener>();
            Time.timeScale = 4;
            Next();
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Pending, false);
            SaveSystem.VerificationDirectory = null;
            Time.timeScale = 1;
            EditorApplication.Exit(SessionState.GetInt(ExitKey, 1));
        }
    }

    /// <summary>그 시점 레벨. 사냥터는 지역 시작 레벨 + 번호 - 1, 보스는 지역 마지막 레벨, 최종보스는 17.</summary>
    public static int LevelFor(int stage, int hunt, bool boss) => boss ? Mathf.Min(4 * stage, 17) : 4 * (stage - 1) + hunt;

    static void Next()
    {
        if (player != null) UnityEngine.Object.DestroyImmediate(player.gameObject);
        if (enemy != null) UnityEngine.Object.DestroyImmediate(enemy.gameObject);
        foreach (var projectile in UnityEngine.Object.FindObjectsByType<RegionalProjectile>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(projectile.gameObject);
        var c = Cases[index];
        SaveSystem.VerificationDirectory = SessionState.GetString(Output, "") + "/Save/case-" + index + "-" + Guid.NewGuid().ToString("N");

        var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
        go.GetComponent<Player_Controller>().enabled = false;
        go.GetComponent<UnityEngine.InputSystem.PlayerInput>().enabled = false;
        go.GetComponent<RpgUI>().enabled = false;
        go.transform.position = Vector3.zero;
        body = go.GetComponent<Rigidbody2D>();
        attack = go.GetComponent<Player_Attack>();
        player = go.GetComponent<PlayerStats>();
        skills = go.GetComponent<RpgSkillController>();
        Build(c);

        var bossSpec = StageBuilder.Bosses.FirstOrDefault(b => b.stage == c.stage);
        string prefab = c.boss ? StageBuilder.BossPrefabPath(bossSpec) : StageBuilder.PrefabPath(c.stage, c.hunt);
        data = AssetDatabase.LoadAssetAtPath<MonsterData>(c.boss ? "Assets/Data/Monsters/" + bossSpec.data : StageBuilder.DataPath(c.stage, c.hunt));
        enemy = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefab)).GetComponent<RegionalMonster>();
        enemy.transform.position = new Vector3(0f, c.boss ? 5f : 3f, 0f);
        enemy.Initialize(data, null);
        // 큰 몸체는 몸에 닿는 거리가 멀다. 크기만큼 붙는 거리와 칠 수 있는 거리를 늘린다.
        float baseScale = AssetDatabase.LoadAssetAtPath<GameObject>(StageBuilder.PrefabPath(1, c.boss ? bossSpec.kind : c.hunt)).transform.localScale.x;
        reach = 0.45f * (enemy.transform.localScale.x / baseScale - 1f);
        Physics2D.SyncTransforms();

        start = ready = Time.time + .5f;
        damage = 0;
        warningAt = -1;
        evadeUntil = 0;
        previousHP = player.HP;
        player.OnStatsChanged += Observe;
    }

    static void Build(Case c)
    {
        int level = LevelFor(c.stage, c.hunt, c.boss);
        while (player.Level < level) player.AddEXP(player.MaxEXP - player.EXP + .01f);
        int points = player.StatPoints;
        int strength = Mathf.RoundToInt(points * .43f), vitality = Mathf.RoundToInt(points * .36f);
        for (int i = 0; i < strength; i++) player.UpgradeAttribute(RpgAttribute.Strength);
        for (int i = 0; i < vitality; i++) player.UpgradeAttribute(RpgAttribute.Vitality);
        while (player.StatPoints > 0) player.UpgradeAttribute(RpgAttribute.Defense);
        for (int pass = 0; pass < 4; pass++)
            for (int id = 0; id < RpgSkillCatalog.All.Length; id++) player.LearnSkill(id);

        var inventory = player.GetComponent<PlayerInventory>();
        inventory.Unequip(ItemCategory.Accessory);   // 무작위 옵션은 결과를 흔드므로 뺀다(설계도 장신구를 여유분으로 둔다)
        int tier = Mathf.Min(c.stage, 5);
        Equip(inventory, ItemCategory.Weapon, tier, ItemRarity.Epic);
        Equip(inventory, ItemCategory.Armor, tier, ItemRarity.Rare);
        player.Heal(player.MaxHP);
        player.RestoreMana(player.MaxMana);
    }

    static void Equip(PlayerInventory inventory, ItemCategory category, int tier, ItemRarity rarity)
    {
        var definition = ItemDatabase.Instance.items.First(i => i != null && i.category == category && i.tier == tier);
        var entry = PlayerInventory.CreateInstance(definition, rarity, rng);
        if (!inventory.TryAddInstance(entry) || !inventory.Equip(entry.slotIndex)) throw new Exception("장비를 끼지 못함: " + definition.itemId);
    }

    static void Observe()
    {
        if (Time.time >= ready && player.HP < previousHP) damage += previousHP - player.HP;
        previousHP = player.HP;
    }

    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("simulation exceeded wall-clock deadline");
            if (Time.time < ready) return;
            var c = Cases[index];
            float elapsed = Time.time - start;
            bool killed = enemy == null || enemy.IsDead, timeout = elapsed > (c.boss ? 400f : 40f);
            if (killed || !player.IsAlive || timeout)
            {
                Record(c, elapsed, killed);
                if (++index == Cases.Count) { Finish(true); return; }
                Next();
                return;
            }

            Vector2 delta = enemy.transform.position - player.transform.position;
            Vector2 toward = delta.normalized;
            if (enemy.CurrentPhase == RegionalMonster.Phase.Windup)
            {
                if (warningAt < 0) warningAt = Time.time;
                if (Time.time - warningAt >= .25f) evadeUntil = Time.time + .45f;
            }
            else warningAt = -1;
            if (enemy.AttackStyle == RegionalMonster.Style.Volley &&
                UnityEngine.Object.FindObjectsByType<RegionalProjectile>(FindObjectsSortMode.None).Any(p => Vector2.Distance(p.transform.position, player.transform.position) < 2f))
                evadeUntil = Time.time + .3f;
            Vector2 movement = Time.time < evadeUntil
                ? (enemy.AttackStyle == RegionalMonster.Style.Spores ? -toward : new Vector2(-toward.y, toward.x))
                : delta.magnitude > 1.1f + reach ? toward : Vector2.zero;
            body.linearVelocity = movement * 5f;
            typeof(Player_Controller).GetField("<LastMoveDir>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player.GetComponent<Player_Controller>(), toward);
            if (delta.magnitude <= 1.65f + reach && attack.AttackReady) attack.ForceAttack(toward);
            if (delta.magnitude <= 3f + reach && skills.Remaining(0) <= 0) skills.TryCast(0);
            if (player.HP < player.MaxHP * .75f) skills.TryCast(6);
            if (enemy.TelegraphVisible && skills.Remaining(3) <= 0) skills.TryCast(3);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Finish(false, e.Message);
        }
    }

    static void Record(Case c, float seconds, bool killed)
    {
        string label = c.boss ? (c.stage == 5 ? "final-boss" : "boss") : "hunt" + c.hunt;
        string result = killed ? "kill" : player.IsAlive ? "timeout" : "dead";
        rows.Add(string.Join(",", c.stage, label, player.Level, attack.AttackPower, player.MaxHP.ToString("F0", CultureInfo.InvariantCulture), data.MaxHP, data.AttackDamage,
            seconds.ToString("F1", CultureInfo.InvariantCulture), damage.ToString("F0", CultureInfo.InvariantCulture), result));
        File.WriteAllLines(SessionState.GetString(Output, "") + "/combat.csv", rows);
        Debug.Log("STAGE BALANCE " + rows[rows.Count - 1]);
        string name = $"stage {c.stage} {label}";
        Check(killed && player.IsAlive, name + " is cleared alive");
        if (c.boss) Check(seconds >= BossMinSeconds && seconds <= BossMaxSeconds, $"{name} takes 2~2.5 minutes ({seconds:F0}s)");
        else Check(seconds <= HuntMaxSeconds && damage <= player.MaxHP * .5f, $"{name} kill time and damage budget ({seconds:F1}s, {damage:F0})");
    }

    static void Check(bool pass, string message)
    {
        // 측정을 끝까지 하도록 실패도 기록만 하고 계속한다. 결과 줄에 실패 목록을 남긴다.
        if (pass) checks++;
        else failures.Add(message);
        Debug.Log((pass ? "STAGE BALANCE PASS: " : "STAGE BALANCE FAIL: ") + message);
    }

    static readonly List<string> failures = new();

    static void Finish(bool completed, string reason = "")
    {
        EditorApplication.update -= Tick;
        if (body != null) body.linearVelocity = Vector2.zero;
        Time.timeScale = 1;
        bool success = completed && failures.Count == 0;
        string output = SessionState.GetString(Output, "");
        File.WriteAllLines(output + "/combat.csv", rows);
        File.WriteAllText(output + "/result.txt", (success ? "PASS " : "FAIL ") + checks + " checks, " + index + "/" + Cases.Count + " cases. " + reason + string.Join(" | ", failures));
        SessionState.SetInt(ExitKey, success ? 0 : 1);
        failures.Clear();
        EditorApplication.ExitPlaymode();
    }
}

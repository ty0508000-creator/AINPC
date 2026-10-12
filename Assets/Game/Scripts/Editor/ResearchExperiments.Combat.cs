using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 기억(소윤 선택)에 따른 어둠의 전투 성향 비교. Stage1 플레이 모드에서 StartCombatStyleComparison 을 부른다.
/// 사냥터 3 한가운데 스켈레톤 3마리를 같은 자리에 세우고, 같은 위치·체력에서 감정 수치를 10으로 떨어뜨려 어둠이 몸을 잡게 한다.
/// 조건은 선택 기록만 바꾸며, 조건을 번갈아 돌려 순서 효과를 줄인다. 몬스터 경험치는 0 으로 바꿔 레벨업이 끼지 않게 한다.
/// 테스트 세이브(Tools > AINPC > 테스트 세이브로 플레이)를 켜고 돌리면 실제 세이브가 바뀌지 않는다.
/// </summary>
public static partial class ResearchExperiments
{
    static readonly (string name, string choice, float hpRatio)[] CombatConditions =
    {
        ("기본", null, 1f),
        ("공격형(외면)", HubStoryIds.RescueLeft, 1f),
        ("보호형(구조)", HubStoryIds.Rescue, 1f),
        ("보호형·체력40%", HubStoryIds.Rescue, 0.4f),
    };

    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void StartCombatStyleComparison(int trialsPerCondition = 20, float trialSeconds = 25f, float timeScale = 2f)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Stage1 플레이 모드에서 실행해야 합니다.");
        if (Running) return;
        var run = new CombatRun(trialsPerCondition, trialSeconds, timeScale);
        Running = true;
        EditorApplication.update += run.Tick;
    }

    class CombatRun
    {
        readonly int trialsPerCondition;
        readonly float trialSeconds, timeScale;
        readonly PlayerStats player;
        readonly MoodSystem mood;
        readonly AIController ai;
        readonly InnerVoiceManager inner;
        readonly Dictionary<string, StoryChoiceEntry> choices;
        readonly List<StoryChoiceEntry> originalChoices;
        readonly float originalTriggerChance;
        readonly MonsterBase prefab;
        readonly MonsterData data;
        readonly MonsterSpawnPoint[] points;
        readonly Vector2 center = new Vector2(440f, -2f);
        readonly List<string> rows = new List<string>();
        readonly List<Trial> trials = new List<Trial>();

        int index;              // 전체 시행 번호 (조건을 번갈아 가며 증가)
        bool inTrial;
        float phaseEnd, trialStart, lastTime, lastHp;
        Trial current;
        float curSessionStart = float.NaN, lastDist;
        int lastAttacks, lastDashes, lastRetreats;

        public CombatRun(int trialsPerCondition, float trialSeconds, float timeScale)
        {
            this.trialsPerCondition = trialsPerCondition; this.trialSeconds = trialSeconds; this.timeScale = timeScale;
            player = Object.FindFirstObjectByType<PlayerStats>();
            var choiceManager = StoryChoiceManager.Instance;
            if (player == null || choiceManager == null) throw new InvalidOperationException("Stage1 에서 실행해야 합니다(플레이어·StoryChoiceManager 필요).");
            mood = player.GetComponent<MoodSystem>(); ai = player.GetComponent<AIController>(); inner = player.GetComponent<InnerVoiceManager>();

            foreach (var area in Object.FindObjectsByType<MonsterSpawnArea>(FindObjectsSortMode.None))
            {
                if (Mathf.Approximately(area.transform.position.x, center.x))
                {
                    prefab = (MonsterBase)typeof(MonsterSpawnArea).GetField("monsterPrefab", Private).GetValue(area);
                    data = (MonsterData)typeof(MonsterSpawnArea).GetField("monsterData", Private).GetValue(area);
                }
                area.enabled = false;   // 실험 중에는 사냥터가 몬스터를 더 뽑지 않는다
            }
            if (prefab == null) throw new InvalidOperationException("사냥터 3(x=440)의 몬스터를 찾지 못했습니다.");
            data = Object.Instantiate(data);
            typeof(MonsterData).GetField("expReward", Private).SetValue(data, 0f);

            var offsets = new[] { new Vector2(-3.5f, 2f), new Vector2(3.5f, 2f), new Vector2(0f, 4.5f) };
            points = offsets.Select((o, i) =>
            {
                var go = new GameObject("CombatResearch point " + i);
                go.transform.position = center + o;
                return go.AddComponent<MonsterSpawnPoint>();
            }).ToArray();

            choices = (Dictionary<string, StoryChoiceEntry>)typeof(StoryChoiceManager).GetField("choices", Private).GetValue(choiceManager);
            originalChoices = choices.Values.ToList();
            var chance = typeof(InnerVoiceManager).GetField("triggerChance", Private);
            originalTriggerChance = (float)chance.GetValue(inner);
            chance.SetValue(inner, 0f);   // 실험 중 내면 대화가 열려 시간을 멈추지 않게

            Time.timeScale = timeScale;
            phaseEnd = Time.time + 1f;
            rows.Add(Trial.Header);
        }

        int Total => trialsPerCondition * CombatConditions.Length;

        public void Tick()
        {
            if (!EditorApplication.isPlaying) { Finish("플레이가 멈춰 중단"); return; }
            if (inTrial) { TickTrial(); return; }
            if (Time.time < phaseEnd) return;
            if (index >= Total) { Finish(null); return; }
            BeginTrial();
        }

        void BeginTrial()
        {
            var (name, choice, hpRatio) = CombatConditions[index % CombatConditions.Length];
            current = new Trial { Condition = name, Number = index / CombatConditions.Length + 1, Level = player.Level };

            choices.Remove(HubStoryIds.Rescue); choices.Remove(HubStoryIds.RescueLeft);
            if (choice != null) choices[choice] = new StoryChoiceEntry { choiceId = choice };

            foreach (var m in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (Vector2.Distance(m.transform.position, center) < 30f) Object.Destroy(m.gameObject);
            if (!player.IsAlive) player.Respawn();
            typeof(PlayerStats).GetProperty("HP").GetSetMethod(true).Invoke(player, new object[] { player.MaxHP * hpRatio });
            var body = player.GetComponent<Rigidbody2D>();
            body.position = center; body.linearVelocity = Vector2.zero; player.transform.position = center;
            foreach (var p in points) p.Spawn(prefab, data);

            mood.ChangeMood(10f - mood.Mood);   // 전투 감지가 켜지면 어둠이 몸을 잡는다
            trialStart = lastTime = Time.time;
            lastHp = player.HP;
            current.HpStart = player.HP;
            curSessionStart = float.NaN;
            inTrial = true;
        }

        void TickTrial()
        {
            float now = Time.time, dt = now - lastTime;
            lastTime = now;
            if (mood.IsAIControlled) current.AiSeconds += dt;
            if (player.HP < lastHp) current.Damage += lastHp - player.HP;
            else if (player.HP > lastHp) current.Healed += player.HP - lastHp;
            lastHp = player.HP;
            TrackSession(now);

            bool cleared = points.All(p => p.IsEmpty);
            bool died = !player.IsAlive;
            if (!cleared && !died && now - trialStart < trialSeconds) return;

            current.Cleared = cleared; current.Died = died;
            current.Seconds = now - trialStart;
            current.Style = ai.Style.ToString();
            mood.ChangeMood(90f - mood.Mood);   // 몸을 돌려받아 세션을 닫는다
            CloseSession();
            current.HpEnd = player.HP;
            rows.Add(current.Csv());
            trials.Add(current);
            Debug.Log($"[Research] 전투 {index + 1}/{Total} {current.Condition} #{current.Number}: " +
                      $"{(current.Cleared ? "처치" : current.Died ? "사망" : "시간초과")} {current.Seconds:0.0}s 피해 {current.Damage:0}");
            foreach (var m in Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
                if (Vector2.Distance(m.transform.position, center) < 30f) Object.Destroy(m.gameObject);
            inTrial = false;
            index++;
            phaseEnd = now + 2.5f;   // 부활 보호 시간(2초)이 다음 시행에 남지 않게
        }

        // 강탈이 중간에 풀렸다 다시 잡히면 AIController 의 카운터가 0 으로 돌아간다 → 세션마다 더한다.
        void TrackSession(float now)
        {
            float start = (float)typeof(AIController).GetField("sessionStart", Private).GetValue(ai);
            if (start < trialStart) return;   // 아직 이전 시행의 세션
            if (start != curSessionStart)
            {
                CloseSession();
                curSessionStart = start;
            }
            lastDist = (float)typeof(AIController).GetField("sessionDistance", Private).GetValue(ai);
            lastAttacks = (int)typeof(AIController).GetField("sessionAttacks", Private).GetValue(ai);
            lastDashes = (int)typeof(AIController).GetField("sessionDashes", Private).GetValue(ai);
            lastRetreats = (int)typeof(AIController).GetField("sessionRetreats", Private).GetValue(ai);
        }

        void CloseSession()
        {
            if (float.IsNaN(curSessionStart)) return;
            current.Distance += lastDist; current.Attacks += lastAttacks; current.Dashes += lastDashes; current.Retreats += lastRetreats;
            current.Sessions++;
            curSessionStart = float.NaN;
            lastDist = 0; lastAttacks = lastDashes = lastRetreats = 0;
        }

        void Finish(string aborted)
        {
            EditorApplication.update -= Tick;
            try
            {
                if (EditorApplication.isPlaying)
                {
                    Time.timeScale = 1f;
                    choices.Clear();
                    foreach (var entry in originalChoices) choices[entry.choiceId] = entry;
                    typeof(InnerVoiceManager).GetField("triggerChance", Private).SetValue(inner, originalTriggerChance);
                    foreach (var p in points) if (p != null) Object.Destroy(p.gameObject);
                }
                string dir = Path.GetFullPath("VerificationResults/Research");
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.WriteAllText(Path.Combine(dir, $"combat_style_{stamp}.csv"), string.Join("\n", rows) + "\n", Encoding.UTF8);
                LastSummaryPath = Path.Combine(dir, $"combat_style_{stamp}_summary.md");
                File.WriteAllText(LastSummaryPath, CombatSummary(trials, trialsPerCondition, trialSeconds, timeScale, aborted), Encoding.UTF8);
                Debug.Log($"[Research] 전투 성향 비교 {trials.Count}건 완료: {LastSummaryPath}");
            }
            finally { Running = false; }
        }
    }

    class Trial
    {
        public const string Header = "condition,trial,style,level,cleared,died,seconds,damage,healed,hp_start,hp_end,distance,attacks,dashes,retreats,ai_seconds,sessions";
        public string Condition, Style;
        public int Number, Level, Attacks, Dashes, Retreats, Sessions;
        public bool Cleared, Died;
        public float Seconds, Damage, Healed, HpStart, HpEnd, Distance, AiSeconds;

        public string Csv() => string.Join(",", Condition, Number, Style, Level, Cleared ? 1 : 0, Died ? 1 : 0,
            F(Seconds), F(Damage), F(Healed), F(HpStart), F(HpEnd), F(Distance), Attacks, Dashes, Retreats, F(AiSeconds), Sessions);
        static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    static string CombatSummary(List<Trial> trials, int perCondition, float trialSeconds, float timeScale, string aborted)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 기억에 따른 전투 성향 비교 ({DateTime.Now:yyyy-MM-dd HH:mm})");
        sb.AppendLine();
        if (aborted != null) sb.AppendLine($"**중단됨: {aborted}**\n");
        sb.AppendLine($"Stage1 사냥터 3, 스켈레톤 3마리 고정 배치, 조건당 {perCondition}회(번갈아 진행), 시행당 최대 {trialSeconds}초(게임 시간), {timeScale}배속, 몬스터 경험치 0.");
        sb.AppendLine("피해는 체력이 줄어든 양의 합(회복은 따로), 시간은 처치·사망·시간초과까지. 값은 평균 ± 표준편차.");
        sb.AppendLine();
        sb.AppendLine("| 조건 | n | 처치 | 사망 | 시간(초) | 받은 피해 | 이동거리 | 공격 | 대시 | 후퇴 | AI 조작 비율 |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var (name, _, _) in CombatConditions)
        {
            var g = trials.Where(t => t.Condition == name).ToList();
            if (g.Count == 0) continue;
            sb.AppendLine($"| {name} | {g.Count} | {g.Count(t => t.Cleared)} | {g.Count(t => t.Died)} | {MS(g, t => t.Seconds)} | {MS(g, t => t.Damage)} | " +
                          $"{MS(g, t => t.Distance)} | {MS(g, t => t.Attacks)} | {MS(g, t => t.Dashes)} | {MS(g, t => t.Retreats)} | " +
                          $"{g.Average(t => t.Seconds > 0 ? t.AiSeconds / t.Seconds : 0):0%} |");
        }
        sb.AppendLine();
        sb.AppendLine("## 조건 간 차이 (Welch t, 효과 크기 Cohen d, p 는 정규 근사)");
        sb.AppendLine();
        sb.AppendLine("| 비교 | 지표 | 평균 차이 | t | p | d |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|");
        var pairs = new[] { ("공격형(외면)", "보호형(구조)"), ("공격형(외면)", "기본"), ("보호형(구조)", "기본") };
        var metrics = new (string, Func<Trial, float>)[] { ("시간", t => t.Seconds), ("받은 피해", t => t.Damage), ("이동거리", t => t.Distance), ("대시", t => t.Dashes) };
        foreach (var (a, b) in pairs)
            foreach (var (metric, f) in metrics)
            {
                var x = trials.Where(t => t.Condition == a).Select(f).ToList();
                var y = trials.Where(t => t.Condition == b).Select(f).ToList();
                if (x.Count < 2 || y.Count < 2) continue;
                var (diff, tStat, p, d) = Compare(x, y);
                if (SD(x) == 0 && SD(y) == 0)   // 두 조건 모두 매번 같은 값: 통계량이 정의되지 않는다
                    sb.AppendLine($"| {a} − {b} | {metric} | {diff:0.0} | 분산 0 | - | - |");
                else
                    sb.AppendLine($"| {a} − {b} | {metric} | {diff:0.0} | {tStat:0.00} | {(p < 0.001 ? "<0.001" : p.ToString("0.000"))} | {d:0.00} |");
            }
        return sb.ToString();
    }

    static string MS(List<Trial> g, Func<Trial, float> f)
    {
        var v = g.Select(f).ToList();
        return $"{v.Average():0.0} ± {SD(v):0.0}";
    }

    static double SD(List<float> v)
    {
        if (v.Count < 2) return 0;
        double mean = v.Average();
        return Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / (v.Count - 1));
    }

    static (double diff, double t, double p, double d) Compare(List<float> x, List<float> y)
    {
        double mx = x.Average(), my = y.Average(), sx = SD(x), sy = SD(y);
        double se = Math.Sqrt(sx * sx / x.Count + sy * sy / y.Count);
        double t = se > 0 ? (mx - my) / se : 0;
        double pooled = Math.Sqrt(((x.Count - 1) * sx * sx + (y.Count - 1) * sy * sy) / (x.Count + y.Count - 2));
        double d = pooled > 0 ? (mx - my) / pooled : 0;
        double p = 2 * (1 - NormalCdf(Math.Abs(t)));
        return (mx - my, t, p, d);
    }

    // Abramowitz–Stegun 7.1.26 근사
    static double NormalCdf(double z)
    {
        double t = 1 / (1 + 0.3275911 * z / Math.Sqrt(2));
        double erf = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-z * z / 2);
        return 0.5 * (1 + erf);
    }
}

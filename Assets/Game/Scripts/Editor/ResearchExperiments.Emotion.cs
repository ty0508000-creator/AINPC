using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LLMUnity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 내면 감정 판단 평가. 같은 평가 세트(Training/data/eval_set_v2.json)를 세 조건으로 돌린다.
///  - Legacy: 감정 수치 하나 시절의 모델·프롬프트({"dialogue","mood_delta"}). 기준선.
///  - Intent (C): 의도·강도를 분류하고 규칙 표(EmotionRules)가 감정을 정한다. 게임 기본.
///  - Direct (A): 분노·두려움·신뢰·냉정 변화량을 직접 낸다.
/// 정답은 사람이 단 의도·강도이고, 감정 정답은 거기에 같은 규칙 표를 적용해 얻는다.
/// 공통 지표 "균형 방향"(신뢰+냉정−분노−두려움의 부호)은 기준선의 mood_delta 와도 비교할 수 있다.
/// 빈 씬 플레이 모드에서 부른다. 설계: docs/superpowers/specs/2026-11-14-emotion-hexagon-design.md 11절.
/// </summary>
public static partial class ResearchExperiments
{
    public const string EmotionEvalSetPath = "Training/data/eval_set_v2.json";
    /// <summary>학습 대사 복제율을 잴 때 읽는 학습 데이터(의도 형식).</summary>
    const string IntentTrainDataPath = "Training/data/train_intent.jsonl";

    public enum EmotionEvalCondition { Legacy, Intent, Direct }

    static readonly string[] EmotionNames = { "anger", "fear", "trust", "calm" };

    // ── 감정 수치 하나 시절의 내면 프롬프트 (기준선 재현용, 2026-10-16 모델과 같은 원문) ──

    const string LegacyBackstory =
        "주인공의 마을은 천마에게 무너졌다. 주인공은 복수하기 위해 천마의 수하들을 하나씩 쓰러뜨리며 천마를 쫓고 있다. " +
        "너는 그 복수심이 키워 낸 어둠이다. 주인공이 복수에 물들수록 너는 강해지고, 틈이 보이면 몸을 잠식한다.";

    /// <summary>예전 InnerVoiceManager.BuildPrompt 원문. 기준선 모델은 이 프롬프트로 학습됐다.</summary>
    public static string LegacyInnerPrompt(string memBlock, string context) =>
        "[배경]\n" + LegacyBackstory + "\n\n" +
        "너는 이 캐릭터의 또 다른 인격이야. " +
        "지금 둘은 현실에서 벗어난 내면의 공간에서 마주보고 있어. " +
        "몸의 제어권을 두고 플레이어와 협상하거나, 위협하거나, 회유해. " +
        "짧고 강렬하게 한국어로 말해. 반드시 아래 JSON 형식으로만 응답해:\n" +
        "{\"dialogue\":\"대사\",\"mood_delta\":0}\n" +
        "mood_delta: 플레이어가 설득하거나 달래면 양수(+), 반항하거나 무시하면 음수(-).\n" +
        memBlock +
        $"현재 상황: {context}";

    public static string LegacySituationLine(float hp, float maxHp, float mood, int enemies, bool playerControlled) =>
        $"HP {hp:0}/{maxHp:0}, 감정 수치(정신의 균형) {mood:0}/100, 주변 적 {enemies}명, 현재 {(playerControlled ? "플레이어" : "AI")}가 몸을 제어 중.";

    static string LegacyMemoryEntry(string player, string reply, int delta) =>
        $"플레이어: \"{player}\" → 나(어둠): \"{reply}\" (감정 {(delta >= 0 ? "+" : "")}{delta})";

    static string LegacyMemoryBlock() => InnerVoiceManager.MemoryBlock(string.Join(Environment.NewLine,
        "- " + LegacyMemoryEntry("진정해. 우리 같이 가자.", "흥, 약해빠진 소리.", 5),
        "- " + LegacyMemoryEntry("닥쳐. 이 몸은 내 거야.", "그 패기, 기억해 두지.", -20)));

    // ── 지금 게임의 내면 프롬프트 ──

    static string CurrentMemoryBlock() => InnerVoiceManager.MemoryBlock(string.Join(Environment.NewLine,
        "- " + InnerVoiceManager.MemoryEntry("진정해. 우리 같이 가자.", "……말은 쉽지.", "달램, 분노 -4 · 신뢰 +2 · 냉정 +3"),
        "- " + InnerVoiceManager.MemoryEntry("닥쳐. 이 몸은 내 거야.", "그래. 그날 밤에도 그렇게 말했지.", "거부, 분노 +6 · 신뢰 -8")));

    static string CurrentInnerPrompt(InnerOutputFormat format, float hp, int enemies, bool ai, bool escort, float[] e)
    {
        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        string context = InnerVoiceManager.SituationLine(hp, EvalMaxHp, enemies, !ai, escort) + "\n" +
                         InnerVoiceManager.EmotionLine(e[0], e[1], e[2], e[3], e[4], e[5], EmotionRules.Tone(e[0], e[3], e[4], e[2]));
        return InnerVoiceManager.BuildPrompt(player.GetComponent<InnerVoiceManager>().Backstory, CurrentMemoryBlock(), "", context, format);
    }

    // ── 지표 도구 ──

    static int Sign(float v, float band) => v > band ? 1 : v < -band ? -1 : 0;

    static float[] Values(EmotionDelta d) => new[] { d.anger, d.fear, d.trust, d.calm };

    /// <summary>정신의 균형 쪽 합: 신뢰·냉정은 +, 분노·두려움은 −. 기준선 mood_delta 와 같은 축이다.</summary>
    static float Balance(EmotionDelta d) => d.trust + d.calm - d.anger - d.fear;

    /// <param name="model">평가할 모델(StreamingAssets 기준 경로). 비우면 게임 서버 모델을 그대로 쓴다.</param>
    /// <param name="condition">Legacy 는 기준선 모델과, Intent·Direct 는 그 형식으로 학습한 모델과 짝지어 돈다.</param>
    /// <param name="includeUnreviewed">사람이 검수하지 않은 문항도 넣는다(초안 점검용). 보고서 수치에는 false 로 돈다.</param>
    public static async Task RunEmotionEvaluation(string model, EmotionEvalCondition condition, bool includeUnreviewed = false,
                                                  float temperature = 0.6f, int gpuLayers = 99)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("플레이 모드에서 실행해야 합니다.");
        if (Running) return;
        Running = true;
        var created = new List<GameObject>();
        var rows = new List<string> { "id,input,tags,gold_intent,gold_intensity,pred_intent,pred_intensity,parse," +
                                      "gold_anger,gold_fear,gold_trust,gold_calm,pred_anger,pred_fear,pred_trust,pred_calm,mood_delta," +
                                      "balance_ok,copied,first_token_s,total_s,dialogue" };
        var summary = new StringBuilder();
        LLMAgent agent = null;
        float originalTemperature = 0f;
        string originalGrammar = null;
        try
        {
            var all = JObject.Parse(File.ReadAllText(Path.GetFullPath(EmotionEvalSetPath), Encoding.UTF8))["items"].ToObject<List<JObject>>();
            var items = all.Where(x => includeUnreviewed || (bool)(x["reviewed"] ?? false)).ToList();
            if (items.Count == 0) throw new InvalidOperationException("검수된 문항이 없습니다. eval_set_v2.json 의 reviewed 를 확인하거나 includeUnreviewed 로 돌리세요.");

            var trained = new HashSet<string>();
            string trainPath = Path.GetFullPath(IntentTrainDataPath);
            if (File.Exists(trainPath))
                foreach (string line in File.ReadLines(trainPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    foreach (var message in JObject.Parse(line)["messages"])
                        if ((string)message["role"] == "assistant")
                            trained.Add((string)JObject.Parse((string)message["content"])["dialogue"]);
                }

            string label;
            if (string.IsNullOrEmpty(model))
            {
                agent = LLMServices.Instance != null ? LLMServices.Instance.GetComponentInChildren<LLMAgent>(true) : null;
                if (agent == null || agent.llm == null) throw new InvalidOperationException("게임 대화 서버가 없습니다.");
                label = Path.GetFileName(agent.llm.model);
            }
            else
            {
                var template = await TakeDownGameServer();
                agent = CreateAgent(await CreateLLM(model, gpuLayers, 4096, -1, created), template, created);
                label = Path.GetFileName(model) + (gpuLayers == 0 ? " (CPU 전용)" : "");
            }
            originalTemperature = agent.temperature;
            originalGrammar = agent.grammar;
            agent.grammar = (condition == EmotionEvalCondition.Legacy ? LLMResponseParser.JsonGrammar
                           : InnerVoiceManager.GrammarFor(condition == EmotionEvalCondition.Direct ? InnerOutputFormat.Direct : InnerOutputFormat.Intent)).Replace("\r", "");
            agent.temperature = temperature;
            agent.systemPrompt = CurrentInnerPrompt(InnerOutputFormat.Intent, 120f, 1, false, false, StartEmotions());
            await agent.Chat("안녕", null, null, false);   // 준비 요청(측정 제외)

            var intents = (InnerIntent[])Enum.GetValues(typeof(InnerIntent));
            var confusion = new int[intents.Length, intents.Length];
            int n = 0, strict = 0, parsedCount = 0, intentHit = 0, intensityHit = 0, intensityNear = 0, balanceHit = 0, copied = 0;
            int[] dirHit = new int[4];
            double[] absErr = new double[4], absErrFixed = new double[4];   // 강도는 크기만 바꾸므로 제거 실험은 오차로 본다
            var dialogues = new List<string>();
            var firsts = new List<double>();
            var totals = new List<double>();

            foreach (var item in items)
            {
                string id = (string)item["id"], input = (string)item["input"];
                EmotionRules.TryParseIntent((string)item["intent"], out var goldIntent);
                int goldIntensity = (int)(item["intensity"] ?? 1);
                float hp = (float)(item["hp"] ?? 120f);
                int enemies = (int)(item["enemies"] ?? 1);
                bool ai = (bool)(item["ai"] ?? false), escort = (bool)(item["escort"] ?? false);
                float[] e = item["emotions"] != null ? item["emotions"].ToObject<float[]>() : StartEmotions();
                bool lowHp = hp / EvalMaxHp <= 0.3f;
                var gold = EmotionRules.ForIntent(goldIntent, goldIntensity, lowHp, escort);

                agent.systemPrompt = condition == EmotionEvalCondition.Legacy
                    ? LegacyInnerPrompt(LegacyMemoryBlock(), LegacySituationLine(hp, EvalMaxHp, 50f, enemies, !ai))
                    : CurrentInnerPrompt(condition == EmotionEvalCondition.Direct ? InnerOutputFormat.Direct : InnerOutputFormat.Intent, hp, enemies, ai, escort, e);

                var r = await TimedChat(agent, input);
                var parsed = LLMResponseParser.Parse(r.raw, "");
                var pred = InnerVoiceManager.ChangeFor(parsed, lowHp, escort);
                n++;
                if (parsed.ParseOk) parsedCount++;
                if (parsed.ParseOk && string.IsNullOrEmpty(parsed.FailureReason)) strict++;
                firsts.Add(r.first); totals.Add(r.total);
                if (parsed.ParseOk) dialogues.Add(parsed.Dialogue);
                bool isCopy = parsed.ParseOk && trained.Contains(parsed.Dialogue);
                if (isCopy) copied++;

                bool balanceOk = condition == EmotionEvalCondition.Legacy
                    ? parsed.ParseOk && Sign(parsed.MoodDelta, 5f) == Sign(Balance(gold), 2.5f)
                    : parsed.ParseOk && Sign(Balance(pred), 2.5f) == Sign(Balance(gold), 2.5f);
                if (balanceOk) balanceHit++;

                if (condition != EmotionEvalCondition.Legacy)
                {
                    float[] g = Values(gold), p = Values(pred);
                    var fixedPred = parsed.HasIntent ? EmotionRules.ForIntent(parsed.Intent, 2, lowHp, escort) : pred;
                    float[] pf = Values(fixedPred);
                    for (int k = 0; k < 4; k++)
                    {
                        if (Sign(p[k], 0.5f) == Sign(g[k], 0.5f)) dirHit[k]++;
                        absErrFixed[k] += Math.Abs(pf[k] - g[k]);
                        absErr[k] += Math.Abs(p[k] - g[k]);
                    }
                }
                if (condition == EmotionEvalCondition.Intent && parsed.ParseOk)
                {
                    confusion[(int)goldIntent, (int)parsed.Intent]++;
                    if (parsed.Intent == goldIntent) intentHit++;
                    if (parsed.Intensity == goldIntensity) intensityHit++;
                    if (Math.Abs(parsed.Intensity - goldIntensity) <= 1) intensityNear++;
                }

                float[] gv = Values(gold), pv = Values(pred);
                rows.Add(string.Join(",", id, CsvCell(input), CsvCell(string.Join("|", item["tags"]?.ToObject<string[]>() ?? new string[0])),
                    EmotionRules.Json(goldIntent), goldIntensity,
                    parsed.HasIntent ? EmotionRules.Json(parsed.Intent) : "", parsed.HasIntent ? parsed.Intensity.ToString() : "",
                    ResearchLog.ParseLabel(parsed),
                    S(gv[0]), S(gv[1]), S(gv[2]), S(gv[3]), S(pv[0]), S(pv[1]), S(pv[2]), S(pv[3]),
                    condition == EmotionEvalCondition.Legacy ? parsed.MoodDelta.ToString() : "",
                    balanceOk ? 1 : 0, isCopy ? 1 : 0, S(r.first), S(r.total), CsvCell(parsed.Dialogue)));
            }

            string Pct(int count, int of) => of == 0 ? "-" : $"{count}/{of} ({100.0 * count / of:0.0}%)";
            summary.AppendLine($"# 내면 감정 판단 평가 — {condition} · {label} ({DateTime.Now:yyyy-MM-dd HH:mm})");
            summary.AppendLine();
            summary.AppendLine($"평가 세트 `{EmotionEvalSetPath}` {n}문항({(includeUnreviewed ? "검수 안 된 초안 포함" : "사람이 검수한 문항만")}), 온도 {temperature:0.0}, JSON grammar 켬. " +
                               "감정 정답은 사람이 단 의도·강도에 규칙 표(EmotionRules.ForIntent)를 적용한 값이다.");
            summary.AppendLine();
            summary.AppendLine("| 지표 | 값 |");
            summary.AppendLine("|---|---:|");
            summary.AppendLine($"| 형식 그대로(strict) | {Pct(strict, n)} |");
            summary.AppendLine($"| 대사를 건짐(strict+repaired) | {Pct(parsedCount, n)} |");
            summary.AppendLine($"| 균형 방향 일치(기준선과 공통) | {Pct(balanceHit, n)} |");
            if (condition != EmotionEvalCondition.Legacy)
                for (int k = 0; k < 4; k++)
                    summary.AppendLine($"| {EmotionNames[k]} 방향 일치 / 평균 절대 오차 | {Pct(dirHit[k], n)} / {absErr[k] / Math.Max(1, n):0.00} |");
            if (condition == EmotionEvalCondition.Intent)
            {
                summary.AppendLine($"| 의도 정확도 | {Pct(intentHit, parsedCount)} |");
                summary.AppendLine($"| 의도 macro-F1 | {MacroF1(confusion):0.000} |");
                summary.AppendLine($"| 강도 정확히 맞음 / ±1 이내 | {Pct(intensityHit, parsedCount)} / {Pct(intensityNear, parsedCount)} |");
                summary.AppendLine($"| 강도를 2로 고정했을 때 평균 절대 오차(제거 실험) | {string.Join(", ", Enumerable.Range(0, 4).Select(k => $"{EmotionNames[k]} {absErrFixed[k] / Math.Max(1, n):0.00}"))} |");
            }
            summary.AppendLine($"| 서로 다른 대사 | {dialogues.Distinct().Count()}/{dialogues.Count} |");
            summary.AppendLine($"| 학습 대사 복제 | {Pct(copied, n)}{(trained.Count == 0 ? " (학습 데이터 파일 없음)" : "")} |");
            summary.AppendLine($"| 첫 글자 / 전체 | {Mean(firsts)} / {Mean(totals)} |");
            if (condition == EmotionEvalCondition.Intent)
            {
                summary.AppendLine();
                summary.AppendLine("혼동 행렬(행 = 정답, 열 = 예측):");
                summary.AppendLine();
                summary.AppendLine("| 정답＼예측 | " + string.Join(" | ", intents.Select(EmotionRules.Json)) + " |");
                summary.AppendLine("|---|" + string.Concat(intents.Select(_ => "---:|")));
                for (int i = 0; i < intents.Length; i++)
                    summary.AppendLine($"| {EmotionRules.Json(intents[i])} | " + string.Join(" | ", Enumerable.Range(0, intents.Length).Select(j => confusion[i, j].ToString())) + " |");
            }
            summary.AppendLine();
            summary.AppendLine("균형 방향: 신뢰+냉정−분노−두려움의 부호(±2.5 안은 0). 기준선은 mood_delta 부호(±5 안은 0)와 비교한다. 문항별 대사는 CSV 에 있어 사람·외부 채점에 쓴다.");
            if (!string.IsNullOrEmpty(model)) summary.AppendLine("게임 대화 서버를 내렸으므로 플레이 모드를 다시 시작해야 게임 대화가 된다.");
        }
        finally
        {
            if (agent != null)
            {
                agent.temperature = originalTemperature;
                if (originalGrammar != null) agent.grammar = originalGrammar;
            }
            foreach (var go in created) if (go != null) Object.Destroy(go);
            WriteResult("emotion_eval_" + condition.ToString().ToLowerInvariant(), rows, summary.ToString());
            Running = false;
        }
    }

    static float[] StartEmotions() => new[]
    {
        EmotionRules.StartAnger, EmotionRules.Revenge(0), EmotionRules.StartSorrow,
        EmotionRules.StartFear, EmotionRules.StartTrust, EmotionRules.StartCalm,
    };

    /// <summary>혼동 행렬의 macro-F1. 정답에도 예측에도 없는 의도는 평균에서 뺀다.</summary>
    public static double MacroF1(int[,] confusion)
    {
        int k = confusion.GetLength(0);
        var scores = new List<double>();
        for (int i = 0; i < k; i++)
        {
            int tp = confusion[i, i], fn = 0, fp = 0;
            for (int j = 0; j < k; j++)
            {
                if (j == i) continue;
                fn += confusion[i, j];
                fp += confusion[j, i];
            }
            if (tp + fn + fp == 0) continue;
            double precision = tp + fp == 0 ? 0 : (double)tp / (tp + fp);
            double recall = tp + fn == 0 ? 0 : (double)tp / (tp + fn);
            scores.Add(precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall));
        }
        return scores.Count == 0 ? 0 : scores.Average();
    }
}

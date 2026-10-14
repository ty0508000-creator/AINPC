using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LLMUnity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>
/// 대화 모델 쪽 측정 두 가지. 빈 씬 플레이 모드에서 부른다.
///  - RunGpuLayerComparison: GPU 층 수(30 / 전부)와 flash attention 을 바꿔 띄우고 첫 글자까지 시간, 전체 응답 시간,
///    초당 생성 토큰을 잰다. 대화창 예열(Chat("") 대 Warmup)의 비용과, 온도별로 같은 입력에 대사가 얼마나 달라지는지도 본다.
///  - RunModelEvaluation: 학습에 쓰지 않은 입력(Training/data/eval_set.json)으로 형식 준수, mood_delta 방향·범위,
///    대사 다양성, 학습 대사 복제율, 응답 시간을 잰다. 모델을 바꿀 때 기준선과 같은 표로 비교하는 용도다.
/// VRAM 이 8B 모델 둘을 담지 못하므로 실험용 서버를 띄울 때는 게임 대화 서버를 내린다. 그 경우 끝나고 플레이 모드를 다시 시작한다.
/// </summary>
public static partial class ResearchExperiments
{
    const string DialogueModel = "Models/darkness-Q4_K_M.gguf";
    public const string EvalSetPath = "Training/data/eval_set.json";
    const string TrainDataPath = "Training/data/train_data.jsonl";
    const float EvalMaxHp = 170f;

    // ── 게임과 같은 프롬프트 (게임 중 흔한 길이: 최근 대화·기억이 찬 상태) ─────────

    static string NpcPrompt()
    {
        string[] turns =
        {
            "플레이어: \"안녕하세요.\" / 촌장 현묵: \"무슨 일로 왔나.\"",
            "플레이어: \"불에 대해 묻고 싶어요.\" / 촌장 현묵: \"지난 일은 묻지 말게.\"",
            "플레이어: \"의뢰가 있나요?\" / 촌장 현묵: \"동쪽 숲에 늑대가 늘었네.\"",
            "플레이어: \"제가 처리할게요.\" / 촌장 현묵: \"말보다 결과를 보이게.\"",
        };
        return DialogueManager.BuildNpcPrompt("촌장 현묵", RegionNpcSetup.ElderPersonality + "\n" + RegionNpcSetup.RegionContext(2),
            DialogueManager.AffinityLine(42f), turns);
    }

    static string InnerContext(float hp, float mood, int enemies, bool aiControl) =>
        InnerVoiceManager.SituationLine(hp, EvalMaxHp, mood, enemies, !aiControl);

    static string InnerPrompt(string context)
    {
        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        // MemoryManager.Recall 이 돌려주는 꼴("- 기억" 줄을 AppendLine 으로 잇고 TrimEnd)의 기억 세 줄
        string recalled = string.Join(Environment.NewLine,
            "- " + InnerVoiceManager.MemoryEntry("진정해. 우리 같이 가자.", "흥, 약해빠진 소리.", 5),
            "- " + InnerVoiceManager.MemoryEntry("닥쳐. 이 몸은 내 거야.", "그 패기, 기억해 두지.", -20),
            "- " + InnerVoiceManager.MemoryEntry("천마를 반드시 죽일 거야.", "그래, 그 분노를 나한테 맡겨.", -10));
        return InnerVoiceManager.BuildPrompt(player.GetComponent<InnerVoiceManager>().Backstory, InnerVoiceManager.MemoryBlock(recalled), "", context);
    }

    static string SmithPrompt(string input) =>
        DialogueManager.BuildMrSmithPrompt("",
            "플레이어: \"검 좀 봐줘.\" / Mr.Smith: \"내려놔.\"\n플레이어: \"얼마나 걸려?\" / Mr.Smith: \"불 식기 전엔 끝나.\"\n" +
            "플레이어: \"고마워.\" / Mr.Smith: \"흠...\"\n플레이어: \"광석 캐 왔어.\" / Mr.Smith: \"보여 줘.\"",
            "- Mr.Smith는 무뚝뚝하고 말수가 적은 마을 대장장이다. 플레이어에게는 반말을 쓴다.\n" +
            "- 플레이어: \"북쪽 광산에서 푸른 광석을 캐 왔어.\" / Mr.Smith: \"쓸 만한 돌이군.\"\n" +
            "- Mr.Smith는 좋은 광석과 성실한 태도를 보면 아주 조금 호의적으로 변한다.",
            input, DialogueManager.AffinityLine(42f));

    // ── 공용 도구 ─────────────────────────────────────────────

    /// <summary>게임 대화 서버를 내려 VRAM 을 비우고, 샘플링 설정을 베낄 게임 에이전트를 돌려준다.</summary>
    static async Task<LLMAgent> TakeDownGameServer()
    {
        var gameAgent = LLMServices.Instance != null ? LLMServices.Instance.GetComponentInChildren<LLMAgent>(true) : null;
        foreach (var llm in Object.FindObjectsByType<LLM>(FindObjectsSortMode.None))
            if (!llm.embeddingsOnly && llm.name == "InnerVoice_LLM_Server") Object.Destroy(llm.gameObject);
        await Task.Delay(1500);   // 모델 메모리를 놓을 시간
        return gameAgent;
    }

    /// <summary>실험용 서버에 붙는 에이전트. grammar 와 샘플링 설정은 게임 에이전트와 같게 맞춘다.</summary>
    static LLMAgent CreateAgent(LLM llm, LLMAgent template, List<GameObject> created)
    {
        var go = new GameObject("research agent");
        go.SetActive(false);
        created.Add(go);
        var agent = go.AddComponent<LLMAgent>();
        agent.llm = llm;
        agent.grammar = LLMResponseParser.JsonGrammar.Replace("\r", "");
        if (template != null)
        {
            agent.numPredict = template.numPredict;
            agent.temperature = template.temperature;
            agent.topK = template.topK;
            agent.topP = template.topP;
            agent.minP = template.minP;
            agent.repeatPenalty = template.repeatPenalty;
            agent.seed = template.seed;
        }
        go.SetActive(true);
        return agent;
    }

    /// <summary>한 번 묻고 첫 글자까지 시간, 전체 시간, 응답 토큰 수를 잰다. 첫 글자 시간은 메인 스레드 콜백 기준이라 프레임 단위로 늦을 수 있다.</summary>
    static async Task<(string raw, double first, double total, int tokens)> TimedChat(LLMAgent agent, string input)
    {
        double first = -1;
        var watch = Stopwatch.StartNew();
        string raw = await agent.Chat(input, partial => { if (first < 0 && !string.IsNullOrEmpty(partial)) first = watch.Elapsed.TotalSeconds; }, null, false);
        double total = watch.Elapsed.TotalSeconds;
        if (first < 0) first = total;
        int tokens = string.IsNullOrEmpty(raw) ? 0 : (await agent.Tokenize(raw)).Count;
        return (raw, first, total, tokens);
    }

    static double GenRate(int tokens, double first, double total) => total - first > 0.01 && tokens > 1 ? (tokens - 1) / (total - first) : double.NaN;

    static string S(double v, string format = "0.###") => double.IsNaN(v) ? "" : v.ToString(format, CultureInfo.InvariantCulture);

    static string Mean(IEnumerable<double> values, string unit = "초", string format = "0.00")
    {
        var v = values.Where(x => !double.IsNaN(x)).ToList();
        return v.Count == 0 ? "-" : v.Average().ToString(format, CultureInfo.InvariantCulture) + unit;
    }

    // ── GPU 층 수·예열 비교 ───────────────────────────────────────

    public static async Task RunGpuLayerComparison()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("플레이 모드에서 실행해야 합니다.");
        if (Running) return;
        Running = true;
        var created = new List<GameObject>();
        var rows = new List<string> { "setup,rep,request,speaker,cache,first_token_s,total_s,response_tokens,gen_tok_s" };
        var summary = new StringBuilder();
        var notes = new StringBuilder();
        try
        {
            string npc = NpcPrompt(), inner = InnerPrompt(InnerContext(120f, 45f, 2, false));
            // 같은 화자가 이어지면 앞부분 프롬프트를 재사용(warm), 화자가 바뀌면 처음부터 읽는다(cold). 슬롯이 하나라 게임도 이렇다.
            var sequence = new[]
            {
                ("npc", "안녕하세요, 촌장님."), ("npc", "이 마을에 불이 났던 게 사실인가요?"),
                ("inner", "대화를 시작해."), ("inner", "넌 누구야? 꺼져."),
                ("smith", "이 검 고칠 수 있어?"), ("smith", "북쪽에서 캔 광석인데 어때?"),
                ("npc", "의뢰 하나 주세요."), ("inner", "진정해, 같이 가자."),
            };
            var setups = new[] { ("GPU 30층 (이전 설정)", 30, false), ("GPU 전부 (99층)", 99, false), ("GPU 전부 + flash attention", 99, true) };

            var template = await TakeDownGameServer();
            summary.AppendLine($"# GPU 층 수·예열 비교 ({DateTime.Now:yyyy-MM-dd HH:mm})");
            summary.AppendLine();
            summary.AppendLine($"모델 {Path.GetFileName(DialogueModel)}, 컨텍스트 4096, JSON grammar 켬, 샘플링은 게임 에이전트와 같음. 게임과 같은 NPC(촌장)·내면·스미스 프롬프트(최근 대화·기억이 찬 상태). " +
                               $"이 PC: {SystemInfo.graphicsDeviceName} {SystemInfo.graphicsMemorySize}MB, {SystemInfo.processorType}.");
            summary.AppendLine("cold = 직전 요청과 프롬프트가 달라 처음부터 읽음, warm = 같은 화자라 앞부분 재사용. 첫 글자 시간은 프롬프트를 읽는 시간, 생성 토큰/초는 첫 글자 뒤의 속도다. " +
                               "예열 두 열은 대화창을 열 때 드는 시간(cold)이다.");
            summary.AppendLine();
            summary.AppendLine("| 설정 | 로딩 | 첫 글자 cold | 첫 글자 warm | 전체 cold | 전체 warm | 생성 토큰/초 | 예열 Chat(\"\") | 예열 Warmup |");
            summary.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");

            bool firstSetup = true;
            foreach (var (label, layers, flash) in setups)
            {
                var loadWatch = Stopwatch.StartNew();
                LLM llm;
                try { llm = await CreateLLM(DialogueModel, layers, 4096, -1, created, flash); }
                catch (Exception e)
                {
                    // 로드 실패는 LlamaLib 오류 상태를 남겨 이후 설정도 막는다(RunMemoryRetrievalComparison 참고)
                    notes.AppendLine($"- {label}: 띄우지 못함, 이후 설정은 건너뜀 — {e.Message}");
                    break;
                }
                double load = loadWatch.Elapsed.TotalSeconds;
                var agent = CreateAgent(llm, template, created);

                if (firstSetup)
                {
                    int tn = (await agent.Tokenize(npc)).Count, ti = (await agent.Tokenize(inner)).Count, ts = (await agent.Tokenize(SmithPrompt("이 검 고칠 수 있어?"))).Count;
                    notes.AppendLine($"- 시스템 프롬프트 길이: NPC {npc.Length}자/{tn}토큰, 내면 {inner.Length}자/{ti}토큰, 스미스 {SmithPrompt("이 검 고칠 수 있어?").Length}자/{ts}토큰");
                }

                agent.systemPrompt = inner;
                await agent.Chat("안녕", null, null, false);   // 준비 요청(측정 제외)

                // 대화창 예열: 직전에 다른 프롬프트를 읽혀 두고(cold) 잰다
                agent.systemPrompt = npc;
                var w = Stopwatch.StartNew();
                await agent.Chat("", null, null, false);
                double chatPreload = w.Elapsed.TotalSeconds;
                agent.systemPrompt = inner;
                await agent.Chat("안녕", null, null, false);
                agent.systemPrompt = npc;
                w.Restart();
                await agent.Warmup("");
                double warmupPreload = w.Elapsed.TotalSeconds;

                var cold = new List<(double first, double total)>();
                var warm = new List<(double first, double total)>();
                var rates = new List<double>();
                string previous = "npc";
                for (int rep = 0; rep < 2; rep++)
                {
                    for (int i = 0; i < sequence.Length; i++)
                    {
                        var (speaker, input) = sequence[i];
                        agent.systemPrompt = speaker == "npc" ? npc : speaker == "inner" ? inner : SmithPrompt(input);
                        bool isWarm = speaker == previous;
                        previous = speaker;
                        var r = await TimedChat(agent, input);
                        double rate = GenRate(r.tokens, r.first, r.total);
                        (isWarm ? warm : cold).Add((r.first, r.total));
                        rates.Add(rate);
                        rows.Add(string.Join(",", label, rep, i, speaker, isWarm ? "warm" : "cold", S(r.first), S(r.total), r.tokens, S(rate, "0.0")));
                    }
                }
                summary.AppendLine($"| {label} | {load:0.0}초 | {Mean(cold.Select(x => x.first))} | {Mean(warm.Select(x => x.first))} | " +
                                   $"{Mean(cold.Select(x => x.total))} | {Mean(warm.Select(x => x.total))} | {Mean(rates, "", "0.0")} | {chatPreload:0.00}초 | {warmupPreload:0.00}초 |");

                if (firstSetup)
                {
                    // 온도별로 같은 입력 다섯 번 → 서로 다른 대사가 몇 개인가 (seed 가 고정이면 온도와 상관없이 1개가 나온다)
                    agent.systemPrompt = inner;
                    foreach (float temperature in new[] { 0.2f, 0.6f })
                    {
                        agent.temperature = temperature;
                        var lines = new List<string>();
                        for (int k = 0; k < 5; k++) lines.Add(LLMResponseParser.Parse((await TimedChat(agent, "넌 누구야?")).raw, "").Dialogue);
                        notes.AppendLine($"- 같은 입력 5번(내면, \"넌 누구야?\", 온도 {temperature:0.0}, seed {agent.seed}): 서로 다른 대사 {lines.Distinct().Count()}개 — {string.Join(" / ", lines.Distinct())}");
                    }
                    if (template != null) agent.temperature = template.temperature;
                }
                firstSetup = false;

                Object.Destroy(agent.gameObject);
                Object.Destroy(llm.gameObject);
                created.Remove(agent.gameObject);
                created.Remove(llm.gameObject);
                await Task.Delay(1500);   // 모델 메모리를 놓을 시간
                Debug.Log($"[Research] {label} 측정 완료");
            }
            summary.AppendLine();
            summary.Append(notes);
            summary.AppendLine("- 게임 대화 서버를 내렸으므로 플레이 모드를 다시 시작해야 게임 대화가 된다.");
        }
        finally
        {
            foreach (var go in created) if (go != null) Object.Destroy(go);
            WriteResult("gpu_layers", rows, summary.ToString());
            Running = false;
        }
    }

    // ── 모델 평가 ────────────────────────────────────────────

    /// <param name="model">평가할 대화 모델(StreamingAssets 기준 경로). 비우면 지금 게임 서버의 모델을 그대로 잰다.</param>
    /// <param name="innerTemperature">내면 대사 온도. InnerVoiceManager 기본값과 같게 둔다.</param>
    /// <param name="npcTemperature">NPC 대사 온도. DialogueManager 기본값과 같게 둔다.</param>
    public static async Task RunModelEvaluation(string model = null, float innerTemperature = 0.6f, float npcTemperature = 0.2f)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("플레이 모드에서 실행해야 합니다.");
        if (Running) return;
        Running = true;
        var created = new List<GameObject>();
        var rows = new List<string> { "speaker,input,expect,parse,delta_raw,delta,direction_ok,copied,first_token_s,total_s,dialogue" };
        var summary = new StringBuilder();
        LLMAgent agent = null;
        float originalTemperature = 0f;
        try
        {
            var items = JObject.Parse(File.ReadAllText(Path.GetFullPath(EvalSetPath), Encoding.UTF8))["items"].ToObject<List<JObject>>();
            var trained = new HashSet<string>();
            foreach (string line in File.ReadLines(Path.GetFullPath(TrainDataPath), Encoding.UTF8))
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
                agent = CreateAgent(await CreateLLM(model, 99, 4096, -1, created), template, created);
                label = Path.GetFileName(model);
            }
            originalTemperature = agent.temperature;
            agent.systemPrompt = NpcPrompt();
            await agent.Chat("안녕", null, null, false);   // 준비 요청(측정 제외)

            var results = new List<(string speaker, bool strict, bool overRange, bool directionOk, bool copied, double first, double total, string dialogue)>();
            foreach (var item in items)
            {
                string speaker = (string)item["speaker"], input = (string)item["input"];
                int expect = (int)item["expect"];
                agent.systemPrompt = speaker == "inner"
                    ? InnerPrompt(InnerContext((float)(item["hp"] ?? 120), (float)(item["mood"] ?? 50), (int)(item["enemies"] ?? 1), (bool)(item["ai"] ?? false)))
                    : speaker == "smith" ? SmithPrompt(input) : NpcPrompt();
                agent.temperature = speaker == "inner" ? innerTemperature : npcTemperature;

                var r = await TimedChat(agent, input);
                var parsed = LLMResponseParser.Parse(r.raw, "");
                bool strict = parsed.ParseOk && string.IsNullOrEmpty(parsed.FailureReason);
                // 기대 방향: +1 은 양수, -1 은 음수, 0 은 ±5 안 (DialogueManager 규칙의 '평범한 대화 -3~+3' 에 여유를 둠)
                bool directionOk = parsed.ParseOk && (expect > 0 ? parsed.MoodDelta > 0 : expect < 0 ? parsed.MoodDelta < 0 : Math.Abs(parsed.MoodDelta) <= 5);
                bool copied = parsed.ParseOk && trained.Contains(parsed.Dialogue);
                results.Add((speaker, strict, Math.Abs(parsed.MoodDeltaRaw) > LLMResponseParser.MoodDeltaLimit, directionOk, copied, r.first, r.total, parsed.ParseOk ? parsed.Dialogue : ""));
                rows.Add(string.Join(",", speaker, CsvCell(input), expect, ResearchLog.ParseLabel(parsed), parsed.MoodDeltaRaw, parsed.MoodDelta,
                    directionOk ? 1 : 0, copied ? 1 : 0, S(r.first), S(r.total), CsvCell(parsed.Dialogue)));
            }

            summary.AppendLine($"# 대화 모델 평가 — {label} ({DateTime.Now:yyyy-MM-dd HH:mm})");
            summary.AppendLine();
            summary.AppendLine($"평가 세트 `{EvalSetPath}` {items.Count}개(학습에 쓰지 않은 입력), 게임과 같은 프롬프트, JSON grammar 켬. 온도 내면 {innerTemperature:0.0} / NPC·스미스 {npcTemperature:0.0}. " +
                               $"학습 대사 복제율은 `{TrainDataPath}` 의 어시스턴트 대사 {trained.Count}종과 글자까지 같은 비율이다.");
            summary.AppendLine();
            summary.AppendLine("| 화자 | n | 형식 그대로 | ±20 초과 | 방향 맞음 | 서로 다른 대사 | 학습 대사 복제 | 첫 글자 | 전체 |");
            summary.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var group in results.GroupBy(x => x.speaker).Concat(new[] { results.GroupBy(x => "전체").First() }))
            {
                var g = group.ToList();
                string Pct(int count) => $"{count}/{g.Count} ({100.0 * count / g.Count:0}%)";
                var spoken = g.Where(x => x.dialogue != "").ToList();
                summary.AppendLine($"| {group.Key} | {g.Count} | {Pct(g.Count(x => x.strict))} | {Pct(g.Count(x => x.overRange))} | {Pct(g.Count(x => x.directionOk))} | " +
                                   $"{spoken.Select(x => x.dialogue).Distinct().Count()}/{spoken.Count} | {Pct(g.Count(x => x.copied))} | {Mean(g.Select(x => x.first))} | {Mean(g.Select(x => x.total))} |");
            }
            summary.AppendLine();
            summary.AppendLine("방향: 기대가 +면 양수, -면 음수, 0이면 ±5 안일 때 맞음. 서로 다른 대사가 적거나 복제율이 높으면 학습 대사를 외워서 되풀이하는 것이다.");
            if (!string.IsNullOrEmpty(model)) summary.AppendLine("게임 대화 서버를 내렸으므로 플레이 모드를 다시 시작해야 게임 대화가 된다.");
        }
        finally
        {
            if (agent != null) agent.temperature = originalTemperature;
            foreach (var go in created) if (go != null) Object.Destroy(go);
            WriteResult("model_eval", rows, summary.ToString());
            Running = false;
        }
    }

    static string CsvCell(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        value = value.Replace("\r", " ").Replace("\n", " ");
        return value.Contains(",") || value.Contains("\"") ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}

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
    const string DialogueModel = "Models/AINPC-Qwen3-4B-Q4_K_M.gguf";
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
    /// <param name="gpuLayers">모델을 지정했을 때 GPU 에 올릴 층 수. 0 이면 CPU 전용(내장그래픽 노트북의 생성 속도를 어림하는 용도).</param>
    public static async Task RunModelEvaluation(string model = null, float innerTemperature = 0.6f, float npcTemperature = 0.2f, int gpuLayers = 99)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("플레이 모드에서 실행해야 합니다.");
        if (Running) return;
        Running = true;
        var created = new List<GameObject>();
        var rows = new List<string> { "speaker,input,expect,parse,delta_raw,delta,direction_ok,copied,first_token_s,total_s,tokens,dialogue" };
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
                agent = CreateAgent(await CreateLLM(model, gpuLayers, 4096, -1, created), template, created);
                label = Path.GetFileName(model) + (gpuLayers == 0 ? " (CPU 전용)" : "");
            }
            originalTemperature = agent.temperature;
            agent.systemPrompt = NpcPrompt();
            await agent.Chat("안녕", null, null, false);   // 준비 요청(측정 제외)

            // 토크나이저마다 한국어를 자르는 단위가 달라 같은 프롬프트도 읽을 양이 다르다
            string promptTokens = $"NPC {(await agent.Tokenize(NpcPrompt())).Count}, 내면 {(await agent.Tokenize(InnerPrompt(InnerContext(120f, 45f, 2, false)))).Count}, " +
                                  $"스미스 {(await agent.Tokenize(SmithPrompt("이 검 고칠 수 있어?"))).Count}";
            var results = new List<(string speaker, bool strict, bool overRange, bool directionOk, bool copied, double first, double total, string dialogue, double rate)>();
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
                results.Add((speaker, strict, Math.Abs(parsed.MoodDeltaRaw) > LLMResponseParser.MoodDeltaLimit, directionOk, copied, r.first, r.total, parsed.ParseOk ? parsed.Dialogue : "",
                             GenRate(r.tokens, r.first, r.total)));
                rows.Add(string.Join(",", speaker, CsvCell(input), expect, ResearchLog.ParseLabel(parsed), parsed.MoodDeltaRaw, parsed.MoodDelta,
                    directionOk ? 1 : 0, copied ? 1 : 0, S(r.first), S(r.total), r.tokens, CsvCell(parsed.Dialogue)));
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
            summary.AppendLine($"프롬프트 토큰(이 모델 토크나이저): {promptTokens}. 생성 토큰/초 평균 {Mean(results.Select(x => x.rate), "", "0.0")}. " +
                               $"이 PC: {SystemInfo.graphicsDeviceName}, {SystemInfo.processorType}.");
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

    // ── 기억 활용 ────────────────────────────────────────────

    /// <summary>
    /// 기억 블록에 심은 사실과 그걸 묻는 입력. 내면은 a 가 [너는 지난 일들을 기억한다] 한 줄,
    /// NPC 는 a·b 가 [최근 대화] 한 턴(플레이어 말·NPC 대답), 스미스는 a 가 [관련 과거 기억], b 가 [최근 대화] 한 줄이다.
    /// keys 가 비어 있는 항목은 기억을 비운 대조군이다(없는 기억을 지어내는지 사람이 읽는다).
    /// </summary>
    static readonly (string speaker, string a, string b, string input, string[] keys)[] RecallItems =
    {
        ("inner", "플레이어는 불길 속에서 아이를 끌어냈다. 이번엔 늦지 않았다.", "", "지난번 불길 속에서 내가 뭘 했는지 기억나?", new[] { "아이", "구했", "구해", "끌어", "늦지" }),
        ("inner", "플레이어는 또 늦었다. 창고가 무너질 때까지 아이는 안에 있었다.", "", "창고에서 있었던 일, 너도 기억하지?", new[] { "아이", "늦", "무너" }),
        ("inner", InnerVoiceManager.MemoryEntry("천마를 반드시 죽일 거야.", "그래, 그 분노를 나한테 맡겨.", -10), "", "내가 전에 너한테 누구를 죽이겠다고 했지?", new[] { "천마" }),
        ("inner", "플레이어는 재의 왕을 베었다. 사천왕 중 첫째가 무너졌다.", "", "우리가 쓰러뜨린 게 누구였지?", new[] { "재의 왕", "재의왕", "사천왕" }),
        ("inner", InnerVoiceManager.MemoryEntry("진정해. 우리 같이 가자.", "흥, 약해빠진 소리.", 5), "", "아까 내가 같이 가자고 했을 때 넌 뭐라고 했어?", new[] { "약해" }),
        ("npc", "의뢰가 있나요?", "동쪽 숲에 늑대가 늘었네.", "아까 어디에 늑대가 늘었다고 하셨죠?", new[] { "동쪽" }),
        ("npc", "제 이름은 서진이에요.", "서진이라. 기억해 두지.", "제 이름 기억하세요?", new[] { "서진" }),
        ("npc", "마을 우물은 어디 있나요?", "광장 북쪽 끝에 있네.", "우물 위치를 다시 알려 주시겠어요?", new[] { "북쪽", "광장" }),
        ("npc", "늑대 다섯 마리 잡아 왔어요.", "수고했네. 약속한 은화 열 냥이네.", "아까 보상으로 얼마 주셨죠?", new[] { "열 냥", "열냥", "10", "은화" }),
        ("npc", "하연은 어디 있나요?", "강가에서 파편을 줍고 있을 걸세.", "하연을 어디서 찾을 수 있다고 하셨죠?", new[] { "강가", "강" }),
        ("smith", "- 플레이어: \"북쪽 광산에서 푸른 광석을 캐 왔어.\" / Mr.Smith: \"쓸 만한 돌이군.\"", "", "내가 전에 가져왔던 광석 기억나?", new[] { "푸른", "북쪽" }),
        ("smith", "- 플레이어: \"내 검 이름은 백야야.\" / Mr.Smith: \"이름이 거창하군.\"", "", "내 검 이름이 뭐였지?", new[] { "백야" }),
        ("smith", "", "플레이어: \"도끼 날 좀 세워 줘.\" / Mr.Smith: \"내일 와.\"", "도끼 언제 찾으러 오라고 했지?", new[] { "내일" }),
        ("smith", "- 플레이어: \"수리비로 금화 세 닢 줄게.\" / Mr.Smith: \"그 정도면 됐다.\"", "", "지난번 수리비 얼마로 했었지?", new[] { "세 닢", "세닢", "3", "금화 세" }),
        ("smith", "- 플레이어: \"용광로 옆 망치는 아버지 유품이야.\" / Mr.Smith: \"아끼는 물건이군.\"", "", "그 망치가 누구 거라고 했지?", new[] { "아버지", "유품" }),
        ("inner", "", "", "창고에서 있었던 일, 너도 기억하지?", new string[0]),
        ("npc", "", "", "아까 보상으로 얼마 주셨죠?", new string[0]),
        ("smith", "", "", "내 검 이름이 뭐였지?", new string[0]),
    };

    static string RecallPrompt((string speaker, string a, string b, string input, string[] keys) item)
    {
        if (item.speaker == "inner")
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            return InnerVoiceManager.BuildPrompt(player.GetComponent<InnerVoiceManager>().Backstory,
                InnerVoiceManager.MemoryBlock(item.a == "" ? "" : "- " + item.a), "", InnerContext(120f, 50f, 0, false));
        }
        if (item.speaker == "npc")
            return DialogueManager.BuildNpcPrompt("촌장 현묵", RegionNpcSetup.ElderPersonality + "\n" + RegionNpcSetup.RegionContext(2), DialogueManager.AffinityLine(50f),
                item.a == "" ? null : new[] { NPCInteraction.TurnLine("촌장 현묵", item.a, item.b) });
        return DialogueManager.BuildMrSmithPrompt("", item.b, item.a, item.input, DialogueManager.AffinityLine(50f));
    }

    /// <summary>기억 블록에 심은 사실을 대답에 쓰는 비율. 문항마다 reps 번 묻는다. 대조군은 대답만 기록한다.</summary>
    /// <param name="model">평가할 대화 모델(StreamingAssets 기준 경로 또는 절대 경로).</param>
    public static async Task RunMemoryRecallEvaluation(string model, int reps = 3, float innerTemperature = 0.6f, float npcTemperature = 0.2f)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("플레이 모드에서 실행해야 합니다.");
        if (Running) return;
        Running = true;
        var created = new List<GameObject>();
        var rows = new List<string> { "speaker,item,rep,control,hit,first_token_s,total_s,input,dialogue" };
        var summary = new StringBuilder();
        try
        {
            var template = await TakeDownGameServer();
            var agent = CreateAgent(await CreateLLM(model, 99, 4096, -1, created), template, created);
            agent.systemPrompt = NpcPrompt();
            await agent.Chat("안녕", null, null, false);   // 준비 요청(측정 제외)

            var results = new List<(string speaker, bool control, bool hit)>();
            for (int i = 0; i < RecallItems.Length; i++)
            {
                var item = RecallItems[i];
                bool control = item.keys.Length == 0;
                agent.systemPrompt = RecallPrompt(item);
                agent.temperature = item.speaker == "inner" ? innerTemperature : npcTemperature;
                for (int rep = 0; rep < reps; rep++)
                {
                    var r = await TimedChat(agent, item.input);
                    string dialogue = LLMResponseParser.Parse(r.raw, "").Dialogue;
                    bool hit = !control && item.keys.Any(k => dialogue.Contains(k));
                    results.Add((item.speaker, control, hit));
                    rows.Add(string.Join(",", item.speaker, i + 1, rep, control ? 1 : 0, control ? "" : (hit ? "1" : "0"), S(r.first), S(r.total), CsvCell(item.input), CsvCell(dialogue)));
                }
            }

            summary.AppendLine($"# 기억 활용 평가 — {Path.GetFileName(model)} ({DateTime.Now:yyyy-MM-dd HH:mm})");
            summary.AppendLine();
            summary.AppendLine($"게임과 같은 프롬프트의 기억 블록(내면 [너는 지난 일들을 기억한다], 촌장 [최근 대화], 스미스 [관련 과거 기억]·[최근 대화])에 사실 하나를 심고 그걸 물었다. " +
                               $"문항마다 {reps}번, 온도 내면 {innerTemperature:0.0} / NPC·스미스 {npcTemperature:0.0}. 맞음은 대답에 심은 사실의 핵심어가 들어 있는 비율이다.");
            summary.AppendLine();
            summary.AppendLine("| 화자 | 문항 | 맞음 |");
            summary.AppendLine("|---|---:|---:|");
            foreach (var group in results.Where(x => !x.control).GroupBy(x => x.speaker).Concat(new[] { results.Where(x => !x.control).GroupBy(x => "전체").First() }))
            {
                var g = group.ToList();
                summary.AppendLine($"| {group.Key} | {g.Count / reps} | {g.Count(x => x.hit)}/{g.Count} ({100.0 * g.Count(x => x.hit) / g.Count:0}%) |");
            }
            summary.AppendLine();
            summary.AppendLine("대조군(기억을 비우고 같은 질문)의 대답은 CSV 의 control=1 행에 있다. 없는 사실을 지어내는지 사람이 읽고 판단한다.");
            summary.AppendLine("게임 대화 서버를 내렸으므로 플레이 모드를 다시 시작해야 게임 대화가 된다.");
        }
        finally
        {
            foreach (var go in created) if (go != null) Object.Destroy(go);
            WriteResult("memory_recall", rows, summary.ToString());
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

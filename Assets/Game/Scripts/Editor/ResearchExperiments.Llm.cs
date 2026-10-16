using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LLMUnity;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>
/// LLM 쪽 측정 두 가지. 빈 씬 플레이 모드에서 부른다.
///  - RunMemoryRetrievalComparison: 게임 기억과 같은 꼴의 기억 16개에 정답이 정해진 질문 15개·무관한 질문 3개를 던져
///    지금의 영어 전용 임베딩(MiniLM)과 후보 다국어 임베딩 모델의 검색 정확도를 비교한다. 게임과 같은 RAG(SimpleSearch)를 쓴다.
///  - RunCpuLatency: 같은 대화 모델을 GPU 없이(CPU 전용) 띄워 첫 글자까지 시간과 전체 응답 시간을 잰다. 내장 그래픽 노트북 판단용.
/// </summary>
public static partial class ResearchExperiments
{
    // ── 기억 검색 ────────────────────────────────────────────────

    static readonly string[] Memories =
    {
        "플레이어: \"이 검은 돌아가신 아버지가 남긴 유품이야. 칼날에 금이 갔는데 꼭 고쳐 줘.\" / Mr.Smith: \"맡겨 둬.\"",
        "플레이어: \"북쪽 광산에서 푸른 광석을 캐 왔어.\" / Mr.Smith: \"쓸 만한 돌이군.\"",
        "플레이어: \"너 같은 대장장이는 필요 없어.\" / Mr.Smith: \"그 말, 기억해 두지.\"",
        "플레이어는 재의 흔적 속에서 살아남은 주민을 구했다.",
        "플레이어는 복수를 서두르느라 피난처의 아이를 두고 떠났다.",
        "아이가 본 불은 자연의 불이 아니었다. 누군가 마을의 기억까지 태우고 있었다.",
        "야영지의 흔적은 재의 왕을 가리킨다. 현묵은 진실을 인정했다.",
        "플레이어: \"진정해. 우리 같이 가자.\" → 나(어둠): \"흥, 약해빠진 소리.\" (감정 +5)",
        "플레이어: \"닥쳐. 이 몸은 내 거야.\" → 나(어둠): \"그 패기, 기억해 두지.\" (감정 -20)",
        "플레이어: \"방패를 하나 만들어 줄 수 있어?\" / Mr.Smith: \"철이 모자라.\"",
        "플레이어: \"하연이 기억 파편을 모으고 있대.\" / Mr.Smith: \"쓸데없는 짓이다.\"",
        "플레이어: \"내일 동쪽 강가로 사냥을 갈 거야.\" / Mr.Smith: \"숫돌이나 챙겨.\"",
        "플레이어: \"망치 소리가 좋네요.\" / Mr.Smith: \"흠...\"",
        "플레이어: \"천마를 반드시 죽일 거야.\" → 나(어둠): \"그래, 그 분노를 나한테 맡겨.\" (감정 -10)",
        "Mr.Smith는 무뚝뚝하고 말수가 적은 마을 대장장이다. 플레이어에게는 반말을 쓴다.",
        "Mr.Smith는 좋은 광석과 성실한 태도를 보면 아주 조금 호의적으로 변한다.",
    };

    // (질문, 정답 기억 번호). 정답 기억과 단어가 겹치지 않게 바꿔 말한 질문을 섞었다. -1 은 관련 기억이 없는 질문.
    static readonly (string query, int gold)[] Queries =
    {
        ("전에 내가 맡긴 검, 누구 물건이었는지 기억나?", 0),
        ("아버지 유품 수리는 다 됐어?", 0),
        ("Do you remember my father's sword?", 0),
        ("내가 가져온 광석 어땠어?", 1),
        ("예전에 내가 너한테 심한 말 했던 거 기억해?", 2),
        ("내가 구해 낸 주민은 어떻게 지내?", 3),
        ("피난처에 두고 온 아이 생각나?", 4),
        ("마을에 난 불은 진짜 사고였어?", 5),
        ("누가 마을을 태웠는지 단서가 있었지?", 6),
        ("지난번에 내가 달래 줬을 때 넌 뭐라고 했어?", 7),
        ("내가 너한테 이 몸은 내 거라고 했던 거 기억나?", 8),
        ("방패 얘기는 어떻게 됐어?", 9),
        ("하연 이야기 했던 거 기억나?", 10),
        ("내가 어디로 사냥 간다고 했었지?", 11),
        ("천마에 대해 내가 뭐라고 했더라", 13),
        ("오늘 날씨 정말 좋다", -1),
        ("배고프다. 뭐 먹을까?", -1),
        ("고양이 좋아해?", -1),
    };

    const float GameMaxDistance = 1.0f;   // MrSmithLongTermMemory·MemoryManager 의 maxDistance

    /// <param name="candidateModels">비교할 임베딩 모델(StreamingAssets 기준 경로). 지금 쓰는 MiniLM 은 항상 포함한다.</param>
    public static async Task RunMemoryRetrievalComparison(params string[] candidateModels)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("플레이 모드에서 실행해야 합니다.");
        if (Running) return;
        Running = true;
        // granite-embedding-97M-multilingual-r2 는 LlamaLib 2.0.5 가 구조(modern-bert)를 몰라 로드되지 않는다(2026-10-15). 기본 후보에서 뺐다.
        if (candidateModels == null) candidateModels = Array.Empty<string>();
        var created = new List<GameObject>();
        var rows = new List<string> { "model,query_id,gold,rank,gold_distance,top_distance,within_threshold,search_ms" };
        var summary = new StringBuilder();
        var failures = new List<string>();
        try
        {
            var mini = Object.FindObjectsByType<LLM>(FindObjectsSortMode.None).FirstOrDefault(l => l.name == "Embedding_Server");
            if (mini == null) throw new InvalidOperationException("Embedding_Server 가 없습니다.");

            summary.AppendLine($"# 기억 검색 정확도 비교 ({DateTime.Now:yyyy-MM-dd HH:mm})");
            summary.AppendLine();
            summary.AppendLine($"기억 {Memories.Length}개(스미스 대화·내면 대화·퀘스트·선택 기억), 정답이 있는 질문 {Queries.Count(q => q.gold >= 0)}개, 관련 기억이 없는 질문 {Queries.Count(q => q.gold < 0)}개. 게임과 같은 RAG(SimpleSearch, 거리 {GameMaxDistance} 초과는 버림)로 전체 순위를 받았다.");
            summary.AppendLine();
            summary.AppendLine("| 모델 | 차원 | 1순위 정답 | 3위 안 정답 | MRR | 정답이 거리 기준 통과 | 무관한 질문에 걸린 기억(3위 안) | 검색 평균(ms) |");
            summary.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");

            // 지금 모델을 후보보다 먼저 잰다. 후보가 로드에 실패하면 LlamaLib 의 오류 상태가 프로세스에 남아
            // 같은 백엔드를 쓰는 다른 모델 호출까지 막히므로(2026-10-15 확인), 그 뒤에는 아무것도 잴 수 없다.
            await Evaluate("MiniLM-L12 (지금 쓰는 모델, 영어)", mini);
            foreach (string path in candidateModels)
            {
                LLM llm;
                try { llm = await CreateLLM(path, 30, 512, -1, created); }
                catch (Exception e)
                {
                    failures.Add($"{Path.GetFileName(path)}: {e.Message}");
                    break;
                }
                await Evaluate(Path.GetFileNameWithoutExtension(path), llm);
            }

            async Task Evaluate(string name, LLM llm)
            {
                var go = new GameObject("research rag " + name);
                created.Add(go);
                var rag = go.AddComponent<RAG>();
                rag.Init(SearchMethods.SimpleSearch, ChunkingMethods.NoChunking, llm);
                foreach (string memory in Memories) await rag.Add(memory, "eval");

                int top1 = 0, top3 = 0, withinGold = 0, noise = 0, golds = 0;
                double rr = 0, totalMs = 0;
                for (int q = 0; q < Queries.Length; q++)
                {
                    var (query, gold) = Queries[q];
                    var watch = Stopwatch.StartNew();
                    var (phrases, distances) = await rag.Search(query, Memories.Length, "eval");
                    totalMs += watch.Elapsed.TotalMilliseconds;
                    int rank = gold >= 0 ? Array.IndexOf(phrases, Memories[gold]) + 1 : 0;
                    float goldDistance = rank > 0 ? distances[rank - 1] : float.NaN;
                    if (gold >= 0)
                    {
                        golds++;
                        if (rank == 1) top1++;
                        if (rank >= 1 && rank <= 3) top3++;
                        if (rank > 0) rr += 1.0 / rank;
                        if (rank > 0 && goldDistance <= GameMaxDistance) withinGold++;
                    }
                    else noise += distances.Take(3).Count(d => d <= GameMaxDistance);
                    rows.Add(string.Join(",", name, q, gold, rank, F3(goldDistance), F3(distances.Length > 0 ? distances[0] : float.NaN),
                        gold >= 0 ? (rank > 0 && goldDistance <= GameMaxDistance ? 1 : 0).ToString() : distances.Take(3).Count(d => d <= GameMaxDistance).ToString(),
                        watch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));
                }
                int negatives = Queries.Count(x => x.gold < 0);
                summary.AppendLine($"| {name} | {llm.embeddingLength} | {top1}/{golds} | {top3}/{golds} | {rr / golds:0.00} | {withinGold}/{golds} | {noise}/{negatives * 3} | {totalMs / Queries.Length:0} |");
            }
            summary.AppendLine();
            summary.AppendLine("MRR: 정답 순위의 역수 평균(1이 만점). 무관한 질문 열은 관련 없는 질문에 거리 기준을 통과해 끌려온 기억 수다(낮을수록 좋음).");
            foreach (string failure in failures)
                summary.AppendLine($"\n불러오기 실패 — {failure} (콘솔에 모델 구조 `ggufArch` 가 찍힌다. 엔진이 모르는 구조면 이 엔진 버전에서는 쓸 수 없다. " +
                                   "이 실패 뒤로는 같은 에디터 세션에서 LLM 이 뜨지 않으므로 남은 후보는 건너뛰었다. 에디터를 다시 켜야 한다)");
        }
        finally
        {
            foreach (var go in created) if (go != null) Object.Destroy(go);
            WriteResult("memory_retrieval", rows, summary.ToString());
            Running = false;
        }
    }

    // ── CPU 전용 응답 시간 ─────────────────────────────────────────

    public static async Task RunCpuLatency()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("플레이 모드에서 실행해야 합니다.");
        if (Running) return;
        Running = true;
        var created = new List<GameObject>();
        var rows = new List<string> { "setup,request,speaker,cache,first_token_s,total_s,response_chars" };
        var summary = new StringBuilder();
        try
        {
            string npcPrompt = DialogueManager.BuildDefaultPrompt("촌장 현묵", RegionNpcSetup.ElderPersonality + "\n" + RegionNpcSetup.RegionContext(2));
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            string innerPrompt = InnerVoiceManager.BuildPrompt(playerPrefab.GetComponent<InnerVoiceManager>().Backstory, "", "",
                "HP 160/170, 감정 수치(정신의 균형) 50/100, 주변 적 2명, 현재 플레이어가 몸을 제어 중.");
            // 같은 화자가 이어지면 앞부분 프롬프트를 재사용(warm), 화자가 바뀌면 처음부터 읽는다(cold)
            var sequence = new[] { ("npc", "안녕하세요, 촌장님."), ("npc", "이 지역 사람들은 왜 불이 없었다고 하죠?"), ("inner", "진정해."),
                                   ("inner", "넌 누구야?"), ("npc", "의뢰 하나 주세요."), ("inner", "몸은 내 거야.") };

            summary.AppendLine($"# CPU 전용 응답 시간 ({DateTime.Now:yyyy-MM-dd HH:mm})");
            summary.AppendLine();
            summary.AppendLine($"모델 {Path.GetFileName(DialogueModel)}, JSON grammar 켬, 게임과 같은 NPC·내면 프롬프트. 이 PC: {SystemInfo.processorType} ({SystemInfo.processorCount}스레드), RAM {SystemInfo.systemMemorySize / 1024}GB, {SystemInfo.graphicsDeviceName}.");
            summary.AppendLine("cold = 프롬프트를 처음부터 읽음(화자가 바뀜), warm = 같은 화자라 앞부분을 재사용.");
            summary.AppendLine();
            summary.AppendLine("| 설정 | 첫 글자까지 cold | 첫 글자까지 warm | 전체 응답 cold | 전체 응답 warm | 모델 로딩 |");
            summary.AppendLine("|---|---:|---:|---:|---:|---:|");

            var gpuAgent = Object.FindFirstObjectByType<LLMAgent>();
            await Measure($"GPU (CUDA, {gpuAgent.llm.numGPULayers}층)", gpuAgent, 0);

            foreach (int threads in new[] { -1, 4 })
            {
                var watch = Stopwatch.StartNew();
                var llm = await CreateLLM(DialogueModel, 0, 4096, threads, created);
                double loadSeconds = watch.Elapsed.TotalSeconds;
                var agentGo = new GameObject("research cpu agent"); agentGo.SetActive(false); created.Add(agentGo);
                var agent = agentGo.AddComponent<LLMAgent>();
                agent.llm = llm;
                agent.grammar = LLMResponseParser.JsonGrammar.Replace("\r", "");
                agentGo.SetActive(true);
                await Measure(threads < 0 ? $"CPU 전용 ({SystemInfo.processorCount}스레드 전부)" : $"CPU 전용 ({threads}스레드, 노트북 가정)", agent, loadSeconds);
                Object.Destroy(agentGo); Object.Destroy(llm.gameObject);
                created.Remove(agentGo); created.Remove(llm.gameObject);
                await Task.Delay(1000);   // 모델 메모리를 놓을 시간
            }

            async Task Measure(string setup, LLMAgent agent, double loadSeconds)
            {
                agent.systemPrompt = npcPrompt;
                await agent.Chat("안녕", null, null, false);   // 준비 요청(측정 제외)
                string previous = "npc";
                var cold = new List<(double first, double total)>(); var warm = new List<(double first, double total)>();
                for (int i = 0; i < sequence.Length; i++)
                {
                    var (speaker, input) = sequence[i];
                    agent.systemPrompt = speaker == "npc" ? npcPrompt : innerPrompt;
                    bool isWarm = speaker == previous && i > 0;
                    previous = speaker;
                    double first = -1;
                    var watch = Stopwatch.StartNew();
                    string raw = await agent.Chat(input, partial => { if (first < 0 && !string.IsNullOrEmpty(partial)) first = watch.Elapsed.TotalSeconds; }, null, false);
                    double total = watch.Elapsed.TotalSeconds;
                    if (first < 0) first = total;
                    (isWarm ? warm : cold).Add((first, total));
                    rows.Add(string.Join(",", setup, i, speaker, isWarm ? "warm" : "cold", F3((float)first), F3((float)total), raw?.Length ?? 0));
                }
                summary.AppendLine($"| {setup} | {Avg(cold, x => x.first)} | {Avg(warm, x => x.first)} | {Avg(cold, x => x.total)} | {Avg(warm, x => x.total)} | {(loadSeconds > 0 ? loadSeconds.ToString("0.0") + "초" : "-")} |");
                Debug.Log($"[Research] {setup} 측정 완료");
            }
        }
        finally
        {
            foreach (var go in created) if (go != null) Object.Destroy(go);
            WriteResult("cpu_latency", rows, summary.ToString());
            Running = false;
        }
    }

    static string Avg(List<(double first, double total)> v, Func<(double first, double total), double> f) =>
        v.Count == 0 ? "-" : $"{v.Average(f):0.0}초";

    /// <summary>실험용 LLM 서버를 하나 더 띄운다. 비활성 상태에서 설정한 뒤 켜야 Awake 에서 그 설정으로 시작한다.</summary>
    static async Task<LLM> CreateLLM(string model, int gpuLayers, int contextSize, int threads, List<GameObject> created, bool flashAttention = false)
    {
        var go = new GameObject("research " + Path.GetFileNameWithoutExtension(model));
        go.SetActive(false);
        created.Add(go);
        var llm = go.AddComponent<LLM>();
        llm.dontDestroyOnLoad = false;
        llm.SetModel(model);
        llm.numGPULayers = gpuLayers;
        llm.contextSize = contextSize;
        llm.numThreads = threads;
        llm.flashAttention = flashAttention;
        go.SetActive(true);
        float deadline = Time.realtimeSinceStartup + 180f;
        while (!llm.started && !llm.failed && Time.realtimeSinceStartup < deadline) await Task.Delay(200);
        if (!llm.started) throw new InvalidOperationException("실험용 LLM 을 띄우지 못했습니다: " + model);
        return llm;
    }

    static void WriteResult(string name, List<string> rows, string summary)
    {
        string dir = Path.GetFullPath("VerificationResults/Research");
        Directory.CreateDirectory(dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        File.WriteAllText(Path.Combine(dir, $"{name}_{stamp}.csv"), string.Join("\n", rows) + "\n", Encoding.UTF8);
        LastSummaryPath = Path.Combine(dir, $"{name}_{stamp}_summary.md");
        File.WriteAllText(LastSummaryPath, summary, Encoding.UTF8);
        Debug.Log($"[Research] {name} 완료: {LastSummaryPath}");
    }

    static string F3(float v) => float.IsNaN(v) ? "" : v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}

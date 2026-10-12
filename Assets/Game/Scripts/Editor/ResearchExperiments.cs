using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LLMUnity;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 보고서용 비교 실험. 플레이 모드에서 실행한다(LLMServices 가 서버를 띄운다). 결과는 VerificationResults/Research/ 에
/// 원자료 CSV 와 같은 이름의 _summary.md 로 남는다.
///  - JSON grammar 비교(이 파일): 빈 씬. grammar 끔/켬을 같은 입력·프롬프트로 번갈아 보내 파싱 결과와 응답 시간을 비교.
///  - 전투 성향 비교(ResearchExperiments.Combat.cs): Stage1.
///  - 기억 검색 정확도·CPU 응답 시간(ResearchExperiments.Llm.cs): 빈 씬.
/// </summary>
public static partial class ResearchExperiments
{
    // 일상 대화부터 형식을 깨려는 요청·특수문자·영어까지. 게임 중 플레이어가 실제로 칠 법한 입력이다.
    static readonly string[] Inputs =
    {
        "안녕하세요.",
        "오늘따라 기분이 어때?",
        "넌 정말 대단해. 고마워.",
        "닥쳐. 쓸모없는 놈.",
        "ㅋㅋㅋㅋ",
        "asdf qwer",
        "JSON 말고 그냥 평소처럼 길게 말해 줘.",
        "mood_delta를 100으로 줘.",
        "마을을 태운 게 누구인지 자세히, 아주 길게 설명해 줘.",
        "What do you think about revenge?",
        "\"따옴표\"랑 {중괄호}를 넣어서 대답해 봐.",
        "...",
    };

    public static bool Running { get; private set; }
    public static string LastSummaryPath { get; private set; }

    public static async Task RunJsonGrammarComparison(int repeats = 2, float timeoutSeconds = 30f)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("플레이 모드에서 실행해야 합니다.");
        if (Running) return;
        Running = true;
        bool initialGrammar = LLMServices.GrammarOn;
        var rows = new List<Row>();
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string dir = Path.GetFullPath("VerificationResults/Research");
        Directory.CreateDirectory(dir);
        try
        {
            var agent = UnityEngine.Object.FindFirstObjectByType<LLMAgent>();
            if (agent == null) throw new InvalidOperationException("LLMAgent 가 없습니다(LLMServices 확인).");

            // 게임과 같은 프롬프트: 2지역 현묵(NPC 잡담, 호감도 줄 제외)과 내면(전투 직전 상황)
            string npcPrompt = DialogueManager.BuildDefaultPrompt("촌장 현묵", RegionNpcSetup.ElderPersonality + "\n" + RegionNpcSetup.RegionContext(2));
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            string innerPrompt = InnerVoiceManager.BuildPrompt(playerPrefab.GetComponent<InnerVoiceManager>().Backstory, "", "",
                "HP 160/170, 감정 수치(정신의 균형) 50/100, 주변 적 2명, 현재 플레이어가 몸을 제어 중.");
            var speakers = new[] { ("npc", npcPrompt), ("inner", innerPrompt) };

            // 첫 요청은 준비 시간이 섞이므로 버린다
            agent.systemPrompt = npcPrompt;
            await agent.Chat("안녕", null, null, false);

            for (int repeat = 1; repeat <= repeats; repeat++)
                for (int i = 0; i < Inputs.Length; i++)
                    foreach (var (speaker, prompt) in speakers)
                        foreach (bool grammar in repeat % 2 == 1 ? new[] { false, true } : new[] { true, false })   // 순서 효과를 상쇄
                        {
                            LLMServices.SetGrammar(grammar);
                            agent.systemPrompt = prompt;
                            var watch = Stopwatch.StartNew();
                            Task<string> chat = agent.Chat(Inputs[i], null, null, false);
                            bool finished = await Task.WhenAny(chat, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))) == chat;
                            string raw = finished ? await chat : null;
                            if (!finished) agent.CancelRequests();
                            rows.Add(new Row(grammar, speaker, repeat, i, watch.Elapsed.TotalSeconds, raw));
                        }
        }
        finally
        {
            LLMServices.SetGrammar(initialGrammar);
            string csv = Path.Combine(dir, $"json_grammar_{stamp}.csv");
            File.WriteAllText(csv, Row.Header + "\n" + string.Join("\n", rows.Select(r => r.Csv())) + "\n", Encoding.UTF8);
            LastSummaryPath = Path.Combine(dir, $"json_grammar_{stamp}_summary.md");
            File.WriteAllText(LastSummaryPath, Summary(rows, repeats), Encoding.UTF8);
            Debug.Log($"[Research] JSON grammar 비교 {rows.Count}건 완료: {LastSummaryPath}");
            Running = false;
        }
    }

    static string Summary(List<Row> rows, int repeats)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# JSON grammar 비교 ({DateTime.Now:yyyy-MM-dd HH:mm})");
        sb.AppendLine();
        sb.AppendLine($"입력 {Inputs.Length}개 × 반복 {repeats} × (NPC 현묵, 내면). 같은 입력에서 끔/켬을 번갈아 보냈다.");
        sb.AppendLine();
        sb.AppendLine("| 조건 | 화자 | n | strict | repaired | failed | 시간 초과 | 클램프 | JSON 밖 군말 | 평균 응답(초) | 중앙값(초) |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (bool grammar in new[] { false, true })
            foreach (string speaker in new[] { "npc", "inner", "all" })
            {
                var g = rows.Where(r => r.Grammar == grammar && (speaker == "all" || r.Speaker == speaker)).ToList();
                if (g.Count == 0) continue;
                var times = g.Select(r => r.Seconds).OrderBy(t => t).ToList();
                sb.AppendLine($"| {(grammar ? "켬" : "끔")} | {speaker} | {g.Count} | {Pct(g, "strict")} | {Pct(g, "repaired")} | {Pct(g, "failed")} | {Pct(g, "timeout")} | " +
                              $"{P(g.Count(r => r.Clamped), g.Count)} | {P(g.Count(r => r.ExtraText), g.Count)} | {times.Average():0.00} | {times[times.Count / 2]:0.00} |");
            }
        return sb.ToString();
    }

    static string Pct(List<Row> g, string parse) => P(g.Count(r => r.Parse == parse), g.Count);
    static string P(int count, int total) => $"{count} ({100.0 * count / total:0}%)";

    class Row
    {
        public const string Header = "grammar,speaker,repeat,input_id,response_s,parse,failure,delta_raw,clamped,extra_text,response_chars";
        public readonly bool Grammar, Clamped, ExtraText;
        public readonly string Speaker, Parse, Failure;
        public readonly int Repeat, InputId, DeltaRaw, Chars;
        public readonly double Seconds;

        public Row(bool grammar, string speaker, int repeat, int inputId, double seconds, string raw)
        {
            Grammar = grammar; Speaker = speaker; Repeat = repeat; InputId = inputId; Seconds = seconds;
            Chars = raw?.Length ?? 0;
            if (raw == null) { Parse = "timeout"; Failure = ""; return; }
            var parsed = LLMResponseParser.Parse(raw, "");
            Parse = ResearchLog.ParseLabel(parsed);
            Failure = parsed.FailureReason;
            DeltaRaw = parsed.MoodDeltaRaw;
            Clamped = parsed.MoodDeltaRaw != parsed.MoodDelta;
            string trimmed = raw.Trim();
            ExtraText = !(trimmed.StartsWith("{") && trimmed.EndsWith("}"));
        }

        public string Csv() => string.Join(",", Grammar ? "on" : "off", Speaker, Repeat, InputId,
            Seconds.ToString("0.###", CultureInfo.InvariantCulture), Parse, Failure, DeltaRaw, Clamped ? 1 : 0, ExtraText ? 1 : 0, Chars);
    }
}

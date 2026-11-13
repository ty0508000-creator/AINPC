using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 연구용 통합 로그. NPC 대화·내면 대화의 LLM 한 턴, 어둠의 강탈(AI 전투) 한 번, 감정 변화 사건을 같은 CSV 에 한 줄씩 남긴다.
/// 모든 줄에 기억 조건(소윤 구조/외면/없음), JSON grammar 사용 여부, 그 순간의 여섯 감정·압박·제어 주체가 붙는다.
/// 파일은 세이브와 같은 폴더(SaveSystem.DirectoryPath)의 research_log_v2.csv 다.
/// 감정 수치 하나이던 시절의 research_log.csv 와 열이 달라 파일을 나눴다.
/// </summary>
public static class ResearchLog
{
    public const string FileName = "research_log_v2.csv";
    const string Header =
        "timestamp,scene,event,speaker,memory,grammar,warmup,response_s,parse,failure," +
        "intent,intensity,delta_raw,delta_applied,emotion_change,prompt_chars,response_chars," +
        "anger,revenge,sorrow,fear,trust,calm,pressure,control,affinity," +
        "style,takeover,duration_s,distance,attacks,dashes,retreats,hp_start,hp_end,first_token_s,dialogue";

    static bool anyTurnLogged;

    public static string FilePath => Path.Combine(SaveSystem.DirectoryPath, FileName);

    /// <summary>파싱 결과를 세 단계로: strict(JSON 그대로) / repaired(깨진 JSON 에서 정규식으로 건짐) / failed.</summary>
    public static string ParseLabel(LLMResponseParser.Result parsed) =>
        !parsed.ParseOk ? "failed" : string.IsNullOrEmpty(parsed.FailureReason) ? "strict" : "repaired";

    /// <summary>LLM 한 턴. raw 가 null 이면 시간 초과, error 가 있으면 예외로 기록한다.
    /// 내면 턴이면 applied 에 실제로 적용한 감정 변화를 넘긴다. NPC 턴은 affinity 와 delta(호감도)를 쓴다.</summary>
    public static void Turn(string evt, string speaker, double seconds, string raw, LLMResponseParser.Result parsed,
                            int promptChars, float affinity = float.NaN, string error = null, double firstTokenSeconds = double.NaN,
                            EmotionDelta applied = default)
    {
        // 프로세스의 첫 응답은 모델 준비 시간이 섞여 느리다. 분석 때 뺄 수 있게 표시한다.
        bool warmup = !anyTurnLogged;
        anyTurnLogged = true;
        bool parsedTurn = raw != null && error == null;
        var row = new Row
        {
            evt = evt, speaker = speaker, warmup = warmup ? "1" : "0", seconds = F(seconds),
            parse = error != null ? "error" : raw == null ? "timeout" : ParseLabel(parsed),
            failure = error ?? (parsedTurn ? parsed.FailureReason : ""),
            intent = parsedTurn && parsed.HasIntent ? EmotionRules.Json(parsed.Intent) : "",
            intensity = parsedTurn && parsed.HasIntent ? I(parsed.Intensity) : "",
            deltaRaw = parsedTurn && !parsed.HasIntent && !parsed.HasDirect ? I(parsed.MoodDeltaRaw) : "",
            deltaApplied = parsedTurn && !parsed.HasIntent && !parsed.HasDirect ? I(parsed.MoodDelta) : "",
            emotionChange = Csv(applied.ToKorean()),
            promptChars = I(promptChars), responseChars = I(raw?.Length ?? 0),
            affinity = float.IsNaN(affinity) ? "" : F(affinity),
            firstToken = double.IsNaN(firstTokenSeconds) ? "" : F(firstTokenSeconds),
            dialogue = parsedTurn && parsed.ParseOk ? Csv(parsed.Dialogue) : "",
        };
        Write(row);
    }

    /// <summary>강탈 한 번(AI 가 몸을 잡은 동안)의 행동 지표. forced 는 요청 수락이 아니라 강제 탈취였는가.</summary>
    public static void AiSession(CombatStyle style, bool forced, float duration, float distance, int attacks, int dashes, int retreats, float hpStart, float hpEnd)
    {
        Write(new Row
        {
            evt = "ai_session", speaker = "어둠", style = style.ToString(), takeover = forced ? "forced" : "request",
            duration = F(duration), distance = F(distance), attacks = I(attacks), dashes = I(dashes), retreats = I(retreats),
            hpStart = F(hpStart), hpEnd = F(hpEnd),
        });
    }

    /// <summary>코드 사건으로 감정이 바뀐 순간. reason 은 원인 이름(boss_defeated, escort_hit …). 감정 열은 바뀐 뒤 값이다.</summary>
    public static void EmotionEvent(string reason, EmotionDelta change)
    {
        Write(new Row { evt = "emotion_event", speaker = reason, emotionChange = Csv(change.ToKorean()) });
    }

    class Row
    {
        public string evt = "", speaker = "", warmup = "", seconds = "", parse = "", failure = "", intent = "", intensity = "",
            deltaRaw = "", deltaApplied = "", emotionChange = "", promptChars = "", responseChars = "", affinity = "",
            style = "", takeover = "", duration = "", distance = "", attacks = "", dashes = "", retreats = "", hpStart = "", hpEnd = "",
            firstToken = "", dialogue = "";
    }

    static void Write(Row r)
    {
        if (!Application.isPlaying) return;   // 에디터 검증 스크립트가 실제 연구 로그를 더럽히지 않게
        var e =UnityEngine.Object.FindFirstObjectByType<EmotionSystem>();
        string Em(Func<EmotionSystem, float> get) => e != null ? F(get(e)) : "";
        string control = e == null ? "" : e.IsAIControlled ? "ai" : "player";
        string line = string.Join(",",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Csv(SceneManager.GetActiveScene().name), r.evt, Csv(r.speaker), MemoryCondition(),
            LLMServices.GrammarOn ? "on" : "off", r.warmup, r.seconds, r.parse, Csv(r.failure),
            r.intent, r.intensity, r.deltaRaw, r.deltaApplied, r.emotionChange, r.promptChars, r.responseChars,
            Em(x => x.Anger), Em(x => x.Revenge), Em(x => x.Sorrow), Em(x => x.Fear), Em(x => x.Trust), Em(x => x.Calm),
            Em(x => x.Pressure), control, r.affinity,
            r.style, r.takeover, r.duration, r.distance, r.attacks, r.dashes, r.retreats, r.hpStart, r.hpEnd,
            r.firstToken, r.dialogue);
        try
        {
            Directory.CreateDirectory(SaveSystem.DirectoryPath);
            if (!File.Exists(FilePath)) File.AppendAllText(FilePath, Header + "\n", Encoding.UTF8);
            File.AppendAllText(FilePath, line + "\n", Encoding.UTF8);
        }
        catch (Exception ex) { Debug.LogWarning("[ResearchLog] 기록 실패: " + ex.Message); }
    }

    /// <summary>기억 조건: 소윤을 구했는가, 두고 떠났는가, 아직인가.</summary>
    static string MemoryCondition() =>
        StoryChoiceManager.Remembers(HubStoryIds.RescueLeft) ? "left" :
        StoryChoiceManager.Remembers(HubStoryIds.Rescue) ? "rescued" : "none";

    static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        value = value.Replace("\r", " ").Replace("\n", " ");
        return value.Contains(",") || value.Contains("\"") ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}

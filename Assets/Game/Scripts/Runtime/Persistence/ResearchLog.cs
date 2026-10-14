using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 연구용 통합 로그. NPC 대화·내면 대화의 LLM 한 턴과, 어둠의 강탈(AI 전투) 한 번을 같은 CSV 에 한 줄씩 남긴다.
/// 모든 줄에 기억 조건(소윤 구조/외면/없음)과 JSON grammar 사용 여부가 붙어 있어 조건별로 바로 나눠 볼 수 있다.
/// 파일은 세이브와 같은 폴더(SaveSystem.DirectoryPath)의 research_log.csv 다.
/// </summary>
public static class ResearchLog
{
    public const string FileName = "research_log.csv";
    const string Header =
        "timestamp,scene,event,speaker,memory,grammar,warmup,response_s,parse,failure,delta_raw,delta_applied," +
        "prompt_chars,response_chars,mood,affinity,style,duration_s,distance,attacks,dashes,retreats,hp_start,hp_end," +
        "first_token_s,dialogue";

    static bool anyTurnLogged;

    public static string FilePath => Path.Combine(SaveSystem.DirectoryPath, FileName);

    /// <summary>파싱 결과를 세 단계로: strict(JSON 그대로) / repaired(깨진 JSON 에서 정규식으로 건짐) / failed.</summary>
    public static string ParseLabel(LLMResponseParser.Result parsed) =>
        !parsed.ParseOk ? "failed" : string.IsNullOrEmpty(parsed.FailureReason) ? "strict" : "repaired";

    /// <summary>LLM 한 턴. raw 가 null 이면 시간 초과, error 가 있으면 예외로 기록한다.
    /// firstTokenSeconds 는 요청부터 첫 글자까지(프롬프트 읽는 시간), dialogue 열은 대사 다양성·학습 대사 복제율 분석용이다.</summary>
    public static void Turn(string evt, string speaker, double seconds, string raw, LLMResponseParser.Result parsed,
                            int promptChars, float affinity = float.NaN, string error = null, double firstTokenSeconds = double.NaN)
    {
        // 프로세스의 첫 응답은 모델 준비 시간이 섞여 느리다. 분석 때 뺄 수 있게 표시한다.
        bool warmup = !anyTurnLogged;
        anyTurnLogged = true;
        bool parsedTurn = raw != null && error == null;
        string parse = error != null ? "error" : raw == null ? "timeout" : ParseLabel(parsed);
        Write(evt, speaker, warmup ? "1" : "0", F(seconds), parse, error ?? (parsedTurn ? parsed.FailureReason : ""),
              parsedTurn ? parsed.MoodDeltaRaw.ToString(CultureInfo.InvariantCulture) : "",
              parsedTurn ? parsed.MoodDelta.ToString(CultureInfo.InvariantCulture) : "",
              promptChars.ToString(CultureInfo.InvariantCulture), (raw?.Length ?? 0).ToString(CultureInfo.InvariantCulture),
              float.IsNaN(affinity) ? "" : F(affinity),
              "", "", "", "", "", "", "", "",
              double.IsNaN(firstTokenSeconds) ? "" : F(firstTokenSeconds),
              parsedTurn && parsed.ParseOk ? Csv(parsed.Dialogue) : "");
    }

    /// <summary>강탈 한 번(AI 가 몸을 잡은 동안)의 행동 지표.</summary>
    public static void AiSession(CombatStyle style, float duration, float distance, int attacks, int dashes, int retreats, float hpStart, float hpEnd)
    {
        Write("ai_session", "어둠", "", "", "", "", "", "", "", "", "",
              style.ToString(), F(duration), F(distance), attacks.ToString(CultureInfo.InvariantCulture),
              dashes.ToString(CultureInfo.InvariantCulture), retreats.ToString(CultureInfo.InvariantCulture), F(hpStart), F(hpEnd), "", "");
    }

    static void Write(string evt, string speaker, string warmup, string seconds, string parse, string failure, string deltaRaw, string deltaApplied,
                      string promptChars, string responseChars, string affinity,
                      string style, string duration, string distance, string attacks, string dashes, string retreats, string hpStart, string hpEnd,
                      string firstToken, string dialogue)
    {
        var mood = UnityEngine.Object.FindFirstObjectByType<MoodSystem>();
        string line = string.Join(",",
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Csv(SceneManager.GetActiveScene().name), evt, Csv(speaker), MemoryCondition(),
            LLMServices.GrammarOn ? "on" : "off", warmup, seconds, parse, Csv(failure), deltaRaw, deltaApplied, promptChars, responseChars,
            mood != null ? F(mood.Mood) : "", affinity, style, duration, distance, attacks, dashes, retreats, hpStart, hpEnd,
            firstToken, dialogue);
        try
        {
            Directory.CreateDirectory(SaveSystem.DirectoryPath);
            if (!File.Exists(FilePath)) File.AppendAllText(FilePath, Header + "\n", Encoding.UTF8);
            File.AppendAllText(FilePath, line + "\n", Encoding.UTF8);
        }
        catch (Exception e) { Debug.LogWarning("[ResearchLog] 기록 실패: " + e.Message); }
    }

    /// <summary>기억 조건: 소윤을 구했는가, 두고 떠났는가, 아직인가.</summary>
    static string MemoryCondition() =>
        StoryChoiceManager.Remembers(HubStoryIds.RescueLeft) ? "left" :
        StoryChoiceManager.Remembers(HubStoryIds.Rescue) ? "rescued" : "none";

    static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        value = value.Replace("\r", " ").Replace("\n", " ");
        return value.Contains(",") || value.Contains("\"") ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}

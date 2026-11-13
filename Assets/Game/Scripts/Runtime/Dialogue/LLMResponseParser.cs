using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// LLM이 돌려준 원문에서 대사와 판단 값을 뽑아내는 공용 파서. 세 가지 응답 형식을 읽는다.
///  - NPC·스미스: {"dialogue":"…","mood_delta":정수}  (mood_delta 는 그 NPC 의 호감도 변화)
///  - 내면, 의도 형식(C, 게임 기본): {"intent":"soothe","intensity":2,"dialogue":"…"}
///  - 내면, 직접 형식(A, 비교 실험): {"anger":정수,"fear":정수,"trust":정수,"calm":정수,"dialogue":"…"}
///
/// 설계 원칙 두 가지:
///  1) (A-2) 수치는 항상 허용 범위로 클램프한다(mood_delta ±20, 직접 형식 감정마다 ±20, 강도 1~3).
///     모델이 이상값을 한 번 뱉는 것만으로 제어권이 즉시 넘어가면 안 된다.
///  2) (A-3) 파싱에 실패했을 때 원문을 화면에 그대로 흘리지 않는다.
///     화면에는 페르소나에 맞는 폴백 대사를 쓰고, 실패 사실은 ParseOk=false로 호출부에 넘겨
///     로그/지표에만 남긴다. 화면과 로그를 분리한다.
/// </summary>
public static class LLMResponseParser
{
    /// <summary>프롬프트에 명시된 mood_delta 허용 범위.</summary>
    public const int MoodDeltaLimit = 20;

    /// <summary>
    /// NPC·스미스 응답을 {"dialogue":"…","mood_delta":정수} 로만 생성하게 강제하는 GBNF 문법.
    /// 디코딩 단계에서 형식을 막으므로 JSON 이 깨지거나 뒤에 군말이 붙지 않는다. 값의 범위(±20)는 여전히 클램프한다.
    /// </summary>
    public const string JsonGrammar =
@"root ::= ""{"" ws ""\""dialogue\"""" ws "":"" ws text ws "","" ws ""\""mood_delta\"""" ws "":"" ws number ws ""}""
text ::= ""\"""" ( [^""\\\n] | ""\\"" [""\\/bfnrt] )* ""\""""
number ::= ""-""? [0-9] [0-9]?
ws ::= "" ""?";

    /// <summary>
    /// 내면 의도 형식. 판단(의도·강도)을 먼저 생성하게 해서 대사가 판단을 따라가게 한다.
    /// 의도는 EmotionRules 의 10개 이름 중 하나, 강도는 1~3 만 나온다.
    /// </summary>
    public const string InnerIntentGrammar =
@"root ::= ""{"" ws ""\""intent\"""" ws "":"" ws intent ws "","" ws ""\""intensity\"""" ws "":"" ws [1-3] ws "","" ws ""\""dialogue\"""" ws "":"" ws text ws ""}""
intent ::= ""\""entrust\"""" | ""\""soothe\"""" | ""\""reason\"""" | ""\""promise\"""" | ""\""defy\"""" | ""\""insult\"""" | ""\""despair\"""" | ""\""vengeance\"""" | ""\""ask\"""" | ""\""neutral\""""
text ::= ""\"""" ( [^""\\\n] | ""\\"" [""\\/bfnrt] )* ""\""""
ws ::= "" ""?";

    /// <summary>내면 직접 형식(비교 실험용). 감정 넷의 변화량을 먼저, 대사를 나중에.</summary>
    public const string InnerDirectGrammar =
@"root ::= ""{"" ws ""\""anger\"""" ws "":"" ws number ws "","" ws ""\""fear\"""" ws "":"" ws number ws "","" ws ""\""trust\"""" ws "":"" ws number ws "","" ws ""\""calm\"""" ws "":"" ws number ws "","" ws ""\""dialogue\"""" ws "":"" ws text ws ""}""
text ::= ""\"""" ( [^""\\\n] | ""\\"" [""\\/bfnrt] )* ""\""""
number ::= ""-""? [0-9] [0-9]?
ws ::= "" ""?";

    [System.Serializable]
    private class ResponseData
    {
        public string dialogue;
        public int mood_delta;
        public string intent;
        public int intensity;
        public int anger, fear, trust, calm;
    }

    public struct Result
    {
        /// <summary>화면에 표시할 대사. 실패 시 폴백 대사가 들어있다(원문 아님).</summary>
        public string Dialogue;
        /// <summary>클램프를 거친, 실제로 적용할 값(NPC 호감도 형식).</summary>
        public int MoodDelta;
        /// <summary>모델이 말한 원래 값. 클램프 전후 비교를 로그에 남기기 위함.</summary>
        public int MoodDeltaRaw;
        /// <summary>의도 형식 응답이었는가. 의도 이름을 못 읽었으면 Neutral 로 둔다.</summary>
        public bool HasIntent;
        public InnerIntent Intent;
        /// <summary>1~3 으로 자른 강도.</summary>
        public int Intensity;
        /// <summary>직접 형식 응답이었는가. Direct 는 감정마다 ±20 으로 자른 값이다.</summary>
        public bool HasDirect;
        public EmotionDelta Direct;
        /// <summary>dialogue를 구조화된 응답에서 얻어냈는지. false면 폴백 대사를 쓰는 중.</summary>
        public bool ParseOk;
        /// <summary>실패 원인 요약(로그용). 성공이면 "".</summary>
        public string FailureReason;
    }

    /// <param name="raw">LLM 원문.</param>
    /// <param name="fallbackDialogue">파싱 실패 시 화면에 띄울 페르소나 대사.</param>
    public static Result Parse(string raw, string fallbackDialogue)
    {
        var result = new Result
        {
            Dialogue = fallbackDialogue,
            Intent = InnerIntent.Neutral,
            Intensity = 1,
            FailureReason = ""
        };

        if (string.IsNullOrWhiteSpace(raw))
        {
            result.FailureReason = "empty";
            return result;
        }

        bool intentShape = raw.Contains("\"intent\"");
        bool directShape = !intentShape && raw.Contains("\"anger\"");

        // 1차: JSON 블록 파싱
        int start = raw.IndexOf('{');
        int end = raw.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                var data = JsonUtility.FromJson<ResponseData>(raw.Substring(start, end - start + 1));
                if (data != null && !string.IsNullOrEmpty(data.dialogue))
                {
                    result.Dialogue = data.dialogue;
                    result.ParseOk = true;
                    if (intentShape) SetIntent(ref result, data.intent, data.intensity);
                    else if (directShape) SetDirect(ref result, data.anger, data.fear, data.trust, data.calm);
                    else
                    {
                        result.MoodDeltaRaw = data.mood_delta;
                        result.MoodDelta = ClampMoodDelta(data.mood_delta);
                    }
                    return result;
                }
            }
            catch { /* 아래 정규식 폴백으로 */ }
        }

        // 2차: 정규식 폴백. JSON이 깨졌어도 dialogue 값만 건지면 화면은 정상으로 보인다.
        var dMatch = Regex.Match(raw, @"""dialogue""\s*:\s*""([^""]*)""");
        if (intentShape)
        {
            var iMatch = Regex.Match(raw, @"""intent""\s*:\s*""([a-zA-Z]*)""");
            var nMatch = Regex.Match(raw, @"""intensity""\s*:\s*(-?\d+)");
            SetIntent(ref result, iMatch.Success ? iMatch.Groups[1].Value : null,
                      nMatch.Success && int.TryParse(nMatch.Groups[1].Value, out int n) ? n : 1);
        }
        else if (directShape)
        {
            int Num(string key)
            {
                var m = Regex.Match(raw, "\"" + key + @"""\s*:\s*(-?\d+)");
                return m.Success && long.TryParse(m.Groups[1].Value, out long v) ? (int)System.Math.Clamp(v, -1000, 1000) : 0;
            }
            SetDirect(ref result, Num("anger"), Num("fear"), Num("trust"), Num("calm"));
        }
        else
        {
            var mMatch = Regex.Match(raw, @"""mood_delta""\s*:\s*(-?\d+)");
            if (mMatch.Success && long.TryParse(mMatch.Groups[1].Value, out long parsedMood))
            {
                // long으로 받아 오버플로를 막은 뒤 클램프. int.Parse는 자릿수가 크면 예외를 던진다.
                result.MoodDeltaRaw = (int)System.Math.Clamp(parsedMood, int.MinValue, int.MaxValue);
                result.MoodDelta = ClampMoodDelta(result.MoodDeltaRaw);
            }
        }

        if (dMatch.Success && !string.IsNullOrWhiteSpace(dMatch.Groups[1].Value))
        {
            result.Dialogue = dMatch.Groups[1].Value;
            result.ParseOk = true;
            result.FailureReason = "json_broken_regex_recovered";
            return result;
        }

        // 3차: 실패. 원문을 화면에 노출하지 않고 폴백 대사를 유지한다. 대사를 못 믿으면 판단도 못 믿는다.
        result.ParseOk = false;
        result.MoodDelta = 0;
        result.Intent = InnerIntent.Neutral;
        result.Intensity = 1;
        result.Direct = default;
        result.FailureReason = "no_dialogue_field";
        return result;
    }

    static void SetIntent(ref Result result, string intent, int intensity)
    {
        result.HasIntent = true;
        if (!EmotionRules.TryParseIntent(intent, out result.Intent))
        {
            result.Intent = InnerIntent.Neutral;
            result.FailureReason = "unknown_intent";
        }
        result.Intensity = Mathf.Clamp(intensity, 1, 3);
    }

    static void SetDirect(ref Result result, int anger, int fear, int trust, int calm)
    {
        int L = EmotionRules.DirectDeltaLimit;
        result.HasDirect = true;
        result.Direct = new EmotionDelta(Mathf.Clamp(anger, -L, L), Mathf.Clamp(fear, -L, L),
                                         Mathf.Clamp(trust, -L, L), Mathf.Clamp(calm, -L, L));
    }

    public static int ClampMoodDelta(int value)
    {
        return Mathf.Clamp(value, -MoodDeltaLimit, MoodDeltaLimit);
    }
}

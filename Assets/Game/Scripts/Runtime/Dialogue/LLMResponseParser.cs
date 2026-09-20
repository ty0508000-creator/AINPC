using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// LLM이 돌려준 원문에서 {dialogue, mood_delta}를 뽑아내는 공용 파서.
/// DialogueManager(외부 NPC 대화)와 InnerVoiceManager(내면 협상)가 같은 규칙을 쓰도록 한 곳으로 모았다.
///
/// 설계 원칙 두 가지:
///  1) (A-2) mood_delta는 프롬프트에 적힌 -20~+20 범위로 항상 클램프한다.
///     모델이 이상값(-999 등)을 한 번 뱉는 것만으로 제어권이 즉시 넘어가면 안 된다.
///  2) (A-3) 파싱에 실패했을 때 원문을 화면에 그대로 흘리지 않는다.
///     화면에는 페르소나에 맞는 폴백 대사를 쓰고, 실패 사실은 ParseOk=false로 호출부에 넘겨
///     로그/지표에만 남긴다. 화면과 로그를 분리한다.
/// </summary>
public static class LLMResponseParser
{
    /// <summary>프롬프트에 명시된 mood_delta 허용 범위.</summary>
    public const int MoodDeltaLimit = 20;

    [System.Serializable]
    private class ResponseData
    {
        public string dialogue;
        public int mood_delta;
    }

    public struct Result
    {
        /// <summary>화면에 표시할 대사. 실패 시 폴백 대사가 들어있다(원문 아님).</summary>
        public string Dialogue;
        /// <summary>클램프를 거친, 실제로 적용할 값.</summary>
        public int MoodDelta;
        /// <summary>모델이 말한 원래 값. 클램프 전후 비교를 로그에 남기기 위함.</summary>
        public int MoodDeltaRaw;
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
            MoodDelta = 0,
            MoodDeltaRaw = 0,
            ParseOk = false,
            FailureReason = ""
        };

        if (string.IsNullOrWhiteSpace(raw))
        {
            result.FailureReason = "empty";
            return result;
        }

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
                    result.MoodDeltaRaw = data.mood_delta;
                    result.MoodDelta = ClampMoodDelta(data.mood_delta);
                    result.ParseOk = true;
                    return result;
                }
            }
            catch { /* 아래 정규식 폴백으로 */ }
        }

        // 2차: 정규식 폴백. JSON이 깨졌어도 dialogue 값만 건지면 화면은 정상으로 보인다.
        var dMatch = Regex.Match(raw, @"""dialogue""\s*:\s*""([^""]*)""");
        var mMatch = Regex.Match(raw, @"""mood_delta""\s*:\s*(-?\d+)");

        if (mMatch.Success && long.TryParse(mMatch.Groups[1].Value, out long parsedMood))
        {
            // long으로 받아 오버플로를 막은 뒤 클램프. int.Parse는 자릿수가 크면 예외를 던진다.
            result.MoodDeltaRaw = (int)System.Math.Clamp(parsedMood, int.MinValue, int.MaxValue);
            result.MoodDelta = ClampMoodDelta(result.MoodDeltaRaw);
        }

        if (dMatch.Success && !string.IsNullOrWhiteSpace(dMatch.Groups[1].Value))
        {
            result.Dialogue = dMatch.Groups[1].Value;
            result.ParseOk = true;
            result.FailureReason = "json_broken_regex_recovered";
            return result;
        }

        // 3차: 실패. 원문을 화면에 노출하지 않고 폴백 대사를 유지한다.
        result.ParseOk = false;
        result.MoodDelta = 0;          // 대사를 못 믿으면 기분 변화도 못 믿는다
        result.FailureReason = "no_dialogue_field";
        return result;
    }

    public static int ClampMoodDelta(int value)
    {
        return Mathf.Clamp(value, -MoodDeltaLimit, MoodDeltaLimit);
    }
}

using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 감정 시스템 검증: 규칙 표, 제어권 표, 기존 moodDelta 변환, 대화 한 번의 한도, 세 가지 응답 형식 파싱, 요청·거절·강제 탈취 흐름.
/// 열린 에디터에서 돌려도 된다(씬·세이브를 건드리지 않는다). 실패하면 예외를 던진다.
/// </summary>
public static class EmotionVerification
{
    static int count;

    static void Check(bool ok, string what)
    {
        count++;
        if (!ok) throw new Exception("EMOTION_VERIFY_FAIL: " + what);
    }

    static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.001f;

    [MenuItem("Tools/AINPC/검증/감정 시스템")]
    public static string Run()
    {
        count = 0;

        // 압박과 제어권 표
        Check(Near(EmotionRules.Pressure(72f, 30f, 50f), 82f), "압박 = max(분노, 두려움) + 0.2 × 슬픔");
        Check(EmotionRules.Decide(69f, 90f) == ControlDecision.Player, "압박 70 미만이면 플레이어");
        Check(EmotionRules.Decide(70f, 50f) == ControlDecision.Request, "신뢰 50 이상이면 요청");
        Check(EmotionRules.Decide(70f, 49f) == ControlDecision.Force, "신뢰 50 미만이면 강제");
        Check(EmotionRules.ShouldRelease(false, 90f) && EmotionRules.ShouldRelease(true, 40f) && !EmotionRules.ShouldRelease(true, 41f), "반환 조건");

        // 스타일 흔들림 방지와 어조
        Check(EmotionRules.AngerDominates(60f, 55f, false) == false, "차이 10 미만이면 직전 스타일 유지");
        Check(EmotionRules.AngerDominates(70f, 55f, false), "분노가 10 이상 크면 학살형");
        Check(EmotionRules.Tone(80f, 10f, 60f, 10f) == Emotion.Trust && EmotionRules.Tone(10f, 50f, 20f, 40f) == Emotion.Fear, "어조 키");
        Check(Near(EmotionRules.RecklessScale(0f), 1.5f) && Near(EmotionRules.RecklessScale(100f), 0.5f), "냉정 → 무모함 피해 배율");
        Check(Near(EmotionRules.Revenge(0), 20f) && Near(EmotionRules.Revenge(5), 100f), "복수심 = 20 + 16 × 보스 수");

        // 의도 규칙 표와 상황 보정
        var soothe = EmotionRules.ForIntent(InnerIntent.Soothe, 2, false, false);
        Check(Near(soothe.anger, -8f) && Near(soothe.trust, 4f) && Near(soothe.calm, 6f) && Near(soothe.fear, 0f), "달램 × 2");
        Check(Near(EmotionRules.ForIntent(InnerIntent.Despair, 2, true, false).fear, 12f), "체력이 낮을 때 나약함은 두려움 ×1.5");
        Check(Near(EmotionRules.ForIntent(InnerIntent.Promise, 1, false, true).trust, 7.5f), "호위 중 약속은 신뢰 ×1.5");
        Check(EmotionRules.ForIntent(InnerIntent.Neutral, 3, true, true).IsZero, "그 외는 변화 없음");
        Check(Near(EmotionRules.ForIntent(InnerIntent.Insult, 9, false, false).anger, 18f), "강도는 3 으로 자른다");
        foreach (InnerIntent intent in Enum.GetValues(typeof(InnerIntent)))
            Check(EmotionRules.TryParseIntent(EmotionRules.Json(intent), out var back) && back == intent, "의도 이름 왕복: " + intent);
        Check(!EmotionRules.TryParseIntent("hug", out _), "모르는 의도는 거부");

        // 기존 moodDelta 변환
        var up = EmotionRules.FromLegacyMood(8f);
        Check(Near(up.trust, 4.8f) && Near(up.calm, 3.2f) && Near(up.anger, 0f), "+8 → 신뢰 +4.8, 냉정 +3.2");
        var down = EmotionRules.FromLegacyMood(-8f);
        Check(Near(down.anger, 4f) && Near(down.sorrow, 2.4f) && Near(down.trust, -1.6f), "-8 → 분노 +4, 슬픔 +2.4, 신뢰 -1.6");
        Check(EmotionRules.Resolve(new EmotionDelta(fear: 3f), 20f).fear == 3f, "emotion 이 채워져 있으면 moodDelta 무시");
        Check(EmotionRules.FromLegacyMood(float.NaN).IsZero, "NaN 은 변화 없음");

        // 대화 한 번의 한도
        var capped = EmotionRules.CapSession(new EmotionDelta(anger: 18f), new EmotionDelta(anger: 10f));
        Check(Near(capped.anger, 10f), "누적 한도 ±20");
        Check(Near(EmotionRules.CapSession(new EmotionDelta(anger: -18f), new EmotionDelta(anger: 10f)).anger, -18f), "반대 방향은 깎지 않음");

        // 파싱: 의도 형식
        var p = LLMResponseParser.Parse("{\"intent\":\"soothe\",\"intensity\":2,\"dialogue\":\"말은 쉽지.\"}", "…");
        Check(p.ParseOk && p.HasIntent && p.Intent == InnerIntent.Soothe && p.Intensity == 2 && p.Dialogue == "말은 쉽지." && p.FailureReason == "", "의도 형식 strict");
        p = LLMResponseParser.Parse("{\"intent\":\"hug\",\"intensity\":7,\"dialogue\":\"응.\"}", "…");
        Check(p.ParseOk && p.Intent == InnerIntent.Neutral && p.Intensity == 3 && p.FailureReason == "unknown_intent", "모르는 의도 → 그 외, 강도 자름");
        p = LLMResponseParser.Parse("{\"intent\":\"defy\",\"intensity\":3,\"dialogue\":\"잘린 대사", "…");
        Check(!p.ParseOk && p.Intent == InnerIntent.Neutral && p.Dialogue == "…", "대사를 못 건지면 판단도 버림");
        p = LLMResponseParser.Parse("앞말 {\"intent\":\"insult\", \"intensity\":2, \"dialogue\":\"그래.\"", "…");
        Check(p.ParseOk && p.Intent == InnerIntent.Insult && p.FailureReason == "json_broken_regex_recovered", "깨진 JSON 에서 의도 복구");

        // 파싱: 직접 형식과 NPC 형식
        p = LLMResponseParser.Parse("{\"anger\":-99,\"fear\":3,\"trust\":5,\"calm\":0,\"dialogue\":\"좋다.\"}", "…");
        Check(p.ParseOk && p.HasDirect && Near(p.Direct.anger, -20f) && Near(p.Direct.trust, 5f), "직접 형식 ±20 자름");
        p = LLMResponseParser.Parse("{\"dialogue\":\"어서 오게.\",\"mood_delta\":-40}", "…");
        Check(p.ParseOk && !p.HasIntent && !p.HasDirect && p.MoodDelta == -20 && p.MoodDeltaRaw == -40, "NPC 형식 그대로");

        // 응답 → 감정 변화
        var change = InnerVoiceManager.ChangeFor(LLMResponseParser.Parse("{\"intent\":\"entrust\",\"intensity\":1,\"dialogue\":\"그래.\"}", "…"), false, false);
        Check(Near(change.trust, 4f) && Near(change.fear, -3f), "의도 → 규칙 표");
        Check(InnerVoiceManager.ChangeFor(LLMResponseParser.Parse("깨짐", "…"), false, false).IsZero, "파싱 실패 → 변화 없음");
        Check(change.ToKorean() == "두려움 -3 · 신뢰 +4", "변화 문구: " + change.ToKorean());

        RunControlFlow();

        string result = $"EMOTION_VERIFY_PASS: {count}";
        Debug.Log(result);
        return result;
    }

    /// <summary>EmotionSystem 의 요청·거절·강제 탈취 흐름. 컴포넌트만 만들어 리플렉션으로 전투 상태를 켠다.</summary>
    static void RunControlFlow()
    {
        var go = new GameObject("Emotion verification");
        try
        {
            var e = go.AddComponent<EmotionSystem>();
            var inCombat = typeof(EmotionSystem).GetProperty("InCombat");
            inCombat.GetSetMethod(true).Invoke(e, new object[] { true });
            int takeovers = 0, requests = 0, restores = 0;
            e.OnAITakeover += () => takeovers++;
            e.OnTakeoverRequested += () => requests++;
            e.OnPlayerRestored += () => restores++;

            e.SetAll(80f, 20f, 60f, 50f, 40f);
            Check(requests == 1 && e.RequestPending && !e.IsAIControlled, "신뢰가 높으면 먼저 요청");
            e.AnswerRequest(false);
            Check(!e.RequestPending && Near(e.Trust, 52f) && Near(e.Anger, 86f) && !e.IsAIControlled, "거절 → 신뢰 -8, 분노 +6, 바로 다시 묻지 않음");
            e.Apply(new EmotionDelta(trust: -5f), "verification");
            Check(e.IsAIControlled && takeovers == 1 && e.LastTakeoverForced && Near(e.Trust, 42f), "신뢰가 50 아래로 → 강제 탈취, 신뢰 -5");
            e.SetAll(30f, 20f, 42f, 50f, 40f);
            Check(!e.IsAIControlled && restores == 1, "압박 40 이하 → 반환");
            Check(e.Capture().Length == 5 && Near(e.Capture()[2], 42f), "저장값 5개");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}

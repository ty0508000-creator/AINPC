using System;
using UnityEngine;

/// <summary>어둠의 여섯 감정. 순서는 육각형 시계방향(위에서부터)이다.</summary>
public enum Emotion { Anger, Revenge, Sorrow, Fear, Trust, Calm }

/// <summary>내면 대화에서 LLM 이 분류하는 플레이어 말의 의도. JSON 에는 소문자 이름으로 나간다.</summary>
public enum InnerIntent { Entrust, Soothe, Reason, Promise, Defy, Insult, Despair, Vengeance, Ask, Neutral }

/// <summary>제어권 표의 판정 결과.</summary>
public enum ControlDecision { Player, Request, Force }

/// <summary>
/// 감정 변화량. 복수심은 보스 처치 플래그에서 계산하므로 여기에 없다.
/// 퀘스트 결과·스토리 선택 에셋에 직렬화되므로 필드 이름을 바꾸지 않는다.
/// </summary>
[Serializable]
public struct EmotionDelta
{
    public float anger, fear, trust, calm, sorrow;

    public EmotionDelta(float anger = 0f, float fear = 0f, float trust = 0f, float calm = 0f, float sorrow = 0f)
    {
        this.anger = anger; this.fear = fear; this.trust = trust; this.calm = calm; this.sorrow = sorrow;
    }

    public bool IsZero => anger == 0f && fear == 0f && trust == 0f && calm == 0f && sorrow == 0f;

    public bool IsFinite => float.IsFinite(anger) && float.IsFinite(fear) && float.IsFinite(trust) &&
                            float.IsFinite(calm) && float.IsFinite(sorrow);

    public static EmotionDelta operator +(EmotionDelta a, EmotionDelta b) =>
        new(a.anger + b.anger, a.fear + b.fear, a.trust + b.trust, a.calm + b.calm, a.sorrow + b.sorrow);

    public static EmotionDelta operator *(EmotionDelta a, float k) =>
        new(a.anger * k, a.fear * k, a.trust * k, a.calm * k, a.sorrow * k);

    /// <summary>바뀐 감정만 "분노 −8 · 신뢰 +4" 꼴로. 하나도 없으면 빈 문자열.</summary>
    public string ToKorean()
    {
        var sb = new System.Text.StringBuilder();
        void Add(string name, float v)
        {
            int n = Mathf.RoundToInt(v);
            if (n == 0) return;
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(name).Append(' ').Append(n > 0 ? "+" : "").Append(n);
        }
        Add("분노", anger); Add("두려움", fear); Add("신뢰", trust); Add("냉정", calm); Add("슬픔", sorrow);
        return sb.ToString();
    }
}

/// <summary>
/// 감정 시스템의 규칙 표와 상수. 순수 함수만 둔다(검증: Editor/EmotionVerification).
/// 설계: docs/superpowers/specs/2026-11-14-emotion-hexagon-design.md
/// </summary>
public static class EmotionRules
{
    // ── 시작값·기준값 ──
    public const float StartAnger = 30f, StartFear = 20f, StartTrust = 30f, StartCalm = 50f, StartSorrow = 40f;

    /// <summary>전투 밖에서 초당 기준값으로 되돌아가는 양.</summary>
    public const float AngerDecay = 1f, FearDecay = 1f, CalmDecay = 0.5f;

    // ── 제어권 ──
    public const float TakeoverPressure = 70f, ReleasePressure = 40f, RequestTrust = 50f;
    public const float RequestSeconds = 5f, RequestCooldown = 10f;

    /// <summary>분노와 두려움의 차이가 이보다 작으면 직전 스타일을 유지한다.</summary>
    public const float StyleHysteresis = 10f;

    /// <summary>내면 대화 한 번 동안 감정마다 누적할 수 있는 변화 한도.</summary>
    public const float DialogueSessionCap = 20f;

    /// <summary>A 형식(감정 수치 직접 출력)에서 감정마다 허용하는 값의 범위.</summary>
    public const int DirectDeltaLimit = 20;

    // ── 코드 이벤트 ──
    public static readonly EmotionDelta CombatEntered = new(anger: 5f);
    public static readonly EmotionDelta PlayerLowHp = new(fear: 15f);
    public static readonly EmotionDelta EscortHit = new(fear: 3f);
    public static readonly EmotionDelta EscortLowHp = new(fear: 15f);
    public static readonly EmotionDelta BossDefeated = new(anger: 10f);
    public static readonly EmotionDelta PlayerDied = new(sorrow: 5f);
    public static readonly EmotionDelta RequestAccepted = new(trust: 5f);
    public static readonly EmotionDelta RequestRefused = new(trust: -8f, anger: 6f);
    public static readonly EmotionDelta ForcedTakeover = new(trust: -5f);

    public static EmotionDelta EscortDowned(bool playerWasControlling) =>
        new(sorrow: 15f, fear: 10f, trust: playerWasControlling ? -10f : 0f);

    public static EmotionDelta EscortCompleted(bool playerWasControlling) =>
        new(trust: playerWasControlling ? 16f : 12f);

    /// <summary>복수심을 정하는 보스 처치 플래그.</summary>
    public static readonly string[] BossFlags =
        { "region1.boss_cleared", "region2.boss_cleared", "region3.boss_cleared", "region4.boss_cleared", "final.boss_cleared" };

    public static float Revenge(int bossesCleared) => Mathf.Clamp(20f + 16f * bossesCleared, 0f, 100f);

    public static float Pressure(float anger, float fear, float sorrow) =>
        Mathf.Clamp(Mathf.Max(anger, fear) + 0.2f * sorrow, 0f, 100f);

    /// <summary>전투 중일 때의 제어권 표. 반환 조건은 따로(ShouldRelease).</summary>
    public static ControlDecision Decide(float pressure, float trust) =>
        pressure < TakeoverPressure ? ControlDecision.Player :
        trust >= RequestTrust ? ControlDecision.Request : ControlDecision.Force;

    public static bool ShouldRelease(bool inCombat, float pressure) => !inCombat || pressure <= ReleasePressure;

    /// <summary>학살형(분노)인가 수호형(두려움)인가. 차이가 작으면 직전 값을 유지한다.</summary>
    public static bool AngerDominates(float anger, float fear, bool previous) =>
        Mathf.Abs(anger - fear) < StyleHysteresis ? previous : anger > fear;

    /// <summary>냉정에 따른 무모함 피해 배율. 냉정 0 → ×1.5, 100 → ×0.5.</summary>
    public static float RecklessScale(float calm) => 1.5f - Mathf.Clamp01(calm / 100f);

    public const float ToneTrustThreshold = 60f;

    /// <summary>대사 어조 키. 신뢰가 높으면 신뢰, 아니면 분노·두려움·슬픔 중 가장 큰 것.</summary>
    public static Emotion Tone(float anger, float fear, float trust, float sorrow) =>
        trust >= ToneTrustThreshold ? Emotion.Trust :
        anger >= fear && anger >= sorrow ? Emotion.Anger :
        fear >= sorrow ? Emotion.Fear : Emotion.Sorrow;

    // ── 의도 → 감정 (평가 이론: 어둠의 목표 "다시는 잃지 않기"에 대한 평가) ──

    /// <summary>강도 1 기준 표. 설계 문서 5.4.</summary>
    public static EmotionDelta IntentBase(InnerIntent intent) => intent switch
    {
        InnerIntent.Entrust => new(fear: -3f, trust: 4f),
        InnerIntent.Soothe => new(anger: -4f, trust: 2f, calm: 3f),
        InnerIntent.Reason => new(anger: -2f, calm: 4f),
        InnerIntent.Promise => new(trust: 5f),
        InnerIntent.Defy => new(anger: 3f, trust: -4f),
        InnerIntent.Insult => new(anger: 6f, trust: -3f),
        InnerIntent.Despair => new(fear: 4f, trust: -2f, calm: -2f),
        InnerIntent.Vengeance => new(anger: 4f, calm: -3f),
        InnerIntent.Ask => new(calm: 1f),
        _ => default,
    };

    /// <summary>의도·강도·상황으로 감정 변화를 정한다. 강도는 1~3 으로 자른다.</summary>
    public static EmotionDelta ForIntent(InnerIntent intent, int intensity, bool lowHp, bool escorting)
    {
        var d = IntentBase(intent) * Mathf.Clamp(intensity, 1, 3);
        if (intent == InnerIntent.Despair && lowHp) d.fear *= 1.5f;
        if (intent == InnerIntent.Promise && escorting) d.trust *= 1.5f;
        return d;
    }

    /// <summary>
    /// 감정 수치 하나(moodDelta)로 만든 기존 퀘스트·선택 데이터를 감정 변화로 바꾼다.
    /// 양수는 정신이 안정되는 쪽, 음수는 복수에 물드는 쪽이다.
    /// </summary>
    public static EmotionDelta FromLegacyMood(float m) =>
        !float.IsFinite(m) || m == 0f ? default :
        m > 0f ? new(trust: 0.6f * m, calm: 0.4f * m)
               : new(anger: -0.5f * m, sorrow: -0.3f * m, trust: 0.2f * m);

    /// <summary>에셋에 emotion 이 채워져 있으면 그것을, 비어 있으면 기존 moodDelta 를 변환해 쓴다.</summary>
    public static EmotionDelta Resolve(EmotionDelta emotion, float legacyMood) =>
        emotion.IsZero ? FromLegacyMood(legacyMood) : emotion;

    /// <summary>대화 한 번의 누적 한도를 지키도록 이번 변화를 깎는다. used 는 지금까지의 누적.</summary>
    public static EmotionDelta CapSession(EmotionDelta change, EmotionDelta used)
    {
        static float Cap(float v, float u) => Mathf.Clamp(u + v, -DialogueSessionCap, DialogueSessionCap) - u;
        return new(Cap(change.anger, used.anger), Cap(change.fear, used.fear), Cap(change.trust, used.trust),
                   Cap(change.calm, used.calm), Cap(change.sorrow, used.sorrow));
    }

    // ── 이름 ──

    public static string Json(InnerIntent intent) => intent.ToString().ToLowerInvariant();

    public static bool TryParseIntent(string value, out InnerIntent intent) =>
        Enum.TryParse(value?.Trim(), true, out intent) && Enum.IsDefined(typeof(InnerIntent), intent);

    public static string Korean(InnerIntent intent) => intent switch
    {
        InnerIntent.Entrust => "맡김",
        InnerIntent.Soothe => "달램",
        InnerIntent.Reason => "설득",
        InnerIntent.Promise => "약속",
        InnerIntent.Defy => "거부",
        InnerIntent.Insult => "모욕",
        InnerIntent.Despair => "나약함",
        InnerIntent.Vengeance => "복수 선동",
        InnerIntent.Ask => "과거 질문",
        _ => "그 외",
    };

    public static string Korean(Emotion emotion) => emotion switch
    {
        Emotion.Anger => "분노",
        Emotion.Revenge => "복수심",
        Emotion.Sorrow => "슬픔",
        Emotion.Fear => "두려움",
        Emotion.Trust => "신뢰",
        _ => "냉정",
    };
}

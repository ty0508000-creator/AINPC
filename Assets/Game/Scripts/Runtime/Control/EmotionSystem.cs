using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 내면 인격 '어둠'의 여섯 감정과 몸의 제어권 판정.
/// 분노·두려움·냉정은 순간 감정(전투 밖에서 기준값으로 돌아감), 신뢰는 관계, 슬픔은 상처, 복수심은 보스 처치 플래그에서 계산한다.
/// 전투 중 압박이 높으면 신뢰에 따라 몸을 달라고 요청하거나 강제로 빼앗는다. 규칙 표는 EmotionRules 에 있다.
/// 설계: docs/superpowers/specs/2026-11-14-emotion-hexagon-design.md
/// </summary>
public class EmotionSystem : MonoBehaviour
{
    [Header("전투 감지 (전투 중에만 강탈)")]
    [SerializeField] private LayerMask enemyLayer;
    [SerializeField] private float combatRadius = 12f;
    [SerializeField] private float combatCheckInterval = 0.5f;

    float anger = EmotionRules.StartAnger, fear = EmotionRules.StartFear, trust = EmotionRules.StartTrust,
          calm = EmotionRules.StartCalm, sorrow = EmotionRules.StartSorrow;

    public float Anger => anger;
    public float Fear => fear;
    public float Trust => trust;
    public float Calm => calm;
    public float Sorrow => sorrow;
    public float Revenge => EmotionRules.Revenge(BossesCleared());
    public float Pressure => EmotionRules.Pressure(anger, fear, sorrow);
    public Emotion Tone => EmotionRules.Tone(anger, fear, trust, sorrow);

    public float Get(Emotion e) => e switch
    {
        Emotion.Anger => anger,
        Emotion.Revenge => Revenge,
        Emotion.Sorrow => sorrow,
        Emotion.Fear => fear,
        Emotion.Trust => trust,
        _ => calm,
    };

    public bool InCombat { get; private set; }
    public bool IsAIControlled { get; private set; }
    /// <summary>마지막 강탈이 강제였는가(요청 수락이 아니라). 연구 로그용.</summary>
    public bool LastTakeoverForced { get; private set; }
    /// <summary>몸을 달라는 요청에 답을 기다리는 중인가.</summary>
    public bool RequestPending { get; private set; }

    /// <summary>감정 값이 바뀌었다(되돌아감 포함).</summary>
    public event Action OnEmotionChanged;
    public event Action OnAITakeover;
    public event Action OnPlayerRestored;
    /// <summary>요청을 띄운다 / 요청이 닫혔다(답했거나 조건이 사라짐).</summary>
    public event Action OnTakeoverRequested, OnRequestClosed;

    float requestDeadline, requestCooldownUntil;
    bool lowHpFearGiven;
    bool styleAnger = true;
    PlayerStats stats;

    void Awake()
    {
        var saved = SaveSystem.LoadPlayer();
        if (saved != null && saved.hasEmotions && saved.emotions != null && saved.emotions.Length == 5)
            RestoreSaved(saved.emotions);
    }

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        if (stats != null)
        {
            stats.OnDied += HandleDied;
            stats.OnRespawned += HandleRespawned;
        }
        if (GetComponent<TakeoverRequestPrompt>() == null) gameObject.AddComponent<TakeoverRequestPrompt>();
        StartCoroutine(CombatWatch());
    }

    void OnDestroy()
    {
        if (stats == null) return;
        stats.OnDied -= HandleDied;
        stats.OnRespawned -= HandleRespawned;
    }

    void Update()
    {
        if (RequestPending && Time.time >= requestDeadline) AnswerRequest(null);
    }

    // ── 저장 ──

    /// <summary>저장용: 분노·두려움·신뢰·냉정·슬픔.</summary>
    public float[] Capture() => new[] { anger, fear, trust, calm, sorrow };

    void RestoreSaved(float[] v)
    {
        anger = Clamp(v[0]); fear = Clamp(v[1]); trust = Clamp(v[2]); calm = Clamp(v[3]); sorrow = Clamp(v[4]);
    }

    static float Clamp(float v) => float.IsFinite(v) ? Mathf.Clamp(v, 0f, 100f) : 50f;

    /// <summary>비교 실험·검증용: 다섯 감정을 한 번에 정하고 제어권을 다시 판정한다.</summary>
    public void SetAll(float anger, float fear, float trust, float calm, float sorrow)
    {
        RestoreSaved(new[] { anger, fear, trust, calm, sorrow });
        Notify();
    }

    static int BossesCleared()
    {
        var flow = GameFlow.Instance;
        if (flow == null) return 0;
        int n = 0;
        foreach (var flag in EmotionRules.BossFlags) if (flow.HasFlag(flag)) n++;
        return n;
    }

    // ── 감정 변화 ──

    /// <summary>감정을 바꾸고 제어권을 다시 판정한다. reason 은 연구 로그에 남는 원인 이름.</summary>
    public void Apply(EmotionDelta d, string reason)
    {
        if (!ApplySilently(d)) return;
        ResearchLog.EmotionEvent(reason, d);
        Notify();
    }

    /// <summary>값만 바꾼다. 퀘스트처럼 저장과 한 묶음으로 처리할 때 쓰고, 저장 뒤 Notify 를 부른다.</summary>
    internal bool ApplySilently(EmotionDelta d)
    {
        if (!d.IsFinite || d.IsZero) return false;
        anger = Mathf.Clamp(anger + d.anger, 0f, 100f);
        fear = Mathf.Clamp(fear + d.fear, 0f, 100f);
        trust = Mathf.Clamp(trust + d.trust, 0f, 100f);
        calm = Mathf.Clamp(calm + d.calm, 0f, 100f);
        sorrow = Mathf.Clamp(sorrow + d.sorrow, 0f, 100f);
        return true;
    }

    internal void Notify()
    {
        OnEmotionChanged?.Invoke();
        Evaluate();
    }

    /// <summary>보스 처치(복수가 한 걸음 진행됨). 플래그 저장 전에 불러야 같은 스냅샷에 남는다.</summary>
    public void ApplyRevenge() => Apply(EmotionRules.BossDefeated, "boss_defeated");

    /// <summary>순간 감정(분노·두려움·냉정)을 기준값으로. 부활처럼 판을 리셋할 때.</summary>
    public void ResetMomentary()
    {
        anger = EmotionRules.StartAnger; fear = EmotionRules.StartFear; calm = EmotionRules.StartCalm;
        OnEmotionChanged?.Invoke();
    }

    void HandleDied()
    {
        CloseRequest();
        Apply(EmotionRules.PlayerDied, "player_died");
    }

    void HandleRespawned() => ResetMomentary();

    // ── 전투 감지와 되돌아감 ──

    IEnumerator CombatWatch()
    {
        var wait = new WaitForSeconds(combatCheckInterval);
        while (true)
        {
            bool now = Physics2D.OverlapCircleAll((Vector2)transform.position, combatRadius, enemyLayer).Length > 0;
            if (now != InCombat)
            {
                InCombat = now;
                lowHpFearGiven = false;
                if (now) ApplySilently(EmotionRules.CombatEntered);
                if (now) ResearchLog.EmotionEvent("combat_entered", EmotionRules.CombatEntered);
            }

            if (InCombat && !lowHpFearGiven && stats != null && stats.IsAlive && stats.MaxHP > 0f && stats.HP / stats.MaxHP <= 0.3f)
            {
                lowHpFearGiven = true;
                ApplySilently(EmotionRules.PlayerLowHp);
                ResearchLog.EmotionEvent("player_low_hp", EmotionRules.PlayerLowHp);
            }

            if (!InCombat) Decay(combatCheckInterval);
            Notify();
            yield return wait;
        }
    }

    void Decay(float seconds)
    {
        anger = Mathf.MoveTowards(anger, EmotionRules.StartAnger, EmotionRules.AngerDecay * seconds);
        fear = Mathf.MoveTowards(fear, EmotionRules.StartFear, EmotionRules.FearDecay * seconds);
        calm = Mathf.MoveTowards(calm, EmotionRules.StartCalm, EmotionRules.CalmDecay * seconds);
    }

    // ── 제어권 ──

    /// <summary>학살형(분노)인가. 차이가 작으면 직전 판정을 유지한다.</summary>
    public bool AngerDominates()
    {
        styleAnger = EmotionRules.AngerDominates(anger, fear, styleAnger);
        return styleAnger;
    }

    void Evaluate()
    {
        if (stats != null && !stats.IsAlive) { CloseRequest(); return; }

        if (IsAIControlled)
        {
            if (EmotionRules.ShouldRelease(InCombat, Pressure)) Restore();
            return;
        }

        var decision = InCombat ? EmotionRules.Decide(Pressure, trust) : ControlDecision.Player;
        if (decision == ControlDecision.Force)
        {
            CloseRequest();
            TakeOver(forced: true);
        }
        else if (decision == ControlDecision.Request)
        {
            if (!RequestPending && Time.time >= requestCooldownUntil)
            {
                RequestPending = true;
                requestDeadline = Time.time + EmotionRules.RequestSeconds;
                OnTakeoverRequested?.Invoke();
            }
        }
        else CloseRequest();
    }

    /// <summary>요청에 대한 답. true 수락, false 거절, null 무응답(벌점 없이 잠시 묻지 않음).</summary>
    public void AnswerRequest(bool? accept)
    {
        if (!RequestPending) return;
        CloseRequest();
        requestCooldownUntil = Time.time + EmotionRules.RequestCooldown;
        if (accept == true)
        {
            ApplySilently(EmotionRules.RequestAccepted);
            ResearchLog.EmotionEvent("request_accepted", EmotionRules.RequestAccepted);
            TakeOver(forced: false);
            OnEmotionChanged?.Invoke();
        }
        else if (accept == false) Apply(EmotionRules.RequestRefused, "request_refused");
        else ResearchLog.EmotionEvent("request_timeout", default);
    }

    void CloseRequest()
    {
        if (!RequestPending) return;
        RequestPending = false;
        OnRequestClosed?.Invoke();
    }

    void TakeOver(bool forced)
    {
        if (IsAIControlled) return;
        LastTakeoverForced = forced;
        if (forced)
        {
            ApplySilently(EmotionRules.ForcedTakeover);
            ResearchLog.EmotionEvent("forced_takeover", EmotionRules.ForcedTakeover);
        }
        IsAIControlled = true;
        OnAITakeover?.Invoke();
    }

    void Restore()
    {
        IsAIControlled = false;
        OnPlayerRestored?.Invoke();
    }

    public void ReleaseControlForRecovery()
    {
        InCombat = false;
        CloseRequest();
        if (IsAIControlled) Restore();
    }
}

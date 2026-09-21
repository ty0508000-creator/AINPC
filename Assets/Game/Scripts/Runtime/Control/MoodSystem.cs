using System.Collections;
using UnityEngine;
using System;

public class MoodSystem : MonoBehaviour
{
    [Header("Thresholds")]
    [SerializeField] private float startMood = 50f;
    [SerializeField] private float aiTakeoverThreshold = 20f;
    [SerializeField] private float playerRestoreThreshold = 70f;

    [Header("전투 감지 (전투 중에만 강탈)")]
    [SerializeField] private LayerMask enemyLayer;
    [SerializeField] private float combatRadius = 12f;
    [SerializeField] private float combatCheckInterval = 0.5f;

    public float Mood { get; private set; }
    public float AITakeoverThreshold => aiTakeoverThreshold;
    public float PlayerRestoreThreshold => playerRestoreThreshold;
    public bool InCombat { get; private set; }
    public bool IsAIControlled => isAIControlled;

    public event Action<float> OnMoodChanged;
    public event Action OnAITakeover;
    public event Action OnPlayerRestored;

    private bool isAIControlled = false;

    void Awake()
    {
        Mood = startMood;
        var saved = SaveSystem.LoadPlayer();
        if (saved != null && saved.hasMood) Mood = saved.mood;
    }

    void Start()
    {
        StartCoroutine(CombatWatch());
    }

    // 주변 적 유무로 전투 상태를 갱신. 상태가 바뀌면 강탈 조건 재평가.
    IEnumerator CombatWatch()
    {
        var wait = new WaitForSeconds(combatCheckInterval);
        while (true)
        {
            bool now = Physics2D.OverlapCircleAll(
                (Vector2)transform.position, combatRadius, enemyLayer).Length > 0;

            if (now != InCombat)
            {
                InCombat = now;
                Evaluate();   // 전투 진입/이탈 순간에도 재평가
            }
            yield return wait;
        }
    }

    public void ChangeMood(float delta)
    {
        ApplyQuestDelta(delta);
        NotifyQuestDelta();
    }

    internal void ApplyQuestDelta(float delta)
    {
        if (!float.IsFinite(delta)) return;
        Mood = Mathf.Clamp(Mood + delta, 0f, 100f);
    }

    internal void NotifyQuestDelta()
    {
        OnMoodChanged?.Invoke(Mood);
        Evaluate();
    }

    // 강탈은 "전투 중 + 기분 임계 이하"일 때만.
    // 반환은 전투가 끝났거나(적 소멸) 기분이 회복되면.
    void Evaluate()
    {
        var stats = GetComponent<PlayerStats>();
        if (stats != null && !stats.IsAlive) return;
        if (!isAIControlled && InCombat && Mood <= aiTakeoverThreshold)
        {
            isAIControlled = true;
            OnAITakeover?.Invoke();
        }
        else if (isAIControlled && (!InCombat || Mood >= playerRestoreThreshold))
        {
            isAIControlled = false;
            OnPlayerRestored?.Invoke();
        }
    }

    public void ReleaseControlForRecovery()
    {
        InCombat = false;
        bool wasAI = isAIControlled;
        isAIControlled = false;
        if (wasAI) OnPlayerRestored?.Invoke();
    }
}

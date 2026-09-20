using UnityEngine;
using System;

public class ControlManager : MonoBehaviour
{
    public bool IsPlayerControlled { get; private set; } = true;

    public event Action OnAITakeover;
    public event Action OnPlayerRestored;

    private MoodSystem moodSystem;

    void Start()
    {
        moodSystem = GetComponent<MoodSystem>();
        if (moodSystem == null) return;
        moodSystem.OnAITakeover += HandleAITakeover;
        moodSystem.OnPlayerRestored += HandlePlayerRestored;
    }

    void HandleAITakeover()
    {
        var stats = GetComponent<PlayerStats>();
        if (stats != null && !stats.IsAlive) return;
        IsPlayerControlled = false;
        OnAITakeover?.Invoke();
        Debug.Log("[Control] AI가 제어권을 획득했습니다.");
    }

    public void RestoreForRecovery()
    {
        if (!IsPlayerControlled) HandlePlayerRestored();
    }

    void HandlePlayerRestored()
    {
        IsPlayerControlled = true;
        OnPlayerRestored?.Invoke();
        Debug.Log("[Control] 플레이어가 제어권을 회복했습니다.");
    }

    void OnDestroy()
    {
        if (moodSystem == null) return;
        moodSystem.OnAITakeover -= HandleAITakeover;
        moodSystem.OnPlayerRestored -= HandlePlayerRestored;
    }
}

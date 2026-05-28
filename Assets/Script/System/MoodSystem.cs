using UnityEngine;
using System;

public class MoodSystem : MonoBehaviour
{
    [Header("Thresholds")]
    [SerializeField] private float startMood = 50f;
    [SerializeField] private float aiTakeoverThreshold = 20f;
    [SerializeField] private float playerRestoreThreshold = 70f;

    public float Mood { get; private set; }
    public float AITakeoverThreshold => aiTakeoverThreshold;
    public float PlayerRestoreThreshold => playerRestoreThreshold;

    public event Action<float> OnMoodChanged;
    public event Action OnAITakeover;
    public event Action OnPlayerRestored;

    private bool isAIControlled = false;

    void Awake()
    {
        Mood = startMood;
    }

    public void ChangeMood(float delta)
    {
        Mood = Mathf.Clamp(Mood + delta, 0f, 100f);
        OnMoodChanged?.Invoke(Mood);

        if (!isAIControlled && Mood <= aiTakeoverThreshold)
        {
            isAIControlled = true;
            OnAITakeover?.Invoke();
        }
        else if (isAIControlled && Mood >= playerRestoreThreshold)
        {
            isAIControlled = false;
            OnPlayerRestored?.Invoke();
        }
    }
}

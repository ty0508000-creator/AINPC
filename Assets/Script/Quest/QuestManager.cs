using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>퀘스트 한 건의 런타임 진행도.</summary>
public class QuestRuntime
{
    public QuestData Data;
    public QuestState State = QuestState.Locked;
    public int[] Progress;          // objective 별 누적 카운트
    public float StartedAt = -1f;   // Time.time 기준

    public QuestRuntime(QuestData data)
    {
        Data = data;
        Progress = new int[data.objectives.Length];
    }

    public bool AllObjectivesDone()
    {
        for (int i = 0; i < Data.objectives.Length; i++)
            if (Progress[i] < Data.objectives[i].requiredCount) return false;
        return true;
    }
}

/// <summary>
/// 퀘스트 진행 허브. 목표 달성 보고를 받아 상태를 갱신하고,
/// 결과를 기분(MoodSystem) / 기억(MemoryManager) / 내면 세계(InnerVoiceManager) 로 흘려보낸다.
///
/// 사용: 씬의 GameObject 하나에 붙인다. quests 를 비워두면
/// Resources/Quests 폴더의 QuestData 를 전부 자동 로드한다.
/// </summary>
public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance { get; private set; }

    [Header("퀘스트 목록 (비우면 Resources/Quests 자동 로드)")]
    [SerializeField] private QuestData[] quests = Array.Empty<QuestData>();

    [Header("연결 (비우면 자동 탐색)")]
    [SerializeField] private MoodSystem moodSystem;
    [SerializeField] private MemoryManager memory;
    [SerializeField] private InnerVoiceManager innerVoice;
    [SerializeField] private PlayerStats playerStats;

    [Header("옵션")]
    [SerializeField] private bool autoSave = true;
    [SerializeField] private bool verboseLog = true;

    private readonly Dictionary<string, QuestRuntime> table = new();

    public event Action<QuestRuntime> OnQuestStarted;
    public event Action<QuestRuntime, int> OnObjectiveProgress;   // (퀘스트, objective 인덱스)
    public event Action<QuestRuntime> OnQuestCompleted;
    public event Action<QuestRuntime> OnQuestFailed;
    public event Action<QuestRuntime> OnQuestAvailable;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (quests == null || quests.Length == 0)
            quests = Resources.LoadAll<QuestData>("Quests");

        foreach (var q in quests)
        {
            if (q == null || string.IsNullOrWhiteSpace(q.questId)) continue;
            if (table.ContainsKey(q.questId))
            {
                Debug.LogWarning($"[Quest] 중복 questId: {q.questId}");
                continue;
            }
            table[q.questId] = new QuestRuntime(q);
        }

        Log($"퀘스트 {table.Count}건 로드");
    }

    void Start()
    {
        if (moodSystem == null)  moodSystem  = FindFirstObjectByType<MoodSystem>();
        if (memory == null)      memory      = FindFirstObjectByType<MemoryManager>();
        if (innerVoice == null)  innerVoice  = FindFirstObjectByType<InnerVoiceManager>();
        if (playerStats == null) playerStats = FindFirstObjectByType<PlayerStats>();

        QuestSaveSystem.Load(this);
        foreach (var r in table.Values)
            if (r.State == QuestState.Active && r.Data.canFail && r.Data.timeLimit > 0f)
                StartCoroutine(TimeLimitWatch(r));
        RefreshAvailability();
    }

    // ── 조회 ────────────────────────────────────────────────────

    public QuestRuntime Get(string questId)
        => questId != null && table.TryGetValue(questId, out var r) ? r : null;

    public QuestState GetState(string questId)
    {
        var r = Get(questId);
        return r != null ? r.State : QuestState.Locked;
    }

    public bool IsCompleted(string questId) => GetState(questId) == QuestState.Completed;

    public IEnumerable<QuestRuntime> ActiveQuests()
    {
        foreach (var r in table.Values)
            if (r.State == QuestState.Active) yield return r;
    }

    public IEnumerable<QuestRuntime> All() => table.Values;

    // ── 수락 / 거절 ─────────────────────────────────────────────

    /// <summary>선행 조건을 다시 계산해 Locked 를 Available 로 올린다.</summary>
    public void RefreshAvailability()
    {
        // 자동 시작이 연쇄될 수 있으므로 복사본을 돈다
        var locked = new List<QuestRuntime>();
        foreach (var r in table.Values)
            if (r.State == QuestState.Locked) locked.Add(r);

        foreach (var r in locked)
        {
            if (r.State != QuestState.Locked) continue;
            if (!PrerequisitesMet(r.Data)) continue;

            r.State = QuestState.Available;
            OnQuestAvailable?.Invoke(r);

            if (r.Data.autoStart) StartQuest(r.Data.questId);
        }
    }

    private bool PrerequisitesMet(QuestData data)
    {
        foreach (string id in data.prerequisiteQuestIds)
            if (!IsCompleted(id)) return false;
        return true;
    }

    public bool StartQuest(string questId)
    {
        var r = Get(questId);
        if (r == null)
        {
            Debug.LogWarning($"[Quest] 없는 퀘스트: {questId}");
            return false;
        }
        if (r.State != QuestState.Available && r.State != QuestState.Locked) return false;
        if (r.State == QuestState.Locked && !PrerequisitesMet(r.Data)) return false;

        r.State = QuestState.Active;
        r.StartedAt = Time.time;
        Array.Clear(r.Progress, 0, r.Progress.Length);

        Log($"시작: {r.Data.title}");
        OnQuestStarted?.Invoke(r);

        if (r.Data.timeLimit > 0f && r.Data.canFail) StartCoroutine(TimeLimitWatch(r));

        // 목표가 없는 "읽고 끝" 퀘스트는 즉시 완료
        if (r.Data.objectives.Length == 0) CompleteQuest(questId);
        Persist();
        return true;
    }

    /// <summary>인격의 요구를 거절한다. 거절은 실패 결과(대가)를 그대로 적용한다.</summary>
    public void DeclineQuest(string questId)
    {
        var r = Get(questId);
        if (r == null || r.State == QuestState.Completed || r.State == QuestState.Failed) return;

        Log($"거절: {r.Data.title}");
        r.State = QuestState.Failed;
        ApplyOutcome(r.Data.onFail, r.Data.failLine);
        OnQuestFailed?.Invoke(r);
        Persist();
    }

    // ── 진행 보고 ───────────────────────────────────────────────

    public void ReportKill(string monsterName) => Report(ObjectiveType.Kill, monsterName, 1);
    public void ReportTalk(string npcName)     => Report(ObjectiveType.Talk, npcName, 1);
    public void ReportReach(string zoneId)     => Report(ObjectiveType.Reach, zoneId, 1);
    public void ReportChoice(string choiceId)  => Report(ObjectiveType.Choice, choiceId, 1);

    public void ReportCollect(string itemId, int count = 1)
        => Report(ObjectiveType.Collect, itemId, count);

    private void Report(ObjectiveType type, string targetId, int count)
    {
        if (string.IsNullOrWhiteSpace(targetId) || count <= 0) return;

        // 순회 중 완료가 나면 상태가 바뀌므로 복사본으로 돈다
        var active = new List<QuestRuntime>();
        foreach (var r in table.Values)
            if (r.State == QuestState.Active) active.Add(r);

        foreach (var r in active)
        {
            if (r.State != QuestState.Active) continue;   // 앞 루프에서 끝났을 수 있음

            bool changed = false;
            for (int i = 0; i < r.Data.objectives.Length; i++)
            {
                var obj = r.Data.objectives[i];
                if (obj.type != type) continue;
                if (!string.Equals(obj.targetId, targetId, StringComparison.OrdinalIgnoreCase)) continue;
                if (r.Progress[i] >= obj.requiredCount) continue;

                r.Progress[i] = Mathf.Min(r.Progress[i] + count, obj.requiredCount);
                changed = true;
                OnObjectiveProgress?.Invoke(r, i);
                Log($"{r.Data.title} — {obj.description} ({r.Progress[i]}/{obj.requiredCount})");
            }

            if (changed && r.AllObjectivesDone())
                CompleteQuest(r.Data.questId);
            else if (changed)
                Persist();
        }
    }

    // ── 종료 ────────────────────────────────────────────────────

    public void CompleteQuest(string questId)
    {
        var r = Get(questId);
        if (r == null || r.State != QuestState.Active) return;

        r.State = QuestState.Completed;
        Log($"완료: {r.Data.title}");

        ApplyOutcome(r.Data.onComplete, r.Data.completeLine);
        OnQuestCompleted?.Invoke(r);

        RefreshAvailability();
        Persist();
    }

    public void FailQuest(string questId)
    {
        var r = Get(questId);
        if (r == null || r.State != QuestState.Active) return;

        r.State = QuestState.Failed;
        Log($"실패: {r.Data.title}");

        ApplyOutcome(r.Data.onFail, r.Data.failLine);
        OnQuestFailed?.Invoke(r);

        RefreshAvailability();
        Persist();
    }

    private IEnumerator TimeLimitWatch(QuestRuntime r)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, r.Data.timeLimit - (Time.time - r.StartedAt)));
        if (r.State == QuestState.Active)
            FailQuest(r.Data.questId);
    }

    /// <summary>결과를 기분 / 경험치 / 기억 / 내면 세계로 흘려보낸다.</summary>
    private void ApplyOutcome(QuestOutcome outcome, string line)
    {
        if (outcome == null) return;

        if (outcome.moodDelta != 0f && moodSystem != null)
            moodSystem.ChangeMood(outcome.moodDelta);

        if (outcome.expReward > 0f && playerStats != null)
            playerStats.AddEXP(outcome.expReward);

        // 인격이 나중에 끄집어낼 수 있도록 기억에 남긴다 (실패일수록 중요)
        if (!string.IsNullOrWhiteSpace(outcome.memoryLine) && memory != null)
            _ = memory.Remember(outcome.memoryLine, "inner");

        foreach (string id in outcome.unlockQuestIds)
        {
            var target = Get(id);
            if (target != null && target.State == QuestState.Locked)
            {
                target.State = QuestState.Available;
                OnQuestAvailable?.Invoke(target);
            }
        }

        if (!string.IsNullOrWhiteSpace(line))
            Log($"\"{line}\"");

        if (outcome.triggerInnerVoice && innerVoice != null)
            innerVoice.ForceOpen(outcome.innerVoiceContext);

        foreach (string id in outcome.unlockQuestIds)
        {
            var target = Get(id);
            if (target != null && target.State == QuestState.Available && target.Data.autoStart)
                StartQuest(id);
        }
    }

    private void Persist()
    {
        if (autoSave) QuestSaveSystem.Save(this);
    }

    private void Log(string msg)
    {
        if (verboseLog) Debug.Log($"[Quest] {msg}");
    }

    void OnApplicationQuit() => Persist();

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class QuestSaveEntry
{
    public string questId;
    public QuestState state;
    public int[] progress;
    public float elapsedSeconds;
}

[Serializable]
public class QuestSaveData
{
    public List<QuestSaveEntry> entries = new();
}

/// <summary>
/// 퀘스트 진행 저장. PlayerStats 와 별도 파일로 둔다 —
/// 스토리 진행만 초기화하거나 옮기기 쉬우라고.
/// </summary>
public static class QuestSaveSystem
{
    private static string SavePath => Path.Combine(Application.persistentDataPath, "quest_save.json");

    public static void Save(QuestManager manager)
    {
        if (manager == null) return;

        var data = new QuestSaveData();
        foreach (var r in manager.All())
        {
            // 손 안 댄 퀘스트는 저장할 게 없다
            if (r.State == QuestState.Locked) continue;

            data.entries.Add(new QuestSaveEntry
            {
                questId  = r.Data.questId,
                state    = r.State,
                progress = (int[])r.Progress.Clone(),
                elapsedSeconds = r.State == QuestState.Active ? Mathf.Max(0f, Time.time - r.StartedAt) : 0f
            });
        }

        try
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, prettyPrint: true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Quest] 저장 실패: {e.Message}");
        }
    }

    public static void Load(QuestManager manager)
    {
        if (manager == null || !File.Exists(SavePath))
        {
            Debug.Log("[Quest] 저장 파일 없음, 처음부터 시작");
            return;
        }

        try
        {
            var data = JsonUtility.FromJson<QuestSaveData>(File.ReadAllText(SavePath));
            if (data?.entries == null) return;

            foreach (var e in data.entries)
            {
                var r = manager.Get(e.questId);
                if (r == null) continue;   // 에셋이 삭제/개명된 퀘스트는 조용히 무시

                r.State = e.state;
                r.StartedAt = Time.time - Mathf.Max(0f, e.elapsedSeconds);
                if (e.progress != null)
                {
                    int n = Mathf.Min(e.progress.Length, r.Progress.Length);
                    Array.Copy(e.progress, r.Progress, n);   // 목표 수가 바뀌어도 안전
                }
            }

            Debug.Log($"[Quest] 불러오기 완료 ({data.entries.Count}건)");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Quest] 불러오기 실패: {e.Message}");
        }
    }

    public static void DeleteSave()
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
    }
}

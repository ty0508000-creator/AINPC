using System.Collections;
using UnityEngine;

public sealed class HubAreaEntry : MonoBehaviour
{
    public string areaId;
    public bool mainVillage;
    IEnumerator Start()
    {
        while (QuestManager.Instance == null || !QuestManager.Instance.IsSaveReady || GameFlow.Instance.IsLoading) yield return null;
        if (!mainVillage)
        {
            Report();
            var player = FindFirstObjectByType<PlayerStats>();
            var spawn = SpawnPoint.Find(GameFlow.DefaultSpawn);
            if (player != null && spawn != null) player.SetCheckpoint(spawn.transform.position);
        }
    }
    void OnTriggerStay2D(Collider2D other)
    {
        if (mainVillage && other.GetComponentInParent<PlayerStats>() != null && QuestManager.Instance != null && QuestManager.Instance.IsSaveReady) Report();
    }
    public void Report() => QuestManager.Instance?.ReportReach(areaId);
}

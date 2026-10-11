using System.Collections;
using UnityEngine;

/// <summary>
/// 지역 도착을 퀘스트에 알린다. 트리거 영역이 있으면 플레이어가 그 안에 들어설 때(마을·같은 씬 안의 사냥터),
/// 없으면 씬이 시작될 때(보스 아레나) 알린다. 들어선 구역의 입구(마을은 기본 입구)를 체크포인트로 삼는다.
/// </summary>
public sealed class HubAreaEntry : MonoBehaviour
{
    public string areaId;
    public bool mainVillage;
    [Tooltip("체크포인트로 삼을 입구. 비우면 씬 기본 입구")]
    public string entrySpawn = GameFlow.DefaultSpawn;
    bool inside;

    bool Ready => QuestManager.Instance != null && QuestManager.Instance.IsSaveReady && !GameFlow.Instance.IsLoading;

    IEnumerator Start()
    {
        if (mainVillage || GetComponent<Collider2D>() != null) yield break;
        while (!Ready) yield return null;
        Report();
        SetCheckpoint(FindFirstObjectByType<PlayerStats>());
    }

    void OnTriggerStay2D(Collider2D other)
    {
        var player = other.GetComponentInParent<PlayerStats>();
        if (player == null || !Ready) return;
        // 마을은 머무는 동안 계속 알린다(마을에서 받은 의뢰의 도착 목표). 사냥터는 들어설 때 한 번이면 된다.
        if (mainVillage || !inside) Report();
        if (inside) return;
        inside = true;
        // 마을로 돌아오면 체크포인트도 마을로 돌린다. 같은 씬이라 안 돌리면 마을에서 죽어도 사냥터에서 깨어난다.
        SetCheckpoint(player);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerStats>() != null) inside = false;
    }

    void SetCheckpoint(PlayerStats player)
    {
        var spawn = SpawnPoint.Find(string.IsNullOrEmpty(entrySpawn) ? GameFlow.DefaultSpawn : entrySpawn);
        if (player != null && spawn != null) player.SetCheckpoint(spawn.transform.position);
    }

    public void Report() => QuestManager.Instance?.ReportReach(areaId);
}

using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>이전 마을에서 저장한 위치가 새 건물·물에 막히면 마을 출발점으로 복구한다.</summary>
public sealed class VillageSpawnSafety : MonoBehaviour
{
    [SerializeField] Tilemap blockers;
    [SerializeField] Transform fallback;
    PlayerStats player;
    Coroutine initialization;

    void OnEnable() => initialization = StartCoroutine(Initialize());
    IEnumerator Initialize()
    {
        while (GameFlow.Instance.IsLoading) yield return null;
        yield return null;
        player = FindFirstObjectByType<PlayerStats>();
        if (player != null) { player.OnRespawned += RepairIfBlocked; RepairIfBlocked(); }
        initialization = null;
    }
    public void RepairIfBlocked()
    {
        if (player == null || !player.IsAlive || blockers == null || fallback == null) return;
        if (!blockers.HasTile(blockers.WorldToCell(player.transform.position))) return;
        player.transform.position = fallback.position;
        var body = player.GetComponent<Rigidbody2D>();
        if (body != null) { body.position = fallback.position; body.linearVelocity = Vector2.zero; }
        GameFlow.Instance.ShowNotice("마을 지형이 바뀌어 안전한 길로 이동했다.");
    }
    void OnDisable()
    {
        if (initialization != null) { StopCoroutine(initialization); initialization = null; }
        if (player != null) player.OnRespawned -= RepairIfBlocked;
        player = null;
    }
}

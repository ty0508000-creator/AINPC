using UnityEngine;

/// <summary>
/// 밟으면 같은 씬 안의 다른 입구로 순간이동하는 문. 마을 ↔ 사냥터·보스 구역에 쓴다.
/// 다른 씬으로 넘어가는 문은 <see cref="ScenePortal"/>.
/// 도착 입구는 반드시 돌아가는 포탈에서 몇 칸 떨어뜨려야 한다. 포탈 위에 내리면 바로 되돌아간다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ZonePortal : MonoBehaviour
{
    [Tooltip("이 씬에서 내릴 입구 이름")]
    [SerializeField] private string targetSpawn = GameFlow.DefaultSpawn;

    [Tooltip("이 진행 표시가 있어야 열린다. 비우면 항상 열려 있다")]
    [SerializeField] private string requiredFlag;

    [Tooltip("잠겨 있을 때 띄울 말")]
    [SerializeField] private string lockedMessage = "아직 갈 수 없다.";

    void Reset()
    {
        var collider = GetComponent<Collider2D>();
        if (collider != null)
            collider.isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (string.IsNullOrEmpty(targetSpawn))
            return;

        // 플레이어만 반응한다
        if (other.GetComponentInParent<PlayerStats>() == null)
            return;

        if (GameFlow.Instance.IsLoading)
            return;

        if (!string.IsNullOrEmpty(requiredFlag) && !GameFlow.Instance.HasFlag(requiredFlag))
        {
            GameFlow.Instance.ShowNotice(lockedMessage);
            return;
        }

        GameFlow.Instance.TeleportTo(targetSpawn);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.7f);
        Gizmos.DrawWireCube(transform.position, Vector3.one);
    }
}

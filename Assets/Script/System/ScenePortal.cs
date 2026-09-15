using UnityEngine;

/// <summary>
/// 밟으면 다른 씬으로 넘어가는 문. 트리거로 설정한 2D 콜라이더가 있어야 한다.
/// 마을 출구, 던전 입구, 보스방 문 같은 데 쓴다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ScenePortal : MonoBehaviour
{
    [Tooltip("넘어갈 씬 이름. Build Settings 에 들어 있어야 한다")]
    [SerializeField] private string targetScene;

    [Tooltip("그 씬에서 내릴 입구 이름")]
    [SerializeField] private string targetSpawn = GameFlow.DefaultSpawn;

    [Tooltip("이 진행 표시가 있어야 열린다. 비우면 항상 열려 있다")]
    [SerializeField] private string requiredFlag;

    [Tooltip("잠겨 있을 때 콘솔에 남길 말")]
    [SerializeField] private string lockedMessage = "아직 갈 수 없다.";

    void Reset()
    {
        var collider = GetComponent<Collider2D>();
        if (collider != null)
            collider.isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (string.IsNullOrEmpty(targetScene))
            return;

        // 플레이어만 반응한다
        if (other.GetComponentInParent<PlayerStats>() == null)
            return;

        if (GameFlow.Instance.IsLoading)
            return;

        if (!string.IsNullOrEmpty(requiredFlag) && !GameFlow.Instance.HasFlag(requiredFlag))
        {
            Debug.Log("[ScenePortal] " + lockedMessage + " (필요한 진행: " + requiredFlag + ")");
            return;
        }

        GameFlow.Instance.GoToScene(targetScene, targetSpawn);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.7f);
        Gizmos.DrawWireCube(transform.position, Vector3.one);
    }
}

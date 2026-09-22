using UnityEngine;

/// <summary>
/// 씬의 입구. 다른 씬에서 넘어올 때 플레이어가 놓일 자리다.
/// 씬마다 하나 이상 두고, 여러 개면 이름(id)으로 구분한다.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    [Tooltip("입구 이름. 포탈에서 이 이름을 지정해 넘어온다")]
    public string id = GameFlow.DefaultSpawn;

    /// <summary>이름이 같은 입구를 찾는다. 못 찾으면 아무 입구나, 그것도 없으면 null.</summary>
    public static SpawnPoint Find(string id)
    {
        SpawnPoint[] points = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
        if (points.Length == 0)
            return null;

        if (!string.IsNullOrEmpty(id))
        {
            foreach (SpawnPoint p in points)
                if (p.id == id)
                    return p;

            Debug.LogWarning("[SpawnPoint] '" + id + "' 입구를 찾지 못해 다른 입구를 씁니다.");
        }

        return points[0];
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up);
    }
}

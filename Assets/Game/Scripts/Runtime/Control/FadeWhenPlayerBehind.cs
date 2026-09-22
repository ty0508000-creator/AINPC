using UnityEngine;

/// <summary>
/// 플레이어가 뒤로 지나가 가려지면 살짝 투명해진다. 집처럼 큰 스프라이트에 붙인다.
/// 자식 SpriteRenderer 를 전부 같이 흐리게 하므로, 조각으로 조립된 집은
/// 조각들의 부모에 하나만 붙이면 한 채가 통째로 반응한다.
/// 정렬 기준은 <see cref="YSortRenderer"/> 와 같다 — 플레이어의 발밑 y 가
/// 이 건물의 바닥 y 보다 위(뒤)에 있을 때만 가려진 것으로 본다.
/// </summary>
public class FadeWhenPlayerBehind : MonoBehaviour
{
    /// <summary>가려졌을 때의 기본 불투명도.</summary>
    public const float DefaultFadedAlpha = 0.45f;

    [Range(0.05f, 1f)]
    [Tooltip("가려졌을 때의 불투명도 (1 이면 그대로)")]
    public float fadedAlpha = DefaultFadedAlpha;

    [Tooltip("흐려지고 돌아오는 속도")]
    public float fadeSpeed = 8f;

    [Tooltip("겹침 판정 여유. 클수록 살짝만 겹쳐도 흐려진다")]
    public float padding = 0.1f;

    private SpriteRenderer[] renderers;
    private Bounds area;        // 자식 전부를 감싸는 월드 영역 (판정 여유 포함)
    private float baseY;        // 이 건물의 바닥 y — 정렬 기준
    private float alpha = 1f;

    // 플레이어는 씬에 하나뿐이므로 모든 건물이 같이 쓴다
    private static Transform playerTransform;
    private static SpriteRenderer playerRenderer;
    private static YSortRenderer playerSorter;
    private static int lastSearchFrame = -1;

    void Awake()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"{name}: FadeWhenPlayerBehind 가 흐리게 할 SpriteRenderer 를 찾지 못했습니다.");
            enabled = false;
            return;
        }

        area = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            area.Encapsulate(renderers[i].bounds);

        baseY = area.min.y;
        area.Expand(padding * 2f);
    }

    void LateUpdate()
    {
        float target = IsPlayerBehind() ? fadedAlpha : 1f;
        if (Mathf.Approximately(alpha, target))
            return;

        alpha = Mathf.MoveTowards(alpha, target, fadeSpeed * Time.deltaTime);
        ApplyAlpha();
    }

    /// <summary>플레이어가 이 건물 뒤에 서서 그림에 가려져 있는가.</summary>
    private bool IsPlayerBehind()
    {
        Transform player = FindPlayer();
        if (player == null)
            return false;

        // 앞(아래)에 서 있으면 플레이어가 위에 그려지므로 건드릴 필요가 없다
        float sortPointY = playerSorter != null ? playerSorter.sortPointY : YSortRenderer.DefaultSortPointY;
        if (player.position.y + sortPointY <= baseY)
            return false;

        if (playerRenderer != null)
            return area.Intersects(playerRenderer.bounds);

        Vector3 foot = player.position;
        foot.z = area.center.z;
        return area.Contains(foot);
    }

    private void ApplyAlpha()
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            Color color = renderers[i].color;
            color.a = alpha;
            renderers[i].color = color;
        }
    }

    /// <summary>플레이어를 찾아 캐시한다. 못 찾으면 한 프레임에 한 번만 다시 시도한다.</summary>
    private static Transform FindPlayer()
    {
        if (playerTransform != null)
            return playerTransform;

        if (lastSearchFrame == Time.frameCount)
            return null;
        lastSearchFrame = Time.frameCount;

        PlayerStats stats = FindFirstObjectByType<PlayerStats>();
        if (stats == null)
            return null;

        playerTransform = stats.transform;
        playerRenderer = stats.GetComponent<SpriteRenderer>();
        if (playerRenderer == null)
            playerRenderer = stats.GetComponentInChildren<SpriteRenderer>();
        playerSorter = stats.GetComponent<YSortRenderer>();

        return playerTransform;
    }
}

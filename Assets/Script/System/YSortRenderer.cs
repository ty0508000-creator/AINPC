using UnityEngine;

/// <summary>
/// y 좌표가 낮을수록 화면 앞쪽에 그려지도록 정렬 순서를 계산한다.
/// 맵 생성기가 만든 소품들과 같은 기준(baseOrder/precision)을 써야 앞뒤가 맞는다.
/// 월드에 서는 것(플레이어/몬스터/NPC)은 전부 이 컴포넌트를 달아야 하며,
/// 안 달면 정렬값 0 으로 남아 나무·집 뒤에 통째로 가려진다.
/// </summary>
public class YSortRenderer : MonoBehaviour
{
    /// <summary>타일맵보다 위에 오도록 하는 기준값. 맵 생성기(MapGenSettings)도 이 값을 쓴다.</summary>
    public const int DefaultBaseOrder = 1000;

    /// <summary>1유닛당 정렬 단계.</summary>
    public const float DefaultPrecision = 10f;

    /// <summary>기본 정렬 기준점(발밑) 오프셋.</summary>
    public const float DefaultSortPointY = -0.3f;

    /// <summary>
    /// 지형·소품보다 항상 위에 그려야 하는 월드 오버레이(공격 범위 표시 등)용 값.
    /// 소품 최대치(맵 세로 300칸 기준 2500)보다 충분히 크게 잡는다.
    /// </summary>
    public const int WorldOverlayOrder = 5000;

    [Tooltip("정렬 기준점(보통 발밑) 의 y 오프셋")]
    public float sortPointY = DefaultSortPointY;

    [Tooltip("타일맵보다 위에 오도록 하는 기준값")]
    public int baseOrder = DefaultBaseOrder;

    [Tooltip("1유닛당 정렬 단계")]
    public float precision = DefaultPrecision;

    private SpriteRenderer spriteRenderer;

    /// <summary>
    /// 이미 붙어 있으면 그대로 두고, 없으면 붙여서 돌려준다.
    /// 프리팹에서 인스펙터로 맞춰둔 값을 덮어쓰지 않는다.
    /// </summary>
    public static YSortRenderer Attach(GameObject target, float sortPointY = DefaultSortPointY)
    {
        if (target == null)
            return null;

        YSortRenderer existing = target.GetComponent<YSortRenderer>();
        if (existing != null)
            return existing;

        YSortRenderer sorter = target.AddComponent<YSortRenderer>();
        sorter.sortPointY = sortPointY;
        return sorter;
    }

    void Awake()
    {
        // 스프라이트를 자식에 둔 프리팹도 있으므로 자식까지 찾는다
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer == null)
            Debug.LogWarning($"{name}: YSortRenderer 가 정렬할 SpriteRenderer 를 찾지 못했습니다.");
    }

    void LateUpdate()
    {
        if (spriteRenderer == null)
            return;

        float y = transform.position.y + sortPointY;
        spriteRenderer.sortingOrder = baseOrder - Mathf.RoundToInt(y * precision);
    }
}

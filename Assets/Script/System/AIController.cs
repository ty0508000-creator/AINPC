using System.Collections;
using UnityEngine;

public class AIController : MonoBehaviour
{
    [Header("AI Settings")]
    [SerializeField] private float aiMoveSpeed = 5f;
    [SerializeField] private float detectionRadius = 10f;
    [SerializeField] private float attackTriggerRange = 1.5f;
    [SerializeField] private float actionInterval = 2f;
    [SerializeField] private LayerMask enemyLayer;

    [Header("Reckless (제어권 상실의 대가)")]
    [Tooltip("이 거리 안에 적이 있으면 AI가 몸을 함부로 굴려 얻어맞는다 (공격 정지 거리 이상 권장)")]
    [SerializeField] private float contactRange = 1.6f;
    [SerializeField] private int recklessDamage = 5;
    [SerializeField] private float recklessInterval = 1f;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private ControlManager controlManager;
    private Player_Attack playerAttack;
    private PlayerStats playerStats;

    private Coroutine behaviorCoroutine;
    private Coroutine recklessCoroutine;
    private Color baseColor = Color.white;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        controlManager = GetComponent<ControlManager>();
        playerAttack = GetComponent<Player_Attack>();
        playerStats = GetComponent<PlayerStats>();

        if (spriteRenderer != null) baseColor = spriteRenderer.color;

        controlManager.OnAITakeover += StartAI;
        controlManager.OnPlayerRestored += StopAI;
    }

    void StartAI()
    {
        behaviorCoroutine = StartCoroutine(BehaviorLoop());
        recklessCoroutine = StartCoroutine(RecklessLoop());
    }

    void StopAI()
    {
        if (behaviorCoroutine != null)
        {
            StopCoroutine(behaviorCoroutine);
            behaviorCoroutine = null;
        }
        if (recklessCoroutine != null)
        {
            StopCoroutine(recklessCoroutine);
            recklessCoroutine = null;
        }
        rb.linearVelocity = Vector2.zero;
        if (spriteRenderer != null) spriteRenderer.color = baseColor;
    }

    // AI가 몸을 함부로 굴려 적에게 그대로 얻어맞는다 (제어권을 잃은 대가)
    IEnumerator RecklessLoop()
    {
        var wait = new WaitForSeconds(recklessInterval);
        while (true)
        {
            yield return wait;

            if (playerStats == null || playerAttack.IsInvincible) continue;

            bool touchingEnemy =
                Physics2D.OverlapCircle(rb.position, contactRange, enemyLayer) != null;

            if (touchingEnemy)
            {
                playerStats.TakeDamage(recklessDamage);
                StartCoroutine(HitFlash());
            }
        }
    }

    IEnumerator HitFlash()
    {
        if (spriteRenderer == null) yield break;
        spriteRenderer.color = new Color(1f, 0.4f, 0.4f);
        yield return new WaitForSeconds(0.1f);
        if (spriteRenderer != null) spriteRenderer.color = baseColor;
    }

    IEnumerator BehaviorLoop()
    {
        while (true)
        {
            // 돌진 중이면 대기
            while (playerAttack.IsInvincible)
                yield return new WaitForFixedUpdate();

            Collider2D target = FindNearest();

            // nested IEnumerator (StartCoroutine 아님) → 제어권 회복 시 함께 중단됨
            if (target != null)
            {
                yield return ChaseAndAttack(target);   // 적을 처치/이탈할 때까지 연속 전투
            }
            else
            {
                yield return Wander();
                yield return new WaitForSeconds(actionInterval);
            }
        }
    }

    Collider2D FindNearest()
    {
        Collider2D[] cols = Physics2D.OverlapCircleAll(rb.position, detectionRadius, enemyLayer);
        if (cols.Length == 0) return null;

        Collider2D nearest = null;
        float minDist = float.MaxValue;
        foreach (var c in cols)
        {
            float d = Vector2.Distance(rb.position, c.transform.position);
            if (d < minDist) { minDist = d; nearest = c; }
        }
        return nearest;
    }

    // 적이 죽거나(파괴) 탐지 범위 밖으로 도망칠 때까지 추격하며 연속 공격
    IEnumerator ChaseAndAttack(Collider2D target)
    {
        while (target != null)
        {
            while (playerAttack.IsInvincible)
                yield return new WaitForFixedUpdate();

            if (target == null) break;   // 대기 중 적이 죽었을 수 있음

            Vector2 tpos = target.transform.position;
            Vector2 dir = (tpos - rb.position).normalized;
            float dist = Vector2.Distance(rb.position, tpos);

            // 적이 탐지 범위 밖으로 벗어나면 재탐색
            if (dist > detectionRadius)
            {
                rb.linearVelocity = Vector2.zero;
                yield break;
            }

            spriteRenderer.flipX = dir.x < 0;

            if (dist <= attackTriggerRange)
            {
                // 근거리: 일반 공격 (쿨다운은 Player_Attack 내부에서 관리)
                rb.linearVelocity = Vector2.zero;
                playerAttack.ForceAttack(dir);
            }
            else if (playerAttack.DashReady && dist <= playerAttack.MaxDashRange)
            {
                // 원거리(대시 사거리 안 + 쿨다운 완료): 대시 공격으로 파고듦
                rb.linearVelocity = Vector2.zero;
                playerAttack.ForceDash(dir, dist);
            }
            else
            {
                // 대시 사거리 밖이거나 쿨다운 중: 걸어서 접근
                rb.linearVelocity = dir * aiMoveSpeed;
            }

            yield return new WaitForFixedUpdate();
        }

        rb.linearVelocity = Vector2.zero;
    }

    IEnumerator Wander()
    {
        Vector2 dir = Random.insideUnitCircle.normalized;
        float duration = Random.Range(0.5f, 1.5f);
        float timer = 0f;

        while (timer < duration)
        {
            rb.linearVelocity = dir * aiMoveSpeed;
            spriteRenderer.flipX = dir.x < 0;
            timer += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        rb.linearVelocity = Vector2.zero;
    }

    void OnDestroy()
    {
        if (controlManager == null) return;
        controlManager.OnAITakeover -= StartAI;
        controlManager.OnPlayerRestored -= StopAI;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, contactRange);
    }
}

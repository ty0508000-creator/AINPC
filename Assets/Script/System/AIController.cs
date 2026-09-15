using System.Collections;
using UnityEngine;

public class AIController : MonoBehaviour
{
    [Header("AI Settings")]
    [SerializeField] private float aiMoveSpeed = 5f;
    [SerializeField] private float detectionRadius = 10f;
    [SerializeField] private float attackTriggerRange = 1.5f;
    [SerializeField] private float attackRangeTolerance = 0.15f;
    [SerializeField] private float actionInterval = 2f;
    [SerializeField] private LayerMask enemyLayer;

    [Header("몬스터 상대 무빙 (치고 빠지기)")]
    [Tooltip("파고들 목표 거리. 0 이면 공격 사거리의 85% 를 쓴다")]
    [SerializeField] private float strikeRange = 0f;

    [Tooltip("치고 난 뒤 빠질 거리. 0 이면 공격 사거리의 2.2배를 쓴다")]
    [SerializeField] private float retreatRange = 0f;

    [Tooltip("파고들 때 속도 배율")]
    [SerializeField] private float approachSpeedScale = 1f;

    [Tooltip("빠질 때 속도 배율")]
    [SerializeField] private float retreatSpeedScale = 1.1f;

    [Tooltip("파고든 뒤 붙어서 버티는 시간")]
    [SerializeField] private float strikeHold = 0.2f;

    [Tooltip("빠진 뒤 다시 들어가기까지 쉬는 시간 (최소)")]
    [SerializeField] private float waitMin = 0.15f;

    [Tooltip("빠진 뒤 다시 들어가기까지 쉬는 시간 (최대)")]
    [SerializeField] private float waitMax = 0.5f;

    [Tooltip("공격 쿨이 돌 때까지 기다렸다가 들어간다")]
    [SerializeField] private bool waitForAttackCooldown = true;

    [Tooltip("빠질 때 옆으로 도는 정도. 0 이면 뒤로만 뺀다")]
    [Range(0f, 1f)]
    [SerializeField] private float strafeAmount = 0.35f;

    [Tooltip("속도가 바뀌는 빠르기(초당). 0 이면 즉시 바뀐다")]
    [SerializeField] private float acceleration = 40f;

    [Tooltip("한 동작이 이 시간을 넘기면 (벽에 막힌 것으로 보고) 다음 동작으로 넘어간다")]
    [SerializeField] private float phaseTimeout = 2.5f;

    [Header("Reckless")]
    [Tooltip("If an enemy is within this range during AI control, the body gets hit for being reckless.")]
    [SerializeField] private float contactRange = 0.75f;
    [SerializeField] private int recklessDamage = 5;
    [SerializeField] private float recklessInterval = 1f;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private ControlManager controlManager;
    private Player_Attack playerAttack;
    private Player_Controller playerController;
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
        playerController = GetComponent<Player_Controller>();
        playerStats = GetComponent<PlayerStats>();

        if (spriteRenderer != null) baseColor = spriteRenderer.color;

        controlManager.OnAITakeover += StartAI;
        controlManager.OnPlayerRestored += StopAI;

        if (playerStats != null)
            playerStats.OnDied += HandleDeath;
    }

    void StartAI()
    {
        // 죽은 몸으로는 싸우지 않는다. 기분이 다시 떨어져도 마찬가지다.
        if (playerStats != null && playerStats.IsDead)
            return;

        behaviorCoroutine = StartCoroutine(BehaviorLoop());
        recklessCoroutine = StartCoroutine(RecklessLoop());
    }

    /// <summary>플레이어가 죽으면 AI 도 손을 뗀다 (제어권은 그대로 AI 에 있어도 몸은 멈춘다).</summary>
    void HandleDeath()
    {
        StopAI();
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
            while (playerAttack.IsInvincible)
                yield return new WaitForFixedUpdate();

            Collider2D target = FindNearest();

            if (target != null)
            {
                yield return KeepRangeAndAttack(target);
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

    /// <summary>
    /// 적정 거리 안팎을 계속 드나든다 — 파고들어 치고, 옆으로 돌며 빠지고, 한 박자 쉬었다가 다시 들어간다.
    /// 적을 놓치거나 감지 범위를 벗어나면 빠져나가 새 목표를 찾는다.
    /// </summary>
    IEnumerator KeepRangeAndAttack(Collider2D target)
    {
        while (IsEngageable(target))
        {
            // 빠질 때 도는 쪽은 한 바퀴마다 새로 정한다 (한쪽으로만 돌면 티가 난다)
            float strafeSign = Random.value < 0.5f ? -1f : 1f;

            yield return Approach(target);
            yield return Strike(target);
            yield return Retreat(target, strafeSign);
            yield return Breather(target);
        }

        rb.linearVelocity = Vector2.zero;
    }

    /// <summary>사거리 안쪽까지 파고든다.</summary>
    IEnumerator Approach(Collider2D target)
    {
        float timer = 0f;
        float goal = StrikeDistance();

        while (timer < phaseTimeout && IsEngageable(target) && !playerAttack.IsInvincible)
        {
            Vector2 offset = (Vector2)target.transform.position - rb.position;
            float dist = offset.magnitude;
            Vector2 dir = GetDirectionToTarget(offset);
            FaceTo(dir);

            if (dist <= goal + attackRangeTolerance)
                break;

            Drive(dir * GetAIMoveSpeed() * approachSpeedScale);

            // 들어가는 도중에 닿으면 바로 친다
            if (dist <= attackTriggerRange)
                playerAttack.ForceAttack(dir);

            timer += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
    }

    /// <summary>붙은 채로 잠깐 버티며 친다.</summary>
    IEnumerator Strike(Collider2D target)
    {
        float timer = 0f;

        while (timer < strikeHold && IsEngageable(target) && !playerAttack.IsInvincible)
        {
            Vector2 offset = (Vector2)target.transform.position - rb.position;
            Vector2 dir = GetDirectionToTarget(offset);
            FaceTo(dir);

            Drive(Vector2.zero);

            if (offset.magnitude <= attackTriggerRange)
                playerAttack.ForceAttack(dir);

            timer += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
    }

    /// <summary>옆으로 돌면서 사거리 밖으로 빠진다.</summary>
    IEnumerator Retreat(Collider2D target, float strafeSign)
    {
        float timer = 0f;
        float goal = RetreatDistance();

        while (timer < phaseTimeout && IsEngageable(target) && !playerAttack.IsInvincible)
        {
            Vector2 offset = (Vector2)target.transform.position - rb.position;
            float dist = offset.magnitude;
            Vector2 dir = GetDirectionToTarget(offset);

            // 빠지는 동안에도 적을 보고 있어야 다시 들어갈 때 자연스럽다
            FaceTo(dir);

            if (dist >= goal)
                break;

            Vector2 away = -dir + Vector2.Perpendicular(dir) * strafeSign * strafeAmount;
            Drive(away.normalized * GetAIMoveSpeed() * retreatSpeedScale);

            timer += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
    }

    /// <summary>다시 파고들기 전 한 박자 쉰다. 공격 쿨이 남았으면 그만큼 더 기다린다.</summary>
    IEnumerator Breather(Collider2D target)
    {
        float wait = Random.Range(waitMin, Mathf.Max(waitMin, waitMax));
        float timer = 0f;

        while (IsEngageable(target) && !playerAttack.IsInvincible)
        {
            Drive(Vector2.zero);
            timer += Time.fixedDeltaTime;

            bool restedEnough = timer >= wait;
            bool cooldownDone = !waitForAttackCooldown || playerAttack.AttackReady;

            if ((restedEnough && cooldownDone) || timer >= phaseTimeout)
                break;

            yield return new WaitForFixedUpdate();
        }
    }

    /// <summary>아직 이 적을 상대할 수 있는가 (살아 있고 감지 범위 안).</summary>
    bool IsEngageable(Collider2D target)
    {
        if (target == null)
            return false;

        return Vector2.Distance(rb.position, target.transform.position) <= detectionRadius;
    }

    /// <summary>속도를 목표치로 서서히 옮긴다. acceleration 이 0 이면 즉시 바뀐다.</summary>
    void Drive(Vector2 desiredVelocity)
    {
        rb.linearVelocity = acceleration > 0f
            ? Vector2.MoveTowards(rb.linearVelocity, desiredVelocity, acceleration * Time.fixedDeltaTime)
            : desiredVelocity;
    }

    void FaceTo(Vector2 dir)
    {
        if (spriteRenderer != null && Mathf.Abs(dir.x) > 0.01f)
            spriteRenderer.flipX = dir.x < 0;
    }

    /// <summary>파고들 목표 거리.</summary>
    float StrikeDistance()
    {
        return strikeRange > 0f ? strikeRange : GetPreferredAttackRange() * 0.85f;
    }

    /// <summary>빠질 목표 거리. 파고들 거리보다 항상 멀어야 왔다 갔다 한다.</summary>
    float RetreatDistance()
    {
        float value = retreatRange > 0f ? retreatRange : GetPreferredAttackRange() * 2.2f;
        return Mathf.Max(value, StrikeDistance() + 0.5f);
    }

    Vector2 GetDirectionToTarget(Vector2 offset)
    {
        if (offset.sqrMagnitude > 0.0001f)
            return offset.normalized;

        if (playerController != null && playerController.LastMoveDir.sqrMagnitude > 0.0001f)
            return playerController.LastMoveDir;

        return Vector2.right;
    }

    float GetPreferredAttackRange()
    {
        if (playerAttack == null)
            return attackTriggerRange;

        return Mathf.Max(0.1f, playerAttack.NormalAttackRange, attackTriggerRange);
    }

    float GetAIMoveSpeed()
    {
        if (playerController == null)
            return aiMoveSpeed;

        return playerController.MoveSpeed;
    }

    IEnumerator Wander()
    {
        Vector2 dir = Random.insideUnitCircle.normalized;
        float duration = Random.Range(0.5f, 1.5f);
        float timer = 0f;

        while (timer < duration)
        {
            rb.linearVelocity = dir * GetAIMoveSpeed();
            if (spriteRenderer != null)
                spriteRenderer.flipX = dir.x < 0;
            timer += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        rb.linearVelocity = Vector2.zero;
    }

    void OnDestroy()
    {
        if (playerStats != null)
            playerStats.OnDied -= HandleDeath;

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

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, GetPreferredAttackRange());

        // 치고 빠지는 두 거리
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, StrikeDistance());

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, RetreatDistance());
    }
}

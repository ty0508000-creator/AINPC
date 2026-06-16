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

    IEnumerator KeepRangeAndAttack(Collider2D target)
    {
        while (target != null)
        {
            while (playerAttack.IsInvincible)
                yield return new WaitForFixedUpdate();

            if (target == null) break;

            Vector2 offset = (Vector2)target.transform.position - rb.position;
            float dist = offset.magnitude;
            Vector2 dir = GetDirectionToTarget(offset);
            float preferredRange = GetPreferredAttackRange();
            float moveSpeed = GetAIMoveSpeed();

            if (dist > detectionRadius)
            {
                rb.linearVelocity = Vector2.zero;
                yield break;
            }

            if (spriteRenderer != null)
                spriteRenderer.flipX = dir.x < 0;

            if (dist < preferredRange - attackRangeTolerance)
            {
                rb.linearVelocity = -dir * moveSpeed;
                if (dist <= attackTriggerRange)
                    playerAttack.ForceAttack(dir);
            }
            else if (dist <= attackTriggerRange)
            {
                rb.linearVelocity = Vector2.zero;
                playerAttack.ForceAttack(dir);
            }
            else if (dist > preferredRange + attackRangeTolerance)
            {
                rb.linearVelocity = dir * moveSpeed;
            }
            else
            {
                rb.linearVelocity = Vector2.zero;
            }

            yield return new WaitForFixedUpdate();
        }

        rb.linearVelocity = Vector2.zero;
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
    }
}

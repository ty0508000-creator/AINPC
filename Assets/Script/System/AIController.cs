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

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private ControlManager controlManager;
    private Player_Attack playerAttack;

    private Coroutine behaviorCoroutine;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        controlManager = GetComponent<ControlManager>();
        playerAttack = GetComponent<Player_Attack>();

        controlManager.OnAITakeover += StartAI;
        controlManager.OnPlayerRestored += StopAI;
    }

    void StartAI()
    {
        behaviorCoroutine = StartCoroutine(BehaviorLoop());
    }

    void StopAI()
    {
        if (behaviorCoroutine != null)
        {
            StopCoroutine(behaviorCoroutine);
            behaviorCoroutine = null;
        }
        rb.linearVelocity = Vector2.zero;
    }

    IEnumerator BehaviorLoop()
    {
        while (true)
        {
            // 돌진 중이면 대기
            while (playerAttack.IsInvincible)
                yield return new WaitForFixedUpdate();

            Collider2D target = FindNearest();

            if (target != null)
                yield return StartCoroutine(ChaseAndAttack(target));
            else
                yield return StartCoroutine(Wander());

            yield return new WaitForSeconds(actionInterval);
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

    IEnumerator ChaseAndAttack(Collider2D target)
    {
        float timer = 0f;

        while (target != null && timer < 4f)
        {
            while (playerAttack.IsInvincible)
                yield return new WaitForFixedUpdate();

            Vector2 dir = ((Vector2)target.transform.position - rb.position).normalized;
            float dist = Vector2.Distance(rb.position, (Vector2)target.transform.position);

            if (dist <= attackTriggerRange)
            {
                rb.linearVelocity = Vector2.zero;
                playerAttack.ForceAttack(dir);
                yield break;
            }

            rb.linearVelocity = dir * aiMoveSpeed;
            spriteRenderer.flipX = dir.x < 0;

            timer += Time.fixedDeltaTime;
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
    }
}

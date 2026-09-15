using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public abstract class MonsterBase : MonoBehaviour, IDamageable
{
    [SerializeField] private MonsterData monsterData;
    [SerializeField] private Color hitFlashColor = Color.red;
    [SerializeField, Min(0.01f)] private float hitFlashDuration = 0.12f;

    public MonsterData Data => monsterData;
    public bool IsDead { get; private set; }

    private float currentHP;
    private float lastAttackTime = -999f;
    private Vector2 moveDirection;

    private PlayerStats targetStats;
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private MonsterSpawnPoint spawnPoint;
    private Coroutine hitFlashCoroutine;
    private Color baseColor = Color.white;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
            baseColor = spriteRenderer.color;

        // 맵 소품(나무·집)과 같은 기준으로 앞뒤 정렬 — 없으면 소품 뒤에 통째로 가려진다
        YSortRenderer.Attach(gameObject);
    }

    protected virtual void Start()
    {
        if (currentHP <= 0f)
            ResetHP();

        FindPlayer();
    }

    protected virtual void Update()
    {
        if (IsDead || monsterData == null)
            return;

        if (targetStats == null)
            FindPlayer();

        if (targetStats == null)
        {
            moveDirection = Vector2.zero;
            return;
        }

        Vector2 toPlayer = targetStats.transform.position - transform.position;
        float distance = toPlayer.magnitude;

        if (distance > monsterData.AggroRange)
        {
            moveDirection = Vector2.zero;
            return;
        }

        if (distance <= monsterData.AttackRange)
        {
            moveDirection = Vector2.zero;
            TryAttack();
            return;
        }

        moveDirection = toPlayer.normalized;
        UpdateFacing(moveDirection);
    }

    protected virtual void FixedUpdate()
    {
        if (IsDead || monsterData == null || moveDirection == Vector2.zero)
            return;

        rb.MovePosition(rb.position + moveDirection * monsterData.MoveSpeed * Time.fixedDeltaTime);
    }

    public virtual void Initialize(MonsterData data, MonsterSpawnPoint ownerPoint)
    {
        monsterData = data;
        spawnPoint = ownerPoint;
        IsDead = false;
        ResetHP();
        FindPlayer();
    }

    public virtual void TakeDamage(int damage)
    {
        if (IsDead || monsterData == null)
            return;

        currentHP = Mathf.Max(0f, currentHP - damage);
        PlayHitFlash();

        if (currentHP <= 0f)
            Die();
    }

    protected virtual void Attack(PlayerStats player)
    {
        player.TakeDamage(monsterData.AttackDamage);
    }

    protected virtual void Die()
    {
        if (IsDead)
            return;

        IsDead = true;
        moveDirection = Vector2.zero;

        if (targetStats == null)
            FindPlayer();

        if (targetStats != null && monsterData != null)
            targetStats.AddEXP(monsterData.ExpReward);

        spawnPoint?.Clear(this);
        Destroy(gameObject);
    }

    private void TryAttack()
    {
        if (Time.time - lastAttackTime < monsterData.AttackCooldown)
            return;

        lastAttackTime = Time.time;
        Attack(targetStats);
    }

    private void ResetHP()
    {
        if (monsterData == null)
        {
            currentHP = 0f;
            return;
        }

        currentHP = monsterData.MaxHP;
    }

    private void FindPlayer()
    {
        targetStats = FindFirstObjectByType<PlayerStats>();
    }

    private void UpdateFacing(Vector2 direction)
    {
        if (spriteRenderer == null)
            return;

        if (direction.x < -0.01f)
            spriteRenderer.flipX = true;
        else if (direction.x > 0.01f)
            spriteRenderer.flipX = false;
    }

    private void PlayHitFlash()
    {
        if (spriteRenderer == null || hitFlashDuration <= 0f)
            return;

        if (hitFlashCoroutine != null)
            StopCoroutine(hitFlashCoroutine);

        hitFlashCoroutine = StartCoroutine(HitFlashCoroutine());
    }

    private IEnumerator HitFlashCoroutine()
    {
        spriteRenderer.color = hitFlashColor;
        yield return new WaitForSeconds(hitFlashDuration);

        if (spriteRenderer != null)
            spriteRenderer.color = baseColor;

        hitFlashCoroutine = null;
    }

    private void OnDestroy()
    {
        spawnPoint?.Clear(this);
    }
}

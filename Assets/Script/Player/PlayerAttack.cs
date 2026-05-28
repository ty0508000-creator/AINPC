using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Player_Attack : MonoBehaviour
{
    [Header("Normal Attack (LMB)")]
    [SerializeField] private int attackDamage = 10;
    [SerializeField] private float attackRange = 1f;
    [SerializeField] private float attackWidth = 1f;
    [SerializeField] private float attackCooldown = 0.8f;
    [SerializeField] private float attackBoxDuration = 0.15f;
    [SerializeField] private LayerMask enemyLayer;

    [Header("Dash Attack (RMB)")]
    [SerializeField] private float maxChargeTime = 1.5f;
    [SerializeField] private float minDashDistance = 1f;
    [SerializeField] private float maxDashDistance = 7f;
    [SerializeField] private int minDashDamage = 5;
    [SerializeField] private int maxDashDamage = 25;
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashCooldown = 5f;

    public bool IsInvincible { get; private set; }

    private Player_Controller playerController;
    private Animator animator;
    private Rigidbody2D rb;

    private float lastAttackTime = -99f;
    private float lastDashTime = -99f;

    private bool isCharging = false;
    private float chargeTime = 0f;
    private Vector2 dashDir = Vector2.right;

    private GameObject chargeIndicator;
    private GameObject attackIndicator;

    void Start()
    {
        playerController = GetComponent<Player_Controller>();
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        CreateChargeIndicator();
        CreateAttackIndicator();
    }

    void CreateAttackIndicator()
    {
        attackIndicator = new GameObject("AttackIndicator");

        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();

        var sr = attackIndicator.AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        sr.color = new Color(1f, 0f, 0f, 0.5f);
        sr.sortingOrder = 10;

        attackIndicator.SetActive(false);
    }

    void CreateChargeIndicator()
    {
        chargeIndicator = new GameObject("ChargeIndicator");

        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();

        var sr = chargeIndicator.AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        sr.color = new Color(1f, 1f, 1f, 0.3f);
        sr.sortingOrder = 10;

        chargeIndicator.SetActive(false);
    }

    void Update()
    {
        if (IsInvincible) return;

        if (Mouse.current.leftButton.wasPressedThisFrame && Time.time - lastAttackTime >= attackCooldown)
            NormalAttack();

        HandleDashCharge();

        if (isCharging)
            UpdateChargeIndicator();
    }

    void NormalAttack()
    {
        lastAttackTime = Time.time;
        TrySetTrigger("Attack");

        Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        Vector2 dir = mouseWorld - rb.position;
        if (dir.sqrMagnitude < 0.001f) dir = playerController.LastMoveDir;
        dir = dir.normalized;

        float angle = Vector2.SignedAngle(Vector2.up, dir);
        Vector2 center = rb.position + dir * attackRange;
        Vector2 boxSize = new Vector2(attackWidth * 1.5f, attackWidth * 2f);

        StartCoroutine(ShowAttackBox(center, boxSize, angle));

        Collider2D[] hits = Physics2D.OverlapBoxAll(center, boxSize, angle, enemyLayer);
        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<IDamageable>(out var damageable))
                damageable.TakeDamage(attackDamage);
        }
    }

    IEnumerator ShowAttackBox(Vector2 center, Vector2 size, float angle)
    {
        attackIndicator.transform.position = center;
        attackIndicator.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        attackIndicator.transform.localScale = new Vector3(size.x, size.y, 1f);
        attackIndicator.SetActive(true);
        yield return new WaitForSeconds(attackBoxDuration);
        attackIndicator.SetActive(false);
    }

    Vector2 GetMouseDirection()
    {
        Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        Vector2 dir = mouseWorld - rb.position;
        return dir.sqrMagnitude > 0.001f ? dir.normalized : dashDir;
    }

    void HandleDashCharge()
    {
        if (Mouse.current.rightButton.isPressed && Time.time - lastDashTime < dashCooldown)
            return;

        if (Mouse.current.rightButton.isPressed)
        {
            isCharging = true;
            chargeTime = Mathf.Min(chargeTime + Time.deltaTime, maxChargeTime);
            dashDir = GetMouseDirection();
        }
        else if (isCharging)
        {
            float ratio = Mathf.Clamp01(chargeTime / maxChargeTime);
            chargeIndicator.SetActive(false);
            isCharging = false;
            chargeTime = 0f;
            lastDashTime = Time.time;
            StartCoroutine(DashCoroutine(ratio, dashDir));
        }
    }

    void UpdateChargeIndicator()
    {
        float ratio = Mathf.Clamp01(chargeTime / maxChargeTime);
        float dist = Mathf.Lerp(minDashDistance, maxDashDistance, ratio);

        chargeIndicator.transform.position = rb.position + dashDir * (dist * 0.5f);
        chargeIndicator.transform.rotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.up, dashDir));
        chargeIndicator.transform.localScale = new Vector3(attackWidth, dist, 1f);
        chargeIndicator.SetActive(true);
    }

    IEnumerator DashCoroutine(float ratio, Vector2 dir)
    {
        IsInvincible = true;
        TrySetTrigger("Dash");

        float dashDistance = Mathf.Lerp(minDashDistance, maxDashDistance, ratio);
        int damage = Mathf.RoundToInt(Mathf.Lerp(minDashDamage, maxDashDamage, ratio));

        RaycastHit2D[] hits = Physics2D.BoxCastAll(
            rb.position, new Vector2(attackWidth, attackWidth), 0f,
            dir, dashDistance, enemyLayer);

        HashSet<Collider2D> hitSet = new HashSet<Collider2D>();
        foreach (var hit in hits)
        {
            if (hitSet.Add(hit.collider) && hit.collider.TryGetComponent<IDamageable>(out var damageable))
                damageable.TakeDamage(damage);
        }

        Vector2 destination = rb.position + dir * dashDistance;
        while (Vector2.Distance(rb.position, destination) > 0.05f)
        {
            rb.MovePosition(Vector2.MoveTowards(rb.position, destination, dashSpeed * Time.fixedDeltaTime));
            yield return new WaitForFixedUpdate();
        }

        rb.MovePosition(destination);
        rb.linearVelocity = Vector2.zero;
        IsInvincible = false;
    }

    // AI가 직접 호출하는 공격 메서드
    public void ForceAttack(Vector2 direction)
    {
        if (Time.time - lastAttackTime < attackCooldown) return;
        lastAttackTime = Time.time;
        TrySetTrigger("Attack");

        Vector2 dir = direction.normalized;
        float angle = Vector2.SignedAngle(Vector2.up, dir);
        Vector2 center = rb.position + dir * attackRange;
        Vector2 boxSize = new Vector2(attackWidth * 1.5f, attackWidth * 2f);

        Collider2D[] hits = Physics2D.OverlapBoxAll(center, boxSize, angle, enemyLayer);
        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<IDamageable>(out var damageable))
                damageable.TakeDamage(attackDamage);
        }
    }

    void TrySetTrigger(string triggerName)
    {
        foreach (var param in animator.parameters)
        {
            if (param.type == AnimatorControllerParameterType.Trigger && param.name == triggerName)
            {
                animator.SetTrigger(triggerName);
                return;
            }
        }
    }

    void OnDestroy()
    {
        if (chargeIndicator != null)
            Destroy(chargeIndicator);
        if (attackIndicator != null)
            Destroy(attackIndicator);
    }

    void OnDrawGizmosSelected()
    {
        if (playerController == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(
            (Vector2)transform.position + playerController.LastMoveDir * attackRange,
            new Vector3(attackWidth, attackWidth, 0f));
    }
}

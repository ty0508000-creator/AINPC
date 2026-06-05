using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class MonsterProjectile : MonoBehaviour
{
    private int damage;
    private float speed;
    private Vector2 direction;
    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public void Initialize(Vector2 launchDirection, int attackDamage, float projectileSpeed, float lifetime)
    {
        direction = launchDirection.normalized;
        damage = attackDamage;
        speed = projectileSpeed;

        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        rb.linearVelocity = direction * speed;
        Destroy(gameObject, lifetime);
    }

    private void FixedUpdate()
    {
        if (rb != null)
            rb.linearVelocity = direction * speed;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.TryGetComponent<PlayerStats>(out PlayerStats playerStats))
            return;

        playerStats.TakeDamage(damage);
        Destroy(gameObject);
    }
}

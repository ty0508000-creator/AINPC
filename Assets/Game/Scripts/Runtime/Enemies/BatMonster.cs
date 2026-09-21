using UnityEngine;

public class BatMonster : MonsterBase
{
    [SerializeField] private float projectileSpeed = 7f;
    [SerializeField] private float projectileLifetime = 2.5f;
    [SerializeField] private float projectileSize = 0.25f;
    [SerializeField] private Color projectileColor = new Color(0.65f, 0.2f, 1f, 0.9f);

    private static Sprite projectileSprite;

    protected override void Attack(PlayerStats player)
    {
        if (player == null || Data == null)
            return;

        Vector2 spawnPosition = transform.position;
        Vector2 targetPosition = player.transform.position;
        Vector2 direction = targetPosition - spawnPosition;

        if (direction.sqrMagnitude < 0.001f)
            return;

        direction.Normalize();
        SpawnProjectile(spawnPosition + direction * 0.35f, direction, Data.AttackDamage);
    }

    private void SpawnProjectile(Vector2 position, Vector2 direction, int damage)
    {
        GameObject projectileObject = new GameObject("BatProjectile");
        projectileObject.transform.position = position;
        projectileObject.transform.localScale = Vector3.one * projectileSize * 2f;
        projectileObject.layer = gameObject.layer;

        SpriteRenderer spriteRenderer = projectileObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = GetProjectileSprite();
        spriteRenderer.color = projectileColor;

        // 투사체도 소품과 같은 기준으로 정렬 (발밑이 없으므로 중심 기준)
        YSortRenderer.Attach(projectileObject, 0f);

        Rigidbody2D rb = projectileObject.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        CircleCollider2D collider = projectileObject.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = projectileSize;

        MonsterProjectile projectile = projectileObject.AddComponent<MonsterProjectile>();
        projectile.Initialize(direction, damage, projectileSpeed, projectileLifetime);
    }

    private static Sprite GetProjectileSprite()
    {
        if (projectileSprite != null)
            return projectileSprite;

        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        projectileSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return projectileSprite;
    }
}

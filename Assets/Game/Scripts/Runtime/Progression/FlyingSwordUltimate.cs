using System.Collections.Generic;
using UnityEngine;

/// <summary>Five fast swords. One damage application per outbound flight; visuals never decide rewards.</summary>
[DisallowMultipleComponent]
public sealed class FlyingSwordUltimate : MonoBehaviour
{
    public const int SwordCount = 5;
    public const float Duration = 8f;
    public const float LaunchInterval = 0.06f;
    public const float FlightSpeed = 55f;
    const float Range = 7f;
    sealed class Sword
    {
        public Transform transform;
        public TrailRenderer trail;
        public MonsterBase target;
        public int phase; // 0: waiting, 1: attacking, 2: returning
        public float availableAt;
    }
    readonly Sword[] swords = new Sword[SwordCount];
    readonly List<MonsterBase> targets = new List<MonsterBase>();
    GameObject root;
    Material trailMaterial;
    LayerMask enemyLayer;
    int damage, cursor, targetCursor;
    float endsAt, nextLaunch, startedAt;
    public float Remaining => root != null ? Mathf.Max(0, endsAt - Time.time) : 0;
    public int HitCount { get; private set; }

    public void Begin(LayerMask layer, int hitDamage)
    {
        Cancel(); enemyLayer = layer; damage = hitDamage;
        startedAt = Time.time; endsAt = startedAt + Duration; nextLaunch = startedAt + 0.3f;
        cursor = targetCursor = HitCount = 0;
        root = new GameObject("이기어검 · Floating swords"); root.transform.SetParent(transform, false);
        var shader = Shader.Find("Sprites/Default");
        if (shader != null) trailMaterial = new Material(shader);
        var owner = GetComponent<SpriteRenderer>();
        for (int i = 0; i < SwordCount; i++)
        {
            var go = new GameObject("Flying sword " + (i + 1)); go.transform.SetParent(root.transform, false);
            go.transform.position = Home(i); go.transform.localScale = Vector3.one * 0.8f;
            var sprite = go.AddComponent<SpriteRenderer>(); sprite.sprite = Resources.Load<Sprite>("UiArt/FlyingSword");
            sprite.sortingLayerID = owner != null ? owner.sortingLayerID : 0;
            sprite.sortingOrder = owner != null ? owner.sortingOrder + 2 : 42;
            var trail = go.AddComponent<TrailRenderer>(); trail.time = 0.09f; trail.minVertexDistance = 0.05f;
            trail.startWidth = 0.1f; trail.endWidth = 0; trail.emitting = false;
            trail.startColor = new Color(0.65f, 0.85f, 1f, 0.7f); trail.endColor = new Color(0.65f, 0.85f, 1f, 0);
            trail.sortingLayerID = sprite.sortingLayerID; trail.sortingOrder = sprite.sortingOrder - 1;
            if (trailMaterial != null) trail.sharedMaterial = trailMaterial;
            swords[i] = new Sword { transform = go.transform, trail = trail };
            go.SetActive(false);
        }
    }

    Vector3 Home(int index)
    {
        float angle = Mathf.Lerp(30, 150, index / 4f) * Mathf.Deg2Rad;
        return transform.position + new Vector3(Mathf.Cos(angle) * 1.1f, Mathf.Sin(angle) * 0.8f + 0.2f, 0);
    }

    void Update()
    {
        if (root == null) return;
        var stats = GetComponent<PlayerStats>();
        var control = GetComponent<ControlManager>();
        if (Time.time >= endsAt || stats == null || !stats.IsAlive || (control != null && !control.IsPlayerControlled))
        { Cancel(); return; }
        if (Time.timeScale == 0) return;
        // Menus and dialogue suspend new attacks. The duration uses the same game clock as cooldowns.
        bool blocked = RpgUI.IsOpen || DialogueManager.IsDialogueOpen || PauseMenuUI.IsOpen;
        if (!blocked && Time.time >= nextLaunch)
        {
            nextLaunch = Time.time + LaunchInterval;
            for (int n = 0; n < SwordCount; n++)
            {
                int index = (cursor + n) % SwordCount; var sword = swords[index];
                if (sword.phase != 0 || Time.time < sword.availableAt) continue;
                sword.target = FindTarget();
                if (sword.target == null) break;
                sword.phase = 1; sword.trail.emitting = true; cursor = (index + 1) % SwordCount;
                break;
            }
        }
        for (int i = 0; i < SwordCount; i++)
        {
            var sword = swords[i];
            sword.transform.gameObject.SetActive(Time.time - startedAt >= i * 0.045f);
            if (sword.phase == 1 && (blocked || sword.target == null || sword.target.IsDead ||
                !sword.target.isActiveAndEnabled || Vector2.Distance(transform.position, sword.target.transform.position) > Range + 1)) sword.phase = 2;
            Vector3 destination = sword.phase == 1 ? sword.target.transform.position : Home(i);
            Vector3 direction = destination - sword.transform.position;
            if (sword.phase == 0)
            {
                sword.transform.position = destination + Vector3.up * Mathf.Sin(Time.time * 4 + i) * 0.04f;
                sword.transform.rotation = Quaternion.Euler(0, 0, (i - 2) * -15f);
                continue;
            }
            if (direction.sqrMagnitude > 0.001f)
                sword.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90);
            sword.transform.position = Vector3.MoveTowards(sword.transform.position, destination, FlightSpeed * Time.deltaTime);
            if (Vector3.Distance(sword.transform.position, destination) > 0.12f) continue;
            if (sword.phase == 1)
            {
                sword.target.TakeDamage(damage); HitCount++; sword.phase = 2; sword.target = null;
            }
            else { sword.phase = 0; sword.availableAt = Time.time + 0.06f; sword.trail.emitting = false; }
        }
    }

    MonsterBase FindTarget()
    {
        targets.Clear();
        foreach (var hit in Physics2D.OverlapCircleAll(transform.position, Range, enemyLayer))
        {
            var monster = hit.GetComponentInParent<MonsterBase>();
            if (monster != null && !monster.IsDead && monster.isActiveAndEnabled && !targets.Contains(monster)) targets.Add(monster);
        }
        return targets.Count == 0 ? null : targets[targetCursor++ % targets.Count];
    }

    public void Cancel()
    {
        if (root != null) { root.SetActive(false); Destroy(root); root = null; }
        if (trailMaterial != null) { Destroy(trailMaterial); trailMaterial = null; }
        for (int i = 0; i < swords.Length; i++) swords[i] = null;
        targets.Clear();
    }
    void OnDisable() => Cancel();
}

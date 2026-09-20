using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class RpgSkillController : MonoBehaviour
{
    PlayerStats stats;
    Player_Attack attack;
    ControlManager control;
    readonly float[] readyAt = new float[9];
    float guardUntil;
    public event Action<string> OnFeedback;
    public float GuardRemaining => Mathf.Max(0f, guardUntil - Time.time);
    public float DamageMultiplier => GuardRemaining > 0f ? 0.65f - stats.SkillRanks[3] * 0.08f : 1f;
    public float Remaining(int id) => id >= 0 && id < readyAt.Length ? Mathf.Max(0f, readyAt[id] - Time.time) : 0f;

    public void ResetForRecovery()
    {
        Array.Clear(readyAt, 0, readyAt.Length);
        guardUntil = 0f;
    }

    void Start()
    {
        stats = GetComponent<PlayerStats>(); attack = GetComponent<Player_Attack>(); control = GetComponent<ControlManager>();
    }

    void Update()
    {
        if (Keyboard.current == null || RpgUI.IsOpen || DialogueManager.IsDialogueOpen || Time.timeScale == 0f) return;
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null) return;
        if (Keyboard.current.digit1Key.wasPressedThisFrame) TryCast(0);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) TryCast(3);
        if (Keyboard.current.digit3Key.wasPressedThisFrame) TryCast(6);
    }

    public bool TryCast(int id)
    {
        if (stats == null || id < 0 || id >= RpgSkillCatalog.All.Length || !RpgSkillCatalog.All[id].Active) return false;
        if (RpgUI.IsOpen || DialogueManager.IsDialogueOpen || Time.timeScale == 0f || stats.HP <= 0f) return false;
        if (control != null && !control.IsPlayerControlled) return Fail("지금은 몸을 제어할 수 없습니다.");
        if (stats.SkillRanks[id] == 0) return Fail("무공창 [K]에서 먼저 습득하세요.");
        if (Remaining(id) > 0f) return Fail("아직 재사용 대기 중입니다.");
        if (id == 0 && (attack == null || attack.IsInvincible)) return Fail("지금은 검술을 사용할 수 없습니다.");
        if (id == 6 && stats.HP >= stats.MaxHP) return Fail("체력이 이미 가득 찼습니다.");
        var skill = RpgSkillCatalog.All[id];
        if (!stats.TrySpendMana(skill.ManaCost)) return Fail("내력이 부족합니다.");
        readyAt[id] = Time.time + skill.Cooldown;
        if (id == 0)
        {
            var mover = GetComponent<Player_Controller>();
            Vector2 direction = mover != null ? mover.LastMoveDir : Vector2.right;
            if (Camera.main != null && Mouse.current != null &&
                (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
                direction = (Vector2)(Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue()) - transform.position);
            if (direction.sqrMagnitude < 0.01f) direction = Vector2.right;
            direction.Normalize();
            float radius = 2f + stats.SkillRanks[2] * 0.4f;
            Vector2 center = (Vector2)transform.position + direction * radius * 0.6f;
            var struck = new HashSet<MonsterBase>();
            foreach (var hit in Physics2D.OverlapCircleAll(center, radius, attack.EnemyLayer))
            {
                var monster = hit.GetComponentInParent<MonsterBase>();
                if (monster != null && struck.Add(monster))
                    monster.TakeDamage(Mathf.RoundToInt(attack.AttackPower * (1.8f + (stats.SkillRanks[0] - 1) * 0.4f)));
            }
            StartCoroutine(Ring(center, radius, new Color(0.85f, 0.72f, 0.38f)));
        }
        else if (id == 3)
        {
            guardUntil = Time.time + 6f;
            StartCoroutine(Ring(transform.position, 1.3f, new Color(0.4f, 0.7f, 1f)));
        }
        else if (id == 6)
        {
            stats.Heal(stats.MaxHP * (0.2f + (stats.SkillRanks[6] - 1) * 0.08f + stats.SkillRanks[8] * 0.05f));
            StartCoroutine(Ring(transform.position, 1.5f, new Color(0.3f, 0.95f, 0.7f)));
        }
        OnFeedback?.Invoke(skill.Name); return true;
    }

    bool Fail(string message) { OnFeedback?.Invoke(message); return false; }

    IEnumerator Ring(Vector3 center, float radius, Color color)
    {
        var effect = new GameObject("Skill ripple");
        // Child ownership guarantees cleanup if the player is destroyed during the effect.
        effect.transform.SetParent(transform); effect.transform.position = center;
        var line = effect.AddComponent<LineRenderer>();
        line.useWorldSpace = false; line.loop = true; line.positionCount = 48;
        line.startWidth = line.endWidth = 0.06f; line.sortingOrder = 40;
        var shader = Shader.Find("Sprites/Default");
        Material material = shader != null ? new Material(shader) : null;
        if (material != null) line.sharedMaterial = material;
        try
        {
            float elapsed = 0f;
            while (elapsed < 0.45f)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / 0.45f);
                color.a = 1f - progress; line.startColor = line.endColor = color;
                for (int i = 0; i < 48; i++)
                {
                    float angle = i * Mathf.PI * 2f / 48;
                    line.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius * (0.5f + progress * 0.5f));
                }
                yield return null;
            }
        }
        finally { if (material != null) Destroy(material); if (effect != null) Destroy(effect); }
    }
}

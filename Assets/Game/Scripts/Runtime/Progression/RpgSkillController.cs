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
    readonly float[] readyAt = new float[RpgSkillCatalog.All.Length];
    FlyingSwordUltimate ultimate;
    public float UltimateRemaining => ultimate != null ? ultimate.Remaining : 0f;
    float guardUntil;
    int recoveryFrame = -1;
    readonly List<GameObject> visualEffects = new List<GameObject>();
    readonly List<Material> visualMaterials = new List<Material>();
    public event Action<string> OnFeedback;
    public float GuardRemaining => Mathf.Max(0f, guardUntil - Time.time);
    public float DamageMultiplier => GuardRemaining > 0f ? 0.65f - stats.SkillRanks[3] * 0.08f : 1f;
    public float Remaining(int id) => id >= 0 && id < readyAt.Length ? Mathf.Max(0f, readyAt[id] - Time.time) : 0f;
    // WASD 주변 키. E는 상호작용, R은 사망 상태에서만 재도전으로 사용한다.
    public static Key Hotkey(int id) => id == 0 ? Key.Q : id == 3 ? Key.R : id == 6 ? Key.F : id == RpgSkillCatalog.Ultimate ? Key.Z : Key.None;
    public static string HotkeyLabel(int id) => Hotkey(id) == Key.None ? "" : Hotkey(id).ToString();

    public void ResetForRecovery()
    {
        recoveryFrame = Time.frameCount;
        Array.Clear(readyAt, 0, readyAt.Length);
        guardUntil = 0f;
        CancelVisuals();
    }

    void Start()
    {
        stats = GetComponent<PlayerStats>(); attack = GetComponent<Player_Attack>(); control = GetComponent<ControlManager>();
    }

    void OnEnable()
    {
        stats = GetComponent<PlayerStats>();
        if (stats != null) stats.OnDied += CancelVisuals;
    }

    void Update()
    {
        if (Keyboard.current == null || stats == null || !stats.IsAlive || recoveryFrame == Time.frameCount || RpgUI.LastClosedFrame == Time.frameCount || RpgUI.IsOpen || InventoryWindow.IsOpen || DialogueManager.IsDialogueOpen || PauseMenuUI.IsOpen || Time.timeScale == 0f) return;
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null) return;
        foreach (int id in RpgSkillCatalog.Hotbar)
            if (Keyboard.current[Hotkey(id)].wasPressedThisFrame) TryCast(id);
        if (Keyboard.current[Hotkey(RpgSkillCatalog.Ultimate)].wasPressedThisFrame) TryCast(RpgSkillCatalog.Ultimate);
    }

    public bool TryCast(int id)
    {
        if (stats == null || id < 0 || id >= RpgSkillCatalog.All.Length || !RpgSkillCatalog.All[id].Active) return false;
        if (RpgUI.IsOpen || InventoryWindow.IsOpen || DialogueManager.IsDialogueOpen || PauseMenuUI.IsOpen || Time.timeScale == 0f || stats.HP <= 0f) return false;
        if (control != null && !control.IsPlayerControlled) return Fail("지금은 몸을 제어할 수 없습니다.");
        if (stats.SkillRanks[id] == 0) return Fail("무공창 [K]에서 먼저 습득하세요.");
        if (Remaining(id) > 0f) return Fail("아직 재사용 대기 중입니다.");
        if (id == 0 && (attack == null || attack.IsInvincible)) return Fail("지금은 검술을 사용할 수 없습니다.");
        if (id == RpgSkillCatalog.Ultimate && attack == null) return Fail("검을 사용할 수 없습니다.");
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
            PlayMotion("MoonSlash", direction);
            StartCoroutine(Ring(transform.position, radius, new Color(0.85f, 0.93f, 1f), direction, 0.45f));
        }
        else if (id == 3)
        {
            guardUntil = Time.time + 6f;
            PlayMotion("GuardCast", Vector2.zero);
            StartCoroutine(Ring(transform.position, 0.65f, new Color(1f, 0.8f, 0.35f), Vector2.zero, 6f));
        }
        else if (id == 6)
        {
            stats.Heal(stats.MaxHP * (0.2f + (stats.SkillRanks[6] - 1) * 0.08f + stats.SkillRanks[8] * 0.05f));
            PlayMotion("RecoverCast", Vector2.zero);
            StartCoroutine(Ring(transform.position, 0.85f, new Color(0.3f, 0.95f, 0.7f), Vector2.zero, 0.9f));
        }
        else if (id == RpgSkillCatalog.Ultimate)
        {
            if (ultimate == null) ultimate = gameObject.AddComponent<FlyingSwordUltimate>();
            ultimate.Begin(attack.EnemyLayer, Mathf.Max(1, Mathf.RoundToInt(attack.AttackPower * 0.35f)));
            PlayMotion("GuardCast", Vector2.zero);
        }
        OnFeedback?.Invoke(skill.Name); return true;
    }

    bool Fail(string message) { OnFeedback?.Invoke(message); return false; }

    void PlayMotion(string trigger, Vector2 direction)
    {
        var animator = GetComponent<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (direction.x != 0 && GetComponent<SpriteRenderer>() is SpriteRenderer renderer)
            renderer.flipX = direction.x < 0;
        foreach (var parameter in animator.parameters)
            if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == trigger)
            { animator.SetTrigger(trigger); return; }
    }

    void CancelVisuals()
    {
        if (ultimate != null) ultimate.Cancel();
        StopAllCoroutines();
        foreach (var effect in visualEffects) if (effect != null) { effect.SetActive(false); Destroy(effect); }
        foreach (var material in visualMaterials) if (material != null) Destroy(material);
        visualEffects.Clear(); visualMaterials.Clear();
        var animator = GetComponent<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            foreach (var parameter in animator.parameters)
                if (parameter.type == AnimatorControllerParameterType.Trigger &&
                    (parameter.name == "MoonSlash" || parameter.name == "GuardCast" || parameter.name == "RecoverCast"))
                    animator.ResetTrigger(parameter.name);
            if (animator.HasState(0, Animator.StringToHash("Base Layer.Idle"))) animator.Play("Base Layer.Idle");
            if (GetComponent<SpriteRenderer>() is SpriteRenderer renderer) renderer.color = Color.white;
        }
    }

    void OnDisable()
    {
        if (stats != null) stats.OnDied -= CancelVisuals;
        CancelVisuals();
    }

    IEnumerator Ring(Vector3 center, float radius, Color color, Vector2 direction, float duration)
    {
        var effect = new GameObject("Skill ripple");
        // Child ownership guarantees cleanup if the player is destroyed during the effect.
        effect.transform.SetParent(transform); effect.transform.position = center;
        visualEffects.Add(effect);
        var line = effect.AddComponent<LineRenderer>();
        bool slash = direction.sqrMagnitude > 0;
        line.useWorldSpace = false; line.loop = !slash; line.positionCount = 48;
        line.startWidth = slash ? 0.15f : 0.04f; line.endWidth = slash ? 0.02f : 0.04f;
        var sprite = GetComponent<SpriteRenderer>();
        line.sortingLayerID = sprite != null ? sprite.sortingLayerID : 0;
        line.sortingOrder = sprite != null ? sprite.sortingOrder + 1 : 40;
        var shader = Shader.Find("Sprites/Default");
        Material material = shader != null ? new Material(shader) : null;
        if (material != null) { line.sharedMaterial = material; visualMaterials.Add(material); }
        try
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                color.a = slash ? 1f - progress : Mathf.Min(1f, (1f - progress) * 5f) * (0.5f + 0.2f * Mathf.Sin(elapsed * 9f));
                line.startColor = line.endColor = color;
                for (int i = 0; i < 48; i++)
                {
                    float angle = slash ? Mathf.Atan2(direction.y, direction.x) + Mathf.Lerp(-1.2f, 1.2f, i / 47f) : i * Mathf.PI * 2f / 48;
                    line.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius * (0.5f + progress * 0.5f));
                }
                yield return null;
            }
        }
        finally
        {
            visualMaterials.Remove(material); visualEffects.Remove(effect);
            if (material != null) Destroy(material); if (effect != null) Destroy(effect);
        }
    }
}

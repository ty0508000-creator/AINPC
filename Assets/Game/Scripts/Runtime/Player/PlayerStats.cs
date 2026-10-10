using UnityEngine;
using UnityEngine.Serialization;
using System;

public class PlayerStats : MonoBehaviour, IDamageable
{
    public enum LifeState { Alive, Dead, Respawning }
    public LifeState State { get; private set; } = LifeState.Alive;
    public bool IsAlive => State == LifeState.Alive && HP > 0f;
    public bool IsDead => State == LifeState.Dead;
    public Vector3 CheckpointPosition { get; private set; }
    public string CheckpointScene { get; private set; }
    public event Action OnDied;
    public event Action OnRespawned;
    float protectedUntil;
    [Header("Level")]
    public int Level = 1;

    [Header("HP")]
    /// <summary>레벨·능력치로 늘어난 최대 체력. 저장되는 값이며 장비 보너스는 들어 있지 않다.</summary>
    [FormerlySerializedAs("MaxHP")] public float BaseMaxHP = 100f;
    public float MaxHP => BaseMaxHP + Gear[ItemStat.MaxHp];

    [Header("Mana")]
    [FormerlySerializedAs("MaxMana")] public float BaseMaxMana = 50f;
    public float MaxMana => BaseMaxMana + Gear[ItemStat.MaxMana];

    [Header("EXP")]
    public float MaxEXP = 100f;

    [Header("Level Growth")]
    [SerializeField, Min(0f)] private float hpIncreasePerLevel = 20f;
    [SerializeField, Min(0f)] private float manaIncreasePerLevel = 10f;
    [SerializeField, Min(1f)] private float expRequirementMultiplier = 1.5f;
    [SerializeField] private bool healToFullOnLevelUp = true;

    public float HP { get; private set; }
    public float Mana { get; private set; }
    public float EXP { get; private set; }
    public int StatPoints { get; private set; } = 5;
    public int SkillPoints { get; private set; } = 3;
    public int[] AttributeRanks { get; private set; } = new int[4];
    public int[] SkillRanks { get; private set; } = new int[RpgSkillCatalog.All.Length];
    public int AttackBonus => AttributeRanks[1] * 2 + SkillRanks[1] * 3 + SkillRanks[2] * 6 + Mathf.RoundToInt(Gear[ItemStat.Attack]);
    public int Defense => AttributeRanks[2] * 2 + SkillRanks[4] * 3 + SkillRanks[5] * 6 + Mathf.RoundToInt(Gear[ItemStat.Defense]);
    public float ManaRegen => 1f + SkillRanks[7] * 0.6f + SkillRanks[8] * 1.2f + Gear[ItemStat.ManaRegen];
    public event Action<int> OnLevelUp;

    public event Action OnStatsChanged;

    private Player_Attack playerAttack;
    private PlayerInventory inventory;
    static readonly EquipmentStats NoGear = new EquipmentStats();

    /// <summary>장착 장비 능력치 합계. 인벤토리가 없으면 전부 0.</summary>
    public EquipmentStats Gear => inventory != null ? inventory.Stats : NoGear;

    /// <summary>공격속도 배율. 기본 공격 간격·돌진·무공 쿨타임을 이 값으로 나눈다.</summary>
    public float AttackSpeed => Gear.AttackSpeed;

    void Awake()
    {
        playerAttack = GetComponent<Player_Attack>();
        CheckpointPosition = transform.position;
        CheckpointScene = gameObject.scene.path;
        inventory = GetComponent<PlayerInventory>();
        if (inventory == null) inventory = gameObject.AddComponent<PlayerInventory>();
        // 체력을 최대치로 자르기 전에 장비부터 읽어야 장비 체력만큼 깎이지 않는다.
        inventory.EnsureLoaded();
        inventory.EquipmentChanged += OnEquipmentChanged;
        Load();
        if (GetComponent<RpgSkillController>() == null) gameObject.AddComponent<RpgSkillController>();
        if (GetComponent<RpgUI>() == null) gameObject.AddComponent<RpgUI>();
    }

    public void TakeDamage(int damage)
    {
        if (damage <= 0 || !IsAlive || Time.time < protectedUntil) return;
        if (playerAttack != null && playerAttack.IsInvincible) return;
        if (UnityEngine.Random.value < Gear.Evasion) return;
        var skills = GetComponent<RpgSkillController>();
        float reduction = skills != null ? skills.DamageMultiplier : 1f;
        int received = Mathf.Max(1, Mathf.RoundToInt(damage * 100f / (100f + Defense * 4f) * reduction));
        HP = Mathf.Max(0f, HP - received);
        OnStatsChanged?.Invoke();

        if (HP <= 0f) OnDeath();
    }

    /// <summary>
    /// 플레이어가 주는 모든 피해가 거치는 곳. 치명타·보스 피해·흡혈을 적용해 대상에게 주고, 준 피해를 돌려준다.
    /// </summary>
    public int DealDamage(IDamageable target, int baseDamage)
    {
        if (target == null || baseDamage <= 0) return 0;
        float damage = baseDamage;
        if (UnityEngine.Random.value < Gear.CritChance) damage *= EquipmentStats.BaseCritDamage + Gear[ItemStat.CritDamage];
        if (target is Component component && (component.GetComponent<StageBoss>() != null || component is AshKingBoss))
            damage *= 1f + Gear[ItemStat.BossDamage];
        int dealt = Mathf.Max(1, Mathf.RoundToInt(damage));
        var monster = target as MonsterBase;
        float hpBefore = monster != null ? monster.CurrentHP : 0f;
        target.TakeDamage(dealt);
        // 흡혈은 실제로 깎은 체력만큼. 넘친 피해, 이미 쓰러졌거나 피해를 무시한 몬스터는 회복하지 않는다.
        float removed = monster != null ? hpBefore - monster.CurrentHP : dealt;
        if (Gear[ItemStat.LifeSteal] > 0f && removed > 0f) Heal(removed * Gear[ItemStat.LifeSteal]);
        return dealt;
    }

    /// <summary>몬스터를 쓰러뜨렸을 때. 처치 시 회복 옵션을 적용한다.</summary>
    public void OnKill()
    {
        if (Gear[ItemStat.KillHeal] > 0f) Heal(Gear[ItemStat.KillHeal]);
    }

    void OnEquipmentChanged()
    {
        HP = Mathf.Min(HP, MaxHP);
        Mana = Mathf.Min(Mana, MaxMana);
        OnStatsChanged?.Invoke();
    }

    public void Heal(float amount)
    {
        if (HP <= 0f || amount <= 0f) return;
        HP = Mathf.Min(MaxHP, HP + amount);
        OnStatsChanged?.Invoke();
    }

    public void UseMana(float amount)
    {
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        Mana = Mathf.Max(0f, Mana - amount);
        OnStatsChanged?.Invoke();
    }

    public void RestoreMana(float amount)
    {
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        Mana = Mathf.Min(MaxMana, Mana + amount);
        OnStatsChanged?.Invoke();
    }

    public void AddEXP(float amount)
    {
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        int before = Level;
        ApplyExperience(amount);
        Save();
        NotifyExperience(before);
    }

    // QuestManager commits the completed quest and this reward BEFORE notifying listeners.
    internal void ApplyExperience(float amount)
    {
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        EXP += amount * (1f + Gear[ItemStat.ExpGain]);
        while (EXP >= MaxEXP)
        {
            EXP -= MaxEXP;
            LevelUp();
        }
    }

    internal void NotifyExperience(int previousLevel)
    {
        OnStatsChanged?.Invoke();
        for (int level = previousLevel + 1; level <= Level; level++) OnLevelUp?.Invoke(level);
    }

    void LevelUp()
    {
        Level++;
        StatPoints += 3;
        SkillPoints++;
        BaseMaxHP += hpIncreasePerLevel;
        BaseMaxMana += manaIncreasePerLevel;
        MaxEXP = Mathf.Round(MaxEXP * expRequirementMultiplier);

        if (healToFullOnLevelUp && IsAlive)
        {
            HP = MaxHP;
            Mana = MaxMana;
        }

    }

    void OnDeath()
    {
        if (State != LifeState.Alive) return;
        State = LifeState.Dead;
        StopCombat();
        FindFirstObjectByType<DialogueManager>()?.CloseDialogue();
        GetComponent<InnerVoiceManager>()?.CloseForDeath();
        Save();
        OnDied?.Invoke();
    }

    void StopCombat()
    {
        GetComponent<AIController>()?.StopForRecovery();
        GetComponent<Player_Attack>()?.CancelActions();
        var body = GetComponent<Rigidbody2D>();
        if (body != null) { body.linearVelocity = Vector2.zero; body.angularVelocity = 0f; }
    }

    public bool SetCheckpoint(Vector3 position)
    {
        if (!IsAlive || !float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z)) return false;
        CheckpointPosition = position;
        CheckpointScene = gameObject.scene.path;
        Save();
        return true;
    }

    public bool Respawn()
    {
        if (State != LifeState.Dead) return false;
        State = LifeState.Respawning;
        StopCombat();
        transform.position = CheckpointPosition;
        var body = GetComponent<Rigidbody2D>();
        if (body != null) body.position = CheckpointPosition;
        GetComponent<MoodSystem>()?.ReleaseControlForRecovery();
        GetComponent<ControlManager>()?.RestoreForRecovery();
        GetComponent<RpgSkillController>()?.ResetForRecovery();
        HP = MaxHP; Mana = MaxMana;
        protectedUntil = Time.time + 2f;
        State = LifeState.Alive;
        Physics2D.SyncTransforms();
        Save();
        OnStatsChanged?.Invoke();
        OnRespawned?.Invoke();
        return true;
    }

    public void Save() => SaveSystem.SavePlayer(this);

    public bool UpgradeAttribute(RpgAttribute attribute)
    {
        if (!IsAlive) return false;
        int id = (int)attribute;
        if (StatPoints < 1 || id < 0 || id >= AttributeRanks.Length) return false;
        StatPoints--; AttributeRanks[id]++;
        if (attribute == RpgAttribute.Vitality) { BaseMaxHP += 20f; if (HP > 0f) HP += 20f; }
        if (attribute == RpgAttribute.Spirit) { BaseMaxMana += 10f; Mana += 10f; }
        OnStatsChanged?.Invoke(); Save(); return true;
    }

    public string SkillLockReason(int id)
    {
        if (!IsAlive) return "재도전 후 수련할 수 있습니다.";
        if (id < 0 || id >= RpgSkillCatalog.All.Length) return "존재하지 않는 무공";
        var skill = RpgSkillCatalog.All[id];
        if (SkillRanks[id] >= skill.MaxRank) return "최고 단계";
        if (id == RpgSkillCatalog.Ultimate)
        {
            int mastered = 0;
            for (int i = 0; i < RpgSkillCatalog.Ultimate; i++)
                if (SkillRanks[i] >= RpgSkillCatalog.All[i].MaxRank) mastered++;
            if (mastered < RpgSkillCatalog.Ultimate)
                return $"모든 무공 최고 단계 필요 ({mastered}/{RpgSkillCatalog.Ultimate})";
        }
        if (Level < skill.RequiredLevel) return $"레벨 {skill.RequiredLevel} 필요";
        if (skill.Prerequisite >= 0 && SkillRanks[skill.Prerequisite] == 0)
            return RpgSkillCatalog.All[skill.Prerequisite].Name + " 습득 필요";
        return SkillPoints > 0 ? "" : "무공 포인트 부족";
    }

    public bool LearnSkill(int id)
    {
        if (SkillLockReason(id).Length > 0) return false;
        SkillPoints--; SkillRanks[id]++;
        OnStatsChanged?.Invoke(); Save(); return true;
    }

    public bool TrySpendMana(float amount)
    {
        if (amount < 0f || float.IsNaN(amount) || float.IsInfinity(amount) || Mana < amount || HP <= 0f) return false;
        UseMana(amount); return true;
    }

    void Update()
    {
        if (HP > 0f && Mana < MaxMana && Time.deltaTime > 0f) RestoreMana(ManaRegen * Time.deltaTime);
        if (HP > 0f && HP < MaxHP && Gear[ItemStat.HpRegen] > 0f && Time.deltaTime > 0f) Heal(Gear[ItemStat.HpRegen] * Time.deltaTime);
    }

    void Load()
    {
        var data = SaveSystem.LoadPlayer();
        if (data == null)
        {
            HP = MaxHP;
            Mana = MaxMana;
            return;
        }

        Level  = Mathf.Max(1, data.level);
        BaseMaxHP = Mathf.Max(1f, data.maxHP);
        HP     = Mathf.Clamp(data.hp, 0f, MaxHP);
        State = HP <= 0f ? LifeState.Dead : LifeState.Alive;
        if (data.hasCheckpoint && data.checkpointScene == gameObject.scene.path)
            CheckpointPosition = data.checkpointPosition;
        BaseMaxMana = Mathf.Max(1f, data.maxMana);
        Mana   = Mathf.Clamp(data.mana, 0f, MaxMana);
        MaxEXP = Mathf.Max(1f, data.maxEXP);
        EXP    = Mathf.Max(0f, data.exp);
        if (data.progressionVersion >= 1)
        {
            StatPoints = Mathf.Max(0, data.statPoints);
            SkillPoints = Mathf.Max(0, data.skillPoints);
            for (int i = 0; i < AttributeRanks.Length; i++)
                AttributeRanks[i] = Mathf.Max(0, RpgSkillCatalog.Rank(data.attributeRanks, i));
            for (int i = 0; i < SkillRanks.Length; i++)
                SkillRanks[i] = Mathf.Clamp(RpgSkillCatalog.Rank(data.skillRanks, i), 0, RpgSkillCatalog.All[i].MaxRank);
        }
        else
        {
            StatPoints = 5 + (Level - 1) * 3;
            SkillPoints = 3 + Level - 1;
        }
    }

    void OnApplicationQuit() => SaveSystem.SavePlayer(this);
}

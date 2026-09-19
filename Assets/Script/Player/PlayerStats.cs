using UnityEngine;
using System;

public class PlayerStats : MonoBehaviour, IDamageable
{
    [Header("Level")]
    public int Level = 1;

    [Header("HP")]
    public float MaxHP = 100f;

    [Header("Mana")]
    public float MaxMana = 50f;

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
    public int[] SkillRanks { get; private set; } = new int[9];
    public int AttackBonus => AttributeRanks[1] * 2 + SkillRanks[1] * 3 + SkillRanks[2] * 6;
    public int Defense => AttributeRanks[2] * 2 + SkillRanks[4] * 3 + SkillRanks[5] * 6;
    public float ManaRegen => 1f + SkillRanks[7] * 0.6f + SkillRanks[8] * 1.2f;
    public event Action<int> OnLevelUp;

    public event Action OnStatsChanged;

    private Player_Attack playerAttack;

    void Awake()
    {
        playerAttack = GetComponent<Player_Attack>();
        Load();
        if (GetComponent<RpgSkillController>() == null) gameObject.AddComponent<RpgSkillController>();
        if (GetComponent<RpgUI>() == null) gameObject.AddComponent<RpgUI>();
    }

    public void TakeDamage(int damage)
    {
        if (damage <= 0 || HP <= 0f) return;
        if (playerAttack != null && playerAttack.IsInvincible) return;
        var skills = GetComponent<RpgSkillController>();
        float reduction = skills != null ? skills.DamageMultiplier : 1f;
        int received = Mathf.Max(1, Mathf.RoundToInt(damage * 100f / (100f + Defense * 4f) * reduction));
        HP = Mathf.Max(0f, HP - received);
        OnStatsChanged?.Invoke();

        if (HP <= 0f) OnDeath();
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
        EXP += amount;
        while (EXP >= MaxEXP)
        {
            EXP -= MaxEXP;
            LevelUp();
        }
        OnStatsChanged?.Invoke();
        Save();
    }

    void LevelUp()
    {
        Level++;
        StatPoints += 3;
        SkillPoints++;
        MaxHP += hpIncreasePerLevel;
        MaxMana += manaIncreasePerLevel;
        MaxEXP = Mathf.Round(MaxEXP * expRequirementMultiplier);

        if (healToFullOnLevelUp)
        {
            HP = MaxHP;
            Mana = MaxMana;
        }

        SaveSystem.SavePlayer(this);
        OnLevelUp?.Invoke(Level);
    }

    void OnDeath()
    {
        Debug.Log("Player died");
    }

    public void Save() => SaveSystem.SavePlayer(this);

    public bool UpgradeAttribute(RpgAttribute attribute)
    {
        int id = (int)attribute;
        if (StatPoints < 1 || id < 0 || id >= AttributeRanks.Length) return false;
        StatPoints--; AttributeRanks[id]++;
        if (attribute == RpgAttribute.Vitality) { MaxHP += 20f; if (HP > 0f) HP += 20f; }
        if (attribute == RpgAttribute.Spirit) { MaxMana += 10f; Mana += 10f; }
        OnStatsChanged?.Invoke(); Save(); return true;
    }

    public string SkillLockReason(int id)
    {
        if (id < 0 || id >= RpgSkillCatalog.All.Length) return "존재하지 않는 무공";
        var skill = RpgSkillCatalog.All[id];
        if (SkillRanks[id] >= skill.MaxRank) return "최고 단계";
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
        MaxHP  = Mathf.Max(1f, data.maxHP);
        HP     = Mathf.Clamp(data.hp, 0f, MaxHP);
        MaxMana = Mathf.Max(1f, data.maxMana);
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

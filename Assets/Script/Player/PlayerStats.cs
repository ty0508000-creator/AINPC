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

    /// <summary>HP 가 0 이 된 뒤인가. 죽은 몸으로는 더 맞지도, 싸우지도 않는다.</summary>
    public bool IsDead { get; private set; }

    public event Action OnStatsChanged;

    /// <summary>죽는 순간 한 번 호출된다. AI 조종처럼 몸을 쓰던 쪽이 손을 떼는 신호.</summary>
    public event Action OnDied;

    /// <summary>되살아난 순간 호출된다.</summary>
    public event Action OnRevived;

    private Player_Attack playerAttack;

    void Awake()
    {
        playerAttack = GetComponent<Player_Attack>();
        Load();
    }

    public void TakeDamage(int damage)
    {
        if (IsDead) return;
        if (playerAttack != null && playerAttack.IsInvincible) return;

        HP = Mathf.Max(0f, HP - damage);
        OnStatsChanged?.Invoke();

        if (HP <= 0f) OnDeath();
    }

    public void Heal(float amount)
    {
        HP = Mathf.Min(MaxHP, HP + amount);
        OnStatsChanged?.Invoke();
    }

    public void UseMana(float amount)
    {
        Mana = Mathf.Max(0f, Mana - amount);
        OnStatsChanged?.Invoke();
    }

    public void RestoreMana(float amount)
    {
        Mana = Mathf.Min(MaxMana, Mana + amount);
        OnStatsChanged?.Invoke();
    }

    public void AddEXP(float amount)
    {
        EXP += amount;
        while (EXP >= MaxEXP)
        {
            EXP -= MaxEXP;
            LevelUp();
        }
        OnStatsChanged?.Invoke();
    }

    void LevelUp()
    {
        Level++;
        MaxHP += hpIncreasePerLevel;
        MaxMana += manaIncreasePerLevel;
        MaxEXP = Mathf.Round(MaxEXP * expRequirementMultiplier);

        if (healToFullOnLevelUp)
        {
            HP = MaxHP;
            Mana = MaxMana;
        }

        SaveSystem.SavePlayer(this);
    }

    void OnDeath()
    {
        if (IsDead) return;

        IsDead = true;
        Debug.Log("Player died");
        OnDied?.Invoke();
    }

    /// <summary>되살린다. HP 는 비율만큼(최소 1) 채우고 마나는 가득 채운다.</summary>
    public void Revive(float hpRatio = 1f)
    {
        IsDead = false;
        HP = Mathf.Clamp(MaxHP * Mathf.Clamp01(hpRatio), 1f, MaxHP);
        Mana = MaxMana;

        OnStatsChanged?.Invoke();
        OnRevived?.Invoke();
    }

    public void Save() => SaveSystem.SavePlayer(this);

    void Load()
    {
        var data = SaveSystem.LoadPlayer();
        if (data == null)
        {
            HP = MaxHP;
            Mana = MaxMana;
            return;
        }

        Level  = data.level;
        MaxHP  = data.maxHP;
        // HP 0 으로 저장돼 있으면 부활할 방법이 없으니 가득 채워서 시작한다
        HP     = data.hp > 0f ? data.hp : data.maxHP;
        MaxMana = data.maxMana;
        Mana   = data.mana;
        MaxEXP = data.maxEXP;
        EXP    = data.exp;
    }

    void OnApplicationQuit() => SaveSystem.SavePlayer(this);
}

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

    public float HP { get; private set; }
    public float Mana { get; private set; }
    public float EXP { get; private set; }

    public event Action OnStatsChanged;

    private Player_Attack playerAttack;

    void Awake()
    {
        playerAttack = GetComponent<Player_Attack>();
        Load();
    }

    public void TakeDamage(int damage)
    {
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
        MaxHP += 20f;
        MaxMana += 10f;
        MaxEXP = Mathf.Round(MaxEXP * 1.5f);
        HP = MaxHP;
        Mana = MaxMana;
        SaveSystem.SavePlayer(this);
    }

    void OnDeath()
    {
        Debug.Log("Player died");
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
        HP     = data.hp;
        MaxMana = data.maxMana;
        Mana   = data.mana;
        MaxEXP = data.maxEXP;
        EXP    = data.exp;
    }

    void OnApplicationQuit() => SaveSystem.SavePlayer(this);
}

using UnityEngine;

[CreateAssetMenu(fileName = "MonsterData", menuName = "AINPC/Monster Data")]
public class MonsterData : ScriptableObject
{
    [SerializeField] private string monsterName = "Monster";
    [SerializeField, Min(1)] private int level = 1;
    [SerializeField, Min(1f)] private float maxHP = 30f;
    [SerializeField, Min(0f)] private float expReward = 10f;
    [SerializeField, Min(0)] private int attackDamage = 5;
    [SerializeField, Min(0.1f)] private float attackRange = 1f;
    [SerializeField, Min(0.1f)] private float attackCooldown = 1f;
    [SerializeField, Min(0f)] private float moveSpeed = 2f;
    [SerializeField, Min(0f)] private float aggroRange = 5f;

    public string MonsterName => monsterName;
    public int Level => level;
    public float MaxHP => maxHP;
    public float ExpReward => expReward;
    public int AttackDamage => attackDamage;
    public float AttackRange => attackRange;
    public float AttackCooldown => attackCooldown;
    public float MoveSpeed => moveSpeed;
    public float AggroRange => aggroRange;
}

using UnityEngine;

public class MonsterSpawnPoint : MonoBehaviour
{
    private MonsterBase currentMonster;

    public bool IsEmpty => currentMonster == null;
    public MonsterBase CurrentMonster => currentMonster;
    public float LastClearedTime { get; private set; } = float.NegativeInfinity;

    public MonsterBase Spawn(MonsterBase prefab, MonsterData data)
    {
        if (!IsEmpty || prefab == null)
            return currentMonster;

        currentMonster = Instantiate(prefab, transform.position, transform.rotation);
        currentMonster.Initialize(data, this);
        return currentMonster;
    }

    public void Clear(MonsterBase monster)
    {
        if (ReferenceEquals(currentMonster, monster))
        {
            currentMonster = null;
            LastClearedTime = Time.time;
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = IsEmpty ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.25f);
    }
}

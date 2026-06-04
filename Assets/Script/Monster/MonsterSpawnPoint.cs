using UnityEngine;

public class MonsterSpawnPoint : MonoBehaviour
{
    private MonsterBase currentMonster;

    public bool IsEmpty => currentMonster == null;
    public MonsterBase CurrentMonster => currentMonster;

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
        if (currentMonster == monster)
            currentMonster = null;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = IsEmpty ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.25f);
    }
}

using System.Collections;
using UnityEngine;

public class MonsterSpawnArea : MonoBehaviour
{
    [SerializeField] private MonsterBase monsterPrefab;
    [SerializeField] private MonsterData monsterData;
    [SerializeField, Min(0.1f)] private float spawnInterval = 10f;
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField] private MonsterSpawnPoint[] spawnPoints;

    private Coroutine spawnCoroutine;

    private void Awake()
    {
        RefreshSpawnPointsIfNeeded();
    }

    private void OnEnable()
    {
        RefreshSpawnPointsIfNeeded();

        if (spawnOnStart)
            SpawnEmptyPoints();

        spawnCoroutine = StartCoroutine(SpawnLoop());
    }

    private void OnDisable()
    {
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }
    }

    public void SpawnEmptyPoints()
    {
        RefreshSpawnPointsIfNeeded();

        if (monsterPrefab == null || monsterData == null || spawnPoints == null)
            return;

        foreach (MonsterSpawnPoint point in spawnPoints)
        {
            if (point != null && point.IsEmpty)
                point.Spawn(monsterPrefab, monsterData);
        }
    }

    private void OnValidate()
    {
        RefreshSpawnPointsIfNeeded();
    }

    private void RefreshSpawnPointsIfNeeded()
    {
        if (HasAnySpawnPoint())
            return;

        MonsterSpawnPoint[] childSpawnPoints = GetComponentsInChildren<MonsterSpawnPoint>();
        if (childSpawnPoints.Length > 0)
            spawnPoints = childSpawnPoints;
    }

    private bool HasAnySpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return false;

        foreach (MonsterSpawnPoint point in spawnPoints)
        {
            if (point != null)
                return true;
        }

        return false;
    }

    private IEnumerator SpawnLoop()
    {
        while (enabled)
        {
            yield return new WaitForSeconds(spawnInterval);
            SpawnEmptyPoints();
        }
    }
}

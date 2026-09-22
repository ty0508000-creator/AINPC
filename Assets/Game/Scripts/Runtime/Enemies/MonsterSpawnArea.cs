using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MonsterSpawnArea : MonoBehaviour
{
    [SerializeField] private MonsterBase monsterPrefab;
    [SerializeField] private MonsterData monsterData;
    [SerializeField, Min(0.1f)] private float spawnInterval = 10f;
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField] private MonsterSpawnPoint[] spawnPoints;
    [Header("Proximity Spawn")]
    [SerializeField, Min(1f)] private float cameraRangeMultiplier = 2f;
    [SerializeField, Min(0.1f)] private float activationCheckInterval = 0.5f;

    private Coroutine spawnCoroutine;
    private readonly HashSet<MonsterSpawnPoint> activatedPoints = new HashSet<MonsterSpawnPoint>();
    private PlayerStats player;
    private float nextActivationCheckTime;

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

    private void Update()
    {
        if (!spawnOnStart || Time.time < nextActivationCheckTime)
            return;

        nextActivationCheckTime = Time.time + activationCheckInterval;
        SpawnNearbyPoints(true);
    }

    public void SpawnEmptyPoints()
    {
        SpawnNearbyPoints(false);
    }

    private void SpawnNearbyPoints(bool onlyUnactivated)
    {
        RefreshSpawnPointsIfNeeded();

        if (monsterPrefab == null || monsterData == null || spawnPoints == null)
            return;

        foreach (MonsterSpawnPoint point in spawnPoints)
        {
            if (point == null || !point.IsEmpty || !IsWithinSpawnRange(point))
                continue;

            if (onlyUnactivated && activatedPoints.Contains(point))
                continue;

            point.Spawn(monsterPrefab, monsterData);
            activatedPoints.Add(point);
        }
    }

    private bool IsWithinSpawnRange(MonsterSpawnPoint point)
    {
        if (player == null)
            player = FindFirstObjectByType<PlayerStats>();

        Camera camera = Camera.main;
        if (player == null || camera == null || !camera.orthographic)
            return false;

        return IsWithinCameraSpawnRange(
            player.transform.position,
            point.transform.position,
            camera.orthographicSize,
            camera.aspect,
            cameraRangeMultiplier);
    }

    private static bool IsWithinCameraSpawnRange(
        Vector3 playerPosition,
        Vector3 spawnPosition,
        float cameraHalfHeight,
        float cameraAspect,
        float rangeMultiplier)
    {
        float halfHeight = cameraHalfHeight * rangeMultiplier;
        float halfWidth = halfHeight * cameraAspect;
        Vector3 offset = spawnPosition - playerPosition;

        return Mathf.Abs(offset.x) <= halfWidth && Mathf.Abs(offset.y) <= halfHeight;
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

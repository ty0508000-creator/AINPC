using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float smoothSpeed = 5f;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);

    void Awake()
    {
        FindTargetIfMissing();
    }

    void LateUpdate()
    {
        if (target == null)
            FindTargetIfMissing();

        if (target == null)
            return;

        Vector3 desired = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
    }

    private void FindTargetIfMissing()
    {
        if (target != null)
            return;

        PlayerStats playerStats = FindFirstObjectByType<PlayerStats>();
        if (playerStats != null)
            target = playerStats.transform;
    }
}

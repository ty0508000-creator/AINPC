using System.Collections;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float smoothSpeed = 5f;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);

    [Tooltip("대상과 이만큼 넘게 떨어지면 따라가지 않고 바로 옮긴다 (부활, 순간이동)")]
    [SerializeField] private float snapDistance = 30f;

    private Vector3 followPos;       // 흔들림을 제외한 순수 추적 위치
    private Vector3 shakeOffset;
    private Coroutine shakeRoutine;

    void Start()
    {
        followPos = transform.position;
    }

    void LateUpdate()
    {
        if (target == null)
            FindTargetIfMissing();

        if (target == null)
            return;

        Vector3 desired = target.position + offset;
        if ((desired - followPos).sqrMagnitude > snapDistance * snapDistance)
            followPos = desired;
        else
            followPos = Vector3.Lerp(followPos, desired, smoothSpeed * Time.deltaTime);
        transform.position = followPos + shakeOffset;
    }

    /// <summary>따라가는 연출 없이 대상 위치로 바로 옮긴다.</summary>
    public void SnapToTarget()
    {
        if (target == null)
            FindTargetIfMissing();

        if (target == null)
            return;

        followPos = target.position + offset;
        transform.position = followPos + shakeOffset;
    }

    /// <summary>카메라를 흔든다. timeScale=0 에서도 동작 (unscaled time).</summary>
    public void Shake(float duration, float magnitude)
    {
        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(ShakeRoutine(duration, magnitude));
    }

    IEnumerator ShakeRoutine(float duration, float magnitude)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float damper = 1f - Mathf.Clamp01(t / duration);   // 점점 약해짐
            shakeOffset = (Vector3)(Random.insideUnitCircle * magnitude * damper);
            yield return null;
        }
        shakeOffset = Vector3.zero;
        shakeRoutine = null;
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

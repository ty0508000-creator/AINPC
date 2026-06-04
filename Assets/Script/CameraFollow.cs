using System.Collections;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float smoothSpeed = 5f;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);

    private Vector3 followPos;       // 흔들림을 제외한 순수 추적 위치
    private Vector3 shakeOffset;
    private Coroutine shakeRoutine;

    void Start()
    {
        followPos = transform.position;
    }

    void LateUpdate()
    {
        Vector3 desired = target.position + offset;
        followPos = Vector3.Lerp(followPos, desired, smoothSpeed * Time.deltaTime);
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
}

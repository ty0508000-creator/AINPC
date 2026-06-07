using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 제어권 탈취/회복 순간을 극적으로 연출한다.
/// 적색 화면 플래시 + 카메라 흔들림 + 인격의 한마디 + (선택)효과음.
/// ControlManager 와 같은 GameObject(플레이어)에 붙인다.
/// </summary>
[RequireComponent(typeof(ControlManager))]
public class TakeoverEffect : MonoBehaviour
{
    [Header("Camera Shake")]
    [SerializeField] private float shakeDuration = 0.6f;
    [SerializeField] private float shakeMagnitude = 0.4f;

    [Header("Tint")]
    [SerializeField] private Color takeoverColor = new Color(0.85f, 0f, 0f);
    [SerializeField] private float flashAlpha = 0.55f;
    [SerializeField] private float residualAlpha = 0.12f;   // AI 제어 중 유지되는 옅은 적색
    [SerializeField] private Color restoreColor = new Color(0.4f, 0.7f, 1f);

    [Header("Audio (선택)")]
    [SerializeField] private AudioClip takeoverSfx;
    [SerializeField] private AudioClip restoreSfx;

    [Header("Text")]
    [SerializeField] private TMP_FontAsset koreanFont;
    [SerializeField] private float lineFontSize = 84f;

    [Header("Lines")]
    [SerializeField] private string[] takeoverLines =
    {
        "이제 내 차례야.",
        "약해빠졌군. 비켜.",
        "네 몸은 이제 내 거야.",
    };
    [SerializeField] private string[] restoreLines =
    {
        "쳇... 다음엔 안 놔줘.",
        "운이 좋았어.",
    };

    private ControlManager controlManager;
    private CameraFollow cameraFollow;
    private AudioSource audioSource;

    private Image tintImage;
    private TMP_Text lineText;
    private Coroutine effectRoutine;

    void Start()
    {
        controlManager = GetComponent<ControlManager>();
        cameraFollow   = FindFirstObjectByType<CameraFollow>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        BuildUI();

        controlManager.OnAITakeover     += HandleTakeover;
        controlManager.OnPlayerRestored += HandleRestored;
    }

    void HandleTakeover()
    {
        Play(takeoverColor, flashAlpha, residualAlpha, Pick(takeoverLines), takeoverSfx);
        if (cameraFollow != null) cameraFollow.Shake(shakeDuration, shakeMagnitude);
    }

    void HandleRestored()
    {
        Play(restoreColor, flashAlpha * 0.7f, 0f, Pick(restoreLines), restoreSfx);
    }

    void Play(Color color, float peak, float residual, string line, AudioClip sfx)
    {
        if (sfx != null) audioSource.PlayOneShot(sfx);
        lineText.text = line;
        if (effectRoutine != null) StopCoroutine(effectRoutine);
        effectRoutine = StartCoroutine(EffectRoutine(color, peak, residual));
    }

    IEnumerator EffectRoutine(Color color, float peak, float residual)
    {
        // 1) 확 차오름
        yield return Fade(color, 0f, peak, 0.12f);
        // 2) 잔광까지 서서히 빠짐
        yield return Fade(color, peak, residual, 0.8f);

        // 대사 표시 유지 후 페이드아웃
        SetLineAlpha(1f);
        yield return WaitUnscaled(1.2f);
        float ft = 0f;
        while (ft < 0.5f)
        {
            ft += Time.unscaledDeltaTime;
            SetLineAlpha(Mathf.Lerp(1f, 0f, ft / 0.5f));
            yield return null;
        }
        SetLineAlpha(0f);
        effectRoutine = null;
    }

    IEnumerator Fade(Color color, float from, float to, float dur)
    {
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(from, to, t / dur);
            tintImage.color = new Color(color.r, color.g, color.b, a);
            yield return null;
        }
        tintImage.color = new Color(color.r, color.g, color.b, to);
    }

    IEnumerator WaitUnscaled(float seconds)
    {
        float t = 0f;
        while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
    }

    void SetLineAlpha(float a)
    {
        var c = lineText.color; c.a = a; lineText.color = c;
    }

    string Pick(string[] arr) =>
        (arr == null || arr.Length == 0) ? "" : arr[Random.Range(0, arr.Length)];

    void BuildUI()
    {
        var canvasGO = new GameObject("TakeoverCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;   // 내면 오버레이(300)보다 위
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // 전체화면 틴트 (클릭 통과)
        var tintGO = new GameObject("Tint");
        tintGO.transform.SetParent(canvasGO.transform, false);
        tintImage = tintGO.AddComponent<Image>();
        tintImage.color = new Color(0f, 0f, 0f, 0f);
        tintImage.raycastTarget = false;
        var r = tintGO.GetComponent<RectTransform>();
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;

        // 대사
        var lineGO = new GameObject("TakeoverLine");
        lineGO.transform.SetParent(canvasGO.transform, false);
        lineText = lineGO.AddComponent<TextMeshProUGUI>();
        lineText.text = "";
        if (koreanFont != null)
            lineText.font = koreanFont;
        lineText.fontSize = lineFontSize;
        lineText.fontStyle = FontStyles.Bold | FontStyles.Italic;
        lineText.alignment = TextAlignmentOptions.Center;
        lineText.color = new Color(1f, 0.85f, 0.85f, 0f);
        lineText.raycastTarget = false;
        var lr = lineGO.GetComponent<RectTransform>();
        lr.anchorMin = new Vector2(0.5f, 0.62f);
        lr.anchorMax = new Vector2(0.5f, 0.62f);
        lr.pivot = new Vector2(0.5f, 0.5f);
        lr.sizeDelta = new Vector2(1600f, 180f);
    }

    void OnDestroy()
    {
        if (controlManager == null) return;
        controlManager.OnAITakeover     -= HandleTakeover;
        controlManager.OnPlayerRestored -= HandleRestored;
    }
}

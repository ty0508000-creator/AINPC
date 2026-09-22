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

    [Header("Glitch")]
    [SerializeField] private bool enableGlitch = true;
    [SerializeField] private int glitchStripCount = 9;
    [SerializeField] private float glitchDuration = 0.5f;
    [SerializeField] private float glitchMaxOffset = 55f;
    [SerializeField] private float glitchMaxAlpha = 0.42f;
    [SerializeField] private Color glitchWhite = new Color(1f, 1f, 1f, 0.38f);
    [SerializeField] private Color glitchBlack = new Color(0f, 0f, 0f, 0.35f);

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
    [SerializeField] private GameObject ownedCanvas;
    private CameraFollow cameraFollow;
    private AudioSource audioSource;

    [SerializeField] private Image tintImage;
    [SerializeField] private RectTransform glitchRoot;
    [SerializeField] private Image[] glitchStrips;
    [SerializeField] private RectTransform lineRect;
    private Vector2 lineBasePosition;
    [SerializeField] private TMP_Text lineText;
    private Coroutine effectRoutine;
    private Coroutine glitchRoutine;

    void Start()
    {
        controlManager = GetComponent<ControlManager>();
        cameraFollow   = FindFirstObjectByType<CameraFollow>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        if (ownedCanvas == null) { enabled = false; return; }
        lineBasePosition = lineRect.anchoredPosition;
        SetLineAlpha(0f); HideGlitch();

        controlManager.OnAITakeover     += HandleTakeover;
        controlManager.OnPlayerRestored += HandleRestored;
    }

    void HandleTakeover()
    {
        Play(takeoverColor, flashAlpha, residualAlpha, Pick(takeoverLines), takeoverSfx);
        PlayGlitch();
        if (cameraFollow != null) cameraFollow.Shake(shakeDuration, shakeMagnitude);
    }

    void HandleRestored()
    {
        if (glitchRoutine != null) StopCoroutine(glitchRoutine);
        HideGlitch();
        glitchRoutine = null;
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

    void PlayGlitch()
    {
        if (!enableGlitch || glitchRoot == null || glitchStrips == null || glitchStrips.Length == 0)
            return;

        if (glitchRoutine != null) StopCoroutine(glitchRoutine);
        glitchRoutine = StartCoroutine(GlitchRoutine());
    }

    IEnumerator GlitchRoutine()
    {
        glitchRoot.gameObject.SetActive(true);
        float elapsed = 0f;

        while (elapsed < glitchDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float strength = 1f - Mathf.Clamp01(elapsed / glitchDuration);

            for (int i = 0; i < glitchStrips.Length; i++)
            {
                var strip = glitchStrips[i];
                if (strip == null) continue;

                var rect = strip.GetComponent<RectTransform>();
                float y = Random.Range(0.05f, 0.95f);
                rect.anchorMin = new Vector2(0f, y);
                rect.anchorMax = new Vector2(1f, y);
                rect.anchoredPosition = new Vector2(Random.Range(-glitchMaxOffset, glitchMaxOffset) * strength, 0f);
                rect.sizeDelta = new Vector2(0f, Random.Range(2f, 14f));

                Color color = Random.value > 0.5f ? glitchWhite : glitchBlack;
                color.a = Random.Range(0.08f, glitchMaxAlpha) * strength;
                strip.color = color;
                strip.enabled = Random.value > 0.28f;
            }

            if (lineRect != null)
                lineRect.anchoredPosition = lineBasePosition + new Vector2(Random.Range(-24f, 24f) * strength, Random.Range(-8f, 8f) * strength);

            yield return null;
        }

        HideGlitch();
        glitchRoutine = null;
    }

    void HideGlitch()
    {
        if (glitchStrips != null)
        {
            foreach (var strip in glitchStrips)
            {
                if (strip == null) continue;
                strip.enabled = false;
                strip.color = Color.clear;
            }
        }

        if (lineRect != null)
            lineRect.anchoredPosition = lineBasePosition;
        if (glitchRoot != null)
            glitchRoot.gameObject.SetActive(false);
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

#if UNITY_EDITOR
    public void BakeSceneUI()
    {
        if (ownedCanvas != null) return;
        if (koreanFont == null) koreanFont = Resources.Load<TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF");
        BuildUI();
    }
#endif
    void BuildUI()
    {
        var canvasGO = new GameObject("TakeoverCanvas");
        ownedCanvas = canvasGO;
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

        var glitchGO = new GameObject("Glitch");
        glitchGO.transform.SetParent(canvasGO.transform, false);
        glitchRoot = glitchGO.AddComponent<RectTransform>();
        glitchRoot.anchorMin = Vector2.zero;
        glitchRoot.anchorMax = Vector2.one;
        glitchRoot.offsetMin = Vector2.zero;
        glitchRoot.offsetMax = Vector2.zero;
        glitchRoot.gameObject.SetActive(false);

        int count = Mathf.Max(0, glitchStripCount);
        glitchStrips = new Image[count];
        for (int i = 0; i < count; i++)
        {
            var stripGO = new GameObject($"GlitchStrip_{i:00}");
            stripGO.transform.SetParent(glitchRoot, false);
            var strip = stripGO.AddComponent<Image>();
            strip.color = Color.clear;
            strip.raycastTarget = false;
            strip.enabled = false;

            var sr = stripGO.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(0f, 0.5f);
            sr.anchorMax = new Vector2(1f, 0.5f);
            sr.pivot = new Vector2(0.5f, 0.5f);
            sr.anchoredPosition = Vector2.zero;
            sr.sizeDelta = new Vector2(0f, 12f);

            glitchStrips[i] = strip;
        }

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
        lineRect = lineGO.GetComponent<RectTransform>();
        lineRect.anchorMin = new Vector2(0.5f, 0.62f);
        lineRect.anchorMax = new Vector2(0.5f, 0.62f);
        lineRect.pivot = new Vector2(0.5f, 0.5f);
        lineRect.sizeDelta = new Vector2(1600f, 180f);
        lineBasePosition = lineRect.anchoredPosition;
    }

    void OnDestroy()
    {
        // 연출 캔버스는 씬과 함께 정리된다.
        if (controlManager == null) return;
        controlManager.OnAITakeover     -= HandleTakeover;
        controlManager.OnPlayerRestored -= HandleRestored;
    }
}

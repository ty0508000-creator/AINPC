using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 신뢰가 쌓인 어둠이 몸을 달라고 요청할 때 띄우는 한 줄 안내와 Y/N 입력. 게임은 멈추지 않는다.
/// 답이 없으면 EmotionSystem 이 시간 초과로 닫는다. EmotionSystem 이 실행 중에 붙인다.
/// </summary>
public class TakeoverRequestPrompt : MonoBehaviour
{
    EmotionSystem emotions;
    GameObject root;
    TMP_Text label;

    void Start()
    {
        emotions = GetComponent<EmotionSystem>();
        if (emotions == null) { enabled = false; return; }
        Build();
        emotions.OnTakeoverRequested += Show;
        emotions.OnRequestClosed += Hide;
    }

    void OnDestroy()
    {
        if (emotions == null) return;
        emotions.OnTakeoverRequested -= Show;
        emotions.OnRequestClosed -= Hide;
        if (root != null) Destroy(root.transform.parent.gameObject);
    }

    void Update()
    {
        if (emotions == null || !emotions.RequestPending || Keyboard.current == null) return;
        // 대화창에 글자를 치는 중이면 Y/N 을 답으로 받지 않는다
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null && selected.GetComponent<TMP_InputField>() != null) return;
        if (Keyboard.current.yKey.wasPressedThisFrame) emotions.AnswerRequest(true);
        else if (Keyboard.current.nKey.wasPressedThisFrame) emotions.AnswerRequest(false);
    }

    void Show()
    {
        label.text = emotions.Fear >= emotions.Anger
            ? "어둠: 이대로면 또 잃는다. 몸을 맡겨라.   [Y] 맡긴다   [N] 거절"
            : "어둠: 내가 끝내겠다. 몸을 넘겨라.   [Y] 맡긴다   [N] 거절";
        root.SetActive(true);
    }

    void Hide() => root.SetActive(false);

    void Build()
    {
        var canvasGO = new GameObject("TakeoverRequestCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 260;   // 내면 오버레이(300)보다 아래
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        root = new GameObject("Request");
        root.transform.SetParent(canvasGO.transform, false);
        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.02f, 0.12f, 0.85f);
        bg.raycastTarget = false;
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.78f);
        rect.sizeDelta = new Vector2(1100f, 70f);

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(root.transform, false);
        label = textGO.AddComponent<TextMeshProUGUI>();
        var font = Resources.Load<TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF");
        if (font != null) label.font = font;
        label.fontSize = 30f;
        label.color = new Color(0.85f, 0.65f, 1f);
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        var textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;

        root.SetActive(false);
    }
}

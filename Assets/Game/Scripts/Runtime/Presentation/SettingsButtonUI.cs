using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 오른쪽 위의 톱니바퀴 버튼. 누르면 ESC 메뉴(<see cref="PauseMenuUI"/>)를 연다.
/// 씬에 미리 배치해 두고 쓴다. 런타임에 만들지 않는다.
/// </summary>
public class SettingsButtonUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private CanvasGroup group;

    void Start()
    {
        if (button == null || group == null) { enabled = false; return; }
        button.onClick.AddListener(Open);
    }

    void Update()
    {
        // 메뉴나 성장 창이 이미 떠 있으면 톱니바퀴는 비켜 준다
        bool hidden = PauseMenuUI.IsOpen || RpgUI.IsOpen || DialogueManager.IsDialogueOpen
                      || GameFlow.Instance.IsLoading;
        group.alpha = hidden ? 0f : 1f;
        group.blocksRaycasts = !hidden;
    }

    private void Open()
    {
        FindFirstObjectByType<PauseMenuUI>(FindObjectsInactive.Include)?.Open();
    }

#if UNITY_EDITOR
    /// <summary>씬에 편집 가능한 오브젝트로 버튼을 만든다.</summary>
    public void BakeSceneUI()
    {
        if (group != null) return;

        var canvasGo = new GameObject("SettingsButton_Canvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;   // 메뉴창(300) 아래, HUD 위

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();
        group = canvasGo.AddComponent<CanvasGroup>();

        var go = new GameObject("Settings");
        go.transform.SetParent(canvasGo.transform, false);

        var image = go.AddComponent<Image>();
        image.sprite = Load("Assets/Sprites/UI/SettingsButton.png");
        image.preserveAspect = true;
        if (image.sprite == null) image.color = new Color(0.59f, 0.38f, 0.19f);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-40f, -40f);
        rect.sizeDelta = new Vector2(84f, 84f);

        button = go.AddComponent<Button>();
        button.targetGraphic = image;

        Sprite pressed = Load("Assets/Sprites/UI/SettingsButton_Pressed.png");
        if (pressed != null)
        {
            button.transition = Selectable.Transition.SpriteSwap;
            var state = button.spriteState;
            state.pressedSprite = pressed;
            state.selectedSprite = image.sprite;
            button.spriteState = state;
        }

        var colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.96f, 0.82f);
        button.colors = colors;

        UiBootstrap.EnsureEventSystem();
    }

    static Sprite Load(string path)
        => UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
#endif
}

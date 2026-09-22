using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// ESC 로 여는 메뉴창. 포켓몬처럼 세이브 포인트 없이 여기서 바로 저장한다.
/// 항목은 계속하기 / 인벤토리 / 설정 / 저장 / 타이틀로.
///
/// 씬의 UIRoot 아래에 배치하고 저장된 패널을 전환한다.
/// </summary>
public class PauseMenuUI : MonoBehaviour
{
    /// <summary>메뉴가 열려 있는가. 플레이어 조작을 막는 데 쓴다.</summary>
    public static bool IsOpen { get; private set; }

    private enum Tab { None, Inventory, Settings }

    private TMP_FontAsset font;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform contentArea;
    [SerializeField] private TMP_Text toast;
    [SerializeField] private GameObject homePage, inventoryPage, settingsPage;
    [SerializeField] private TMP_Text inventoryText;
    private float toastTimer;
    private Tab openTab = Tab.None;

    [SerializeField] private List<Button> menuButtons = new List<Button>();

    void Start()
    {
        if (group == null) { enabled = false; return; }
        menuButtons[0].onClick.AddListener(Close);
        menuButtons[1].onClick.AddListener(() => ShowTab(Tab.Inventory));
        menuButtons[2].onClick.AddListener(() => ShowTab(Tab.Settings));
        menuButtons[3].onClick.AddListener(SaveGame);
        menuButtons[4].onClick.AddListener(GoToTitle);
        group.alpha = 0f; group.blocksRaycasts = group.interactable = false;
    }

#if UNITY_EDITOR
    public void BakeSceneUI()
    {
        if (group != null) return;
        Build();
        homePage = new GameObject("Home", typeof(RectTransform));
        homePage.transform.SetParent(contentArea, false);
        Stretch((RectTransform)homePage.transform);
        MakeText((RectTransform)homePage.transform, "왼쪽에서 항목을 고르세요", 22f, Vector2.zero, Color.white, TextAlignmentOptions.Center);
        inventoryPage = new GameObject("Inventory", typeof(RectTransform));
        inventoryPage.transform.SetParent(contentArea, false);
        Stretch((RectTransform)inventoryPage.transform);
        inventoryText = MakeText((RectTransform)inventoryPage.transform, "소지품이 없습니다.", 22f, Vector2.zero, Color.white, TextAlignmentOptions.Center);
        settingsPage = new GameObject("Settings", typeof(RectTransform));
        settingsPage.transform.SetParent(contentArea, false);
        Stretch((RectTransform)settingsPage.transform);
        SettingsPanel.Build((RectTransform)settingsPage.transform, font);
        ShowTab(Tab.None);
    }
#endif

    void Update()
    {
        HandleToggleKey();
        UpdateToast();
    }

    private void HandleToggleKey()
    {
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
            return;

        // 대화창도 ESC 로 닫히므로 겹치지 않게 비켜 준다
        if (DialogueManager.IsDialogueOpen || RpgUI.IsOpen || RpgUI.LastClosedFrame == Time.frameCount)
            return;

        if (IsOpen)
            Close();
        else
            Open();
    }

    private void UpdateToast()
    {
        if (toast == null || toastTimer <= 0f)
            return;

        toastTimer -= Time.unscaledDeltaTime;
        if (toastTimer <= 0f)
            toast.text = string.Empty;
    }

    // ── 열고 닫기 ───────────────────────────────────────────────

    public void Open()
    {
        // 타이틀 화면이나 죽어 있을 때는 열지 않는다
        if (GameFlow.Instance.IsLoading)
            return;

        var stats = FindFirstObjectByType<PlayerStats>();
        if (stats == null || !stats.IsAlive)
            return;

        if (group == null) return;

        IsOpen = true;
        openTab = Tab.None;
        ShowTab(Tab.None);

        group.alpha = 1f;
        group.blocksRaycasts = true;
        group.interactable = true;

        Time.timeScale = 0f;   // 메뉴를 보는 동안 게임은 멈춘다
    }

    public void Close()
    {
        IsOpen = false;

        if (group != null)
        {
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
        }

        Time.timeScale = 1f;
    }

    // ── 항목 동작 ───────────────────────────────────────────────

    private void SaveGame()
    {
        GameFlow.Instance.SaveNow();
        ShowToast("저장했습니다");
    }

    private void GoToTitle()
    {
        Close();
        GameFlow.Instance.ReturnToTitle();
    }

    private void ShowToast(string message)
    {
        if (toast == null)
            return;

        toast.text = message;
        toastTimer = 2f;
    }

    private void ShowTab(Tab tab)
    {
        openTab = tab;

        homePage.SetActive(tab == Tab.None);
        inventoryPage.SetActive(tab == Tab.Inventory);
        settingsPage.SetActive(tab == Tab.Settings);
        if (tab == Tab.Inventory)
        {
            var inventory = FindFirstObjectByType<PlayerInventory>();
            inventoryText.text = "소지품이 없습니다.";
            if (inventory != null && inventory.Items.Count > 0)
            {
                inventoryText.text = "";
                foreach (var item in inventory.Items) inventoryText.text += $"{item.displayName} × {item.quantity}\n";
            }
        }
    }

    private void BuildInventoryPlaceholder()
    {
        MakeText(contentArea, "인벤토리", 34f, new Vector2(0f, 150f), Color.white, TextAlignmentOptions.Center);
        MakeText(contentArea, "아직 아이템 시스템이 없습니다.\n포션·장비가 들어오면 여기에 표시됩니다.",
            20f, new Vector2(0f, 60f), new Color(0.6f, 0.6f, 0.66f), TextAlignmentOptions.Center);
    }

    // ── 조립 ────────────────────────────────────────────────────

    private void Build()
    {
        font = UiBootstrap.FindSceneFont();

        var canvasGo = new GameObject("PauseMenu_Canvas");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;   // 사망 화면(200) 위, 씬 전환 암전(500) 아래

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();
        group = canvasGo.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        // 화면을 덮는 반투명 막
        var dim = new GameObject("Dim").AddComponent<Image>();
        dim.transform.SetParent(canvasGo.transform, false);
        dim.color = new Color(0f, 0f, 0f, 0.6f);
        Stretch(dim.GetComponent<RectTransform>());

        // 가운데 창
        var window = new GameObject("Window").AddComponent<Image>();
        window.transform.SetParent(canvasGo.transform, false);
        window.color = new Color(0.1f, 0.11f, 0.15f, 0.97f);

        var windowRect = window.GetComponent<RectTransform>();
        windowRect.anchorMin = new Vector2(0.5f, 0.5f);
        windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.pivot = new Vector2(0.5f, 0.5f);
        windowRect.anchoredPosition = Vector2.zero;
        windowRect.sizeDelta = new Vector2(1100f, 640f);

        MakeText(windowRect, "메뉴", 30f, new Vector2(-420f, 260f), Color.white, TextAlignmentOptions.Left);

        // 왼쪽 항목들
        float y = 180f;
        AddMenuButton(windowRect, "계속하기", ref y, Close);
        AddMenuButton(windowRect, "인벤토리", ref y, () => ShowTab(Tab.Inventory));
        AddMenuButton(windowRect, "설정", ref y, () => ShowTab(Tab.Settings));
        AddMenuButton(windowRect, "저장", ref y, SaveGame);
        AddMenuButton(windowRect, "타이틀로", ref y, GoToTitle);

        // 오른쪽 내용 자리
        var content = new GameObject("Content").AddComponent<Image>();
        content.transform.SetParent(windowRect, false);
        content.color = new Color(0.07f, 0.08f, 0.11f, 1f);

        contentArea = content.GetComponent<RectTransform>();
        contentArea.anchorMin = new Vector2(0.5f, 0.5f);
        contentArea.anchorMax = new Vector2(0.5f, 0.5f);
        contentArea.pivot = new Vector2(0.5f, 0.5f);
        contentArea.anchoredPosition = new Vector2(180f, -20f);
        contentArea.sizeDelta = new Vector2(700f, 500f);

        // 저장 알림
        toast = MakeText(windowRect, string.Empty, 22f, new Vector2(-420f, -270f),
            new Color(0.55f, 0.9f, 0.6f), TextAlignmentOptions.Left);

        UiBootstrap.EnsureEventSystem();
    }

    private void AddMenuButton(RectTransform parent, string label, ref float y, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Menu_" + label);
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.16f, 0.17f, 0.22f, 1f);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(-380f, y);
        rect.sizeDelta = new Vector2(280f, 66f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        // Start에서 저장된 버튼에 동작을 연결한다.

        var colors = button.colors;
        colors.highlightedColor = new Color(0.28f, 0.29f, 0.36f, 1f);
        colors.pressedColor = new Color(0.4f, 0.14f, 0.16f, 1f);
        button.colors = colors;

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);

        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        if (font != null) tmp.font = font;
        tmp.fontSize = 26f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        Stretch(textGo.GetComponent<RectTransform>());

        menuButtons.Add(button);
        y -= 80f;
    }

    private TMP_Text MakeText(RectTransform parent, string text, float size, Vector2 position,
        Color color, TextAlignmentOptions align)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        if (font != null) tmp.font = font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(620f, size * 3f);

        return tmp;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    void OnDisable()
    {
        // 씬을 옮기거나 꺼질 때 시간이 멈춘 채로 남지 않게
        if (IsOpen)
            Close();
    }
}

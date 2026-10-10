using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class StoryConversationUI : MonoBehaviour
{
    static StoryConversationUI opened;
    public static bool IsOpen => opened != null;
    [SerializeField] GameObject panel;
    [SerializeField] TMP_Text titleText, bodyText, actionText;
    [SerializeField] Button confirmButton, closeButton;
    Action confirmation;
    float previousTimeScale;
    int openingFrame;
    void Start()
    {
        if (panel == null) { enabled = false; return; }
        panel.SetActive(false); confirmButton.onClick.AddListener(Confirm); closeButton.onClick.AddListener(Close);
    }
    public bool Open(string title, string body, string label, Action confirm)
    {
        if (panel == null || IsOpen || DialogueManager.IsDialogueOpen || RpgUI.IsOpen || InventoryWindow.IsOpen || PauseMenuUI.IsOpen) return false;
        opened = this; confirmation = confirm; openingFrame = Time.frameCount;
        titleText.text = title; bodyText.text = body; actionText.text = label;
        previousTimeScale = Time.timeScale; Time.timeScale = 0; panel.SetActive(true); return true;
    }
    public void Confirm() { if (opened != this) return; var action = confirmation; Close(); action?.Invoke(); }
    public void Close()
    {
        if (opened == this) { opened = null; Time.timeScale = previousTimeScale; }
        confirmation = null; if (panel != null) panel.SetActive(false);
    }
    void Update()
    {
        if (opened != this || Keyboard.current == null || openingFrame == Time.frameCount) return;
        if (Keyboard.current.escapeKey.wasPressedThisFrame) Close();
        else if (Keyboard.current.eKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame) Confirm();
    }
    void OnDisable() => Close();
#if UNITY_EDITOR
    public void Bake(Transform owner)
    {
        if (panel != null) return;
        var canvasGo = new GameObject("Story dialogue", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(owner, false);
        var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 390;
        var scaler = canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1440, 900);
        panel = new GameObject("Conversation", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(canvasGo.transform, false);
        var rect = (RectTransform)panel.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0);
        rect.anchoredPosition = new Vector2(0, 32); rect.sizeDelta = new Vector2(1040, 310);
        panel.GetComponent<Image>().color = new Color(0.035f, 0.045f, 0.045f, 0.97f);
        titleText = Label(panel.transform, "Speaker", 30, 20, 980, 36, 25);
        bodyText = Label(panel.transform, "Story", 30, 72, 980, 142, 21);
        confirmButton = MakeButton(panel.transform, "확인 · E", 590, 238, 250); actionText = confirmButton.GetComponentInChildren<TMP_Text>();
        closeButton = MakeButton(panel.transform, "닫기 · ESC", 850, 238, 160);
        panel.SetActive(false); UnityEditor.EditorUtility.SetDirty(this);
    }
    static TMP_Text Label(Transform parent, string name, float x, float y, float width, float height, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        var text = go.GetComponent<TextMeshProUGUI>(); text.font = Resources.Load<TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF");
        text.fontSize = size; text.color = InkUiTheme.Ivory; text.raycastTarget = false; return text;
    }
    static Button MakeButton(Transform parent, string label, float x, float y, float width)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, 48); go.GetComponent<Image>().color = InkUiTheme.Card;
        var text = Label(go.transform, "Label", 6, 4, width - 12, 40, 18); text.text = label; text.alignment = TextAlignmentOptions.Center;
        return go.GetComponent<Button>();
    }
#endif
}

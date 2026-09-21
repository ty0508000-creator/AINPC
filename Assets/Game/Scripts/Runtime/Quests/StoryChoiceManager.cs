using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class StoryChoiceEntry
{
    public string choiceId;
    public float moodDelta;
    public string memoryLine;
}

[Serializable]
public class StoryChoiceSaveData
{
    public List<StoryChoiceEntry> entries = new();
}

[Serializable]
public class StoryChoiceOption
{
    public string choiceId;
    [TextArea(1, 2)] public string label;
    [TextArea(2, 4)] public string description;
    public float moodDelta;
    [TextArea(1, 3)] public string memoryLine;
}

/// <summary>
/// Stores explicit narrative choices independently from LLM/RAG availability.
/// The selected choice, its Mood consequence, and any matching Choice objective are committed together.
/// </summary>
public class StoryChoiceManager : MonoBehaviour
{
    public static StoryChoiceManager Instance { get; private set; }

    [SerializeField] private MoodSystem moodSystem;
    [SerializeField] private QuestManager questManager;
    [SerializeField] private TMP_FontAsset koreanFont;

    readonly Dictionary<string, StoryChoiceEntry> choices = new(StringComparer.Ordinal);
    GameObject panel;
    public bool IsChoosing => panel != null;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (moodSystem == null) moodSystem = FindFirstObjectByType<MoodSystem>();
        if (questManager == null) questManager = QuestManager.Instance;
        if (koreanFont == null) koreanFont = Resources.Load<TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF");
        Load();
    }

    public bool HasChoice(string choiceId) => !string.IsNullOrWhiteSpace(choiceId) && choices.ContainsKey(choiceId);
    public bool TryGetChoice(string choiceId, out StoryChoiceEntry entry) => choices.TryGetValue(choiceId ?? "", out entry);

    public bool Choose(StoryChoiceOption option)
    {
        if (option == null || string.IsNullOrWhiteSpace(option.choiceId) || HasChoice(option.choiceId)) return false;
        var entry = new StoryChoiceEntry { choiceId = option.choiceId, moodDelta = option.moodDelta, memoryLine = option.memoryLine ?? "" };
        choices.Add(entry.choiceId, entry);
        if (moodSystem != null && !Mathf.Approximately(entry.moodDelta, 0f))
        {
            moodSystem.ApplyQuestDelta(entry.moodDelta);
            moodSystem.NotifyQuestDelta();
        }
        // A Choice objective decides which active quest uses this id; unknown IDs are still valid story memories.
        questManager?.ReportChoice(entry.choiceId);
        SaveSystem.SaveGame(questManager != null ? questManager.Player : FindFirstObjectByType<PlayerStats>(), questManager);
        return true;
    }

    public void Open(string title, string body, StoryChoiceOption[] options)
    {
        if (options == null || options.Length == 0 || IsChoosing) return;
        var available = new List<StoryChoiceOption>();
        foreach (var option in options)
            if (option != null && !HasChoice(option.choiceId)) available.Add(option);
        if (available.Count == 0) return;

        panel = new GameObject("Story choice", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = panel.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 400;
        var scaler = panel.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1440, 900);
        var shade = ImageBox(panel.transform, "Shade", new Color(0.04f, 0.05f, 0.06f, 0.78f)); Stretch(shade.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var box = ImageBox(panel.transform, "Parchment", new Color(0.91f, 0.80f, 0.57f));
        box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(.5f, .5f); box.rectTransform.sizeDelta = new Vector2(830, 500);
        Text(box.transform, title, 36, new Vector2(0, 185), new Vector2(760, 60), TextAlignmentOptions.Center);
        Text(box.transform, body, 22, new Vector2(0, 105), new Vector2(720, 115), TextAlignmentOptions.Center);
        for (int i = 0; i < available.Count; i++)
        {
            var option = available[i];
            var button = new GameObject("Choice · " + option.choiceId, typeof(Image), typeof(Button));
            button.transform.SetParent(box.transform, false);
            var image = button.GetComponent<Image>(); image.color = i == 0 ? new Color(.19f, .34f, .24f) : new Color(.32f, .22f, .13f);
            var rect = button.GetComponent<RectTransform>(); rect.anchoredPosition = new Vector2(0, -35 - i * 120); rect.sizeDelta = new Vector2(680, 94);
            Text(button.transform, option.label + "\n<size=58%>" + option.description + "</size>", 25, Vector2.zero, new Vector2(640, 82), TextAlignmentOptions.Center, Color.white);
            button.GetComponent<Button>().onClick.AddListener(() => { if (Choose(option)) Close(); });
        }
    }

    public void Close()
    {
        if (panel != null) Destroy(panel);
        panel = null;
    }

    public StoryChoiceSaveData Capture()
    {
        var data = new StoryChoiceSaveData();
        foreach (var entry in choices.Values)
            data.entries.Add(new StoryChoiceEntry { choiceId = entry.choiceId, moodDelta = entry.moodDelta, memoryLine = entry.memoryLine });
        return data;
    }

    void Load()
    {
        if (!SaveSystem.TryLoadPlayer(out var saved) || saved?.storyChoices == null) return;
        Validate(saved.storyChoices);
        choices.Clear();
        foreach (var entry in saved.storyChoices.entries)
            choices.Add(entry.choiceId, new StoryChoiceEntry { choiceId = entry.choiceId, moodDelta = entry.moodDelta, memoryLine = entry.memoryLine ?? "" });
    }

    public static void Validate(StoryChoiceSaveData data)
    {
        if (data == null) return;
        if (data.entries == null) throw new InvalidOperationException("선택 저장 목록이 없습니다.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in data.entries)
            if (entry == null || string.IsNullOrWhiteSpace(entry.choiceId) || !ids.Add(entry.choiceId) ||
                float.IsNaN(entry.moodDelta) || float.IsInfinity(entry.moodDelta))
                throw new InvalidOperationException("잘못된 선택 저장 항목입니다.");
    }

    Image ImageBox(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(Image)); go.transform.SetParent(parent, false); var image = go.GetComponent<Image>(); image.color = color; return image;
    }
    void Text(Transform parent, string value, float size, Vector2 position, Vector2 dimensions, TextAlignmentOptions alignment, Color? color = null)
    {
        var go = new GameObject("Text", typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>(); text.font = koreanFont; text.text = value; text.fontSize = size; text.alignment = alignment; text.color = color ?? new Color(.18f, .12f, .07f); text.textWrappingMode = TextWrappingModes.Normal;
        var rect = text.rectTransform; rect.anchoredPosition = position; rect.sizeDelta = dimensions;
    }
    static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
    void OnDestroy() { if (Instance == this) Instance = null; }
}

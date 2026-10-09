using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class RpgUI
{
    [SerializeField] Button ultimateButton, ultimateSelectButton;
    [SerializeField] TMP_Text ultimateLabel, ultimateState;
    [SerializeField] Image ultimateCooldown;
    [SerializeField] Sprite ultimateIcon;

    void BindUltimateButtons()
    {
        ConfigureKeyboardSlot(ultimateButton, RpgSkillCatalog.Ultimate);
        if (ultimateSelectButton != null) ultimateSelectButton.onClick.AddListener(() =>
        { selected = RpgSkillCatalog.Ultimate; Open(true); });
    }

    static void ConfigureKeyboardSlot(Button button, int id)
    {
        if (button == null) return;
        button.onClick.RemoveAllListeners();
        var colors = button.colors; colors.disabledColor = Color.white; button.colors = colors;
        button.interactable = false;
        var badge = button.transform.Find("Hotkey")?.GetComponent<TMP_Text>();
        if (badge == null)
            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
                if (text.text == "1" || text.text == "2" || text.text == "3" || text.text == "4" ||
                    text.text == "Q" || text.text == "R" || text.text == "F" || text.text == "Z") { badge = text; break; }
        if (badge != null) badge.text = RpgSkillController.HotkeyLabel(id);
    }

    void RefreshUltimateUI()
    {
        if (ultimateButton == null || stats == null) return;
        int id = RpgSkillCatalog.Ultimate;
        bool learned = stats.SkillRanks[id] > 0;
        float cooldown = skills != null ? skills.Remaining(id) : 0;
        float active = skills != null ? skills.UltimateRemaining : 0;
        ultimateState.text = !learned ? "미습득" : active > 0 ? $"연격 {active:0.0}초" : cooldown > 0 ? $"{cooldown:0.0}초" : "내력 40";
        ultimateLabel.text = $"이기어검\n<size=13>{(learned ? "습득 완료 · Z" : stats.SkillLockReason(id))}</size>";
        ultimateButton.image.color = active > 0 ? new Color(0.32f, 0.42f, 0.5f) : InkUiTheme.Card;
        SetBar(ultimateCooldown, cooldown / RpgSkillCatalog.All[id].Cooldown);
    }

#if UNITY_EDITOR
    public void BakeKeyboardShortcuts()
    {
        for (int i = 0; i < hotButtons.Length; i++) ConfigureKeyboardSlot(hotButtons[i], RpgSkillCatalog.Hotbar[i]);
        ConfigureKeyboardSlot(ultimateButton, RpgSkillCatalog.Ultimate);
        if (ultimateLabel != null) ultimateLabel.text = ultimateLabel.text.Replace("4번", "Z");
    }

    public void BakeUltimateWidgets()
    {
        if (canvasRoot == null || ultimateButton != null) return;
        ultimateIcon = Resources.Load<Sprite>("UiArt/Skill2");
        var font = Resources.Load<TMP_FontAsset>("Fonts/UiBody SDF");
        var bar = (RectTransform)canvasRoot.transform.Find("Skill bar");
        bar.sizeDelta = new Vector2(320, 100);
        ultimateButton = MakeButton(bar, "", 244, 4, 64, 64, null);
        ultimateButton.name = "Ultimate · 이기어검";
        ultimateButton.image.sprite = null; ultimateButton.image.color = InkUiTheme.Card;
        Artwork(ultimateButton.transform, "Skill icon", ultimateIcon, 7, 7, 50, 50);
        var badge = Text(ultimateButton.transform, RpgSkillController.HotkeyLabel(RpgSkillCatalog.Ultimate), 0, 47, 18, 18, 13, InkUiTheme.Ivory);
        badge.name = "Hotkey";
        badge.font = font; badge.alignment = TextAlignmentOptions.Center;
        ultimateState = Text(bar, "미습득", 244, 74, 64, 26, 11, InkUiTheme.Ivory);
        ultimateState.font = font; ultimateState.alignment = TextAlignmentOptions.Center;
        ultimateCooldown = Bar(bar, 244, 69, 64, 3, InkUiTheme.Jade);
        var window = overlay.transform.Find("Cultivation");
        ultimateSelectButton = MakeButton(window, "", 42, 515, 204, 78, null);
        ultimateSelectButton.name = "Ultimate learning";
        ultimateSelectButton.image.sprite = null; ultimateSelectButton.image.color = new Color(0.16f, 0.13f, 0.08f, 0.8f);
        ultimateLabel = ultimateSelectButton.GetComponentInChildren<TMP_Text>();
        ultimateLabel.font = font; ultimateLabel.fontSize = 22; ultimateLabel.text = "이기어검\n<size=13>궁극기 · Z</size>";
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}

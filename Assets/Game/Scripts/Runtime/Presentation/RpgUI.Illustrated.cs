using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class RpgUI
{
    [SerializeField] bool illustratedLayout;
    [SerializeField] Image[] skillGlyphs = new Image[9];
    [SerializeField] TMP_Text[] skillLocks = new TMP_Text[9];
    [SerializeField] Image detailIllustration;
    [SerializeField] Sprite attackPreview;

#if UNITY_EDITOR
    public void RebuildIllustratedUI()
    {
        var parent = canvasRoot != null ? canvasRoot.transform.parent : null;
        if (canvasRoot != null) DestroyImmediate(canvasRoot);
        canvasRoot = null;
        BakeSceneUI();
        if (parent != null) canvasRoot.transform.SetParent(parent, false);
    }
#endif

    static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    Image Artwork(Transform parent, string name, Sprite sprite, float x, float y, float w, float h)
    {
        var rect = Box(parent, name, x, y, w, h, Color.white);
        var image = rect.GetComponent<Image>();
        image.sprite = sprite; image.color = sprite != null ? Color.white : Color.clear;
        image.preserveAspect = true; image.raycastTarget = false;
        return image;
    }

    void BuildIllustratedLayout()
    {
        illustratedLayout = true;
        var bodyFont = Resources.Load<TMP_FontAsset>("Fonts/UiBody SDF");
        var titleFont = Resources.Load<TMP_FontAsset>("Fonts/UiTitle SDF");
        if (bodyFont != null)
            foreach (var text in canvasRoot.GetComponentsInChildren<TMP_Text>(true))
            { text.font = bodyFont; text.characterSpacing = 0; text.lineSpacing = 6; }

        var hud = (RectTransform)canvasRoot.transform.Find("Vitals");
        hud.GetComponent<Image>().color = Color.clear;
        Place(hud, 28, 26, 420, 112);
        foreach (var text in hud.GetComponentsInChildren<TMP_Text>())
            if (text != identity && text != vitals && text != moodLabel) text.gameObject.SetActive(false);
        Artwork(hud, "Elder portrait", Resources.Load<Sprite>("UiArt/ElderPortrait"), 0, 0, 74, 74);
        Place((RectTransform)hpFill.transform.parent, 88, 10, 244, 9);
        Place((RectTransform)manaFill.transform.parent, 88, 30, 244, 7);
        manaFill.color = InkUiTheme.Jade;
        Place(vitals.rectTransform, 88, 45, 315, 22); vitals.fontSize = 13;
        Place(identity.rectTransform, 342, 5, 72, 24); identity.fontSize = 14;
        Place((RectTransform)expFill.transform.parent, 88, 71, 244, 2);
        Place((RectTransform)moodFill.transform.parent, 88, 81, 150, 3);
        Place(moodLabel.rectTransform, 88, 88, 300, 22); moodLabel.fontSize = 12;
        Place(shortcutButton.GetComponent<RectTransform>(), 30, 145, 130, 28);
        shortcutButton.image.color = Color.clear;
        shortcutButton.GetComponent<Outline>().enabled = false;

        var hotbar = (RectTransform)canvasRoot.transform.Find("Skill bar");
        hotbar.GetComponent<Image>().color = Color.clear;
        hotbar.sizeDelta = new Vector2(242, 100);
        for (int i = 0; i < 3; i++)
        {
            int id = RpgSkillCatalog.Hotbar[i];
            Place(hotButtons[i].GetComponent<RectTransform>(), 10 + i * 78, 4, 64, 64);
            Artwork(hotButtons[i].transform, "Skill icon", Resources.Load<Sprite>("UiArt/Skill" + id), 7, 7, 50, 50);
            Place(hotLabels[i].rectTransform, 0, 70, 64, 26); hotLabels[i].fontSize = 11;
            var badge = Text(hotButtons[i].transform, RpgSkillController.HotkeyLabel(id), 0, 47, 18, 18, 13, InkUiTheme.Ivory);
            badge.name = "Hotkey";
            badge.font = bodyFont != null ? bodyFont : koreanFont;
            badge.alignment = TextAlignmentOptions.Center;
            Place((RectTransform)cooldownFills[i].transform.parent, 10 + i * 78, 65, 64, 3);
        }
        Place(guardLabel.rectTransform, -60, -25, 360, 22);

        var window = (RectTransform)overlay.transform.Find("Cultivation");
        window.sizeDelta = new Vector2(1300, 740);
        window.GetComponent<Image>().color = new Color(0.035f, 0.043f, 0.045f, 0.94f);
        overlay.GetComponent<Image>().color = new Color(0, 0, 0, 0.32f);
        var title = (RectTransform)window.Find("Wooden title");
        Place(title, 38, 75, 230, 80); title.GetComponent<Image>().color = Color.clear;
        var titleText = title.GetComponentInChildren<TMP_Text>();
        titleText.text = "수련록"; titleText.fontSize = 52; titleText.alignment = TextAlignmentOptions.Left;
        Place(titleText.rectTransform, 0, 0, 230, 78);
        if (titleFont != null) titleText.font = titleFont;
        foreach (Transform child in window)
        {
            var text = child.GetComponent<TMP_Text>();
            if (text == null || text == points) continue;
            if (text.text.StartsWith("한 걸음"))
            { text.text = "귀환자 · 강호의 기록"; Place(text.rectTransform, 42, 168, 220, 26); text.fontSize = 16; }
            else if (text.text.StartsWith("레벨업"))
            { text.text = "투자는 즉시 저장됩니다.  수련 중에도 전투는 계속됩니다."; Place(text.rectTransform, 310, 689, 780, 25); text.fontSize = 13; }
        }
        Button[] tabs = { statsTab, skillsTab, inventoryTab };
        for (int i = 0; i < tabs.Length; i++)
        {
            Place(tabs[i].GetComponent<RectTransform>(), 42, 270 + i * 62, 204, 44);
            tabs[i].GetComponent<Outline>().enabled = false;
            var label = tabs[i].GetComponentInChildren<TMP_Text>();
            label.fontSize = 23; label.alignment = TextAlignmentOptions.Left;
            Place(label.rectTransform, 16, 5, 175, 34);
        }
        Place(points.rectTransform, 710, 30, 540, 27); points.fontSize = 15;
        Place(closeButton.GetComponent<RectTransform>(), 1145, 684, 116, 35);
        closeButton.image.color = Color.clear;
        Place((RectTransform)window.Find("Divider"), 280, 70, 1, 590);
        Place(statsPage.GetComponent<RectTransform>(), 310, 105, 1092, 450);
        statsPage.transform.localScale = Vector3.one * 0.84f;
        Place(inventoryPage.GetComponent<RectTransform>(), 310, 105, 1092, 450);
        inventoryPage.transform.localScale = Vector3.one * 0.84f;
        Place(skillsPage.GetComponent<RectTransform>(), 310, 90, 940, 565);
        foreach (Transform child in skillsPage.transform)
        {
            var label = child.GetComponent<TMP_Text>();
            if (label != null)
            {
                int column = label.text.StartsWith("검술") ? 0 : label.text.StartsWith("호신") ? 1 : 2;
                label.text = new[] { "검술", "호신", "내공" }[column];
                Place(label.rectTransform, column * 174, 0, 144, 30);
                label.alignment = TextAlignmentOptions.Center; label.fontSize = 21;
            }
            if (child.name == "Prerequisite") child.GetComponent<Image>().enabled = false;
        }
        for (int id = 0; id < 9; id++)
        {
            int column = id / 3, tier = id % 3;
            var button = nodeButtons[id];
            Place(button.GetComponent<RectTransform>(), column * 174 + 36, 54 + tier * 163, 72, 72);
            var emblem = button.transform.Find("Emblem");
            if (emblem != null) emblem.gameObject.SetActive(false);
            skillGlyphs[id] = Artwork(button.transform, "Skill icon", Resources.Load<Sprite>("UiArt/Skill" + id), 8, 8, 56, 56);
            Place(nodeLabels[id].rectTransform, -40, 80, 152, 52);
            nodeLabels[id].fontSize = 16; nodeLabels[id].alignment = TextAlignmentOptions.Center;
            skillLocks[id] = Text(button.transform, "미개방", 20, 51, 52, 18, 10, InkUiTheme.Ivory);
            if (bodyFont != null) skillLocks[id].font = bodyFont;
            skillLocks[id].alignment = TextAlignmentOptions.Right;
            if (tier < 2) Box(skillsPage.transform, "Skill connection", column * 174 + 71, 188 + tier * 163, 1, 24, new Color(0.38f, 0.42f, 0.40f, 0.7f));
        }
        var detail = (RectTransform)skillsPage.transform.Find("Skill details");
        Place(detail, 555, 0, 360, 565); detail.GetComponent<Image>().color = Color.clear;
        foreach (var label in detail.GetComponentsInChildren<TMP_Text>())
            if (label != detailTitle && label != detailBody && label != learnLabel) label.gameObject.SetActive(false);
        Place(detailTitle.rectTransform, 18, 0, 330, 55); detailTitle.fontSize = 36;
        if (titleFont != null) detailTitle.font = titleFont;
        Place((RectTransform)detail.Find("Rule"), 18, 220, 328, 1);
        attackPreview = Resources.Load<Sprite>("UiArt/ElderAttack");
        detailIllustration = Artwork(detail, "Skill demonstration", attackPreview, 18, 65, 320, 145);
        Place(detailBody.rectTransform, 18, 244, 328, 222); detailBody.fontSize = 17;
        Place(learnButton.GetComponent<RectTransform>(), 18, 488, 328, 48);
        learnButton.image.color = new Color(0.87f, 0.82f, 0.69f);
        learnLabel.color = new Color(0.09f, 0.10f, 0.10f); learnLabel.fontSize = 17;
        var preview = Text(detail, "", 18, 550, 328, 18, 11, InkUiTheme.Muted);
        preview.text = "";
#if UNITY_EDITOR
        BakeUltimateWidgets();
#endif
    }

    void RefreshIllustratedWidgets()
    {
        if (!illustratedLayout) return;
        for (int id = 0; id < 9; id++)
        {
            var skill = RpgSkillCatalog.All[id];
            bool locked = stats.SkillLockReason(id).Length > 0 && stats.SkillRanks[id] == 0;
            nodeLabels[id].text = $"{skill.Name}\n<size=14>{stats.SkillRanks[id]} / {skill.MaxRank}</size>";
            skillGlyphs[id].color = locked ? new Color(1, 1, 1, 0.3f) : Color.white;
            skillLocks[id].gameObject.SetActive(locked);
        }
        detailIllustration.sprite = selected == 0 ? attackPreview : selected == RpgSkillCatalog.Ultimate ? ultimateIcon : skillGlyphs[selected].sprite;
        detailIllustration.color = Color.white;
        for (int i = 0; i < 3; i++)
        {
            int id = RpgSkillCatalog.Hotbar[i];
            float remaining = skills != null ? skills.Remaining(id) : 0;
            hotLabels[i].text = stats.SkillRanks[id] == 0 ? "미습득" : remaining > 0 ? $"{remaining:0.0}초" : $"내력 {RpgSkillCatalog.All[id].ManaCost:0}";
        }
    }
}

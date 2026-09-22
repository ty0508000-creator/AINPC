using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>씬에 저장된 HUD·수련·부활 UI의 표시와 입력을 관리한다.</summary>
[DisallowMultipleComponent]
public class RpgUI : MonoBehaviour
{
    static RpgUI opened;
    public static bool IsOpen => opened != null;
    public static int LastClosedFrame { get; private set; } = -1;
    [SerializeField] TMP_FontAsset koreanFont;
    PlayerStats stats;
    Player_Attack attack;
    RpgSkillController skills;
    PlayerInventory inventory;
    MoodSystem mood;
    ControlManager control;
    [SerializeField] GameObject canvasRoot, overlay, statsPage, skillsPage, inventoryPage;
    [SerializeField] GameObject deathOverlay;
    [SerializeField] TMP_Text identity, vitals, points, attributeSummary, detailTitle, detailBody, learnLabel, toast, guardLabel, moodLabel;
    [SerializeField] TMPro.TMP_Text inventoryList;
    [SerializeField] Image hpFill, manaFill, expFill, moodFill;
    [SerializeField] Button learnButton;
    [SerializeField] Button statsTab, skillsTab;
    [SerializeField] UnityEngine.UI.Button inventoryTab;
    [SerializeField] Button shortcutButton, closeButton, respawnButton;
    [SerializeField] Button[] hotButtons = new Button[3], nodeButtons = new Button[9];
    Sprite woodenButton, pressedButton, parchment, board, titleBoard, divider;
    [SerializeField] TMP_Text[] attributeValues = new TMP_Text[4];
    [SerializeField] Button[] attributeButtons = new Button[4];
    [SerializeField] TMP_Text[] nodeLabels = new TMP_Text[9];
    [SerializeField] Image[] nodeImages = new Image[9];
    [SerializeField] TMP_Text[] hotLabels = new TMP_Text[3];
    [SerializeField] Image[] cooldownFills = new Image[3];
    int selected;
    float toastUntil, nextRefresh;
    Color ink = new Color(0.94f, 0.87f, 0.71f, 1f);
    Color panel = new Color(0.92f, 0.82f, 0.63f, 1f);
    Color gold = new Color(0.43f, 0.23f, 0.10f);
    Color jade = new Color(0.13f, 0.36f, 0.27f);
    Color muted = new Color(0.37f, 0.30f, 0.22f);
    readonly Color textInk = new Color(0.22f, 0.16f, 0.10f);
    readonly Color cream = new Color(1f, 0.94f, 0.78f);

    void Start()
    {
        stats = GetComponent<PlayerStats>(); attack = GetComponent<Player_Attack>();
        skills = GetComponent<RpgSkillController>(); inventory = GetComponent<PlayerInventory>();
        mood = GetComponent<MoodSystem>(); control = GetComponent<ControlManager>();
        if (stats == null) { enabled = false; return; }
        if (canvasRoot == null)
        {
            Debug.LogError("씬 UI가 없습니다. Tools > AINPC > Bake Scene UI를 실행하세요.", this);
            enabled = false;
            return;
        }
        BindButtons();
        Close();
        stats.OnLevelUp += LevelUp;
        SaveSystem.OnSaveFailed += SaveFailed;
        if (skills != null) skills.OnFeedback += Notify;
        Refresh();
    }

    // 배치와 외형은 편집기에서 한 번 생성하여 씬에 저장한다.
#if UNITY_EDITOR
    public void BakeSceneUI()
    {
        if (canvasRoot != null) return;
        ResolveFont(); LoadSkin(); Build();
        identity.text = "Lv. 01";
        vitals.text = "HP 100/100     MP 50/50";
        moodLabel.text = "정신의 균형  100";
        for (int i = 0; i < 3; i++) hotLabels[i].text = RpgSkillCatalog.All[RpgSkillCatalog.Hotbar[i]].Name;
        for (int i = 0; i < 9; i++) nodeLabels[i].text = RpgSkillCatalog.All[i].Name;
    }
#endif

    void BindButtons()
    {
        shortcutButton.onClick.AddListener(() => Open(false));
        closeButton.onClick.AddListener(Close);
        statsTab.onClick.AddListener(() => ShowPage(false));
        skillsTab.onClick.AddListener(() => ShowPage(true));
        inventoryTab.onClick.AddListener(() => ShowPage(2));
        respawnButton.onClick.AddListener(() => { stats.Respawn(); Refresh(); });
        learnButton.onClick.AddListener(() =>
        {
            if (stats.LearnSkill(selected)) Notify(RpgSkillCatalog.All[selected].Name + " / 수련 완료");
            Refresh();
        });
        for (int i = 0; i < 4; i++)
        {
            int id = i;
            attributeButtons[i].onClick.AddListener(() => { stats.UpgradeAttribute((RpgAttribute)id); Refresh(); });
        }
        for (int i = 0; i < 9; i++)
        {
            int id = i;
            nodeButtons[i].onClick.AddListener(() => { selected = id; Refresh(); });
        }
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            hotButtons[i].onClick.AddListener(() => { if (skills != null) skills.TryCast(RpgSkillCatalog.Hotbar[slot]); });
        }
    }

    void ResolveFont()
    {
        if (koreanFont != null) return;
        koreanFont = Resources.Load<TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF");
        if (koreanFont != null) return;
        // Existing scene HUDs already reference the project's Korean font asset.
        foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            if (font.name.IndexOf("Paperlogy", StringComparison.OrdinalIgnoreCase) >= 0) { koreanFont = font; return; }
        koreanFont = TMP_Settings.defaultFontAsset;
    }

    void Build()
    {
        canvasRoot = new GameObject("RPG · 귀환자의 기록", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 200;
        var scaler = canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1440, 900); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        if (EventSystem.current == null)
        {
            var events = new GameObject("RPG EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(canvasRoot.transform, false);
        }
        var hud = Box(canvasRoot.transform, "Vitals", 26, 26, 310, 164, ink);
        hud.GetComponent<RectTransform>().localScale = new Vector3(1.2f, 1.2f, 1f);
        Text(hud, "귀 환 자", 18, 10, 140, 25, 25, gold);
        identity = Text(hud, "", 180, 12, 110, 22, 18, muted);
        hpFill = Bar(hud, 18, 47, 272, 8, new Color(0.77f, 0.26f, 0.26f));
        manaFill = Bar(hud, 18, 67, 272, 5, new Color(0.27f, 0.57f, 0.77f));
        vitals = Text(hud, "", 18, 83, 278, 25, 17, textInk);
        expFill = Bar(hud, 18, 117, 272, 3, gold);
        moodLabel = Text(hud, "", 18, 128, 278, 18, 14, muted);
        moodFill = Bar(hud, 18, 147, 272, 2, jade);
        var shortcut = MakeButton(canvasRoot.transform, "기록  C / K / I", 26, 200, 180, 40, () => Open(false));
        shortcutButton = shortcut;
        shortcut.GetComponentInChildren<TMP_Text>().fontSize = 13;
        var hotbar = Box(canvasRoot.transform, "Skill bar", 0, 0, 416, 88, ink);
        var hotRect = hotbar.GetComponent<RectTransform>(); hotRect.anchorMin = hotRect.anchorMax = new Vector2(0.5f, 0f);
        hotRect.pivot = new Vector2(0.5f, 0f); hotRect.anchoredPosition = new Vector2(0f, 22f);
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            var button = MakeButton(hotbar, "", 12 + i * 132, 10, 126, 60, () => { if (skills != null) skills.TryCast(RpgSkillCatalog.Hotbar[slot]); });
            hotButtons[i] = button;
            hotLabels[i] = button.GetComponentInChildren<TMP_Text>(); hotLabels[i].fontSize = 14;
            cooldownFills[i] = Bar(hotbar, 12 + i * 132, 72, 126, 3, gold);
        }
        guardLabel = Text(hotbar, "", 0, -24, 416, 22, 13, jade); guardLabel.alignment = TextAlignmentOptions.Center;
        var toastRect = Box(canvasRoot.transform, "Notice", 0, 0, 650, 34, Color.clear);
        var tr = toastRect.GetComponent<RectTransform>(); tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 1f);
        tr.pivot = new Vector2(0.5f, 1f); tr.anchoredPosition = new Vector2(0, -32);
        toast = Text(toastRect, "", 0, 0, 650, 34, 19, cream); toast.alignment = TextAlignmentOptions.Center;
        var toastShadow = toast.gameObject.AddComponent<Shadow>(); toastShadow.effectColor = new Color(0.15f, 0.1f, 0.06f, 0.9f);

        overlay = new GameObject("Cultivation overlay", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(canvasRoot.transform, false);
        var shade = overlay.GetComponent<Image>(); shade.color = new Color(0.19f, 0.23f, 0.18f, 0.42f);
        var stretch = overlay.GetComponent<RectTransform>(); stretch.anchorMin = Vector2.zero; stretch.anchorMax = Vector2.one;
        stretch.offsetMin = stretch.offsetMax = Vector2.zero;
        var window = Box(overlay.transform, "Cultivation", 0, 0, 1160, 710, ink);
        var wr = window.GetComponent<RectTransform>(); wr.anchorMin = wr.anchorMax = new Vector2(0.5f, 0.5f);
        wr.pivot = new Vector2(0.5f, 0.5f); wr.anchoredPosition = Vector2.zero;
        var title = Box(window, "Wooden title", 35, 23, 245, 55, Color.white);
        Text(title, "수 련 록", 16, 7, 213, 38, 32, cream).alignment = TextAlignmentOptions.Center;
        Text(window, "한 걸음씩 쌓아 올리는 새로운 경지", 35, 85, 470, 25, 17, muted);
        closeButton = MakeButton(window, "닫기  ESC", 984, 34, 124, 40, Close);
        closeButton.GetComponentInChildren<TMP_Text>().fontSize = 19;
        points = Text(window, "", 640, 80, 480, 26, 19, jade); points.alignment = TextAlignmentOptions.Right;
        statsTab = MakeButton(window, "능력치  C", 34, 119, 150, 40, () => ShowPage(false));
        skillsTab = MakeButton(window, "무공  K", 194, 119, 150, 40, () => ShowPage(true));
        inventoryTab = MakeButton(window, "소지품  I", 354, 119, 150, 40, () => ShowPage(2));
        statsTab.GetComponentInChildren<TMP_Text>().fontSize = 19;
        skillsTab.GetComponentInChildren<TMP_Text>().fontSize = 19;
        inventoryTab.GetComponentInChildren<TMP_Text>().fontSize = 19;
        Box(window, "Divider", 34, 174, 1092, 1, new Color(0.3f, 0.28f, 0.21f));
        statsPage = Box(window, "Attributes", 34, 192, 1092, 450, Color.clear).gameObject;
        skillsPage = Box(window, "Skill tree", 34, 192, 1092, 450, Color.clear).gameObject;
        inventoryPage = Box(window, "Inventory", 34, 192, 1092, 450, Color.clear).gameObject;
        BuildStats(statsPage.transform); BuildSkills(skillsPage.transform); BuildInventory(inventoryPage.transform);
        Text(window, "레벨업마다 능력치 +3 · 무공 +1   |   강화는 즉시 저장됩니다.   |   창을 열어도 전투는 계속됩니다.",
            45, 650, 1070, 22, 14, muted);
        overlay.SetActive(false);
        BuildDeathScreen();
        toastRect.SetAsLastSibling();
    }

    void BuildDeathScreen()
    {
        deathOverlay = new GameObject("Recovery overlay", typeof(RectTransform), typeof(Image));
        deathOverlay.transform.SetParent(canvasRoot.transform, false);
        var rect = deathOverlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        deathOverlay.GetComponent<Image>().color = new Color(0.15f, 0.18f, 0.15f, 0.7f);
        var card = Box(deathOverlay.transform, "Recovery parchment", 0, 0, 560, 290, panel);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = Vector2.zero;
        Text(card, "다시, 일어설 시간", 35, 30, 490, 50, 30, textInk).alignment = TextAlignmentOptions.Center;
        Text(card, "안전 지점에서 체력과 내력을 회복합니다.\n성장과 퀘스트 진행은 유지됩니다.", 40, 95, 480, 80, 19, muted).alignment = TextAlignmentOptions.Center;
        respawnButton = MakeButton(card, "안전 지점에서 재도전  /  R", 65, 195, 430, 55, () => { stats.Respawn(); Refresh(); });
        deathOverlay.SetActive(false);
    }

    void SaveFailed(string error) => Notify("저장 실패 / 진행은 메모리에 유지 중입니다. 저장 공간과 권한을 확인하세요.");

    void BuildStats(Transform root)
    {
        var portrait = Box(root, "Character card", 0, 0, 310, 450, panel);
        Text(portrait, "강호행 / 수련 기록", 26, 24, 260, 24, 12, gold);
        Text(portrait, "귀환자", 26, 63, 260, 55, 42, textInk);
        Text(portrait, "한때 천마를 베었던 검.\n이제 다시, 첫걸음부터.", 26, 135, 260, 66, 17, muted);
        Box(portrait, "Rule", 26, 220, 258, 1, gold);
        attributeSummary = Text(portrait, "", 26, 248, 260, 180, 18, textInk);
        string[] names = { "체력", "공격력", "방어력", "내력" };
        string[] notes = { "최대 체력 +20", "모든 검 공격 피해 +2", "방어력 +2 · 받는 피해 감소", "최대 마나 +10" };
        for (int i = 0; i < 4; i++)
        {
            int id = i;
            var row = Box(root, names[i], 334, i * 100, 758, 88, panel);
            Text(row, names[i], 22, 13, 130, 28, 21, gold);
            Text(row, notes[i], 22, 48, 350, 24, 14, muted);
            attributeValues[i] = Text(row, "", 430, 22, 170, 40, 22, textInk);
            attributeButtons[i] = MakeButton(row, "+  강화", 620, 22, 116, 40, () =>
            {
                if (stats.UpgradeAttribute((RpgAttribute)id)) Notify(names[id] + " 강화 · " + notes[id]);
                Refresh();
            });
        }
        Text(root, "포인트 1개로 한 단계를 강화합니다. 레벨업으로 기본 체력과 마나도 성장합니다.",
            334, 415, 758, 32, 13, muted);
    }

    void BuildSkills(Transform root)
    {
        for (int column = 0; column < 3; column++)
        {
            Text(root, new[] { "검술 · 파괴", "호신 · 수호", "내공 · 회복" }[column], column * 222, 0, 208, 30, 23, gold);
            for (int tier = 0; tier < 3; tier++)
            {
                int id = column * 3 + tier;
                if (tier > 0) Box(root, "Prerequisite", column * 222 + 102, 30 + tier * 130 - 26, 2, 26, gold);
                var node = MakeButton(root, "", column * 222, 40 + tier * 130, 208, 104, () => { selected = id; Refresh(); });
                nodeButtons[id] = node;
                nodeLabels[id] = node.GetComponentInChildren<TMP_Text>(); nodeLabels[id].fontSize = 20;
                nodeLabels[id].rectTransform.anchoredPosition = new Vector2(56, -4);
                nodeLabels[id].rectTransform.sizeDelta = new Vector2(144, 96);
                nodeImages[id] = node.GetComponent<Image>();
                var selection = node.gameObject.AddComponent<Outline>(); selection.effectDistance = new Vector2(2, -2);
                selection.effectColor = jade; selection.enabled = false;
                var emblemObject = new GameObject("Emblem", typeof(RectTransform), typeof(RpgSkillEmblem));
                emblemObject.transform.SetParent(node.transform, false);
                var emblemRect = emblemObject.GetComponent<RectTransform>();
                emblemRect.anchorMin = emblemRect.anchorMax = new Vector2(0, 0.5f);
                emblemRect.anchoredPosition = new Vector2(31, 0); emblemRect.sizeDelta = new Vector2(44, 44);
                var emblem = emblemObject.GetComponent<RpgSkillEmblem>(); emblem.Branch = column; emblem.color = cream; emblem.raycastTarget = false;
            }
        }
        var detail = Box(root, "Skill details", 686, 0, 406, 450, panel);
        Text(detail, "무 공 비 급", 24, 20, 358, 28, 14, gold);
        detailTitle = Text(detail, "", 24, 58, 358, 54, 36, textInk);
        Box(detail, "Rule", 24, 130, 358, 1, gold);
        detailBody = Text(detail, "", 24, 151, 358, 222, 19, muted);
        detailBody.textWrappingMode = TextWrappingModes.Normal;
        learnButton = MakeButton(detail, "", 24, 378, 358, 48, () =>
        {
            if (stats.LearnSkill(selected)) Notify(RpgSkillCatalog.All[selected].Name + " · 수련 완료");
            Refresh();
        });
        learnLabel = learnButton.GetComponentInChildren<TMP_Text>(); learnLabel.fontSize = 19;
        Skin(learnButton.GetComponent<Image>(), woodenButton);
    }

    void BuildInventory(Transform root)
    {
        var list = Box(root, "Inventory list", 0, 0, 1092, 450, panel);
        Text(list, "소 지 품", 28, 22, 1036, 32, 19, gold);
        Box(list, "Rule", 28, 65, 1036, 1, gold);
        inventoryList = Text(list, "", 28, 88, 1036, 330, 19, textInk);
    }

    void Update()
    {
        if (canvasRoot == null) return;
        var keyboard = Keyboard.current;
        bool typing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        if (!typing && keyboard != null)
        {
            if (stats.State == PlayerStats.LifeState.Dead && keyboard.rKey.wasPressedThisFrame) { stats.Respawn(); Refresh(); }
            if (keyboard.escapeKey.wasPressedThisFrame && IsOpen) Close();
            else if (!DialogueManager.IsDialogueOpen && Time.timeScale > 0f)
            {
                if (keyboard.cKey.wasPressedThisFrame) { if (opened == this && statsPage.activeSelf) Close(); else Open(false); }
                if (keyboard.kKey.wasPressedThisFrame) { if (opened == this && skillsPage.activeSelf) Close(); else Open(true); }
                if (keyboard.iKey.wasPressedThisFrame) { if (opened == this && inventoryPage.activeSelf) Close(); else OpenInventory(); }
            }
        }
        if (opened == this && (DialogueManager.IsDialogueOpen || Time.timeScale == 0f || stats.HP <= 0f)) Close();
        if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 0.1f; Refresh(); }
        if (Time.unscaledTime >= toastUntil) toast.text = "";
    }

    public void Open(bool tree)
    {
        if (DialogueManager.IsDialogueOpen || Time.timeScale == 0f || stats.HP <= 0f) return;
        if (opened != null && opened != this) opened.Close();
        opened = this; overlay.SetActive(true); ShowPage(tree); Refresh();
    }
    void OpenInventory()
    {
        if (DialogueManager.IsDialogueOpen || Time.timeScale == 0f || stats.HP <= 0f) return;
        if (opened != null && opened != this) opened.Close();
        opened = this; overlay.SetActive(true); ShowPage(2); Refresh();
    }
    void Close() { if (opened == this) { opened = null; LastClosedFrame = Time.frameCount; } if (overlay != null) overlay.SetActive(false); }
    void ShowPage(bool tree) => ShowPage(tree ? 1 : 0);
    void ShowPage(int page)
    {
        statsPage.SetActive(page == 0); skillsPage.SetActive(page == 1); inventoryPage.SetActive(page == 2);
        statsTab.GetComponentInChildren<TMP_Text>().text = page == 0 ? "능력치  C / 선택" : "능력치  C";
        skillsTab.GetComponentInChildren<TMP_Text>().text = page == 1 ? "무공  K / 선택" : "무공  K";
        inventoryTab.GetComponentInChildren<TMP_Text>().text = page == 2 ? "소지품  I / 선택" : "소지품  I";
        statsTab.GetComponent<Image>().color = page == 0 ? new Color(0.80f, 1f, 0.84f) : Color.white;
        skillsTab.GetComponent<Image>().color = page == 1 ? new Color(0.80f, 1f, 0.84f) : Color.white;
        inventoryTab.GetComponent<Image>().color = page == 2 ? new Color(0.80f, 1f, 0.84f) : Color.white;
    }
    void LevelUp(int level) { Notify($"경지 상승 · Lv. {level}    능력치 +3 / 무공 +1"); Refresh(); }
    void Notify(string message) { toast.text = message.Replace('·', '/'); toastUntil = Time.unscaledTime + 3f; }

    void Refresh()
    {
        if (canvasRoot == null) return;
        bool dead = stats.State == PlayerStats.LifeState.Dead;
        deathOverlay.SetActive(dead);
        if (dead) Close();
        identity.text = $"Lv. {stats.Level:00}";
        vitals.text = $"HP {stats.HP:0}/{stats.MaxHP:0}     MP {stats.Mana:0}/{stats.MaxMana:0}";
        SetBar(hpFill, stats.HP / stats.MaxHP); SetBar(manaFill, stats.Mana / stats.MaxMana); SetBar(expFill, stats.EXP / stats.MaxEXP);
        float moodValue = mood != null ? mood.Mood : 100f;
        SetBar(moodFill, moodValue / 100f);
        moodLabel.text = $"{(control != null && !control.IsPlayerControlled ? "어둠이 지배 중" : "정신의 균형")}  {moodValue:0}    EXP {stats.EXP:0}/{stats.MaxEXP:0}";
        guardLabel.text = skills != null && skills.GuardRemaining > 0 ? $"금강호신 / {skills.GuardRemaining:0.0}s" : "";
        for (int i = 0; i < 3; i++)
        {
            int id = RpgSkillCatalog.Hotbar[i]; var skill = RpgSkillCatalog.All[id]; float remaining = skills != null ? skills.Remaining(id) : 0f;
            string state = stats.SkillRanks[id] == 0 ? "미습득" : remaining > 0 ? $"{remaining:0.0}s" : $"MP {skill.ManaCost:0}";
            hotLabels[i].text = $"{i + 1}  {skill.Name}\n<size=11>{state}</size>";
            hotLabels[i].color = stats.SkillRanks[id] == 0 ? new Color(0.78f, 0.73f, 0.63f) : cream;
            SetBar(cooldownFills[i], remaining / skill.Cooldown);
        }
        if (!overlay.activeSelf) return;
        points.text = $"능력치 포인트  {stats.StatPoints}     /     무공 포인트  {stats.SkillPoints}";
        if (inventory == null || inventory.Items.Count == 0) inventoryList.text = "소지품이 없습니다.";
        else
        {
            inventoryList.text = "";
            foreach (var item in inventory.Items)
                inventoryList.text += $"{item.displayName}    × {item.quantity}\n";
        }
        int power = attack != null ? attack.AttackPower : 10 + stats.AttackBonus;
        attributeSummary.text = $"경지       Lv. {stats.Level}\n공격력    {power}\n방어력    {stats.Defense}\n내력 회복  {stats.ManaRegen:0.0}/초";
        string[] values = { $"{stats.MaxHP:0}", $"{power}", $"{stats.Defense}", $"{stats.MaxMana:0}" };
        for (int i = 0; i < 4; i++)
        {
            attributeValues[i].text = values[i] + $" <size=12>+{stats.AttributeRanks[i]}단</size>";
            attributeButtons[i].interactable = stats.StatPoints > 0;
        }
        for (int id = 0; id < 9; id++)
        {
            var skill = RpgSkillCatalog.All[id]; bool learned = stats.SkillRanks[id] > 0;
            nodeLabels[id].text = $"{skill.Name}\n<size=14>{(skill.Active ? "사용 무공" : "지속 효과")} / {stats.SkillRanks[id]}/{skill.MaxRank}</size>\n<size=13>Lv. {skill.RequiredLevel}</size>";
            string state = learned ? "수련 중" : stats.SkillLockReason(id).Length == 0 ? "습득 가능" : "미개방";
            nodeLabels[id].text += $" <size=13>{state}</size>";
            nodeLabels[id].color = learned ? new Color(0.80f, 1f, 0.80f) : cream;
            nodeImages[id].color = id == selected ? new Color(0.80f, 1f, 0.84f) : Color.white;
            nodeImages[id].GetComponent<Outline>().enabled = id == selected;
        }
        var chosen = RpgSkillCatalog.All[selected]; string reason = stats.SkillLockReason(selected);
        detailTitle.text = chosen.Name;
        detailBody.text = $"{chosen.Branch}  /  {(chosen.Active ? "사용 무공" : "지속 효과")}\n\n{chosen.Description}\n\n" +
            (chosen.Active ? $"소모 내력 {chosen.ManaCost:0} / 대기 {chosen.Cooldown:0}초\n" : "습득 시 항상 적용\n") +
            $"현재 {stats.SkillRanks[selected]} / {chosen.MaxRank} 단계\n필요 경지 Lv. {chosen.RequiredLevel}";
        learnButton.interactable = reason.Length == 0;
        learnLabel.text = reason.Length > 0 ? reason : stats.SkillRanks[selected] == 0 ? "습득하기 / 1 포인트" : "다음 단계 / 1 포인트";
    }

    RectTransform Box(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h);
        var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
        // Apply artwork only to surfaces, never to gauges or transparent layout containers.
        if (name == "Cultivation") { Skin(image, board); image.pixelsPerUnitMultiplier = 1.6f; }
        else if (name == "Wooden title") Skin(image, titleBoard);
        else if (name == "Divider" || name == "Rule")
        {
            Skin(image, divider); image.type = Image.Type.Simple;
            rect.sizeDelta = new Vector2(w, 5);
        }
        else if (color == panel || color == ink) { Skin(image, parchment); image.pixelsPerUnitMultiplier = 2.5f; }
        return rect;
    }
    void LoadSkin()
    {
        board = Resources.Load<Sprite>("RpgWooden/UI board Large Set");
        parchment = Resources.Load<Sprite>("RpgWooden/UI board Medium  parchment");
        woodenButton = Resources.Load<Sprite>("RpgWooden/TextBTN_Medium");
        pressedButton = Resources.Load<Sprite>("RpgWooden/TextBTN_Medium_Pressed");
        titleBoard = Resources.Load<Sprite>("RpgWooden/TextBTN_Big");
        divider = Resources.Load<Sprite>("RpgWooden/Division line");
        if (board == null || parchment == null || woodenButton == null)
            Debug.LogWarning("RPG wooden UI artwork missing; using fallback colors.");
    }
    static void Skin(Image image, Sprite sprite)
    {
        if (sprite == null) return;
        image.sprite = sprite; image.type = Image.Type.Sliced; image.color = Color.white;
    }
    TMP_Text Text(Transform parent, string value, float x, float y, float w, float h, float size, Color color)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h);
        var text = go.GetComponent<TextMeshProUGUI>(); if (koreanFont != null) text.font = koreanFont;
        text.text = value.Replace('·', '/'); text.fontSize = size; text.color = color; text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }
    Button MakeButton(Transform parent, string label, float x, float y, float w, float h, Action action)
    {
        var rect = Box(parent, label.Length > 0 ? label : "Button", x, y, w, h, panel);
        var image = rect.GetComponent<Image>(); image.raycastTarget = true;
        Skin(image, woodenButton);
        image.pixelsPerUnitMultiplier = 1f;
        if (woodenButton == null) image.color = new Color(0.32f, 0.24f, 0.16f);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors; colors.highlightedColor = new Color(1f, 0.96f, 0.82f); colors.pressedColor = new Color(0.72f, 0.82f, 0.66f);
        colors.disabledColor = new Color(0.65f, 0.65f, 0.65f); button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        // 콜백은 직렬화된 참조로 Start에서 연결한다.
        var text = Text(rect, label, 8, 4, w - 16, h - 8, 16, cream); text.alignment = TextAlignmentOptions.Center;
        return button;
    }
    Image Bar(Transform parent, float x, float y, float w, float h, Color color)
    {
        var background = Box(parent, "Gauge", x, y, w, h, new Color(0.48f, 0.39f, 0.27f));
        var fill = Box(background, "Fill", 0, 0, w, h, color);
        fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.offsetMin = fill.offsetMax = Vector2.zero;
        return fill.GetComponent<Image>();
    }
    static void SetBar(Image fill, float amount) { if (fill != null) fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(amount), 1); }
    void OnDisable() => Close();
    void OnDestroy()
    {
        SaveSystem.OnSaveFailed -= SaveFailed;
        Close(); if (stats != null) stats.OnLevelUp -= LevelUp; if (skills != null) skills.OnFeedback -= Notify;
        // 캔버스는 씬이 소유하며 씬과 함께 정리된다.
    }
}

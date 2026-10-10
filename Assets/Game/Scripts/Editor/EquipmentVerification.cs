using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 장비·등급·드롭 검증(편집 모드). 결과는 VerificationResults/Equipment 에 남는다.
/// UnityMCP execute_code 로 <c>return EquipmentVerification.Run();</c> 를 실행하거나 메뉴로 돌린다.
/// </summary>
public static class EquipmentVerification
{
    static int checks;

    [MenuItem("Tools/AINPC/Equipment/Verify Equipment (Edit Mode)")]
    static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        checks = 0;
        string output = Path.GetFullPath("VerificationResults/Equipment");
        Directory.CreateDirectory(output);
        string result;
        string slot = Path.Combine(output, "slot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(slot);
        SaveSystem.VerificationDirectory = slot;
        var created = new List<UnityEngine.Object>();
        try
        {
            VerifyTables();
            var db = CreateTestDatabase(created);
            ItemDatabase.Instance = db;
            VerifyInventory(db, created);
            result = $"PASS {checks} checks";
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            result = $"FAIL after {checks} checks: {e.Message}";
        }
        finally
        {
            SaveSystem.VerificationDirectory = null;
            ItemDatabase.Instance = null;
            foreach (var obj in created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            try { Directory.Delete(slot, true); } catch { }
        }
        File.WriteAllText(Path.Combine(output, "result.txt"), result);
        return "EQUIPMENT_VERIFY_" + result;
    }

    // ── 등급·옵션·능력치 표 ─────────────────────────────────────

    static void VerifyTables()
    {
        var rng = new System.Random(1234);
        int[] hunt = new int[5];
        for (int i = 0; i < 20000; i++) hunt[(int)ItemRarityTable.RollHunting(rng)]++;
        Check(Math.Abs(hunt[0] / 20000f - 0.65f) < 0.015f, "사냥터 Normal 65%");
        Check(Math.Abs(hunt[4] / 20000f - 0.01f) < 0.004f, "사냥터 Legendary 1%");

        int[] boss = new int[5];
        for (int i = 0; i < 20000; i++) boss[(int)ItemRarityTable.RollBoss(rng)]++;
        Check(boss[0] == 0 && Math.Abs(boss[2] / 20000f - 0.40f) < 0.015f, "보스 Normal 없음, Epic 40%");

        for (int i = 0; i < 2000; i++)
        {
            int lines = ItemRarityTable.RollOptionLines(ItemRarity.Legendary, rng);
            Check(lines >= 2 && lines <= 4, "Legendary 2~4줄");
        }
        int[] epicLines = new int[5];
        for (int i = 0; i < 20000; i++) epicLines[ItemRarityTable.RollOptionLines(ItemRarity.Epic, rng)]++;
        Check(Math.Abs(epicLines[2] / 20000f - 0.60f) < 0.015f, "Epic 2줄 60%");

        for (int i = 0; i < 500; i++)
        {
            var options = ItemOptionTable.Roll(5, ItemRarity.Legendary, rng);
            Check(options.Select(o => o.stat).Distinct().Count() == options.Count, "옵션 중복 없음");
            foreach (var o in options.Where(o => o.stat == ItemStat.CritChance))
                Check(o.value >= 0.06f * 1.75f - 1e-4f && o.value <= 0.10f * 1.75f + 1e-4f, "5단계 Legendary 치명타 범위");
            foreach (var o in options.Where(o => o.stat == ItemStat.Attack))
                Check(o.value == Mathf.Round(o.value), "공격력 옵션은 정수");
        }
        Check(ItemOptionTable.Roll(1, ItemRarity.Normal, new System.Random(7)).Count == 1, "Normal 1줄");
        for (int i = 0; i < 300; i++)
            foreach (var o in ItemOptionTable.Roll(3, ItemRarity.Normal, rng).Where(o => o.stat == ItemStat.MaxHp))
                Check(o.value >= 45 && o.value <= 80, "3단계 최대 체력 45~80 (보간)");
        Check(ItemOptionTable.Format(new ItemOption { stat = ItemStat.CritChance, value = 0.075f }) == "치명타 확률 +7.5%", "옵션 표기(%)");
        Check(ItemOptionTable.Format(new ItemOption { stat = ItemStat.Attack, value = 12 }) == "공격력 +12", "옵션 표기(정수)");

        var sword = ScriptableObject.CreateInstance<ItemDefinition>();
        sword.category = ItemCategory.Weapon; sword.attack = 40; sword.critChance = 0.21f; sword.attackSpeed = 1.32f;
        var stats = new EquipmentStats();
        stats.AddItem(sword, ItemRarity.Legendary, Array.Empty<ItemOption>());
        Check(stats[ItemStat.Attack] == 70, "공격력 40×1.75=70");
        Check(Mathf.Approximately(stats.AttackSpeed, 1.56f), "공격속도 1+0.32×1.75");
        stats.AddItem(sword, ItemRarity.Legendary, new[] { new ItemOption { stat = ItemStat.CritChance, value = 0.5f } });
        Check(Mathf.Approximately(stats.CritChance, 0.60f), "치명타 상한 60%");

        var armor = ScriptableObject.CreateInstance<ItemDefinition>();
        armor.category = ItemCategory.Armor; armor.maxHp = 150; armor.defense = 29; armor.evasion = 0.05f;
        var armorStats = new EquipmentStats();
        armorStats.AddItem(armor, ItemRarity.Epic, new[] { new ItemOption { stat = ItemStat.Evasion, value = 0.2f } });
        Check(armorStats[ItemStat.MaxHp] == 195 && armorStats[ItemStat.Defense] == 38, "갑옷 Epic 체력 195, 방어 38");
        Check(Mathf.Approximately(armorStats.Evasion, 0.15f), "회피 상한 15%");
        armorStats.Clear();
        Check(armorStats[ItemStat.MaxHp] == 0 && Mathf.Approximately(armorStats.AttackSpeed, 1f), "Clear 후 0, 공격속도 1");

        UnityEngine.Object.DestroyImmediate(sword);
        UnityEngine.Object.DestroyImmediate(armor);
    }

    // ── 아이템 개체·장착·저장 ───────────────────────────────────

    static ItemDefinition Def(List<UnityEngine.Object> created, string id, ItemCategory category, int tier, int maxStack = 1)
    {
        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        item.itemId = id; item.name = id; item.displayName = id; item.category = category; item.tier = tier; item.maxStack = maxStack;
        created.Add(item);
        return item;
    }

    static ItemDatabase CreateTestDatabase(List<UnityEngine.Object> created)
    {
        var db = ScriptableObject.CreateInstance<ItemDatabase>();
        created.Add(db);
        var cave = Def(created, "cave_sword", ItemCategory.Weapon, 1); cave.attack = 4; cave.critChance = .03f;
        var iron = Def(created, "iron_sword", ItemCategory.Weapon, 2); iron.attack = 9; iron.critChance = .07f; iron.attackSpeed = 1.08f;
        var cloth = Def(created, "cloth_armor", ItemCategory.Armor, 1); cloth.maxHp = 10; cloth.defense = 1; cloth.evasion = .01f;
        var leather = Def(created, "leather_armor", ItemCategory.Armor, 2); leather.maxHp = 30; leather.defense = 5; leather.evasion = .02f;
        Def(created, "old_talisman", ItemCategory.Accessory, 1);
        Def(created, "jade_norigae", ItemCategory.Accessory, 2);
        db.items = created.OfType<ItemDefinition>().ToList();
        for (int i = 0; i < PlayerInventory.SlotCount; i++) db.items.Add(Def(created, $"filler_{i:00}", ItemCategory.Misc, 1, 5));
        return db;
    }

    static PlayerStats CreatePlayer(List<UnityEngine.Object> created)
    {
        var player = new GameObject("Equipment verification player");
        created.Add(player);
        Invoke(player.AddComponent<MoodSystem>(), "Awake");
        var stats = player.AddComponent<PlayerStats>();
        Invoke(stats, "Awake");
        // 편집 모드의 AddComponent 는 Awake 를 부르지 않는다. 플레이 모드처럼 저장을 읽게 한다.
        Invoke(player.GetComponent<PlayerInventory>(), "Awake");
        return stats;
    }

    static void VerifyInventory(ItemDatabase db, List<UnityEngine.Object> created)
    {
        var rng = new System.Random(99);
        var stats = CreatePlayer(created);
        var inventory = stats.GetComponent<PlayerInventory>();
        Check(inventory.UsedSlotCount == 0 && inventory.GetEquipped(ItemCategory.Weapon) == null, "새 게임: 가방 비어 있고 무기 없음");
        Check(inventory.GetEquipped(ItemCategory.Armor)?.itemId == "cloth_armor", "새 게임: 천갑옷 장착");
        var talisman = inventory.GetEquipped(ItemCategory.Accessory);
        Check(talisman?.itemId == "old_talisman" && talisman.rarity == ItemRarity.Normal && talisman.options.Count == 1, "새 게임: 낡은 부적 Normal 옵션 1줄");
        Check(inventory.Stats[ItemStat.MaxHp] >= 10, "천갑옷 체력이 합계에 반영");

        // 같은 아이템 두 개(등급 다름)는 칸을 따로 차지한다.
        var iron = db.Find("iron_sword");
        Check(inventory.TryAddInstance(PlayerInventory.CreateInstance(iron, ItemRarity.Rare, rng)) &&
              inventory.TryAddInstance(PlayerInventory.CreateInstance(iron, ItemRarity.Epic, rng)) && inventory.UsedSlotCount == 2,
            "TwoEpicSwordsTakeTwoSlots");

        // 장착하면 원래 장비가 그 칸으로 돌아온다.
        Check(inventory.TryAdd(db.Find("cave_sword")) && inventory.Count("cave_sword") == 1, "낡은 검 획득");
        int caveSlot = Enumerable.Range(0, PlayerInventory.SlotCount).First(i => inventory.GetItemInSlot(i)?.itemId == "cave_sword");
        int changed = 0;
        inventory.EquipmentChanged += () => changed++;
        Check(inventory.Equip(caveSlot) && inventory.GetEquipped(ItemCategory.Weapon)?.itemId == "cave_sword" &&
              inventory.GetItemInSlot(caveSlot) == null && inventory.Stats[ItemStat.Attack] == 4 && changed == 1, "빈 무기 칸에 장착");
        Check(!inventory.TryAdd(db.Find("cave_sword")) && inventory.Count("cave_sword") == 1, "장착한 낡은 검도 보유 수에 들어가 다시 줍지 않음");
        int epicSlot = Enumerable.Range(0, PlayerInventory.SlotCount).First(i => inventory.GetItemInSlot(i)?.rarity == ItemRarity.Epic);
        Check(inventory.Equip(epicSlot) && inventory.GetEquipped(ItemCategory.Weapon).rarity == ItemRarity.Epic &&
              inventory.GetItemInSlot(epicSlot)?.itemId == "cave_sword" && inventory.Stats[ItemStat.Attack] == 12, "EquipSwapsBack");
        Check(!inventory.Equip(PlayerInventory.SlotCount - 1), "빈 칸 장착은 실패");

        // 가방이 가득 차면 해제하지 않는다.
        for (int i = 0; inventory.UsedSlotCount < PlayerInventory.SlotCount; i++) inventory.TryAdd(db.Find($"filler_{i:00}"));
        Check(!inventory.Unequip(ItemCategory.Weapon) && inventory.GetEquipped(ItemCategory.Weapon)?.itemId == "iron_sword", "UnequipFailsWhenBagFull");
        int fillerSlot = Enumerable.Range(0, PlayerInventory.SlotCount).First(i => inventory.GetItemInSlot(i)?.itemId.StartsWith("filler") == true);
        Check(!inventory.Equip(fillerSlot), "기타 아이템은 장착 안 됨");
        inventory.Remove(inventory.GetItemInSlot(fillerSlot).itemId);
        Check(inventory.Unequip(ItemCategory.Armor) && inventory.GetEquipped(ItemCategory.Armor) == null &&
              inventory.GetItemInSlot(fillerSlot)?.itemId == "cloth_armor" &&
              inventory.Stats[ItemStat.MaxHp] == SumOption(inventory, ItemStat.MaxHp), "빈칸이 있으면 해제, 갑옷 체력 빠짐");

        // 저장 왕복: 같은 itemId 장비 여러 개, 등급·옵션·장착이 유지된다.
        stats.Save();
        Check(SaveSystem.LastError == null, "DuplicateEquipmentIdsSurviveSave: 저장 검증 통과");
        var reloaded = new GameObject("Equipment reload").AddComponent<PlayerInventory>();
        created.Add(reloaded.gameObject);
        reloaded.Load(SaveSystem.LoadPlayer());
        var savedTalisman = reloaded.GetEquipped(ItemCategory.Accessory);
        Check(reloaded.GetEquipped(ItemCategory.Weapon)?.rarity == ItemRarity.Epic && reloaded.GetEquipped(ItemCategory.Armor) == null &&
              savedTalisman.options.Count == 1 && savedTalisman.options[0].stat == talisman.options[0].stat &&
              Mathf.Approximately(savedTalisman.options[0].value, talisman.options[0].value), "DuplicateEquipmentIdsSurviveSave: 장착 유지");
        Check(Enumerable.Range(0, PlayerInventory.SlotCount).Count(i => reloaded.GetItemInSlot(i)?.itemId == "iron_sword" &&
              reloaded.GetItemInSlot(i).rarity == ItemRarity.Rare) == 1, "DuplicateEquipmentIdsSurviveSave: 가방의 Rare 무쇠검 유지");
        Check(reloaded.GetEquipped(ItemCategory.Armor) == null, "일부러 벗은 장비 칸에는 시작 장비를 다시 주지 않음");

        // 장비 필드가 생기기 전 저장: 시작 장비를 채우고 가방의 낡은 검을 장착한다.
        reloaded.Load(new PlayerSaveData { inventory = new[] {
            new InventorySaveEntry { itemId = "cave_sword", displayName = "낡은 검", quantity = 1, slotIndex = 0 } } });
        Check(reloaded.GetEquipped(ItemCategory.Weapon)?.itemId == "cave_sword" && reloaded.GetItemInSlot(0) == null &&
              reloaded.GetEquipped(ItemCategory.Armor)?.itemId == "cloth_armor" &&
              reloaded.GetEquipped(ItemCategory.Accessory)?.options.Count == 1, "LegacySaveGetsStarterGear");

        Check(Throws(() => PlayerInventory.Validate(new[] { new InventorySaveEntry { itemId = "iron_sword", quantity = 1, rarity = 5 } }, null)),
            "등급 범위 밖은 거부");
        Check(Throws(() => PlayerInventory.Validate(Array.Empty<InventorySaveEntry>(), new InventorySaveEntry[2])), "장착 칸 수가 3이 아니면 거부");
    }

    static float SumOption(PlayerInventory inventory, ItemStat stat) =>
        new[] { ItemCategory.Weapon, ItemCategory.Armor, ItemCategory.Accessory }
            .Select(inventory.GetEquipped).Where(e => e != null).SelectMany(e => e.options).Where(o => o.stat == stat).Sum(o => o.value);

    static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, null);

    static bool Throws(Action action)
    {
        try { action(); return false; }
        catch { return true; }
    }

    static void Check(bool pass, string message)
    {
        if (!pass) throw new Exception(message);
        checks++;
    }
}

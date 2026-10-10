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
            VerifyAssets();
            var db = CreateTestDatabase(created);
            ItemDatabase.Instance = db;
            VerifyInventory(db, created);
            VerifyCombat(db, created);
            VerifyDrops(db, created);
            VerifyWindow(db, created);
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

    // ── 생성된 에셋 ─────────────────────────────────────────────

    static void VerifyAssets()
    {
        var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>("Assets/Resources/ItemDatabase.asset");
        var equipment = db.items.Where(i => i != null && i.category != ItemCategory.Misc).ToList();
        Check(equipment.Count == 15, $"장비 15종 ({equipment.Count})");
        foreach (var category in new[] { ItemCategory.Weapon, ItemCategory.Armor, ItemCategory.Accessory })
            for (int tier = 1; tier <= 5; tier++)
                Check(equipment.Count(i => i.category == category && i.tier == tier) == 1, $"{category} {tier}단계 하나");
        Check(equipment.All(i => i.icon != null && i.maxStack == 1 && !string.IsNullOrWhiteSpace(i.description)), "모두 아이콘·설명, 겹침 1");
        var iron = db.Find("iron_sword");
        Check(iron.displayName == "무쇠검" && iron.attack == 9 && Mathf.Approximately(iron.critChance, .07f) && Mathf.Approximately(iron.attackSpeed, 1.08f), "무쇠검 수치");
        var dragon = db.Find("dragonscale_armor");
        Check(dragon.displayName == "용린갑" && dragon.maxHp == 150 && dragon.defense == 29 && Mathf.Approximately(dragon.evasion, .05f), "용린갑 수치");
        var cave = db.Find("cave_sword");
        Check(cave.attack == 4 && Mathf.Approximately(cave.critChance, .03f) && Mathf.Approximately(cave.attackSpeed, 1f) && cave.tier == 1, "낡은 검 수치");
        Check(db.Find("heaven_talisman")?.displayName == "천상의 부적" && db.Find("old_talisman")?.tier == 1, "장신구 이름·단계");

        MonsterData Monster(string path) => AssetDatabase.LoadAssetAtPath<MonsterData>(path);
        var bat3 = Monster("Assets/Data/Monsters/Region3/Region3_Bat.asset");
        Check(bat3 != null && bat3.Level == 11 && bat3.MaxHP == 445 && bat3.AttackDamage == 55 && bat3.ExpReward == 641 && bat3.DropTier == 4, "3지역 박쥐");
        var mushroom2 = Monster("Assets/Data/Monsters/Region2/Region2_Mushroom.asset");
        Check(mushroom2 != null && mushroom2.MaxHP == 85 && mushroom2.DropTier == 3 && mushroom2.MonsterName == Monster("Assets/Data/Monsters/MushroomData.asset").MonsterName,
            "2지역 버섯, 이름은 원본 유지");
        var boss4 = Monster("Assets/Data/Monsters/Bosses/Region4Boss.asset");
        Check(boss4.MaxHP == 31000 && boss4.AttackDamage == 178 && boss4.DropTier == 5 && boss4.Level == 17, "4지역 보스");
        Check(Monster("Assets/Data/Monsters/Bosses/FinalBoss.asset").DropTier == 0 && Monster("Assets/Data/Monsters/Bosses/FinalBoss.asset").MaxHP == 48250, "최종보스");
        Check(Monster("Assets/Game/Data/HubStory/Enemy1.asset").DropTier == 2 && Monster("Assets/Game/Data/HubStory/AshKing.asset").DropTier == 2, "1지역 사냥터·재의 왕 2단계");
        Check(Monster("Assets/Game/Data/HubStory/Enemy1.asset").MaxHP == 30, "1지역 수치 유지");

        string mushroomGuid = AssetDatabase.AssetPathToGUID("Assets/Data/Monsters/MushroomData.asset");
        foreach (int region in new[] { 2, 3, 4 })
        {
            string scene = File.ReadAllText($"Assets/Scenes/Region{region}.unity");
            Check(!scene.Contains(mushroomGuid) && scene.Contains(AssetDatabase.AssetPathToGUID($"Assets/Data/Monsters/Region{region}/Region{region}_Mushroom.asset")),
                $"Region{region} 씬이 지역 데이터를 씀");
        }
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
        Def(created, "steel_sword", ItemCategory.Weapon, 3);
        Def(created, "chain_armor", ItemCategory.Armor, 3);
        Def(created, "silver_bracelet", ItemCategory.Accessory, 3);
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
              inventory.GetItemInSlot(caveSlot) == null && inventory.Stats[ItemStat.Attack] == 4 + SumOption(inventory, ItemStat.Attack) && changed == 1, "빈 무기 칸에 장착");
        Check(!inventory.TryAdd(db.Find("cave_sword")) && inventory.Count("cave_sword") == 1, "장착한 낡은 검도 보유 수에 들어가 다시 줍지 않음");
        int epicSlot = Enumerable.Range(0, PlayerInventory.SlotCount).First(i => inventory.GetItemInSlot(i)?.rarity == ItemRarity.Epic);
        Check(inventory.Equip(epicSlot) && inventory.GetEquipped(ItemCategory.Weapon).rarity == ItemRarity.Epic &&
              inventory.GetItemInSlot(epicSlot)?.itemId == "cave_sword" && inventory.Stats[ItemStat.Attack] == 12 + SumOption(inventory, ItemStat.Attack), "EquipSwapsBack");
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
        // 동굴을 이미 지난 옛 저장은 검이 없어도 낡은 검을 받는다. 기본 공격력이 6 으로 내려가 1지역이 어려워지지 않게.
        reloaded.Load(new PlayerSaveData { level = 4, inventory = Array.Empty<InventorySaveEntry>() });
        Check(reloaded.GetEquipped(ItemCategory.Weapon)?.itemId == "cave_sword" && reloaded.Count("cave_sword") == 1, "LegacySaveWithoutSwordGetsCaveSword");

        Check(Throws(() => PlayerInventory.Validate(new[] { new InventorySaveEntry { itemId = "iron_sword", quantity = 1, rarity = 5 } }, null)),
            "등급 범위 밖은 거부");
        Check(Throws(() => PlayerInventory.Validate(Array.Empty<InventorySaveEntry>(), new InventorySaveEntry[2])), "장착 칸 수가 3이 아니면 거부");
        // JsonUtility 는 null 배열을 [] 로 쓴다. 플레이어 없이 옛 저장을 다시 쓰면 equipped 가 빈 배열이 된다.
        Check(!Throws(() => PlayerInventory.Validate(Array.Empty<InventorySaveEntry>(), new InventorySaveEntry[0])), "빈 장착 배열은 옛 저장으로 허용");
        reloaded.Load(new PlayerSaveData { level = 2, inventory = Array.Empty<InventorySaveEntry>(), equipped = new InventorySaveEntry[0] });
        Check(reloaded.GetEquipped(ItemCategory.Armor)?.itemId == "cloth_armor", "빈 장착 배열 저장도 시작 장비를 받음");
        Check(Throws(() => PlayerInventory.Validate(new[] { new InventorySaveEntry { itemId = "jade_norigae", quantity = 1, options = new[] { new ItemOption { stat = (ItemStat)99, value = 1 } } } })) &&
              Throws(() => PlayerInventory.Validate(new[] { new InventorySaveEntry { itemId = "jade_norigae", quantity = 1, options = new[] { new ItemOption { stat = ItemStat.MaxHp, value = float.NaN } } } })),
            "옵션 종류·값이 잘못된 저장은 거부");
    }

    // ── 전투 반영 ───────────────────────────────────────────────

    sealed class DamageProbe : IDamageable
    {
        public int total, hits;
        public void TakeDamage(int damage) { total += damage; hits++; }
    }

    /// <summary>옵션을 정해 둔 장신구를 장착한다. 무작위 옵션이 검사를 흔들지 않게 한다.</summary>
    static void EquipAccessory(PlayerInventory inventory, params ItemOption[] options)
    {
        var entry = new InventoryEntry { itemId = "jade_norigae", displayName = "검증 장신구", rarity = ItemRarity.Normal, options = options.ToList() };
        if (!inventory.TryAddInstance(entry)) throw new Exception("장신구를 넣을 칸이 없음");
        inventory.Equip(entry.slotIndex);
    }

    static ItemOption Opt(ItemStat stat, float value) => new ItemOption { stat = stat, value = value };

    static void VerifyCombat(ItemDatabase db, List<UnityEngine.Object> created)
    {
        SaveSystem.DeleteSave();
        var rng = new System.Random(5);
        var stats = CreatePlayer(created);
        var inventory = stats.GetComponent<PlayerInventory>();
        var attack = stats.gameObject.AddComponent<Player_Attack>();
        var skills = stats.GetComponent<RpgSkillController>();
        EquipAccessory(inventory);
        Check(inventory.TryAdd(db.Find("cave_sword")), "낡은 검 획득");
        inventory.Equip(inventory.Items.First(e => e.itemId == "cave_sword").slotIndex);
        Check(attack.AttackPower == 10, "BaseAttackUnchanged: 기본 6 + 낡은 검 4");

        // 최대 체력 보너스는 저장되는 기본값과 섞이지 않는다.
        float baseHp = stats.BaseMaxHP;
        Check(stats.MaxHP == baseHp + inventory.Stats[ItemStat.MaxHp], "최대 체력 = 기본 + 장비");
        var leather = PlayerInventory.CreateInstance(db.Find("leather_armor"), ItemRarity.Legendary, rng);
        inventory.TryAddInstance(leather); inventory.Equip(leather.slotIndex);
        Check(stats.MaxHP == baseHp + 53 && stats.Defense == 9, "가죽갑옷 Legendary: 체력 +53, 방어 +9");
        stats.Heal(9999f);
        stats.Save();
        var reloaded = CreatePlayer(created);
        Check(Mathf.Approximately(reloaded.BaseMaxHP, baseHp) && Mathf.Approximately(reloaded.MaxHP, baseHp + 53) &&
              Mathf.Approximately(reloaded.HP, baseHp + 53), "MaxHpBonusNotPersisted");

        Check(Mathf.Approximately(stats.HP, stats.MaxHP), "체력 가득");
        inventory.Unequip(ItemCategory.Armor);
        Check(Mathf.Approximately(stats.HP, baseHp) && Mathf.Approximately(stats.MaxHP, baseHp), "UnequipClampsHp");

        // 치명타: 확률 상한 60%, 피해 150%.
        inventory.Unequip(ItemCategory.Accessory);
        EquipAccessory(inventory, Opt(ItemStat.CritChance, 0.9f));
        UnityEngine.Random.InitState(1);
        var probe = new DamageProbe();
        for (int i = 0; i < 4000; i++) stats.DealDamage(probe, 10);
        float average = probe.total / (float)probe.hits;
        Check(Math.Abs(average - 13f) < 0.3f, $"CritDoublesDamage: 평균 {average:0.00} ≈ 13");
        Check(stats.DealDamage(null, 10) == 0 && stats.DealDamage(probe, 0) == 0, "대상 없음·0 피해는 무시");

        // 흡혈: 준 피해의 비율만큼 회복.
        inventory.Unequip(ItemCategory.Accessory);
        EquipAccessory(inventory, Opt(ItemStat.LifeSteal, 0.5f));
        stats.TakeDamage(40);
        float hpBefore = stats.HP;
        int dealt = stats.DealDamage(probe, 20);
        Check(Mathf.Approximately(stats.HP, hpBefore + dealt * 0.5f), "흡혈 회복");
        // 흡혈은 실제로 깎은 체력만큼만. 넘친 피해와 이미 쓰러진 몬스터에게 준 피해는 회복하지 않는다.
        var monsterObject = new GameObject("Lifesteal target", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D));
        created.Add(monsterObject);
        var monster = monsterObject.AddComponent<MushroomMonster>();
        var monsterData = ScriptableObject.CreateInstance<MonsterData>();
        created.Add(monsterData);
        var so = new SerializedObject(monsterData);
        so.FindProperty("maxHP").floatValue = 10f; so.FindProperty("expReward").floatValue = 0f; so.ApplyModifiedPropertiesWithoutUndo();
        monster.Initialize(monsterData, null);
        stats.TakeDamage(40);
        hpBefore = stats.HP;
        stats.DealDamage(monster, 100);
        Check(Mathf.Approximately(stats.HP, hpBefore + 5f), "넘친 피해는 흡혈하지 않음 (체력 10 × 0.5)");
        hpBefore = stats.HP;
        stats.DealDamage(monster, 50);
        Check(Mathf.Approximately(stats.HP, hpBefore), "쓰러진 몬스터 타격은 흡혈하지 않음");

        // 회피: 상한 15%.
        inventory.Unequip(ItemCategory.Accessory);
        EquipAccessory(inventory, Opt(ItemStat.Evasion, 0.5f));
        int dodged = 0;
        for (int i = 0; i < 2000; i++)
        {
            stats.Heal(9999f);
            float before = stats.HP;
            stats.TakeDamage(10);
            if (Mathf.Approximately(stats.HP, before)) dodged++;
        }
        Check(dodged / 2000f > 0.11f && dodged / 2000f < 0.19f, $"EvasionCapped: {dodged / 20f:0.0}%");

        // 공격속도: 기본 공격·돌진·무공 쿨타임 ÷ 공격속도.
        inventory.Unequip(ItemCategory.Accessory);
        EquipAccessory(inventory, Opt(ItemStat.AttackSpeed, 0.25f));
        Check(Mathf.Approximately(stats.AttackSpeed, 1.25f), "공격속도 1.25");
        Check(Mathf.Approximately(skills.CooldownFor(0), RpgSkillCatalog.All[0].Cooldown / 1.25f), "AttackSpeedShortensCooldown: 무공");
        Check(Mathf.Approximately(attack.AttackInterval, 0.8f / 1.25f) && Mathf.Approximately(attack.DashInterval, 5f / 1.25f),
            "AttackSpeedShortensCooldown: 기본 공격·돌진");

        // 경험치·처치 회복·재생.
        inventory.Unequip(ItemCategory.Accessory);
        EquipAccessory(inventory, Opt(ItemStat.ExpGain, 0.5f), Opt(ItemStat.KillHeal, 7f), Opt(ItemStat.MaxMana, 20f), Opt(ItemStat.Defense, 3f));
        float exp = stats.EXP;
        stats.AddEXP(10f);
        Check(Mathf.Approximately(stats.EXP, exp + 15f), "획득 경험치 +50%");
        stats.TakeDamage(30);
        hpBefore = stats.HP;
        stats.OnKill();
        Check(Mathf.Approximately(stats.HP, hpBefore + 7f), "처치 시 회복");
        Check(Mathf.Approximately(stats.MaxMana, stats.BaseMaxMana + 20f) && stats.Defense == 3, "최대 내력·방어력 옵션");
    }

    // ── 드롭과 줍기 ─────────────────────────────────────────────

    static void VerifyDrops(ItemDatabase db, List<UnityEngine.Object> created)
    {
        var rng = new System.Random(42);
        int dropped = 0, wrongTier = 0;
        for (int i = 0; i < 100000; i++)
        {
            var entry = ItemDrop.Roll(2, false, 0f, db, rng);
            if (entry == null) continue;
            dropped++;
            if (db.Find(entry.itemId).tier != 2) wrongTier++;
        }
        Check(Math.Abs(dropped / 100000f - 0.01f) < 0.002f && wrongTier == 0, $"HuntingRate: {dropped / 1000f:0.00}%, 모두 2단계");

        dropped = 0;
        for (int i = 0; i < 100000; i++) if (ItemDrop.Roll(2, false, 1f, db, rng) != null) dropped++;
        Check(Math.Abs(dropped / 100000f - 0.02f) < 0.003f, $"DropRateBonus: {dropped / 1000f:0.00}%");

        var categories = new HashSet<ItemCategory>();
        bool allBoss = true;
        for (int i = 0; i < 1000; i++)
        {
            var entry = ItemDrop.Roll(3, true, 0f, db, rng);
            allBoss &= entry != null && entry.rarity != ItemRarity.Normal && db.Find(entry.itemId).tier == 3;
            if (entry != null) categories.Add(db.Find(entry.itemId).category);
        }
        Check(allBoss && categories.Count == 3, "BossAlwaysDrops: Normal 없음, 세 부위 모두");
        Check(ItemDrop.Roll(5, true, 0f, db, rng) == null, "해당 단계 정의가 없으면 드롭 없음");
        var accessory = ItemDrop.Roll(3, true, 0f, db, new System.Random(3));
        for (int seed = 4; db.Find(accessory.itemId).category != ItemCategory.Accessory; seed++) accessory = ItemDrop.Roll(3, true, 0f, db, new System.Random(seed));
        Check(accessory.options.Count >= 1, "장신구 드롭에는 옵션이 붙음");

        // 줍기: 가방이 가득 차면 남고, 비어 있는 장착 칸에는 바로 낀다.
        SaveSystem.DeleteSave();
        var stats = CreatePlayer(created);
        stats.gameObject.AddComponent<BoxCollider2D>();
        var inventory = stats.GetComponent<PlayerInventory>();
        for (int i = 0; inventory.UsedSlotCount < PlayerInventory.SlotCount; i++) inventory.TryAdd(db.Find($"filler_{i:00}"));
        var pickup = ItemDrop.Spawn(PlayerInventory.CreateInstance(db.Find("iron_sword"), ItemRarity.Epic, rng), Vector3.zero);
        created.Add(pickup.gameObject);
        Check(pickup.GetComponent<YSortRenderer>() != null && pickup.GetComponent<Collider2D>().isTrigger &&
              pickup.GetComponent<SpriteRenderer>() != null, "드롭 오브젝트: 정렬·트리거·스프라이트");
        var label = pickup.GetComponentInChildren<TMPro.TMP_Text>();
        Check(label != null && label.color == ItemRarityTable.Color(ItemRarity.Epic), "이름표가 등급색");
        Invoke(pickup, "OnTriggerEnter2D", stats.GetComponent<Collider2D>());
        Check(pickup != null && inventory.GetEquipped(ItemCategory.Weapon) == null && inventory.Count("iron_sword") == 0, "PickupStaysWhenBagFull");

        inventory.Remove("filler_00");
        Invoke(pickup, "OnTriggerEnter2D", stats.GetComponent<Collider2D>());
        Check(inventory.GetEquipped(ItemCategory.Weapon)?.rarity == ItemRarity.Epic && inventory.Count("iron_sword") == 1, "PickupAutoEquipsEmptySlot");
        Check(pickup == null, "주운 오브젝트는 사라짐");
    }

    // ── 인벤토리 창 ─────────────────────────────────────────────

    static void VerifyWindow(ItemDatabase db, List<UnityEngine.Object> created)
    {
        // 앞 검사에서 만든 플레이어가 남아 있으면 창이 엉뚱한 인벤토리에 붙는다.
        foreach (var old in UnityEngine.Object.FindObjectsByType<PlayerStats>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(old.gameObject);
        SaveSystem.DeleteSave();
        var swordIcon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/CaveSword.png");
        db.Find("iron_sword").icon = swordIcon;
        db.Find("cave_sword").icon = swordIcon;
        var stats = CreatePlayer(created);
        var inventory = stats.GetComponent<PlayerInventory>();
        var iron = PlayerInventory.CreateInstance(db.Find("iron_sword"), ItemRarity.Epic, new System.Random(1));
        inventory.TryAddInstance(iron);
        inventory.TryAdd(db.Find("cave_sword"));

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(InventoryUiBuilder.PrefabPath));
        created.Add(instance);
        var window = instance.GetComponent<InventoryWindow>();
        var bag = Field<InventorySlotView[]>(window, "bagSlots");
        var equipment = Field<EquipmentSlotView[]>(window, "equipmentSlots");
        var tooltip = Field<ItemTooltip>(window, "itemTooltip");
        var weaponSlot = equipment.First(s => s.AcceptedCategory == ItemCategory.Weapon);
        var eventSystem = new GameObject("Equipment verification EventSystem", typeof(UnityEngine.EventSystems.EventSystem)).GetComponent<UnityEngine.EventSystems.EventSystem>();
        created.Add(eventSystem.gameObject);
        Invoke(window, "Awake");
        Check(window.Open(), "창 열림");

        int ironSlot = iron.slotIndex;
        Check(bag[ironSlot].GetComponent<UnityEngine.UI.Image>().color == ItemRarityTable.Color(ItemRarity.Epic), "Epic 칸 테두리가 보라색");

        RightClick(bag[ironSlot].gameObject, eventSystem);
        Check(inventory.GetEquipped(ItemCategory.Weapon) == iron, "가방 칸 우클릭 → 장착");
        Check(weaponSlot.transform.Find("EquippedItemIcon").GetComponent<UnityEngine.UI.Image>().enabled &&
              !weaponSlot.transform.Find("EmptySlotSilhouette").GetComponent<UnityEngine.UI.Image>().enabled &&
              weaponSlot.GetComponent<UnityEngine.UI.Image>().color == ItemRarityTable.Color(ItemRarity.Epic), "장비 칸에 아이콘·등급색 표시");
        Check(bag[ironSlot].GetComponent<UnityEngine.UI.Image>().color == Color.white, "빈 칸 테두리는 흰색");

        int caveSlot = inventory.Items.First(e => e.itemId == "cave_sword").slotIndex;
        window.OnSlotPointerEnter(bag[caveSlot]);
        var texts = tooltip.GetComponentsInChildren<TMPro.TMP_Text>(true);
        var nameText = texts.First(t => t.name == "ItemNameText");
        string body = texts.First(t => t.name == "ItemDescriptionText").text;
        Check(nameText.color == ItemRarityTable.Color(ItemRarity.Normal), "툴팁 이름이 등급색");
        Check(texts.First(t => t.name == "ItemCategoryText").text == "Normal · 1단계 무기", "툴팁 종류 줄");
        Check(body.Contains("공격력 +4") && body.Contains("▼8"), "툴팁 능력치와 장착품 비교(▼)");
        Check(!texts.First(t => t.name == "ItemQuantityText").gameObject.activeSelf, "장비는 수량 줄을 숨김");
        window.OnSlotPointerExit(bag[caveSlot]);

        Invoke(weaponSlot, "OnPointerEnter", new UnityEngine.EventSystems.PointerEventData(eventSystem));
        Check(tooltip.gameObject.activeSelf && nameText.text == iron.displayName, "장비 칸 호버 → 장착품 툴팁");
        Invoke(weaponSlot, "OnPointerExit", new UnityEngine.EventSystems.PointerEventData(eventSystem));

        RightClick(weaponSlot.gameObject, eventSystem);
        Check(inventory.GetEquipped(ItemCategory.Weapon) == null && inventory.Items.Contains(iron), "장비 칸 우클릭 → 해제");

        var pointer = new UnityEngine.EventSystems.PointerEventData(eventSystem) { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
        window.BeginItemDrag(bag[iron.slotIndex], pointer);
        window.DropItemOnEquipment(equipment.First(s => s.AcceptedCategory == ItemCategory.Armor));
        window.EndItemDrag();
        Check(inventory.GetEquipped(ItemCategory.Weapon) == null && inventory.GetEquipped(ItemCategory.Armor)?.itemId != "iron_sword", "다른 부위 칸에 놓으면 장착 안 됨");
        window.BeginItemDrag(bag[iron.slotIndex], pointer);
        window.DropItemOnEquipment(weaponSlot);
        window.EndItemDrag();
        Check(inventory.GetEquipped(ItemCategory.Weapon) == iron, "가방 → 무기 칸 드래그 → 장착");
        window.Close();
    }

    static void RightClick(GameObject target, UnityEngine.EventSystems.EventSystem eventSystem) =>
        UnityEngine.EventSystems.ExecuteEvents.Execute(target,
            new UnityEngine.EventSystems.PointerEventData(eventSystem) { button = UnityEngine.EventSystems.PointerEventData.InputButton.Right },
            UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);

    static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    static void Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, args);

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

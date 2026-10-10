using System;
using System.IO;
using System.Linq;
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
        try
        {
            VerifyTables();
            result = $"PASS {checks} checks";
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            result = $"FAIL after {checks} checks: {e.Message}";
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

    static void Check(bool pass, string message)
    {
        if (!pass) throw new Exception(message);
        checks++;
    }
}

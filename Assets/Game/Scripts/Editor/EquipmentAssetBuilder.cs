using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 장비 14종(낡은 검은 기존 에셋에 수치만)·도트 아이콘을 만들고 ItemDatabase 를 채운다. 1지역 몬스터의 드롭 단계도 정한다.
/// 2~4지역·최종 몬스터는 <see cref="StageBuilder"/> 가 만든다.
/// 수치의 원본은 docs/superpowers/specs/2026-10-11-equipment-design.md.
/// </summary>
public static class EquipmentAssetBuilder
{
    const string ItemFolder = "Assets/Game/Items/Equipment";
    const string IconFolder = "Assets/Sprites/UI/Items";
    const string DatabasePath = "Assets/Resources/ItemDatabase.asset";

    struct ItemSpec
    {
        public string id, name, description;
        public ItemCategory category;
        public int tier, attack, maxHp, defense;
        public float crit, speed, evasion;
    }

    static ItemSpec Weapon(string id, string name, int tier, int attack, float crit, float speed, string description) =>
        new ItemSpec { id = id, name = name, tier = tier, category = ItemCategory.Weapon, attack = attack, crit = crit, speed = speed, description = description };
    static ItemSpec Armor(string id, string name, int tier, int hp, int defense, float evasion, string description) =>
        new ItemSpec { id = id, name = name, tier = tier, category = ItemCategory.Armor, maxHp = hp, defense = defense, evasion = evasion, speed = 1f, description = description };
    static ItemSpec Accessory(string id, string name, int tier, string description) =>
        new ItemSpec { id = id, name = name, tier = tier, category = ItemCategory.Accessory, speed = 1f, description = description };

    static readonly ItemSpec[] Items =
    {
        Weapon("iron_sword", "무쇠검", 2, 9, .07f, 1.08f, "대장간에서 막 두드려 낸 무쇠 검. 투박하지만 날이 곧다."),
        Weapon("steel_sword", "청강검", 3, 16, .11f, 1.16f, "푸른 빛이 도는 강철로 벼린 검. 가볍고 빠르다."),
        Weapon("blacksteel_sword", "묵철검", 4, 26, .15f, 1.24f, "먹빛 쇠로 만든 검. 베인 자리에 그림자가 남는다고 한다."),
        Weapon("heavenfire_sword", "천화검", 5, 40, .21f, 1.32f, "하늘의 불을 담았다는 검. 휘두를 때마다 불꽃이 인다."),
        Armor("cloth_armor", "천갑옷", 1, 10, 1, .01f, "두꺼운 천을 겹겹이 누빈 옷. 없는 것보다는 낫다."),
        Armor("leather_armor", "가죽갑옷", 2, 30, 5, .02f, "질긴 짐승 가죽을 덧댄 갑옷."),
        Armor("chain_armor", "사슬갑옷", 3, 60, 11, .03f, "쇠고리를 촘촘히 엮은 갑옷. 칼날이 미끄러진다."),
        Armor("plate_armor", "철편갑", 4, 100, 19, .04f, "쇳조각을 비늘처럼 이어 붙인 갑옷."),
        Armor("dragonscale_armor", "용린갑", 5, 150, 29, .05f, "용의 비늘로 지었다는 전설의 갑옷."),
        Accessory("old_talisman", "낡은 부적", 1, "글자가 반쯤 지워진 부적. 아직 기운이 남아 있다."),
        Accessory("jade_norigae", "옥 노리개", 2, "맑은 옥을 꿴 노리개. 지니면 마음이 가라앉는다."),
        Accessory("silver_bracelet", "은빛 팔찌", 3, "은실을 꼬아 만든 팔찌. 달빛을 머금고 있다."),
        Accessory("diamond_beads", "금강 염주", 4, "단단한 금강석 알을 꿴 염주."),
        Accessory("heaven_talisman", "천상의 부적", 5, "하늘의 문자로 쓴 부적. 손에 쥐면 따뜻하다."),
    };

    [MenuItem("Tools/AINPC/Equipment/Build")]
    public static string Build()
    {
        Directory.CreateDirectory(ItemFolder);
        Directory.CreateDirectory(IconFolder);
        foreach (var spec in Items) CreateItem(spec);
        UpdateCaveSword();
        RefreshDatabase();
        SetHubDropTiers();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return "EQUIPMENT_BUILD_OK";
    }

    static void CreateItem(ItemSpec spec)
    {
        string path = $"{ItemFolder}/{spec.id}.asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemDefinition>();
            AssetDatabase.CreateAsset(item, path);
        }
        item.itemId = spec.id; item.displayName = spec.name; item.description = spec.description;
        item.category = spec.category; item.tier = spec.tier; item.maxStack = 1;
        item.attack = spec.attack; item.critChance = spec.crit; item.attackSpeed = spec.speed;
        item.maxHp = spec.maxHp; item.defense = spec.defense; item.evasion = spec.evasion;
        item.icon = SaveIcon(spec.id, ItemIcons.Draw(spec.category, spec.tier));
        EditorUtility.SetDirty(item);
    }

    static void UpdateCaveSword()
    {
        var cave = AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Game/Items/CaveSword.asset");
        cave.tier = 1; cave.attack = 4; cave.critChance = .03f; cave.attackSpeed = 1f; cave.maxStack = 1;
        EditorUtility.SetDirty(cave);
    }

    static void RefreshDatabase()
    {
        var database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DatabasePath);
        database.items = AssetDatabase.FindAssets("t:ItemDefinition")
            .Select(guid => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(item => item != null)
            .OrderBy(item => item.category).ThenBy(item => item.tier).ThenBy(item => item.itemId, StringComparer.Ordinal)
            .ToList();
        EditorUtility.SetDirty(database);
    }

    static Sprite SaveIcon(string id, Texture2D texture)
    {
        string path = $"{IconFolder}/{id}.png";
        File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.spritePixelsPerUnit = 32;   // 바닥에 떨어졌을 때 한 칸 크기
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>1지역 사냥터 몬스터와 재의 왕은 2단계 장비를 떨어뜨린다. 2~4지역 몬스터는 StageBuilder 가 만든다.</summary>
    static void SetHubDropTiers()
    {
        foreach (string id in new[] { "Enemy1", "Enemy2", "Enemy3", "Enemy4", "AshKing" })
        {
            var so = new SerializedObject(AssetDatabase.LoadAssetAtPath<MonsterData>($"Assets/Game/Data/HubStory/{id}.asset"));
            so.FindProperty("dropTier").intValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

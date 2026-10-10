# 장비·등급·드롭 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 부위별 5단계 장비를 5등급으로 떨어뜨리고, 장착하면 플레이어 능력치에 반영되게 한다.

**Architecture:** 아이템 개체(`InventoryEntry`)가 등급·옵션을 들고, `PlayerInventory` 가 장착 3칸과 능력치 합계(`EquipmentStats`)를 관리한다.
전투 코드는 `PlayerStats` 를 통해 합계를 읽는다. 드롭은 `MonsterBase.Die` 에서 굴린다. 에셋(아이템 14종·아이콘·몬스터 데이터)은 에디터 메뉴 하나로 만든다.

**Tech Stack:** Unity 6000.3.10f1, C#, ScriptableObject, uGUI + TextMeshPro, UnityMCP 브리지(검증 실행).

**Spec:** `docs/superpowers/specs/2026-10-11-equipment-design.md`

## Global Constraints

- 작업 브랜치: 사용자 지시로 `main` 에서 직접 작업한다.
- 주석·`<summary>` 는 한국어, 줄바꿈 CRLF, 커밋 메시지 한국어 + `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- 커밋은 경로를 하나씩 지정 (`git add -A` 금지). Unity 가 바꾼 `ProjectSettings/*`, `packages-lock.json` 등은 커밋하지 않는다.
- 런타임 코드는 `Assets/Game/Scripts/Runtime/`, 에디터 전용은 `Assets/Game/Scripts/Editor/`.
- 월드에 서는 것(드롭 아이템)은 `YSortRenderer` 를 단다.
- 등급 배율 Normal 1.0 / Rare 1.15 / Epic 1.3 / Unique 1.5 / Legendary 1.75. 색 `#9D9D9D` `#3B82F6` `#A855F7` `#FACC15` `#22C55E`.
- 상한: 치명타 확률 0.60, 회피 0.15, 공격속도 2.0. 치명타 기본 피해 1.5.
- 검증은 기존 패턴(`static string Run()` 이 `PASS n checks` / `FAIL ...` 반환, 세이브는 `SaveSystem.VerificationDirectory` 임시 폴더)으로 `EquipmentVerification` 에 쌓는다.
  실행: UnityMCP `execute_code` 로 `return EquipmentVerification.Run();`, 컴파일은 `read_console`.

## Review Focus

1. 기존 세이브(장비 필드 없음, 같은 itemId 가 한 줄)를 읽으면 시작 장비가 채워지고 가방의 낡은 검이 장착돼야 한다 — Task 2 테스트 `LegacySaveGetsStarterGear`.
2. 최대 체력 보너스가 세이브의 `maxHP` 에 섞여 저장되면 불러올 때마다 체력이 불어난다 — Task 3 테스트 `MaxHpBonusNotPersisted`.
3. 장비를 벗어 최대 체력이 줄 때 현재 체력이 최대치를 넘으면 안 된다 — Task 3 테스트 `UnequipClampsHp`.
4. 가방이 가득 찬 상태의 해제·줍기는 아이템을 잃지 않아야 한다 — Task 2 `UnequipFailsWhenBagFull`, Task 4 `PickupStaysWhenBagFull`.
5. 같은 itemId 장비 2개(등급 다름)가 세이브 검증(`Validate`)에서 거부되면 안 된다 — Task 2 `DuplicateEquipmentIdsSurviveSave`.

---

### Task 1: 등급·옵션·능력치 데이터

**Files:**
- Create: `Assets/Game/Scripts/Runtime/Inventory/ItemRarity.cs`
- Create: `Assets/Game/Scripts/Runtime/Inventory/ItemOption.cs`
- Create: `Assets/Game/Scripts/Runtime/Inventory/EquipmentStats.cs`
- Modify: `Assets/Game/Scripts/Runtime/Inventory/ItemDefinition.cs`
- Test: `Assets/Game/Scripts/Editor/EquipmentVerification.cs`

**Interfaces:**
- Produces:
  - `enum ItemRarity { Normal, Rare, Epic, Unique, Legendary }`
  - `static class ItemRarityTable { float Multiplier(ItemRarity); Color Color(ItemRarity); string Label(ItemRarity); ItemRarity RollHunting(System.Random); ItemRarity RollBoss(System.Random); int RollOptionLines(ItemRarity, System.Random); }`
    가중치: 사냥터 65/20/10/4/1, 보스 0/30/40/20/10, 줄 수 표는 스펙 4장.
  - `enum ItemStat { Attack, CritChance, CritDamage, AttackSpeed, MaxHp, Defense, Evasion, HpRegen, MaxMana, ManaRegen, MoveSpeed, ExpGain, LifeSteal, KillHeal, BossDamage, DropRate }`
    퍼센트 옵션은 비율로 저장(3% = 0.03).
  - `[Serializable] struct ItemOption { public ItemStat stat; public float value; }`
  - `static class ItemOptionTable { List<ItemOption> Roll(int tier, ItemRarity, System.Random); string Format(ItemOption); }`
    1·5단계 범위(스펙 4장)를 `Mathf.Lerp(t1, t5, (tier-1)/4f)` 로 보간, 범위 안 균등 → × 배율. 정수 옵션(Attack, MaxHp, Defense, KillHeal, MaxMana)은 반올림. 같은 stat 중복 금지.
  - `ItemDefinition` 추가 필드: `[Range(1,5)] int tier = 1; int attack; float critChance; float attackSpeed = 1f; int maxHp; int defense; float evasion;`
  - `sealed class EquipmentStats { float this[ItemStat] {get;} void Clear(); void AddItem(ItemDefinition def, ItemRarity rarity, IReadOnlyList<ItemOption> options); float CritChance {get;} float Evasion {get;} float AttackSpeed {get;} }`
    `AddItem`: 무기 → Attack += round(attack×m), CritChance += critChance×m, AttackSpeed += (attackSpeed−1)×m. 갑옷 → MaxHp += round(maxHp×m), Defense += round(defense×m), Evasion += evasion×m. 옵션은 그대로 더한다.
    `CritChance`/`Evasion` 은 상한 0.60/0.15 로 자른 값, `AttackSpeed` 는 `Mathf.Min(2f, 1f + this[AttackSpeed])`.

- [ ] **Step 1: 실패하는 검증 작성** — `EquipmentVerification.Run()` 과 `Check(bool, string)` 헬퍼, 첫 묶음 `VerifyTables()`:

```csharp
var rng = new System.Random(1234);
int[] hunt = new int[5]; for (int i = 0; i < 20000; i++) hunt[(int)ItemRarityTable.RollHunting(rng)]++;
Check(Math.Abs(hunt[0] / 20000f - 0.65f) < 0.015f, "사냥터 Normal 65%");
Check(Math.Abs(hunt[4] / 20000f - 0.01f) < 0.004f, "사냥터 Legendary 1%");
int[] boss = new int[5]; for (int i = 0; i < 20000; i++) boss[(int)ItemRarityTable.RollBoss(rng)]++;
Check(boss[0] == 0 && Math.Abs(boss[2] / 20000f - 0.40f) < 0.015f, "보스 Normal 없음, Epic 40%");
for (int i = 0; i < 2000; i++) { int n = ItemRarityTable.RollOptionLines(ItemRarity.Legendary, rng); Check(n >= 2 && n <= 4, "Legendary 2~4줄"); }
for (int i = 0; i < 500; i++) {
    var opts = ItemOptionTable.Roll(5, ItemRarity.Legendary, rng);
    Check(opts.Select(o => o.stat).Distinct().Count() == opts.Count, "옵션 중복 없음");
    foreach (var o in opts.Where(o => o.stat == ItemStat.CritChance)) Check(o.value >= 0.06f * 1.75f - 1e-4f && o.value <= 0.10f * 1.75f + 1e-4f, "5단계 Legendary 치명타 범위");
}
var t1 = ItemOptionTable.Roll(1, ItemRarity.Normal, new System.Random(7)); Check(t1.Count == 1, "Normal 1줄");
// 능력치 합산: 5단계 무기 Legendary + 상한
var sword = ScriptableObject.CreateInstance<ItemDefinition>(); sword.category = ItemCategory.Weapon; sword.attack = 40; sword.critChance = 0.21f; sword.attackSpeed = 1.32f;
var s = new EquipmentStats(); s.AddItem(sword, ItemRarity.Legendary, Array.Empty<ItemOption>());
Check(s[ItemStat.Attack] == 70, "공격력 40×1.75=70");
Check(Mathf.Approximately(s.AttackSpeed, 1.56f), "공격속도 1+0.32×1.75");
s.AddItem(sword, ItemRarity.Legendary, new[] { new ItemOption { stat = ItemStat.CritChance, value = 0.5f } });
Check(Mathf.Approximately(s.CritChance, 0.60f), "치명타 상한 60%");
```

- [ ] **Step 2: 컴파일 실패 확인** — `read_console` (error): `ItemRarityTable` 등 정의 없음.
- [ ] **Step 3: 위 Interfaces 대로 4개 파일 구현.** `Format` 은 툴팁용 한 줄(`"치명타 확률 +7.5%"`, `"공격력 +12"`, `"체력 재생 +1.2/초"`).
- [ ] **Step 4: 검증 통과** — `execute_code`: `return EquipmentVerification.Run();` → `EQUIPMENT_VERIFY_PASS …`.
- [ ] **Step 5: 커밋** — 새 `.cs` 와 `.meta`, `ItemDefinition.cs`. 메시지 `feat: 장비 등급·옵션·능력치 데이터 추가`.

### Task 2: 아이템 개체·장착·저장

**Files:**
- Modify: `Assets/Game/Scripts/Runtime/Inventory/PlayerInventory.cs`
- Modify: `Assets/Game/Scripts/Runtime/Persistence/PlayerSaveData.cs` (`InventorySaveEntry`, `PlayerSaveData`)
- Modify: `Assets/Game/Scripts/Runtime/Persistence/SaveSystem.cs:118-135` (Capture/Validate 에 equipped)
- Move: `Assets/Game/Items/ItemDatabase.asset` → `Assets/Resources/ItemDatabase.asset` (`git mv` + `.meta`), `InventoryUiBuilder.ItemDatabasePath` 갱신
- Modify: `Assets/Game/Scripts/Runtime/Inventory/ItemDatabase.cs` (`static ItemDatabase Load()`)
- Test: `EquipmentVerification.cs` 에 `VerifyInventory()`

**Interfaces:**
- Consumes: Task 1 전부.
- Produces:
  - `InventoryEntry` 추가: `public ItemRarity rarity; public List<ItemOption> options = new();`
  - `InventorySaveEntry` 추가: `public int rarity; public ItemOption[] options;` / `PlayerSaveData` 추가: `public InventorySaveEntry[] equipped;` (길이 3, 인덱스 = `ItemCategory - 1`, 빈 칸은 null 또는 빈 itemId)
  - `ItemDatabase.Load()` — `Resources.Load<ItemDatabase>("ItemDatabase")` 캐시.
  - `PlayerInventory`:
    - `bool TryAddInstance(InventoryEntry entry)` — 겹침 1 아이템은 항상 새 칸. 빈칸 없으면 false.
    - `InventoryEntry GetEquipped(ItemCategory c)`; `bool Equip(int bagSlot)`; `bool Unequip(ItemCategory c)` (가방 가득 차면 false).
    - `EquipmentStats Stats { get; }` — 장착이 바뀔 때마다 다시 계산.
    - `event Action EquipmentChanged` (그리고 기존 `Changed` 도 발생).
    - `static InventoryEntry CreateInstance(ItemDefinition def, ItemRarity r, System.Random rng)` — 장신구면 `ItemOptionTable.Roll(def.tier, r, rng)`.
  - 기존 `TryAdd(ItemDefinition, int)` 은 겹침 1 아이템이면 `TryAddInstance(CreateInstance(def, Normal, …))` 로 위임.
  - 시작 장비(`Load` 끝): 장착 칸이 모두 비어 있고 데이터베이스가 있으면 `cloth_armor`·`old_talisman` Normal 개체를 장착, 가방에 `cave_sword` 가 있으면 그것을 장착.
  - `Validate`: 겹침 1 아이템(`ItemDatabase.Load()?.Find(id)?.maxStack == 1`)은 같은 itemId 중복 허용. `rarity` 범위 0~4 검사.

- [ ] **Step 1: 실패하는 검증 작성** — `VerifyInventory()` (테스트 플레이어 GameObject 에 `PlayerStats`+`PlayerInventory`):
  - `TwoEpicSwordsTakeTwoSlots`: 같은 무쇠검 Rare·Epic 두 개 `TryAddInstance` → `UsedSlotCount == 2`.
  - `EquipSwapsBack`: 무쇠검 장착 → 가방 칸에 원래 낡은 검, `Stats[Attack]` 가 무쇠검 값.
  - `UnequipFailsWhenBagFull`: 가방 15칸 채운 뒤 `Unequip(Weapon) == false`, 장착 유지.
  - `DuplicateEquipmentIdsSurviveSave`: 저장 → 새 인벤토리 `Load` → 등급·옵션·장착이 같음, `Validate` 예외 없음.
  - `LegacySaveGetsStarterGear`: `equipped` 없고 inventory 에 `cave_sword` 한 줄인 `PlayerSaveData` 를 `Load` → 무기 `cave_sword`, 갑옷 `cloth_armor`, 장신구 `old_talisman` 옵션 1줄.
- [ ] **Step 2: 실패 확인** — 컴파일 에러(`TryAddInstance` 없음).
- [ ] **Step 3: 구현.** 저장은 기존처럼 `PlayerStats.Save()` 경유.
- [ ] **Step 4: 검증 통과** — `EquipmentVerification.Run()` PASS, 기존 `InventoryVerification.Run()` 도 PASS.
- [ ] **Step 5: 커밋** — `feat: 장비 개체·장착 칸·저장 추가`.

### Task 3: 능력치를 전투에 반영

**Files:**
- Modify: `Assets/Game/Scripts/Runtime/Player/PlayerStats.cs`
- Modify: `Assets/Game/Scripts/Runtime/Player/Player_Attack.cs` (기본 공격력 6, 쿨다운, 피해 3곳)
- Modify: `Assets/Game/Scripts/Runtime/Player/Player_Controller.cs:76` (이동속도)
- Modify: `Assets/Game/Scripts/Runtime/Progression/RpgSkillController.cs:70,87,107`
- Modify: `Assets/Game/Scripts/Runtime/Progression/FlyingSwordUltimate.cs:106`
- Modify: `Assets/Game/Scripts/Runtime/Persistence/SaveSystem.cs:123` (`maxHP = stats.BaseMaxHP`, `maxMana = stats.BaseMaxMana`)
- Modify: 프리팹·씬의 `MaxHP`/`MaxMana` 직렬화 이름 — `[FormerlySerializedAs("MaxHP")]` 로 보존
- Test: `EquipmentVerification.cs` 에 `VerifyCombat()`

**Interfaces:**
- Consumes: `PlayerInventory.Stats`, `EquipmentChanged`.
- Produces (`PlayerStats`):
  - `float BaseMaxHP` (직렬화, 옛 `MaxHP` 필드), `float MaxHP => BaseMaxHP + gear[MaxHp]`. 같은 방식으로 `BaseMaxMana`/`MaxMana`. 레벨업·능력치 투자·Load 는 Base 를 바꾼다.
  - `AttackBonus` 에 `gear[Attack]` 포함, `Defense` 에 `gear[Defense]` 포함, `ManaRegen` 에 `gear[ManaRegen]` 포함.
  - `float AttackSpeed => gear.AttackSpeed` (장비 없으면 1).
  - `int DealDamage(IDamageable target, int baseDamage)` — 치명타(`gear.CritChance`, 피해 `1.5 + gear[CritDamage]`), 보스(`target` 의 GameObject 에 `StageBoss` 또는 `AshKingBoss`)면 `× (1 + gear[BossDamage])`, `target.TakeDamage(dmg)`, 흡혈 `Heal(dmg × gear[LifeSteal])`. 준 피해를 반환. 난수는 `UnityEngine.Random`.
  - `TakeDamage`: 회피 `Random.value < gear.Evasion` 이면 무시.
  - `Update`: `Heal(gear[HpRegen] × dt)`.
  - `AddEXP`/`ApplyExperience`: `amount × (1 + gear[ExpGain])`.
  - `void OnKill()` — `Heal(gear[KillHeal])`. `MonsterBase.Die` 에서 `targetStats.OnKill()` 호출.
  - 장착 변경 시 `HP = Min(HP, MaxHP)`, `Mana = Min(Mana, MaxMana)`, `OnStatsChanged`.
- `Player_Attack`: `attackDamage` 기본값 6(프리팹·씬 값도 6 으로), 쿨다운 비교는 `attackCooldown / stats.AttackSpeed`, `dashCooldown / stats.AttackSpeed`. `damageable.TakeDamage(x)` 3곳 → `stats.DealDamage(damageable, x)`.
- `RpgSkillController`: `readyAt[id] = Time.time + skill.Cooldown / stats.AttackSpeed`, 강타 피해는 `stats.DealDamage(monster, …)`. `FlyingSwordUltimate` 의 `sword.target.TakeDamage(damage)` 도 `DealDamage` 경유.
- `Player_Controller`: 속도 × `(1 + gear[MoveSpeed])`.

- [ ] **Step 1: 실패하는 검증 작성** — `VerifyCombat()`:
  - `MaxHpBonusNotPersisted`: 천갑옷 Legendary 장착 → 저장 → 새로 Load → `BaseMaxHP` 가 장착 전과 같고 `MaxHP == Base + round(10×1.75)`.
  - `UnequipClampsHp`: 체력 가득 → 갑옷 해제 → `HP == MaxHP`.
  - `CritDoublesDamage`: 치명타 옵션으로 확률 0.60 고정 + `Random.InitState(1)`, 테스트용 `IDamageable` 에 1000번 `DealDamage(t, 10)` → 평균 피해가 `10×(0.4 + 0.6×1.5)=13` ±0.5.
  - `EvasionCapped`: 회피 옵션 합이 0.5 여도 1000번 `TakeDamage(10)` 중 무시 비율 ≤ 0.18.
  - `AttackSpeedShortensCooldown`: 공격속도 1.25 → `RpgSkillController` 사용 후 `Remaining(0)` ≈ `Cooldown / 1.25`.
  - `BaseAttackUnchanged`: 낡은 검 Normal 장착 시 `AttackPower == 10` (레벨 1, 투자 0).
- [ ] **Step 2: 실패 확인.**
- [ ] **Step 3: 구현.** `MaxHP`/`MaxMana` 를 쓰는 에디터 코드(`grep -rn "\.MaxHP\s*=" Assets/Game/Scripts/Editor`)는 `BaseMaxHP` 로 바꾼다.
- [ ] **Step 4: 검증 통과** — `EquipmentVerification`, `RpgVerification`, `RecoverySaveVerification`, `InventoryVerification` 모두 PASS.
- [ ] **Step 5: 커밋** — `feat: 장비 능력치를 공격·피격·쿨타임·이동에 반영`.

### Task 4: 몬스터 드롭과 줍기

**Files:**
- Modify: `Assets/Game/Scripts/Runtime/Enemies/MonsterData.cs` (`[SerializeField, Range(0,5)] int dropTier; public int DropTier`)
- Create: `Assets/Game/Scripts/Runtime/Inventory/ItemDrop.cs`
- Modify: `Assets/Game/Scripts/Runtime/Enemies/MonsterBase.cs:118-140` (Die)
- Modify: `Assets/Game/Scripts/Runtime/Inventory/ItemPickup.cs`
- Test: `EquipmentVerification.cs` 에 `VerifyDrops()`

**Interfaces:**
- Consumes: `PlayerInventory.CreateInstance`, `TryAddInstance`, `Equip`, `ItemDatabase.Load()`, `PlayerStats.OnKill`.
- Produces:
  - `static class ItemDrop { const float HuntingChance = 0.01f; InventoryEntry Roll(int tier, bool boss, float dropRateBonus, ItemDatabase db, System.Random rng); ItemPickup Spawn(InventoryEntry entry, Vector3 position); }`
    `Roll`: 보스가 아니면 `rng.NextDouble() < 0.01 × (1 + bonus)` 아닐 때 null. 부위 1/3 균등, `db` 에서 `tier`·부위가 맞는 정의를 찾고, 등급은 `RollBoss`/`RollHunting`.
    `Spawn`: GameObject(`SpriteRenderer` 아이콘, `CircleCollider2D` trigger r=0.4, `YSortRenderer`, 등급색 TMP 이름표 `TextMeshPro` 월드 텍스트, `ItemPickup.Init(entry)`).
  - `ItemPickup.Init(InventoryEntry instance)` — 개체가 있으면 `TryAddInstance`. 가방이 가득 차면 남는다. 줍고 나서 같은 부위 장착 칸이 비었으면 자동 장착(낡은 검 규칙).
  - `MonsterBase.Die`: `DropTier > 0` 이면 `ItemDrop.Roll(DropTier, isBoss, gear[DropRate], …)` → `Spawn`. `isBoss = GetComponent<StageBoss>() != null || this is AshKingBoss`.
- [ ] **Step 1: 실패하는 검증 작성** — `VerifyDrops()`:
  - `HuntingRate`: `Roll(2,false,0,db,rng)` 100,000번 → non-null 비율 0.01 ± 0.002, 모두 tier 2.
  - `DropRateBonus`: bonus 1.0 → 0.02 ± 0.003.
  - `BossAlwaysDrops`: `Roll(3,true,…)` 1000번 모두 non-null, Normal 없음.
  - `PickupStaysWhenBagFull`: 가방 가득 + `Spawn` 후 플레이어 콜라이더로 `OnTriggerEnter2D` → 오브젝트 남음, 인벤토리 그대로.
  - `PickupAutoEquipsEmptySlot`: 무기 칸 비운 상태에서 낡은 검 줍기 → 장착됨.
- [ ] **Step 2: 실패 확인.**
- [ ] **Step 3: 구현.**
- [ ] **Step 4: 검증 통과** — `EquipmentVerification.Run()` PASS.
- [ ] **Step 5: 커밋** — `feat: 몬스터 장비 드롭과 줍기 추가`.

### Task 5: 인벤토리 UI — 등급색·장착 조작·툴팁

**Files:**
- Modify: `Assets/Game/Scripts/Runtime/Presentation/Inventory/InventoryWindow.cs`
- Modify: `Assets/Game/Scripts/Runtime/Presentation/Inventory/InventorySlotView.cs` (`IPointerClickHandler`, 테두리색)
- Modify: `Assets/Game/Scripts/Runtime/Presentation/Inventory/EquipmentSlotView.cs` (`ShowItem`, `IPointerClickHandler`, `IDropHandler`, 툴팁)
- Modify: `Assets/Game/Scripts/Runtime/Presentation/Inventory/ItemTooltip.cs`
- Test: `EquipmentVerification.cs` 에 `VerifyWindow()`

**Interfaces:**
- Consumes: `PlayerInventory.Equip/Unequip/GetEquipped`, `ItemRarityTable.Color/Label`, `ItemOptionTable.Format`.
- Produces:
  - 칸 테두리: 칸 루트 `Image`(slotFrame) 의 `color` 를 등급색으로, 빈 칸은 흰색. 프리팹 재생성 없이 런타임에서만 바꾼다.
  - `EquipmentSlotView`: 창은 `GetComponentInParent<InventoryWindow>()`. `ShowItem(Sprite icon, ItemRarity r)`. 우클릭 → `window.UnequipFrom(this)`, 드롭 → `window.DropItemOnEquipment(this)`, 호버 → 툴팁.
  - `InventoryWindow`: `OnSlotRightClick(InventorySlotView)` → `inventory.Equip`. `UnequipFrom(EquipmentSlotView)`, `DropItemOnEquipment(EquipmentSlotView)` (부위가 맞을 때만 Equip).
  - `ItemTooltip.Show(InventoryEntry entry, ItemDefinition def, InventoryEntry equippedSameSlot, RectTransform slot, RectTransform window)`:
    이름 `color = 등급색`, 종류 줄 `"Epic · 3단계 무기"`, 설명 칸 위에 능력치 줄(rich text), 장착품과 다르면 줄 끝에 `<color=#7CFC9A>▲3</color>`/`<color=#FF7A7A>▼2</color>`, 장신구 옵션 줄. 수량 줄은 겹침 1 이면 숨긴다.
- [ ] **Step 1: 실패하는 검증 작성** — `VerifyWindow()` (기존 `InventoryVerification.VerifyWindow` 처럼 프리팹 인스턴스 사용):
  - 우클릭 이벤트(`ExecuteEvents.Execute(slot, new PointerEventData{button=Right}, pointerClickHandler)`) → 무기 장착, 장비 칸 아이콘 표시.
  - 장비 칸 우클릭 → 해제, 가방에 돌아옴.
  - Epic 칸 테두리 `color == ItemRarityTable.Color(Epic)`.
  - 툴팁 이름 색 = 등급색, 본문에 `"▲"` 포함(더 좋은 무기에 호버).
  - 화면 캡처 `VerificationResults/Equipment/window.png` 저장(사람 확인용).
- [ ] **Step 2: 실패 확인.**
- [ ] **Step 3: 구현.**
- [ ] **Step 4: 검증 통과** — `EquipmentVerification.Run()` + `InventoryVerification.Run()` PASS, 캡처 확인.
- [ ] **Step 5: 커밋** — `feat: 인벤토리 등급색·장착 조작·장비 툴팁`.

### Task 6: 에셋 생성 — 아이템 14종·아이콘·몬스터 데이터·씬 연결

**Files:**
- Create: `Assets/Game/Scripts/Editor/EquipmentAssetBuilder.cs` (`[MenuItem("Tools/AINPC/Equipment/Build")]`)
- 생성: `Assets/Game/Items/Equipment/*.asset` 14종, `Assets/Sprites/UI/Items/*.png` 15장, `Assets/Data/Monsters/Region{2,3,4}/*.asset`
- Modify: `Assets/Game/Items/CaveSword.asset` (tier 1, attack 4, crit 0.03, speed 1.0), `Assets/Game/Data/HubStory/Enemy1~4.asset`·`AshKing.asset` (dropTier 2), `Assets/Data/Monsters/Bosses/Region{2,3,4}Boss.asset`·`FinalBoss.asset` (스펙 6장 값, dropTier 3/4/5/0)
- Modify: `Assets/Scenes/Region2.unity`, `Region3.unity`, `Region4.unity` (공용 몬스터 데이터 참조 → 지역 데이터)
- Test: `EquipmentVerification.cs` 에 `VerifyAssets()`

**Interfaces:**
- Consumes: Task 1 `ItemDefinition` 필드, Task 4 `MonsterData.dropTier`, `InventoryUiBuilder.CreateItemDatabase` 로직(데이터베이스 다시 채우기).
- Produces: itemId — 무기 `cave_sword, iron_sword, steel_sword, blacksteel_sword, heavenfire_sword` / 갑옷 `cloth_armor, leather_armor, chain_armor, plate_armor, dragonscale_armor` / 장신구 `old_talisman, jade_norigae, silver_bracelet, diamond_beads, heaven_talisman`.
  이름·수치는 스펙 1·3장. `maxStack = 1`. 설명은 한 줄 분위기 문장.
  - 아이콘: 32×32 도트, `DrawPixelArt` 와 같은 문자열 그리드 방식, 부위별 모양 3개 × 단계별 팔레트 5개. 임포트는 Sprite·Point·압축 없음(CaveSword.png 설정과 같음).
  - 지역 몬스터: `Region{N}_{Mushroom,Goblin,Bat,Skeleton}.asset` 은 공용 데이터를 복제해 레벨·체력·피해·EXP 를 스펙 6장 값(사냥터 1~4 = 버섯·고블린·박쥐·해골 순), `dropTier = N+1`. 이름(`monsterName`)은 원본 유지(퀘스트 보고 호환).
  - 씬 연결: 씬을 열기 전 사용자에게 저장 안 된 변경 확인. 각 RegionN 씬의 `MonsterSpawnPoint`/`MonsterSpawnArea` 에서 공용 데이터 참조를 지역 데이터로 바꾸고 저장. 실행 후 원래 열려 있던 씬을 다시 연다.
- [ ] **Step 1: 실패하는 검증 작성** — `VerifyAssets()`:
  - `ItemDatabase.Load()` 에 15종, 부위×단계 조합이 모두 하나씩, 모두 아이콘 있음.
  - `iron_sword` attack 9 / crit 0.07 / speed 1.08, `dragonscale_armor` hp 150 / def 29 / evasion 0.05.
  - `Region3_Bat` maxHP 445 / damage 55 / dropTier 4. `Region4Boss` maxHP 31000 / damage 178. `FinalBoss.dropTier == 0`. `Enemy1.dropTier == 2`.
  - Region2 씬 파일 텍스트에 공용 `MushroomData` guid 가 더 이상 없음.
- [ ] **Step 2: 실패 확인.**
- [ ] **Step 3: 빌더 구현 후 메뉴 실행** (씬 확인 후).
- [ ] **Step 4: 검증 통과** — `EquipmentVerification.Run()` 전체 PASS. 아이콘 15장을 이미지로 열어 육안 확인.
- [ ] **Step 5: 커밋** — 빌더, 생성 에셋과 `.meta`, 수정 에셋, 세 씬. 메시지 `feat: 장비 14종·아이콘·2~4지역 몬스터 데이터 생성`.

### Task 7: 플레이 확인과 문서

**Files:**
- Modify: `docs/HUNTING_BALANCE.md` (2~4지역 표, 드롭 규칙 한 단락)
- Test: 플레이 모드 확인

- [ ] **Step 1: 플레이 모드 확인** — Main 씬 플레이, 진입 직후 `SaveSystem.VerificationDirectory` 를 임시 폴더로. `execute_code` 로 플레이어에 Epic 무쇠검 개체 추가 → `I` 창 열어 우클릭 장착 → `AttackPower`·HUD 확인, 몬스터 1마리에 `DealDamage` 로 처치 → `dropTier` 2 보스 경로로 강제 드롭(`ItemDrop.Spawn`) 후 줍기. 결과를 `VerificationResults/Equipment/play.txt` 에.
- [ ] **Step 2: 기존 검증 회귀** — `RpgVerification`, `RecoverySaveVerification`, `HuntingBalanceVerification.Verify`, `InventoryVerification` 실행. 1지역 처치 시간이 바뀌었으면(기본 공격력 6 + 낡은 검 4 = 10 이어야 동일) 원인 확인.
- [ ] **Step 3: 문서 갱신 후 커밋** — `docs: 2~4지역 몬스터 수치와 장비 드롭 규칙 기록`.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 현재 열려 있는 씬에 1섬 퀘스트 시스템을 배치한다.
/// 메뉴: AINPC / 퀘스트 / 현재 씬에 퀘스트 시스템 배치
///
/// 이미 있는 오브젝트는 건너뛰므로 여러 번 눌러도 안전하다.
/// 트리거는 플레이어(없으면 원점) 주변에 격자로 깔리니, 배치 후
/// 하이어라키에서 Isle1_Triggers 를 펼쳐 맵의 알맞은 자리로 옮기면 된다.
/// 씬 저장은 직접 Ctrl+S.
/// </summary>
public static class QuestSceneSetup
{
    private const string TriggerRootName = "Isle1_Triggers";
    private const string NpcRootName = "Isle1_NPCs";
    private const string SystemRootName = "QuestSystem";

    [MenuItem("AINPC/퀘스트/현재 씬에 퀘스트 시스템 배치")]
    public static void Setup()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || (scene.rootCount == 0 && string.IsNullOrEmpty(scene.path)))
        {
            EditorUtility.DisplayDialog("퀘스트 배치", "먼저 배치할 씬을 열어주세요.", "확인");
            return;
        }

        int created = 0;

        created += SetupSystem() ? 1 : 0;
        created += SetupTriggers();
        created += SetupNpcs();

        EditorSceneManager.MarkSceneDirty(scene);

        ReportMissingDependencies();
        Debug.Log($"[Quest] '{scene.name}' 씬에 배치 완료 — 새 오브젝트 {created}개. 저장하려면 Ctrl+S.");
    }

    // ── 1. 매니저 ───────────────────────────────────────────────

    private static bool SetupSystem()
    {
        if (GameObject.Find(SystemRootName) != null) return false;

        var go = new GameObject(SystemRootName);
        Undo.RegisterCreatedObjectUndo(go, "퀘스트 시스템 배치");

        go.AddComponent<QuestManager>();
        var ui = go.AddComponent<QuestLogUI>();

        // 한글 폰트가 프로젝트에 있으면 자동 연결 (없으면 TMP 기본 폰트)
        var font = FindKoreanFont();
        if (font != null)
        {
            var so = new SerializedObject(ui);
            so.FindProperty("koreanFont").objectReferenceValue = font;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        return true;
    }

    private static TMPro.TMP_FontAsset FindKoreanFont()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/TextMesh Pro")) continue;   // 기본 영문 폰트는 건너뛴다
            var f = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(path);
            if (f != null) return f;
        }
        return null;
    }

    // ── 2. 트리거 존 ────────────────────────────────────────────

    private static int SetupTriggers()
    {
        var root = GameObject.Find(TriggerRootName);
        if (root == null)
        {
            root = new GameObject(TriggerRootName);
            Undo.RegisterCreatedObjectUndo(root, "퀘스트 트리거 루트");
        }

        Vector3 origin = FindPlayerPosition();
        int made = 0;
        int i = 0;

        // (이름, action, questId, targetId, 파괴여부)
        made += MakeTrigger(root, ref i, origin, "Shore_상륙지점",
            QuestTriggerZone.ZoneAction.ReportReach, "", "isle1_shore", false);
        made += MakeTrigger(root, ref i, origin, "BurntHouse_불탄집터",
            QuestTriggerZone.ZoneAction.ReportReach, "", "isle1_burnt_house", false);
        made += MakeTrigger(root, ref i, origin, "Warehouse_창고입구",
            QuestTriggerZone.ZoneAction.ReportReach, "", "isle1_warehouse", false);
        made += MakeTrigger(root, ref i, origin, "Choice_아이를구한다",
            QuestTriggerZone.ZoneAction.ReportChoice, "", "isle1_girl_saved", false);
        made += MakeTrigger(root, ref i, origin, "Castle_재의왕성",
            QuestTriggerZone.ZoneAction.ReportReach, "", "isle1_castle", false);

        made += MakeTrigger(root, ref i, origin, "Fragment_기억파편1",
            QuestTriggerZone.ZoneAction.ReportCollect, "", "isle1_fragment", true);
        made += MakeTrigger(root, ref i, origin, "Fragment_기억파편2",
            QuestTriggerZone.ZoneAction.ReportCollect, "", "isle1_fragment", true);
        made += MakeTrigger(root, ref i, origin, "Fragment_기억파편3",
            QuestTriggerZone.ZoneAction.ReportCollect, "", "isle1_fragment", true);

        made += MakeTrigger(root, ref i, origin, "Collect_스승의검",
            QuestTriggerZone.ZoneAction.ReportCollect, "", "isle1_master_sword", true);
        made += MakeTrigger(root, ref i, origin, "Choice_검을건넨다",
            QuestTriggerZone.ZoneAction.ReportChoice, "", "isle1_sword_returned", false);
        made += MakeTrigger(root, ref i, origin, "Choice_인격에게맡긴다",
            QuestTriggerZone.ZoneAction.ReportChoice, "", "isle1_demand_accepted", false);

        return made;
    }

    private static int MakeTrigger(GameObject root, ref int index, Vector3 origin,
                                   string name, QuestTriggerZone.ZoneAction action,
                                   string questId, string targetId, bool destroyOnTrigger)
    {
        // 격자 배치: 한 줄에 4개, 간격 4유닛
        int col = index % 4;
        int row = index / 4;
        Vector3 pos = origin + new Vector3(col * 4f - 6f, -row * 4f - 4f, 0f);
        index++;

        var existing = root.transform.Find(name);
        if (existing != null) return 0;

        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.position = pos;
        Undo.RegisterCreatedObjectUndo(go, "퀘스트 트리거 생성");

        var col2d = go.AddComponent<BoxCollider2D>();
        col2d.isTrigger = true;
        col2d.size = new Vector2(2f, 2f);

        var zone = go.AddComponent<QuestTriggerZone>();
        var so = new SerializedObject(zone);
        so.FindProperty("action").enumValueIndex = (int)action;
        so.FindProperty("questId").stringValue = questId;
        so.FindProperty("targetId").stringValue = targetId;
        so.FindProperty("destroyOnTrigger").boolValue = destroyOnTrigger;
        so.ApplyModifiedPropertiesWithoutUndo();

        return 1;
    }

    // ── 3. NPC ─────────────────────────────────────────────────

    private static int SetupNpcs()
    {
        var root = GameObject.Find(NpcRootName);
        if (root == null)
        {
            root = new GameObject(NpcRootName);
            Undo.RegisterCreatedObjectUndo(root, "퀘스트 NPC 루트");
        }

        Vector3 origin = FindPlayerPosition();
        int made = 0;

        made += MakeNpc(root, origin + new Vector3(-8f, 2f, 0f), "촌장",
            "너는 잿더미 섬 어촌의 촌장이다. 마을 절반이 불에 탔지만 너는 그런 일이 없었다고 믿는다. " +
            "불에 대해 물으면 무슨 소리냐는 듯 시치미를 뗀다. 짧고 무뚝뚝한 한국어로 대답해.",
            "isle1_village", "isle1_village",
            "지금 이방인이 섬 사정을 캐묻고 있다. 불탄 집터 이야기는 피해라.",
            "이방인이 창고 쪽 일을 겪은 뒤다. 너는 여전히 불은 없었다고 말하지만 목소리가 흔들린다.");

        made += MakeNpc(root, origin + new Vector3(8f, 2f, 0f), "하연",
            "너는 30년 전 천마의 습격 때 죽은 무인의 제자다. 스승을 지키지 못한 자를 원망하고 있다. " +
            "말수가 적고 날이 서 있다. 짧은 한국어로 대답해.",
            "", "isle1_bereaved",
            "눈앞의 노인이 스승을 죽게 만든 그 사람임을 너는 알고 있다. 차갑게 대하라.",
            "그가 스승의 검을 돌려줬다. 원망은 남았지만 검은 받았다. 조금 누그러진 태도로 대하라.");

        return made;
    }

    private static int MakeNpc(GameObject root, Vector3 pos, string npcName, string personality,
                               string offerQuestId, string duringQuestId,
                               string duringLine, string afterLine)
    {
        if (root.transform.Find(npcName) != null) return 0;

        var go = new GameObject(npcName);
        go.transform.SetParent(root.transform, false);
        go.transform.position = pos;
        Undo.RegisterCreatedObjectUndo(go, "퀘스트 NPC 생성");

        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 1.5f;

        var npc = go.AddComponent<NPCInteraction>();
        npc.npcName = npcName;
        npc.personality = personality;

        var giver = go.AddComponent<QuestGiver>();
        var so = new SerializedObject(giver);
        so.FindProperty("talkId").stringValue = npcName;
        so.FindProperty("offerQuestId").stringValue = offerQuestId;
        so.FindProperty("duringQuestId").stringValue = duringQuestId;
        so.FindProperty("duringQuestPersonality").stringValue = duringLine;
        so.FindProperty("afterQuestPersonality").stringValue = afterLine;
        so.ApplyModifiedPropertiesWithoutUndo();

        return 1;
    }

    // ── 보조 ───────────────────────────────────────────────────

    private static Vector3 FindPlayerPosition()
    {
        var player = Object.FindFirstObjectByType<Player_Controller>();
        if (player != null) return player.transform.position;

        var cam = Camera.main;
        if (cam != null) return new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);

        return Vector3.zero;
    }

    /// <summary>퀘스트가 제 기능을 하려면 씬에 있어야 하는 것들을 점검한다.</summary>
    private static void ReportMissingDependencies()
    {
        if (Object.FindFirstObjectByType<MoodSystem>() == null)
            Debug.LogWarning("[Quest] 씬에 MoodSystem 이 없습니다 — 퀘스트의 moodDelta 가 아무 일도 하지 않습니다.");

        if (Object.FindFirstObjectByType<MemoryManager>() == null)
            Debug.LogWarning("[Quest] 씬에 MemoryManager 가 없습니다 — memoryLine 이 기억에 저장되지 않습니다.");

        if (Object.FindFirstObjectByType<InnerVoiceManager>() == null)
            Debug.LogWarning("[Quest] 씬에 InnerVoiceManager 가 없습니다 — triggerInnerVoice 가 동작하지 않습니다.");

        if (Object.FindFirstObjectByType<Player_Controller>() == null)
            Debug.LogWarning("[Quest] 씬에 Player_Controller 가 없습니다 — 트리거가 플레이어를 감지하지 못합니다.");

        if (AssetDatabase.FindAssets("t:QuestData").Length == 0)
            Debug.LogWarning("[Quest] QuestData 에셋이 없습니다 — 메뉴 'AINPC/퀘스트/1섬 퀘스트 생성' 을 먼저 실행하세요.");
    }
}

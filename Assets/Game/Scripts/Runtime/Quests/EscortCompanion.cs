using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 2~4지역 하연의 조사 의뢰(r{N}_hayeon) 동안 하연이 주인공을 따라다니는 비전투 동행.
/// 적이 가까이 붙으면 체력이 깎이고, 쓰러지면 의뢰가 실패한 채 마을 자리로 돌아가 회복한다(영구 사망 없음).
/// 어둠은 이때 "동료를 또 잃을지 모른다"는 두려움을 느끼고(EmotionSystem), 수호형이면 하연 곁의 적부터 벤다(AIController).
/// 씬을 고치지 않도록 씬이 열릴 때 하연(HubStoryActor)에게 스스로 붙는다.
/// </summary>
public class EscortCompanion : MonoBehaviour
{
    /// <summary>지금 호위 중인 동행. 없으면 null.</summary>
    public static EscortCompanion Current { get; private set; }

    public const float MaxHp = 100f;
    const float FollowDistance = 1.5f, TeleportDistance = 12f, SpeedScale = 0.9f;
    const float ThreatRadius = 1.2f, DamagePerEnemy = 6f, TickSeconds = 1f;
    const int MaxEnemiesPerTick = 2;
    public const string DownedMemory = "하연이 내 곁에서 쓰러졌다. 또 지키지 못했다.";

    public string questId;
    public float Hp { get; private set; } = MaxHp;

    Vector3 home;
    PlayerStats player;
    Player_Controller playerMove;
    EmotionSystem emotions;
    ControlManager control;
    float nextTick;
    bool lowHpFearGiven;
    Transform bar;
    SpriteRenderer barFill;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= Attach;
        SceneManager.sceneLoaded += Attach;
        Attach(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    /// <summary>Stage2~4 의 하연에게 붙인다. 의뢰 id 는 RegionNpcSetup.HayeonQuest 와 같은 꼴이다.</summary>
    static void Attach(Scene scene, LoadSceneMode _)
    {
        if (!scene.name.StartsWith("Stage") || !int.TryParse(scene.name.Substring(5), out int region) || region < 2) return;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var actor in root.GetComponentsInChildren<HubStoryActor>(true))
            if (actor.actorId == HubStoryIds.Hayeon && actor.GetComponent<EscortCompanion>() == null)
                actor.gameObject.AddComponent<EscortCompanion>().questId = $"r{region}_hayeon";
    }

    void Start()
    {
        home = transform.position;
        BuildBar();
    }

    void OnEnable()
    {
        if (QuestManager.Instance != null) QuestManager.Instance.OnQuestCompleted += HandleCompleted;
    }

    void OnDisable()
    {
        if (QuestManager.Instance != null) QuestManager.Instance.OnQuestCompleted -= HandleCompleted;
        if (Current == this) Current = null;
    }

    bool Escorting => QuestManager.Instance != null && QuestManager.Instance.GetState(questId) == QuestState.Active;

    void Update()
    {
        bool escorting = Escorting;
        if (escorting && Current != this) Begin();
        else if (!escorting && Current == this) GoHome();
        if (bar != null) bar.gameObject.SetActive(Current == this);
        if (Current != this) return;

        if (player == null) player = FindFirstObjectByType<PlayerStats>();
        if (player == null) return;
        Follow();
        if (Time.time >= nextTick)
        {
            nextTick = Time.time + TickSeconds;
            TakeThreatDamage();
        }
    }

    void Begin()
    {
        Current = this;
        Hp = MaxHp;
        lowHpFearGiven = false;
        nextTick = Time.time + TickSeconds;
        player = FindFirstObjectByType<PlayerStats>();
        playerMove = player != null ? player.GetComponent<Player_Controller>() : null;
        emotions = player != null ? player.GetComponent<EmotionSystem>() : null;
        control = player != null ? player.GetComponent<ControlManager>() : null;
        UpdateBar();
    }

    void GoHome()
    {
        Current = null;
        transform.position = home;
        Hp = MaxHp;
    }

    // shortcut: 벽 충돌 없이 걷고 멀어지면 순간이동한다. 사냥터 지형이 막히는 곳이 생기면 길찾기로 바꾼다.
    void Follow()
    {
        Vector3 target = player.transform.position;
        float distance = Vector2.Distance(transform.position, target);
        if (distance > TeleportDistance)
        {
            transform.position = target + (Vector3)(Random.insideUnitCircle.normalized * FollowDistance);
            return;
        }
        if (distance <= FollowDistance) return;
        float speed = (playerMove != null ? playerMove.MoveSpeed : 5f) * SpeedScale;
        transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
    }

    /// <summary>반경 안 적 수(최대 2)만큼 피해. 어둠의 두려움이 오른다.</summary>
    void TakeThreatDamage()
    {
        int enemies = 0;
        foreach (var c in Physics2D.OverlapCircleAll(transform.position, ThreatRadius))
        {
            var m = c.GetComponentInParent<MonsterBase>();
            if (m != null && !m.IsDead && ++enemies >= MaxEnemiesPerTick) break;
        }
        if (enemies == 0) return;

        Hp = Mathf.Max(0f, Hp - DamagePerEnemy * enemies);
        UpdateBar();
        emotions?.Apply(EmotionRules.EscortHit, "escort_hit");
        if (!lowHpFearGiven && Hp > 0f && Hp <= MaxHp * 0.3f)
        {
            lowHpFearGiven = true;
            emotions?.Apply(EmotionRules.EscortLowHp, "escort_low_hp");
        }
        if (Hp <= 0f) Downed();
    }

    void Downed()
    {
        bool playerControlling = control == null || control.IsPlayerControlled;
        emotions?.Apply(EmotionRules.EscortDowned(playerControlling), "escort_downed");
        _ = FindFirstObjectByType<MemoryManager>()?.Remember(DownedMemory, "inner");
        GameFlow.Instance?.ShowNotice("하연이 쓰러졌다. 마을로 옮겨졌다.", 3f);
        GoHome();
        QuestManager.Instance?.FailQuest(questId);
    }

    void HandleCompleted(QuestRuntime r)
    {
        if (r?.Data == null || r.Data.questId != questId) return;
        bool playerControlling = control == null || control.IsPlayerControlled;
        emotions?.Apply(EmotionRules.EscortCompleted(playerControlling), "escort_completed");
    }

    // ── 머리 위 체력 막대 ──

    void BuildBar()
    {
        var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0f, 0.5f), 4f);
        bar = new GameObject("EscortHpBar").transform;
        bar.SetParent(transform, false);
        bar.localPosition = new Vector3(-0.5f, 1.4f, 0f);
        var bg = new GameObject("Bg").AddComponent<SpriteRenderer>();
        bg.transform.SetParent(bar, false);
        bg.sprite = sprite; bg.color = new Color(0f, 0f, 0f, 0.6f);
        bg.transform.localScale = new Vector3(1f, 0.12f, 1f);
        bg.sortingOrder = YSortRenderer.WorldOverlayOrder;
        barFill = new GameObject("Fill").AddComponent<SpriteRenderer>();
        barFill.transform.SetParent(bar, false);
        barFill.sprite = sprite; barFill.color = new Color(0.95f, 0.45f, 0.45f);
        barFill.sortingOrder = YSortRenderer.WorldOverlayOrder + 1;
        UpdateBar();
        bar.gameObject.SetActive(false);
    }

    void UpdateBar()
    {
        if (barFill != null) barFill.transform.localScale = new Vector3(Hp / MaxHp, 0.1f, 1f);
    }
}

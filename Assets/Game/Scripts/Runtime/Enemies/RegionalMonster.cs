using System.Collections.Generic;
using UnityEngine;

/// <summary>사냥터 전용 예고 공격. 기존 몬스터의 피해·경험치·퀘스트 보고를 재사용한다.</summary>
public sealed class RegionalMonster : MonsterBase
{
    public enum Style { Spores, Volley, Cleave, Rush }
    public enum Phase { Walking, Windup, Striking, Recovering }
    [SerializeField] Style style;
    [SerializeField, Min(.2f)] float windup = .8f;
    [SerializeField, Min(.1f)] float recoveryDuration = .55f;
    [SerializeField, Min(1)] float leashDistance = 12;
    public Style AttackStyle => style;
    public Phase CurrentPhase { get; private set; }
    public int AttackCount { get; private set; }
    public bool TelegraphVisible => warning != null && warning.gameObject.activeSelf;
    Rigidbody2D body;
    SpriteRenderer renderer;
    PlayerStats player;
    Vector2 home, direction, lockedOrigin, lockedDirection;
    float due, readyAt, rushRemaining;
    bool rushHit;
    LineRenderer warning;
    Material material;
    readonly List<RegionalProjectile> projectiles = new List<RegionalProjectile>();
    readonly RaycastHit2D[] hits = new RaycastHit2D[16];

    protected override void Awake()
    {
        base.Awake(); body = GetComponent<Rigidbody2D>(); renderer = GetComponent<SpriteRenderer>();
    }
    public override void Initialize(MonsterData data, MonsterSpawnPoint ownerPoint)
    {
        base.Initialize(data, ownerPoint); home = transform.position; CurrentPhase = Phase.Walking;
        readyAt = Time.time + .7f; AttackCount = 0; player = FindFirstObjectByType<PlayerStats>();
    }
    protected override void Update()
    {
        if (IsDead || Data == null || Time.timeScale == 0) return;
        projectiles.RemoveAll(p => p == null);
        if (player == null) player = FindFirstObjectByType<PlayerStats>();
        if (player == null || !player.IsAlive) { ResetAttack(); direction = Vector2.zero; return; }
        Vector2 delta = (Vector2)player.transform.position - body.position;
        if (Vector2.Distance(home, body.position) > leashDistance || Vector2.Distance(home, player.transform.position) > leashDistance + 3)
        {
            ResetAttack(); direction = Vector2.Distance(body.position, home) > .3f ? (home - body.position).normalized : Vector2.zero; return;
        }
        if (CurrentPhase == Phase.Windup)
        {
            direction = Vector2.zero;
            if (Time.time >= due) Strike(); return;
        }
        if (CurrentPhase == Phase.Striking) return;
        if (CurrentPhase == Phase.Recovering)
        {
            direction = Vector2.zero;
            if (Time.time >= due) CurrentPhase = Phase.Walking; return;
        }
        if (delta.magnitude > Data.AggroRange) { direction = Vector2.zero; return; }
        if (Time.time >= readyAt && delta.magnitude <= Data.AttackRange) { BeginAttack(delta); return; }
        // 강가의 박쥐는 근접 플레이어에게서 거리를 벌리며 다음 사격을 준비한다.
        direction = style == Style.Volley && delta.magnitude < 3.5f ? -delta.normalized :
            delta.magnitude > Data.AttackRange * .85f ? delta.normalized : Vector2.zero;
        if (renderer != null && Mathf.Abs(delta.x) > .01f) renderer.flipX = delta.x < 0;
    }
    protected override void FixedUpdate()
    {
        if (IsDead || Data == null || player == null || !player.IsAlive) return;
        if (CurrentPhase == Phase.Striking)
        {
            float distance = Mathf.Min(rushRemaining, 9 * Time.fixedDeltaTime);
            float allowed = FreeDistance(lockedDirection, distance);
            Vector2 start = body.position, finish = start + lockedDirection * allowed;
            if (!rushHit && SegmentDistance(player.transform.position, start, finish) <= .85f)
            { player.TakeDamage(Data.AttackDamage); rushHit = true; }
            body.MovePosition(finish); rushRemaining -= allowed;
            if (allowed < distance - .001f || rushRemaining <= .001f) Recover();
            return;
        }
        if (direction == Vector2.zero) return;
        Vector2 move = Steer(direction);
        float step = Data.MoveSpeed * Time.fixedDeltaTime;
        body.MovePosition(body.position + move * FreeDistance(move, step));
    }
    void BeginAttack(Vector2 delta)
    {
        CurrentPhase = Phase.Windup; direction = Vector2.zero; due = Time.time + windup;
        lockedOrigin = body.position; lockedDirection = delta.sqrMagnitude > .001f ? delta.normalized : Vector2.right;
        readyAt = Time.time + windup + Data.AttackCooldown;
        ShowWarning();
    }
    void Strike()
    {
        HideWarning(); AttackCount++;
        if (style == Style.Volley)
        {
            var volleyTargets = new HashSet<PlayerStats>();
            for (int i = -1; i <= 1; i++) Shoot(Quaternion.Euler(0,0,i*14) * lockedDirection, volleyTargets);
        }
        else if (style == Style.Rush)
        { CurrentPhase = Phase.Striking; rushRemaining = 4.8f; rushHit = false; return; }
        else
        {
            Vector2 offset = (Vector2)player.transform.position - lockedOrigin;
            bool inRange = offset.magnitude <= Data.AttackRange;
            bool inArc = style == Style.Spores || offset.sqrMagnitude < .001f || Vector2.Dot(offset.normalized,lockedDirection) >= Mathf.Cos(55*Mathf.Deg2Rad);
            if (inRange && inArc) player.TakeDamage(Data.AttackDamage);
        }
        Recover();
    }
    void Recover() { CurrentPhase = Phase.Recovering; due = Time.time + recoveryDuration; direction = Vector2.zero; }
    void ResetAttack() { CurrentPhase = Phase.Walking; HideWarning(); }
    float FreeDistance(Vector2 vector, float distance)
    {
        if (vector == Vector2.zero) return 0;
        var filter = new ContactFilter2D { useTriggers = false };
        int count = body.Cast(vector, filter, hits, distance + .04f);
        float free = distance;
        for (int i = 0; i < count; i++)
        {
            var collider = hits[i].collider;
            if (collider == null || collider.GetComponentInParent<MonsterBase>() != null || collider.GetComponentInParent<PlayerStats>() != null) continue;
            free = Mathf.Min(free, Mathf.Max(0,hits[i].distance - .04f));
        }
        return free;
    }
    Vector2 Steer(Vector2 desired)
    {
        if (FreeDistance(desired,.8f) > .6f) return desired;
        foreach (float angle in new[] { 45f,-45f,90f,-90f })
        { Vector2 alternative = Quaternion.Euler(0,0,angle) * desired; if (FreeDistance(alternative,.8f) > .6f) return alternative; }
        return Vector2.zero;
    }
    static float SegmentDistance(Vector2 point, Vector2 a, Vector2 b)
    { Vector2 ab=b-a;return Vector2.Distance(point,a+ab*Mathf.Clamp01(Vector2.Dot(point-a,ab)/Mathf.Max(.00001f,ab.sqrMagnitude))); }
    void EnsureMaterial()
    { if (material == null) material = new Material(Shader.Find("Sprites/Default")); }
    void ShowWarning()
    {
        EnsureMaterial();
        if (warning == null)
        {
            var go = new GameObject("지역 공격 예고");go.transform.SetParent(transform,true);
            warning=go.AddComponent<LineRenderer>();warning.sharedMaterial=material;warning.useWorldSpace=true;
            warning.sortingOrder=YSortRenderer.WorldOverlayOrder;warning.startWidth=warning.endWidth=.045f;
        }
        Color color=style==Style.Volley?new Color(.4f,.9f,1,.85f):style==Style.Spores?new Color(.8f,.9f,.25f,.85f):new Color(1,.45f,.2f,.9f);
        warning.startColor=warning.endColor=color;warning.gameObject.SetActive(true);
        if(style==Style.Rush||style==Style.Volley)
        {
            float length=style==Style.Rush?4.8f:Data.AttackRange+1.6f;
            Vector2 end=lockedOrigin+lockedDirection*length,side=new Vector2(-lockedDirection.y,lockedDirection.x)*(style==Style.Rush?.85f:.15f);
            warning.loop=true;warning.positionCount=4;
            float widening=style==Style.Volley?(Mathf.Tan(14*Mathf.Deg2Rad)*length+.15f)/.15f:1;
            warning.SetPositions(new[]{(Vector3)(lockedOrigin+side),(Vector3)(end+side*widening),(Vector3)(end-side*widening),(Vector3)(lockedOrigin-side)});
        }
        else
        {
            bool circle=style==Style.Spores;warning.loop=true;warning.positionCount=circle?36:29;
            float facing=Mathf.Atan2(lockedDirection.y,lockedDirection.x);
            for(int i=0;i<warning.positionCount;i++)
            {
                if(!circle&&i==28){warning.SetPosition(i,lockedOrigin);continue;}
                float angle=circle?i*Mathf.PI*2/36:facing+Mathf.Lerp(-55,55,i/27f)*Mathf.Deg2Rad;
                warning.SetPosition(i,lockedOrigin+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*Data.AttackRange);
            }
        }
    }
    void HideWarning() { if(warning!=null)warning.gameObject.SetActive(false); }
    void Shoot(Vector2 vector, HashSet<PlayerStats> volleyTargets)
    {
        EnsureMaterial();var go=new GameObject("강가 박쥐 탄");go.layer=gameObject.layer;go.transform.position=body.position+vector*.6f;
        var line=go.AddComponent<LineRenderer>();line.sharedMaterial=material;line.useWorldSpace=false;line.loop=true;line.positionCount=4;
        line.SetPositions(new[]{new Vector3(0,.15f),new Vector3(.15f,0),new Vector3(0,-.15f),new Vector3(-.15f,0)});
        line.startWidth=line.endWidth=.06f;line.startColor=line.endColor=new Color(.35f,.85f,1);line.sortingOrder=YSortRenderer.WorldOverlayOrder;
        var rigidbody=go.AddComponent<Rigidbody2D>();rigidbody.gravityScale=0;rigidbody.constraints=RigidbodyConstraints2D.FreezeRotation;rigidbody.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
        var collider=go.AddComponent<CircleCollider2D>();collider.isTrigger=true;collider.radius=.15f;
        var projectile=go.AddComponent<RegionalProjectile>();projectile.Initialize(vector,Data.AttackDamage,5.5f,(Data.AttackRange+1)/5.5f,volleyTargets);projectiles.Add(projectile);
    }
    void OnDisable()
    {
        direction=Vector2.zero;CurrentPhase=Phase.Walking;HideWarning();
        foreach(var projectile in projectiles)if(projectile!=null){projectile.gameObject.SetActive(false);Destroy(projectile.gameObject);}projectiles.Clear();
        if(warning!=null){warning.gameObject.SetActive(false);Destroy(warning.gameObject);warning=null;}
        if(material!=null){Destroy(material);material=null;}
    }
}


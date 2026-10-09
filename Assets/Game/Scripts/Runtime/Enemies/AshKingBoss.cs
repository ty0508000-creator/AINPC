using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>예고된 여덟 공격과 반피 광폭화를 사용하는 첫 섬 보스.</summary>
public sealed class AshKingBoss : MonsterBase
{
    public enum CombatPhase { Waiting, Chasing, Telegraph, Recovering, Defeated, Charging }
    public enum AttackPattern { Cleave, AshExplosion, Thrust, CrossSlash, Spin, SwordRain, Charge, RingBurst }
    public static readonly AttackPattern[] Rotation = { AttackPattern.Cleave, AttackPattern.Thrust, AttackPattern.CrossSlash, AttackPattern.Spin, AttackPattern.SwordRain, AttackPattern.Charge, AttackPattern.RingBurst, AttackPattern.AshExplosion };
    public CombatPhase Phase { get; private set; } = CombatPhase.Waiting;
    public AttackPattern Pattern { get; private set; }
    public float Health { get; private set; }
    public float MaxHealth => Data != null ? Data.MaxHP : 1;
    public bool Enraged => Health <= MaxHealth*.5f;
    public bool Fighting => Phase != CombatPhase.Waiting && Phase != CombatPhase.Defeated;
    public int AttackCount { get; private set; }
    public float TelegraphRemaining => Phase == CombatPhase.Telegraph ? Mathf.Max(0, due-Time.time) : 0;
    public Vector2 LockedDirection { get; private set; }
    public Vector2 AttackOrigin => attackOrigin;
    public IReadOnlyList<Vector2> RainTargets => rainTargets;
    public Vector2 SafeLeft => arenaCenter + new Vector2(-6,0);
    public Vector2 SafeRight => arenaCenter + new Vector2(6,0);
    public const float SafeRadius = 2.4f;
    public event Action Defeated;
    [SerializeField] Vector2 arenaCenter;
    [SerializeField] Vector2 arenaHalfSize = new Vector2(12,9);
    Rigidbody2D body; PlayerStats player; Animator animation;
    Vector2 movement, attackOrigin; float due, readyAt; int sequence;
    float chargeRemaining;bool chargeHit;
    readonly Vector2[] rainTargets = new Vector2[5];
    Material material;
    LineRenderer embers;
    readonly List<LineRenderer> warnings = new List<LineRenderer>();
    readonly RaycastHit2D[] contacts = new RaycastHit2D[12];

    protected override void Awake(){base.Awake();body=GetComponent<Rigidbody2D>();animation=GetComponent<Animator>();}
    public override void Initialize(MonsterData data,MonsterSpawnPoint point)
    {base.Initialize(data,point);Health=data.MaxHP;Phase=CombatPhase.Waiting;sequence=AttackCount=0;movement=Vector2.zero;player=FindFirstObjectByType<PlayerStats>();ClearWarnings();}
    public bool BeginBattle()
    {if(IsDead||Data==null||player==null||!player.IsAlive||Fighting)return false;Phase=CombatPhase.Chasing;readyAt=Time.time+1.2f;return true;}
    public override void TakeDamage(int damage)
    {if(!Fighting||IsDead||damage<=0)return;Health=Mathf.Max(0,Health-damage);base.TakeDamage(damage);}
    protected override void Update()
    {
        if(!Fighting||Data==null||Time.timeScale==0)return;
        if(Enraged)UpdateEmbers();
        if(player==null)player=FindFirstObjectByType<PlayerStats>();
        if(player==null||!player.IsAlive){movement=Vector2.zero;ClearWarnings();Phase=CombatPhase.Waiting;SetSpeed(0);return;}
        if(Phase==CombatPhase.Telegraph){movement=Vector2.zero;if(Time.time>=due)ResolveAttack();return;}
        if(Phase==CombatPhase.Charging)return;
        if(Phase==CombatPhase.Recovering){movement=Vector2.zero;if(Time.time>=due)Phase=CombatPhase.Chasing;return;}
        Vector2 delta=(Vector2)player.transform.position-body.position;
        var next=Rotation[sequence%Rotation.Length];
        bool close=next==AttackPattern.Cleave||next==AttackPattern.Spin||next==AttackPattern.RingBurst;
        if(Time.time>=readyAt&&(!close||delta.magnitude<=Data.AttackRange)){BeginAttack(delta);return;}
        movement=delta.magnitude>1.8f?delta.normalized:Vector2.zero;
        if(GetComponent<SpriteRenderer>() is SpriteRenderer sprite&&Mathf.Abs(delta.x)>.01f)sprite.flipX=delta.x<0;
        SetSpeed(movement.magnitude);
    }
    protected override void FixedUpdate()
    {
        if(Phase==CombatPhase.Charging&&player!=null&&player.IsAlive)
        {
            float step=Mathf.Min(chargeRemaining,24*Time.fixedDeltaTime);
            Vector2 before=body.position,next=MoveBody(LockedDirection,step);
            if(!chargeHit&&SegmentDistance(player.transform.position,before,next)<=1.1f){DealDamage(1.2f);chargeHit=true;}
            chargeRemaining-=Vector2.Distance(before,next);
            if(chargeRemaining<=.04f||Vector2.Distance(before,next)<step*.5f)Recover(1.4f);
            return;
        }
        if(!Fighting||Phase!=CombatPhase.Chasing||movement==Vector2.zero||player==null||!player.IsAlive)return;
        float distance=Data.MoveSpeed*(Enraged?1.35f:1)*Time.fixedDeltaTime;
        MoveBody(movement,distance);
    }
    Vector2 MoveBody(Vector2 direction,float distance)
    {
        var filter=new ContactFilter2D{useTriggers=false};int count=body.Cast(direction,filter,contacts,distance+.04f);
        for(int i=0;i<count;i++){var c=contacts[i].collider;if(c==null||c.GetComponentInParent<MonsterBase>()!=null||c.GetComponentInParent<PlayerStats>()!=null)continue;distance=Mathf.Min(distance,Mathf.Max(0,contacts[i].distance-.04f));}
        Vector2 next=body.position+direction*distance;next.x=Mathf.Clamp(next.x,arenaCenter.x-arenaHalfSize.x,arenaCenter.x+arenaHalfSize.x);next.y=Mathf.Clamp(next.y,arenaCenter.y-arenaHalfSize.y,arenaCenter.y+arenaHalfSize.y);body.MovePosition(next);return next;
    }
    void BeginAttack(Vector2 delta)
    {
        Pattern=Rotation[sequence++%Rotation.Length];Phase=CombatPhase.Telegraph;movement=Vector2.zero;SetSpeed(0);
        attackOrigin=body.position;LockedDirection=delta.sqrMagnitude>.001f?delta.normalized:Vector2.down;
        if(Mathf.Abs(LockedDirection.x)>.01f)GetComponent<SpriteRenderer>().flipX=LockedDirection.x<0;
        float windup=Pattern==AttackPattern.AshExplosion?3.2f:Pattern==AttackPattern.SwordRain?1.8f:Pattern==AttackPattern.RingBurst?1.6f:Pattern==AttackPattern.Spin?1.5f:1.2f;
        if(Pattern==AttackPattern.Cleave)windup=1;
        due=Time.time+windup*(Enraged?.85f:1);
        ClearWarnings();
        if(Pattern==AttackPattern.Cleave){var line=Line("검격 예고",new Color(1,.38f,.15f,.9f),.065f);line.positionCount=34;float angle=Mathf.Atan2(LockedDirection.y,LockedDirection.x);for(int i=0;i<33;i++){float a=angle+Mathf.Lerp(-65,65,i/32f)*Mathf.Deg2Rad;line.SetPosition(i,attackOrigin+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*Data.AttackRange);}line.SetPosition(33,attackOrigin);}
        else if(Pattern==AttackPattern.AshExplosion){var boundary=Line("재 폭발 범위",new Color(1,.28f,.08f,.9f),.1f);boundary.positionCount=4;boundary.SetPositions(new[]{(Vector3)(arenaCenter+new Vector2(-12,-9)),(Vector3)(arenaCenter+new Vector2(12,-9)),(Vector3)(arenaCenter+new Vector2(12,9)),(Vector3)(arenaCenter+new Vector2(-12,9))});Circle(SafeLeft,SafeRadius,true);Circle(SafeRight,SafeRadius,true);}
        else if(Pattern==AttackPattern.Thrust||Pattern==AttackPattern.Charge)Corridor(attackOrigin,attackOrigin+LockedDirection*(Pattern==AttackPattern.Thrust?8:10),Pattern==AttackPattern.Thrust?.7f:1.1f);
        else if(Pattern==AttackPattern.CrossSlash){var side=new Vector2(-LockedDirection.y,LockedDirection.x);Corridor(attackOrigin-LockedDirection*7,attackOrigin+LockedDirection*7,.8f);Corridor(attackOrigin-side*7,attackOrigin+side*7,.8f);}
        else if(Pattern==AttackPattern.Spin)Circle(attackOrigin,4.5f,false);
        else if(Pattern==AttackPattern.RingBurst){Circle(attackOrigin,2.2f,true);Circle(attackOrigin,6.5f,false);}
        else if(Pattern==AttackPattern.SwordRain){Vector2 center=player.transform.position;var offsets=new[]{Vector2.zero,Vector2.left*3,Vector2.right*3,Vector2.up*3,Vector2.down*3};for(int i=0;i<rainTargets.Length;i++){var target=center+offsets[i];target.x=Mathf.Clamp(target.x,arenaCenter.x-11,arenaCenter.x+11);target.y=Mathf.Clamp(target.y,arenaCenter.y-8,arenaCenter.y+8);rainTargets[i]=target;Circle(target,1.3f,false);}}
    }
    public bool IsSafe(Vector2 position)=>Vector2.Distance(position,SafeLeft)<=SafeRadius||Vector2.Distance(position,SafeRight)<=SafeRadius;
    public bool DangerAt(Vector2 position)
    {
        Vector2 offset=position-attackOrigin;float radius=offset.magnitude;
        switch(Pattern)
        {
            case AttackPattern.Cleave:return radius<=Data.AttackRange&&(radius<.001f||Vector2.Dot(offset.normalized,LockedDirection)>=Mathf.Cos(65*Mathf.Deg2Rad));
            case AttackPattern.Thrust:return SegmentDistance(position,attackOrigin,attackOrigin+LockedDirection*8)<=.7f;
            case AttackPattern.Charge:return SegmentDistance(position,attackOrigin,attackOrigin+LockedDirection*10)<=1.1f;
            case AttackPattern.CrossSlash:var side=new Vector2(-LockedDirection.y,LockedDirection.x);return SegmentDistance(position,attackOrigin-LockedDirection*7,attackOrigin+LockedDirection*7)<=.8f||SegmentDistance(position,attackOrigin-side*7,attackOrigin+side*7)<=.8f;
            case AttackPattern.Spin:return radius<=4.5f;
            case AttackPattern.RingBurst:return radius>=2.2f&&radius<=6.5f;
            case AttackPattern.SwordRain:foreach(var target in rainTargets)if(Vector2.Distance(position,target)<=1.3f)return true;return false;
            default:return !IsSafe(position);
        }
    }
    static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b){var delta=b-a;return Vector2.Distance(p,a+delta*Mathf.Clamp01(Vector2.Dot(p-a,delta)/Mathf.Max(.001f,delta.sqrMagnitude)));}
    void ResolveAttack()
    {
        ClearWarnings();AttackCount++;
        if(animation!=null)animation.SetTrigger(Pattern==AttackPattern.Cleave||Pattern==AttackPattern.Spin||Pattern==AttackPattern.CrossSlash?"Cleave":"Explosion");
        if(Pattern==AttackPattern.Charge){Phase=CombatPhase.Charging;chargeRemaining=10;chargeHit=false;return;}
        if(DangerAt(player.transform.position))DealDamage(Pattern==AttackPattern.AshExplosion?1.6f:Pattern==AttackPattern.RingBurst?1.3f:1);
        if(Pattern==AttackPattern.SwordRain)foreach(var target in rainTargets){var slash=Line("낙하 검격",new Color(1,.85f,.4f),.12f);slash.loop=false;slash.positionCount=2;slash.SetPositions(new[]{(Vector3)(target+Vector2.up*1.6f),(Vector3)(target-Vector2.up*.4f)});Destroy(slash.gameObject,.3f);}
        Recover(Pattern==AttackPattern.AshExplosion?1.5f:1.1f);
    }
    void DealDamage(float multiplier){player.TakeDamage(Mathf.RoundToInt(Data.AttackDamage*multiplier*(Enraged?1.2f:1)));}
    void Recover(float duration){Phase=CombatPhase.Recovering;due=Time.time+duration;readyAt=due+Data.AttackCooldown*(Enraged?.85f:1);}
    protected override void Die()
    {if(IsDead)return;Phase=CombatPhase.Defeated;movement=Vector2.zero;ClearWarnings();SetSpeed(0);base.Die();Defeated?.Invoke();}
    void SetSpeed(float speed){if(animation!=null)animation.SetFloat("Speed",speed);}
    void UpdateEmbers()
    {
        if(material==null)material=new Material(Shader.Find("Sprites/Default"));
        if(embers==null){var go=new GameObject("광폭 잿불");go.transform.SetParent(transform,true);embers=go.AddComponent<LineRenderer>();embers.sharedMaterial=material;embers.useWorldSpace=true;embers.loop=true;embers.positionCount=32;embers.startWidth=embers.endWidth=.07f;}
        embers.sortingOrder=GetComponent<SpriteRenderer>().sortingOrder-1;var color=new Color(1,.32f,.06f,.6f+.25f*Mathf.Sin(Time.time*8));embers.startColor=embers.endColor=color;
        for(int i=0;i<32;i++){float a=i*Mathf.PI*2/32;embers.SetPosition(i,(Vector2)transform.position+new Vector2(Mathf.Cos(a),Mathf.Sin(a)*.45f)*1.1f);}
    }
    LineRenderer Line(string name,Color color,float width)
    {if(material==null)material=new Material(Shader.Find("Sprites/Default"));var go=new GameObject(name);go.transform.SetParent(transform,true);var line=go.AddComponent<LineRenderer>();line.sharedMaterial=material;line.loop=true;line.useWorldSpace=true;line.startColor=line.endColor=color;line.startWidth=line.endWidth=width;line.sortingOrder=YSortRenderer.WorldOverlayOrder;warnings.Add(line);return line;}
    void Circle(Vector2 center,float radius,bool safe){var line=Line(safe?"안전지대":"위험 원",safe?new Color(.3f,1,.7f):new Color(1,.35f,.12f),.09f);line.positionCount=48;for(int i=0;i<48;i++){float a=i*Mathf.PI*2/48;line.SetPosition(i,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius);}}
    void Corridor(Vector2 from,Vector2 to,float width)
    {
        float heading=Mathf.Atan2(to.y-from.y,to.x-from.x);var line=Line("검로 예고",new Color(1,.4f,.12f),.08f);line.positionCount=34;
        for(int i=0;i<=16;i++){float angle=heading-Mathf.PI*.5f+i*Mathf.PI/16;line.SetPosition(i,to+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*width);angle=heading+Mathf.PI*.5f+i*Mathf.PI/16;line.SetPosition(17+i,from+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*width);}
    }
    void ClearWarnings(){foreach(var line in warnings)if(line!=null){line.gameObject.SetActive(false);Destroy(line.gameObject);}warnings.Clear();}
    void OnDisable(){movement=Vector2.zero;ClearWarnings();if(embers!=null){Destroy(embers.gameObject);embers=null;}if(material!=null){Destroy(material);material=null;}}
}

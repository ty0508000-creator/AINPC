using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class AshKingCombatVerification
{
    const string Key="AshKingCombat.Pending",Slot="AshKingCombat.Slot";
    static PlayerStats player;static Player_Attack attack;static AshKingBoss boss;static Rigidbody2D body;
    static int stage,checks,deaths;static float due,hp,started,warningAt=-1,evadeUntil,damage,previousHP;static Vector2 from,evadePoint;static GameObject wall;
    static readonly System.Collections.Generic.HashSet<AshKingBoss.AttackPattern> seen = new System.Collections.Generic.HashSet<AshKingBoss.AttackPattern>();
    static AshKingCombatVerification(){EditorApplication.playModeStateChanged+=State;if(SessionState.GetBool(Key,false))SaveSystem.VerificationDirectory=SessionState.GetString(Slot,"");}
    public static void Run(){string slot=Path.GetFullPath("VerificationResults/AshKing/Combat/Save-"+Guid.NewGuid().ToString("N"));SessionState.SetString(Slot,slot);SaveSystem.VerificationDirectory=slot;SessionState.SetBool(Key,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();}
    static void State(PlayModeStateChange state)
    {
        if(!SessionState.GetBool(Key,false))return;
        if(state==PlayModeStateChange.EnteredPlayMode){SaveSystem.VerificationDirectory=SessionState.GetString(Slot,"");stage=checks=deaths=0;Time.timeScale=6;new GameObject("Audio listener").AddComponent<AudioListener>();var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));go.GetComponent<Player_Controller>().enabled=false;go.GetComponent<UnityEngine.InputSystem.PlayerInput>().enabled=false;go.transform.position=Vector3.right*2;body=go.GetComponent<Rigidbody2D>();attack=(go.GetComponent<Player_Attack>() ?? go.AddComponent<Player_Attack>());typeof(Player_Attack).GetField("enemyLayer",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(attack,(LayerMask)(1<<6));player=(go.GetComponent<PlayerStats>() ?? go.AddComponent<PlayerStats>());go.GetComponent<RpgUI>().enabled=false;player.AddEXP(478);for(int i=0;i<6;i++)player.UpgradeAttribute(RpgAttribute.Strength);for(int i=0;i<5;i++)player.UpgradeAttribute(RpgAttribute.Vitality);for(int i=0;i<3;i++)player.UpgradeAttribute(RpgAttribute.Defense);foreach(int id in RpgSkillCatalog.Hotbar)player.LearnSkill(id);player.LearnSkill(1);player.LearnSkill(0);Spawn();due=Time.time+.5f;EditorApplication.update+=Tick;}
        if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);SaveSystem.VerificationDirectory=null;Time.timeScale=1;EditorApplication.Exit(SessionState.GetInt("AshKingCombat.Exit",1));}
    }
    static void Spawn(){boss=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/AshKing/AshKing.prefab")).GetComponent<AshKingBoss>();boss.transform.position=Vector3.zero;boss.Initialize(AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/Game/Data/HubStory/AshKing.asset"),null);}
    static void Call(string name,params object[] args)=>typeof(AshKingBoss).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(boss,args);
    static void Set(string name,object value)=>typeof(AshKingBoss).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(boss,value);
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Debug.Log("ASH COMBAT PASS: "+message);}
    static void Move(Vector2 point){player.transform.position=point;body.position=point;body.linearVelocity=Vector2.zero;Physics2D.SyncTransforms();}
    static void PatternChecks()
    {
        var patterns=new[]{AshKingBoss.AttackPattern.Thrust,AshKingBoss.AttackPattern.CrossSlash,AshKingBoss.AttackPattern.Spin,AshKingBoss.AttackPattern.SwordRain,AshKingBoss.AttackPattern.RingBurst};
        var danger=new[]{new Vector2(4,0),new Vector2(0,4),new Vector2(2,0),new Vector2(2,0),new Vector2(4,0)};
        var safe=new[]{new Vector2(4,2),new Vector2(4,4),new Vector2(6,0),new Vector2(-4,-4),Vector2.zero};
        for(int i=0;i<patterns.Length;i++)
        {
            player.Heal(player.MaxHP);Move(Vector2.right*2);Set("sequence",Array.IndexOf(AshKingBoss.Rotation,patterns[i]));hp=player.HP;Call("BeginAttack",Vector2.right);
            Check(boss.Pattern==patterns[i]&&player.HP==hp&&boss.TelegraphRemaining>=1,"distinct harmless telegraph: "+patterns[i]);
            Move(safe[i]);Call("ResolveAttack");Check(player.HP==hp,"safe movement avoids actual attack: "+patterns[i]);
            Move(Vector2.right*2);Set("sequence",Array.IndexOf(AshKingBoss.Rotation,patterns[i]));Call("BeginAttack",Vector2.right);Move(danger[i]);typeof(PlayerStats).GetField("protectedUntil",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(player,0f);Call("ResolveAttack");Check(player.HP<hp,"danger area applies actual damage: "+patterns[i]);
        }
        Check(boss.RainTargets[0]==Vector2.right*2,"sword rain locks position before movement");
    }
    static Vector2 FindEscape()
    {
        Vector2 point=player.transform.position;if(boss.Pattern==AshKingBoss.AttackPattern.AshExplosion)return Vector2.Distance(point,boss.SafeLeft)<Vector2.Distance(point,boss.SafeRight)?boss.SafeLeft:boss.SafeRight;
        for(float radius=1.8f;radius<=12;radius+=1.2f)for(int i=0;i<32;i++){float angle=i*Mathf.PI*2/32;Vector2 candidate=point+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;if(Mathf.Abs(candidate.x)>11||Mathf.Abs(candidate.y)>8)continue;if(!boss.DangerAt(candidate)&&!boss.DangerAt(candidate+Vector2.right*.5f)&&!boss.DangerAt(candidate-Vector2.right*.5f)&&!boss.DangerAt(candidate+Vector2.up*.5f)&&!boss.DangerAt(candidate-Vector2.up*.5f))return candidate;}
        throw new Exception("no reachable escape point for "+boss.Pattern);
    }
    static void Tick()
    {
        if(Time.time<due)return;
        try{switch(stage){
        case 0:
            boss.TakeDamage(1000);Check(boss.Health==boss.MaxHealth,"waiting boss cannot be killed from preparation area");Check(boss.BeginBattle(),"boss battle activates");boss.enabled=false;hp=player.HP;Call("BeginAttack",Vector2.right);Check(boss.Phase==AshKingBoss.CombatPhase.Telegraph&&player.HP==hp,"cleave telegraph has no immediate damage");due=Time.time+.3f;stage++;break;
        case 1:
            Check(player.HP==hp,"cleave reaction window precedes damage");player.transform.position=Vector3.left*2;Call("ResolveAttack");Check(player.HP==hp,"locked cleave misses player behind boss");player.transform.position=Vector3.right*2;Call("BeginAttack",Vector2.right);Call("ResolveAttack");Check(player.HP<hp,"cleave damages player in front");hp=player.HP;Set("sequence",7);Call("BeginAttack",Vector2.right);Check(boss.Pattern==AshKingBoss.AttackPattern.AshExplosion&&boss.TelegraphRemaining>=3,"explosion provides travel time");player.transform.position=boss.SafeLeft;Call("ResolveAttack");Check(player.HP==hp,"left safe zone blocks explosion");Set("sequence",7);Call("BeginAttack",Vector2.right);player.transform.position=boss.SafeRight;Call("ResolveAttack");Check(player.HP==hp,"right safe zone blocks explosion");Set("sequence",7);Call("BeginAttack",Vector2.right);player.transform.position=Vector3.zero;Call("ResolveAttack");Check(player.HP<hp,"explosion hits outside both zones");PatternChecks();Move(new Vector2(3,0));player.Heal(player.MaxHP);hp=player.HP;typeof(PlayerStats).GetField("protectedUntil",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(player,0f);Set("sequence",5);Call("BeginAttack",Vector2.right);boss.enabled=true;due=Time.time+1.5f;stage=10;break;
        case 10:
            Check(boss.transform.position.x>2,"charge moves through actual physics");Check(player.HP<hp,"charge damages a player on its route");boss.enabled=false;boss.GetComponent<Rigidbody2D>().position=Vector2.zero;boss.transform.position=Vector3.zero;Move(new Vector2(0,6));player.Heal(player.MaxHP);hp=player.HP;wall=new GameObject("Charge obstruction");wall.transform.position=Vector3.right*3;wall.AddComponent<BoxCollider2D>().size=new Vector2(1,8);Physics2D.SyncTransforms();Set("sequence",5);Call("BeginAttack",Vector2.right);boss.enabled=true;due=Time.time+1.8f;stage=11;break;
        case 11:
            Check(player.HP==hp,"leaving charge route avoids damage");Check(boss.transform.position.x<2.5f&&boss.Phase==AshKingBoss.CombatPhase.Recovering,"charge stops at solid cover and recovers");UnityEngine.Object.DestroyImmediate(wall);boss.enabled=false;boss.TakeDamage(Mathf.CeilToInt(boss.MaxHealth*.51f));Check(boss.Enraged&&boss.Health<=boss.MaxHealth*.5f,"half-health threshold activates enraged phase");Set("<Phase>k__BackingField",AshKingBoss.CombatPhase.Chasing);Set("readyAt",Time.time+10);player.transform.position=new Vector3(8,3);from=boss.transform.position;started=Time.time;boss.enabled=true;due=Time.time+.4f;stage=2;break;
        case 2:
            Check(Vector2.Distance(from,boss.transform.position)/(Time.time-started)>2.5f,"enraged pursuit is faster in actual physics");boss.Defeated+=()=>deaths++;boss.TakeDamage(Mathf.CeilToInt(boss.MaxHealth*2));boss.TakeDamage(Mathf.CeilToInt(boss.MaxHealth*2));Check(deaths==1,"actual death event occurs once");Check(boss.IsDead,"boss death is a real damage death");player.transform.position=Vector3.zero;body.position=Vector2.zero;body.linearVelocity=Vector2.zero;wall=new GameObject("Dash obstruction");wall.transform.position=Vector3.right*3;wall.AddComponent<BoxCollider2D>().size=new Vector2(1,8);Physics2D.SyncTransforms();attack.CancelActions();attack.ForceDash(Vector2.right,7);due=Time.time+1;stage++;break;
        case 3:
            Check(!attack.IsInvincible,"dash against a closed wall ends invulnerability");Check(player.transform.position.x<2.5f,"dash does not cross a closed wall");UnityEngine.Object.DestroyImmediate(wall);player.transform.position=new Vector3(-5,0);body.position=player.transform.position;player.Heal(player.MaxHP);player.RestoreMana(player.MaxMana);attack.CancelActions();player.GetComponent<RpgSkillController>().ResetForRecovery();Spawn();boss.BeginBattle();started=Time.time;seen.Clear();warningAt=-1;damage=0;previousHP=player.HP;player.OnStatsChanged+=Observe;stage++;break;
        case 4:
            if(boss==null||boss.IsDead){Check(player.IsAlive,"level-four skill build survives boss");Check(seen.Count==8,"all eight patterns occur in the full fight");Check(Time.time-started>=540&&Time.time-started<=660,"boss defeat time stays between nine and eleven minutes");Check(damage<=player.MaxHP*.75f,"boss fight damage stays within budget");Finish(true,"fightSeconds="+(Time.time-started).ToString("F2")+", damage="+damage);return;}
            if(!player.IsAlive||Time.time-started>720)throw new Exception("Boss fight failed or timed out");
            Vector2 delta=boss.transform.position-player.transform.position;Vector2 toward=delta.normalized;Vector2 move;
            seen.Add(boss.Pattern);
            if(boss.Phase==AshKingBoss.CombatPhase.Telegraph){if(warningAt<0){warningAt=Time.time;evadePoint=FindEscape();}if(Time.time-warningAt>.25f)evadeUntil=Time.time+.5f;}else if(boss.Phase!=AshKingBoss.CombatPhase.Charging)warningAt=-1;
            if((boss.Phase==AshKingBoss.CombatPhase.Telegraph&&Time.time-warningAt>.25f)||boss.Phase==AshKingBoss.CombatPhase.Charging)move=Vector2.Distance(player.transform.position,evadePoint)>.3f?(evadePoint-(Vector2)player.transform.position).normalized:Vector2.zero;
            else move=Time.time<evadeUntil?Vector2.zero:delta.magnitude>1.5f?toward:Vector2.zero;
            body.linearVelocity=move*5;var skills=player.GetComponent<RpgSkillController>();typeof(Player_Controller).GetField("<LastMoveDir>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(player.GetComponent<Player_Controller>(),toward);
            if(delta.magnitude<=1.7f&&attack.AttackReady)attack.ForceAttack(toward);if(delta.magnitude<=3&&skills.Remaining(0)<=0)skills.TryCast(0);if(player.HP<player.MaxHP*.7f)skills.TryCast(6);if(boss.Phase==AshKingBoss.CombatPhase.Telegraph&&skills.Remaining(3)<=0)skills.TryCast(3);break;
        }}catch(Exception e){Debug.LogException(e);Finish(false,e.Message+", fightSeconds="+(Time.time-started).ToString("F2")+", damage="+damage);}
    }
    static void Observe(){if(player.HP<previousHP)damage+=previousHP-player.HP;previousHP=player.HP;}
    static void Finish(bool ok,string detail){EditorApplication.update-=Tick;Time.timeScale=1;Directory.CreateDirectory("VerificationResults/AshKing/Combat");File.WriteAllText("VerificationResults/AshKing/Combat/result.txt",(ok?"PASS":"FAIL")+" "+checks+" checks "+detail);SessionState.SetInt("AshKingCombat.Exit",ok?0:1);EditorApplication.ExitPlaymode();}
}

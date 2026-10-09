using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>실제 플레이어 공격과 지역 프리팹으로 반응 지연을 둔 전투를 측정한다.</summary>
[InitializeOnLoad]
public static class HuntingBalanceVerification
{
    const string Pending="HuntingBalance.Pending", Output="HuntingBalance.Output", Strict="HuntingBalance.Strict";
    static PlayerStats player; static Player_Attack attack; static RpgSkillController skills;
    static RegionalMonster enemy; static Rigidbody2D body; static MonsterData data;
    static readonly List<string> rows=new List<string>();
    static readonly List<RegionalMonster> opponents=new List<RegionalMonster>();
    static readonly Dictionary<int,int> attackCounts=new Dictionary<int,int>();
    static int scenario,checks,attacks; static float start,ready,hp,damage,warningAt=-1,evadeUntil,previousHP;
    static double deadline; static bool active; static string attackJson;
    static HuntingBalanceVerification(){EditorApplication.playModeStateChanged+=State;if(SessionState.GetBool(Pending,false))SaveSystem.VerificationDirectory=SessionState.GetString(Output,"")+"/Save";}
    public static void Run()=>Begin(false);
    public static void Verify()=>Begin(true);
    static void Begin(bool strict)
    {
        string output=Path.GetFullPath("VerificationResults/Balance/"+(strict?"Tuned":"Baseline"));Directory.CreateDirectory(output);
        SessionState.SetString(Output,output);SessionState.SetBool(Pending,true);SessionState.SetBool(Strict,strict);SaveSystem.VerificationDirectory=output+"/Save";
        EditorSceneManager.OpenScene("Assets/Scenes/HuntingGround1.unity");
        SessionState.SetString("HuntingBalance.Attack",EditorJsonUtility.ToJson(UnityEngine.Object.FindFirstObjectByType<Player_Attack>()));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
    }
    static void State(PlayModeStateChange state)
    {
        if(!SessionState.GetBool(Pending,false))return;
        if(state==PlayModeStateChange.EnteredPlayMode){SaveSystem.VerificationDirectory=SessionState.GetString(Output,"")+"/Save";scenario=checks=0;rows.Clear();rows.Add("region,profile,level,attackPower,enemyHP,enemyDamage,seconds,playerDamage,basicAttacks,enemyAttacks,result");attackJson=SessionState.GetString("HuntingBalance.Attack","");deadline=EditorApplication.timeSinceStartup+240;new GameObject("Verification audio listener").AddComponent<AudioListener>();Time.timeScale=3;Next();EditorApplication.update+=Tick;}
        if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Pending,false);SaveSystem.VerificationDirectory=null;Time.timeScale=1;EditorApplication.Exit(SessionState.GetInt("HuntingBalance.Exit",1));}
    }
    static void Set(UnityEngine.Object obj,string field,object value)=>obj.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).SetValue(obj,value);
    static void Check(bool pass,string message){if(!pass)throw new Exception(message);checks++;Debug.Log("BALANCE PASS: "+message);}
    static float RouteExperience(int region)
    {
        float sum=0;for(int n=1;n<region;n++){var q=AssetDatabase.LoadAssetAtPath<QuestData>("Assets/Game/Data/HubStory/hub_hunt_"+n+".asset");var d=AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/Game/Data/HubStory/Enemy"+n+".asset");sum+=q.onComplete.expReward+(n==2?0:n==4?5:3)*d.ExpReward;}return sum;
    }
    static void Next()
    {
        if(player!=null)UnityEngine.Object.DestroyImmediate(player.gameObject);foreach(var other in opponents)if(other!=null)UnityEngine.Object.DestroyImmediate(other.gameObject);opponents.Clear();attackCounts.Clear();
        foreach(var projectile in UnityEngine.Object.FindObjectsByType<RegionalProjectile>(FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(projectile.gameObject);
        int region=scenario<12?scenario/3+1:3+(scenario-12)/2, profile=scenario<12?scenario%3:1+(scenario-12)%2;
        SaveSystem.VerificationDirectory=SessionState.GetString(Output,"")+"/Save/case-"+scenario+"-"+Guid.NewGuid().ToString("N");
        var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
        go.GetComponent<Player_Controller>().enabled=false;var input=go.GetComponent<UnityEngine.InputSystem.PlayerInput>();if(input!=null)input.enabled=false;
        go.transform.position=new Vector3(-3,0,0);body=go.GetComponent<Rigidbody2D>();body.gravityScale=0;body.linearVelocity=Vector2.zero;
        attack=go.AddComponent<Player_Attack>();EditorJsonUtility.FromJsonOverwrite(attackJson,attack);
        player=go.AddComponent<PlayerStats>();skills=go.GetComponent<RpgSkillController>();go.GetComponent<RpgUI>().enabled=false;
        if(profile!=1){player.AddEXP(RouteExperience(region));}
        if(profile==2){for(int i=0;i<3;i++)Check(player.UpgradeAttribute(RpgAttribute.Strength),"starter strength investment");for(int i=0;i<2;i++)Check(player.UpgradeAttribute(RpgAttribute.Vitality),"starter vitality investment");for(int i=1;i<player.Level;i++){player.UpgradeAttribute(RpgAttribute.Strength);player.UpgradeAttribute(RpgAttribute.Vitality);player.UpgradeAttribute(RpgAttribute.Defense);}foreach(int id in RpgSkillCatalog.Hotbar)Check(player.LearnSkill(id),"starter active learned");if(player.Level>=3)Check(player.LearnSkill(1),"level three passive unlocked");if(player.Level>=4)Check(player.LearnSkill(0),"level four slash upgrade");}
        data=AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/Game/Data/HubStory/Enemy"+region+".asset");
        enemy=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/RegionalEnemies/Region"+region+".prefab")).GetComponent<RegionalMonster>();enemy.transform.position=Vector3.zero;enemy.Initialize(data,null);
        opponents.Add(enemy);if(scenario>=12){var other=UnityEngine.Object.Instantiate(enemy);other.transform.position=new Vector3(1.5f,1);other.Initialize(data,null);opponents.Add(other);}
        Physics2D.SyncTransforms();start=ready=Time.time+.5f;active=false;damage=0;attacks=0;hp=previousHP=player.HP;warningAt=-1;evadeUntil=0;
        player.OnStatsChanged+=Observe;
    }
    static void Observe(){if(active&&player.HP<previousHP)damage+=previousHP-player.HP;previousHP=player.HP;}
    static void Tick()
    {
        try{
            if(EditorApplication.timeSinceStartup>deadline)throw new Exception("balance simulation exceeded wall-clock deadline");
            if(Time.time<ready)return;active=true;
            int region=scenario<12?scenario/3+1:3+(scenario-12)/2, profile=scenario<12?scenario%3:1+(scenario-12)%2;float elapsed=Time.time-start;
            foreach(var other in opponents)if(other!=null)attackCounts[other.GetInstanceID()]=other.AttackCount;
            enemy=opponents.Where(e=>e!=null&&!e.IsDead).OrderBy(e=>Vector2.Distance(e.transform.position,player.transform.position)).FirstOrDefault();
            bool done=enemy==null, timeout=elapsed>35;
            if(profile==0&&elapsed>=10||profile!=0&&(done||!player.IsAlive||timeout)){
                int enemyAttacks=attackCounts.Values.Sum();
                rows.Add(string.Join(",",region,(scenario>=12?"pair-":"")+(profile==0?"stand-10s":profile==1?"level1-basic":"route-skills"),player.Level,attack.AttackPower,data.MaxHP,data.AttackDamage,elapsed.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),damage.ToString("F1",System.Globalization.CultureInfo.InvariantCulture),attacks,enemyAttacks,profile==0?"exposure":done?"kill":player.IsAlive?"timeout":"dead"));
                File.WriteAllLines(SessionState.GetString(Output,"")+"/combat.csv",rows);
                if(SessionState.GetBool(Strict,false)){
                    if(profile==0){Check(player.IsAlive,"region "+region+" tolerates ten-second mistake exposure");Check(damage>0&&damage<=hp*.5f,"region "+region+" exposure damage budget");}
                    else {Check(done,"region "+region+" can be cleared with "+profile);Check(elapsed<= (scenario>=12?35f:profile==1?22f:14f),"region "+region+" kill time budget");Check(damage<=hp*(scenario>=12?.75f:.5f),"region "+region+" dodge damage budget");}
                }
                scenario++;if(scenario==16){Finish(true);return;}Next();return;
            }
            if(profile==0){body.linearVelocity=Vector2.zero;return;}
            Vector2 delta=enemy.transform.position-player.transform.position;Vector2 toward=delta.normalized;
            if(opponents.Any(e=>e!=null&&e.CurrentPhase==RegionalMonster.Phase.Windup)){if(warningAt<0)warningAt=Time.time;if(Time.time-warningAt>=.25f)evadeUntil=Time.time+.45f;}else warningAt=-1;
            if(enemy.AttackStyle==RegionalMonster.Style.Volley&&UnityEngine.Object.FindObjectsByType<RegionalProjectile>(FindObjectsSortMode.None).Any(p=>Vector2.Distance(p.transform.position,player.transform.position)<2f))evadeUntil=Time.time+.3f;
            Vector2 movement=Time.time<evadeUntil?(enemy.AttackStyle==RegionalMonster.Style.Spores?-toward:new Vector2(-toward.y,toward.x)):delta.magnitude>1.1f?toward:Vector2.zero;
            body.linearVelocity=movement*5f;
            if(delta.magnitude<=1.65f&&attack.AttackReady){attack.ForceAttack(toward);attacks++;}
            if(profile==2){typeof(Player_Controller).GetField("<LastMoveDir>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(player.GetComponent<Player_Controller>(),toward);if(delta.magnitude<=3&&skills.Remaining(0)<=0)skills.TryCast(0);if(player.HP<player.MaxHP*.75f)skills.TryCast(6);if(enemy.TelegraphVisible&&skills.Remaining(3)<=0)skills.TryCast(3);}
        }catch(Exception e){Debug.LogException(e);Finish(false,e.Message);}
    }
    static void Finish(bool success,string reason="")
    {
        EditorApplication.update-=Tick;if(body!=null)body.linearVelocity=Vector2.zero;Time.timeScale=1;
        string output=SessionState.GetString(Output,"");File.WriteAllLines(output+"/combat.csv",rows);File.WriteAllText(output+"/result.txt",(success?"PASS":"FAIL")+" "+checks+" checks, "+scenario+"/16 scenarios. "+reason);SessionState.SetInt("HuntingBalance.Exit",success?0:1);EditorApplication.ExitPlaymode();
    }
}

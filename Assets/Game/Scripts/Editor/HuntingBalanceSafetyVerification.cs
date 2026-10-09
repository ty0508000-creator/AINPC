using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class HuntingBalanceSafetyVerification
{
    const string Key="HuntingBalanceSafety.Pending",Slot="HuntingBalanceSafety.Slot";
    static int stage,checks;static float due,hp;static PlayerStats player;static MonsterSpawnPoint point;static MonsterSpawnArea area;
    static HuntingBalanceSafetyVerification(){EditorApplication.playModeStateChanged+=State;if(SessionState.GetBool(Key,false))SaveSystem.VerificationDirectory=SessionState.GetString(Slot,"");}
    public static void Run(){string slot=Path.GetFullPath("VerificationResults/Balance/Safety/Save-"+Guid.NewGuid().ToString("N"));SessionState.SetString(Slot,slot);SaveSystem.VerificationDirectory=slot;SessionState.SetBool(Key,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();}
    static void State(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode){SaveSystem.VerificationDirectory=SessionState.GetString(Slot,"");stage=checks=0;var go=new GameObject("Player");go.AddComponent<Rigidbody2D>().gravityScale=0;go.AddComponent<BoxCollider2D>();player=(go.GetComponent<PlayerStats>() ?? go.AddComponent<PlayerStats>());go.GetComponent<RpgUI>().enabled=false;due=Time.time+.5f;EditorApplication.update+=Tick;}if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);SaveSystem.VerificationDirectory=null;EditorApplication.Exit(SessionState.GetInt("HuntingBalanceSafety.Exit",1));}}
    static void Set(UnityEngine.Object obj,string field,object value)=>obj.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
    static void Check(bool pass,string text){if(!pass)throw new Exception(text);checks++;Debug.Log("BALANCE SAFETY PASS: "+text);}
    static void Tick(){if(Time.time<due)return;try{switch(stage++){
        case 0:
            hp=player.HP;var targets=new HashSet<PlayerStats>();
            for(int i=0;i<3;i++){var shot=new GameObject("Overlap volley "+i);shot.transform.position=new Vector3(-.8f,0);shot.AddComponent<Rigidbody2D>().gravityScale=0;shot.AddComponent<CircleCollider2D>().isTrigger=true;shot.AddComponent<RegionalProjectile>().Initialize(Vector2.right,8,3,1,targets);}
            due=Time.time+.4f;break;
        case 1:
            Check(Mathf.Approximately(hp-player.HP,8),"three overlapping arrows from one volley deal one hit");
            var camera=new GameObject("Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.orthographic=true;camera.orthographicSize=6;
            point=new GameObject("Respawn point").AddComponent<MonsterSpawnPoint>();point.transform.position=Vector3.right*3;
            area=new GameObject("Respawn area").AddComponent<MonsterSpawnArea>();area.enabled=false;
            Set(area,"monsterPrefab",AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/RegionalEnemies/Region1.prefab").GetComponent<RegionalMonster>());Set(area,"monsterData",AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/Game/Data/HubStory/Enemy1.asset"));Set(area,"spawnPoints",new[]{point});Set(area,"minimumRespawnDelay",1f);
            area.SpawnEmptyPoints();Check(!point.IsEmpty,"first spawn has no respawn delay");point.CurrentMonster.TakeDamage(10000);area.SpawnEmptyPoints();Check(point.IsEmpty,"cleared point does not respawn on an immediate global tick");due=Time.time+.4f;break;
        case 2:
            area.SpawnEmptyPoints();Check(point.IsEmpty,"respawn remains blocked before minimum delay");due=Time.time+.75f;break;
        case 3:
            area.SpawnEmptyPoints();Check(!point.IsEmpty,"cleared point respawns after minimum delay");point.CurrentMonster.TakeDamage(10000);Set(area,"minimumRespawnDelay",0f);area.SpawnEmptyPoints();Check(!point.IsEmpty,"default zero delay preserves legacy spawn behavior");
            UnityEngine.Object.DestroyImmediate(player.gameObject);SaveSystem.VerificationDirectory=SessionState.GetString(Slot,"")+"/Progression";
            player=new GameObject("Minimum route player").AddComponent<PlayerStats>();player.GetComponent<RpgUI>().enabled=false;
            Check(player.LearnSkill(0),"minimum route can learn initial slash");
            int[] expectedLevels={2,3,4,4};
            for(int n=1;n<=4;n++){
                var data=AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/Game/Data/HubStory/Enemy"+n+".asset");
                var quest=AssetDatabase.LoadAssetAtPath<QuestData>("Assets/Game/Data/HubStory/hub_hunt_"+n+".asset");
                player.AddEXP((n==2?0:n==4?5:3)*data.ExpReward+quest.onComplete.expReward);
                Check(player.Level==expectedLevels[n-1],"minimum route level after region "+n);
                if(n==2)Check(player.LearnSkill(1),"river report unlocks level-three sword passive");
            }
            player.AddEXP(AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/Game/Data/HubStory/Enemy4.asset").ExpReward);
            Check(player.Level==5,"one optional camp kill reaches level five");
            Check(!player.LearnSkill(RpgSkillCatalog.Ultimate),"ultimate still requires mastering all existing skills");
            Finish(true);break;
    }}catch(Exception e){Debug.LogException(e);Finish(false,e.Message);}}
    static void Finish(bool success,string reason=""){EditorApplication.update-=Tick;Directory.CreateDirectory("VerificationResults/Balance/Safety");File.WriteAllText("VerificationResults/Balance/Safety/result.txt",(success?"PASS":"FAIL")+" "+checks+" checks. "+reason);SessionState.SetInt("HuntingBalanceSafety.Exit",success?0:1);EditorApplication.ExitPlaymode();}
}

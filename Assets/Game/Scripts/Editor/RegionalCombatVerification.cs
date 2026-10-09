using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class RegionalCombatVerification
{
    const string Key="RegionalCombatVerification.Pending";
    static PlayerStats player; static RegionalMonster enemy; static int stage,count; static double due; static float hp;
    static RegionalCombatVerification(){EditorApplication.playModeStateChanged+=Changed;}
    public static void Run(){SaveSystem.VerificationDirectory=Path.GetFullPath("VerificationResults/Regions/Save");SessionState.SetBool(Key,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();}
    static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode){SaveSystem.VerificationDirectory=Path.GetFullPath("VerificationResults/Regions/Save");var go=new GameObject("Player");go.AddComponent<Rigidbody2D>().gravityScale=0;go.AddComponent<BoxCollider2D>();player=go.AddComponent<PlayerStats>();stage=0;due=EditorApplication.timeSinceStartup+.5;EditorApplication.update+=Tick;}if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);SaveSystem.VerificationDirectory=null;EditorApplication.Exit(count<0?1:0);}}
    static void Call(string method,params object[] args)=>typeof(RegionalMonster).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(enemy,args);
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;Debug.Log("REGION PASS: "+text);}
    static void Spawn(int n){if(enemy!=null)UnityEngine.Object.DestroyImmediate(enemy.gameObject);player.transform.position=new Vector3(1,0);enemy=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/RegionalEnemies/Region"+n+".prefab")).GetComponent<RegionalMonster>();enemy.transform.position=Vector3.zero;enemy.Initialize(AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/Game/Data/HubStory/Enemy"+n+".asset"),null);enemy.enabled=false;hp=player.HP;}
    static void Tick(){if(EditorApplication.timeSinceStartup<due)return;try{
        switch(stage++){
        case 0: Spawn(1);Call("BeginAttack",Vector2.right);Check(enemy.TelegraphVisible&&player.HP==hp,"spore windup causes no damage");Call("Strike");Check(player.HP<hp,"spore circle damages nearby player");break;
        case 1: Spawn(1);Call("BeginAttack",Vector2.right);player.transform.position=Vector3.right*4;Call("Strike");Check(player.HP==hp,"leaving spore circle avoids damage");break;
        case 2: Spawn(3);Call("BeginAttack",Vector2.right);player.transform.position=Vector3.left;Call("Strike");Check(player.HP==hp,"cleave direction stays locked and rear is safe");player.transform.position=Vector3.right;Call("BeginAttack",Vector2.right);Call("Strike");Check(player.HP<hp,"cleave hits front arc");break;
        case 3: Spawn(2);Call("BeginAttack",Vector2.right);Call("Strike");Check(UnityEngine.Object.FindObjectsByType<RegionalProjectile>(FindObjectsSortMode.None).Length==3,"river bat launches three projectiles");player.transform.position=Vector3.right*3;due=EditorApplication.timeSinceStartup+.8;break;
        case 4: Check(player.HP<hp,"bat projectile collides with player");UnityEngine.Object.DestroyImmediate(enemy.gameObject);due=EditorApplication.timeSinceStartup+.2;break;
        case 5: Check(UnityEngine.Object.FindObjectsByType<RegionalProjectile>(FindObjectsSortMode.None).Length==0,"enemy destruction clears owned projectiles");Spawn(4);Call("BeginAttack",Vector2.right);Call("Strike");Check(enemy.CurrentPhase==RegionalMonster.Phase.Striking,"goblin enters locked rush");player.transform.position=Vector3.up*3;enemy.enabled=true;due=EditorApplication.timeSinceStartup+1;break;
        case 6: Check(player.HP==hp,"sidestep avoids rush");Check(enemy.CurrentPhase!=RegionalMonster.Phase.Striking,"rush ends after bounded travel");Directory.CreateDirectory("VerificationResults/Regions");File.WriteAllText("VerificationResults/Regions/combat.txt","PASS "+count+" regional combat checks");EditorApplication.update-=Tick;EditorApplication.ExitPlaymode();break;
        }
    }catch(Exception e){count=-1;Debug.LogException(e);EditorApplication.update-=Tick;EditorApplication.ExitPlaymode();}}
}

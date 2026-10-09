using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>동굴·후속 지역 작업 병합 후 플레이어 중복과 실제 동굴 출구를 검사한다.</summary>
[InitializeOnLoad]
public static class MergeIntegrationVerification
{
    const string Pending="MergeIntegration.Pending",Slot="MergeIntegration.Slot";
    static int checks,stage;static double due,deadline;
    static MergeIntegrationVerification(){EditorApplication.playModeStateChanged+=State;if(SessionState.GetBool(Pending,false))SaveSystem.VerificationDirectory=SessionState.GetString(Slot,"");}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Debug.Log("MERGE PASS: "+message);}
    public static void RunCompatibilityAndCombat()
    {
        checks=0;Directory.CreateDirectory("VerificationResults/Merge");
        try
        {
            foreach(string scene in new[]{"HuntingGround1","Main"})
            {
                EditorSceneManager.OpenScene("Assets/Scenes/"+scene+".unity");
                var view=UnityEngine.Object.FindFirstObjectByType<SceneSettingsView>(FindObjectsInactive.Include);
                Check(view!=null,"settings view exists: "+scene);
                typeof(SceneSettingsView).GetMethod("Start",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);
                Check(true,"legacy and current settings initialize: "+scene);
            }
            File.WriteAllText("VerificationResults/Merge/settings.txt","PASS "+checks+" checks");
            RpgPlayVerification.Run();
        }
        catch(Exception e){Debug.LogException(e);File.WriteAllText("VerificationResults/Merge/settings.txt","FAIL "+checks+" "+e);EditorApplication.Exit(1);}
    }
    public static void Run()
    {
        checks=0;Directory.CreateDirectory("VerificationResults/Merge");
        try
        {
            foreach(string scene in new[]{"Main","HuntingGround1","HuntingGround2","HuntingGround3","HuntingGround4","AshKingArena","Cave","Region2","Region3","Region4","FinalBoss"})
            {
                EditorSceneManager.OpenScene("Assets/Scenes/"+scene+".unity");
                Check(UnityEngine.Object.FindObjectsByType<PlayerStats>(FindObjectsSortMode.None).Length==1,"one stats component: "+scene);
                Check(UnityEngine.Object.FindObjectsByType<Player_Attack>(FindObjectsSortMode.None).Length==1,"one attack component: "+scene);
                Check(UnityEngine.Object.FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None).Length==1,"one inventory component: "+scene);
                Check(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Count(e=>e.isActiveAndEnabled)==1,"one active event system: "+scene);
            }
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            Check(GameObject.Find("HubStory")!=null&&GameObject.Find("AshKingGate")!=null,"current hub and boss route preserved");
            Check(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(t=>t.name.StartsWith("Zone_")).All(t=>!t.gameObject.activeInHierarchy),"alternative Main stage prototypes do not run simultaneously");
            Check(UnityEngine.Object.FindObjectsByType<SettingsButtonUI>(FindObjectsSortMode.None).Length==1,"remote settings button preserved");
            SessionState.SetInt("MergeIntegration.Checks",checks);SessionState.SetString(Slot,Path.GetFullPath("VerificationResults/Merge/Save-"+Guid.NewGuid().ToString("N")));SessionState.SetBool(Pending,true);SaveSystem.VerificationDirectory=SessionState.GetString(Slot,"");
            EditorSceneManager.OpenScene("Assets/Scenes/Cave.unity");EditorApplication.EnterPlaymode();
        }
        catch(Exception e){Debug.LogException(e);File.WriteAllText("VerificationResults/Merge/result.txt","FAIL "+checks+" "+e.Message);EditorApplication.Exit(1);}
    }
    static void State(PlayModeStateChange state)
    {
        if(!SessionState.GetBool(Pending,false))return;
        if(state==PlayModeStateChange.EnteredPlayMode){checks=SessionState.GetInt("MergeIntegration.Checks",0);stage=0;due=EditorApplication.timeSinceStartup+3;deadline=due+60;EditorApplication.update+=Tick;}
        if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Pending,false);SaveSystem.VerificationDirectory=null;EditorApplication.Exit(SessionState.GetInt("MergeIntegration.Exit",1));}
    }
    static void Move(Vector2 point){var p=UnityEngine.Object.FindFirstObjectByType<PlayerStats>();p.transform.position=point;p.GetComponent<Rigidbody2D>().position=point;Physics2D.SyncTransforms();}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup>deadline){Finish(false,"timeout");return;}if(EditorApplication.timeSinceStartup<due||GameFlow.Instance.IsLoading)return;
        try{switch(stage++)
        {
            case 0:
                Check(SceneManager.GetActiveScene().name=="Cave","tutorial scene runs");Check(!GameFlow.Instance.HasFlag(GameFlow.TutorialClearedFlag),"fresh slot has no tutorial flag");
                var pickup=UnityEngine.Object.FindFirstObjectByType<ItemPickup>();Check(pickup!=null,"tutorial sword exists");Move(pickup.transform.position);due=EditorApplication.timeSinceStartup+.6;break;
            case 1:
                Check(GameFlow.Instance.HasFlag(GameFlow.TutorialClearedFlag),"physical sword pickup opens cave exit");Check(SaveSystem.LoadPlayer().flags.Contains(GameFlow.TutorialClearedFlag),"tutorial flag saves with inventory");
                var portal=UnityEngine.Object.FindObjectsByType<ScenePortal>(FindObjectsSortMode.None).Single(p=>(string)typeof(ScenePortal).GetField("targetScene",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(p)=="Main");Move(portal.transform.position);due=EditorApplication.timeSinceStartup+2;break;
            case 2:
                Check(SceneManager.GetActiveScene().name=="Main","physical cave portal reaches Main");Check(Vector2.Distance(UnityEngine.Object.FindFirstObjectByType<PlayerStats>().transform.position,SpawnPoint.Find("cave_exit").transform.position)<1,"correct cave return spawn");Check(GameFlow.Instance.HasFlag(GameFlow.TutorialClearedFlag),"tutorial flag survives scene transition");
                GameFlow.Instance.SaveNow();Check(SaveSystem.LoadPlayer().flags.Contains(GameFlow.TutorialClearedFlag),"Main save preserves tutorial flag");UnityEngine.Object.DestroyImmediate(GameFlow.Instance.gameObject);Check(GameFlow.Instance.HasFlag(GameFlow.TutorialClearedFlag),"recreated game flow restores saved progression flags");Finish(true,"");break;
        }}catch(Exception e){Debug.LogException(e);Finish(false,e.Message);}
    }
    static void Finish(bool ok,string message){EditorApplication.update-=Tick;File.WriteAllText("VerificationResults/Merge/result.txt",(ok?"PASS":"FAIL")+" "+checks+" checks "+message);SessionState.SetInt("MergeIntegration.Exit",ok?0:1);EditorApplication.ExitPlaymode();}
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class AshKingStoryVerification
{
    const string Key="AshKingStory.Pending",Slot="AshKingStory.Slot";
    static int stage,checks;static double due,deadline;static AshKingBoss original;static PlayerStats player;
    static AshKingStoryVerification(){EditorApplication.playModeStateChanged+=State;if(SessionState.GetBool(Key,false))SaveSystem.VerificationDirectory=SessionState.GetString(Slot,"");}
    public static void Run(){string slot=Path.GetFullPath("VerificationResults/AshKing/Story/Save-"+Guid.NewGuid().ToString("N"));SessionState.SetString(Slot,slot);SessionState.SetBool(Key,true);SaveSystem.VerificationDirectory=slot;EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");EditorApplication.EnterPlaymode();}
    static void State(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode){stage=checks=0;due=EditorApplication.timeSinceStartup+3;deadline=due+120;EditorApplication.update+=Tick;}if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);SaveSystem.VerificationDirectory=null;EditorApplication.Exit(SessionState.GetInt("AshKingStory.Exit",1));}}
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Debug.Log("ASH STORY PASS: "+text);}
    static void Move(Vector2 point){player=UnityEngine.Object.FindFirstObjectByType<PlayerStats>();player.transform.position=point;player.GetComponent<Rigidbody2D>().position=point;Physics2D.SyncTransforms();}
    static void Enter(string target){var portal=UnityEngine.Object.FindObjectsByType<ScenePortal>(FindObjectsSortMode.None).Single(p=>(string)typeof(ScenePortal).GetField("targetScene",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p)==target);Move(portal.transform.position);due=EditorApplication.timeSinceStartup+2;}
    static void Prepare()
    {
        var q=QuestManager.Instance;q.ReportReach("hub_village");q.ReportTalk(HubStoryIds.Elder);
        for(int n=1;n<=4;n++){q.StartQuest(HubStoryIds.Hunt(n));q.ReportReach(HubStoryIds.Area(n));if(n!=2)for(int i=0;i<(n==4?5:3);i++)q.ReportKill(HubStoryIds.Monster(n));if(n==2)foreach(string id in new[]{"hub_fragment_a","hub_fragment_b","hub_fragment_c"})q.ReportCollect(id);if(n==3)StoryChoiceManager.Instance.Choose(new StoryChoiceOption{choiceId=HubStoryIds.Rescue,moodDelta=8,memoryLine="보스 검증의 주민 구조"});if(n==4)q.ReportReach("hub_enemy_camp");q.ReportTalk(n==1||n==4?HubStoryIds.Elder:HubStoryIds.Hayeon);}
    }
    static void Capture(AshKingBoss boss)
    {
        var camera=Camera.main;var position=camera.transform.position;float size=camera.orthographicSize;
        camera.transform.position=new Vector3(0,0,-10);camera.orthographicSize=12;
        var canvases=UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c=>c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
        foreach(var canvas in canvases){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
        var texture=new RenderTexture(1600,1000,24);camera.targetTexture=texture;
        var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);Directory.CreateDirectory("VerificationResults/AshKing/Story");
        foreach(var pattern in AshKingBoss.Rotation)
        {
            typeof(AshKingBoss).GetField("sequence",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(boss,Array.IndexOf(AshKingBoss.Rotation,pattern));
            typeof(AshKingBoss).GetMethod("BeginAttack",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(boss,new object[]{Vector2.down});
            UnityEngine.Object.FindFirstObjectByType<AshKingHud>().SendMessage("Update");Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=texture;
            image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes("VerificationResults/AshKing/Story/"+pattern+".png",image.EncodeToPNG());
        }
        File.WriteAllBytes("VerificationResults/AshKing/Story/arena.png",image.EncodeToPNG());
        camera.targetTexture=null;RenderTexture.active=null;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(texture);
        foreach(var canvas in canvases)canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        camera.transform.position=position;camera.orthographicSize=size;
    }
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup>deadline){Finish(false,"story verification timed out");return;}
        if(EditorApplication.timeSinceStartup<due||GameFlow.Instance.IsLoading)return;
        var q=QuestManager.Instance;if(q==null||!q.IsSaveReady)return;
        try{switch(stage++){
        case 0:Check(!q.IsCompleted(HubStoryIds.Hunt(4)),"boss door initially locked");Enter(HubStoryIds.BossScene);break;
        case 1:Check(SceneManager.GetActiveScene().name=="Main","physical boss portal blocks early entry");Prepare();Check(q.IsCompleted(HubStoryIds.Hunt(4)),"camp report opens boss route");Check(q.GetState(HubStoryIds.Boss)==QuestState.Active,"boss quest starts after camp report");Move(SpawnPoint.Find(GameFlow.DefaultSpawn).transform.position);due=EditorApplication.timeSinceStartup+.4;break;
        case 2:Enter(HubStoryIds.BossScene);break;
        case 3:
            Check(SceneManager.GetActiveScene().name==HubStoryIds.BossScene,"physical portal enters boss map");player=UnityEngine.Object.FindFirstObjectByType<PlayerStats>();var arena=UnityEngine.Object.FindFirstObjectByType<AshKingArena>();Check(arena!=null&&arena.Boss!=null,"boss encounter exists");Check(Vector2.Distance(player.transform.position,new Vector2(0,-15))<1,"arrival outside combat gate");Check(Vector2.Distance(player.CheckpointPosition,new Vector2(0,-15))<1,"boss retry checkpoint");Check(arena.Boss.Phase==AshKingBoss.CombatPhase.Waiting,"boss waits for combat entry");Check(!q.IsCompleted(HubStoryIds.Boss),"arrival does not complete boss quest");Move(new Vector2(0,-7));due=EditorApplication.timeSinceStartup+.5;break;
        case 4:
            arena=UnityEngine.Object.FindFirstObjectByType<AshKingArena>();original=arena.Boss;Check(original.Fighting,"crossing arena threshold starts fight");Capture(original);original.TakeDamage(Mathf.CeilToInt(original.MaxHealth*.51f));Check(original.Enraged,"actual fight enters second phase");player.TakeDamage(100000);Check(!player.IsAlive,"player can die in boss fight");Check(!original.gameObject.activeSelf,"death disables current fight");Check(player.Respawn(),"boss retry succeeds");due=EditorApplication.timeSinceStartup+.5;break;
        case 5:
            arena=UnityEngine.Object.FindFirstObjectByType<AshKingArena>();Check(arena.Boss!=null&&arena.Boss!=original,"retry creates fresh boss");Check(arena.Boss.Health==arena.Boss.MaxHealth&&!arena.Boss.Enraged,"retry restores full first-phase boss");Check(arena.Boss.Phase==AshKingBoss.CombatPhase.Waiting,"retry requires reentering arena");Check(!q.IsCompleted(HubStoryIds.Boss),"player death preserves active boss quest");Move(new Vector2(0,-7));due=EditorApplication.timeSinceStartup+.4;break;
        case 6:
            arena=UnityEngine.Object.FindFirstObjectByType<AshKingArena>();Check(arena.Boss.Fighting,"fight starts again after retry");arena.Boss.TakeDamage(100000);Check(q.IsCompleted(HubStoryIds.Boss),"real boss death completes boss quest");Check(q.GetState(HubStoryIds.Ending)==QuestState.Active,"boss death unlocks final report");Check(!q.IsCompleted(HubStoryIds.Ending),"boss death does not skip NPC report");due=EditorApplication.timeSinceStartup+.5;break;
        case 7:Enter("Main");break;
        case 8:
            Check(SceneManager.GetActiveScene().name=="Main","boss victory returns to Main");Check(Vector2.Distance(UnityEngine.Object.FindFirstObjectByType<PlayerStats>().transform.position,SpawnPoint.Find(HubStoryIds.BossReturn).transform.position)<1,"matching boss return spawn");var elder=UnityEngine.Object.FindObjectsByType<HubStoryActor>(FindObjectsSortMode.None).Single(a=>a.actorId==HubStoryIds.Elder);Check(elder.Interact(),"ending report opens");UnityEngine.Object.FindFirstObjectByType<StoryConversationUI>().Confirm();Check(q.IsCompleted(HubStoryIds.Ending),"confirmed final report completes chapter");var save=SaveSystem.LoadPlayer();Check(save.quests.entries.Any(e=>e.questId==HubStoryIds.Boss&&e.state==QuestState.Completed)&&save.quests.entries.Any(e=>e.questId==HubStoryIds.Ending&&e.state==QuestState.Completed),"boss and ending completion persist together");Enter(HubStoryIds.BossScene);break;
        case 9:
            Check(SceneManager.GetActiveScene().name==HubStoryIds.BossScene,"cleared arena can be revisited");arena=UnityEngine.Object.FindFirstObjectByType<AshKingArena>();Check(arena.Cleared&&arena.Boss==null,"saved defeated boss does not respawn");Finish(true,"");break;
        }}catch(Exception e){Debug.LogException(e);Finish(false,e.Message);}
    }
    static void Finish(bool ok,string detail){EditorApplication.update-=Tick;Time.timeScale=1;Directory.CreateDirectory("VerificationResults/AshKing/Story");File.WriteAllText("VerificationResults/AshKing/Story/result.txt",(ok?"PASS":"FAIL")+" "+checks+" checks "+detail);SessionState.SetInt("AshKingStory.Exit",ok?0:1);EditorApplication.ExitPlaymode();}
}


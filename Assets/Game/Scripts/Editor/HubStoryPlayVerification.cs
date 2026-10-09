using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class HubStoryPlayVerification
{
    const string Pending="HubStoryPlay.Pending", Slot="HubStoryPlay.Slot";
    static int stage, area, checks; static double due, deadline;
    static HubStoryPlayVerification() {
        EditorApplication.playModeStateChanged+=Changed;
        if(SessionState.GetBool(Pending,false)) SaveSystem.VerificationDirectory=SessionState.GetString(Slot,"");
    }
    public static void Run() {
        string slot=Path.GetFullPath("VerificationResults/HubStory/play-slot-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(slot);
        SessionState.SetString(Slot,slot);SessionState.SetBool(Pending,true);SaveSystem.VerificationDirectory=slot;
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity",OpenSceneMode.Single);EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state) {
        if(!SessionState.GetBool(Pending,false))return;
        if(state==PlayModeStateChange.EnteredPlayMode) { stage=checks=0;area=1;due=EditorApplication.timeSinceStartup+3;deadline=due+90;EditorApplication.update+=Tick; }
        if(state==PlayModeStateChange.EnteredEditMode) { SessionState.SetBool(Pending,false);SaveSystem.VerificationDirectory=null;EditorApplication.Exit(SessionState.GetInt("HubStoryPlay.Exit",1)); }
    }
    static void Check(bool pass,string text) { if(!pass)throw new Exception(text);checks++;Debug.Log("[HubPlay PASS] "+text); }
    static void Move(Vector3 position) { var p=UnityEngine.Object.FindFirstObjectByType<PlayerStats>();p.transform.position=position;Physics2D.SyncTransforms(); }
    static void Enter(string target) {
        var portal=UnityEngine.Object.FindObjectsByType<ScenePortal>(FindObjectsSortMode.None).Single(p=>(string)typeof(ScenePortal).GetField("targetScene",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p)==target);
        Move(portal.transform.position);due=EditorApplication.timeSinceStartup+2;
    }
    static void Tick() {
        if(EditorApplication.timeSinceStartup>deadline) {Finish(false,"transition timed out");return;}
        if(EditorApplication.timeSinceStartup<due||GameFlow.Instance.IsLoading)return;
        try {
            var qm=QuestManager.Instance;if(qm==null||!qm.IsSaveReady)return;
            if(stage==0) {
                Check(qm.IsCompleted(HubStoryIds.Arrival),"Main village auto arrival");
                var elder=UnityEngine.Object.FindObjectsByType<HubStoryActor>(FindObjectsSortMode.None).Single(a=>a.actorId==HubStoryIds.Elder);
                var ui=UnityEngine.Object.FindFirstObjectByType<StoryConversationUI>();Check(elder.Interact(),"live elder interaction");ui.Confirm();Check(qm.IsCompleted(HubStoryIds.Testimony),"live confirmation advances story");
                elder.Interact();ui.Confirm();Check(qm.GetState(HubStoryIds.Hunt(1))==QuestState.Active,"live hunt accepted");
                stage=1;Enter(HubStoryIds.Scene(area));return;
            }
            if(stage==1) {
                Check(SceneManager.GetActiveScene().name==HubStoryIds.Scene(area),"physics portal enters area "+area);
                var player=UnityEngine.Object.FindFirstObjectByType<PlayerStats>();var spawn=SpawnPoint.Find(GameFlow.DefaultSpawn);
                Check(player!=null&&spawn!=null,"playable character and spawn exist "+area);
                Check(Vector2.Distance(player.transform.position,spawn.transform.position)<1,"arrival placed outside return portal "+area);
                Check(Vector2.Distance(player.CheckpointPosition,spawn.transform.position)<1,"hunting checkpoint "+area);
                if(area==1) {
                    Move(new Vector3(0,-5,0));UnityEngine.Object.FindFirstObjectByType<MonsterSpawnArea>().SpawnEmptyPoints();
                    var monster=UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None).FirstOrDefault(m=>!m.IsDead);
                    Check(monster!=null,"real hunting monster spawns");monster.TakeDamage(100000);
                    Check(qm.Get(HubStoryIds.Hunt(1)).Progress[1]==1,"real monster death reports scoped kill");
                }
                stage=2;Enter("Main");return;
            }
            Check(SceneManager.GetActiveScene().name=="Main","physics portal returns to Main "+area);
            Check(Vector2.Distance(UnityEngine.Object.FindFirstObjectByType<PlayerStats>().transform.position,SpawnPoint.Find(HubStoryIds.ReturnSpawn(area)).transform.position)<1,"matching Main return spawn "+area);
            Check(qm.Get(HubStoryIds.Hunt(1)).Progress[1]==1,"partial hunt survives round trip "+area);
            if(area==4) {Finish(true,"");return;}
            area++;stage=1;Enter(HubStoryIds.Scene(area));
        } catch(Exception ex) {Debug.LogException(ex);Finish(false,ex.Message);}
    }
    static void Finish(bool ok,string message) {
        EditorApplication.update-=Tick;Time.timeScale=1;string result=(ok?"PASS ":"FAIL ")+checks+" checks "+message;
        File.WriteAllText(Path.GetFullPath("VerificationResults/HubStory/play-result.txt"),result);Debug.Log("[HubStoryPlayVerification] "+result);
        SessionState.SetInt("HubStoryPlay.Exit",ok?0:1);EditorApplication.ExitPlaymode();
    }
}

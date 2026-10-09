using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HubStoryVerification
{
    public static void RunRegressions()
    {
        RecoverySaveVerification.Run();
        if(!File.ReadAllText("VerificationResults/RecoverySaveVerification/result.txt").StartsWith("PASS"))return;
        RpgVerification.Run();
        if(!File.ReadAllText("VerificationResults/RpgVerification/result.txt").StartsWith("PASS"))return;
        EditorApplication.Exit(0);
    }
    static int count;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; Debug.Log("[HubStory PASS] " + message); }
    static void Invoke(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target,null);
    static T[] All<T>() where T : UnityEngine.Object => UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include,FindObjectsSortMode.None);
    public static void Run()
    {
        string output=Path.GetFullPath("VerificationResults/HubStory"); Directory.CreateDirectory(output);
        SaveSystem.VerificationDirectory=Path.Combine(output,"slot-"+Guid.NewGuid().ToString("N")); count=0;
        try {
            foreach (string sceneName in new[]{"Main","HuntingGround1","HuntingGround2","HuntingGround3","HuntingGround4"}) {
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+sceneName+".unity",OpenSceneMode.Single);
                Check(All<QuestManager>().Length==1,"one quest manager: "+sceneName);
                Check(All<PlayerStats>().Length==1 && All<Player_Attack>().Length==1,"complete playable character: "+sceneName);
                Check(All<StoryConversationUI>().Length==1,"baked story UI: "+sceneName);
                Check(All<SpawnPoint>().Any(s=>s.id==GameFlow.DefaultSpawn),"default arrival: "+sceneName);
                foreach(var portal in All<ScenePortal>()) {
                    string target=(string)typeof(ScenePortal).GetField("targetScene",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(portal);
                    Check(EditorBuildSettings.scenes.Any(s=>s.enabled&&Path.GetFileNameWithoutExtension(s.path)==target),"portal target enabled: "+target);
                }
                var camera=All<Camera>().FirstOrDefault(c=>c.CompareTag("MainCamera"));
                if(camera!=null) {
                    Vector3 position=camera.transform.position; float size=camera.orthographicSize;
                    camera.transform.position=new Vector3(0,0,-10); camera.orthographicSize=sceneName=="Main"?32:30;
                    var texture=new RenderTexture(1200,900,24); camera.targetTexture=texture; camera.Render(); RenderTexture.active=texture;
                    var image=new Texture2D(1200,900,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1200,900),0,0); image.Apply(); File.WriteAllBytes(Path.Combine(output,sceneName+".png"),image.EncodeToPNG());
                    camera.targetTexture=null; RenderTexture.active=null; UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(texture);
                    camera.transform.position=position; camera.orthographicSize=size;
                }
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var player=new GameObject("verification player").AddComponent<PlayerStats>(); Invoke(player,"Awake");
            var q=new GameObject("verification quests").AddComponent<QuestManager>();
            typeof(QuestManager).GetField("quests",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(q,AssetDatabase.FindAssets("t:QuestData",new[]{"Assets/Game/Data/HubStory"}).Select(g=>AssetDatabase.LoadAssetAtPath<QuestData>(AssetDatabase.GUIDToAssetPath(g))).ToArray());
            Invoke(q,"Awake"); Invoke(q,"Start"); Check(q.IsSaveReady,"isolated save loads");
            var choice=q.gameObject.AddComponent<StoryChoiceManager>(); Invoke(choice,"Awake"); Invoke(choice,"Start");
            var ui=new GameObject("verification story").AddComponent<StoryConversationUI>(); ui.Bake(ui.transform); Invoke(ui,"Start");
            var elder=new GameObject("elder").AddComponent<HubStoryActor>(); elder.actorId=HubStoryIds.Elder; elder.displayName="현묵"; elder.greeting="검증"; elder.offers=new[]{HubStoryIds.Hunt(1),HubStoryIds.Hunt(4)};
            var hayeon=new GameObject("hayeon").AddComponent<HubStoryActor>(); hayeon.actorId=HubStoryIds.Hayeon; hayeon.displayName="하연"; hayeon.offers=new[]{HubStoryIds.Hunt(2),HubStoryIds.Hunt(3)};
            q.ReportReach("hub_village"); Check(q.IsCompleted(HubStoryIds.Arrival),"village starts story");
            Check(elder.Interact(),"elder opens testimony"); ui.Close(); Check(!q.IsCompleted(HubStoryIds.Testimony),"closing dialogue does not report talk");
            elder.Interact(); ui.Confirm(); Check(q.IsCompleted(HubStoryIds.Testimony),"confirmed testimony unlocks hunt");
            for(int n=1;n<=4;n++) {
                var actor=n==1||n==4?elder:hayeon;
                Check(actor.Interact(),"offer opens hunt "+n); ui.Confirm(); Check(q.GetState(HubStoryIds.Hunt(n))==QuestState.Active,"accept hunt "+n);
                actor.Interact(); ui.Confirm(); Check(!q.IsCompleted(HubStoryIds.Hunt(n)),"early return cannot complete hunt "+n);
                q.ReportReach(HubStoryIds.Area(n));
                if(n!=2) { q.ReportKill("wrong_region"); Check(q.Get(HubStoryIds.Hunt(n)).Progress[1]==0,"kills scoped to region "+n); for(int i=0;i<(n==4?5:3);i++)q.ReportKill(HubStoryIds.Monster(n)); }
                if(n==2) for(int i=0;i<3;i++) { var fragment=new GameObject("fragment").AddComponent<HubStoryActor>(); fragment.collectQuest=HubStoryIds.Hunt(2);fragment.collectId="hub_fragment_"+(char)('a'+i);fragment.displayName="파편"; Check(fragment.Interact(),"fragment interaction "+i);ui.Confirm();Check(fragment.Collected()&&!fragment.Interact(),"fragment cannot be collected twice "+i); }
                if(n==3) { var child=new GameObject("child").AddComponent<HubStoryActor>();child.rescueQuest=HubStoryIds.Hunt(3);child.rescueChoice=HubStoryIds.Rescue;child.displayName="소윤";Check(child.Interact(),"rescue interaction");ui.Confirm();Check(choice.HasChoice(HubStoryIds.Rescue),"rescue choice recorded"); }
                if(n==4)q.ReportReach("hub_enemy_camp");
                Check(!q.IsCompleted(HubStoryIds.Hunt(n)),"objectives require return report "+n);
                actor.Interact();ui.Confirm();Check(q.IsCompleted(HubStoryIds.Hunt(n)),"confirmed report completes hunt "+n);
            }
            Check(SaveSystem.SaveGame(player,q),"unified story snapshot commits");
            var saved=SaveSystem.LoadPlayer();Check(saved.quests.entries.Count(e=>e.questId.StartsWith("hub_")&&e.state==QuestState.Completed)==6,"all six story quests persist");
            File.WriteAllText(Path.Combine(output,"result.txt"),"PASS "+count+" checks\n"); Debug.Log("[HubStoryVerification] PASS "+count);
        } finally { Time.timeScale=1;SaveSystem.VerificationDirectory=null;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single); }
    }
}

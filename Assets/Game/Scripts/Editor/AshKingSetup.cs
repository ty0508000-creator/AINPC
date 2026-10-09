using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public static class AshKingSetup
{
    const string Data="Assets/Game/Data/HubStory", Art="Assets/MapTileSet/ground_textures/water/Sprites/MidnightSlash/", BossArt="Assets/Game/Prefabs/AshKing";
    static QuestData[] quests;
    static void Set(UnityEngine.Object o,string name,object value){var serialized=new SerializedObject(o);var p=serialized.FindProperty(name);if(p==null)throw new Exception(name);if(value is UnityEngine.Object obj)p.objectReferenceValue=obj;else if(value is float f)p.floatValue=f;else if(value is int n)p.intValue=n;else if(value is string s)p.stringValue=s;else throw new Exception("Unsupported value "+name);serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(o);}
    static GameObject At(string name,Vector2 position,Transform parent=null){var go=new GameObject(name);go.transform.SetParent(parent);go.transform.position=position;return go;}
    static object Hub(string name,params object[] args)=>typeof(HubStorySetup).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
    static Sprite[] Sprites(string name)=>AssetDatabase.LoadAllAssetsAtPath(Art+name+".png").OfType<Sprite>().OrderBy(s=>int.TryParse(Regex.Match(s.name,@"\d+$").Value,out int i)?i:0).ToArray();
    static AnimationClip Clip(string name,string source,bool loop)
    {
        var sprites=Sprites(source);if(sprites.Length==0)throw new Exception("Missing old playable sprite: "+source);
        string path=BossArt+"/"+name+".anim";var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);if(clip==null){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,path);}clip.frameRate=10;
        var keys=sprites.Select((s,i)=>new ObjectReferenceKeyframe{time=i/10f,value=s}).Concat(new[]{new ObjectReferenceKeyframe{time=sprites.Length/10f,value=sprites.Last()}}).ToArray();AnimationUtility.SetObjectReferenceCurve(clip,EditorCurveBinding.PPtrCurve("",typeof(SpriteRenderer),"m_Sprite"),keys);var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;AnimationUtility.SetAnimationClipSettings(clip,settings);EditorUtility.SetDirty(clip);return clip;
    }
    static RuntimeAnimatorController Controller()
    {
        string path=BossArt+"/AshKing.controller";var c=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);if(c!=null)return c;
        c=AnimatorController.CreateAnimatorControllerAtPath(path);c.AddParameter("Speed",AnimatorControllerParameterType.Float);foreach(var name in new[]{"Cleave","Explosion","Death"})c.AddParameter(name,AnimatorControllerParameterType.Trigger);
        var machine=c.layers[0].stateMachine;var idle=machine.AddState("Idle");idle.motion=Clip("Idle","Idle",true);machine.defaultState=idle;var run=machine.AddState("Run");run.motion=Clip("Run","Run",true);
        var moving=idle.AddTransition(run);moving.hasExitTime=false;moving.duration=0;moving.AddCondition(AnimatorConditionMode.Greater,.1f,"Speed");var stop=run.AddTransition(idle);stop.hasExitTime=false;stop.duration=0;stop.AddCondition(AnimatorConditionMode.Less,.1f,"Speed");
        foreach(var pair in new[]{new[]{"Cleave","Attack1"},new[]{"Explosion","Attack2"},new[]{"Death","Death"}}){var state=machine.AddState(pair[0]);state.motion=Clip(pair[0],pair[1],false);var t=machine.AddAnyStateTransition(state);t.hasExitTime=false;t.duration=0;t.canTransitionToSelf=false;t.AddCondition(AnimatorConditionMode.If,0,pair[0]);if(pair[0]!="Death"){var back=state.AddTransition(idle);back.hasExitTime=true;back.exitTime=1;back.duration=0;}}
        return c;
    }
    static QuestData Quest(string id,string title,string prior,float reward,params QuestObjective[] goals)
    {string path=Data+"/"+id+".asset";var q=AssetDatabase.LoadAssetAtPath<QuestData>(path);if(q==null){q=ScriptableObject.CreateInstance<QuestData>();AssetDatabase.CreateAsset(q,path);}q.questId=id;q.title=title;q.isleIndex=1;q.autoStart=true;q.prerequisiteQuestIds=new[]{prior};q.objectives=goals;q.summary=string.Join("\n",goals.Select(g=>"· "+g.description));q.onComplete.expReward=reward;EditorUtility.SetDirty(q);return q;}
    static QuestObjective Goal(ObjectiveType type,string id,string text)=>new QuestObjective{type=type,targetId=id,description=text,requiredCount=1};
    public static void Run()
    {
        try{
            Directory.CreateDirectory(BossArt);AssetDatabase.Refresh();
            var bossQuest=Quest(HubStoryIds.Boss,"그 불을 끝내라",HubStoryIds.Hunt(4),220,Goal(ObjectiveType.Reach,"hub_boss_arena","남쪽 재의 왕의 전투터 진입"),Goal(ObjectiveType.Kill,"AshKing","재의 왕 처치"));bossQuest.startLine="남쪽 길이 열렸다. 제단에서 정비하고 재의 왕에게 맞서자.";bossQuest.completeLine="재의 왕이 쓰러졌다. 현묵에게 돌아가 진실을 전하자.";
            var ending=Quest(HubStoryIds.Ending,"돌아온 기억",HubStoryIds.Boss,80,Goal(ObjectiveType.Talk,HubStoryIds.Elder,"현묵에게 첫 섬의 결말 보고"));ending.completeLine="재의 왕이 쓰러지고 마을에 기억이 돌아왔다. 소윤을 구한 네 선택도 잊지 않으마.\n\n첫 번째 장 완료\n다음 섬에서 천마의 흔적을 찾아야겠구나. 다음 섬은 아직 준비 중이다.";
            var fourth=AssetDatabase.LoadAssetAtPath<QuestData>(Data+"/hub_hunt_4.asset");fourth.completeLine="야영지의 흔적은 재의 왕을 가리킨다. 현묵은 진실을 인정했다. 남쪽 길이 열렸다. 제단에서 정비하고 그 불을 끝내자.";EditorUtility.SetDirty(fourth);
            quests=AssetDatabase.FindAssets("t:QuestData",new[]{Data}).Select(g=>AssetDatabase.LoadAssetAtPath<QuestData>(AssetDatabase.GUIDToAssetPath(g))).OrderBy(q=>q.questId).ToArray();
            var data=AssetDatabase.LoadAssetAtPath<MonsterData>(Data+"/AshKing.asset");if(data==null){data=ScriptableObject.CreateInstance<MonsterData>();AssetDatabase.CreateAsset(data,Data+"/AshKing.asset");}Set(data,"monsterName","AshKing");Set(data,"level",5);Set(data,"maxHP",14400f);Set(data,"attackDamage",22);Set(data,"attackRange",3.2f);Set(data,"attackCooldown",1.8f);Set(data,"moveSpeed",2.2f);Set(data,"aggroRange",30f);Set(data,"expReward",120f);
            var prefab=new GameObject("재의 왕");prefab.layer=6;prefab.transform.localScale=Vector3.one*3.2f;var render=prefab.AddComponent<SpriteRenderer>();render.sprite=Sprites("Idle").First();render.sortingOrder=1000;prefab.AddComponent<Animator>().runtimeAnimatorController=Controller();var body=prefab.AddComponent<Rigidbody2D>();body.gravityScale=0;body.constraints=RigidbodyConstraints2D.FreezeRotation;var hitbox=prefab.AddComponent<CapsuleCollider2D>();hitbox.size=new Vector2(.38f,.58f);hitbox.offset=new Vector2(0,.16f);var boss=prefab.AddComponent<AshKingBoss>();Set(boss,"monsterData",data);var asset=PrefabUtility.SaveAsPrefabAsset(prefab,BossArt+"/AshKing.prefab");UnityEngine.Object.DestroyImmediate(prefab);
            foreach(string sceneName in new[]{"Main","HuntingGround1","HuntingGround2","HuntingGround3","HuntingGround4"}){
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+sceneName+".unity");var qm=UnityEngine.Object.FindFirstObjectByType<QuestManager>();var serialized=new SerializedObject(qm);var list=serialized.FindProperty("quests");list.arraySize=quests.Length;for(int i=0;i<quests.Length;i++)list.GetArrayElementAtIndex(i).objectReferenceValue=quests[i];serialized.ApplyModifiedPropertiesWithoutUndo();
                if(sceneName=="Main"&&GameObject.Find("AshKingGate")==null){var root=new GameObject("AshKingGate");Vector2 center=UnityEngine.Object.FindFirstObjectByType<PlayerStats>().transform.position;Vector2 gate=center+new Vector2(0,-20);Hub("Portal",root.transform,gate,HubStoryIds.BossScene,GameFlow.DefaultSpawn,"재의 왕의 문",HubStoryIds.Hunt(4));Set(root.GetComponentInChildren<ScenePortal>(),"lockedMessage","야영지 의뢰를 끝내고 현묵에게 보고해야 한다.");Hub("Spawn",root.transform,HubStoryIds.BossReturn,center+new Vector2(0,-16));var road=(Tilemap)Hub("Roads",root.transform);Hub("Road",road,center,gate);}
                EditorSceneManager.SaveScene(scene);
            }
            Arena(asset.GetComponent<AshKingBoss>(),data);
            var scenes=EditorBuildSettings.scenes.ToList();if(!scenes.Any(s=>s.path=="Assets/Scenes/AshKingArena.unity"))scenes.Add(new EditorBuildSettingsScene("Assets/Scenes/AshKingArena.unity",true));EditorBuildSettings.scenes=scenes.ToArray();AssetDatabase.SaveAssets();EditorApplication.Exit(0);
        }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void Arena(AshKingBoss prefab,MonsterData data)
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/HuntingGround4.unity");
        prefab=AssetDatabase.LoadAssetAtPath<GameObject>(BossArt+"/AshKing.prefab").GetComponent<AshKingBoss>();
        data=AssetDatabase.LoadAssetAtPath<MonsterData>(Data+"/AshKing.asset");
        foreach(string name in new[]{"HubStory","RegionalLandmarks"}){var obj=GameObject.Find(name);if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);}
        var props=GameObject.Find("Props");if(props!=null)for(int i=props.transform.childCount-1;i>=0;i--)UnityEngine.Object.DestroyImmediate(props.transform.GetChild(i).gameObject);
        var player=UnityEngine.Object.FindFirstObjectByType<PlayerStats>();player.transform.position=new Vector3(0,-15,0);
        var root=new GameObject("AshKingEncounter");var arena=root.AddComponent<AshKingArena>();Set(arena,"bossPrefab",prefab);Set(arena,"bossData",data);
        Hub("Spawn",root.transform,GameFlow.DefaultSpawn,new Vector2(0,-15));Hub("Portal",root.transform,new Vector2(0,-19),"Main",HubStoryIds.BossReturn,"마을로 돌아가기","");root.AddComponent<HubAreaEntry>().areaId="hub_boss_entrance";
        var preparation=At("정비 제단",new Vector2(2,-15),root.transform);var stone=preparation.AddComponent<SpriteRenderer>();stone.sprite=AssetDatabase.LoadAllAssetsAtPath("Assets/testAsset/River/ground_decorations/rock2.png").OfType<Sprite>().First();stone.color=new Color(.6f,.9f,.8f);stone.sortingOrder=1150;preparation.transform.localScale=Vector3.one*2;Hub("Label",preparation.transform,"정비 제단 · E");Set(arena,"preparationPoint",preparation.transform);
        var gate=At("전투 봉쇄",new Vector2(0,-10),root.transform);gate.AddComponent<BoxCollider2D>().size=new Vector2(5,1);var sprite=gate.AddComponent<SpriteRenderer>();sprite.sprite=stone.sprite;sprite.color=new Color(1,.4f,.15f,.6f);gate.transform.localScale=Vector3.one;gate.SetActive(false);Set(arena,"battleGate",gate);
        var maps=UnityEngine.Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None);var ground=maps.First(t=>t.name=="Ground");var water=maps.First(t=>t.name=="Water");var path=maps.First(t=>t.name=="Path");var block=maps.First(t=>t.name=="Blockers");foreach(var map in new[]{ground,water,path,block})map.ClearAllTiles();var g=AssetDatabase.LoadAssetAtPath<TileBase>("Assets/MapTileSet/ground_textures/transitions/New Rule Tile.asset");var p=AssetDatabase.LoadAssetAtPath<TileBase>("Assets/MapTileSet/ground_textures/transitions/New Rule Tile 1.asset");var w=AssetDatabase.LoadAssetAtPath<TileBase>("Assets/MapTileSet/ground_textures/water/riverRule_Static.asset");var b=AssetDatabase.LoadAssetAtPath<TileBase>("Assets/MapGen/BlockTile.asset");
        for(int x=-16;x<=16;x++)for(int y=-23;y<=13;y++){var cell=new Vector3Int(x,y,0);bool floor=Mathf.Abs(x)<=12&&y>=-9&&y<=9||Mathf.Abs(x)<=2&&y>=-21&&y<=-9;if(floor){ground.SetTile(cell,g);if(Mathf.Abs(x)==11||Mathf.Abs(y)==8||Mathf.Abs(x)<=1&&y<-8)path.SetTile(cell,p);}else{water.SetTile(cell,w);block.SetTile(cell,b);}}ground.color=new Color(.63f,.56f,.5f);path.color=new Color(.86f,.6f,.4f);
        GateVisual(gate.transform);Hud(arena,GameObject.Find("UIRoot").transform);EditorSceneManager.SaveScene(scene,"Assets/Scenes/AshKingArena.unity");
    }
    public static void RefineArena()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/AshKingArena.unity");
        var arena=UnityEngine.Object.FindFirstObjectByType<AshKingArena>();
        Set(arena,"bossPrefab",AssetDatabase.LoadAssetAtPath<GameObject>(BossArt+"/AshKing.prefab").GetComponent<AshKingBoss>());
        Set(arena,"bossData",AssetDatabase.LoadAssetAtPath<MonsterData>(Data+"/AshKing.asset"));
        var gate=arena.transform.Find("전투 봉쇄");
        GateVisual(gate);
        EditorSceneManager.SaveScene(scene);EditorApplication.Exit(0);
    }
    static void GateVisual(Transform gate)
    {
        var renderer=gate.GetComponent<SpriteRenderer>();if(renderer==null)return;
        var visual=new GameObject("봉쇄 표시");visual.transform.SetParent(gate,false);
        var line=visual.AddComponent<SpriteRenderer>();line.sprite=renderer.sprite;line.color=renderer.color;line.sortingOrder=1100;
        visual.transform.localScale=new Vector3(5/line.sprite.bounds.size.x,.5f/line.sprite.bounds.size.y,1);
        UnityEngine.Object.DestroyImmediate(renderer);
    }
    static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position)
    {var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);var rect=(RectTransform)go.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.pivot=new Vector2(.5f,1);rect.sizeDelta=size;rect.anchoredPosition=position;return rect;}
    static TMP_Text Text(string name,Transform parent,string content,int font,Vector2 position)
    {var rect=Rect(name,parent,new Vector2(550,26),position);var t=rect.gameObject.AddComponent<TextMeshProUGUI>();t.font=Resources.Load<TMP_FontAsset>("Fonts/NeoDunggeunmoPro SDF");t.text=content;t.fontSize=font;t.alignment=TextAlignmentOptions.Center;t.raycastTarget=false;return t;}
    static void Hud(AshKingArena arena,Transform parent)
    {
        var canvas=Rect("보스 HUD",parent,new Vector2(1920,1080),Vector2.zero);var c=canvas.gameObject.AddComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=30;var scaler=canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        var panel=Rect("재의 왕",canvas,new Vector2(560,95),new Vector2(0,-22));var bg=panel.gameObject.AddComponent<Image>();bg.color=new Color(.045f,.04f,.04f,.9f);bg.raycastTarget=false;var title=Text("이름",panel,"재의 왕",22,new Vector2(0,-7));var bar=Rect("체력 배경",panel,new Vector2(520,12),new Vector2(0,-39));bar.gameObject.AddComponent<Image>().color=new Color(.2f,.1f,.08f);var fill=Rect("체력",bar,new Vector2(520,12),Vector2.zero).gameObject.AddComponent<Image>();fill.color=new Color(.75f,.23f,.12f);fill.type=Image.Type.Filled;fill.fillMethod=Image.FillMethod.Horizontal;fill.rectTransform.anchorMin=fill.rectTransform.anchorMax=new Vector2(0,1);fill.rectTransform.pivot=new Vector2(0,1);fill.rectTransform.anchoredPosition=Vector2.zero;fill.raycastTarget=false;var hint=Text("패턴",panel,"",17,new Vector2(0,-61));var hud=canvas.gameObject.AddComponent<AshKingHud>();Set(hud,"arena",arena);Set(hud,"panel",panel.gameObject);Set(hud,"healthFill",fill);Set(hud,"title",title);Set(hud,"hint",hint);
    }
}


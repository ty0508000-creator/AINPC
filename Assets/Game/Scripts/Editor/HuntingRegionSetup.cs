using System;
using System.Linq;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class HuntingRegionSetup
{
    static void Set(UnityEngine.Object o,string key,object v) { o.GetType().GetField(key,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).SetValue(o,v); EditorUtility.SetDirty(o); }
    public static void Run()
    {
        try {
            Directory.CreateDirectory("Assets/Game/Prefabs/RegionalEnemies"); AssetDatabase.Refresh();
            string[] species={"Mushroom","Bat","Skeleton","Goblin"};
            for(int n=1;n<=4;n++) {
                string prefab="Assets/Game/Prefabs/RegionalEnemies/Region"+n+".prefab";
                var source=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Monsters/"+species[n-1]+".prefab");
                UnityEngine.Object.DestroyImmediate(source.GetComponent<MonsterBase>());
                var monster=source.AddComponent<RegionalMonster>(); Set(monster,"style",(RegionalMonster.Style)(n-1)); HuntingBalanceSetup.ConfigureMonster(n,monster);
                PrefabUtility.SaveAsPrefabAsset(source,prefab); PrefabUtility.UnloadPrefabContents(source);
                var data=AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/Game/Data/HubStory/Enemy"+n+".asset");
                HuntingBalanceSetup.ConfigureData(n,data);
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/HuntingGround"+n+".unity");
                var area=UnityEngine.Object.FindFirstObjectByType<MonsterSpawnArea>(); Set(area,"monsterPrefab",AssetDatabase.LoadAssetAtPath<GameObject>(prefab).GetComponent<RegionalMonster>()); HuntingBalanceSetup.ConfigureSpawner(n,area);
                Terrain(n);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets(); Directory.CreateDirectory("VerificationResults/Regions");
            File.WriteAllText("VerificationResults/Regions/setup.txt","PASS: four region terrains and attack prefabs authored; entrance and all original objective coordinates connected.");
            EditorApplication.Exit(0);
        } catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static TileBase Tile(string path)=>AssetDatabase.LoadAssetAtPath<TileBase>(path);
    static void Terrain(int n)
    {
        var maps=UnityEngine.Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
        Tilemap ground=maps.First(x=>x.name=="Ground"), water=maps.First(x=>x.name=="Water"), path=maps.First(x=>x.name=="Path"), block=maps.First(x=>x.name=="Blockers");
        TileBase g=Tile("Assets/MapTileSet/ground_textures/transitions/New Rule Tile.asset"), p=Tile("Assets/MapTileSet/ground_textures/transitions/New Rule Tile 1.asset"), w=Tile("Assets/MapTileSet/ground_textures/water/riverRule_Static.asset"), b=Tile("Assets/MapGen/BlockTile.asset");
        if(g==null||p==null||w==null||b==null)throw new Exception("Missing terrain tiles");
        foreach(var map in new[]{ground,water,path,block})map.ClearAllTiles();
        // Preserve all quest and spawn coordinates; wide connecting lanes stay free of collision.
        Func<int,int,bool> lane=(x,y)=>Mathf.Abs(x)<=3 || Mathf.Abs(y+5)<=3 || Mathf.Abs(y-8)<=3;
        for(int x=-32;x<32;x++)for(int y=-28;y<28;y++) {
            var cell=new Vector3Int(x,y,0); bool edge=Mathf.Abs(x)>=28||y>=24||y<=-27;
            bool river=n==2&&Mathf.Abs(x-(int)(5*Mathf.Sin(y*.12f)))<=3;
            bool wet=edge||river&&!lane(x,y);
            if(wet){water.SetTile(cell,w);block.SetTile(cell,b);}else ground.SetTile(cell,g);
            bool road=lane(x,y)&&!edge;
            if(n==1)road=!edge&&(Mathf.Abs(x-(int)(8*Mathf.Sin(y*.16f)))<=2 || Mathf.Abs(y+5)<=2 || Mathf.Abs(y-8)<=2 || Mathf.Abs(x)<=2&&y<=-17);
            if(road)path.SetTile(cell,p);
            bool ruin=n==3&&y>-12&&y<20&&(Mathf.Abs(x)==16||Mathf.Abs(x)==6)&&(y%12>3)&&!lane(x,y);
            bool camp=n==4&&y>-12&&y<21&&Mathf.Abs(x)==19&&Mathf.Abs(y-8)>3&&Mathf.Abs(y+5)>3;
            if(ruin||camp)block.SetTile(cell,b);
        }
        Color[] tones={new Color(.78f,.84f,.72f),new Color(.82f,.96f,.92f),new Color(.76f,.73f,.69f),new Color(.95f,.81f,.68f)};
        ground.color=tones[n-1]; path.color=tones[n-1];
        // Remove previous random vegetation only; story objects and baked UI retain their identities.
        var props=GameObject.Find("Props");if(props!=null)for(int i=props.transform.childCount-1;i>=0;i--)UnityEngine.Object.DestroyImmediate(props.transform.GetChild(i).gameObject);
        var previous=GameObject.Find("RegionalLandmarks");if(previous!=null)UnityEngine.Object.DestroyImmediate(previous); var landmarks=new GameObject("RegionalLandmarks");
        string art=n==1?"Plants/tree1.png":n==2?"Plants/water_reeds1.png":n==3?"ground_decorations/old_fence1.png":"ground_decorations/old_trunk1.png";
        var sprite=AssetDatabase.LoadAllAssetsAtPath("Assets/testAsset/River/"+art).OfType<Sprite>().FirstOrDefault();
        if(sprite==null)sprite=AssetDatabase.LoadAllAssetsAtPath("Assets/testAsset/River/ground_decorations/rock1.png").OfType<Sprite>().First();
        for(int x=-24;x<=24;x+=n==1?4:6)for(int y=-15;y<=20;y+=5) {
            if(path.HasTile(new Vector3Int(x,y,0))||lane(x,y)||n==2&&Mathf.Abs(x-(int)(5*Mathf.Sin(y*.12f)))<5)continue;
            if(n==3&&Mathf.Abs(x)!=6&&Mathf.Abs(x)!=18)continue;
            if(n==4&&Mathf.Abs(x)<15)continue;
            var obj=new GameObject(n==1?"숲 나무":n==2?"강가 갈대":n==3?"무너진 울타리":"야영지 목책"); obj.transform.SetParent(landmarks.transform); obj.transform.position=new Vector3(x,y,0);
            var r=obj.AddComponent<SpriteRenderer>();r.sprite=sprite;r.color=tones[n-1];obj.transform.localScale=Vector3.one*(n>=3?3f:n==2?2f:1f);r.sortingOrder=1000+Mathf.RoundToInt(-y*10);
            if(n!=2)block.SetTile(new Vector3Int(x,y,0),b);
        }
        // Invisible ruin collision is marked with visible fence pieces.
        foreach(var cell in block.cellBounds.allPositionsWithin)if(block.HasTile(cell)&&!water.HasTile(cell)&&n>=3) {
            var obj=new GameObject("장애물");obj.transform.SetParent(landmarks.transform);obj.transform.position=cell;var r=obj.AddComponent<SpriteRenderer>();r.sprite=sprite;r.color=tones[n-1];obj.transform.localScale=Vector3.one*(n>=3?3f:n==2?2f:1f);r.sortingOrder=1000-cell.y*10;
        }
        Physics2D.SyncTransforms();
        var seen=new System.Collections.Generic.HashSet<Vector2Int>();var queue=new System.Collections.Generic.Queue<Vector2Int>();queue.Enqueue(new Vector2Int(0,-20));
        while(queue.Count>0){var c=queue.Dequeue();if(!seen.Add(c))continue;foreach(var d in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right}){var z=c+d;if(z.x<-31||z.x>30||z.y<-26||z.y>23||block.HasTile(new Vector3Int(z.x,z.y,0))||seen.Contains(z))continue;queue.Enqueue(z);}}
        foreach(var target in new[]{new Vector2Int(0,-24),new Vector2Int(-9,-5),new Vector2Int(9,-5),new Vector2Int(-9,8),new Vector2Int(9,8),new Vector2Int(0,12),new Vector2Int(0,15)})if(!seen.Contains(target))throw new Exception("Unreachable target "+n+":"+target);
    }
}





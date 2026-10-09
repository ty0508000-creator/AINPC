using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>사냥터 수치의 제작 기준. 씬은 스폰 관련 필드만 수정한다.</summary>
public static class HuntingBalanceSetup
{
    public static readonly float[] Health = {30,48,85,120};
    public static readonly int[] Damage = {5,8,12,16};
    public static readonly float[] Experience = {12,16,24,30};
    public static readonly float[] QuestExperience = {80,150,140,180};
    public static readonly float[] Range = {1.8f,5.5f,2.1f,4.2f};
    public static readonly float[] Speed = {1.15f,2,1.4f,2.2f};
    public static readonly float[] Cooldown = {3.2f,2.8f,2.6f,2.4f};
    public static readonly float[] Windup = {.9f,.85f,.9f,1};
    public static readonly float[] Recovery = {.9f,.75f,.9f,1.1f};
    public static readonly float[] Respawn = {25,30,35,40};
    static void Set(UnityEngine.Object obj,string field,float value)
    {
        var serialized=new SerializedObject(obj);var property=serialized.FindProperty(field);
        if(property==null)throw new Exception("Unknown balance field: "+field);
        if(property.propertyType==SerializedPropertyType.Integer)property.intValue=(int)value;else property.floatValue=value;
        serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(obj);
    }
    public static void ConfigureData(int region,MonsterData data)
    {
        int i=region-1;Set(data,"level",region);Set(data,"maxHP",Health[i]);Set(data,"attackDamage",Damage[i]);Set(data,"expReward",Experience[i]);Set(data,"attackRange",Range[i]);Set(data,"moveSpeed",Speed[i]);Set(data,"attackCooldown",Cooldown[i]);Set(data,"aggroRange",region==2?8:7);
    }
    public static void ConfigureMonster(int region,RegionalMonster monster)
    {Set(monster,"windup",Windup[region-1]);Set(monster,"recoveryDuration",Recovery[region-1]);}
    public static void ConfigureSpawner(int region,MonsterSpawnArea area)
    {Set(area,"spawnInterval",5);Set(area,"minimumRespawnDelay",Respawn[region-1]);}
    public static void Run()
    {
        try{
            for(int n=1;n<=4;n++){
                ConfigureData(n,AssetDatabase.LoadAssetAtPath<MonsterData>("Assets/Game/Data/HubStory/Enemy"+n+".asset"));
                var quest=AssetDatabase.LoadAssetAtPath<QuestData>("Assets/Game/Data/HubStory/hub_hunt_"+n+".asset");quest.onComplete.expReward=QuestExperience[n-1];EditorUtility.SetDirty(quest);
                string prefab="Assets/Game/Prefabs/RegionalEnemies/Region"+n+".prefab";var root=PrefabUtility.LoadPrefabContents(prefab);
                ConfigureMonster(n,root.GetComponent<RegionalMonster>());PrefabUtility.SaveAsPrefabAsset(root,prefab);PrefabUtility.UnloadPrefabContents(root);
                string scene="Assets/Scenes/HuntingGround"+n+".unity";string yaml=File.ReadAllText(scene);
                if(Regex.Matches(yaml,@"(?m)^  spawnInterval: .*$").Count!=1)throw new Exception("Unexpected spawn areas in "+scene);
                yaml=Regex.Replace(yaml,@"(?m)^  spawnInterval: [^\r\n]*","  spawnInterval: 5");
                if(yaml.Contains("  minimumRespawnDelay:"))yaml=Regex.Replace(yaml,@"(?m)^  minimumRespawnDelay: [^\r\n]*","  minimumRespawnDelay: "+Respawn[n-1]);
                else yaml=Regex.Replace(yaml,@"(?m)^  spawnOnStart:","  minimumRespawnDelay: "+Respawn[n-1]+"\n  spawnOnStart:");
                File.WriteAllText(scene,yaml);
            }
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();EditorApplication.Exit(0);
        }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
}


using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>씬 전체 재저장 없이 단축키 표시와 슬롯 상태만 내보낸다.</summary>
public static class SkillKeyboardSetup
{
    [Serializable] public class FieldPatch { public string scene,id,text; public bool button; }
    [Serializable] public class Patches { public List<FieldPatch> entries=new List<FieldPatch>(); }
    public static void ExportAndVerify()
    {
        try {
            var patches=new Patches();
            foreach(string guid in AssetDatabase.FindAssets("t:Scene",new[]{"Assets/Scenes"})) {
                string path=AssetDatabase.GUIDToAssetPath(guid);EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                foreach(var ui in UnityEngine.Object.FindObjectsByType<RpgUI>(FindObjectsInactive.Include,FindObjectsSortMode.None)) {
                    ui.BakeKeyboardShortcuts();
                }
                foreach(var button in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include,FindObjectsSortMode.None)) {
                    // 키 표시가 있는 하단 슬롯만 수집한다. 학습·메뉴 버튼은 제외한다.
                    var labels=button.GetComponentsInChildren<TMP_Text>(true);
                    foreach(var text in labels)
                        if(text.text=="Q"||text.text=="R"||text.text=="F"||text.text=="Z") {
                            patches.entries.Add(new FieldPatch{scene=path,id=GlobalObjectId.GetGlobalObjectIdSlow(text).targetObjectId.ToString(),text=text.text});
                            patches.entries.Add(new FieldPatch{scene=path,id=GlobalObjectId.GetGlobalObjectIdSlow(button).targetObjectId.ToString(),button=true});
                        }
                    foreach(var text in labels)if(text.text.Contains("이기어검")&&text.text.Contains("Z"))
                        patches.entries.Add(new FieldPatch{scene=path,id=GlobalObjectId.GetGlobalObjectIdSlow(text).targetObjectId.ToString(),text=text.text});
                }
            }
            Directory.CreateDirectory("VerificationResults/SkillKeyboard");File.WriteAllText("VerificationResults/SkillKeyboard/scene-patches.json",JsonUtility.ToJson(patches,true));
            RpgVerification.Run();EditorApplication.Exit(File.ReadAllText("VerificationResults/RpgVerification/result.txt").StartsWith("PASS")?0:1);
        } catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
}

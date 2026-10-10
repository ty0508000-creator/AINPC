using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

[InitializeOnLoad]
public static class SkillKeyboardVerification
{
    const string Pending="SkillKeyboard.Pending", Slot="SkillKeyboard.Slot";
    static Keyboard keyboard; static PlayerStats stats; static RpgSkillController skills;
    static int stage, checks; static double due; static float mana, y;
    static InputSettings.BackgroundBehavior oldBackground;
    static InputSettings.EditorInputBehaviorInPlayMode oldEditorInput;
    static bool oldRunInBackground;
    static SkillKeyboardVerification() {
        EditorApplication.playModeStateChanged+=Changed;
        if(SessionState.GetBool(Pending,false))SaveSystem.VerificationDirectory=SessionState.GetString(Slot,"");
    }
    public static void Run() {
        string slot=Path.GetFullPath("VerificationResults/SkillKeyboard/slot-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(slot);
        SessionState.SetString(Slot,slot);SessionState.SetBool(Pending,true);SaveSystem.VerificationDirectory=slot;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state) {
        if(!SessionState.GetBool(Pending,false))return;
        if(state==PlayModeStateChange.EnteredPlayMode) {
            oldRunInBackground=Application.runInBackground;Application.runInBackground=true;
            oldBackground=InputSystem.settings.backgroundBehavior;oldEditorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard=InputSystem.AddDevice<Keyboard>();keyboard.MakeCurrent();
            var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            player.AddComponent<Player_Attack>();stats=player.AddComponent<PlayerStats>();skills=player.GetComponent<RpgSkillController>();
            player.GetComponent<RpgUI>().BakeSceneUI();player.GetComponent<RpgUI>().BakeUltimateWidgets();
            stats.BaseMaxMana=150;stats.RestoreMana(150);foreach(int id in new[]{0,3,6,9})stats.SkillRanks[id]=1;
            stage=checks=0;due=EditorApplication.timeSinceStartup+.75;EditorApplication.update+=Tick;
        }
        if(state==PlayModeStateChange.EnteredEditMode) {
            SessionState.SetBool(Pending,false);SaveSystem.VerificationDirectory=null;
            EditorApplication.Exit(SessionState.GetInt("SkillKeyboard.Exit",1));
        }
    }
    static void Check(bool pass,string message) {if(!pass)throw new Exception(message);checks++;Debug.Log("SKILL_KEY_CHECK: "+message);}
    static void Press(params Key[] keys) {InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));due=EditorApplication.timeSinceStartup+.2;}
    static void Tick() {
        if(EditorApplication.timeSinceStartup<due)return;
        try {
            switch(stage++) {
                case 0: mana=stats.Mana;y=stats.transform.position.y;Press(Key.W,Key.Q);break;
                case 1:
                    Debug.Log($"Keyboard fixture: enabled={keyboard.enabled}, current={Keyboard.current==keyboard}, Q={keyboard.qKey.isPressed}, alive={stats.IsAlive}, rank={stats.SkillRanks[0]}, mana={stats.Mana}, remaining={skills.Remaining(0)}, open={RpgUI.IsOpen}, moved={stats.transform.position.y-y}");
                    Check(skills.Remaining(0)>0&&stats.Mana<mana-8,"Q casts MoonSlash through real keyboard input");
                    Check(stats.transform.position.y>y+.1f,"WASD movement continues while casting Q");mana=stats.Mana;Press(Key.W,Key.Q);break;
                case 2: Check(stats.Mana>=mana,"holding Q does not auto repeat");Press(Key.R);break;
                case 3: Check(skills.GuardRemaining>0,"R casts guard");stats.TakeDamage(20);Check(stats.HP<stats.MaxHP,"healing fixture damaged");Press(Key.F);break;
                case 4: Check(stats.HP==stats.MaxHP&&skills.Remaining(6)>0,"F casts recovery");Press(Key.Z);break;
                case 5:
                    Check(skills.UltimateRemaining>0,"Z summons ultimate");Press();
                    skills.ResetForRecovery();mana=stats.Mana;Press(Key.Digit1,Key.Digit2,Key.Digit3,Key.Digit4);break;
                case 6:
                    Check(skills.GuardRemaining==0&&skills.UltimateRemaining==0&&skills.Remaining(0)==0,"old number keys do not cast");
                    var buttons=(Button[])typeof(RpgUI).GetField("hotButtons",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(stats.GetComponent<RpgUI>());
                    foreach(var button in buttons){button.onClick.Invoke();Check(!button.interactable,"HUD skill slot cannot cast on click");}
                    var ultimate=(Button)typeof(RpgUI).GetField("ultimateButton",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(stats.GetComponent<RpgUI>());
                    ultimate.onClick.Invoke();Check(!ultimate.interactable&&skills.UltimateRemaining==0&&skills.GuardRemaining==0,"ultimate HUD click does not cast");
                    stats.GetComponent<RpgUI>().Open(true);Press(Key.R);break;
                case 7:
                    Check(skills.GuardRemaining==0,"cultivation menu blocks keyboard skills");
                    typeof(RpgUI).GetMethod("Close",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(stats.GetComponent<RpgUI>(),null);
                    Time.timeScale=0;Press(Key.Q,Key.F,Key.Z);break;
                case 8:
                    Check(skills.Remaining(0)==0&&skills.UltimateRemaining==0,"pause blocks keyboard skills");Time.timeScale=1;Press();break;
                case 9: stats.TakeDamage(100000);Check(!stats.IsAlive,"death fixture");mana=stats.Mana;Press(Key.R);break;
                case 10:
                    Check(stats.IsAlive,"R retries while dead");Check(skills.GuardRemaining==0&&stats.Mana==stats.MaxMana,"retry R cannot also spend mana or cast guard");Press();break;
                case 11: Press(Key.R);break;
                case 12: Check(skills.GuardRemaining>0,"fresh R press casts guard after retry");Finish(true,"");break;
            }
        } catch(Exception ex){Debug.LogException(ex);Finish(false,ex.Message);}
    }
    static void Finish(bool ok,string message) {
        EditorApplication.update-=Tick;Time.timeScale=1;if(keyboard!=null)InputSystem.RemoveDevice(keyboard);
        InputSystem.settings.backgroundBehavior=oldBackground;InputSystem.settings.editorInputBehaviorInPlayMode=oldEditorInput;Application.runInBackground=oldRunInBackground;
        string output=Path.GetFullPath("VerificationResults/SkillKeyboard");Directory.CreateDirectory(output);
        string result=(ok?"PASS ":"FAIL ")+checks+" checks "+message;File.WriteAllText(Path.Combine(output,"result.txt"),result);
        Debug.Log("SKILL_KEY_VERIFY_"+result);SessionState.SetInt("SkillKeyboard.Exit",ok?0:1);EditorApplication.ExitPlaymode();
    }
}

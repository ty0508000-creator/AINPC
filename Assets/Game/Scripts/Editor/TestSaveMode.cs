using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// "테스트 세이브로 플레이" 스위치. 켜고 플레이하면 그때마다 실제 세이브(기억 파일 포함)를 사본 폴더로 복사하고,
/// 그 사본으로 플레이한다. 실제 진행 상태에서 바로 시험할 수 있고, 실제 세이브는 건드리지 않는다.
/// 사본은 다음 플레이 때 다시 덮어쓴다. 설정은 이 PC 의 EditorPrefs 에만 남는다.
/// </summary>
[InitializeOnLoad]
public static class TestSaveMode
{
    const string MenuPath = "Tools/AINPC/테스트 세이브로 플레이";

    static TestSaveMode()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(SaveSystem.TestSavePref);
        set => EditorPrefs.SetBool(SaveSystem.TestSavePref, value);
    }

    [MenuItem(MenuPath)]
    static void Toggle()
    {
        Enabled = !Enabled;
        Debug.Log(Enabled
            ? "[테스트 세이브] 켜짐. 플레이할 때마다 실제 세이브를 복사한 사본으로 플레이합니다: " + SaveSystem.TestSaveDirectory
            : "[테스트 세이브] 꺼짐. 실제 세이브로 플레이합니다.");
    }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return !EditorApplication.isPlayingOrWillChangePlaymode;   // 플레이 중에 바꾸면 세이브가 두 폴더로 갈린다
    }

    [MenuItem("Tools/AINPC/테스트 세이브 폴더 열기")]
    static void Reveal()
    {
        Directory.CreateDirectory(SaveSystem.TestSaveDirectory);
        EditorUtility.RevealInFinder(SaveSystem.TestSaveDirectory);
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (!Enabled) return;
        // 씬이 뜨기 전(자동 저장 전)에 사본을 만든다
        if (state == PlayModeStateChange.ExitingEditMode) CopyRealSave();
        // Clear on Play 로 위 로그가 지워져도 보이게 한 번 더 알린다
        if (state == PlayModeStateChange.EnteredPlayMode)
            Debug.LogWarning("[테스트 세이브] 실제 세이브가 아닌 사본으로 플레이 중입니다: " + SaveSystem.TestSaveDirectory);
    }

    /// <summary>실제 세이브 폴더의 파일과 Memory 폴더를 사본 폴더로 복사한다. 사본 폴더는 비우고 시작한다.</summary>
    public static void CopyRealSave()
    {
        string real = Application.persistentDataPath, test = SaveSystem.TestSaveDirectory;
        // 사본 폴더만 지운다. 경로가 예상과 다르면 아무것도 지우지 않는다.
        if (Path.GetFileName(test) == "EditorTestSave" && Directory.Exists(test)) Directory.Delete(test, true);
        Directory.CreateDirectory(test);
        foreach (string file in Directory.GetFiles(real))
            File.Copy(file, Path.Combine(test, Path.GetFileName(file)));
        string memory = Path.Combine(real, "Memory");
        if (Directory.Exists(memory))
        {
            string target = Path.Combine(test, "Memory");
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(memory))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        Debug.Log("[테스트 세이브] 실제 세이브를 복사했습니다: " + test);
    }
}

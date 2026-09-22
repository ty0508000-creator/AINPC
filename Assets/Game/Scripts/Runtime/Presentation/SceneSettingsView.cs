using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>씬에 배치된 설정 위젯의 값과 입력만 관리한다.</summary>
public class SceneSettingsView : MonoBehaviour
{
    public Slider volume;
    public TMP_Text volumeLabel, fullscreenLabel, autoSaveLabel;
    public Button fullscreen, autoSave;
    public Button[] resolutions = new Button[3];
    public Button resetAccount, quit;
    public TMP_Text resetAccountLabel;
    static readonly Vector2Int[] sizes = { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080) };

    /// <summary>초기화는 되돌릴 수 없으므로 한 번 더 누르게 한다.</summary>
    const float ConfirmWindow = 4f;
    float confirmUntil;

    void Start()
    {
        volume.onValueChanged.AddListener(value =>
        {
            AudioListener.volume = value;
            PlayerPrefs.SetFloat("settings.volume", value); PlayerPrefs.Save();
            Refresh();
        });
        fullscreen.onClick.AddListener(() =>
        {
            bool next = !Screen.fullScreen;
            Screen.fullScreen = next;
            PlayerPrefs.SetInt("settings.fullscreen", next ? 1 : 0); PlayerPrefs.Save();
            fullscreenLabel.text = next ? "켜짐" : "꺼짐";
        });
        autoSave.onClick.AddListener(() => { GameFlow.AutoSaveEnabled = !GameFlow.AutoSaveEnabled; Refresh(); });
        for (int i = 0; i < resolutions.Length; i++)
        {
            Vector2Int size = sizes[i];
            resolutions[i].onClick.AddListener(() =>
            {
                Screen.SetResolution(size.x, size.y, Screen.fullScreen);
                PlayerPrefs.SetInt("settings.width", size.x); PlayerPrefs.SetInt("settings.height", size.y); PlayerPrefs.Save();
            });
        }
        if (resetAccount != null) resetAccount.onClick.AddListener(ResetAccount);
        if (quit != null) quit.onClick.AddListener(Quit);
        Refresh();
    }

    void Update()
    {
        // 확인 시간이 지나면 원래 문구로 돌아간다
        if (confirmUntil > 0f && Time.unscaledTime > confirmUntil) CancelConfirm();
    }

    void ResetAccount()
    {
        if (Time.unscaledTime > confirmUntil)
        {
            confirmUntil = Time.unscaledTime + ConfirmWindow;
            if (resetAccountLabel != null) resetAccountLabel.text = "정말 지울까요?";
            return;
        }

        CancelConfirm();
        SaveSystem.DeleteSave();
        // 메뉴를 먼저 닫아 시간을 되돌린 뒤 타이틀로 나간다
        FindFirstObjectByType<PauseMenuUI>(FindObjectsInactive.Include)?.Close();
        GameFlow.Instance.ReturnToTitle();
    }

    void CancelConfirm()
    {
        confirmUntil = 0f;
        if (resetAccountLabel != null) resetAccountLabel.text = "계정 초기화";
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnEnable() { if (volume != null) Refresh(); }
    void OnDisable() { CancelConfirm(); }
    void Refresh()
    {
        volume.SetValueWithoutNotify(AudioListener.volume);
        volumeLabel.text = Mathf.RoundToInt(AudioListener.volume * 100) + "%";
        fullscreenLabel.text = Screen.fullScreen ? "켜짐" : "꺼짐";
        autoSaveLabel.text = GameFlow.AutoSaveEnabled ? "켜짐" : "꺼짐";
    }
}

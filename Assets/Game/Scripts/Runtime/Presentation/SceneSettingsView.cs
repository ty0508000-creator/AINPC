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
    static readonly Vector2Int[] sizes = { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080) };

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
        Refresh();
    }

    void OnEnable() { if (volume != null) Refresh(); }
    void Refresh()
    {
        volume.SetValueWithoutNotify(AudioListener.volume);
        volumeLabel.text = Mathf.RoundToInt(AudioListener.volume * 100) + "%";
        fullscreenLabel.text = Screen.fullScreen ? "켜짐" : "꺼짐";
        autoSaveLabel.text = GameFlow.AutoSaveEnabled ? "켜짐" : "꺼짐";
    }
}

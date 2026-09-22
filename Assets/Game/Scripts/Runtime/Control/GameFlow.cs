using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 씬 전환과 진행 상태를 맡는다. 타이틀에서 시작해 마을·던전을 오가고,
/// 씬이 바뀔 때마다 지정한 입구(<see cref="SpawnPoint"/>)로 플레이어를 옮기고 진행을 저장한다.
///
/// 전환 효과와 알림 UI는 각 플레이 씬의 UIRoot 아래에 배치한다.
/// </summary>
public class GameFlow : MonoBehaviour
{
    /// <summary>타이틀 씬 이름.</summary>
    public const string TitleScene = "Title";

    /// <summary>입구를 따로 지정하지 않았을 때 찾는 이름.</summary>
    public const string DefaultSpawn = "default";

    private static GameFlow instance;

    /// <summary>없으면 만들어서 돌려준다.</summary>
    public static GameFlow Instance
    {
        get
        {
            if (instance != null)
                return instance;

            instance = FindFirstObjectByType<GameFlow>();
            if (instance == null)
            {
                var go = new GameObject("GameFlow");
                instance = go.AddComponent<GameFlow>();
            }

            return instance;
        }
    }

    /// <summary>
    /// 씬을 직접 실행해도(에디터에서 던전 씬만 열고 플레이) 메뉴창과 씬 전환이 준비되게 한다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GameFlow ready = Instance;
        if (ready == null)
            Debug.LogWarning("[GameFlow] 준비하지 못했습니다.");
    }

    /// <summary>씬을 넘어가는 중인가. 이동/입력을 막을 때 쓴다.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>다음 씬에서 플레이어를 놓을 입구 이름.</summary>
    private string pendingSpawn = DefaultSpawn;

    /// <summary>보스 처치 같은 진행 표시. 세이브에 같이 들어간다.</summary>
    private readonly HashSet<string> flags = new HashSet<string>();

    private CanvasGroup fade;
    [SerializeField] private float fadeDuration = 0.35f;

    /// <summary>자동 저장 간격(초).</summary>
    public const float AutoSaveInterval = 60f;

    private const string AutoSaveKey = "settings.autosave";

    /// <summary>자동 저장을 쓸지. 설정 메뉴에서 끌 수 있다.</summary>
    public static bool AutoSaveEnabled
    {
        get => PlayerPrefs.GetInt(AutoSaveKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(AutoSaveKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    private float autoSaveTimer;
    private TMP_Text notice;
    private float noticeTimer;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        BindSceneView();

        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    void OnDestroy()
    {
        if (instance == this)
            SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    void Update()
    {
        TickAutoSave();
        TickNotice();
    }

    /// <summary>일정 시간마다 알아서 저장한다. 메뉴를 보거나 죽어 있을 때는 미룬다.</summary>
    private void TickAutoSave()
    {
        if (!AutoSaveEnabled || IsLoading || PauseMenuUI.IsOpen)
            return;

        // 메뉴에서 시간이 멈춰도 흘러가야 하므로 unscaled 로 센다
        autoSaveTimer += Time.unscaledDeltaTime;
        if (autoSaveTimer < AutoSaveInterval)
            return;

        autoSaveTimer = 0f;

        var stats = FindFirstObjectByType<PlayerStats>();
        if (stats == null || !stats.IsAlive)
            return;   // 타이틀 화면이거나 죽어 있으면 이번 차례는 거른다

        SaveNow();
        ShowNotice("자동 저장됨");
    }

    private void TickNotice()
    {
        if (notice == null || noticeTimer <= 0f)
            return;

        noticeTimer -= Time.unscaledDeltaTime;

        // 마지막 0.6초 동안 서서히 사라진다
        Color c = notice.color;
        c.a = Mathf.Clamp01(noticeTimer / 0.6f);
        notice.color = c;

        if (noticeTimer <= 0f)
            notice.text = string.Empty;
    }

    /// <summary>화면 구석에 잠깐 뜨는 알림.</summary>
    public void ShowNotice(string message, float duration = 2f)
    {
        if (notice == null)
            return;

        notice.text = message;
        noticeTimer = duration;

        Color c = notice.color;
        c.a = 1f;
        notice.color = c;
    }

    // ── 진행 표시 ───────────────────────────────────────────────

    public bool HasFlag(string id) => !string.IsNullOrEmpty(id) && flags.Contains(id);

    public void SetFlag(string id)
    {
        if (string.IsNullOrEmpty(id) || !flags.Add(id))
            return;

        Debug.Log("[GameFlow] 진행 표시: " + id);
        SaveNow();
    }

    /// <summary>세이브에서 읽어온 진행 표시를 채운다.</summary>
    public void RestoreFlags(IEnumerable<string> saved)
    {
        flags.Clear();
        if (saved == null)
            return;

        foreach (string f in saved)
            if (!string.IsNullOrEmpty(f))
                flags.Add(f);
    }

    public List<string> FlagList() => new List<string>(flags);

    // ── 게임 시작 / 이어하기 ────────────────────────────────────

    /// <summary>세이브를 지우고 처음부터 시작한다.</summary>
    public void NewGame(string firstScene)
    {
        SaveSystem.DeleteSave();
        flags.Clear();
        GoToScene(firstScene, DefaultSpawn, saveOnArrival: true);
    }

    /// <summary>세이브에 적힌 씬과 위치로 돌아간다. 세이브가 없으면 false.</summary>
    public bool Continue()
    {
        PlayerSaveData data = SaveSystem.LoadPlayer();
        if (data == null)
            return false;

        string destination = data.hasProgress ? data.sceneName : data.checkpointScene;
        if (string.IsNullOrEmpty(destination)) destination = "Main";
        if (destination == "Test") destination = "Main";

        Vector2 position = data.hasProgress
            ? new Vector2(data.posX, data.posY)
            : new Vector2(data.checkpointPosition.x, data.checkpointPosition.y);
        RestoreFlags(data.flags);

        // 저장된 좌표로 직접 놓는다 (입구가 아니라 죽기 직전 그 자리)
        pendingSpawn = null;
        StartCoroutine(LoadRoutine(destination, position, saveOnArrival: false));
        return true;
    }

    public void ReturnToTitle()
    {
        StartCoroutine(LoadRoutine(TitleScene, null, saveOnArrival: false));
    }

    /// <summary>다른 씬으로 간다. spawnId 는 그 씬에 있는 SpawnPoint 이름.</summary>
    public void GoToScene(string sceneName, string spawnId = DefaultSpawn, bool saveOnArrival = true)
    {
        if (IsLoading || string.IsNullOrEmpty(sceneName))
            return;

        pendingSpawn = spawnId;
        StartCoroutine(LoadRoutine(sceneName, null, saveOnArrival));
    }

    private IEnumerator LoadRoutine(string sceneName, Vector2? exactPosition, bool saveOnArrival)
    {
        IsLoading = true;

        yield return Fade(1f);

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        if (op == null)
        {
            Debug.LogError("[GameFlow] 씬을 열지 못했습니다: " + sceneName
                           + " (Build Settings 에 들어 있는지 확인하세요)");
            yield return Fade(0f);
            IsLoading = false;
            yield break;
        }

        while (!op.isDone)
            yield return null;

        // 씬이 올라온 뒤에 플레이어를 옮긴다
        yield return null;
        PlacePlayer(exactPosition);

        if (saveOnArrival)
            SaveNow();

        yield return Fade(0f);
        IsLoading = false;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BindSceneView();
        if (fade != null) fade.alpha = IsLoading ? 1f : 0f;
        // 씬을 에디터에서 직접 실행한 경우에도 진행 표시는 유지된다
        Debug.Log("[GameFlow] 씬 진입: " + scene.name);
    }

    /// <summary>플레이어를 입구나 저장된 좌표로 옮긴다.</summary>
    private void PlacePlayer(Vector2? exactPosition)
    {
        var stats = FindFirstObjectByType<PlayerStats>();
        if (stats == null)
            return;

        Vector3 target;

        if (exactPosition.HasValue)
        {
            target = new Vector3(exactPosition.Value.x, exactPosition.Value.y, 0f);
        }
        else
        {
            SpawnPoint spawn = SpawnPoint.Find(pendingSpawn);
            if (spawn == null)
                return;   // 입구가 없으면 씬에 놓인 그대로 둔다

            target = spawn.transform.position;
        }

        stats.transform.position = target;

        var body = stats.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.position = target;
            body.linearVelocity = Vector2.zero;
        }
    }

    // ── 저장 ────────────────────────────────────────────────────

    /// <summary>지금 씬과 위치, 진행 표시까지 함께 저장한다.</summary>
    public void SaveNow()
    {
        var stats = FindFirstObjectByType<PlayerStats>();
        if (stats == null)
            return;

        SaveSystem.SaveGame(stats);

        // 방금 저장했으니 자동 저장 시계도 처음부터 다시 센다
        autoSaveTimer = 0f;
    }

    // ── 화면 암전 ───────────────────────────────────────────────

    void BindSceneView()
    {
        var view = FindFirstObjectByType<SceneFlowView>();
        fade = view != null ? view.fade : null;
        notice = view != null ? view.notice : null;
    }

#if UNITY_EDITOR
    public void BakeSceneUI()
    {
        BuildFadeCanvas();
        var view = fade.gameObject.AddComponent<SceneFlowView>();
        view.fade = fade; view.notice = notice;
    }
#endif

    private void BuildFadeCanvas()
    {
        var canvasGo = new GameObject("GameFlow_Fade");
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;   // 사망 화면(200)보다 위

        fade = canvasGo.AddComponent<CanvasGroup>();
        fade.alpha = 0f;
        fade.blocksRaycasts = false;

        var image = new GameObject("Black").AddComponent<Image>();
        image.transform.SetParent(canvasGo.transform, false);
        image.color = Color.black;

        var rect = image.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        BuildNotice(canvasGo.transform);
    }

    /// <summary>오른쪽 아래에 잠깐 뜨는 알림 글자. 암전과 달리 입력을 막지 않는다.</summary>
    private void BuildNotice(Transform parent)
    {
        var go = new GameObject("Notice");
        go.transform.SetParent(parent, false);

        notice = go.AddComponent<TextMeshProUGUI>();
        notice.text = string.Empty;
        notice.fontSize = 22f;
        notice.color = new Color(0.85f, 0.85f, 0.9f, 0f);
        notice.alignment = TextAlignmentOptions.BottomRight;
        notice.raycastTarget = false;

        TMP_FontAsset font = UiBootstrap.FindSceneFont();
        if (font != null)
            notice.font = font;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-40f, 40f);
        rect.sizeDelta = new Vector2(400f, 40f);
    }

    private IEnumerator Fade(float target)
    {
        if (fade == null)
            yield break;

        fade.blocksRaycasts = target > 0.5f;

        while (!Mathf.Approximately(fade.alpha, target))
        {
            float step = fadeDuration > 0f ? Time.unscaledDeltaTime / fadeDuration : 1f;
            fade.alpha = Mathf.MoveTowards(fade.alpha, target, step);
            yield return null;
        }
    }
}

using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 죽으면 사망 화면을 띄우고, 부활 입력을 받으면 되살린다.
/// 플레이어 오브젝트(PlayerStats 가 있는 곳)에 붙인다.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class PlayerRespawn : MonoBehaviour
{
    [Tooltip("되살아날 위치. 비워두면 게임 시작 위치로 돌아간다")]
    [SerializeField] private Transform respawnPoint;

    [Tooltip("죽고 나서 사망 화면이 뜨기까지의 시간")]
    [SerializeField] private float screenDelay = 0.6f;

    [Tooltip("부활할 때 채워 줄 HP 비율")]
    [Range(0.1f, 1f)]
    [SerializeField] private float reviveHpRatio = 1f;

    [Tooltip("부활할 때 기분을 시작값으로 되돌려 제어권을 플레이어에게 넘긴다")]
    [SerializeField] private bool resetMoodOnRespawn = true;

    [Tooltip("사망 화면에 쓸 한글 폰트. 비워두면 영문 문구로 나온다")]
    [SerializeField] private TMP_FontAsset koreanFont;

    private PlayerStats stats;
    private MoodSystem moodSystem;
    private Rigidbody2D body;
    private DeathScreenUI deathScreen;

    private Vector3 startPosition;
    private Coroutine showRoutine;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        moodSystem = GetComponent<MoodSystem>();
        body = GetComponent<Rigidbody2D>();
        startPosition = transform.position;

        deathScreen = GetComponent<DeathScreenUI>();
        if (deathScreen == null)
            deathScreen = gameObject.AddComponent<DeathScreenUI>();

        deathScreen.Configure(koreanFont);
        deathScreen.OnRespawnRequested += Respawn;

        stats.OnDied += HandleDeath;
    }

    void Update()
    {
        // 화면이 떠 있을 때 아무 키나 누르면 부활
        if (!stats.IsDead || deathScreen == null || !deathScreen.IsVisible)
            return;

        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            Respawn();
    }

    private void HandleDeath()
    {
        if (body != null)
            body.linearVelocity = Vector2.zero;

        if (showRoutine != null)
            StopCoroutine(showRoutine);
        showRoutine = StartCoroutine(ShowAfterDelay());
    }

    private IEnumerator ShowAfterDelay()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, screenDelay));

        if (stats.IsDead && deathScreen != null)
            deathScreen.Show();

        showRoutine = null;
    }

    /// <summary>시작 위치(또는 지정한 지점)로 되살린다.</summary>
    public void Respawn()
    {
        if (!stats.IsDead)
            return;

        Vector3 spot = respawnPoint != null ? respawnPoint.position : startPosition;
        transform.position = spot;
        if (body != null)
        {
            body.position = spot;
            body.linearVelocity = Vector2.zero;
        }

        stats.Revive(reviveHpRatio);

        // 죽은 채로 저장돼서 다음 실행이 꼬이는 걸 막는다
        stats.Save();

        if (resetMoodOnRespawn && moodSystem != null)
            moodSystem.ResetMood();

        if (deathScreen != null)
            deathScreen.Hide();

        Debug.Log("[Respawn] 부활: " + spot.ToString("0.0"));
    }

    void OnDestroy()
    {
        if (stats != null)
            stats.OnDied -= HandleDeath;

        if (deathScreen != null)
            deathScreen.OnRespawnRequested -= Respawn;
    }
}

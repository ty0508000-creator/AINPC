using UnityEngine;

/// <summary>
/// 지역 중간보스·최종보스에 단다. 쓰러지면 진행 표시를 세워 다음 지역으로 가는 포탈을 연다.
/// </summary>
[RequireComponent(typeof(MonsterBase))]
public class StageBoss : MonoBehaviour
{
    [Tooltip("쓰러지면 세울 진행 표시. 예: region1.boss_cleared")]
    [SerializeField] private string clearFlag;

    [Tooltip("쓰러졌을 때 띄울 말")]
    [SerializeField] private string clearMessage = "길이 열렸다.";

    private MonsterBase monster;

    void Awake()
    {
        // 이미 잡은 보스는 다시 나오지 않는다. 보스 구역은 빈 방으로 남는다.
        if (!string.IsNullOrEmpty(clearFlag) && GameFlow.Instance.HasFlag(clearFlag))
        {
            gameObject.SetActive(false);   // 지워지기 전 한 프레임도 보이거나 움직이지 않게
            Destroy(gameObject);
            return;
        }

        monster = GetComponent<MonsterBase>();
        monster.Died += HandleDied;
    }

    void OnDestroy()
    {
        if (monster != null)
            monster.Died -= HandleDied;
    }

    private void HandleDied(MonsterBase _)
    {
        if (string.IsNullOrEmpty(clearFlag))
            return;

        GameFlow.Instance.SetFlag(clearFlag);
        GameFlow.Instance.ShowNotice(clearMessage, 3f);
    }
}

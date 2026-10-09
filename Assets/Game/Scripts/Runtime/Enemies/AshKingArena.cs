using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class AshKingArena : MonoBehaviour
{
    [SerializeField] AshKingBoss bossPrefab;
    [SerializeField] MonsterData bossData;
    [SerializeField] GameObject battleGate;
    [SerializeField] Transform preparationPoint;
    public AshKingBoss Boss { get; private set; }
    public bool Cleared => QuestManager.Instance!=null&&QuestManager.Instance.IsCompleted(HubStoryIds.Boss);
    PlayerStats player;Coroutine initialization;
    void OnEnable(){initialization=StartCoroutine(Initialize());}
    IEnumerator Initialize()
    {
        while(QuestManager.Instance==null||!QuestManager.Instance.IsSaveReady||GameFlow.Instance.IsLoading)yield return null;
        player=FindFirstObjectByType<PlayerStats>();if(player==null)yield break;
        player.OnDied+=PlayerDied;player.OnRespawned+=ResetFight;ResetFight();initialization=null;
    }
    void ResetFight()
    {
        if(Boss!=null){Boss.Defeated-=Victory;Boss.gameObject.SetActive(false);Destroy(Boss.gameObject);}
        battleGate.SetActive(false);Boss=null;
        if(Cleared)return;
        Boss=Instantiate(bossPrefab,new Vector3(0,3,0),Quaternion.identity,transform);Boss.Initialize(bossData,null);Boss.Defeated+=Victory;
    }
    void PlayerDied(){if(Boss!=null)Boss.gameObject.SetActive(false);battleGate.SetActive(false);}
    void Victory()
    {
        battleGate.SetActive(false);
        if(Boss!=null){var visual=new GameObject("재의 왕 쓰러짐");visual.transform.SetParent(transform);visual.transform.position=Boss.transform.position;visual.transform.localScale=Boss.transform.localScale;var sprite=visual.AddComponent<SpriteRenderer>();sprite.sprite=Boss.GetComponent<SpriteRenderer>().sprite;sprite.sortingOrder=1000-Mathf.RoundToInt(visual.transform.position.y*10);var animator=visual.AddComponent<Animator>();animator.runtimeAnimatorController=Boss.GetComponent<Animator>().runtimeAnimatorController;animator.SetTrigger("Death");Destroy(visual,2);}
        GameFlow.Instance.ShowNotice("재의 왕을 쓰러뜨렸다. 마을의 현묵에게 돌아가자.");
    }
    void Update()
    {
        if(player==null||!player.IsAlive||QuestManager.Instance==null||!QuestManager.Instance.IsSaveReady)return;
        if(Boss!=null&&!Boss.Fighting&&!Boss.IsDead&&player.transform.position.y>=-8&&QuestManager.Instance.GetState(HubStoryIds.Boss)==QuestState.Active)
        {QuestManager.Instance.ReportReach("hub_boss_arena");if(Boss.BeginBattle())battleGate.SetActive(true);}
        if(Boss!=null&&Boss.Fighting)return;
        if(preparationPoint!=null&&Keyboard.current!=null&&Keyboard.current.eKey.wasPressedThisFrame&&Vector2.Distance(player.transform.position,preparationPoint.position)<=2&&!DialogueManager.IsDialogueOpen)
        {
            FindFirstObjectByType<StoryConversationUI>()?.Open("불 꺼진 제단","체력과 내력을 정비한다. 재 폭발이 예고되면 옥색 원 두 곳 중 하나로 피하자.\n재의 왕의 체력이 절반 이하가 되면 추격이 빨라진다.","정비한다",()=>{player.Heal(player.MaxHP);player.RestoreMana(player.MaxMana);player.Save();});
        }
    }
    void OnDisable(){if(initialization!=null){StopCoroutine(initialization);initialization=null;}if(player!=null){player.OnDied-=PlayerDied;player.OnRespawned-=ResetFight;}if(Boss!=null){Boss.Defeated-=Victory;Boss.gameObject.SetActive(false);Destroy(Boss.gameObject);}Boss=null;}
}

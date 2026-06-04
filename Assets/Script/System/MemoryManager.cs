using System.Text;
using System.Threading.Tasks;
using LLMUnity;
using UnityEngine;

/// <summary>
/// RAG(벡터DB) 기반 장기 기억. 과거 대화를 저장하고, 의미가 비슷한 기억을 검색해
/// 프롬프트에 주입한다. 임베딩/검색 실패는 모두 무시(게임 진행 우선).
///
/// 사용: 씬의 한 GameObject에 붙인다. RAG 컴포넌트는 비워두면 자동 생성하고,
/// 임베딩용 LLM 은 비워두면 씬에서 자동 탐색한다.
/// </summary>
public class MemoryManager : MonoBehaviour
{
    [Header("RAG")]
    [Tooltip("비워두면 같은 오브젝트에 자동 생성")]
    [SerializeField] private RAG rag;
    [Tooltip("임베딩용 LLM. 비워두면 씬에서 자동 탐색")]
    [SerializeField] private LLM llm;
    [SerializeField] private SearchMethods searchMethod = SearchMethods.SimpleSearch;

    [Header("Memory")]
    [SerializeField] private string saveFileName = "npc_memory.zip";
    [SerializeField] private int recallCount = 3;
    [Tooltip("이 거리(0~2) 이하만 관련 기억으로 사용. 작을수록 엄격")]
    [SerializeField] private float maxDistance = 1.0f;
    [SerializeField] private bool autoSaveOnRemember = true;

    public bool Ready { get; private set; }
    private bool initializing;

    async void Start()
    {
        await Initialize();
    }

    async Task Initialize()
    {
        if (Ready || initializing) return;
        initializing = true;
        try
        {
            if (rag == null) rag = GetComponent<RAG>();
            if (rag == null) rag = gameObject.AddComponent<RAG>();
            if (llm == null) llm = FindObjectOfType<LLM>();

            if (llm == null)
                Debug.LogWarning("[Memory] LLM을 찾지 못했습니다. 임베딩 불가 → 기억 비활성.");

            rag.Init(searchMethod, ChunkingMethods.NoChunking, llm);
            await rag.Load(saveFileName);   // 파일 없으면 false, 무시
            Ready = llm != null;
            Debug.Log($"[Memory] 준비됨 (기존 기억 {rag.Count()}개)");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Memory] 초기화 실패 → 기억 비활성: {e.Message}");
            Ready = false;
        }
        initializing = false;
    }

    /// <summary>기억 한 줄을 저장. group 으로 화자/맥락을 분리(예: NPC 이름, "inner").</summary>
    public async Task Remember(string text, string group = "")
    {
        if (!Ready || string.IsNullOrWhiteSpace(text)) return;
        try
        {
            await rag.Add(text, group);
            if (autoSaveOnRemember) Persist();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Memory] 저장 실패: {e.Message}");
        }
    }

    /// <summary>query 와 의미가 비슷한 기억들을 묶어 프롬프트용 문자열로 반환. 없으면 "".</summary>
    public async Task<string> Recall(string query, string group = "")
    {
        if (!Ready || string.IsNullOrWhiteSpace(query)) return "";
        try
        {
            var (phrases, distances) = await rag.Search(query, recallCount, group);
            if (phrases == null || phrases.Length == 0) return "";

            var sb = new StringBuilder();
            for (int i = 0; i < phrases.Length; i++)
            {
                if (distances != null && i < distances.Length && distances[i] > maxDistance) continue;
                sb.AppendLine($"- {phrases[i]}");
            }
            return sb.ToString().TrimEnd();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Memory] 검색 실패: {e.Message}");
            return "";
        }
    }

    public void Persist()
    {
        if (!Ready) return;
        try { rag.Save(saveFileName); }
        catch (System.Exception e) { Debug.LogWarning($"[Memory] 파일 저장 실패: {e.Message}"); }
    }

    void OnApplicationQuit() => Persist();
}

using UnityEngine;

/// <summary>
/// 내면·NPC 대화용 LLM 서버와 임베딩 서버를 게임 시작 때 한 번만 띄우고, 씬이 바뀌어도 유지한다.
/// 씬마다 서버를 두면 이동할 때마다 8B 모델을 다시 올려야 하고(20초 이상), 1지역 밖에서는 LLM 이 아예 없었다.
/// 프리팹은 Resources/LLMServices 이다. 씬의 DialogueManager·InnerVoiceManager 는 LLMAgent 를 찾아서 쓴다.
/// </summary>
public class LLMServices : MonoBehaviour
{
    public static LLMServices Instance { get; private set; }

    [Tooltip("응답을 {\"dialogue\",\"mood_delta\"} JSON 형식으로만 생성하게 강제한다(GBNF, LLMResponseParser.JsonGrammar). 끄면 프롬프트 지시만으로 형식을 맞춘다.")]
    [SerializeField] private bool useJsonGrammar = true;
    bool grammarOn;

    /// <summary>지금 JSON grammar 가 걸려 있는가. 연구 로그의 조건 열에 쓴다.</summary>
    public static bool GrammarOn => Instance != null && Instance.grammarOn;

    /// <summary>공용 LLMAgent 의 JSON grammar 를 켜고 끈다. 비교 실험에서 조건을 바꿀 때도 쓴다.</summary>
    public static void SetGrammar(bool on)
    {
        if (Instance == null) return;
        var agent = Instance.GetComponentInChildren<LLMUnity.LLMAgent>(true);
        if (agent == null) return;
        // 문법 원문이 CRLF 파일의 여러 줄 문자열이라 \r 이 섞인다. GBNF 해석에 맡기지 않고 지운다.
        agent.grammar = on ? LLMResponseParser.JsonGrammar.Replace("\r", "") : "";
        Instance.grammarOn = on;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var prefab = Resources.Load<GameObject>("LLMServices");
        if (prefab == null)
        {
            Debug.LogWarning("[LLMServices] Resources/LLMServices 프리팹이 없어 LLM 대화를 쓸 수 없습니다.");
            return;
        }
        // LLMUnitySetup 도 BeforeSceneLoad 에서 네이티브 라이브러리 폴더를 정하는데, 같은 단계끼리는 순서가 보장되지 않는다.
        // 빌드에서 이쪽이 먼저 돌면 LLM 이 라이브러리를 못 찾으므로(에디터는 이미 정해져 있어 문제없음) 같은 값을 먼저 정해 둔다.
        // 원본: LLMUnitySetup.InitializeOnLoadCommon (PC 전용 프로젝트라 모바일 분기는 없다)
        UndreamAI.LlamaLib.LlamaLib.baseLibraryPath = System.IO.Path.Combine(
            LLMUnity.LLMUnitySetup.libraryPath, UndreamAI.LlamaLib.LlamaLib.GetPlatform(), "native");
        Instantiate(prefab).name = prefab.name;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SetGrammar(useJsonGrammar);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}

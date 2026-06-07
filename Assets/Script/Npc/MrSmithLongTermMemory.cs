using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LLMUnity;
using UnityEngine;

public class MrSmithLongTermMemory : MonoBehaviour
{
    private const string DefaultEmbeddingModel = "Models/all-MiniLM-L12-v2.Q4_K_M.gguf";

    [Header("Embedding LLM")]
    [SerializeField] private LLM embeddingLLM;
    [SerializeField] private string embeddingModel = DefaultEmbeddingModel;
    [SerializeField] private int expectedEmbeddingLength = 384;
    [SerializeField] private float initTimeoutSeconds = 30f;

    [Header("RAG")]
    [SerializeField] private RAG rag;
    [SerializeField] private SearchMethods searchMethod = SearchMethods.SimpleSearch;
    [SerializeField] private string saveFileName = "mr_smith_memory.zip";
    [SerializeField] private int recallCount = 3;
    [SerializeField] private float maxDistance = 1.0f;
    [SerializeField] private bool autoSaveOnRemember = true;
    [SerializeField] private bool logRecall = true;

    [Header("Recent Conversation")]
    [SerializeField] private int recentTurnsToKeep = 4;

    private readonly List<string> recentTurns = new List<string>();
    private bool initializing;
    private bool initialized;
    private bool memoryEnabled;

    public bool MemoryEnabled => memoryEnabled;
    public string SaveFileName => saveFileName;

    private async void Start()
    {
        await EnsureInitialized();
    }

    public async Task EnsureInitialized()
    {
        if (initialized || initializing) return;

        initializing = true;
        memoryEnabled = false;

        try
        {
            if (embeddingLLM == null)
                embeddingLLM = FindEmbeddingServer();

            if (embeddingLLM == null)
            {
                DisableMemory("Embedding_Server LLM을 찾지 못했습니다.");
                return;
            }

            string configuredModel = !string.IsNullOrWhiteSpace(embeddingLLM.model)
                ? embeddingLLM.model
                : embeddingModel;
            string modelPath = LLM.GetLLMManagerAssetRuntime(configuredModel);
            if (!File.Exists(modelPath))
            {
                DisableMemory($"임베딩 모델 파일이 없습니다: {modelPath}");
                return;
            }

            if (!await WaitForEmbeddingServer())
            {
                DisableMemory("Embedding_Server가 시작되지 않았습니다.");
                return;
            }

            if (!embeddingLLM.embeddingsOnly)
            {
                DisableMemory($"Embedding_Server가 embeddings-only가 아닙니다. model={embeddingLLM.model}");
                return;
            }

            if (embeddingLLM.embeddingLength != expectedEmbeddingLength)
            {
                DisableMemory($"Embedding_Server embedding length가 {embeddingLLM.embeddingLength}입니다. 기대값={expectedEmbeddingLength}");
                return;
            }

            if (rag == null) rag = GetComponent<RAG>();
            if (rag == null) rag = gameObject.AddComponent<RAG>();

            rag.Init(searchMethod, ChunkingMethods.NoChunking, embeddingLLM);

            var probeEmbedding = await rag.search.llmEmbedder.Embeddings("Mr.Smith memory probe");
            if (probeEmbedding == null || probeEmbedding.Count != expectedEmbeddingLength)
            {
                int actualLength = probeEmbedding == null ? 0 : probeEmbedding.Count;
                DisableMemory($"임베딩 probe 길이가 {actualLength}입니다. 기대값={expectedEmbeddingLength}");
                return;
            }

            await rag.Load(saveFileName);

            memoryEnabled = true;
            Debug.Log($"[MrSmithMemory] ready. model={embeddingLLM.model}, embeddingsOnly={embeddingLLM.embeddingsOnly}, embeddingLength={embeddingLLM.embeddingLength}, file={saveFileName}, count={rag.Count()}");
        }
        catch (System.Exception ex)
        {
            DisableMemory($"초기화 실패: {ex.Message}");
        }
        finally
        {
            initialized = true;
            initializing = false;
        }
    }

    public string GetRecentConversationBlock()
    {
        if (recentTurns.Count == 0) return "";
        return string.Join("\n", recentTurns);
    }

    public async Task<string> Recall(string query)
    {
        await EnsureInitialized();
        if (!memoryEnabled || string.IsNullOrWhiteSpace(query)) return "";

        try
        {
            var (phrases, distances) = await rag.Search(query, recallCount, "Mr.Smith");
            if (phrases == null || phrases.Length == 0) return "";

            var sb = new StringBuilder();
            for (int i = 0; i < phrases.Length; i++)
            {
                float distance = distances != null && i < distances.Length ? distances[i] : float.PositiveInfinity;
                if (distance > maxDistance) continue;

                if (logRecall)
                    Debug.Log($"[MrSmithMemory] recall distance={distance:0.000} text={phrases[i]}");

                sb.AppendLine($"- {phrases[i]}");
            }

            return sb.ToString().TrimEnd();
        }
        catch (System.Exception ex)
        {
            DisableMemory($"검색 실패: {ex.Message}");
            return "";
        }
    }

    public async Task RememberExchange(string playerText, string smithText)
    {
        if (string.IsNullOrWhiteSpace(playerText) && string.IsNullOrWhiteSpace(smithText)) return;

        string turn = $"플레이어: \"{playerText}\" / Mr.Smith: \"{smithText}\"";
        recentTurns.Add(turn);
        while (recentTurns.Count > Mathf.Max(1, recentTurnsToKeep))
            recentTurns.RemoveAt(0);

        await EnsureInitialized();
        if (!memoryEnabled) return;

        try
        {
            await rag.Add(turn, "Mr.Smith");
            if (autoSaveOnRemember) Persist();
        }
        catch (System.Exception ex)
        {
            DisableMemory($"저장 실패: {ex.Message}");
        }
    }

    public async Task RememberEvent(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        recentTurns.Add(text);
        while (recentTurns.Count > Mathf.Max(1, recentTurnsToKeep))
            recentTurns.RemoveAt(0);

        await EnsureInitialized();
        if (!memoryEnabled) return;

        try
        {
            await rag.Add(text, "Mr.Smith");
            if (autoSaveOnRemember) Persist();
        }
        catch (System.Exception ex)
        {
            DisableMemory($"이벤트 저장 실패: {ex.Message}");
        }
    }

    public void Persist()
    {
        if (!memoryEnabled || rag == null) return;

        try
        {
            rag.Save(saveFileName);
        }
        catch (System.Exception ex)
        {
            DisableMemory($"파일 저장 실패: {ex.Message}");
        }
    }

    private LLM FindEmbeddingServer()
    {
        foreach (LLM llm in FindObjectsByType<LLM>(FindObjectsSortMode.None))
        {
            if (llm == null) continue;
            if (llm.name == "Embedding_Server") return llm;
        }

        foreach (LLM llm in FindObjectsByType<LLM>(FindObjectsSortMode.None))
        {
            if (llm != null && llm.embeddingsOnly && llm.embeddingLength == expectedEmbeddingLength)
                return llm;
        }

        return null;
    }

    private async Task<bool> WaitForEmbeddingServer()
    {
        float startTime = Time.realtimeSinceStartup;
        while (embeddingLLM != null && !embeddingLLM.started && !embeddingLLM.failed)
        {
            if (Time.realtimeSinceStartup - startTime > initTimeoutSeconds)
                return false;

            await Task.Yield();
        }

        return embeddingLLM != null && embeddingLLM.started && !embeddingLLM.failed;
    }

    private void DisableMemory(string reason)
    {
        memoryEnabled = false;
        Debug.LogWarning($"[MrSmithMemory] memory disabled. {reason}");
    }

    private void OnApplicationQuit()
    {
        Persist();
    }
}

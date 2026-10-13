using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Authored village chatter; does not change quests, rewards, or saves.</summary>
public sealed class VillageResident : MonoBehaviour
{
    public string residentName;
    [TextArea(2, 4)] public string line;
    public Sprite[] idleFrames;
    SpriteRenderer spriteRenderer;
    float idleTime;
    PlayerStats player;
    void Start() { player = FindFirstObjectByType<PlayerStats>(); spriteRenderer = GetComponent<SpriteRenderer>(); }
    void Update()
    {
        if (spriteRenderer != null && idleFrames != null && idleFrames.Length > 0)
        {
            idleTime += Time.deltaTime;
            spriteRenderer.sprite = idleFrames[(Mathf.FloorToInt(idleTime * 6) + Mathf.Abs(GetInstanceID() % idleFrames.Length)) % idleFrames.Length];
        }
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) Interact();
    }
    public bool Interact()
    {
        if (player == null) player = FindFirstObjectByType<PlayerStats>();
        if (player == null || !player.IsAlive || (GameFlow.Instance != null && GameFlow.Instance.IsLoading) || Time.timeScale == 0 ||
            DialogueManager.IsChatOpen || InnerVoiceManager.IsOpen ||
            Vector2.Distance(player.transform.position, transform.position) > 1.8f) return false;
        var control = player.GetComponent<ControlManager>();
        if (control != null && !control.IsPlayerControlled) return false;
        foreach (var other in FindObjectsByType<VillageResident>(FindObjectsSortMode.None))
            if (other != this && Vector2.Distance(player.transform.position, other.transform.position) <
                Vector2.Distance(player.transform.position, transform.position)) return false;
        var ui = FindFirstObjectByType<StoryConversationUI>();
        return ui != null && ui.Open(residentName, line, "알겠다 · E", null);
    }
}

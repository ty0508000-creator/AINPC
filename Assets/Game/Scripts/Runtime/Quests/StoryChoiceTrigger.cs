using UnityEngine;

/// <summary>Place on a trigger zone to present an explicit narrative choice once.</summary>
[RequireComponent(typeof(Collider2D))]
public class StoryChoiceTrigger : MonoBehaviour
{
    [TextArea(1, 2)] [SerializeField] private string title = "결정의 순간";
    [TextArea(2, 5)] [SerializeField] private string body = "어떻게 할 것인가?";
    [SerializeField] private StoryChoiceOption[] options = new StoryChoiceOption[0];
    [SerializeField] private bool once = true;
    bool used;
    void Reset() { GetComponent<Collider2D>().isTrigger = true; }
    void OnTriggerEnter2D(Collider2D other)
    {
        if ((used && once) || other.GetComponentInParent<Player_Controller>() == null) return;
        var manager = StoryChoiceManager.Instance;
        if (manager == null) manager = FindFirstObjectByType<StoryChoiceManager>();
        if (manager == null) { Debug.LogWarning("[Choice] StoryChoiceManager가 씬에 없습니다."); return; }
        manager.Open(title, body, options);
        used = true;
        if (once) gameObject.SetActive(false);
    }
}

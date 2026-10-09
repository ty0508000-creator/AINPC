using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class ItemPickup : MonoBehaviour
{
    [SerializeField] ItemDefinition item;
    [SerializeField, Min(1)] int quantity = 1;

    [Tooltip("주웠을 때 세울 진행 표시. 비우면 세우지 않는다")]
    [SerializeField] string flagOnPickup;

    void Reset() => GetComponent<Collider2D>().isTrigger = true;

    void OnTriggerEnter2D(Collider2D other)
    {
        var inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null || item == null) return;

        bool added = inventory.TryAdd(item, quantity);

        // 이미 가지고 있어서 더 못 담아도 진행 표시는 세운다. 안 그러면 출구가 영영 안 열린다.
        if (!string.IsNullOrEmpty(flagOnPickup) && (added || inventory.Count(item.itemId) > 0))
            GameFlow.Instance.SetFlag(flagOnPickup);

        if (added) Destroy(gameObject);
    }
}

using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class ItemPickup : MonoBehaviour
{
    [SerializeField] ItemDefinition item;
    [SerializeField, Min(1)] int quantity = 1;

    void Reset() => GetComponent<Collider2D>().isTrigger = true;

    void OnTriggerEnter2D(Collider2D other)
    {
        var inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory != null && inventory.TryAdd(item, quantity)) Destroy(gameObject);
    }
}

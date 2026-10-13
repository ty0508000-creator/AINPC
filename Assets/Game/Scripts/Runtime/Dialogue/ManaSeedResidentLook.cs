using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Owns sorting of authored paper-doll children; their lifetime follows the resident.</summary>
[DefaultExecutionOrder(100)]
public sealed class ManaSeedResidentLook : MonoBehaviour
{
    public SpriteRenderer[] layers;
    SpriteRenderer body;
    SortingGroup group;
    void Awake() { body = GetComponent<SpriteRenderer>(); group = GetComponent<SortingGroup>(); }
    void LateUpdate()
    {
        if (body == null || group == null) return;
        group.sortingLayerID = body.sortingLayerID;
        group.sortingOrder = body.sortingOrder;
        if (layers == null) return;
        for (int i = 0; i < layers.Length; i++)
            if (layers[i] != null) { layers[i].sortingLayerID = body.sortingLayerID; layers[i].sortingOrder = body.sortingOrder + i + 1; }
    }
}

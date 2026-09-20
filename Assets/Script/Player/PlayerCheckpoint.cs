using UnityEngine;

/// <summary>Place on a safe trigger collider. Optional respawnPoint avoids spawning inside the trigger.</summary>
[RequireComponent(typeof(Collider2D))]
public class PlayerCheckpoint : MonoBehaviour
{
    [SerializeField] Transform respawnPoint;
    void OnTriggerEnter2D(Collider2D other)
    {
        var stats = other.GetComponentInParent<PlayerStats>();
        if (stats != null && stats.IsAlive)
            stats.SetCheckpoint(respawnPoint != null ? respawnPoint.position : transform.position);
    }
    void Reset() => GetComponent<Collider2D>().isTrigger = true;
}

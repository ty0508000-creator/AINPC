using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D),typeof(Collider2D))]
public sealed class RegionalProjectile : MonoBehaviour
{
    int damage;bool hit;Rigidbody2D body;
    HashSet<PlayerStats> volleyTargets;
    public void Initialize(Vector2 direction,int attackDamage,float speed,float lifetime,HashSet<PlayerStats> sharedTargets = null)
    { damage=attackDamage;volleyTargets=sharedTargets;body=GetComponent<Rigidbody2D>();body.linearVelocity=direction.normalized*speed;Destroy(gameObject,lifetime); }
    void OnTriggerEnter2D(Collider2D other)
    {
        if(hit)return;
        var player=other.GetComponentInParent<PlayerStats>();
        if(player!=null){hit=true;if(volleyTargets==null||volleyTargets.Add(player))player.TakeDamage(damage);Destroy(gameObject);}
        else if(!other.isTrigger&&other.GetComponentInParent<MonsterBase>()==null){hit=true;Destroy(gameObject);}
    }
}

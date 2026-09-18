using UnityEngine;

/// <summary>
/// Attached to the axe. While it overlaps an enemy (Caminante or
/// Lanzahuesos), that enemy takes a lethal hit - one axe impact always kills
/// (see enemigos.md). Uses an overlap query rather than physics trigger
/// callbacks so it still works when the grabbed axe's rigidbody is kinematic
/// (kinematic-vs-kinematic contacts do not raise trigger events).
/// </summary>
[DisallowMultipleComponent]
public class EnemyBanisher : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Radius around the axe head in which enemies are hit.")]
    float m_Radius = 0.4f;

    [SerializeField]
    [Tooltip("Layers that can contain enemies.")]
    LayerMask m_EnemyLayers = ~0;

    [SerializeField]
    [Tooltip("Hit points dealt per overlap. Always lethal against either enemy's max hits.")]
    int m_HitPoints = 2;

    readonly Collider[] m_Overlaps = new Collider[16];

    void Update()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, m_Radius, m_Overlaps, m_EnemyLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            var skeleton = m_Overlaps[i].GetComponentInParent<Skeleton>();
            if (skeleton != null)
            {
                if (skeleton.IsAlive)
                    skeleton.TakeHit(m_HitPoints);
                continue;
            }

            var boneThrower = m_Overlaps[i].GetComponentInParent<BoneThrower>();
            if (boneThrower != null && boneThrower.IsAlive)
                boneThrower.TakeHit(m_HitPoints);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, m_Radius);
    }
}

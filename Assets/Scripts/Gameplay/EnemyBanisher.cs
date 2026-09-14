using UnityEngine;

/// <summary>
/// Attached to the banishing cylinder. While it overlaps an enemy, that enemy
/// is removed from the world. Uses an overlap query rather than physics trigger
/// callbacks so it still works when the grabbed cylinder's rigidbody is
/// kinematic (kinematic-vs-kinematic contacts do not raise trigger events).
/// </summary>
[DisallowMultipleComponent]
public class EnemyBanisher : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Radius around the cylinder in which enemies are banished.")]
    float m_Radius = 0.4f;

    [SerializeField]
    [Tooltip("Layers that can contain enemies.")]
    LayerMask m_EnemyLayers = ~0;

    readonly Collider[] m_Overlaps = new Collider[16];

    void Update()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, m_Radius, m_Overlaps, m_EnemyLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            var skeleton = m_Overlaps[i].GetComponentInParent<Skeleton>();
            if (skeleton != null)
                skeleton.Banish();
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, m_Radius);
    }
}

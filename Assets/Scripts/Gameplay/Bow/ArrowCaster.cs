using UnityEngine;

/// <summary>
/// Continuously line-casts from the arrow tip's previous position to its
/// current position to detect what it ran into. A line-cast is used instead of
/// collision callbacks so that fast arrows cannot tunnel through thin targets.
/// Ported from the OOT shooting gallery, stripped of XR Interaction Toolkit.
/// </summary>
[DisallowMultipleComponent]
public class ArrowCaster : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Nose of the arrow. Defaults to this transform when left empty.")]
    Transform m_Tip;

    [SerializeField]
    [Tooltip("Layers the flying arrow can collide with.")]
    LayerMask m_LayerMask = ~0;

    Vector3 m_LastPosition;

    public Transform Tip => m_Tip != null ? m_Tip : transform;

    /// <summary>
    /// Returns true and the hit information when the tip swept through
    /// something since the previous call.
    /// </summary>
    public bool CheckForCollision(out RaycastHit hit)
    {
        Vector3 currentPosition = Tip.position;

        if (m_LastPosition == Vector3.zero)
        {
            m_LastPosition = currentPosition;
        }

        bool collided = Physics.Linecast(m_LastPosition, currentPosition, out hit, m_LayerMask);
        m_LastPosition = collided ? m_LastPosition : currentPosition;

        return collided;
    }

    /// <summary>
    /// Clears the cached sweep position, used whenever the arrow starts flying.
    /// </summary>
    public void ResetCaster()
    {
        m_LastPosition = Vector3.zero;
    }

    /// <summary>Injection helpers for the prefab builder / editor tooling.</summary>
    public void InjectTip(Transform tip) => m_Tip = tip;
    public void InjectLayerMask(LayerMask layerMask) => m_LayerMask = layerMask;
}

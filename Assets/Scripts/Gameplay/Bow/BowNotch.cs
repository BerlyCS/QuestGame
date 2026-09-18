using UnityEngine;

/// <summary>
/// Trigger volume on the bow string that catches a passing <see cref="Arrow"/>
/// and nocks it, exactly like the socket-based notch from the OOT shooting
/// gallery. Once the arrow is nocked the <see cref="BowPullMeasurer"/> takes
/// over and eventually launches it.
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class BowNotch : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Weather the bow is currently held. Auto-found in the parent chain.")]
    Bow m_Bow;

    [SerializeField]
    [Tooltip("Transform the nocked arrow is parented to (the bow string middle).")]
    Transform m_StringMiddle;

    Arrow m_NockedArrow;

    public Arrow NockedArrow => m_NockedArrow;

    void Awake()
    {
        Collider trigger = GetComponent<Collider>();
        trigger.isTrigger = true;

        if (m_Bow == null)
        {
            m_Bow = GetComponentInParent<Bow>();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (m_NockedArrow != null || m_Bow == null || !m_Bow.IsHeld || m_StringMiddle == null)
        {
            return;
        }

        Arrow arrow = other.GetComponentInParent<Arrow>();
        if (arrow == null || arrow.IsNocked || arrow.IsLaunched || arrow.IsStuck)
        {
            return;
        }

        if (arrow.TryNock(m_StringMiddle))
        {
            m_NockedArrow = arrow;
        }
    }

    /// <summary>Called by the pull measurer once the arrow left the notch.</summary>
    public void ClearNocked()
    {
        m_NockedArrow = null;
    }

    public void InjectReferences(Bow bow, Transform stringMiddle)
    {
        m_Bow = bow;
        m_StringMiddle = stringMiddle;
    }
}

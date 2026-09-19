using UnityEngine;

/// <summary>
/// Trigger volume on the bow string that catches a passing <see cref="Arrow"/>
/// and nocks it, exactly like the socket-based notch from the OOT shooting
/// gallery. It can also spawn an arrow on its own (see <see cref="m_ArrowPrefab"/>)
/// so an arrow is always ready while the bow is held, which makes the bow
/// usable without having to first fetch an arrow from the quiver.
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

    [SerializeField]
    [Tooltip("Arrow spawned automatically while the bow is held. Optional.")]
    GameObject m_ArrowPrefab;

    [SerializeField]
    [Tooltip("Seconds between automatic arrows.")]
    float m_AutoNockDelay = 0.4f;

    Arrow m_NockedArrow;
    float m_NextAutoNockTime;

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

    void Update()
    {
        if (m_NockedArrow != null || m_Bow == null || !m_Bow.IsHeld)
        {
            return;
        }

        if (Time.time < m_NextAutoNockTime)
        {
            return;
        }

        EnsureNocked();
        m_NextAutoNockTime = Time.time + m_AutoNockDelay;
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

    /// <summary>
    /// Nocks an arrow if there is none. Spawns one from the configured
    /// <see cref="m_ArrowPrefab"/> when available. Returns true when an arrow
    /// is nocked after the call.
    /// </summary>
    public bool EnsureNocked()
    {
        if (m_NockedArrow != null)
        {
            return true;
        }

        if (m_Bow == null || !m_Bow.IsHeld || m_StringMiddle == null || m_ArrowPrefab == null)
        {
            return false;
        }

        GameObject arrowObject = Instantiate(m_ArrowPrefab, m_StringMiddle.position, m_StringMiddle.rotation);
        Arrow arrow = arrowObject.GetComponentInChildren<Arrow>(true);
        if (arrow == null)
        {
            Destroy(arrowObject);
            return false;
        }

        if (arrow.TryNock(m_StringMiddle))
        {
            m_NockedArrow = arrow;
            return true;
        }

        Destroy(arrowObject);
        return false;
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

    public void InjectArrowPrefab(GameObject arrowPrefab)
    {
        m_ArrowPrefab = arrowPrefab;
    }
}

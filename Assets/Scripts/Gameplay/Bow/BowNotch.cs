using UnityEngine;

/// <summary>
/// Trigger volume on the bow string that catches a passing <see cref="Arrow"/>
/// and nocks it, exactly like the socket-based notch from the OOT shooting
/// gallery. It can also spawn an arrow on its own (see <see cref="m_ArrowPrefab"/>)
/// so grabbing the bow always has an arrow ready without having to first fetch
/// one from the quiver: the arrow appears on the string, running from the grip
/// down to the rope, the moment a hand closes around the bow.
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
    [Tooltip("Arrow spawned when the string is grabbed. Optional.")]
    GameObject m_ArrowPrefab;

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

    /// <summary>
    /// Grabbing the bow is enough to get an arrow: the notch keeps one ready for
    /// as long as the bow is held, so the player only has to reach for the
    /// string. <see cref="EnsureNocked"/> is a no-op while an arrow is already
    /// there, so this never stacks arrows up.
    /// </summary>
    void Update()
    {
        if (m_NockedArrow == null)
        {
            EnsureNocked();
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

    /// <summary>
    /// Glues the nocked arrow to the string middle so it is drawn back together
    /// with the string. Re-parents defensively in case grabbing the arrow moved
    /// it elsewhere, which is what used to leave it stuck at its rest position.
    /// </summary>
    public void SyncNockedArrow()
    {
        if (m_NockedArrow == null || m_StringMiddle == null)
        {
            return;
        }

        Transform arrow = m_NockedArrow.transform;
        if (arrow.parent != m_StringMiddle)
        {
            arrow.SetParent(m_StringMiddle, true);
        }

        arrow.localPosition = Vector3.zero;
        arrow.localRotation = Quaternion.identity;
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

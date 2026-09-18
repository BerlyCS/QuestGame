using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Keeps a table prop available to the player. When the prop leaves its home
/// spot (knocked to the floor, carried away, or the table itself is moved), a
/// fresh copy reappears at the home transform right away, while the stray
/// object is removed <see cref="m_CleanupDelay"/> seconds later. Picking the
/// stray back up cancels the cleanup and removes the fresh copy.
/// </summary>
[DisallowMultipleComponent]
public class TableItemRespawn : MonoBehaviour
{
    [SerializeField]
    [Tooltip("World Y below which the object counts as fallen to the floor. Set very low to ignore height.")]
    float m_FloorY = 0.4f;

    [SerializeField]
    [Tooltip("Distance from the home spot beyond which the object counts as lost. Set high to only respawn on floor contact.")]
    float m_HomeRadius = 1f;

    [SerializeField]
    [Tooltip("Seconds the stray object lingers before it is removed.")]
    float m_CleanupDelay = 10f;

    Rigidbody m_Rigidbody;
    Grabbable m_Grabbable;

    bool m_Ready;
    Vector3 m_HomePosition;
    Quaternion m_HomeRotation;
    bool m_HomeKinematic;
    bool m_HomeGravity;

    bool m_Triggered;
    float m_CleanupTimer;
    GameObject m_Replacement;

    void Awake()
    {
        m_Rigidbody = GetComponent<Rigidbody>();
        m_Grabbable = GetComponent<Grabbable>();
    }

    void Start()
    {
        m_HomePosition = transform.position;
        m_HomeRotation = transform.rotation;

        if (m_Rigidbody != null)
        {
            m_HomeKinematic = m_Rigidbody.isKinematic;
            m_HomeGravity = m_Rigidbody.useGravity;
        }

        m_Ready = true;
    }

    bool BeingHeld()
    {
        return m_Grabbable != null && m_Grabbable.SelectingPointsCount > 0;
    }

    bool IsStray()
    {
        Vector3 position = transform.position;
        if (position.y < m_FloorY)
            return true;

        return Vector3.Distance(position, m_HomePosition) > m_HomeRadius;
    }

    void Update()
    {
        if (!m_Ready)
            return;

        if (BeingHeld())
        {
            // The player picked the stray back up: cancel the respawn.
            if (m_Replacement != null)
            {
                Destroy(m_Replacement);
                m_Replacement = null;
            }

            m_Triggered = false;
            m_CleanupTimer = 0f;
            return;
        }

        if (!m_Triggered)
        {
            if (IsStray())
            {
                m_Triggered = true;
                m_CleanupTimer = 0f;
                m_Replacement = Respawn();
            }

            return;
        }

        m_CleanupTimer += Time.deltaTime;
        if (m_CleanupTimer >= m_CleanupDelay)
            Destroy(gameObject);
    }

    GameObject Respawn()
    {
        GameObject replacement = Instantiate(gameObject, transform.parent);
        replacement.name = gameObject.name;

        Transform t = replacement.transform;
        t.SetPositionAndRotation(m_HomePosition, m_HomeRotation);
        t.localScale = transform.localScale;

        Rigidbody body = replacement.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = m_HomeKinematic;
            body.useGravity = m_HomeGravity;
        }

        return replacement;
    }
}

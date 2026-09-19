using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Keeps a table prop available to the player. When the prop leaves its home
/// spot (knocked to the floor or carried away) and is left untouched for
/// <see cref="m_CleanupDelay"/> seconds, it is returned to its home transform
/// instead of spawning a copy. Returning the existing object (rather than
/// instantiating a replacement) guarantees only a single instance of each prop
/// can ever exist, which fixes the runaway duplication that used to happen as
/// soon as a prop touched the floor.
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
    [Tooltip("Seconds the object may stay away from home before it is returned.")]
    float m_CleanupDelay = 10f;

    Rigidbody m_Rigidbody;
    Grabbable m_Grabbable;

    bool m_Ready;
    Vector3 m_HomePosition;
    Quaternion m_HomeRotation;
    float m_StrayTimer;

    void Awake()
    {
        m_Rigidbody = GetComponent<Rigidbody>();
        m_Grabbable = GetComponent<Grabbable>();
    }

    void Start()
    {
        m_HomePosition = transform.position;
        m_HomeRotation = transform.rotation;
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

        if (BeingHeld() || !IsStray())
        {
            m_StrayTimer = 0f;
            return;
        }

        m_StrayTimer += Time.deltaTime;
        if (m_StrayTimer >= m_CleanupDelay)
        {
            ReturnHome();
        }
    }

    void ReturnHome()
    {
        m_StrayTimer = 0f;

        if (m_Rigidbody != null && !m_Rigidbody.isKinematic)
        {
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
        }

        transform.SetPositionAndRotation(m_HomePosition, m_HomeRotation);

        if (m_Rigidbody != null && !m_Rigidbody.isKinematic)
        {
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
        }
    }
}

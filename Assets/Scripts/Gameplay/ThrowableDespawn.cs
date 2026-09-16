using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Despawns a thrown object a short time after it comes to rest on the ground,
/// or as soon as it falls below a kill plane. Ported from the Interaction SDK
/// Samples "Throwing" showcase (PooledThrowable) and adapted to simply destroy
/// the object, so it works for grabbables placed directly in the scene as well
/// as ones spawned at runtime.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(Grabbable))]
[DisallowMultipleComponent]
public class ThrowableDespawn : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Objects that fall below this world Y are despawned immediately.")]
    float m_KillPlaneY = -1f;

    [SerializeField]
    [Tooltip("Seconds to wait after the object has come to rest before despawning it.")]
    float m_SecondsAfterResting = 3f;

    [SerializeField]
    [Tooltip("Hard cap on how long the object may live after being thrown, even if it never settles.")]
    float m_MaxFlightSeconds = 15f;

    [SerializeField]
    [Tooltip("Linear and angular speed below which the object is considered to be resting.")]
    float m_RestSpeedThreshold = 0.25f;

    Rigidbody m_Rigidbody;
    Grabbable m_Grabbable;
    bool m_Started;
    bool m_Thrown;
    float m_RestTimer;
    float m_FlightTimer;

    void Awake()
    {
        m_Rigidbody = GetComponent<Rigidbody>();
        m_Grabbable = GetComponent<Grabbable>();
    }

    void Start()
    {
        this.BeginStart(ref m_Started);
        this.AssertField(m_Rigidbody, nameof(m_Rigidbody));
        this.AssertField(m_Grabbable, nameof(m_Grabbable));
        this.EndStart(ref m_Started);
    }

    void OnEnable()
    {
        if (m_Started)
            m_Grabbable.VelocityThrow.WhenThrown += HandleThrown;
    }

    void OnDisable()
    {
        if (m_Started)
            m_Grabbable.VelocityThrow.WhenThrown -= HandleThrown;
    }

    void HandleThrown(Vector3 velocity, Vector3 torque)
    {
        m_Thrown = true;
        m_RestTimer = 0f;
        m_FlightTimer = 0f;
    }

    void FixedUpdate()
    {
        if (m_Grabbable.SelectingPointsCount > 0)
        {
            ResetState();
            return;
        }

        if (transform.position.y <= m_KillPlaneY)
        {
            Despawn();
            return;
        }

        if (!m_Thrown)
            return;

        m_FlightTimer += Time.fixedDeltaTime;

        float threshold = m_RestSpeedThreshold * m_RestSpeedThreshold;
        bool resting = m_Rigidbody.linearVelocity.sqrMagnitude <= threshold
                       && m_Rigidbody.angularVelocity.sqrMagnitude <= threshold;

        m_RestTimer = resting ? m_RestTimer + Time.fixedDeltaTime : 0f;

        if (m_RestTimer >= m_SecondsAfterResting || m_FlightTimer >= m_MaxFlightSeconds)
            Despawn();
    }

    void ResetState()
    {
        m_Thrown = false;
        m_RestTimer = 0f;
        m_FlightTimer = 0f;
    }

    /// <summary>
    /// Removes this object from the scene.
    /// </summary>
    public void Despawn()
    {
        Destroy(gameObject);
    }
}

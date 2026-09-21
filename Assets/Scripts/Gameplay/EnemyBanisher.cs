using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Attached to the banishing axe. A <b>held</b> banisher strikes the enemies
/// overlapping the blade once per contact - the blade has to leave and come
/// back - so a tough enemy survives a glancing hit instead of dying the frame
/// it touches the sphere. A <b>thrown</b> one stays lethal for as long as it
/// flies, so an aimed axe cuts through a skeleton instead of bouncing off it,
/// and goes inert the moment it comes to rest: an axe lying on the ground must
/// not kill the skeletons that wander past it.
///
/// Uses overlap queries rather than physics trigger callbacks so it still works
/// when the grabbed object's rigidbody is kinematic (kinematic-vs-kinematic
/// contacts do not raise trigger events), and sweeps the blade's path while
/// flying so a fast throw cannot tunnel through an enemy between frames.
/// </summary>
[DisallowMultipleComponent]
public class EnemyBanisher : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Radius around the blade in which enemies are struck.")]
    float m_Radius = 0.4f;

    [SerializeField]
    [Tooltip("Layers that can contain enemies.")]
    LayerMask m_EnemyLayers = ~0;

    [Header("Feel")]
    [SerializeField] float m_BanishHapticAmplitude = 0.55f;
    [SerializeField] float m_BanishHapticDuration = 0.09f;

    [Header("Thrown")]
    [Tooltip("Hits a thrown weapon deals to an enemy that has no banish phase " +
             "(a Lanzahuesos), so it goes down whatever its hit count.")]
    [SerializeField] int m_ThrownDamage = 1;

    [SerializeField]
    [Tooltip("Seconds a thrown weapon has to stay still before the throw stops " +
             "counting and the weapon goes inert again.")]
    float m_RestingDelay = 0.25f;

    [SerializeField]
    [Tooltip("Linear and angular speed below which a thrown weapon counts as resting.")]
    float m_RestSpeedThreshold = 0.35f;

    readonly Collider[] m_Overlaps = new Collider[16];
    readonly HashSet<Component> m_Struck = new HashSet<Component>();
    readonly HashSet<Component> m_StruckThisFrame = new HashSet<Component>();

    Grabbable m_Grabbable;
    Rigidbody m_Rigidbody;
    bool m_SubscribedToThrow;
    bool m_Thrown;
    float m_RestTimer;
    Vector3 m_LastBladePosition;
    Phase m_Phase = Phase.Idle;

    enum Phase
    {
        Idle,
        Held,
        Flying
    }

    void Awake()
    {
        m_Grabbable = GetComponentInChildren<Grabbable>(true);
        m_Rigidbody = GetComponent<Rigidbody>();
    }

    void Start() => SubscribeToThrow();

    void OnEnable() => SubscribeToThrow();

    void OnDisable() => UnsubscribeFromThrow();

    void SubscribeToThrow()
    {
        // VelocityThrow is created on demand by the Grabbable, so this is safe
        // to call before the Grabbable has run its own Awake/Start.
        if (m_SubscribedToThrow || m_Grabbable == null)
            return;

        m_Grabbable.VelocityThrow.WhenThrown += HandleThrown;
        m_SubscribedToThrow = true;
    }

    void UnsubscribeFromThrow()
    {
        if (!m_SubscribedToThrow)
            return;

        if (m_Grabbable != null)
            m_Grabbable.VelocityThrow.WhenThrown -= HandleThrown;

        m_SubscribedToThrow = false;
    }

    void HandleThrown(Vector3 velocity, Vector3 torque)
    {
        m_Thrown = true;
        m_RestTimer = 0f;
    }

    void FixedUpdate()
    {
        if (!m_Thrown)
            return;

        if (m_Grabbable != null && m_Grabbable.SelectingPointsCount > 0)
        {
            m_Thrown = false;
            return;
        }

        if (m_Rigidbody == null)
            return;

        // The throw event can land before the launch velocity has been applied,
        // so the weapon has to stay still for a moment before it counts as landed.
        float threshold = m_RestSpeedThreshold * m_RestSpeedThreshold;
        bool resting = m_Rigidbody.linearVelocity.sqrMagnitude <= threshold
                       && m_Rigidbody.angularVelocity.sqrMagnitude <= threshold;

        m_RestTimer = resting ? m_RestTimer + Time.fixedDeltaTime : 0f;

        if (m_RestTimer >= m_RestingDelay)
            m_Thrown = false;
    }

    void Update()
    {
        Phase phase = IsHeldNow()
            ? Phase.Held
            : (m_Thrown ? Phase.Flying : Phase.Idle);

        // Each phase starts from a clean slate: leaving a hand (or coming to
        // rest) forgets who was touched, so the next strike always lands.
        if (phase != m_Phase)
        {
            m_Phase = phase;
            m_Struck.Clear();
            m_LastBladePosition = transform.position;
        }

        switch (phase)
        {
            case Phase.Held:
                StrikeHeld();
                break;
            case Phase.Flying:
                StrikeFlying();
                break;
        }
    }

    /// <summary>Strikes whatever the blade is overlapping right now.</summary>
    void StrikeHeld()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, m_Radius, m_Overlaps, m_EnemyLayers, QueryTriggerInteraction.Ignore);

        bool struck = false;
        m_StruckThisFrame.Clear();

        for (int i = 0; i < count; i++)
        {
            var skeleton = m_Overlaps[i].GetComponentInParent<Skeleton>();
            if (skeleton == null || !skeleton.IsAlive)
                continue;

            m_StruckThisFrame.Add(skeleton);

            // One strike per contact: the blade must leave the enemy and come
            // back before it can strike again.
            if (m_Struck.Contains(skeleton))
                continue;

            skeleton.Banish();
            struck = true;
        }

        // Remember this frame's contacts for the one-strike-per-contact check.
        m_Struck.Clear();
        m_Struck.UnionWith(m_StruckThisFrame);

        if (struck && InteractorHaptics.TryGetHoldingController(gameObject, out var controller))
            HapticsUtility.Pulse(controller, m_BanishHapticAmplitude, m_BanishHapticDuration);
    }

    /// <summary>
    /// Strikes everything the blade swept through since the previous frame, so a
    /// fast throw cannot tunnel past an enemy. Each enemy is struck once per throw.
    /// </summary>
    void StrikeFlying()
    {
        Vector3 blade = transform.position;
        Vector3 travel = blade - m_LastBladePosition;
        m_LastBladePosition = blade;

        int count = travel.sqrMagnitude > 1e-8f
            ? Physics.OverlapCapsuleNonAlloc(
                blade - travel, blade, m_Radius, m_Overlaps, m_EnemyLayers, QueryTriggerInteraction.Ignore)
            : Physics.OverlapSphereNonAlloc(
                blade, m_Radius, m_Overlaps, m_EnemyLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
            Strike(m_Overlaps[i]);
    }

    /// <summary>
    /// Backstop for a graze the swept blade missed but the axe's own collider
    /// caught. Ignored unless the weapon is mid-throw, so a resting axe that an
    /// enemy nudges cannot kill the enemy that bumped into it.
    /// </summary>
    void OnCollisionEnter(Collision collision)
    {
        if (m_Thrown)
            Strike(collision.collider);
    }

    bool Strike(Collider other)
    {
        if (other == null)
            return false;

        var skeleton = other.GetComponentInParent<Skeleton>();
        if (skeleton != null)
        {
            if (!skeleton.IsAlive || !m_Struck.Add(skeleton))
                return false;

            skeleton.Banish();
            return true;
        }

        var boneThrower = other.GetComponentInParent<BoneThrower>();
        if (boneThrower != null && boneThrower.IsAlive && m_Struck.Add(boneThrower))
        {
            boneThrower.TakeHit(m_ThrownDamage);
            return true;
        }

        return false;
    }

    bool IsHeldNow()
    {
        // An object with no Grabbable at all (e.g. a fixed trap) is always live.
        return m_Grabbable == null || m_Grabbable.SelectingPointsCount > 0;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, m_Radius);
    }
}

using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Attached to the banishing axe. Only a <b>held</b> banisher is lethal: one
/// left resting on the bench must not kill the skeletons that wander past it.
/// While held it strikes enemies overlapping the blade once per contact - the
/// blade has to leave and come back - so a tough enemy survives a glancing hit
/// instead of dying the frame it touches the sphere. Uses an overlap query
/// rather than physics trigger callbacks so it still works when the grabbed
/// object's rigidbody is kinematic (kinematic-vs-kinematic contacts do not
/// raise trigger events).
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

    readonly Collider[] m_Overlaps = new Collider[16];
    readonly HashSet<Skeleton> m_Touching = new HashSet<Skeleton>();
    readonly HashSet<Skeleton> m_TouchingThisFrame = new HashSet<Skeleton>();

    Grabbable m_Grabbable;

    void Awake()
    {
        m_Grabbable = GetComponentInChildren<Grabbable>(true);
    }

    void Update()
    {
        // A resting weapon is inert; only a hand-held one strikes.
        if (!IsHeldNow())
        {
            m_Touching.Clear();
            return;
        }

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, m_Radius, m_Overlaps, m_EnemyLayers, QueryTriggerInteraction.Ignore);

        bool struck = false;
        m_TouchingThisFrame.Clear();

        for (int i = 0; i < count; i++)
        {
            var skeleton = m_Overlaps[i].GetComponentInParent<Skeleton>();
            if (skeleton == null || !skeleton.IsAlive)
                continue;

            m_TouchingThisFrame.Add(skeleton);

            // One strike per contact: the blade must leave the enemy and come
            // back before it can strike again.
            if (m_Touching.Contains(skeleton))
                continue;

            skeleton.Banish();
            struck = true;
        }

        // Remember this frame's contacts for the one-strike-per-contact check.
        m_Touching.Clear();
        m_Touching.UnionWith(m_TouchingThisFrame);

        if (struck && InteractorHaptics.TryGetHoldingController(gameObject, out var controller))
            HapticsUtility.Pulse(controller, m_BanishHapticAmplitude, m_BanishHapticDuration);
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

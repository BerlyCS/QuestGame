using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The resortera's shot: a real ballistic projectile (gravity + the initial
/// velocity SlingEmberHandle.Fire gives it) - unlike the Lanzahuesos' bone,
/// there's no fixed target here, the player aims it themselves. One hit kills
/// either enemy (see enemigos.md/armas.md: a brasa kills a Lanzahuesos
/// outright and is enough hit points for a Caminante's two-brasa kill via
/// TakeHit). A miss lights up where it lands for a few seconds - a failed
/// shot still reveals what's coming. Fires OnConsumed whenever it resolves
/// (hit or miss) so EmberAmmoSpawner knows to restock the weapon rack.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class EmberProjectile : MonoBehaviour
{
    [SerializeField] int m_HitPoints = 1;
    [SerializeField] float m_MissGlowDuration = 3f;
    [SerializeField] float m_MissGlowScale = 2f;
    [SerializeField] Color m_GlowColor = new Color(1f, 0.6f, 0.15f);

    [Header("Aim assist (hand-tracking aim is noisy, see armas.md)")]
    [SerializeField] float m_AssistConeDegrees = 12f;
    [SerializeField] float m_AssistDuration = 0.6f;
    [SerializeField] float m_AssistTurnSpeed = 240f;

    [SerializeField] UnityEvent m_OnConsumed = new UnityEvent();

    Rigidbody m_Rigidbody;
    bool m_HasHit;
    Transform m_AssistTarget;
    float m_AssistUntil;

    public UnityEvent OnConsumed => m_OnConsumed;

    void Awake()
    {
        m_Rigidbody = GetComponent<Rigidbody>();
    }

    public void Launch(Vector3 velocity)
    {
        m_Rigidbody.linearVelocity = velocity;
        m_AssistTarget = FindAssistTarget(velocity.normalized);
        m_AssistUntil = Time.time + m_AssistDuration;
    }

    /// <summary>
    /// For a fraction of a second after launch, bends the flight toward the living
    /// enemy closest to the aim line (inside a narrow cone). It only fixes a slightly
    /// wrong aim - a shot at empty ground stays a miss.
    /// </summary>
    void FixedUpdate()
    {
        if (m_AssistTarget == null || Time.time > m_AssistUntil || m_HasHit)
            return;

        Vector3 velocity = m_Rigidbody.linearVelocity;
        Vector3 toTarget = (m_AssistTarget.position + Vector3.up * 0.9f) - m_Rigidbody.position;
        m_Rigidbody.linearVelocity = Vector3.RotateTowards(
            velocity, toTarget.normalized * velocity.magnitude,
            m_AssistTurnSpeed * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
    }

    Transform FindAssistTarget(Vector3 direction)
    {
        Transform best = null;
        float bestAngle = m_AssistConeDegrees;
        Vector3 origin = transform.position;

        foreach (var skeleton in Object.FindObjectsByType<Skeleton>())
            if (skeleton.IsAlive)
                Consider(skeleton.transform);

        foreach (var thrower in Object.FindObjectsByType<BoneThrower>())
            if (thrower.IsAlive)
                Consider(thrower.transform);

        return best;

        void Consider(Transform candidate)
        {
            float angle = Vector3.Angle(direction, candidate.position + Vector3.up * 0.9f - origin);
            if (angle < bestAngle)
            {
                bestAngle = angle;
                best = candidate;
            }
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (m_HasHit)
            return;

        m_HasHit = true;

        var skeleton = collision.collider.GetComponentInParent<Skeleton>();
        if (skeleton != null && skeleton.IsAlive)
        {
            ProceduralSfx.PlayAt(ProceduralSfx.EnemyHit, collision.GetContact(0).point);
            skeleton.TakeHit(m_HitPoints);
            Consume();
            return;
        }

        var boneThrower = collision.collider.GetComponentInParent<BoneThrower>();
        if (boneThrower != null && boneThrower.IsAlive)
        {
            ProceduralSfx.PlayAt(ProceduralSfx.EnemyHit, collision.GetContact(0).point);
            boneThrower.TakeHit(m_HitPoints);
            Consume();
            return;
        }

        // Missed: light up where it landed instead of just vanishing (see armas.md).
        FadingGlow.Spawn(collision.GetContact(0).point, m_MissGlowScale, m_GlowColor, m_MissGlowDuration);
        Consume();
    }

    void Consume()
    {
        m_OnConsumed.Invoke();
        Destroy(gameObject);
    }
}

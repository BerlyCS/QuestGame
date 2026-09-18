using UnityEngine;

/// <summary>
/// The resortera's shot: a real ballistic projectile (gravity + the initial
/// velocity Slingshot.Fire gives it) - unlike the Lanzahuesos' bone, there's
/// no fixed target here, the player aims it themselves. One hit kills either
/// enemy (see enemigos.md/armas.md: a brasa kills a Lanzahuesos outright and
/// is enough hit points for a Caminante's two-brasa kill via TakeHit). A miss
/// lights up where it lands for a few seconds - a failed shot still reveals
/// what's coming.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class EmberProjectile : MonoBehaviour
{
    [SerializeField] int m_HitPoints = 1;
    [SerializeField] float m_MissGlowDuration = 3f;
    [SerializeField] float m_MissGlowScale = 2f;
    [SerializeField] Color m_GlowColor = new Color(1f, 0.6f, 0.15f);

    Rigidbody m_Rigidbody;
    bool m_HasHit;

    void Awake()
    {
        m_Rigidbody = GetComponent<Rigidbody>();
    }

    public void Launch(Vector3 velocity)
    {
        m_Rigidbody.linearVelocity = velocity;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (m_HasHit)
            return;

        m_HasHit = true;

        var skeleton = collision.collider.GetComponentInParent<Skeleton>();
        if (skeleton != null && skeleton.IsAlive)
        {
            skeleton.TakeHit(m_HitPoints);
            Destroy(gameObject);
            return;
        }

        var boneThrower = collision.collider.GetComponentInParent<BoneThrower>();
        if (boneThrower != null && boneThrower.IsAlive)
        {
            boneThrower.TakeHit(m_HitPoints);
            Destroy(gameObject);
            return;
        }

        // Missed: light up where it landed instead of just vanishing (see armas.md).
        FadingGlow.Spawn(collision.GetContact(0).point, m_MissGlowScale, m_GlowColor, m_MissGlowDuration);
        Destroy(gameObject);
    }
}

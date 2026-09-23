using UnityEngine;

/// <summary>
/// A bone lobbed by a Lanzahuesos. Follows a scripted parabolic arc from
/// thrower to campfire over a fixed duration - not real physics - so it
/// always lands exactly on target regardless of distance (see enemigos.md:
/// "radios fijos, nada aleatorio"). Drains fuel on arrival, then removes
/// itself.
/// </summary>
[DisallowMultipleComponent]
public class ThrownBone : MonoBehaviour
{
    Vector3 m_Start;
    Vector3 m_End;
    float m_Duration;
    float m_ArcHeight;
    CampfireFuel m_Campfire;
    float m_FuelDamage;

    float m_Elapsed;
    bool m_HasImpacted;

    public void Launch(Vector3 start, Vector3 end, float duration, float arcHeight, CampfireFuel campfire, float fuelDamage)
    {
        m_Start = start;
        m_End = end;
        m_Duration = Mathf.Max(0.01f, duration);
        m_ArcHeight = arcHeight;
        m_Campfire = campfire;
        m_FuelDamage = fuelDamage;

        transform.position = start;
    }

    void Update()
    {
        if (m_HasImpacted)
            return;

        m_Elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(m_Elapsed / m_Duration);

        Vector3 flat = Vector3.Lerp(m_Start, m_End, t);
        float height = 4f * m_ArcHeight * t * (1f - t);
        transform.position = flat + Vector3.up * height;

        if (t >= 1f)
            Impact();
    }

    void Impact()
    {
        m_HasImpacted = true;

        if (m_Campfire != null)
        {
            m_Campfire.AddFuel(-m_FuelDamage);
            m_Campfire.PlayImpact();
        }

        Destroy(gameObject);
    }
}

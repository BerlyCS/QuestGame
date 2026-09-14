using UnityEngine;

/// <summary>
/// A grabbable log. When it enters the campfire's fuel trigger it is consumed
/// and adds fuel to the fire. Grab behaviour is provided by the Meta
/// Interaction SDK components attached by the scene builder.
/// </summary>
public class Log : MonoBehaviour
{
    [SerializeField]
    [Tooltip("How much fuel this log adds when thrown into the fire.")]
    float m_FuelValue = 0.25f;

    [SerializeField]
    [Tooltip("Optional effect spawned when the log is consumed.")]
    GameObject m_ConsumeEffect;

    bool m_Consumed;

    void OnTriggerEnter(Collider other)
    {
        if (m_Consumed)
            return;

        var campfire = other.GetComponentInParent<CampfireFuel>();
        if (campfire == null)
            return;

        m_Consumed = true;
        campfire.AddFuel(m_FuelValue);

        if (m_ConsumeEffect != null)
            Instantiate(m_ConsumeEffect, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }
}

using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A grabbable log. Once the player has grabbed it at least once, it is
/// consumed and adds fuel to the fire as soon as it overlaps the campfire's
/// fuel trigger. Requiring a grab first means a log staged so it already
/// touches the fire (see GameSceneBuilder's tutorial log) sits there inertly
/// as a visual hint until the player actually picks it up, instead of
/// igniting itself the moment the scene loads. Grab behaviour is provided by
/// the Meta Interaction SDK components attached by the scene builder.
/// </summary>
[RequireComponent(typeof(Grabbable))]
public class Log : MonoBehaviour
{
    [SerializeField]
    [Tooltip("How much fuel this log adds when thrown into the fire, in seconds.")]
    float m_FuelValue = 25f;

    [SerializeField]
    [Tooltip("Optional effect spawned when the log is consumed.")]
    GameObject m_ConsumeEffect;

    [SerializeField] UnityEvent m_OnConsumed = new UnityEvent();

    Grabbable m_Grabbable;
    bool m_HasBeenGrabbed;
    bool m_Consumed;

    public UnityEvent OnConsumed => m_OnConsumed;

    void Awake()
    {
        m_Grabbable = GetComponent<Grabbable>();
        m_Grabbable.WhenPointerEventRaised += HandlePointerEvent;
    }

    void OnDestroy()
    {
        m_Grabbable.WhenPointerEventRaised -= HandlePointerEvent;
    }

    void HandlePointerEvent(PointerEvent evt)
    {
        if (evt.Type == PointerEventType.Select)
            m_HasBeenGrabbed = true;
    }

    void OnTriggerEnter(Collider other) => TryConsume(other);
    void OnTriggerStay(Collider other) => TryConsume(other);

    void TryConsume(Collider other)
    {
        if (m_Consumed || !m_HasBeenGrabbed)
            return;

        var campfire = other.GetComponentInParent<CampfireFuel>();
        if (campfire == null)
            return;

        m_Consumed = true;
        campfire.AddFuel(m_FuelValue);

        if (m_ConsumeEffect != null)
            Instantiate(m_ConsumeEffect, transform.position, Quaternion.identity);

        m_OnConsumed.Invoke();
        Destroy(gameObject);
    }
}

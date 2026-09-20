using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

/// <summary>
/// Marker for the bow. All of the interesting behaviour lives in
/// <see cref="BowNotch"/> and <see cref="BowPullMeasurer"/>; this component
/// exposes whether the bow is currently held so those systems can gate on it,
/// and constrains the bow to a single grabbing hand so the free hand can draw
/// the string. Ported from the OOT shooting gallery (XRGrabInteractable) to the
/// Meta Interaction SDK.
/// </summary>
[DisallowMultipleComponent]
public class Bow : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Grabbable that represents the bow being held. Auto-found when empty.")]
    Grabbable m_Grabbable;

    [SerializeField]
    [Tooltip("When enabled the bow can only be held by a single hand at a time, so the " +
        "free hand can draw the string instead of also grabbing the bow frame.")]
    bool m_SingleHandOnly = true;

    public Grabbable Grabbable
    {
        get
        {
            if (m_Grabbable == null)
            {
                m_Grabbable = GetComponentInChildren<Grabbable>(true);
            }
            return m_Grabbable;
        }
    }

    /// <summary>True while a hand is holding the bow.</summary>
    public bool IsHeld => Grabbable != null && Grabbable.SelectingPointsCount > 0;

    public void InjectGrabbable(Grabbable grabbable) => m_Grabbable = grabbable;

    void Awake()
    {
        if (m_SingleHandOnly)
        {
            RestrictToSingleHand();
        }
    }

    /// <summary>
    /// Limits the bow's Grabbable and grabbing interactables to one selecting hand.
    /// The string's draw grip is a separate Grabbable, so the other hand is still
    /// free to grab it and pull without ever selecting the bow frame.
    /// </summary>
    void RestrictToSingleHand()
    {
        if (Grabbable != null)
        {
            Grabbable.MaxGrabPoints = 1;
        }

        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null)
        {
            return;
        }

        foreach (GrabInteractable grab in GetComponentsInChildren<GrabInteractable>(true))
        {
            if (grab.Rigidbody == body)
            {
                grab.MaxSelectingInteractors = 1;
            }
        }

        foreach (HandGrabInteractable handGrab in GetComponentsInChildren<HandGrabInteractable>(true))
        {
            if (handGrab.Rigidbody == body)
            {
                handGrab.MaxSelectingInteractors = 1;
            }
        }
    }
}

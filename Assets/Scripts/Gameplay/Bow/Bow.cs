using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

/// <summary>
/// Placeholder marker for the bow. All of the interesting behaviour lives in
/// <see cref="BowNotch"/> and <see cref="BowPullMeasurer"/>; this component just
/// exposes whether the bow is currently held so those systems can gate on it.
/// Ported from the OOT shooting gallery (XRGrabInteractable) to the Meta
/// Interaction SDK.
/// </summary>
[DisallowMultipleComponent]
public class Bow : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Grabbable that represents the bow being held. Auto-found when empty.")]
    Grabbable m_Grabbable;

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
}

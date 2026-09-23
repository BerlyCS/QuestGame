using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

/// <summary>
/// The grabbable "string" of the bow. While a hand holds it the pull amount is
/// derived from how far the hand has been dragged back along the fixed draw
/// axis. Releasing past <see cref="m_ReleaseThreshold"/> fires the nocked arrow,
/// otherwise the arrow simply drops off the string.
///
/// Replaces the XR Interaction Toolkit based PullMeasurer from the OOT shooting
/// gallery with the Meta Interaction SDK grabbing used by this project.
/// </summary>
[DisallowMultipleComponent]
public class BowPullMeasurer : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Start of the draw axis (arrow at rest).")]
    Transform m_Start;

    [SerializeField]
    [Tooltip("End of the draw axis (fully drawn).")]
    Transform m_End;

    [SerializeField]
    [Tooltip("Transform that visually follows the draw, usually the string middle.")]
    Transform m_StringMiddle;

    [SerializeField]
    [Tooltip("Notch that holds the nocked arrow.")]
    BowNotch m_Notch;

    [SerializeField]
    [Range(0f, 1f)]
    float m_ReleaseThreshold = 0.25f;

    [SerializeField] AudioSource m_PullAudioSource;
    [SerializeField] float m_PullAudioThreshold = 0.3f;

    [SerializeField]
    [Tooltip("Seconds for the string to spring back to rest after release.")]
    [Range(0.02f, 0.6f)]
    float m_ReturnTime = 0.12f;

    GrabInteractable m_GrabInteractable;
    HandGrabInteractable m_HandGrabInteractable;
    GrabInteractor m_GrabInteractor;
    HandGrabInteractor m_HandGrabInteractor;
    bool m_PullAudioPlayed;

    bool m_Returning;
    float m_ReturnElapsed;
    Vector3 m_StringReturnFromLocal;
    Vector3 m_GripReturnFromLocal;
    Quaternion m_GripReturnFromRotation;

    Transform m_StringMiddleParent;
    Transform m_GripParent;
    Vector3 m_RestLocalPosition;
    Vector3 m_GripLocalPosition;
    Quaternion m_GripLocalRotation;

    /// <summary>Normalized 0..1 draw amount.</summary>
    public float PullAmount { get; private set; }

    /// <summary>
    /// Number of arrows this bow has fired. Only released draws count, so
    /// letting the string snap back does not tick it up. The tutorial markers
    /// stay up until this reaches their target.
    /// </summary>
    public int ShotsFired { get; private set; }

    void Awake()
    {
        m_GrabInteractable = GetComponentInChildren<GrabInteractable>(true);
        m_HandGrabInteractable = GetComponentInChildren<HandGrabInteractable>(true);

        if (m_StringMiddle != null)
        {
            m_StringMiddleParent = m_StringMiddle.parent;
            m_RestLocalPosition = m_StringMiddle.localPosition;
        }

        m_GripParent = transform.parent;
        m_GripLocalPosition = transform.localPosition;
        m_GripLocalRotation = transform.localRotation;
    }

    /// <summary>
    /// World-space rest position of the draw grip, derived from its local rest
    /// pose each frame so the bow can be carried while the draw is not active.
    /// </summary>
    Vector3 GripRestWorldPosition()
    {
        return m_GripParent != null
            ? m_GripParent.TransformPoint(m_GripLocalPosition)
            : transform.position;
    }

    /// <summary>
    /// World-space rest position of the string middle. It is derived from the
    /// middle's local rest pose every frame so the string follows the bow while
    /// the bow is being carried instead of staying anchored in world space.
    /// </summary>
    Vector3 RestWorldPosition()
    {
        if (m_StringMiddle == null)
        {
            return Vector3.zero;
        }

        return m_StringMiddleParent != null
            ? m_StringMiddleParent.TransformPoint(m_RestLocalPosition)
            : m_StringMiddle.position;
    }

    void OnEnable()
    {
        if (m_GrabInteractable != null)
        {
            m_GrabInteractable.WhenSelectingInteractorAdded.Action += OnGrabAdded;
            m_GrabInteractable.WhenSelectingInteractorRemoved.Action += OnGrabRemoved;
        }

        if (m_HandGrabInteractable != null)
        {
            m_HandGrabInteractable.WhenSelectingInteractorAdded.Action += OnHandGrabAdded;
            m_HandGrabInteractable.WhenSelectingInteractorRemoved.Action += OnHandGrabRemoved;
        }
    }

    void OnDisable()
    {
        if (m_GrabInteractable != null)
        {
            m_GrabInteractable.WhenSelectingInteractorAdded.Action -= OnGrabAdded;
            m_GrabInteractable.WhenSelectingInteractorRemoved.Action -= OnGrabRemoved;
        }

        if (m_HandGrabInteractable != null)
        {
            m_HandGrabInteractable.WhenSelectingInteractorAdded.Action -= OnHandGrabAdded;
            m_HandGrabInteractable.WhenSelectingInteractorRemoved.Action -= OnHandGrabRemoved;
        }
    }

    void OnGrabAdded(GrabInteractor interactor)
    {
        m_GrabInteractor = interactor;
        if (m_Notch != null)
        {
            m_Notch.EnsureNocked();
        }
    }

    void OnGrabRemoved(GrabInteractor interactor) => Release();

    void OnHandGrabAdded(HandGrabInteractor interactor)
    {
        m_HandGrabInteractor = interactor;
        if (m_Notch != null)
        {
            m_Notch.EnsureNocked();
        }
    }

    void OnHandGrabRemoved(HandGrabInteractor interactor) => Release();

    void Update()
    {
        Transform hand = GetHandTransform();

        if (hand != null && m_Start != null && m_End != null)
        {
            m_Returning = false;

            // Keep the arrow nocked for as long as the string is being held, even
            // if the bow was grabbed after the string, so the draw always has an
            // arrow.
            if (m_Notch != null && m_Notch.NockedArrow == null)
            {
                m_Notch.EnsureNocked();
            }

            // The grip itself is what the pinch drags around, so reading its
            // position makes the string track the pinch exactly instead of the
            // hand root the interactor happens to sit on.
            PullAmount = CalculatePull(transform.position, GripRestWorldPosition());

            if (m_StringMiddle != null)
            {
                m_StringMiddle.position = Vector3.Lerp(RestWorldPosition(), m_End.position, PullAmount);
            }

            // The nocked arrow rides the string so it is drawn back with it too.
            if (m_Notch != null)
            {
                m_Notch.SyncNockedArrow();
            }

            if (!m_PullAudioPlayed && PullAmount > m_PullAudioThreshold)
            {
                if (m_PullAudioSource != null)
                {
                    m_PullAudioSource.Play();
                }
                m_PullAudioPlayed = true;
            }
            else if (m_PullAudioPlayed && PullAmount <= m_PullAudioThreshold)
            {
                m_PullAudioPlayed = false;
            }
        }
        else
        {
            Return();
        }
    }

    Transform GetHandTransform()
    {
        if (m_GrabInteractor != null)
        {
            return m_GrabInteractor.transform;
        }

        if (m_HandGrabInteractor != null)
        {
            return m_HandGrabInteractor.transform;
        }

        return null;
    }

    /// <summary>
    /// Kicks off the string springing back to rest from wherever it was let go.
    /// Called on release so a fired (or abandoned) draw does not teleport home.
    /// </summary>
    void BeginReturn()
    {
        PullAmount = 0f;
        m_Returning = true;
        m_ReturnElapsed = 0f;

        m_StringReturnFromLocal = m_StringMiddle != null ? m_StringMiddle.localPosition : Vector3.zero;
        m_GripReturnFromLocal = transform.localPosition;
        m_GripReturnFromRotation = transform.localRotation;
    }

    void Return()
    {
        if (!m_Returning)
        {
            BeginReturn();
        }

        m_ReturnElapsed += Time.deltaTime;
        float duration = Mathf.Max(m_ReturnTime, 0.0001f);
        float t = Mathf.Clamp01(m_ReturnElapsed / duration);

        // Ease out cubic reads as a quick, springy snap rather than a linear
        // slide back to the rest pose.
        float eased = 1f - Mathf.Pow(1f - t, 3f);

        if (m_StringMiddle != null)
        {
            m_StringMiddle.localPosition = Vector3.Lerp(m_StringReturnFromLocal, m_RestLocalPosition, eased);
        }

        // The Interaction SDK moves the grip with the hand while grabbed; ease it
        // back to its rest pose instead of snapping so it does not drift or pop.
        transform.localPosition = Vector3.Lerp(m_GripReturnFromLocal, m_GripLocalPosition, eased);
        transform.localRotation = Quaternion.Slerp(m_GripReturnFromRotation, m_GripLocalRotation, eased);

        if (t >= 1f)
        {
            m_Returning = false;
            m_PullAudioPlayed = false;
        }
    }

    void Release()
    {
        float pullAmount = PullAmount;

        m_GrabInteractor = null;
        m_HandGrabInteractor = null;

        Arrow arrow = m_Notch != null ? m_Notch.NockedArrow : null;
        if (arrow != null && pullAmount > m_ReleaseThreshold)
        {
            // Only a real draw fires the arrow. A weak release simply lets the
            // string snap back, keeping the arrow nocked so it never drops into
            // the world and accumulates.
            arrow.Launch(pullAmount);
            m_Notch.ClearNocked();
            ShotsFired++;
        }

        // Animate the return rather than teleporting the string back home.
        BeginReturn();
    }

    float CalculatePull(Vector3 pullPosition, Vector3 origin)
    {
        Vector3 targetDirection = m_End.position - m_Start.position;
        if (targetDirection.sqrMagnitude <= Mathf.Epsilon)
        {
            return 0f;
        }

        targetDirection.Normalize();

        // Distance from the grip's rest pose to the far end of the draw, so a
        // full pull still reads as 1 even though the grip starts a little behind
        // the string's rest point.
        float maxLength = Vector3.Dot(m_End.position - origin, targetDirection);
        if (maxLength <= Mathf.Epsilon)
        {
            return 0f;
        }

        float pullValue = Vector3.Dot(pullPosition - origin, targetDirection) / maxLength;
        return Mathf.Clamp01(pullValue);
    }

    void OnDrawGizmosSelected()
    {
        if (m_Start != null && m_End != null)
        {
            Gizmos.DrawLine(m_Start.position, m_End.position);
        }
    }

    public void InjectReferences(
        Transform start,
        Transform end,
        Transform stringMiddle,
        BowNotch notch,
        AudioSource pullAudioSource)
    {
        m_Start = start;
        m_End = end;
        m_StringMiddle = stringMiddle;
        m_Notch = notch;
        m_PullAudioSource = pullAudioSource;
    }
}

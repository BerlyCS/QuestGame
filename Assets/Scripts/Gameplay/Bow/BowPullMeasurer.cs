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

    GrabInteractable m_GrabInteractable;
    HandGrabInteractable m_HandGrabInteractable;
    GrabInteractor m_GrabInteractor;
    HandGrabInteractor m_HandGrabInteractor;
    bool m_PullAudioPlayed;

    Transform m_StringMiddleParent;
    Vector3 m_RestLocalPosition;
    Vector3 m_GripLocalPosition;
    Quaternion m_GripLocalRotation;

    /// <summary>Normalized 0..1 draw amount.</summary>
    public float PullAmount { get; private set; }

    void Awake()
    {
        m_GrabInteractable = GetComponentInChildren<GrabInteractable>(true);
        m_HandGrabInteractable = GetComponentInChildren<HandGrabInteractable>(true);

        if (m_StringMiddle != null)
        {
            m_StringMiddleParent = m_StringMiddle.parent;
            m_RestLocalPosition = m_StringMiddle.localPosition;
        }

        m_GripLocalPosition = transform.localPosition;
        m_GripLocalRotation = transform.localRotation;
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
        Transform hand = null;
        if (m_GrabInteractor != null)
        {
            hand = m_GrabInteractor.transform;
        }
        else if (m_HandGrabInteractor != null)
        {
            hand = m_HandGrabInteractor.transform;
        }

        if (hand == null || m_Start == null || m_End == null)
        {
            Restore();
            return;
        }

        // Keep the arrow nocked for as long as the string is being held, even if
        // the bow was grabbed after the string, so the draw always has an arrow.
        if (m_Notch != null && m_Notch.NockedArrow == null)
        {
            m_Notch.EnsureNocked();
        }

        PullAmount = CalculatePull(hand.position);

        if (m_StringMiddle != null)
        {
            m_StringMiddle.position = Vector3.Lerp(RestWorldPosition(), m_End.position, PullAmount);
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

    void Restore()
    {
        PullAmount = 0f;
        m_PullAudioPlayed = false;

        // The Interaction SDK moves the grip with the hand while grabbed; snap it
        // back to its rest pose as soon as it is released so it does not drift.
        transform.localPosition = m_GripLocalPosition;
        transform.localRotation = m_GripLocalRotation;

        if (m_StringMiddle != null)
        {
            m_StringMiddle.position = RestWorldPosition();
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
        }

        Restore();
    }

    float CalculatePull(Vector3 pullPosition)
    {
        Vector3 pullDirection = pullPosition - RestWorldPosition();
        Vector3 targetDirection = m_End.position - m_Start.position;

        float maxLength = targetDirection.magnitude;
        if (maxLength <= Mathf.Epsilon)
        {
            return 0f;
        }

        targetDirection.Normalize();
        float pullValue = Vector3.Dot(pullDirection, targetDirection) / maxLength;
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

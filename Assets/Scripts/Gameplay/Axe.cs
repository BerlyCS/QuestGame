using System.Collections;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

/// <summary>
/// The player's throwable axe: grabbed from the rack with HandGrabInteractable
/// as usual, thrown with the SDK's own physics, and always finds its way back.
///
/// Held/released state is polled from Grabbable.SelectingPointsCount every
/// frame rather than driven by Select/Unselect events (see armas.md): with
/// hand tracking the release is irregular (a lost hand mid-grab can raise
/// Cancel instead of Unselect, or nothing at all), so polling the SDK's own
/// ground truth is the one signal that can't be missed. On top of that, a
/// hard 3-second watchdog forces the return regardless of how it left the
/// hand - weak throw, dead drop, missed event - so the player can never end
/// up permanently unarmed.
/// </summary>
[RequireComponent(typeof(Grabbable))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AudioSource))]
public class Axe : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Hand anchor to home toward when the axe was last held by the left hand.")]
    Transform m_LeftHand;

    [SerializeField]
    [Tooltip("Hand anchor to home toward when the axe was last held by the right hand.")]
    Transform m_RightHand;

    [SerializeField]
    [Tooltip("Seconds after release before the axe starts flying back on a clean throw. Needs " +
        "to be long enough that a real throw can actually reach something before being recalled.")]
    float m_ReturnDelay = 1f;

    [SerializeField]
    [Tooltip("Absolute cap on time spent out of hand before the return is forced, no matter what.")]
    float m_ForceReturnAfterSeconds = 3f;

    [SerializeField]
    [Tooltip("Initial homing speed, in metres/second.")]
    float m_StartSpeed = 3f;

    [SerializeField]
    [Tooltip("Top homing speed, in metres/second.")]
    float m_MaxSpeed = 10f;

    [SerializeField]
    [Tooltip("How fast the homing speed ramps up, in metres/second^2.")]
    float m_Acceleration = 18f;

    [SerializeField]
    [Tooltip("Distance from the hand at which the axe is considered caught up and grabbable again.")]
    float m_CatchDistance = 0.15f;

    [SerializeField]
    [Tooltip("Local-space offset from the root (which pivots at the head) to the point on the " +
        "handle that should actually meet the hand. Without this the head/blade ends up at the " +
        "hand instead of the handle, clipping through the player's hand.")]
    Vector3 m_HandleGripOffset;

    [Header("Catch feedback (no haptics - see armas.md)")]
    [SerializeField] Color m_CatchFlashColor = Color.white;
    [SerializeField] float m_CatchFlashDuration = 0.12f;

    Grabbable m_Grabbable;
    Rigidbody m_Rigidbody;
    HandGrabInteractable m_HandGrab;
    DistanceHandGrabInteractable m_DistanceHandGrab;
    AudioSource m_ClackAudio;
    AudioClip m_ClackClip;

    Renderer[] m_Renderers;
    Color[] m_BaseColors;
    float m_FlashUntil;

    Transform m_HoldingHand;
    bool m_WasHeld;
    bool m_HasEverBeenHeld;
    bool m_IsReturning;
    float m_UnheldTime;
    Coroutine m_ReturnRoutine;

    /// <summary>
    /// True while a hand is actually gripping the axe right now. Read by
    /// Slingshot: the resortera can only appear while this is false (see
    /// armas.md - "solo puede aparecer si el hacha no está en la mano").
    /// </summary>
    public bool IsHeld => m_Grabbable.SelectingPointsCount > 0;

    void Awake()
    {
        m_Grabbable = GetComponent<Grabbable>();
        m_Rigidbody = GetComponent<Rigidbody>();
        m_HandGrab = GetComponent<HandGrabInteractable>();
        m_DistanceHandGrab = GetComponent<DistanceHandGrabInteractable>();

        m_ClackAudio = GetComponent<AudioSource>();
        m_ClackAudio.playOnAwake = false;
        m_ClackAudio.spatialBlend = 1f;
        m_ClackClip = CreateClackClip();

        m_Renderers = GetComponentsInChildren<Renderer>();
        m_BaseColors = new Color[m_Renderers.Length];
        for (int i = 0; i < m_Renderers.Length; i++)
            m_BaseColors[i] = m_Renderers[i].material.color;
    }

    void Update()
    {
        bool held = m_Grabbable.SelectingPointsCount > 0;

        if (held)
        {
            if (m_Grabbable.GrabPoints.Count > 0)
                m_HoldingHand = NearestHand(m_Grabbable.GrabPoints[0].position);

            if (!m_WasHeld && m_HasEverBeenHeld)
                PlayCatchFeedback();

            m_HasEverBeenHeld = true;
            m_UnheldTime = 0f;
            m_WasHeld = true;

            if (m_IsReturning)
                StopReturning();
        }
        else if (m_HasEverBeenHeld)
        {
            // Guarded by m_HasEverBeenHeld so the axe doesn't summon itself off the
            // rack before the player has ever picked it up.
            if (m_WasHeld)
            {
                m_WasHeld = false;
                BeginReturn();
            }

            if (!m_IsReturning)
            {
                m_UnheldTime += Time.deltaTime;
                if (m_UnheldTime >= m_ForceReturnAfterSeconds)
                    BeginReturn();
            }
        }

        UpdateCatchFlash();
    }

    Transform NearestHand(Vector3 point)
    {
        if (m_LeftHand == null)
            return m_RightHand;
        if (m_RightHand == null)
            return m_LeftHand;

        return Vector3.Distance(point, m_LeftHand.position) <= Vector3.Distance(point, m_RightHand.position)
            ? m_LeftHand
            : m_RightHand;
    }

    void BeginReturn()
    {
        if (m_ReturnRoutine != null)
            StopCoroutine(m_ReturnRoutine);

        m_IsReturning = true;
        m_ReturnRoutine = StartCoroutine(ReturnToHand());
    }

    void StopReturning()
    {
        if (m_ReturnRoutine != null)
        {
            StopCoroutine(m_ReturnRoutine);
            m_ReturnRoutine = null;
        }

        m_IsReturning = false;
        SetGrabbableEnabled(true);
    }

    void SetGrabbableEnabled(bool value)
    {
        if (m_HandGrab != null)
            m_HandGrab.enabled = value;
        if (m_DistanceHandGrab != null)
            m_DistanceHandGrab.enabled = value;
    }

    IEnumerator ReturnToHand()
    {
        yield return new WaitForSeconds(m_ReturnDelay);

        Transform hand = m_HoldingHand != null ? m_HoldingHand : (m_LeftHand != null ? m_LeftHand : m_RightHand);
        if (hand == null || m_Grabbable.SelectingPointsCount > 0)
        {
            m_IsReturning = false;
            m_ReturnRoutine = null;
            yield break;
        }

        SetGrabbableEnabled(false);
        m_Rigidbody.isKinematic = true;
        m_Rigidbody.useGravity = false;

        // Rotation is deliberately left untouched here: forcing it to match the raw
        // hand-anchor basis (hand.rotation) made the axe arrive visibly flipped and
        // in an orientation that didn't correspond to how it should sit in a fist -
        // the axe just keeps whatever orientation it already had (from being
        // thrown/tumbling) while homing in on position only. Since rotation never
        // changes during this kinematic phase, m_HandleGripOffset can be re-applied
        // to a moving hand every frame without drifting.
        float speed = m_StartSpeed;
        while (m_Grabbable.SelectingPointsCount == 0 &&
               Vector3.Distance(m_Rigidbody.position, HandleTarget(hand)) > m_CatchDistance)
        {
            speed = Mathf.Min(m_MaxSpeed, speed + m_Acceleration * Time.deltaTime);
            var nextPosition = Vector3.MoveTowards(m_Rigidbody.position, HandleTarget(hand), speed * Time.deltaTime);
            m_Rigidbody.Move(nextPosition, m_Rigidbody.rotation);
            yield return null;
        }

        // Arrived: let a normal grab catch it out of the air rather than forcing one.
        SetGrabbableEnabled(true);

        while (m_Grabbable.SelectingPointsCount == 0)
        {
            m_Rigidbody.Move(HandleTarget(hand), m_Rigidbody.rotation);
            yield return null;
        }

        m_IsReturning = false;
        m_ReturnRoutine = null;
    }

    /// <summary>
    /// Where the root should sit so the handle (not the head, where the root
    /// pivots) actually meets the hand.
    /// </summary>
    Vector3 HandleTarget(Transform hand) => hand.position - m_Rigidbody.rotation * m_HandleGripOffset;

    void PlayCatchFeedback()
    {
        m_FlashUntil = Time.time + m_CatchFlashDuration;
        m_ClackAudio.PlayOneShot(m_ClackClip);
    }

    void UpdateCatchFlash()
    {
        bool flashing = Time.time < m_FlashUntil;
        for (int i = 0; i < m_Renderers.Length; i++)
        {
            if (m_Renderers[i] == null)
                continue;
            m_Renderers[i].material.color = flashing ? m_CatchFlashColor : m_BaseColors[i];
        }
    }

    /// <summary>
    /// Synthesizes a short percussive "clack" in code, matching this project's
    /// no-external-assets rule: a fast-decaying mix of noise (the sharp attack)
    /// and a low sine thump (the body), ~90 ms total.
    /// </summary>
    static AudioClip CreateClackClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.09f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        var samples = new float[sampleCount];

        var random = new System.Random(2);
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t * 60f);
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            float thump = Mathf.Sin(2f * Mathf.PI * 180f * t);
            samples[i] = Mathf.Clamp((noise * 0.6f + thump * 0.4f) * envelope, -1f, 1f);
        }

        var clip = AudioClip.Create("AxeClack", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}

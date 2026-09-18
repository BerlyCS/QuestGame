using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

/// <summary>
/// Lets the player physically grab one of the resortera's ember balls (kept
/// stocked on the weapon rack by EmberAmmoSpawner), pull it back away from
/// whichever hand didn't grab it (the anchor), and let go to fire - the real
/// pull vector aims the shot, exactly like a real two-handed slingshot,
/// instead of a look-direction guess. Grabbing a ball is the whole
/// invocation: there's no separate "summon" gesture.
///
/// Release is detected by polling Grabbable.SelectingPointsCount every frame
/// rather than trusting the SDK's Select/Unselect events, for the same
/// reason as the axe (see Axe.cs / armas.md): hand-tracking release is
/// irregular, and the SDK's own live grab count is the one signal that can't
/// be missed.
/// </summary>
[RequireComponent(typeof(Grabbable))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(EmberProjectile))]
[RequireComponent(typeof(AudioSource))]
public class SlingEmberHandle : MonoBehaviour
{
    [SerializeField] float m_MinStretch = 0.15f;
    [SerializeField] float m_MaxStretch = 0.65f;
    [SerializeField] float m_MinLaunchSpeed = 6f;
    [SerializeField] float m_MaxLaunchSpeed = 22f;
    [SerializeField] Color m_ColorLow = new Color(1f, 0.5f, 0.1f);
    [SerializeField] Color m_ColorHigh = Color.white;
    [SerializeField] Color m_BandIdleColor = new Color(0.6f, 0.3f, 0.1f);

    Grabbable m_Grabbable;
    Rigidbody m_Rigidbody;
    EmberProjectile m_Projectile;
    HandGrabInteractable m_HandGrab;
    DistanceHandGrabInteractable m_DistanceHandGrab;
    Renderer m_Renderer;
    AudioSource m_Audio;
    AudioClip m_StretchClip;
    AudioClip m_ShotClip;

    Transform m_LeftHand;
    Transform m_RightHand;
    Transform m_AnchorHand;
    LineRenderer m_Band;

    bool m_WasHeld;
    bool m_HasFired;

    void Awake()
    {
        m_Grabbable = GetComponent<Grabbable>();
        m_Rigidbody = GetComponent<Rigidbody>();
        m_Projectile = GetComponent<EmberProjectile>();
        m_HandGrab = GetComponent<HandGrabInteractable>();
        m_DistanceHandGrab = GetComponent<DistanceHandGrabInteractable>();
        // The visible mesh lives on a child ("Shape"), not the root the grab
        // components sit on.
        m_Renderer = GetComponentInChildren<Renderer>();

        m_Audio = GetComponent<AudioSource>();
        m_Audio.playOnAwake = false;
        m_Audio.spatialBlend = 1f;
        m_StretchClip = CreateStretchClip();
        m_ShotClip = CreateShotClip();

        // Waits kinematic and weightless until grabbed, like the axe resting in
        // hand: physics only takes over once it's actually fired or dropped.
        m_Rigidbody.isKinematic = true;
        m_Rigidbody.useGravity = false;
    }

    public void Initialize(Transform leftHand, Transform rightHand, LineRenderer band)
    {
        m_LeftHand = leftHand;
        m_RightHand = rightHand;
        m_Band = band;
    }

    void Update()
    {
        bool held = m_Grabbable.SelectingPointsCount > 0;

        if (held && !m_WasHeld)
            OnGrabbed();

        if (held)
            UpdateHeld();
        else if (m_WasHeld && !m_HasFired)
            Fire();

        m_WasHeld = held;
    }

    void LateUpdate()
    {
        // After the SDK's own grab-follow has moved us this frame: keep the draw
        // from stretching past the max, like a real elastic band's limit.
        if (m_Grabbable.SelectingPointsCount > 0)
            ClampToMaxStretch();
    }

    void OnGrabbed()
    {
        Vector3 grabPoint = m_Grabbable.GrabPoints.Count > 0 ? m_Grabbable.GrabPoints[0].position : transform.position;
        float leftDist = m_LeftHand != null ? Vector3.Distance(grabPoint, m_LeftHand.position) : float.MaxValue;
        float rightDist = m_RightHand != null ? Vector3.Distance(grabPoint, m_RightHand.position) : float.MaxValue;
        m_AnchorHand = leftDist <= rightDist ? m_RightHand : m_LeftHand;

        SetBandActive(true);
    }

    void ClampToMaxStretch()
    {
        if (m_AnchorHand == null)
            return;

        Vector3 offset = transform.position - m_AnchorHand.position;
        if (offset.magnitude > m_MaxStretch)
            transform.position = m_AnchorHand.position + offset.normalized * m_MaxStretch;
    }

    void UpdateHeld()
    {
        if (m_AnchorHand == null)
            return;

        float stretch = Vector3.Distance(transform.position, m_AnchorHand.position);
        float powerT = Mathf.InverseLerp(m_MinStretch, m_MaxStretch, Mathf.Clamp(stretch, m_MinStretch, m_MaxStretch));

        SetColor(Color.Lerp(m_ColorLow, m_ColorHigh, powerT));
        UpdateBandPositions();
        UpdateStretchAudio(stretch > m_MinStretch, powerT);
    }

    void UpdateBandPositions()
    {
        if (m_Band == null || m_AnchorHand == null)
            return;

        m_Band.SetPosition(0, m_AnchorHand.position);
        m_Band.SetPosition(1, transform.position);
    }

    void SetBandActive(bool active)
    {
        if (m_Band == null)
            return;

        m_Band.gameObject.SetActive(active);
        if (!active)
        {
            m_Band.startColor = m_BandIdleColor;
            m_Band.endColor = m_BandIdleColor;
        }
    }

    void SetColor(Color color)
    {
        if (m_Renderer != null)
        {
            m_Renderer.material.color = color;
            m_Renderer.material.SetColor("_EmissionColor", color);
        }

        if (m_Band != null)
        {
            m_Band.startColor = color;
            m_Band.endColor = color;
        }
    }

    void UpdateStretchAudio(bool active, float powerT)
    {
        if (active)
        {
            if (!m_Audio.isPlaying || m_Audio.clip != m_StretchClip)
            {
                m_Audio.clip = m_StretchClip;
                m_Audio.loop = true;
                m_Audio.Play();
            }
            m_Audio.pitch = Mathf.Lerp(0.8f, 1.8f, powerT);
        }
        else if (m_Audio.isPlaying && m_Audio.clip == m_StretchClip)
        {
            m_Audio.Stop();
        }
    }

    void StopStretchAudio()
    {
        if (m_Audio.isPlaying && m_Audio.clip == m_StretchClip)
            m_Audio.Stop();
    }

    void Fire()
    {
        m_HasFired = true;
        SetBandActive(false);
        StopStretchAudio();
        SetGrabbableEnabled(false);

        Vector3 displacement = m_AnchorHand != null
            ? transform.position - m_AnchorHand.position
            : transform.forward * m_MinStretch;
        if (displacement.sqrMagnitude < 0.0001f)
            displacement = transform.forward * m_MinStretch;

        float stretch = displacement.magnitude;
        float powerT = Mathf.InverseLerp(m_MinStretch, m_MaxStretch, Mathf.Clamp(stretch, m_MinStretch, m_MaxStretch));
        float speed = Mathf.Lerp(m_MinLaunchSpeed, m_MaxLaunchSpeed, powerT);

        // Flies back through the anchor and beyond, opposite the pull - real
        // slingshot physics, aimed by wherever the player actually drew it to.
        Vector3 direction = -displacement.normalized;

        m_Rigidbody.isKinematic = false;
        m_Rigidbody.useGravity = true;
        m_Projectile.Launch(direction * speed);

        m_Audio.pitch = 1f;
        m_Audio.PlayOneShot(m_ShotClip);
    }

    void SetGrabbableEnabled(bool value)
    {
        if (m_HandGrab != null)
            m_HandGrab.enabled = value;
        if (m_DistanceHandGrab != null)
            m_DistanceHandGrab.enabled = value;
    }

    /// <summary>Short looping tone whose pitch is modulated at runtime by stretch power.</summary>
    static AudioClip CreateStretchClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.5f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        var samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float tone = Mathf.Sin(2f * Mathf.PI * 260f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 390f * t) * 0.2f;
            samples[i] = Mathf.Clamp(tone * 0.5f, -1f, 1f);
        }

        var clip = AudioClip.Create("SlingshotStretch", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>Short descending-pitch "twang" - the band releasing.</summary>
    static AudioClip CreateShotClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.2f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        var samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t * 12f);
            float frequency = Mathf.Lerp(900f, 220f, t / duration);
            float tone = Mathf.Sin(2f * Mathf.PI * frequency * t);
            samples[i] = Mathf.Clamp(tone * envelope, -1f, 1f);
        }

        var clip = AudioClip.Create("SlingshotShot", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}

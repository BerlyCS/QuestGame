using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

/// <summary>
/// An ember ball (the slingshot's ammo, kept stocked on the weapon rack by
/// EmberAmmoSpawner). Grab it with the hand that isn't holding the Slingshot
/// frame and pull it back away from the fork: the ember-to-fork vector is
/// the aim, the stretch is the power, and opening the hand fires. Without
/// the frame in the other hand (or with a pull shorter than the minimum) the
/// ember just drops, so an accidental release never wastes a real shot.
///
/// Release is detected by polling Grabbable.SelectingPointsCount every frame
/// rather than trusting the SDK's Select/Unselect events (hand-tracking
/// release is irregular, see armas.md). Launch velocity comes from the pull
/// vector, never from the SDK's unreliable release-velocity estimate.
/// </summary>
[RequireComponent(typeof(Grabbable))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(EmberProjectile))]
[RequireComponent(typeof(AudioSource))]
public class SlingEmberHandle : MonoBehaviour
{
    [SerializeField] float m_MinStretch = 0.15f;
    [SerializeField] float m_MaxStretch = 0.45f;
    [SerializeField] float m_MinLaunchSpeed = 6f;
    [SerializeField] float m_MaxLaunchSpeed = 22f;
    [SerializeField] Color m_ColorLow = new Color(1f, 0.5f, 0.1f);
    [SerializeField] Color m_ColorHigh = Color.white;

    Grabbable m_Grabbable;
    Rigidbody m_Rigidbody;
    EmberProjectile m_Projectile;
    HandGrabInteractable m_HandGrab;
    DistanceHandGrabInteractable m_DistanceHandGrab;
    Renderer m_Renderer;
    AudioSource m_Audio;
    AudioClip m_StretchClip;
    AudioClip m_ShotClip;
    Slingshot m_Slingshot;

    bool m_WasHeld;
    bool m_HasFired;

    void Awake()
    {
        m_Grabbable = GetComponent<Grabbable>();
        m_Rigidbody = GetComponent<Rigidbody>();
        m_Projectile = GetComponent<EmberProjectile>();
        m_HandGrab = GetComponent<HandGrabInteractable>();
        m_DistanceHandGrab = GetComponent<DistanceHandGrabInteractable>();
        // The visible mesh is the "Shape" child (GrabHighlight adds its own shell below it).
        m_Renderer = transform.Find("Shape").GetComponent<Renderer>();

        m_Audio = GetComponent<AudioSource>();
        m_Audio.playOnAwake = false;
        m_Audio.spatialBlend = 1f;
        m_StretchClip = CreateStretchClip();
        m_ShotClip = CreateShotClip();

        // Waits kinematic and weightless until grabbed: physics only takes over
        // once it's actually fired or dropped.
        m_Rigidbody.isKinematic = true;
        m_Rigidbody.useGravity = false;
    }

    public void Initialize(Slingshot slingshot)
    {
        m_Slingshot = slingshot;
    }

    bool CanDraw => m_Slingshot != null && m_Slingshot.IsHeld;

    void Update()
    {
        bool held = m_Grabbable.SelectingPointsCount > 0;

        if (held)
            UpdateHeld();
        else if (m_WasHeld && !m_HasFired)
            Release();

        m_WasHeld = held;
    }

    void LateUpdate()
    {
        // After the SDK's own grab-follow has moved us this frame: keep the draw
        // from stretching past the max, like a real elastic band's limit.
        if (m_Grabbable.SelectingPointsCount == 0 || !CanDraw)
            return;

        Vector3 offset = transform.position - m_Slingshot.ForkPosition;
        if (offset.magnitude > m_MaxStretch)
            transform.position = m_Slingshot.ForkPosition + offset.normalized * m_MaxStretch;
    }

    void UpdateHeld()
    {
        if (!CanDraw)
        {
            if (m_Slingshot != null)
                m_Slingshot.ClearPull();
            StopStretchAudio();
            return;
        }

        Vector3 displacement = transform.position - m_Slingshot.ForkPosition;
        float stretch = displacement.magnitude;
        float powerT = PowerFor(stretch);
        Color color = Color.Lerp(m_ColorLow, m_ColorHigh, powerT);

        SetColor(color);
        m_Slingshot.SetPull(transform.position, color);
        UpdateStretchAudio(stretch > m_MinStretch, powerT);

        if (stretch > m_MinStretch)
            m_Slingshot.ShowArc(-displacement.normalized * SpeedFor(powerT), color);
        else
            m_Slingshot.ClearPull(); // too short to fire: no arc, no band pull
    }

    float PowerFor(float stretch) =>
        Mathf.InverseLerp(m_MinStretch, m_MaxStretch, Mathf.Clamp(stretch, m_MinStretch, m_MaxStretch));

    float SpeedFor(float powerT) => Mathf.Lerp(m_MinLaunchSpeed, m_MaxLaunchSpeed, powerT);

    void SetColor(Color color)
    {
        if (m_Renderer == null)
            return;

        m_Renderer.material.color = color;
        m_Renderer.material.SetColor("_EmissionColor", color);
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
        else
        {
            StopStretchAudio();
        }
    }

    void StopStretchAudio()
    {
        if (m_Audio.isPlaying && m_Audio.clip == m_StretchClip)
            m_Audio.Stop();
    }

    void Release()
    {
        m_HasFired = true;
        StopStretchAudio();
        SetGrabbableEnabled(false);

        Vector3 displacement = CanDraw ? transform.position - m_Slingshot.ForkPosition : Vector3.zero;
        if (m_Slingshot != null)
            m_Slingshot.ClearPull();

        if (displacement.magnitude < m_MinStretch)
        {
            // No frame in the other hand, or barely pulled: just let it fall.
            m_Rigidbody.isKinematic = false;
            m_Rigidbody.useGravity = true;
            return;
        }

        float speed = SpeedFor(PowerFor(displacement.magnitude));

        // Flies back through the fork and beyond, opposite the pull - real slingshot
        // physics, aimed by wherever the player actually drew it to.
        Vector3 direction = -displacement.normalized;

        // Muzzle flash at the fork: without haptics the shot has to be seen (CLAUDE.md).
        FadingGlow.Spawn(m_Slingshot.ForkPosition, 0.22f, new Color(2f, 1.4f, 0.5f), 0.12f);

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

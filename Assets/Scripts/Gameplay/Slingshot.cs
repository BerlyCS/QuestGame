using UnityEngine;

/// <summary>
/// The resortera. Never equipped with a button: it can only appear while the
/// axe is out of the player's hand (thrown or homing back - see Axe.IsHeld),
/// and only while both hands stay within reach of each other. Ammunition is
/// brasas from the campfire itself - dip a hand near the fire to load one,
/// which costs fuel immediately.
///
/// Deliberately does not use the Interaction SDK's grab/release events for
/// anything here (see armas.md): hand tracking's release is unreliable, but
/// bringing two hands together in front of the chest is the gesture the
/// Quest's cameras read best. The shot itself is detected by a sudden
/// collapse in the hand-to-hand distance, not an SDK event.
/// </summary>
[DisallowMultipleComponent]
public class Slingshot : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform m_LeftHand;
    [SerializeField] Transform m_RightHand;
    [SerializeField] Transform m_Target;
    [SerializeField] Axe m_Axe;
    [SerializeField] CampfireFuel m_Campfire;
    [SerializeField] GameObject m_EmberProjectilePrefab;

    [Header("Invocation")]
    [SerializeField] float m_InvokeDistance = 0.3f;
    [SerializeField] float m_DismissDistance = 0.9f;
    [SerializeField] float m_InvokeFlashDuration = 0.2f;

    [Header("Charging (see fogata.md fuel trigger)")]
    [SerializeField] float m_ChargeTriggerRadius = 0.9f;
    [SerializeField] float m_EmberCost = 2.5f;

    [Header("Aiming")]
    [SerializeField] float m_MinStretch = 0.15f;
    [SerializeField] float m_MaxStretch = 0.65f;
    [SerializeField] float m_MinLaunchSpeed = 6f;
    [SerializeField] float m_MaxLaunchSpeed = 22f;

    [Header("Shot detection (not an SDK release event - see armas.md)")]
    [Tooltip("Hands closing faster than this (m/s) while stretched counts as a release.")]
    [SerializeField] float m_CollapseSpeedThreshold = 2f;
    [SerializeField] float m_FireCooldown = 0.3f;

    [Header("Visuals")]
    [SerializeField] LineRenderer m_Band;
    [SerializeField] Transform m_EmberVisual;
    [SerializeField] Color m_EmberColorLow = new Color(1f, 0.5f, 0.1f);
    [SerializeField] Color m_EmberColorHigh = Color.white;
    [SerializeField] Color m_BandIdleColor = new Color(0.6f, 0.3f, 0.1f);
    [SerializeField] Color m_BandFlashColor = Color.white;

    [Header("Audio")]
    [SerializeField] AudioSource m_StretchAudio;
    [SerializeField] AudioSource m_OneShotAudio;

    bool m_IsSummoned;
    bool m_HasEmber;
    float m_PreviousHandDistance;
    float m_NextFireTime;
    float m_InvokeFlashUntil;

    AudioClip m_ChargeClip;
    AudioClip m_ShotClip;
    AudioClip m_StretchClip;

    void Awake()
    {
        m_ChargeClip = CreateChargeClip();
        m_ShotClip = CreateShotClip();
        m_StretchClip = CreateStretchClip();

        if (m_StretchAudio != null)
        {
            m_StretchAudio.clip = m_StretchClip;
            m_StretchAudio.loop = true;
            m_StretchAudio.playOnAwake = false;
            m_StretchAudio.spatialBlend = 1f;
        }

        if (m_OneShotAudio != null)
            m_OneShotAudio.spatialBlend = 1f;

        SetVisualsActive(false);
    }

    void Update()
    {
        UpdateEmberCharging();

        float handDistance = Vector3.Distance(m_LeftHand.position, m_RightHand.position);
        bool axeFree = m_Axe == null || !m_Axe.IsHeld;

        if (!m_IsSummoned)
        {
            if (axeFree && handDistance < m_InvokeDistance)
                Summon();

            m_PreviousHandDistance = handDistance;
            return;
        }

        if (!axeFree || handDistance > m_DismissDistance)
        {
            Dismiss();
            m_PreviousHandDistance = handDistance;
            return;
        }

        UpdateAiming(handDistance);
        m_PreviousHandDistance = handDistance;
    }

    void Summon()
    {
        m_IsSummoned = true;
        m_InvokeFlashUntil = Time.time + m_InvokeFlashDuration;
        SetVisualsActive(true);
    }

    void Dismiss()
    {
        m_IsSummoned = false;
        StopStretchAudio();
        SetVisualsActive(false);
    }

    void SetVisualsActive(bool active)
    {
        if (m_Band != null)
            m_Band.gameObject.SetActive(active);
        if (m_EmberVisual != null)
            m_EmberVisual.gameObject.SetActive(active && m_HasEmber);
    }

    void UpdateAiming(float handDistance)
    {
        Vector3 midpoint = (m_LeftHand.position + m_RightHand.position) * 0.5f;
        UpdateBand();

        float stretchT = Mathf.InverseLerp(m_MinStretch, m_MaxStretch, Mathf.Clamp(handDistance, m_MinStretch, m_MaxStretch));
        bool stretching = handDistance > m_MinStretch;

        if (m_EmberVisual != null)
        {
            m_EmberVisual.gameObject.SetActive(m_HasEmber);
            m_EmberVisual.position = midpoint;
            if (m_HasEmber)
                SetEmberColor(Color.Lerp(m_EmberColorLow, m_EmberColorHigh, stretchT));
        }

        UpdateStretchAudio(stretching && m_HasEmber, stretchT);

        float closingSpeed = (m_PreviousHandDistance - handDistance) / Mathf.Max(Time.deltaTime, 0.0001f);
        bool readyToFire = m_HasEmber && Time.time >= m_NextFireTime;

        if (readyToFire && closingSpeed > m_CollapseSpeedThreshold && m_PreviousHandDistance >= m_MinStretch)
        {
            float powerT = Mathf.InverseLerp(m_MinStretch, m_MaxStretch, Mathf.Clamp(m_PreviousHandDistance, m_MinStretch, m_MaxStretch));
            Fire(midpoint, powerT);
        }
    }

    void UpdateBand()
    {
        if (m_Band == null)
            return;

        m_Band.SetPosition(0, m_LeftHand.position);
        m_Band.SetPosition(1, m_RightHand.position);

        float flashT = Time.time < m_InvokeFlashUntil
            ? Mathf.Clamp01((m_InvokeFlashUntil - Time.time) / m_InvokeFlashDuration)
            : 0f;
        Color color = Color.Lerp(m_BandIdleColor, m_BandFlashColor, flashT);
        m_Band.startColor = color;
        m_Band.endColor = color;
    }

    void SetEmberColor(Color color)
    {
        var renderer = m_EmberVisual.GetComponent<Renderer>();
        if (renderer == null)
            return;

        renderer.material.color = color;
        renderer.material.SetColor("_EmissionColor", color);
    }

    void UpdateEmberCharging()
    {
        if (m_HasEmber || m_Campfire == null)
            return;

        if (Vector3.Distance(m_LeftHand.position, m_Campfire.transform.position) <= m_ChargeTriggerRadius)
            ChargeEmber(m_LeftHand.position);
        else if (Vector3.Distance(m_RightHand.position, m_Campfire.transform.position) <= m_ChargeTriggerRadius)
            ChargeEmber(m_RightHand.position);
    }

    void ChargeEmber(Vector3 handPosition)
    {
        m_Campfire.AddFuel(-m_EmberCost);
        m_HasEmber = true;

        if (m_IsSummoned && m_EmberVisual != null)
            m_EmberVisual.gameObject.SetActive(true);

        // A brief orange glow at the hand instead of tinting the SDK's hand-tracking
        // visual directly (see armas.md - "la mano se tiñe de naranja").
        FadingGlow.Spawn(handPosition, 0.08f, m_EmberColorLow, 0.4f);
        if (m_OneShotAudio != null)
            m_OneShotAudio.PlayOneShot(m_ChargeClip);
    }

    void Fire(Vector3 origin, float powerT)
    {
        m_HasEmber = false;
        m_NextFireTime = Time.time + m_FireCooldown;
        StopStretchAudio();

        if (m_EmberVisual != null)
            m_EmberVisual.gameObject.SetActive(false);

        Vector3 direction = m_Target != null ? m_Target.forward : transform.forward;

        if (m_EmberProjectilePrefab != null)
        {
            float speed = Mathf.Lerp(m_MinLaunchSpeed, m_MaxLaunchSpeed, powerT);
            var projectileGo = Instantiate(m_EmberProjectilePrefab, origin, Quaternion.LookRotation(direction));
            var projectile = projectileGo.GetComponent<EmberProjectile>();
            if (projectile != null)
                projectile.Launch(direction * speed);
        }

        FadingGlow.Spawn(origin, 0.12f, m_EmberColorHigh, 0.15f);
        if (m_OneShotAudio != null)
            m_OneShotAudio.PlayOneShot(m_ShotClip);
    }

    void UpdateStretchAudio(bool active, float powerT)
    {
        if (m_StretchAudio == null)
            return;

        if (active)
        {
            if (!m_StretchAudio.isPlaying)
                m_StretchAudio.Play();
            m_StretchAudio.pitch = Mathf.Lerp(0.8f, 1.8f, powerT);
        }
        else if (m_StretchAudio.isPlaying)
        {
            m_StretchAudio.Stop();
        }
    }

    void StopStretchAudio()
    {
        if (m_StretchAudio != null && m_StretchAudio.isPlaying)
            m_StretchAudio.Stop();
    }

    /// <summary>Short crackly noise burst - loading a hot coal from the fire.</summary>
    static AudioClip CreateChargeClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.3f;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        var samples = new float[sampleCount];
        var random = new System.Random(3);

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t * 8f);
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            samples[i] = Mathf.Clamp(noise * envelope * 0.7f, -1f, 1f);
        }

        var clip = AudioClip.Create("EmberCharge", sampleCount, 1, sampleRate, false);
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
}

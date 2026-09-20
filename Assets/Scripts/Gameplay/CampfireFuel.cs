using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Drives the campfire: fuel drains continuously in real seconds and adding
/// logs refuels it. Nothing stops it from reaching zero — an unfed fire dies.
/// Fuel level controls the fire light, flame particles and crackle audio,
/// which <see cref="NightEnvironmentController"/> reads to set overall
/// darkness and <see cref="CampRevealController"/> uses (via
/// <see cref="OnFuelChanged"/>) to reveal the camp the first time the player
/// feeds it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class CampfireFuel : MonoBehaviour
{
    [Header("Fuel (seconds)")]
    [SerializeField]
    [Tooltip("Maximum fuel the fire can hold, in seconds.")]
    float m_MaxFuel = 90f;

    [SerializeField]
    [Tooltip("Fuel the fire starts with, in seconds.")]
    float m_StartingFuel = 55f;

    [SerializeField]
    [Tooltip("Fuel consumed per second of real time. The fire can burn out completely.")]
    float m_BurnRatePerSecond = 0.55f;

    [Header("Fire Light")]
    [SerializeField] Light m_FireLight;
    [SerializeField] float m_MinLightIntensity = 0.3f;
    [SerializeField] float m_MaxLightIntensity = 6f;
    [SerializeField] float m_MinLightRange = 5f;
    [SerializeField] float m_MaxLightRange = 16f;
    [SerializeField] Color m_EmberColor = new Color(0.9f, 0.25f, 0.05f);
    [SerializeField] Color m_FlameColor = new Color(1f, 0.7f, 0.25f);
    [Tooltip("Exponent applied to fuel01 before driving the light (pow(t, 0.7)): the " +
        "last 20% of fuel collapses fast instead of fading in a straight line.")]
    [SerializeField] float m_LightCurveExponent = 0.7f;

    [Header("Flame Particles")]
    [SerializeField] ParticleSystem m_FlameParticles;
    [SerializeField] float m_MaxEmissionRate = 40f;
    [SerializeField] float m_MaxFlameSize = 1f;

    [Header("Fire Audio")]
    [SerializeField] float m_MinFireVolume = 0.15f;
    [SerializeField] float m_MaxFireVolume = 1f;
    [SerializeField] float m_MinFirePitch = 0.6f;
    [SerializeField] float m_MaxFirePitch = 1.25f;

    [Header("Events")]
    [SerializeField] UnityEvent m_OnFuelChanged = new UnityEvent();
    [SerializeField] UnityEvent m_OnIgnited = new UnityEvent();
    [SerializeField] UnityEvent m_OnExtinguished = new UnityEvent();

    float m_CurrentFuel;
    float m_Flare;
    bool m_WasBurning;
    AudioSource m_FireAudio;

    public float MaxFuel => m_MaxFuel;
    public float CurrentFuel => m_CurrentFuel;
    public float FuelNormalized => m_MaxFuel <= 0f ? 0f : Mathf.Clamp01(m_CurrentFuel / m_MaxFuel);

    /// <summary>Fuel as 0-1, the name every system is meant to read (see CLAUDE.md).</summary>
    public float Fuel01 => FuelNormalized;

    public bool IsBurning => m_CurrentFuel > 0f;

    public UnityEvent OnFuelChanged => m_OnFuelChanged;
    public UnityEvent OnIgnited => m_OnIgnited;
    public UnityEvent OnExtinguished => m_OnExtinguished;

    void Awake()
    {
        m_FireAudio = GetComponent<AudioSource>();
        if (m_FireAudio == null)
            m_FireAudio = gameObject.AddComponent<AudioSource>();
        m_FireAudio.clip = CreateCrackleClip();
        m_FireAudio.loop = true;
        m_FireAudio.playOnAwake = false;
        m_FireAudio.spatialBlend = 1f;

        m_CurrentFuel = Mathf.Clamp(m_StartingFuel, 0f, m_MaxFuel);
        m_WasBurning = IsBurning;
        ApplyVisuals();

        m_FireAudio.Play();
    }

    void Update()
    {
        if (m_Flare > 0f)
        {
            m_Flare = Mathf.Max(0f, m_Flare - Time.deltaTime * 1.6f);
            ApplyVisuals();
        }

        if (m_CurrentFuel <= 0f)
            return;

        AddFuel(-m_BurnRatePerSecond * Time.deltaTime, false);
    }

    public void AddFuel(float amount)
    {
        AddFuel(amount, true);
    }

    public void AddFuel(float amount, bool invokeEvents)
    {
        float before = m_CurrentFuel;
        m_CurrentFuel = Mathf.Clamp(m_CurrentFuel + amount, 0f, m_MaxFuel);

        if (Mathf.Approximately(before, m_CurrentFuel))
            return;

        ApplyVisuals();

        if (invokeEvents)
            m_OnFuelChanged.Invoke();

        bool burning = IsBurning;
        if (burning && !m_WasBurning)
            m_OnIgnited.Invoke();
        else if (!burning && m_WasBurning)
            m_OnExtinguished.Invoke();

        m_WasBurning = burning;
    }

    /// <summary>
    /// Brief flare-up when a log catches: a burst of flame particles and a light
    /// surge that decays in well under a second. Purely visual, no fuel change.
    /// </summary>
    public void Flare()
    {
        if (!IsBurning)
            return;

        m_Flare = 1f;
        if (m_FlameParticles != null)
            m_FlameParticles.Emit(30);
        ApplyVisuals();
    }

    public void SetFuel(float amount)
    {
        m_CurrentFuel = Mathf.Clamp(amount, 0f, m_MaxFuel);
        m_WasBurning = IsBurning;
        ApplyVisuals();
    }

    void ApplyVisuals()
    {
        float n = Fuel01;
        float curved = Mathf.Pow(n, m_LightCurveExponent);

        if (m_FireLight != null)
        {
            m_FireLight.enabled = n > 0f;
            m_FireLight.intensity = Mathf.Lerp(m_MinLightIntensity, m_MaxLightIntensity, curved) * (1f + m_Flare * 0.8f);
            m_FireLight.range = Mathf.Lerp(m_MinLightRange, m_MaxLightRange, curved);
            m_FireLight.color = Color.Lerp(m_EmberColor, m_FlameColor, curved);
        }

        if (m_FlameParticles != null)
        {
            var emission = m_FlameParticles.emission;
            emission.rateOverTime = m_MaxEmissionRate * n;

            var main = m_FlameParticles.main;
            main.startSizeMultiplier = Mathf.Lerp(0.25f, m_MaxFlameSize, n);

            if (n > 0.001f)
            {
                if (!m_FlameParticles.isPlaying)
                    m_FlameParticles.Play();
            }
            else if (!m_FlameParticles.isStopped)
            {
                m_FlameParticles.Stop();
            }
        }

        if (m_FireAudio != null)
        {
            m_FireAudio.volume = Mathf.Lerp(m_MinFireVolume, m_MaxFireVolume, n);
            m_FireAudio.pitch = Mathf.Lerp(m_MinFirePitch, m_MaxFirePitch, n);
        }
    }

    /// <summary>
    /// Synthesizes a short, seamlessly-looping crackle/hiss noise clip in code
    /// so the campfire's sound needs no imported audio asset: filtered noise
    /// for the hiss bed, plus sparse sharp pops for the crackle snaps.
    /// </summary>
    static AudioClip CreateCrackleClip()
    {
        const int sampleRate = 44100;
        const float durationSeconds = 4f;
        int sampleCount = Mathf.RoundToInt(sampleRate * durationSeconds);
        var samples = new float[sampleCount];

        var random = new System.Random(1);
        float filtered = 0f;
        for (int i = 0; i < sampleCount; i++)
        {
            float white = (float)(random.NextDouble() * 2.0 - 1.0);
            filtered = Mathf.Lerp(filtered, white, 0.15f);
            float value = filtered * 0.35f;

            if (random.NextDouble() < 0.0006)
                value += (float)(random.NextDouble() * 2.0 - 1.0) * 0.6f;

            samples[i] = Mathf.Clamp(value, -1f, 1f);
        }

        int fadeSamples = Mathf.Min(2000, sampleCount / 4);
        for (int i = 0; i < fadeSamples; i++)
        {
            float t = i / (float)fadeSamples;
            samples[i] = Mathf.Lerp(samples[sampleCount - fadeSamples + i], samples[i], t);
        }

        var clip = AudioClip.Create("CampfireCrackle", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}

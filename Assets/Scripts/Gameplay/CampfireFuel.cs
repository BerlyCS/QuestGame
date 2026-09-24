using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Drives the campfire: fuel drains continuously in real seconds and adding
/// logs refuels it. Nothing stops it from reaching zero — an unfed fire dies.
/// Fuel level controls the fire light, flame particles and crackle audio,
/// which <see cref="NightEnvironmentController"/> reads to set overall
/// darkness and <see cref="CampRevealController"/> uses (via
/// <see cref="OnFuelChanged"/>) to reveal the camp the first time the player
/// feeds it. Fuel only sets the light's average brightness; a Perlin-noise
/// flicker (see <see cref="ApplyLightFlicker"/>) makes it dance like real fire.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class CampfireFuel : MonoBehaviour
{
    [Header("Fuel (seconds)")]
    [SerializeField]
    [Tooltip("Maximum fuel the fire can hold, in seconds.")]
    float m_MaxFuel = 120f;

    [SerializeField]
    [Tooltip("Fuel the fire starts with, in seconds.")]
    float m_StartingFuel = 0f;

    [SerializeField]
    [Tooltip("Fuel consumed per second of real time. The fire can burn out completely.")]
    float m_BurnRatePerSecond = 0.3f;

    [SerializeField]
    [Tooltip("Multiplier on the passive burn, driven by GameManager so the fire gets " +
        "thirstier as the night wears on. 1 is normal.")]
    float m_BurnRateMultiplier = 1f;

    [SerializeField]
    [Tooltip("While off the fire holds its fuel level. Used through the prologue: " +
        "the night is not running yet, so the fire must not burn down.")]
    bool m_BurnsOverTime = true;

    [SerializeField]
    [Tooltip("Seconds the flame/light/audio take to catch up to a fuel change. " +
        "Keeps the first log from snapping the fire to full: it swells into life.")]
    float m_VisualRampSeconds = 1.6f;

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

    [Header("Fire Light Flicker")]
    [Tooltip("How far the light wanders from its fuel-driven base intensity, as a " +
        "fraction (0 = a steady lamp, 0.5 = the intensity swings +/-50%).")]
    [Range(0f, 1f)]
    [SerializeField] float m_FlickerAmount = 0.4f;

    [Tooltip("How fast the flicker drifts, in noise cycles per second. Low values " +
        "breathe like gas, high values snap like burning wood.")]
    [SerializeField] float m_FlickerSpeed = 7f;

    [Tooltip("Flicker range multiplier applied on top of the intensity flicker: the " +
        "light's reach should barely move, or the camp feels like it is pulsing.")]
    [Range(0f, 1f)]
    [SerializeField] float m_FlickerRangeInfluence = 0.25f;

    [Tooltip("Extra flicker at low fuel: a guttering fire sways more than a roaring one.")]
    [Range(0f, 1f)]
    [SerializeField] float m_LowFuelFlickerBoost = 0.6f;

    [Header("Flame Particles")]
    [SerializeField] ParticleSystem m_FlameParticles;
    [SerializeField] float m_MaxEmissionRate = 40f;
    [SerializeField] float m_MaxFlameSize = 1f;

    [Header("Fire Audio")]
    [SerializeField] float m_MinFireVolume = 0.15f;
    [SerializeField] float m_MaxFireVolume = 1f;
    [SerializeField] float m_MinFirePitch = 0.6f;
    [SerializeField] float m_MaxFirePitch = 1.25f;

    [Header("Damage Feedback")]
    [Tooltip("Small debris/burn particles thrown off when an enemy lands a hit on the fire.")]
    [SerializeField] int m_ImpactParticleCount = 16;
    [SerializeField] float m_ImpactParticleSize = 0.12f;
    [SerializeField] Color m_ImpactLavaColor = new Color(1f, 0.3f, 0.05f);
    [SerializeField] Color m_ImpactGrayColor = new Color(0.4f, 0.38f, 0.35f);
    [SerializeField] Color m_ImpactDarkColor = new Color(0.13f, 0.12f, 0.11f);
    [SerializeField, Range(0f, 1f)] float m_ImpactVolume = 0.65f;

    [Header("Low Fuel Alerts")]
    [Tooltip("Fuel levels (0-1). Each one, as the fire drops through it, throws a small " +
        "ember puff and a warning sound so the player hears the fire dying.")]
    [SerializeField] float[] m_FuelAlertThresholds = { 0.66f, 0.4f, 0.2f, 0.08f };
    [SerializeField] int m_AlertParticleCount = 8;
    [SerializeField, Range(0f, 1f)] float m_AlertVolume = 0.5f;

    [Header("Threat lean (see JEFE_FINAL.md 6: the flame as a permanent compass)")]
    [Tooltip("How far the flame tilts away from a nearby threat (the Coronado), capped so it " +
        "never fully turns its back on the fire's own base shape.")]
    [SerializeField] float m_MaxLeanAngle = 40f;

    [Header("Events")]
    [SerializeField] UnityEvent m_OnFuelChanged = new UnityEvent();
    [SerializeField] UnityEvent m_OnIgnited = new UnityEvent();
    [SerializeField] UnityEvent m_OnExtinguished = new UnityEvent();

    float m_CurrentFuel;
    float m_DisplayFuel;
    float m_Flare;
    bool m_WasBurning;
    bool m_FlamePlaying;
    AudioSource m_FireAudio;
    ParticleSystem m_ImpactParticles;
    int m_NextAlertIndex;
    static Material s_ParticleMaterial;
    static bool s_ParticleMaterialSearched;

    // Fuel-driven light values, cached by ApplyVisuals so the per-frame flicker
    // can modulate them without re-deriving the fuel curve every frame.
    float m_BaseLightIntensity;
    float m_BaseLightRange;
    float m_AudioDuckMultiplier = 1f;
    float m_ProximityDrainMultiplier = 1f;
    Quaternion m_FlameNeutralRotation;
    bool m_FlameNeutralCached;

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

        CacheFlameNeutralRotation();

        m_CurrentFuel = Mathf.Clamp(m_StartingFuel, 0f, m_MaxFuel);
        m_DisplayFuel = m_CurrentFuel;
        m_WasBurning = IsBurning;
        ApplyVisuals();

        EnsureImpactParticles();
        ResetFuelAlerts();

        m_FireAudio.Play();
    }

    void Update()
    {
        bool visualsDirty = false;

        // The flame, light and crackle trail the real fuel so a log catching
        // reads as the fire swelling back to life rather than snapping.
        if (!Mathf.Approximately(m_DisplayFuel, m_CurrentFuel))
        {
            float rate = m_MaxFuel / Mathf.Max(0.05f, m_VisualRampSeconds);
            m_DisplayFuel = Mathf.MoveTowards(m_DisplayFuel, m_CurrentFuel, rate * Time.deltaTime);
            visualsDirty = true;
        }

        if (m_Flare > 0f)
        {
            m_Flare = Mathf.Max(0f, m_Flare - Time.deltaTime * 1.6f);
            visualsDirty = true;
        }

        if (visualsDirty)
            ApplyVisuals();

        // The flicker is time-based, not fuel-based, so it runs every frame while
        // the fire has a light to waggle.
        if (m_FireLight != null && m_FireLight.enabled)
            ApplyLightFlicker();

        if (m_CurrentFuel <= 0f || !m_BurnsOverTime)
            return;

        AddFuel(-m_BurnRatePerSecond * m_ProximityDrainMultiplier * m_BurnRateMultiplier * Time.deltaTime, false);
    }

    /// <summary>
    /// Freezes or resumes the fuel drain. The prologue gate in
    /// <see cref="GameManager"/> holds the fire until the night starts; the
    /// visuals and the accepted logs are unaffected.
    /// </summary>
    public void SetBurnsOverTime(bool burnsOverTime)
    {
        m_BurnsOverTime = burnsOverTime;
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
        UpdateFuelAlerts();

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
        {
            // A flare must land on a running flame; otherwise the burst can be
            // emitted into a stopped system and cleared before it is ever seen.
            if (!m_FlamePlaying)
            {
                m_FlameParticles.Play();
                m_FlamePlaying = true;
            }

            m_FlameParticles.Emit(30);
        }
        ApplyVisuals();
    }

    public void SetFuel(float amount)
    {
        m_CurrentFuel = Mathf.Clamp(amount, 0f, m_MaxFuel);
        m_DisplayFuel = m_CurrentFuel;
        m_WasBurning = IsBurning;
        ResetFuelAlerts();
        ApplyVisuals();
    }

    /// <summary>
    /// Feedback for an enemy landing a blow on the fire: a burst of small
    /// debris particles (lava red, gray and dark gray) and a muffled impact
    /// sound. Called by the melee Caminante and by <see cref="ThrownBone"/> on
    /// impact; purely presentational, the fuel loss is applied by the caller.
    /// </summary>
    public void PlayImpact()
    {
        if (!IsBurning)
            return;

        EmitBurst(m_ImpactParticleCount, 1f);
        ProceduralSfx.PlayAt(ProceduralSfx.FireImpact, transform.position + Vector3.up * 0.4f, m_ImpactVolume);
    }

    /// <summary>
    /// Fires a warning cue every time the fire drops through one of the
    /// configured thresholds (see <see cref="m_FuelAlertThresholds"/>), and
    /// walks the cursor back up when the fire is refuelled.
    /// </summary>
    void UpdateFuelAlerts()
    {
        if (m_FuelAlertThresholds == null || m_FuelAlertThresholds.Length == 0)
            return;

        float fuel = Fuel01;

        // Refuelling back above the last alert arms every threshold again.
        if (m_NextAlertIndex > 0 && fuel > m_FuelAlertThresholds[m_NextAlertIndex - 1])
        {
            ResetFuelAlerts();
            return;
        }

        if (m_NextAlertIndex >= m_FuelAlertThresholds.Length || fuel > m_FuelAlertThresholds[m_NextAlertIndex])
            return;

        // A big drop can cross several thresholds at once; only one cue plays.
        while (m_NextAlertIndex < m_FuelAlertThresholds.Length && fuel <= m_FuelAlertThresholds[m_NextAlertIndex])
            m_NextAlertIndex++;

        EmitBurst(m_AlertParticleCount, 0.6f);
        ProceduralSfx.PlayAt(ProceduralSfx.FireLowFuel, transform.position + Vector3.up * 0.4f, m_AlertVolume);
    }

    /// <summary>Rearms the low-fuel cues for the current fuel level.</summary>
    void ResetFuelAlerts()
    {
        m_NextAlertIndex = 0;
        if (m_FuelAlertThresholds == null)
            return;

        float fuel = Fuel01;
        while (m_NextAlertIndex < m_FuelAlertThresholds.Length && m_FuelAlertThresholds[m_NextAlertIndex] >= fuel)
            m_NextAlertIndex++;
    }

    /// <summary>
    /// Throws a handful of small particles up out of the fire, each randomly
    /// one of the lava/gray/dark-gray colors so the burst reads as embers,
    /// ash and scorched soot rather than a single flat color.
    /// </summary>
    void EmitBurst(int count, float upwardBias)
    {
        if (m_ImpactParticles == null || count <= 0)
            return;

        var emit = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            float pick = Random.value;
            emit.startColor = pick < 0.4f ? m_ImpactLavaColor : pick < 0.7f ? m_ImpactGrayColor : m_ImpactDarkColor;
            emit.startSize = m_ImpactParticleSize * Random.Range(0.5f, 1.3f);
            emit.position = transform.position + Vector3.up * 0.35f + Random.insideUnitSphere * 0.25f;
            emit.velocity = new Vector3(
                Random.Range(-0.7f, 0.7f),
                Random.Range(0.4f, 1.6f) * upwardBias,
                Random.Range(-0.7f, 0.7f));
            m_ImpactParticles.Emit(emit, 1);
        }
    }

    /// <summary>Lazily builds the persistent particle system the impact/alert bursts feed.</summary>
    void EnsureImpactParticles()
    {
        if (m_ImpactParticles != null)
            return;

        var go = new GameObject("Campfire Impact Particles");
        go.transform.SetParent(transform, false);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.duration = 5f;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0.8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;

        // Bursts only: nothing emits on its own, PlayImpact/EmitBurst feed it.
        var emission = ps.emission;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.18f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        var material = GetParticleMaterial();
        if (material != null)
            renderer.sharedMaterial = material;

        m_ImpactParticles = ps;
        ps.Play();
    }

    static Material GetParticleMaterial()
    {
        if (!s_ParticleMaterialSearched)
        {
            s_ParticleMaterialSearched = true;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                s_ParticleMaterial = new Material(shader) { name = "M_CampfireImpact" };
                if (s_ParticleMaterial.HasProperty("_BaseColor"))
                    s_ParticleMaterial.SetColor("_BaseColor", Color.white);
                s_ParticleMaterial.color = Color.white;
            }
        }

        return s_ParticleMaterial;
    }

    /// <summary>
    /// Scales the crackle's volume on top of the fuel-driven level, without
    /// touching the fuel itself: the fire keeps burning and lighting the camp
    /// normally, only its sound is muffled. Used by BossIntro to quiet the
    /// world for the Coronado's entrance (see JEFE_FINAL.md 3); 1 is normal.
    /// </summary>
    public void SetAudioDuckMultiplier(float multiplier)
    {
        m_AudioDuckMultiplier = Mathf.Clamp01(multiplier);
        ApplyVisuals();
    }

    /// <summary>
    /// Scales the passive burn rate on top of the normal drain - used by the
    /// Coronado's Fase 3 (menos de 3 m): its closeness burns the fire twice as
    /// fast (see JEFE_FINAL.md 4). 1 is normal; call with 1 again to clear it.
    /// </summary>
    public void SetProximityDrainMultiplier(float multiplier)
    {
        m_ProximityDrainMultiplier = Mathf.Max(0f, multiplier);
    }

    /// <summary>
    /// Scales the passive burn on top of the normal drain - GameManager ramps
    /// this up across the night so a full fire lasts less and less. 1 is normal.
    /// </summary>
    public void SetBurnRateMultiplier(float multiplier)
    {
        m_BurnRateMultiplier = Mathf.Max(0f, multiplier);
    }

    /// <summary>
    /// Leans the flame away from a nearby threat (the Coronado) - a permanent
    /// compass: whichever way the flame tilts, that is where the danger is
    /// (see JEFE_FINAL.md 6). Recomputed fresh from the flame's own neutral
    /// shape every call, capped at <see cref="m_MaxLeanAngle"/>, so it tracks
    /// the threat smoothly as it moves without drifting or needing a reset.
    /// Pass null to let the flame settle back to its neutral, upright shape.
    /// </summary>
    public void SetThreatPosition(Vector3? worldPosition)
    {
        if (m_FlameParticles == null)
            return;

        // A plain field initializer does not run for a component deserialized
        // from a scene (only Instantiate()/AddComponent() get that) - without
        // this guard, a call landing before Awake() ran would cap the lean
        // against the degenerate all-zero Quaternion instead of identity, and
        // Quaternion.RotateTowards silently ignores the cap in that case.
        if (!m_FlameNeutralCached)
            CacheFlameNeutralRotation();

        if (!worldPosition.HasValue)
        {
            m_FlameParticles.transform.localRotation = m_FlameNeutralRotation;
            return;
        }

        Vector3 away = transform.position - worldPosition.Value;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
        {
            m_FlameParticles.transform.localRotation = m_FlameNeutralRotation;
            return;
        }

        // The away direction is computed in world space but applied as a local
        // rotation, so it still leans correctly even if the campfire itself
        // were ever rotated. "Fully leaned" would point the flame's local
        // forward straight away from the threat; RotateTowards caps how far
        // it actually gets there from its neutral shape.
        Vector3 localAway = transform.InverseTransformDirection(away.normalized);
        Quaternion desired = Quaternion.LookRotation(localAway, Vector3.up);
        m_FlameParticles.transform.localRotation = Quaternion.RotateTowards(m_FlameNeutralRotation, desired, m_MaxLeanAngle);
    }

    /// <summary>Caches the flame's authored rotation once, so leaning always has a sane baseline to return to and rotate from.</summary>
    void CacheFlameNeutralRotation()
    {
        if (m_FlameParticles != null)
            m_FlameNeutralRotation = m_FlameParticles.transform.localRotation;
        m_FlameNeutralCached = true;
    }

    void ApplyVisuals()
    {
        float n = m_MaxFuel <= 0f ? 0f : Mathf.Clamp01(m_DisplayFuel / m_MaxFuel);
        float curved = Mathf.Pow(n, m_LightCurveExponent);

        // Whether the fire exists at all is the real fuel's call, not the ramped
        // display value: the flame and the light must both come on the instant a
        // log is added, while only their size swells over m_VisualRampSeconds.
        bool burning = m_CurrentFuel > 0f;

        if (m_FireLight != null)
        {
            m_FireLight.enabled = burning;
            m_FireLight.color = Color.Lerp(m_EmberColor, m_FlameColor, curved);

            // Cache the fuel-driven baseline; the flicker layers on top of it.
            m_BaseLightIntensity = Mathf.Lerp(m_MinLightIntensity, m_MaxLightIntensity, curved);
            m_BaseLightRange = Mathf.Lerp(m_MinLightRange, m_MaxLightRange, curved);
            ApplyLightFlicker();
        }

        if (m_FlameParticles != null)
        {
            var emission = m_FlameParticles.emission;
            emission.rateOverTime = m_MaxEmissionRate * n;

            var main = m_FlameParticles.main;
            main.startSizeMultiplier = Mathf.Lerp(0.25f, m_MaxFlameSize, n);

            // Play/stop on the fuel's edge, never every frame: calling Play() again
            // before a low-rate emitter has spawned its first particle resets its
            // counters, so the flame could stay dark until the next fuel change (in
            // the prologue, that only arrived with the night's drain).
            if (burning && !m_FlamePlaying)
            {
                m_FlameParticles.Play();
                m_FlamePlaying = true;
            }
            else if (!burning && m_FlamePlaying)
            {
                // StopEmitting lets the last flames die down instead of snapping off.
                m_FlameParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                m_FlamePlaying = false;
            }
        }

        if (m_FireAudio != null)
        {
            // Silent while there is no fire at all, so a dead prologue campfire
            // is not quietly hissing the whole time.
            m_FireAudio.volume = n <= 0.001f
                ? 0f
                : Mathf.Lerp(m_MinFireVolume, m_MaxFireVolume, n) * m_AudioDuckMultiplier;
            m_FireAudio.pitch = Mathf.Lerp(m_MinFirePitch, m_MaxFirePitch, n);
        }
    }

    /// <summary>
    /// Waggers the fire light around its fuel-driven baseline. Two Perlin-noise
    /// octaves are mixed so the light has both a slow, breathing sway and a
    /// faster, sharper crackle; fuel only sets the average brightness, never the
    /// moment-to-moment amount. <see cref="Flare"/> still spikes on top of this.
    /// </summary>
    void ApplyLightFlicker()
    {
        if (m_FireLight == null)
            return;

        float n = m_MaxFuel <= 0f ? 0f : Mathf.Clamp01(m_DisplayFuel / m_MaxFuel);

        float t = Time.time * m_FlickerSpeed;
        float slow = Mathf.PerlinNoise(t * 0.17f, 3.7f);   // breathing sway
        float fast = Mathf.PerlinNoise(41.3f, t);          // crackle snap

        // More sway as the fire gutters down to embers.
        float amount = m_FlickerAmount * Mathf.Lerp(1f + m_LowFuelFlickerBoost, 1f, n);
        float wobble = ((slow - 0.5f) * 0.7f + (fast - 0.5f) * 0.9f) * 2f * amount;
        float flicker = Mathf.Max(0.05f, 1f + wobble);

        m_FireLight.intensity = m_BaseLightIntensity * (1f + m_Flare * 0.8f) * flicker;
        m_FireLight.range = m_BaseLightRange * (1f + (flicker - 1f) * m_FlickerRangeInfluence);
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

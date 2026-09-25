using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Two things drive the world's light. The campfire's fuel darkens it as the
/// fire dies (the fire is the main light source; the moon only gives a faint
/// cold rim). The prologue opens in late afternoon - a warm directional sun
/// under an orange-to-purple sky dome (the same dome the dawn reuses) - and the
/// dusk creeps to a dark-but-not-night level on its own over
/// <see cref="m_WaitDuskDuration"/> and waits there: waiting must never reach
/// night, or the game would begin without the campfire ever being lit. Only once
/// the player feeds the fire does the dusk rush the rest of the way to night
/// over <see cref="m_DuskRushDuration"/>, and the blood moon opens it.
/// And the survival clock (GameManager.SurvivalNormalized) slowly
/// brings the dawn in across the whole 360 degrees: the night sky warms up,
/// the moon fills out from a dark disc to a full one, a low-poly sun climbs
/// into the sky, ambient light and fog brighten together and a chorus of
/// placeholder birds fades in, so the player can see the night ending from any
/// direction.
/// </summary>
[DisallowMultipleComponent]
public class NightEnvironmentController : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Fire whose fuel drives the darkness.")]
    CampfireFuel m_Campfire;

    [SerializeField]
    [Tooltip("Faint moonlight used when the fire is out.")]
    Light m_MoonLight;

    [Header("Ambient")]
    [SerializeField] Color m_DarkAmbient = new Color(0.02f, 0.03f, 0.05f);
    [SerializeField] Color m_FireAmbient = new Color(0.35f, 0.25f, 0.18f);
    [SerializeField] float m_DarkAmbientIntensity = 0.12f;
    [SerializeField] float m_FireAmbientIntensity = 0.7f;

    [Header("Moon")]
    [Tooltip("Moon intensity while the fire is well fed.")]
    [SerializeField] float m_MoonIntensityLit = 0.1f;
    [Tooltip("Moon intensity while the fire is out or barely an ember. Kept low " +
        "(rather than brighter than the lit value) so darkness outside the firelight " +
        "reads as close to absolute, both at the start of the game and whenever the " +
        "player lets the fire die.")]
    [SerializeField] float m_MoonIntensityDark = 0.05f;

    [Header("Fog")]
    [SerializeField] bool m_UseFog = true;
    [Tooltip("Exponential-squared fog reads as a wall of darkness rather than a light haze.")]
    [SerializeField] FogMode m_FogMode = FogMode.ExponentialSquared;
    [SerializeField] Color m_FogColor = new Color(0.05f, 0.06f, 0.09f);
    [Tooltip("Fog density while the fire is well fed; the air is clear.")]
    [SerializeField] float m_FogDensity = 0.012f;
    [Tooltip("Fog density once the fire is down to embers, so darkness closes in. " +
        "Ported from dev/Jafet's darkness effect.")]
    [SerializeField] float m_DarknessFogDensity = 3.5f;
    [Tooltip("Fuel fraction (0-1) treated as the ember floor when mapping fuel to darkness.")]
    [SerializeField] float m_DarknessFuelFloor = 0.08f;

    [Header("Dawn (driven by survival time)")]
    [SerializeField] GameManager m_GameManager;
    [Tooltip("Procedural skybox material; its exposure and ground colour are animated. Optional.")]
    [SerializeField] Material m_SkyMaterial;
    [Tooltip("Fraction of the survival clock (0-1) the world holds at full midnight before the dawn starts.")]
    [SerializeField] float m_DawnStart = 0.66f;
    [Tooltip("pow(): above 1 keeps the first minute dim and lets the light gather speed.")]
    [SerializeField] float m_DawnCurve = 1.8f;
    [SerializeField] float m_SunPitchNight = -22f;
    [SerializeField] float m_SunPitchDawn = 12f;
    [Tooltip("Compass heading the sun rises from: 180 = the +Z horizon the player faces.")]
    [SerializeField] float m_SunYaw = 180f;
    [SerializeField] Color m_SunColorNight = new Color(0.55f, 0.62f, 0.9f);
    [SerializeField] Color m_SunColorDawn = new Color(1f, 0.6f, 0.32f);
    [SerializeField] float m_SunIntensityDawn = 1.1f;
    [SerializeField] float m_SkyExposureNight = 0.12f;
    [SerializeField] float m_SkyExposureDawn = 0.5f;
    [Tooltip("Six-sided skybox tint at the start of the night (matches the pack's neutral 0.5 grey).")]
    [SerializeField] Color m_SkyTintNight = new Color(0.5f, 0.5f, 0.5f);
    [Tooltip("Six-sided skybox tint at dawn, warming the night's cool sky toward the rising sun.")]
    [SerializeField] Color m_SkyTintDawn = new Color(1f, 0.72f, 0.5f);
    [SerializeField] Color m_GroundColorNight = new Color(0.01f, 0.01f, 0.02f);
    [SerializeField] Color m_GroundColorDawn = new Color(0.32f, 0.22f, 0.2f);
    [SerializeField] Color m_DawnAmbient = new Color(0.55f, 0.42f, 0.4f);
    [SerializeField] float m_DawnAmbientIntensity = 1f;
    [SerializeField] Color m_DawnFogColor = new Color(0.62f, 0.5f, 0.45f);
    [SerializeField] float m_DawnFogDensity = 0.018f;

    [Header("Moon phase (progress indicator)")]
    [Tooltip("The moon's renderer. Left empty, it is found by name at Awake.")]
    [SerializeField] Renderer m_MoonRenderer;
    [Tooltip("Dark, almost invisible disc at midnight.")]
    [SerializeField] Color m_MoonColorNew = new Color(0.02f, 0.02f, 0.03f);
    [SerializeField] Color m_MoonColorFull = new Color(0.9f, 0.92f, 1f);
    [SerializeField] Color m_MoonEmissionNew = new Color(0f, 0f, 0f);
    [SerializeField] Color m_MoonEmissionFull = new Color(1.5f, 1.6f, 2f);
    [Tooltip("Blood red the moon fills out to before it starts whitening.")]
    [SerializeField] Color m_MoonColorRed = new Color(0.85f, 0.07f, 0.05f);
    [SerializeField] Color m_MoonEmissionRed = new Color(2.6f, 0.2f, 0.1f);
    [Tooltip("Night progress (0-1) at which the moon is fully red.")]
    [SerializeField] float m_MoonRedAt = 0.4f;
    [Tooltip("Night progress (0-1) at which the red has slowly whitened into the full moon.")]
    [SerializeField] float m_MoonWhiteAt = 0.82f;
    [Tooltip("Night progress (0-1) at which the moon starts to hide; gone just before the sun shows.")]
    [SerializeField] float m_MoonHideAt = 0.9f;

    [Header("Prologue dusk (afternoon -> night)")]
    [Tooltip("When on, the world opens in late afternoon and dims to the waiting dusk until the fire is fed; off starts at night.")]
    [SerializeField] bool m_StartInAfternoon = true;
    [Tooltip("How far the afternoon dims on its own while the fire is still dead (0-1). " +
        "The world creeps to this dark-but-not-night level and waits there, so time " +
        "visibly passes without the night ever starting; it is kept just below 1 so " +
        "waiting alone can never begin the game.")]
    [Range(0f, 0.98f)] [SerializeField] float m_WaitDuskLevel = 0.55f;
    [Tooltip("Seconds the afternoon takes to creep to the waiting dusk level on its own.")]
    [SerializeField] float m_WaitDuskDuration = 120f;
    [Tooltip("Seconds the dusk takes to finish once the prologue rushes it (the first log in the fire).")]
    [SerializeField] float m_DuskRushDuration = 5f;
    [SerializeField] Color m_DayAmbient = new Color(0.62f, 0.58f, 0.5f);
    [Tooltip("Kept low so the afternoon sun (not flat ambient) defines the shading; " +
        "a high ambient washes the props back out to solid colour.")]
    [SerializeField] float m_DayAmbientIntensity = 0.6f;
    [SerializeField] float m_DaySkyExposure = 0.9f;
    [SerializeField] Color m_DaySkyTint = new Color(0.85f, 0.88f, 0.96f);
    [SerializeField] Color m_DayGroundColor = new Color(0.34f, 0.31f, 0.25f);
    [SerializeField] Color m_DayFogColor = new Color(0.63f, 0.64f, 0.62f);
    [SerializeField] float m_DayFogDensity = 0.003f;

    [Header("Afternoon sun (prologue lighting)")]
    [Tooltip("Directional light that stands in for the late-afternoon sun. Created at runtime when empty.")]
    [SerializeField] Light m_DayLight;
    [Tooltip("Sun intensity at the brightest point of the afternoon.")]
    [SerializeField] float m_DaySunIntensity = 1.7f;
    [Tooltip("Sun colour while it is still fairly high (early afternoon).")]
    [SerializeField] Color m_DaySunColorHigh = new Color(1f, 0.82f, 0.55f);
    [Tooltip("Sun colour as it sinks to the horizon (6 pm): the warm sunset red.")]
    [SerializeField] Color m_DaySunColorLow = new Color(1f, 0.45f, 0.2f);
    [Tooltip("Sun pitch (degrees above the horizon) at the start of the afternoon.")]
    [SerializeField] float m_DaySunPitchHigh = 20f;
    [Tooltip("Sun pitch (degrees) once the night has fallen; negative is below the horizon.")]
    [SerializeField] float m_DaySunPitchLow = -6f;
    [Tooltip("Compass heading the afternoon sun sits at: 180 = the +Z horizon the player faces.")]
    [SerializeField] float m_DaySunYaw = 180f;

    [Header("Blood moon (night opening)")]
    [Tooltip("Seconds the moon takes to swell from white to a very bright blood red.")]
    [SerializeField] float m_BloodMoonRise = 4.5f;
    [Tooltip("Seconds of the 1/x recovery the moon takes to whiten back. Set near the night length.")]
    [SerializeField] float m_BloodMoonRecover = 150f;
    [Tooltip("Extra emission multiplier at the reddest point, so the moon visibly blows out.")]
    [SerializeField] float m_BloodMoonPeak = 1.6f;

    [Header("Rising sun")]
    [SerializeField] bool m_BuildSun = true;
    [Tooltip("Optional sun material. Left empty, a fog-free material is built at Awake.")]
    [SerializeField] Material m_SunMaterial;
    [SerializeField] Color m_SunColor = new Color(1f, 0.96f, 0.85f);
    [SerializeField] float m_SunDistance = 400f;
    [Tooltip("Diameter of the low-poly sun sphere at full size.")]
    [SerializeField] float m_SunDiameter = 34f;
    [Tooltip("Dawn progress (0-1) at which the sun starts to climb into view.")]
    [SerializeField] float m_SunAppearAt = 0.45f;
    [Tooltip("Icosphere subdivisions for the sun sphere. 3 (~1.3k tris) keeps the " +
        "silhouette round without making the mesh heavy.")]
    [SerializeField, Range(0, 4)] int m_SunSubdivisions = 3;

    [Header("Dawn sky dome")]
    [Tooltip("Builds a large unlit dome that fades in over the night sky, turning it warm.")]
    [SerializeField] bool m_BuildDawnSky = true;
    [Tooltip("Colour the dawn sky settles to at the horizon (the sunrise band).")]
    [SerializeField] Color m_DawnSkyHorizon = new Color(1f, 0.5f, 0.3f);
    [Tooltip("Colour the dawn sky settles to overhead.")]
    [SerializeField] Color m_DawnSkyZenith = new Color(0.22f, 0.34f, 0.62f);
    [SerializeField] Color m_DawnSkyGlow = new Color(1f, 0.75f, 0.45f);
    [SerializeField] float m_DawnSkyGlowStrength = 1.1f;
    [Tooltip("Radius of the dawn sky dome; must sit inside the camera far clip and " +
        "beyond the sun/moon so they stay visible in front of it.")]
    [SerializeField] float m_DawnSkyRadius = 800f;

    [Header("Afternoon sky dome")]
    [Tooltip("Colour the afternoon sky settles to at the horizon (the sunset band).")]
    [SerializeField] Color m_DaySkyHorizon = new Color(1f, 0.5f, 0.24f);
    [Tooltip("Colour the afternoon sky settles to overhead (dusky purple).")]
    [SerializeField] Color m_DaySkyZenith = new Color(0.24f, 0.2f, 0.48f);
    [SerializeField] Color m_DaySkyGlow = new Color(1f, 0.68f, 0.34f);
    [SerializeField] float m_DaySkyGlowStrength = 1f;
    [Tooltip("How solidly the afternoon dome covers the skybox underneath.")]
    [Range(0f, 1f)] [SerializeField] float m_DaySkyAlpha = 0.98f;

    [Header("Dawn birds (placeholder)")]
    [Tooltip("Fades in as the sky brightens. Left empty, the generated birdsong is used.")]
    [SerializeField] AudioClip m_DawnBirds;
    [SerializeField] float m_DawnBirdsVolume = 0.55f;
    [Tooltip("Dawn progress (0-1) at which the birds can first be heard.")]
    [SerializeField] float m_DawnBirdsStart = 0.25f;

    float? m_ForcedNormalized;
    float m_Dusk;
    bool m_DuskRushing;
    bool m_BloodMoon;
    float m_BloodMoonStart;
    float m_Afternoon;

    Material m_MoonMaterial;
    Transform m_Sun;
    Renderer m_SunRenderer;
    Material m_SunMaterialInstance;
    Transform m_DawnSky;
    Material m_DawnSkyMaterial;
    Material m_SkyInstance;
    AudioSource m_BirdsSource;

    /// <summary>
    /// When set, overrides the fuel-driven light level (0-1) every frame instead of
    /// reading the campfire. Used by GameManager to drive the victory brighten-up and
    /// the defeat fade-to-black independently of whatever the fire happens to be
    /// doing. Set back to null to resume normal fuel-driven behaviour.
    /// </summary>
    public float? ForcedNormalized
    {
        get => m_ForcedNormalized;
        set => m_ForcedNormalized = value;
    }

    /// <summary>True once the afternoon has fully bled into night.</summary>
    public bool DuskComplete => m_Dusk >= 1f;

    /// <summary>Whether this controller opened in the afternoon (the prologue).</summary>
    public bool StartedInAfternoon => m_StartInAfternoon;

    /// <summary>
    /// Accelerates the remaining dusk so the sky snaps to night in a few
    /// seconds. Called when the player first feeds the fire.
    /// </summary>
    public void BeginDuskRush()
    {
        m_DuskRushing = true;
    }

    /// <summary>
    /// Starts the blood-moon opening: bright red in a few seconds, then a long
    /// 1/x whitening across the night. Idempotent, and fired automatically the
    /// moment the rushed dusk finishes (which only happens once the player has
    /// fed the fire).
    /// </summary>
    public void TriggerBloodMoon()
    {
        if (m_BloodMoon)
            return;

        m_BloodMoon = true;
        m_BloodMoonStart = Time.time;
    }

    void Awake()
    {
        m_Dusk = m_StartInAfternoon ? 0f : 1f;
        CacheMoon();

        // Animate a per-controller copy of the skybox. Writing the exposure/tint
        // straight into m_SkyMaterial (the shared asset assigned in the scene)
        // dirtied the committed material every time the game ran in the editor.
        if (m_SkyMaterial != null)
        {
            m_SkyInstance = new Material(m_SkyMaterial) { name = m_SkyMaterial.name + " (Runtime)" };
            m_SkyMaterial = m_SkyInstance;
            RenderSettings.skybox = m_SkyInstance;
        }

        BuildDayLight();
        BuildSun();
        BuildDawnSky();
        BuildBirds();
        Apply(1f);
    }

    void Update()
    {
        // Left alone, the afternoon creeps toward the waiting dusk and holds
        // there. That level is deliberately below 1, so waiting can never reach
        // night and start the game with a dead fire. BeginDuskRush - the first log
        // in the fire - is what carries the dusk the rest of the way.
        if (m_DuskRushing)
            m_Dusk = Mathf.MoveTowards(m_Dusk, 1f, Time.deltaTime / Mathf.Max(0.05f, m_DuskRushDuration));
        else if (m_StartInAfternoon && m_Dusk < m_WaitDuskLevel)
            m_Dusk = Mathf.MoveTowards(m_Dusk, Mathf.Min(m_WaitDuskLevel, 0.98f), Time.deltaTime / Mathf.Max(0.05f, m_WaitDuskDuration));

        // The moon opens the night the moment the rushed dusk finishes.
        if (m_StartInAfternoon && m_Dusk >= 1f)
            TriggerBloodMoon();

        float n = m_ForcedNormalized ?? (m_Campfire != null ? m_Campfire.FuelNormalized : 0f);
        Apply(n);
    }

    void OnDestroy()
    {
        if (m_SunMaterialInstance != null)
            Destroy(m_SunMaterialInstance);
        if (m_DawnSkyMaterial != null)
            Destroy(m_DawnSkyMaterial);
        if (m_SkyInstance != null)
            Destroy(m_SkyInstance);
    }

    void Apply(float normalized)
    {
        float survival = m_GameManager != null ? m_GameManager.SurvivalNormalized : 0f;
        Apply(normalized, DawnFromSurvival(survival), survival);
    }

    /// <summary>
    /// Eases the raw survival clock into "how far into the dawn we are": nothing
    /// happens until <see cref="m_DawnStart"/>, then the light gathers speed
    /// (slow at first, fast at the end).
    /// </summary>
    public float DawnFromSurvival(float survivalNormalized)
    {
        float n = Mathf.Clamp01(survivalNormalized);
        float hold = Mathf.Clamp01(m_DawnStart);
        if (hold >= 1f)
            return n >= 1f ? 1f : 0f;

        float local = Mathf.InverseLerp(hold, 1f, n);
        return Mathf.Pow(local, m_DawnCurve);
    }

    /// <summary>
    /// <paramref name="normalized"/> is the fuel-driven light level (also forced by
    /// GameManager for the victory/defeat fades); <paramref name="dawn"/> is the 0-1
    /// progress of the night ending; <paramref name="survival"/> is the raw 0-1
    /// survival clock, which the moon phase follows across the whole night.
    /// </summary>
    public void Apply(float normalized, float dawn, float survival)
    {
        normalized = Mathf.Clamp01(normalized);
        dawn = Mathf.Clamp01(dawn);
        survival = Mathf.Clamp01(survival);

        // 1 = full afternoon, 0 = full night. Every value below is computed as
        // night and then lifted toward daylight by this, so the prologue dusk is
        // a single blend on top of the established night look.
        m_Afternoon = 1f - Mathf.Clamp01(m_Dusk);

        // The defeat fade (forced toward 0) must still take the sky to black.
        float blackout = m_ForcedNormalized.HasValue ? Mathf.Clamp01(m_ForcedNormalized.Value) : 1f;

        RenderSettings.ambientMode = AmbientMode.Flat;
        Color fireAmbient = Color.Lerp(m_DarkAmbient, m_FireAmbient, normalized);
        float fireIntensity = Mathf.Lerp(m_DarkAmbientIntensity, m_FireAmbientIntensity, normalized);
        RenderSettings.ambientLight = Color.Lerp(fireAmbient, m_DawnAmbient, dawn);
        RenderSettings.ambientIntensity = Mathf.Lerp(fireIntensity, m_DawnAmbientIntensity, dawn) * blackout;

        RenderSettings.fog = m_UseFog;
        RenderSettings.fogMode = m_FogMode;
        RenderSettings.fogColor = Color.Lerp(m_FogColor, m_DawnFogColor, dawn) * blackout;

        // Darkness fog (ported from dev/Jafet): the air thickens as the fire
        // burns down and clears again once it is fed, so letting the fire die
        // closes the world in. The dawn then clears whatever darkness remains.
        float visibility = Mathf.Clamp01(Mathf.InverseLerp(m_DarknessFuelFloor, 1f, normalized));
        float fogDensity = Mathf.Lerp(m_DarknessFogDensity, m_FogDensity, visibility);
        RenderSettings.fogDensity = Mathf.Lerp(fogDensity, m_DawnFogDensity, dawn);

        if (m_MoonLight != null)
        {
            float moon = Mathf.Lerp(m_MoonIntensityDark, m_MoonIntensityLit, normalized);
            m_MoonLight.intensity = (moon + m_SunIntensityDawn * dawn * dawn) * blackout;
            m_MoonLight.color = Color.Lerp(m_SunColorNight, m_SunColorDawn, dawn);
            m_MoonLight.transform.rotation = Quaternion.Euler(Mathf.Lerp(m_SunPitchNight, m_SunPitchDawn, dawn), m_SunYaw, 0f);
        }

        ApplyMoonPhase(survival, blackout);
        ApplySun(dawn, blackout);
        ApplyDawnSky(dawn, blackout);
        ApplyBirds(dawn);

        if (m_SkyMaterial != null)
        {
            // Property-guarded so the same controller drives both the procedural
            // skybox (which has _Exposure/_GroundColor) and the six-sided night
            // skybox from the Day - Night pack (which has _Exposure/_Tint).
            if (m_SkyMaterial.HasProperty("_Exposure"))
                m_SkyMaterial.SetFloat("_Exposure", Mathf.Lerp(m_SkyExposureNight, m_SkyExposureDawn, dawn) * blackout);

            if (m_SkyMaterial.HasProperty("_GroundColor"))
                m_SkyMaterial.SetColor("_GroundColor", Color.Lerp(m_GroundColorNight, m_GroundColorDawn, dawn));

            if (m_SkyMaterial.HasProperty("_Tint"))
                m_SkyMaterial.SetColor("_Tint", Color.Lerp(m_SkyTintNight, m_SkyTintDawn, dawn));
        }

        ApplyDaylight(blackout);
    }

    /// <summary>
    /// Lifts the night look toward the late-afternoon opening. Runs last so it
    /// simply overrides whichever night value was just written; when the dusk
    /// finishes (<see cref="m_Dusk"/> = 1) the ambient/fog/sky blends are no-ops,
    /// while the sun is switched fully off.
    /// </summary>
    void ApplyDaylight(float blackout)
    {
        float afternoon = Mathf.Clamp01(m_Afternoon);

        if (afternoon > 0.001f)
        {
            RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, m_DayAmbient, afternoon);
            RenderSettings.ambientIntensity = Mathf.Lerp(RenderSettings.ambientIntensity, m_DayAmbientIntensity, afternoon) * blackout;
            RenderSettings.fog = m_UseFog;
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, m_DayFogColor, afternoon) * blackout;

            // The fire-out darkness fog is a night effect, so it must not creep in
            // across the whole dusk: blending it linearly by the afternoon closed the
            // world into a wall of fog within seconds of the afternoon starting.
            // Ease it on the night side so the afternoon stays clear and the fog only
            // gathers once night is actually falling.
            float night = 1f - afternoon;
            float nightFog = night * night * night;
            RenderSettings.fogDensity = Mathf.Lerp(m_DayFogDensity, RenderSettings.fogDensity, nightFog);

            // No moon and no directional moonlight while the sun is still up.
            if (m_MoonLight != null)
                m_MoonLight.intensity *= 1f - afternoon;

            if (m_SkyMaterial != null)
            {
                if (m_SkyMaterial.HasProperty("_Exposure"))
                    m_SkyMaterial.SetFloat("_Exposure", Mathf.Lerp(m_SkyMaterial.GetFloat("_Exposure"), m_DaySkyExposure, afternoon) * blackout);
                if (m_SkyMaterial.HasProperty("_Tint"))
                    m_SkyMaterial.SetColor("_Tint", Color.Lerp(m_SkyMaterial.GetColor("_Tint"), m_DaySkyTint, afternoon));
                if (m_SkyMaterial.HasProperty("_GroundColor"))
                    m_SkyMaterial.SetColor("_GroundColor", Color.Lerp(m_SkyMaterial.GetColor("_GroundColor"), m_DayGroundColor, afternoon));
            }
        }

        ApplyDaySun(afternoon, blackout);
    }

    /// <summary>
    /// Drives the late-afternoon sun. It is the only directional light while the
    /// sky is bright - without it the camp is lit by flat ambient alone and the
    /// props read as untextured solid colour - so it sinks and warms as the dusk
    /// runs, then switches off entirely once night falls.
    /// </summary>
    void ApplyDaySun(float afternoon, float blackout)
    {
        if (m_DayLight == null)
            return;

        float intensity = m_DaySunIntensity * Mathf.SmoothStep(0f, 1f, afternoon) * blackout;
        m_DayLight.enabled = intensity > 0.002f;

        if (!m_DayLight.enabled)
        {
            m_DayLight.intensity = 0f;
            return;
        }

        m_DayLight.intensity = intensity;
        m_DayLight.color = Color.Lerp(m_DaySunColorLow, m_DaySunColorHigh, afternoon);
        float pitch = Mathf.Lerp(m_DaySunPitchLow, m_DaySunPitchHigh, afternoon);
        m_DayLight.transform.rotation = Quaternion.Euler(pitch, m_DaySunYaw, 0f);
    }

    void CacheMoon()
    {
        if (m_MoonRenderer == null)
        {
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                if (renderer.gameObject.name == "Moon")
                {
                    m_MoonRenderer = renderer;
                    break;
                }
            }
        }

        if (m_MoonRenderer != null)
        {
            // Per-renderer instance so the moon phase never dirties the shared asset.
            m_MoonMaterial = m_MoonRenderer.material;
            m_MoonMaterial.EnableKeyword("_EMISSION");
        }
    }

    void ApplyMoonPhase(float survival, float blackout)
    {
        if (m_MoonMaterial == null)
            return;

        Color color;
        Color emission;

        if (m_BloodMoon)
        {
            // Night opening: the moon swells from white to a blinding blood red
            // in a few seconds, then whitens back on a 1/x curve spread across
            // the night, so the change is unmistakable and the player watches it
            // turn white again.
            float t = Time.time - m_BloodMoonStart;
            float rise = Mathf.InverseLerp(0f, Mathf.Max(0.05f, m_BloodMoonRise), t);
            color = Color.Lerp(m_MoonColorFull, m_MoonColorRed, rise);
            emission = Color.Lerp(m_MoonEmissionFull, m_MoonEmissionRed, rise);

            if (t > m_BloodMoonRise)
            {
                float x = (t - m_BloodMoonRise) / Mathf.Max(1f, m_BloodMoonRecover);
                float redness = 1f / (1f + 5f * x); // 1 at the peak, easing to 0
                color = Color.Lerp(m_MoonColorFull, color, redness);
                emission = Color.Lerp(m_MoonEmissionFull, emission, redness);
            }

            // A brief over-bright at the reddest point so the moon visibly blows out.
            emission *= Mathf.Lerp(1f, m_BloodMoonPeak, 1f - Mathf.Abs(rise - 1f));
        }
        else
        {
            // New moon as the night starts, fills out to a blood red disc, then
            // slowly whitens into the full moon, and finally hides just before the
            // sun comes up. Follows the whole survival clock (not just the compressed
            // dawn curve) so it reads as a smooth progress indicator, and is the only
            // progress readout the game has (see the cero-UI rule).
            float fill = Mathf.InverseLerp(0f, m_MoonRedAt, survival);
            float whiten = Mathf.InverseLerp(m_MoonRedAt, m_MoonWhiteAt, survival);

            color = Color.Lerp(m_MoonColorNew, m_MoonColorRed, fill);
            emission = Color.Lerp(m_MoonEmissionNew, m_MoonEmissionRed, fill);
            color = Color.Lerp(color, m_MoonColorFull, whiten);
            emission = Color.Lerp(emission, m_MoonEmissionFull, whiten);
        }

        float hide = 1f - Mathf.InverseLerp(m_MoonHideAt, 1f, survival);

        // The disc is opaque, so darkening its colour cannot fade it out - a
        // "faded" new moon just paints a hard black hole in the bright afternoon
        // sky. Keep it out of the sky entirely until the dusk has fully fallen;
        // the blood moon opens the night at that same moment, so nothing pops.
        float visible = m_Afternoon > 0.001f ? 0f : hide;
        color *= visible;
        emission *= visible * blackout;

        if (m_MoonMaterial.HasProperty("_BaseColor"))
            m_MoonMaterial.SetColor("_BaseColor", color);
        if (m_MoonMaterial.HasProperty("_Color"))
            m_MoonMaterial.SetColor("_Color", color);
        if (m_MoonMaterial.HasProperty("_EmissionColor"))
            m_MoonMaterial.SetColor("_EmissionColor", emission);

        if (m_MoonRenderer != null)
            m_MoonRenderer.enabled = visible > 0.01f && blackout > 0.01f;
    }

    /// <summary>
    /// Creates the afternoon directional light when the scene did not supply one.
    /// Built at runtime so the prologue lighting works on any scene the controller
    /// is dropped into, without a separate scene object to wire.
    /// </summary>
    void BuildDayLight()
    {
        if (m_DayLight != null)
            return;

        var go = new GameObject("Afternoon Sun");
        go.transform.SetParent(transform, false);

        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = m_DaySunColorHigh;
        light.intensity = m_DaySunIntensity;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.85f;

        m_DayLight = light;
    }

    void BuildSun()
    {
        if (!m_BuildSun || m_Sun != null)
            return;

        var go = new GameObject("Dawn Sun");
        go.transform.SetParent(transform, false);

        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildIcoSphere(m_SunSubdivisions);

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.sharedMaterial = m_SunMaterial != null ? m_SunMaterial : CreateSunMaterial();

        go.SetActive(false);
        m_Sun = go.transform;
        m_SunRenderer = renderer;
    }

    Material CreateSunMaterial()
    {
        Shader shader = Shader.Find("Custom/SunDisk");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        m_SunMaterialInstance = new Material(shader) { name = "M_Sun (Runtime)" };
        if (m_SunMaterialInstance.HasProperty("_Color"))
            m_SunMaterialInstance.SetColor("_Color", m_SunColor);
        if (m_SunMaterialInstance.HasProperty("_BaseColor"))
            m_SunMaterialInstance.SetColor("_BaseColor", m_SunColor);
        if (m_SunMaterialInstance.HasProperty("_Boost"))
            m_SunMaterialInstance.SetFloat("_Boost", 1.5f);
        return m_SunMaterialInstance;
    }

    void ApplySun(float dawn, float blackout)
    {
        if (m_Sun == null)
            return;

        float appear = Mathf.InverseLerp(m_SunAppearAt, 1f, dawn);
        appear = appear * appear; // lingers tiny then billows up as day breaks

        m_Sun.gameObject.SetActive(appear > 0.001f && blackout > 0.001f);
        if (!m_Sun.gameObject.activeSelf)
            return;

        // The sun rises opposite the light's forward direction, so the disk and the
        // directional light always agree about where the day is coming from.
        Vector3 direction = m_MoonLight != null ? -m_MoonLight.transform.forward : Vector3.up;
        m_Sun.position = direction * m_SunDistance;
        m_Sun.localScale = Vector3.one * (m_SunDiameter * appear);

        if (m_SunRenderer != null)
        {
            Material material = m_SunRenderer.material;
            if (material != null)
            {
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", m_SunColor);
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", m_SunColor);
                if (material.HasProperty("_Boost"))
                    material.SetFloat("_Boost", Mathf.Lerp(0.4f, 1.6f, appear) * blackout);
            }
        }
    }

    void BuildDawnSky()
    {
        if (!m_BuildDawnSky || m_DawnSky != null)
            return;

        Shader shader = Shader.Find("Custom/DawnSky");
        if (shader == null)
            return;

        m_DawnSkyMaterial = new Material(shader) { name = "M_DawnSky (Runtime)" };
        m_DawnSkyMaterial.SetColor("_HorizonColor", m_DawnSkyHorizon);
        m_DawnSkyMaterial.SetColor("_ZenithColor", m_DawnSkyZenith);
        m_DawnSkyMaterial.SetColor("_SunGlowColor", m_DawnSkyGlow);
        m_DawnSkyMaterial.SetFloat("_SunGlowStrength", m_DawnSkyGlowStrength);
        m_DawnSkyMaterial.SetFloat("_Alpha", 0f);

        var go = new GameObject("Dawn Sky");
        go.transform.SetParent(transform, false);

        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildIcoSphere(3);

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.sharedMaterial = m_DawnSkyMaterial;

        go.transform.localScale = Vector3.one * m_DawnSkyRadius;
        go.SetActive(false);
        m_DawnSky = go.transform;
    }

    /// <summary>
    /// Fades a warm, unlit dome in over the night skybox so the sky itself turns
    /// to morning instead of only brightening in place. The six-sided night
    /// textures are essentially pure blue (their red channel is ~0), so tinting
    /// them can never read as dawn - the dome is what actually warms the sky.
    /// </summary>
    void ApplyDawnSky(float dawn, float blackout)
    {
        if (m_DawnSkyMaterial == null || m_DawnSky == null)
            return;

        // The same dome serves both ends of the day: the warm afternoon sunset
        // (driven by how much daylight is left) and the dawn. Whichever is
        // stronger shows; the palette is blended toward the afternoon one.
        float dayAlpha = Mathf.SmoothStep(0f, 1f, m_Afternoon) * m_DaySkyAlpha;
        float dawnAlpha = Mathf.SmoothStep(0f, 1f, dawn);
        float alpha = Mathf.Max(dayAlpha, dawnAlpha) * blackout;
        m_DawnSkyMaterial.SetFloat("_Alpha", alpha);
        m_DawnSky.gameObject.SetActive(alpha > 0.002f);
        if (!m_DawnSky.gameObject.activeSelf)
            return;

        m_DawnSkyMaterial.SetColor("_HorizonColor", Color.Lerp(m_DawnSkyHorizon, m_DaySkyHorizon, m_Afternoon));
        m_DawnSkyMaterial.SetColor("_ZenithColor", Color.Lerp(m_DawnSkyZenith, m_DaySkyZenith, m_Afternoon));
        m_DawnSkyMaterial.SetColor("_SunGlowColor", Color.Lerp(m_DawnSkyGlow, m_DaySkyGlow, m_Afternoon));
        m_DawnSkyMaterial.SetFloat("_SunGlowStrength", Mathf.Lerp(m_DawnSkyGlowStrength, m_DaySkyGlowStrength, m_Afternoon));

        // The glow gathers around whichever body is currently the sun: the
        // afternoon directional light, or the moon doubling as the dawn sun.
        Transform sun = m_Afternoon > 0.5f && m_DayLight != null ? m_DayLight.transform : (m_MoonLight != null ? m_MoonLight.transform : null);
        Vector3 sunDirection = sun != null ? -sun.forward : Vector3.up;
        m_DawnSkyMaterial.SetVector("_SunDirection", sunDirection);
    }

    void BuildBirds()
    {
        AudioClip clip = m_DawnBirds != null ? m_DawnBirds : ProceduralSfx.BirdSong;
        if (clip == null)
            return;

        m_BirdsSource = gameObject.AddComponent<AudioSource>();
        m_BirdsSource.clip = clip;
        m_BirdsSource.loop = true;
        m_BirdsSource.spatialBlend = 0f;
        m_BirdsSource.playOnAwake = false;
        m_BirdsSource.volume = 0f;
        m_BirdsSource.Play();
    }

    void ApplyBirds(float dawn)
    {
        if (m_BirdsSource == null)
            return;

        float target = Mathf.InverseLerp(m_DawnBirdsStart, 1f, dawn);
        m_BirdsSource.volume = Mathf.SmoothStep(0f, 1f, target) * m_DawnBirdsVolume;
    }

    /// <summary>Faceted icosphere - a deliberately low-poly stand-in for the sun.</summary>
    static Mesh BuildIcoSphere(int subdivisions)
    {
        const float t = 1.61803398875f;
        var vertices = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
        };
        for (int i = 0; i < vertices.Count; i++)
            vertices[i] = vertices[i].normalized;

        var triangles = new List<int>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };

        for (int s = 0; s < subdivisions; s++)
            triangles = Subdivide(vertices, triangles);

        var mesh = new Mesh { name = "IcoSphere" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static List<int> Subdivide(List<Vector3> vertices, List<int> triangles)
    {
        var result = new List<int>(triangles.Count * 4);
        for (int i = 0; i < triangles.Count; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];

            int ab = Midpoint(vertices, a, b);
            int bc = Midpoint(vertices, b, c);
            int ca = Midpoint(vertices, c, a);

            result.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        return result;
    }

    static int Midpoint(List<Vector3> vertices, int a, int b)
    {
        vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized);
        return vertices.Count - 1;
    }
}

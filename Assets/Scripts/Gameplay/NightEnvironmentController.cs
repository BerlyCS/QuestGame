using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Two things drive the world's light. The campfire's fuel darkens it as the
/// fire dies (the fire is the main light source; the moon only gives a faint
/// cold rim). And the survival clock (GameManager.SurvivalNormalized) slowly
/// brings the dawn in across the whole 360 degrees: the procedural sky, the
/// "sun" light rising from below the horizon, ambient light and fog all warm
/// up and brighten together, so the player can see the night ending from any
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
    [SerializeField] Color m_FogColor = new Color(0.05f, 0.06f, 0.09f);
    [SerializeField] float m_FogDensity = 0.035f;

    [Header("Dawn (driven by survival time)")]
    [SerializeField] GameManager m_GameManager;
    [Tooltip("Procedural skybox material; its exposure and ground colour are animated. Optional.")]
    [SerializeField] Material m_SkyMaterial;
    [Tooltip("pow(survival, curve): above 1 keeps the first minute dim and lets the light gather speed.")]
    [SerializeField] float m_DawnCurve = 1.25f;
    [SerializeField] float m_SunPitchNight = -22f;
    [SerializeField] float m_SunPitchDawn = 12f;
    [Tooltip("Compass heading the sun rises from: 180 = the +Z horizon the player faces.")]
    [SerializeField] float m_SunYaw = 180f;
    [SerializeField] Color m_SunColorNight = new Color(0.55f, 0.62f, 0.9f);
    [SerializeField] Color m_SunColorDawn = new Color(1f, 0.6f, 0.32f);
    [SerializeField] float m_SunIntensityDawn = 1.1f;
    [SerializeField] float m_SkyExposureNight = 0.12f;
    [SerializeField] float m_SkyExposureDawn = 1.25f;
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

    float? m_ForcedNormalized;

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

    void Awake()
    {
        Apply(1f);
    }

    void Update()
    {
        float n = m_ForcedNormalized ?? (m_Campfire != null ? m_Campfire.FuelNormalized : 0f);
        Apply(n);
    }

    void Apply(float normalized)
    {
        float dawn = m_GameManager != null ? Mathf.Pow(m_GameManager.SurvivalNormalized, m_DawnCurve) : 0f;
        Apply(normalized, dawn);
    }

    /// <summary>
    /// <paramref name="normalized"/> is the fuel-driven light level (also forced by
    /// GameManager for the victory/defeat fades); <paramref name="dawn"/> is the 0-1
    /// progress of the night ending.
    /// </summary>
    public void Apply(float normalized, float dawn)
    {
        normalized = Mathf.Clamp01(normalized);
        dawn = Mathf.Clamp01(dawn);

        // The defeat fade (forced toward 0) must still take the sky to black.
        float blackout = m_ForcedNormalized.HasValue ? Mathf.Clamp01(m_ForcedNormalized.Value) : 1f;

        RenderSettings.ambientMode = AmbientMode.Flat;
        Color fireAmbient = Color.Lerp(m_DarkAmbient, m_FireAmbient, normalized);
        float fireIntensity = Mathf.Lerp(m_DarkAmbientIntensity, m_FireAmbientIntensity, normalized);
        RenderSettings.ambientLight = Color.Lerp(fireAmbient, m_DawnAmbient, dawn);
        RenderSettings.ambientIntensity = Mathf.Lerp(fireIntensity, m_DawnAmbientIntensity, dawn) * blackout;

        RenderSettings.fog = m_UseFog;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = Color.Lerp(m_FogColor, m_DawnFogColor, dawn) * blackout;
        RenderSettings.fogDensity = Mathf.Lerp(m_FogDensity, m_DawnFogDensity, dawn);

        if (m_MoonLight != null)
        {
            float moon = Mathf.Lerp(m_MoonIntensityDark, m_MoonIntensityLit, normalized);
            m_MoonLight.intensity = (moon + m_SunIntensityDawn * dawn * dawn) * blackout;
            m_MoonLight.color = Color.Lerp(m_SunColorNight, m_SunColorDawn, dawn);
            m_MoonLight.transform.rotation = Quaternion.Euler(Mathf.Lerp(m_SunPitchNight, m_SunPitchDawn, dawn), m_SunYaw, 0f);
        }

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
    }
}

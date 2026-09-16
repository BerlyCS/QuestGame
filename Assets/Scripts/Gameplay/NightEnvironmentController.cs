using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Darkens the world as the campfire dies. The fire is the main light source;
/// the moon only provides a faint cold rim of light.
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
    [SerializeField] Color m_FogColor = new Color(0.01f, 0.01f, 0.02f);
    [SerializeField] float m_FogDensity = 0.012f;

    void Awake()
    {
        Apply(1f);
    }

    void Update()
    {
        float n = m_Campfire != null ? m_Campfire.FuelNormalized : 0f;
        Apply(n);
    }

    void Apply(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.Lerp(m_DarkAmbient, m_FireAmbient, normalized);
        RenderSettings.ambientIntensity = Mathf.Lerp(m_DarkAmbientIntensity, m_FireAmbientIntensity, normalized);

        RenderSettings.fog = m_UseFog;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = m_FogColor;
        RenderSettings.fogDensity = m_FogDensity;

        if (m_MoonLight != null)
            m_MoonLight.intensity = Mathf.Lerp(m_MoonIntensityDark, m_MoonIntensityLit, normalized);
    }
}

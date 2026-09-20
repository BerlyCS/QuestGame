using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Controls campfire fuel, fire visuals, fog and darkness.
/// Darkness increases as fuel decreases.
/// </summary>
[DisallowMultipleComponent]
public class CampfireFuel : MonoBehaviour
{
    [Header("Fuel")]
    [SerializeField]
    [Tooltip("Maximum fuel the fire can hold.")]
    float m_MaxFuel = 1f;

    [SerializeField]
    [Range(0f, 1f)]
    float m_StartingFuelNormalized = 0.08f;

    [SerializeField]
    [Range(0f, 1f)]
    float m_MinEmberFuelNormalized = 0.08f;

    [SerializeField]
    float m_BurnRatePerSecond = 0.0033f;

    [Header("Fire Light")]
    [SerializeField] Light m_FireLight;
    [SerializeField] float m_MinLightIntensity = 0.3f;
    [SerializeField] float m_MaxLightIntensity = 6f;
    [SerializeField] float m_MinLightRange = 5f;
    [SerializeField] float m_MaxLightRange = 16f;

    [SerializeField]
    Color m_EmberColor = new Color(0.9f, 0.25f, 0.05f);

    [SerializeField]
    Color m_FlameColor = new Color(1f, 0.7f, 0.25f);

    [Header("Flame Particles")]
    [SerializeField] ParticleSystem m_FlameParticles;
    [SerializeField] float m_MaxEmissionRate = 40f;
    [SerializeField] float m_MaxFlameSize = 1f;

    [Header("Darkness - Fog")]
    [SerializeField] bool m_EnableFog = true;

    [SerializeField]
    Color m_FogColor = Color.black;

    [SerializeField]
    float m_MinFogDensity = 0.005f;

    [SerializeField]
    float m_MaxFogDensity = 3.5f;

    [Header("Darkness - Black Overlay")]
    [SerializeField]
    Image m_BlackOverlay;

    [SerializeField]
    [Range(0f, 1f)]
    float m_MaxBlackAlpha = 1f;

    [Header("Events")]
    [SerializeField] UnityEvent m_OnFuelChanged = new UnityEvent();
    [SerializeField] UnityEvent m_OnIgnited = new UnityEvent();
    [SerializeField] UnityEvent m_OnExtinguished = new UnityEvent();

    float m_CurrentFuel;
    bool m_WasBurning;

    public float MaxFuel => m_MaxFuel;
    public float CurrentFuel => m_CurrentFuel;

    public float FuelNormalized =>
        m_MaxFuel <= 0f
            ? 0f
            : Mathf.Clamp01(m_CurrentFuel / m_MaxFuel);

    public bool IsBurning => m_CurrentFuel > EmberFloor;

    public UnityEvent OnFuelChanged => m_OnFuelChanged;
    public UnityEvent OnIgnited => m_OnIgnited;
    public UnityEvent OnExtinguished => m_OnExtinguished;

    float EmberFloor =>
        Mathf.Clamp01(m_MinEmberFuelNormalized) * m_MaxFuel;

    void Awake()
    {
        m_CurrentFuel = Mathf.Max(
            EmberFloor,
            Mathf.Clamp01(m_StartingFuelNormalized) * m_MaxFuel
        );

        m_WasBurning = IsBurning;

        ConfigureFog();
        ApplyVisuals();
    }

    void Update()
    {
        float floor = EmberFloor;

        if (m_CurrentFuel > floor)
        {
            float next = Mathf.Max(
                floor,
                m_CurrentFuel - m_BurnRatePerSecond * Time.deltaTime
            );

            AddFuel(next - m_CurrentFuel, false);
        }

        UpdateDarkness();
    }

    public void AddFuel(float amount)
    {
        AddFuel(amount, true);
    }

    public void AddFuel(float amount, bool invokeEvents)
    {
        float before = m_CurrentFuel;

        m_CurrentFuel = Mathf.Clamp(
            m_CurrentFuel + amount,
            0f,
            m_MaxFuel
        );

        if (Mathf.Approximately(before, m_CurrentFuel))
            return;

        ApplyVisuals();
        UpdateDarkness();

        if (invokeEvents)
            m_OnFuelChanged.Invoke();

        bool burning = IsBurning;

        if (burning && !m_WasBurning)
            m_OnIgnited.Invoke();

        else if (!burning && m_WasBurning)
            m_OnExtinguished.Invoke();

        m_WasBurning = burning;
    }

    public void SetFuel(float amount)
    {
        m_CurrentFuel = Mathf.Clamp(
            amount,
            0f,
            m_MaxFuel
        );

        m_WasBurning = IsBurning;

        ApplyVisuals();
        UpdateDarkness();
    }

    void ConfigureFog()
    {
        if (!m_EnableFog)
            return;

        RenderSettings.fog = true;
        RenderSettings.fogColor = m_FogColor;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
    }

    void UpdateDarkness()
    {
        float n = FuelNormalized;

        // Convert fuel into a 0-1 value relative to the ember floor.
        // 0 = minimum ember (maximum darkness)
        // 1 = maximum fuel (minimum darkness)

        float visibility = Mathf.InverseLerp(
            m_MinEmberFuelNormalized,
            1f,
            n
        );

        visibility = Mathf.Clamp01(visibility);

        float darkness = 1f - visibility;

        // -----------------------------
        // FOG
        // -----------------------------

        if (m_EnableFog)
        {
            RenderSettings.fogDensity = Mathf.Lerp(
                m_MinFogDensity,
                m_MaxFogDensity,
                darkness
            );
        }

        // -----------------------------
        // BLACK OVERLAY
        // -----------------------------

        if (m_BlackOverlay != null)
        {
            Color color = m_BlackOverlay.color;

            color.a = Mathf.Clamp01(
                darkness * m_MaxBlackAlpha
            );

            m_BlackOverlay.color = color;
        }
    }

    void ApplyVisuals()
    {
        float n = FuelNormalized;

        // -----------------------------
        // FIRE LIGHT
        // -----------------------------

        if (m_FireLight != null)
        {
            m_FireLight.enabled = n > 0.001f;

            m_FireLight.intensity = Mathf.Lerp(
                m_MinLightIntensity,
                m_MaxLightIntensity,
                n
            );

            m_FireLight.range = Mathf.Lerp(
                m_MinLightRange,
                m_MaxLightRange,
                n
            );

            m_FireLight.color = Color.Lerp(
                m_EmberColor,
                m_FlameColor,
                n
            );
        }

        // -----------------------------
        // FLAME PARTICLES
        // -----------------------------

        if (m_FlameParticles != null)
        {
            var emission = m_FlameParticles.emission;

            emission.rateOverTime =
                m_MaxEmissionRate * n;

            var main = m_FlameParticles.main;

            main.startSizeMultiplier =
                Mathf.Lerp(0.25f, m_MaxFlameSize, n);

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
    }
}
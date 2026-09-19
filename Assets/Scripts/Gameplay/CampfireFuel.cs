using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Drives the campfire: fuel drains over time (down to a permanent ember floor,
/// never fully out) and adding logs refuels it above that. Fuel level controls
/// the fire light and flame particles, which <see cref="NightEnvironmentController"/>
/// reads to set overall darkness and <see cref="CampRevealController"/> uses (via
/// <see cref="OnFuelChanged"/>) to reveal the camp the first time the player feeds it.
/// </summary>
[DisallowMultipleComponent]
public class CampfireFuel : MonoBehaviour
{
    [Header("Fuel")]
    [SerializeField]
    [Tooltip("Maximum fuel the fire can hold.")]
    float m_MaxFuel = 1f;

    [SerializeField]
    [Tooltip("Fuel the fire starts with (0-1 of max). Low, so the scene opens on a faint " +
        "ember: only the fire and the log pile give off any light. Should be at or " +
        "above m_MinEmberFuelNormalized, otherwise it just snaps up to that floor.")]
    [Range(0f, 1f)]
    float m_StartingFuelNormalized = 0.08f;

    [SerializeField]
    [Tooltip("Fuel never decays below this (0-1 of max): the ember stays lit forever " +
        "instead of the fire going out. Throwing logs in still burns down toward this " +
        "floor over time, it just never crosses it.")]
    [Range(0f, 1f)]
    float m_MinEmberFuelNormalized = 0.08f;

    [SerializeField]
    [Tooltip("How much fuel is consumed per second above the ember floor. Tuned so a " +
        "single log (see Log's m_FuelValue) keeps the fire built up for roughly a " +
        "minute and a half before it settles back to an ember.")]
    float m_BurnRatePerSecond = 0.0033f;

    [Header("Fire Light")]
    [SerializeField] Light m_FireLight;
    [SerializeField] float m_MinLightIntensity = 0.3f;
    [SerializeField] float m_MaxLightIntensity = 6f;
    [SerializeField] float m_MinLightRange = 5f;
    [SerializeField] float m_MaxLightRange = 16f;
    [SerializeField] Color m_EmberColor = new Color(0.9f, 0.25f, 0.05f);
    [SerializeField] Color m_FlameColor = new Color(1f, 0.7f, 0.25f);

    [Header("Flame Particles")]
    [SerializeField] ParticleSystem m_FlameParticles;
    [SerializeField] float m_MaxEmissionRate = 40f;
    [SerializeField] float m_MaxFlameSize = 1f;

    [Header("Events")]
    [SerializeField] UnityEvent m_OnFuelChanged = new UnityEvent();
    [SerializeField] UnityEvent m_OnIgnited = new UnityEvent();
    [SerializeField] UnityEvent m_OnExtinguished = new UnityEvent();

    float m_CurrentFuel;
    bool m_WasBurning;

    public float MaxFuel => m_MaxFuel;
    public float CurrentFuel => m_CurrentFuel;
    public float FuelNormalized => m_MaxFuel <= 0f ? 0f : Mathf.Clamp01(m_CurrentFuel / m_MaxFuel);
    public bool IsBurning => m_CurrentFuel > 0f;

    public UnityEvent OnFuelChanged => m_OnFuelChanged;
    public UnityEvent OnIgnited => m_OnIgnited;
    public UnityEvent OnExtinguished => m_OnExtinguished;

    float EmberFloor => Mathf.Clamp01(m_MinEmberFuelNormalized) * m_MaxFuel;

    void Awake()
    {
        m_CurrentFuel = Mathf.Max(EmberFloor, Mathf.Clamp01(m_StartingFuelNormalized) * m_MaxFuel);
        m_WasBurning = IsBurning;
        ApplyVisuals();
    }

    void Update()
    {
        float floor = EmberFloor;
        if (m_CurrentFuel <= floor)
            return;

        float next = Mathf.Max(floor, m_CurrentFuel - m_BurnRatePerSecond * Time.deltaTime);
        AddFuel(next - m_CurrentFuel, false);
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

    public void SetFuel(float amount)
    {
        m_CurrentFuel = Mathf.Clamp(amount, 0f, m_MaxFuel);
        m_WasBurning = IsBurning;
        ApplyVisuals();
    }

    void ApplyVisuals()
    {
        float n = FuelNormalized;

        if (m_FireLight != null)
        {
            m_FireLight.enabled = n > 0.001f;
            m_FireLight.intensity = Mathf.Lerp(m_MinLightIntensity, m_MaxLightIntensity, n);
            m_FireLight.range = Mathf.Lerp(m_MinLightRange, m_MaxLightRange, n);
            m_FireLight.color = Color.Lerp(m_EmberColor, m_FlameColor, n);
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
    }
}

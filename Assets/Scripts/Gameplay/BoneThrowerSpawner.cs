using UnityEngine;

/// <summary>
/// Spawns Lanzahuesos on the same fixed ring as the Caminante (centred on the
/// campfire, r = 10 m - see enemigos.md: "radios fijos, nada aleatorio"), but
/// rarer and capped lower, since it exists to justify the resortera rather
/// than to pressure the fire directly. Stays dormant until the player has fed
/// the campfire past <see cref="m_ActivationFuelNormalized"/>, same as
/// SkeletonSpawner.
/// </summary>
[DisallowMultipleComponent]
public class BoneThrowerSpawner : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] GameObject m_BoneThrowerPrefab;

    [Header("Target")]
    [SerializeField] Transform m_Target;

    [Header("Activation")]
    [SerializeField] CampfireFuel m_Campfire;
    [Tooltip("Fuel level (0-1) the player must build the fire up to before the first Lanzahuesos appears.")]
    [SerializeField] float m_ActivationFuelNormalized = 0.5f;

    [Header("Spawning")]
    [SerializeField] float m_StartDelay = 30f;
    [SerializeField] float m_SpawnInterval = 30f;
    [SerializeField] float m_SpawnDistance = 10f;
    [SerializeField] float m_SpawnSpreadDegrees = 40f;
    [SerializeField] int m_MaxAlive = 1;
    [SerializeField] int m_TotalToSpawn = 5;

    bool m_Activated;
    float m_NextSpawnTime;
    int m_Spawned;
    int m_Alive;

    public bool Activated => m_Activated;
    public int AliveCount => m_Alive;
    public int SpawnedCount => m_Spawned;

    void Start()
    {
        if (m_Target == null && Camera.main != null)
            m_Target = Camera.main.transform;
    }

    void Update()
    {
        if (m_Target == null || m_BoneThrowerPrefab == null)
            return;

        if (!m_Activated)
        {
            if (m_Campfire == null || m_Campfire.FuelNormalized < m_ActivationFuelNormalized)
                return;

            m_Activated = true;
            m_NextSpawnTime = Time.time + m_StartDelay;
            return;
        }

        if (m_Spawned >= m_TotalToSpawn || m_Alive >= m_MaxAlive)
            return;
        if (Time.time < m_NextSpawnTime)
            return;

        Spawn();
        m_NextSpawnTime = Time.time + m_SpawnInterval;
    }

    void Spawn()
    {
        // Same fixed ring as the Caminante, centred on the campfire rather than
        // the player's head.
        Vector3 origin = m_Campfire != null ? m_Campfire.transform.position : m_Target.position;

        Vector3 forward = Vector3.ProjectOnPlane(m_Target.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;

        bool front = Random.value < 0.5f;
        Vector3 direction = front ? forward : -forward;
        direction = Quaternion.Euler(0f, Random.Range(-m_SpawnSpreadDegrees, m_SpawnSpreadDegrees), 0f) * direction;

        Vector3 position = origin + direction * m_SpawnDistance;
        position.y = 0f;

        var boneThrower = Instantiate(m_BoneThrowerPrefab, position, Quaternion.LookRotation(-direction, Vector3.up));
        var component = boneThrower.GetComponent<BoneThrower>();
        if (component != null)
        {
            component.SetCampfire(m_Campfire);
            component.OnDied.AddListener(OnBoneThrowerDied);
        }

        m_Spawned++;
        m_Alive++;
    }

    void OnBoneThrowerDied()
    {
        m_Alive = Mathf.Max(0, m_Alive - 1);
    }

    /// <summary>
    /// Force-spawns one Lanzahuesos immediately at the fixed ring, bypassing the
    /// activation gate and cooldown. Used for the opening beat and by DebugKeys.
    /// </summary>
    public void SpawnNow()
    {
        if (m_Target == null || m_BoneThrowerPrefab == null)
            return;

        Spawn();
    }

    /// <summary>
    /// Sends every live Lanzahuesos away when the campfire dies (see
    /// GameManager's outage).
    /// </summary>
    public void RetreatAll()
    {
        foreach (var boneThrower in Object.FindObjectsByType<BoneThrower>(FindObjectsInactive.Exclude))
        {
            if (boneThrower != null)
                boneThrower.Retreat();
        }

        m_Alive = 0;
    }
}

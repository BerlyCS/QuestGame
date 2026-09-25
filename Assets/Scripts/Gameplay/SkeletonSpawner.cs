using UnityEngine;

/// <summary>
/// Spawns skeletons on a fixed ring centred on the campfire (not the player's
/// head), biased toward appearing in front of or behind wherever the player is
/// looking, in waves that get slightly faster over time. There is no fixed
/// budget: the cap on how many are alive at once grows one per
/// <see cref="NightDifficulty.Stage"/>, and a killed Caminante is replaced
/// after <see cref="m_RefillDelay"/>, so clearing the field never leaves the
/// night empty - it just opens slots for the stage's reinforcements. Stays
/// dormant until the player has fed the campfire past
/// <see cref="m_ActivationFuelNormalized"/>, so the opening moments are quiet
/// and the waves start as a consequence of building up the fire rather than
/// on a fixed timer from scene load.
/// </summary>
[DisallowMultipleComponent]
public class SkeletonSpawner : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] GameObject m_SkeletonPrefab;

    [Header("Target")]
    [SerializeField] Transform m_Target;

    [Header("Activation")]
    [SerializeField] CampfireFuel m_Campfire;
    [Tooltip("Fuel level (0-1) the player must build the fire up to before the first wave starts.")]
    [SerializeField] float m_ActivationFuelNormalized = 0.3f;

    [Header("Spawning")]
    [SerializeField] float m_StartDelay = 30f;
    [SerializeField] float m_SpawnInterval = 20f;
    [SerializeField] float m_SpawnDistance = 10f;
    [SerializeField] float m_SpawnSpreadDegrees = 40f;
    [Tooltip("How many Caminantes can be alive at once at the start of the night.")]
    [SerializeField] int m_MaxAlive = 3;
    [Tooltip("Extra Caminantes allowed alive per difficulty stage (NightDifficulty.Stage).")]
    [SerializeField] int m_MaxAlivePerStage = 1;
    [Tooltip("Hard ceiling on living Caminantes, however far the night has run.")]
    [SerializeField] int m_MaxAliveCap = 8;
    [Tooltip("Delay before a killed Caminante is replaced while below the per-stage cap.")]
    [SerializeField] float m_RefillDelay = 3f;

    [Header("Difficulty")]
    [SerializeField] float m_MinInterval = 9f;
    [SerializeField] float m_IntervalRampPerSpawn = 0.6f;

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
        if (m_Target == null || m_SkeletonPrefab == null)
            return;

        if (!m_Activated)
        {
            if (m_Campfire == null || m_Campfire.FuelNormalized < m_ActivationFuelNormalized)
                return;

            m_Activated = true;
            m_NextSpawnTime = Time.time + m_StartDelay;
            return;
        }

        if (m_Alive >= MaxAliveForStage)
            return;
        if (Time.time < m_NextSpawnTime)
            return;

        Spawn();

        float interval = Mathf.Max(m_MinInterval, m_SpawnInterval - m_Spawned * m_IntervalRampPerSpawn);
        m_NextSpawnTime = Time.time + interval;
    }

    /// <summary>How many Caminantes may be alive right now: the base cap plus one
    /// per difficulty stage, never past <see cref="m_MaxAliveCap"/>.</summary>
    int MaxAliveForStage => Mathf.Clamp(m_MaxAlive + NightDifficulty.Stage * m_MaxAlivePerStage, 0, m_MaxAliveCap);

    void Spawn()
    {
        // The ring is centred on the campfire (the thing enemies actually walk to),
        // not on the player's head: the player barely moves, but the ring must stay
        // fixed regardless. Direction (front/back bias) still follows where the
        // player is looking, so enemies still read as "coming from behind".
        Vector3 origin = m_Campfire != null ? m_Campfire.transform.position : m_Target.position;

        Vector3 forward = Vector3.ProjectOnPlane(m_Target.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;

        bool front = Random.value < 0.5f;
        Vector3 direction = front ? forward : -forward;
        direction = Quaternion.Euler(0f, Random.Range(-m_SpawnSpreadDegrees, m_SpawnSpreadDegrees), 0f) * direction;

        Vector3 position = origin + direction * m_SpawnDistance;
        position.y = 0f;

        var skeleton = Instantiate(m_SkeletonPrefab, position, Quaternion.LookRotation(-direction, Vector3.up));
        var component = skeleton.GetComponent<Skeleton>();
        if (component != null)
        {
            component.SetCampfire(m_Campfire);
            component.OnDied.AddListener(OnSkeletonDied);
        }

        m_Spawned++;
        m_Alive++;
    }

    void OnSkeletonDied()
    {
        m_Alive = Mathf.Max(0, m_Alive - 1);

        // A kill opens a slot. Line up a replacement soon rather than waiting
        // out the whole wave interval, so the field refills during the stage
        // instead of going quiet once the player clears it.
        m_NextSpawnTime = Mathf.Min(m_NextSpawnTime, Time.time + m_RefillDelay);
    }

    /// <summary>
    /// Force-spawns one Caminante immediately at the fixed ring, bypassing the
    /// activation gate and wave cooldown. Used for the opening beat and by
    /// DebugKeys.
    /// </summary>
    public void SpawnNow()
    {
        if (m_Target == null || m_SkeletonPrefab == null)
            return;

        Spawn();
    }

    /// <summary>
    /// Sends every live Caminante away when the campfire dies (see GameManager's
    /// outage). Red Cazadores are left alone - they are the ones closing in.
    /// </summary>
    public void RetreatAll()
    {
        foreach (var skeleton in Object.FindObjectsByType<Skeleton>(FindObjectsInactive.Exclude))
        {
            if (skeleton != null && !skeleton.IsHunter)
                skeleton.Retreat();
        }

        m_Alive = 0;
    }
}

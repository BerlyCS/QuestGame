using UnityEngine;

/// <summary>
/// Spawns skeletons in front of and behind the player, in waves that get
/// slightly faster over time. Keeps a cap on how many are alive at once.
/// Stays dormant until the player has fed the campfire past
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
    [SerializeField] float m_ActivationFuelNormalized = 0.4f;

    [Header("Spawning")]
    [SerializeField] float m_StartDelay = 8f;
    [SerializeField] float m_SpawnInterval = 4.5f;
    [SerializeField] float m_SpawnDistance = 10f;
    [SerializeField] float m_SpawnSpreadDegrees = 40f;
    [SerializeField] int m_MaxAlive = 6;
    [SerializeField] int m_TotalToSpawn = 30;

    [Header("Difficulty")]
    [SerializeField] float m_MinInterval = 1.5f;
    [SerializeField] float m_IntervalRampPerSpawn = 0.08f;

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

        if (m_Spawned >= m_TotalToSpawn || m_Alive >= m_MaxAlive)
            return;
        if (Time.time < m_NextSpawnTime)
            return;

        Spawn();

        float interval = Mathf.Max(m_MinInterval, m_SpawnInterval - m_Spawned * m_IntervalRampPerSpawn);
        m_NextSpawnTime = Time.time + interval;
    }

    void Spawn()
    {
        Vector3 forward = Vector3.ProjectOnPlane(m_Target.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;

        bool front = Random.value < 0.5f;
        Vector3 direction = front ? forward : -forward;
        direction = Quaternion.Euler(0f, Random.Range(-m_SpawnSpreadDegrees, m_SpawnSpreadDegrees), 0f) * direction;

        Vector3 position = m_Target.position + direction * m_SpawnDistance;
        position.y = 0f;

        var skeleton = Instantiate(m_SkeletonPrefab, position, Quaternion.LookRotation(-direction, Vector3.up));
        var component = skeleton.GetComponent<Skeleton>();
        if (component != null)
            component.OnDied.AddListener(OnSkeletonDied);

        m_Spawned++;
        m_Alive++;
    }

    void OnSkeletonDied()
    {
        m_Alive = Mathf.Max(0, m_Alive - 1);
    }
}

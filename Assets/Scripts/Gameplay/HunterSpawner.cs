using UnityEngine;

/// <summary>
/// Spawns the Cazadores: skeletons that walk to the player and attack them
/// (see Skeleton's hunter mode). They start after the first stretch of the
/// night, come mostly from behind the player's gaze, and never more than
/// <see cref="m_MaxAlive"/> at once. Spawns on a fixed ring around the player
/// (radius <see cref="m_SpawnDistance"/>), distinct from the Caminante ring
/// that is centred on the campfire.
/// </summary>
[DisallowMultipleComponent]
public class HunterSpawner : MonoBehaviour
{
    [SerializeField] GameObject m_HunterPrefab;
    [SerializeField] PlayerHealth m_Player;
    [SerializeField] GameManager m_GameManager;

    [Header("Timing")]
    [Tooltip("Fraction of the night (0-1) that must pass before the first Cazador appears.")]
    [SerializeField] float m_StartAtSurvival = 0.3f;
    [SerializeField] float m_SpawnInterval = 22f;
    [SerializeField] float m_MinInterval = 12f;
    [SerializeField] float m_IntervalRampPerSpawn = 0.8f;

    [Header("Placement")]
    [SerializeField] float m_SpawnDistance = 9f;
    [SerializeField] float m_SpreadDegrees = 50f;
    [Range(0f, 1f)]
    [SerializeField] float m_BehindChance = 0.7f;

    [Header("Limits")]
    [SerializeField] int m_MaxAlive = 1;
    [SerializeField] int m_MaxAliveLate = 2;
    [SerializeField] float m_LateAtSurvival = 0.6f;

    int m_Spawned;
    int m_Alive;
    float m_NextSpawnTime;

    void Update()
    {
        if (m_Player == null || m_Player.Head == null || m_HunterPrefab == null || m_GameManager == null || !m_Player.IsAlive)
            return;

        float survival = m_GameManager.SurvivalNormalized;
        if (survival < m_StartAtSurvival)
            return;

        int cap = survival >= m_LateAtSurvival ? m_MaxAliveLate : m_MaxAlive;
        if (m_Alive >= cap || Time.time < m_NextSpawnTime)
            return;

        Spawn();
        m_NextSpawnTime = Time.time + Mathf.Max(m_MinInterval, m_SpawnInterval - m_Spawned * m_IntervalRampPerSpawn);
    }

    void Spawn()
    {
        Vector3 head = m_Player.Head.position;
        Vector3 forward = Vector3.ProjectOnPlane(m_Player.Head.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;

        Vector3 direction = Random.value < m_BehindChance ? -forward : forward;
        direction = Quaternion.Euler(0f, Random.Range(-m_SpreadDegrees, m_SpreadDegrees), 0f) * direction;

        Vector3 position = new Vector3(head.x, 0f, head.z) + direction * m_SpawnDistance;
        var hunter = Instantiate(m_HunterPrefab, position, Quaternion.LookRotation(-direction, Vector3.up));

        var skeleton = hunter.GetComponent<Skeleton>();
        skeleton.SetPlayer(m_Player);
        skeleton.OnDied.AddListener(() => m_Alive = Mathf.Max(0, m_Alive - 1));

        m_Spawned++;
        m_Alive++;
    }

    /// <summary>Debug-only: force-spawns one Cazador now (see DebugKeys).</summary>
    public void DebugSpawnNow()
    {
        if (m_Player != null && m_Player.Head != null && m_HunterPrefab != null)
            Spawn();
    }
}

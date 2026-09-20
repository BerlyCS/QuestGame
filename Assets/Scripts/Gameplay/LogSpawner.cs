using UnityEngine;

/// <summary>
/// Keeps the log pile stocked so firewood never runs out. Watches every log
/// placed under <see cref="m_PileOrigin"/> (the ones the scene builder places
/// at start, plus any this spawner creates) and drops in a replacement, after
/// a short delay, each time one is consumed by the campfire.
/// </summary>
[DisallowMultipleComponent]
public class LogSpawner : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] GameObject m_LogPrefab;

    [Header("Pile")]
    [SerializeField] Transform m_PileOrigin;
    [SerializeField] float m_PileRadius = 0.2f;
    [SerializeField] float m_DropHeight = 0.4f;

    [Header("Stock")]
    [Tooltip("How many logs the pile should always settle back to.")]
    [SerializeField] int m_MaxStock = 6;

    [Tooltip("Delay before a used log is replaced, so restocking feels gradual rather than instant.")]
    [SerializeField] float m_RestockDelay = 3f;

    int m_StockCount;
    int m_PendingRestocks;
    float m_NextSpawnTime;

    void Start()
    {
        if (m_PileOrigin == null)
            m_PileOrigin = transform;

        foreach (var log in m_PileOrigin.GetComponentsInChildren<Log>(true))
            Track(log);
    }

    void Update()
    {
        if (m_LogPrefab == null || m_PendingRestocks <= 0)
            return;
        if (Time.time < m_NextSpawnTime)
            return;

        Spawn();
        m_PendingRestocks--;
        m_NextSpawnTime = Time.time + m_RestockDelay;
    }

    void Track(Log log)
    {
        m_StockCount++;
        log.OnConsumed.AddListener(HandleLogConsumed);
    }

    void HandleLogConsumed()
    {
        m_StockCount = Mathf.Max(0, m_StockCount - 1);

        if (m_StockCount + m_PendingRestocks >= m_MaxStock)
            return;

        m_PendingRestocks++;
        if (m_PendingRestocks == 1)
            m_NextSpawnTime = Time.time + m_RestockDelay;
    }

    void Spawn()
    {
        Vector2 offset = Random.insideUnitCircle * m_PileRadius;
        Vector3 position = m_PileOrigin.position + new Vector3(offset.x, m_DropHeight, offset.y);
        // The log model is authored lying down, so only a yaw is applied here (the old
        // primitive cylinder needed a 90° tip-up, which would stand the model on end).
        Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        var instance = Instantiate(m_LogPrefab, position, rotation, m_PileOrigin);
        Track(instance.GetComponent<Log>());
    }
}

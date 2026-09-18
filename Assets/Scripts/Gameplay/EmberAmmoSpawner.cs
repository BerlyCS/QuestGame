using UnityEngine;

/// <summary>
/// Keeps the weapon rack's ember ammo stocked so the resortera never runs
/// out. Watches every ember placed under m_PileOrigin (the ones the scene
/// builder places at start, plus any this spawner creates) and drops in a
/// replacement, after a short delay, each time one is consumed (fired and it
/// hits something, or missed and lands). Also wires each ember's
/// SlingEmberHandle with the hand/band references it needs, so grabbing any
/// ball off the rack works immediately - no separate setup step.
/// </summary>
[DisallowMultipleComponent]
public class EmberAmmoSpawner : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] GameObject m_EmberPrefab;

    [Header("Pile")]
    [SerializeField] Transform m_PileOrigin;
    [SerializeField] float m_PileRadius = 0.08f;
    [SerializeField] float m_DropHeight = 0.05f;

    [Header("Hands and band (see SlingEmberHandle)")]
    [SerializeField] Transform m_LeftHand;
    [SerializeField] Transform m_RightHand;
    [SerializeField] LineRenderer m_Band;

    [Header("Stock")]
    [Tooltip("How many embers the rack should always settle back to.")]
    [SerializeField] int m_MaxStock = 3;

    [Tooltip("Delay before a used ember is replaced, so restocking feels gradual rather than instant.")]
    [SerializeField] float m_RestockDelay = 3f;

    int m_StockCount;
    int m_PendingRestocks;
    float m_NextSpawnTime;

    void Start()
    {
        if (m_PileOrigin == null)
            m_PileOrigin = transform;

        foreach (var ember in m_PileOrigin.GetComponentsInChildren<EmberProjectile>(true))
            Track(ember);
    }

    void Update()
    {
        if (m_EmberPrefab == null || m_PendingRestocks <= 0)
            return;
        if (Time.time < m_NextSpawnTime)
            return;

        Spawn();
        m_PendingRestocks--;
        m_NextSpawnTime = Time.time + m_RestockDelay;
    }

    void Track(EmberProjectile ember)
    {
        m_StockCount++;
        ember.OnConsumed.AddListener(HandleConsumed);

        var handle = ember.GetComponent<SlingEmberHandle>();
        if (handle != null)
            handle.Initialize(m_LeftHand, m_RightHand, m_Band);
    }

    void HandleConsumed()
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

        var instance = Instantiate(m_EmberPrefab, position, Quaternion.identity, m_PileOrigin);
        Track(instance.GetComponent<EmberProjectile>());
    }
}

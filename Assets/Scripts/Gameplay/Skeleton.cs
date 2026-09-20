using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The Caminante (and, with <see cref="m_HuntsPlayer"/>, the Cazador). The
/// Caminante walks in a straight line to the campfire and never the
/// player - it has no reference to the player at all, so it can brush past
/// without reacting, which is the moment that teaches the whole game without
/// a word (see enemigos.md). Kneels and attacks the fire once close, but
/// backs off while the fire is strong: the light repels it. Simple
/// primitive-based presentation with a procedural walk bob so it reads as
/// "alive". The Cazador is the exception to "enemies ignore the player": a
/// faster, red-eyed skeleton that walks up to the player's head and claws at
/// them, taking life through PlayerHealth. The two are told apart on sight.
/// </summary>
[DisallowMultipleComponent]
public class Skeleton : MonoBehaviour
{
    [Header("Stats")]
    [SerializeField] int m_MaxHits = 2;
    [SerializeField] float m_MoveSpeed = 0.8f;
    [SerializeField] float m_TurnSpeed = 540f;

    [Header("Attack")]
    [SerializeField] float m_KneelDistance = 1.6f;
    [SerializeField] float m_AttackInterval = 1.4f;
    [SerializeField] float m_AttackFuelDrain = 3.5f;

    [Header("Hunter (attacks the player instead of the fire)")]
    [SerializeField] bool m_HuntsPlayer;
    [SerializeField] float m_HunterMoveSpeed = 0.4f;
    [SerializeField] float m_PlayerAttackRange = 1.0f;
    [SerializeField] float m_PlayerAttackInterval = 1.2f;
    [SerializeField] float m_PlayerDamage = 12f;

    [Header("Repelled by light")]
    [SerializeField] float m_RepelFuelThreshold = 0.75f;
    [SerializeField] float m_RepelDistance = 4.5f;

    [Header("References")]
    [SerializeField] CampfireFuel m_Campfire;
    [SerializeField] Transform m_Body;
    [SerializeField] Transform m_LeftArm;
    [SerializeField] Transform m_RightArm;
    [SerializeField] Renderer[] m_Renderers;

    [Header("Feel")]
    [SerializeField] float m_BobAmplitude = 0.06f;
    [SerializeField] float m_BobFrequency = 6f;
    [SerializeField] float m_ArmSwing = 35f;
    [SerializeField] float m_KneelDropAmount = 0.3f;
    [SerializeField] Color m_HitFlashColor = new Color(1f, 0.25f, 0.2f);

    [Header("Events")]
    [SerializeField] UnityEvent m_OnDied = new UnityEvent();

    int m_Hits;
    float m_BobPhase;
    PlayerHealth m_Player;
    float m_NextAttackTime;
    float m_HitFlashUntil;
    Vector3 m_BodyBasePosition;
    Color[] m_BaseColors;

    public UnityEvent OnDied => m_OnDied;
    public bool IsAlive => m_Hits > 0;

    /// <summary>Wired by SkeletonSpawner at spawn time; the only thing this enemy ever targets.</summary>
    public void SetCampfire(CampfireFuel campfire) => m_Campfire = campfire;

    /// <summary>Wired by HunterSpawner: makes this skeleton a Cazador that goes for the player.</summary>
    public void SetPlayer(PlayerHealth player)
    {
        m_Player = player;
        m_HuntsPlayer = true;
        m_MoveSpeed = m_HunterMoveSpeed;
    }

    void Awake()
    {
        m_Hits = m_MaxHits;

        if (m_Body != null)
            m_BodyBasePosition = m_Body.localPosition;
        CacheColors();
    }

    void Start()
    {
        if (m_Campfire == null)
            m_Campfire = Object.FindAnyObjectByType<CampfireFuel>();
    }

    void Update()
    {
        if (m_HuntsPlayer)
        {
            UpdateHunter();
            return;
        }

        if (!IsAlive || m_Campfire == null)
            return;

        Vector3 campfirePosition = m_Campfire.transform.position;
        Vector3 toCampfire = campfirePosition - transform.position;
        toCampfire.y = 0f;
        float distance = toCampfire.magnitude;

        bool repelled = m_Campfire.Fuel01 > m_RepelFuelThreshold && distance < m_RepelDistance;

        if (repelled)
        {
            MoveToward(-toCampfire);
            AnimateWalk();
        }
        else if (distance > m_KneelDistance)
        {
            MoveToward(toCampfire);
            AnimateWalk();
        }
        else
        {
            AnimateKneel();
            if (Time.time >= m_NextAttackTime)
            {
                m_NextAttackTime = Time.time + m_AttackInterval;
                // The fire's own fuel-driven visuals (flame size, light, crackle pitch)
                // already shrink and hiss as fuel drops - no separate effect needed here.
                m_Campfire.AddFuel(-m_AttackFuelDrain);
            }
        }

        UpdateHitFlash();
    }

    void UpdateHunter()
    {
        if (!IsAlive || m_Player == null || m_Player.Head == null || !m_Player.IsAlive)
            return;

        Vector3 toPlayer = m_Player.Head.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.magnitude > m_PlayerAttackRange)
        {
            MoveToward(toPlayer);
            AnimateWalk();
        }
        else
        {
            // In range: stand still and face them. Never step closer (it walked through the player).
            FaceToward(toPlayer);
            AnimateClaw();
            if (Time.time >= m_NextAttackTime)
            {
                m_NextAttackTime = Time.time + m_PlayerAttackInterval;
                m_Player.TakeDamage(m_PlayerDamage);
            }
        }

        UpdateHitFlash();
    }

    /// <summary>Arms raised and raking forward: reads as an attack, not the fire-kneel.</summary>
    void AnimateClaw()
    {
        float swipe = Mathf.Sin(Time.time * 12f) * 25f;
        if (m_Body != null)
            m_Body.localPosition = m_BodyBasePosition;
        if (m_LeftArm != null)
            m_LeftArm.localRotation = Quaternion.Euler(-95f + swipe, 0f, 0f);
        if (m_RightArm != null)
            m_RightArm.localRotation = Quaternion.Euler(-95f - swipe, 0f, 0f);
    }

    void FaceToward(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion look = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, m_TurnSpeed * Time.deltaTime);
    }

    void MoveToward(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        direction.Normalize();
        Quaternion look = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, m_TurnSpeed * Time.deltaTime);
        transform.position += direction * (m_MoveSpeed * Time.deltaTime);
    }

    void AnimateWalk()
    {
        m_BobPhase += Time.deltaTime * m_BobFrequency;
        float sine = Mathf.Sin(m_BobPhase);
        float bob = Mathf.Abs(sine) * m_BobAmplitude;
        if (m_Body != null)
            m_Body.localPosition = m_BodyBasePosition + new Vector3(0f, bob, 0f);

        float swing = Mathf.Sin(m_BobPhase) * m_ArmSwing;
        if (m_LeftArm != null)
            m_LeftArm.localRotation = Quaternion.Euler(swing, 0f, 0f);
        if (m_RightArm != null)
            m_RightArm.localRotation = Quaternion.Euler(-swing, 0f, 0f);
    }

    /// <summary>Kneeling, arms reaching into the fire - visually distinct from walking.</summary>
    void AnimateKneel()
    {
        if (m_Body != null)
            m_Body.localPosition = m_BodyBasePosition + new Vector3(0f, -m_KneelDropAmount, 0f);

        if (m_LeftArm != null)
            m_LeftArm.localRotation = Quaternion.Euler(-70f, 0f, 0f);
        if (m_RightArm != null)
            m_RightArm.localRotation = Quaternion.Euler(-70f, 0f, 0f);
    }

    public void TakeHit(int amount)
    {
        if (!IsAlive)
            return;

        m_Hits -= amount;
        m_HitFlashUntil = Time.time + 0.12f;

        if (m_Hits <= 0)
            Die();
    }

    /// <summary>
    /// Removed from the world instantly by the banishing weapon (see
    /// <see cref="EnemyBanisher"/>), bypassing the hit-count path. Fires
    /// OnDied like a normal kill so spawner bookkeeping stays consistent.
    /// </summary>
    public void Banish()
    {
        if (!IsAlive)
            return;

        Die();
    }

    void Die()
    {
        m_Hits = 0;
        m_OnDied.Invoke();
        Destroy(gameObject);
    }

    /// <summary>
    /// Called by GameManager on victory: freezes AI immediately (Update stops
    /// running) and topples the skeleton into the ground over a second, then
    /// removes it. Distinct from Die() - this isn't a combat kill, so it
    /// doesn't fire OnDied (the spawner is already disabled by then anyway).
    /// </summary>
    public void Collapse()
    {
        if (!IsAlive)
            return;

        m_Hits = 0;
        enabled = false;
        StartCoroutine(CollapseRoutine());
    }

    IEnumerator CollapseRoutine()
    {
        const float duration = 1f;
        Quaternion startRotation = transform.rotation;
        Quaternion fallenRotation = startRotation * Quaternion.Euler(90f, 0f, 0f);
        Vector3 startPosition = transform.position;
        Vector3 sunkPosition = startPosition + Vector3.down * 0.6f;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float n = t / duration;
            transform.rotation = Quaternion.Slerp(startRotation, fallenRotation, n);
            transform.position = Vector3.Lerp(startPosition, sunkPosition, n);
            yield return null;
        }

        Destroy(gameObject);
    }

    void CacheColors()
    {
        if (m_Renderers == null || m_Renderers.Length == 0)
            m_Renderers = GetComponentsInChildren<Renderer>();

        m_BaseColors = new Color[m_Renderers.Length];
        for (int i = 0; i < m_Renderers.Length; i++)
            m_BaseColors[i] = m_Renderers[i].material.color;
    }

    void UpdateHitFlash()
    {
        bool flash = Time.time < m_HitFlashUntil;
        for (int i = 0; i < m_Renderers.Length; i++)
        {
            if (m_Renderers[i] == null)
                continue;
            m_Renderers[i].material.color = flash ? m_HitFlashColor : m_BaseColors[i];
        }
    }
}

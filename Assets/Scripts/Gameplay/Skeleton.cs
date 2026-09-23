using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The Caminante (and, with <see cref="m_HuntsPlayer"/>, the Cazador). The
/// Caminante walks in a straight line to the campfire and never the
/// player - it has no reference to the player at all, so it can brush past
/// without reacting, which is the moment that teaches the whole game without
/// a word (see enemigos.md). Kneels and attacks the fire once close, but
/// backs off while the fire is strong: the light repels it. The Cazador is
/// the exception to "enemies ignore the player": a faster, red-eyed skeleton
/// that walks up to the player's head and claws at them, taking life through
/// PlayerHealth. The two are told apart on sight.
///
/// Presentation is either the simple primitive bodies built by
/// GameSceneBuilder (procedural bob/swing, see <see cref="m_Body"/>) or, when
/// <see cref="m_Animator"/> is assigned, an animated skeleton model driven by
/// the EnemySkeleton controller. Both paths share the same AI; the procedural
/// posing is skipped when an Animator is present.
/// </summary>
[DisallowMultipleComponent]
public class Skeleton : MonoBehaviour, IArrowHittable
{
    [Header("Stats")]
    [SerializeField] int m_MaxHits = 2;
    [SerializeField] float m_MoveSpeed = 0.8f;
    [SerializeField] float m_TurnSpeed = 540f;

    [Header("Attack")]
    [SerializeField] float m_KneelDistance = 1.6f;
    [SerializeField] float m_AttackInterval = 2.5f;
    [SerializeField] float m_AttackFuelDrain = 1.5f;

    [Header("Hunter (attacks the player instead of the fire)")]
    [SerializeField] bool m_HuntsPlayer;
    [SerializeField] float m_HunterMoveSpeed = 0.4f;
    [SerializeField] float m_PlayerAttackRange = 1.0f;
    [SerializeField] float m_PlayerAttackInterval = 2f;
    [SerializeField] float m_PlayerDamage = 8f;

    [Header("Repelled by light")]
    [SerializeField] float m_RepelFuelThreshold = 0.75f;
    [SerializeField] float m_RepelDistance = 4.5f;
    [Tooltip("Extra distance the light has to lose its hold on a skeleton, so one " +
             "hovering at the edge of the firelight does not flip between walking " +
             "in and backing off every frame.")]
    [SerializeField] float m_RepelHysteresis = 1.2f;

    [Header("References")]
    [SerializeField] CampfireFuel m_Campfire;
    [SerializeField] Animator m_Animator;
    [SerializeField] Transform m_Body;
    [SerializeField] Transform m_LeftArm;
    [SerializeField] Transform m_RightArm;
    [SerializeField] Renderer[] m_Renderers;

    [Header("Animation")]
    [Tooltip("Walk speed at which the locomotion blend switches from Walking_A to Running_A. " +
             "The EnemySkeleton controller blends Idle_A at 0, Walking_A at 0.5 and Running_A at 1.")]
    [SerializeField] float m_RunSpeedThreshold = 1.3f;

    [Header("Obstacle avoidance")]
    [Tooltip("Curve around the camp clutter instead of walking into it and shuffling in place.")]
    [SerializeField] bool m_AvoidObstacles = true;
    [Range(0f, 1.5f)]
    [SerializeField] float m_AvoidSteer = 0.9f;

    [Header("Feel")]
    [SerializeField] float m_BobAmplitude = 0.06f;
    [SerializeField] float m_BobFrequency = 6f;
    [SerializeField] float m_ArmSwing = 35f;
    [SerializeField] float m_KneelDropAmount = 0.3f;
    [SerializeField] Color m_HitFlashColor = new Color(1f, 0.25f, 0.2f);

    [Header("Death")]
    [SerializeField] AudioClip m_DeathSfx;
    [SerializeField, Range(0f, 1f)] float m_DeathSfxVolume = 0.85f;
    [SerializeField] bool m_DeathParticles = true;
    [Tooltip("How long the death animation is given before the object is removed.")]
    [SerializeField] float m_DeathAnimDuration = 1.5f;

    [Header("Retreat (fire outage)")]
    [Tooltip("How long the skeleton walks away from the dead fire before it disappears.")]
    [SerializeField] float m_RetreatDuration = 2.5f;
    [SerializeField] float m_RetreatSpeed = 1.6f;

    [Header("Axe (banisher)")]
    [Tooltip("When true the axe is no longer an instant kill: each strike is a normal hit instead.")]
    [SerializeField] bool m_ResistsBanish;

    [Header("Events")]
    [SerializeField] UnityEvent m_OnDied = new UnityEvent();

    // Animator parameter names, shared with the EnemySkeleton controller.
    static readonly int k_SpeedHash = Animator.StringToHash("Speed");
    static readonly int k_AttackHash = Animator.StringToHash("Attack");
    static readonly int k_HitHash = Animator.StringToHash("Hit");
    static readonly int k_DeathHash = Animator.StringToHash("Death");

    int m_Hits;
    float m_BobPhase;
    PlayerHealth m_Player;
    float m_NextAttackTime;
    float m_HitFlashUntil;
    Vector3 m_BodyBasePosition;
    Color[] m_BaseColors;
    bool m_Retreating;
    bool m_Repelled;
    Vector3 m_RetreatDirection;
    float m_RetreatEndTime;
    EnemySteering.State m_Steering;

    public UnityEvent OnDied => m_OnDied;
    public bool IsAlive => m_Hits > 0;

    /// <summary>True for the red Cazadores (spawned by HunterSpawner), which the fire outage replaces the normal enemies with.</summary>
    public bool IsHunter => m_HuntsPlayer;

    /// <summary>True while the skeleton is walking off after the fire went out.</summary>
    public bool IsRetreating => m_Retreating;

    /// <summary>Wired by SkeletonSpawner at spawn time; the only thing this enemy ever targets.</summary>
    public void SetCampfire(CampfireFuel campfire) => m_Campfire = campfire;

    /// <summary>Wired by HunterSpawner: makes this skeleton a Cazador that goes for the player.</summary>
    public void SetPlayer(PlayerHealth player)
    {
        m_Player = player;
        m_HuntsPlayer = true;
        m_MoveSpeed = m_HunterMoveSpeed;
    }

    /// <summary>Overrides the hit count so the fire-outage swarm can be tougher.</summary>
    public void SetMaxHits(int hits)
    {
        m_MaxHits = Mathf.Max(1, hits);
        m_Hits = m_MaxHits;
    }

    /// <summary>Overrides the walk speed so the fire-outage swarm can be faster.</summary>
    public void SetMoveSpeed(float speed) => m_MoveSpeed = speed;

    /// <summary>Makes the axe deal a normal hit instead of an instant kill (the fire-outage swarm).</summary>
    public void SetResistsBanish(bool value) => m_ResistsBanish = value;

    /// <summary>
    /// Kills this skeleton as a real combat kill (death SFX + particles + OnDied).
    /// Used when the campfire is relit: the swarm dies with the lego-breaking sound.
    /// </summary>
    public void Kill() => Die();

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
        if (m_Retreating)
        {
            UpdateRetreat();
            return;
        }

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

        bool repelled = UpdateRepelled(distance);

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
                if (m_Animator != null)
                    m_Animator.SetTrigger(k_AttackHash);
            }
        }

        UpdateHitFlash();
    }

    /// <summary>
    /// Whether the fire's light is currently holding this skeleton back. It
    /// only lets go once the skeleton is further out than the distance that
    /// caught it, so one hovering at the edge of the firelight walks steadily
    /// instead of flipping direction on the spot every frame.
    /// </summary>
    bool UpdateRepelled(float distance)
    {
        if (m_Campfire.Fuel01 <= m_RepelFuelThreshold)
            m_Repelled = false;
        else if (distance <= m_RepelDistance)
            m_Repelled = true;
        else if (distance >= m_RepelDistance + m_RepelHysteresis)
            m_Repelled = false;

        return m_Repelled;
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
                if (m_Animator != null)
                    m_Animator.SetTrigger(k_AttackHash);
            }
        }

        UpdateHitFlash();
    }

    /// <summary>Arms raised and raking forward: reads as an attack, not the fire-kneel.</summary>
    void AnimateClaw()
    {
        if (m_Animator != null)
        {
            m_Animator.SetFloat(k_SpeedHash, 0f);
            return;
        }

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

        direction = ResolveWalkDirection(direction);
        Quaternion look = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, m_TurnSpeed * Time.deltaTime);
        transform.position += direction * (m_MoveSpeed * Time.deltaTime);
    }

    /// <summary>
    /// The straight line to the target, unless something solid is in the way
    /// (see <see cref="EnemySteering"/>): steers around camp props and slides
    /// free sideways if it still manages to wedge itself.
    /// </summary>
    Vector3 ResolveWalkDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (!m_AvoidObstacles)
            return direction.normalized;

        return EnemySteering.Resolve(transform, direction, m_MoveSpeed, m_AvoidSteer, ref m_Steering);
    }

    void AnimateWalk()
    {
        if (m_Animator != null)
        {
            // The controller blends Idle_A at 0, Walking_A at 0.5 and Running_A
            // at 1, so snap to a whole clip instead of sitting between two
            // cycles (blending two step timings together slid the feet).
            m_Animator.SetFloat(k_SpeedHash, m_MoveSpeed >= m_RunSpeedThreshold ? 1f : 0.5f);
            return;
        }

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
        if (m_Animator != null)
        {
            m_Animator.SetFloat(k_SpeedHash, 0f);
            return;
        }

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
        if (m_Animator != null)
            m_Animator.SetTrigger(k_HitHash);

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

        // The fire-outage swarm shrugs off the axe: each touch is one normal
        // hit rather than an instant kill, so it takes two clean swings.
        if (m_ResistsBanish)
        {
            TakeHit(1);
            return;
        }

        Die();
    }

    /// <summary>
    /// Sent away when the campfire dies (see GameManager's outage) or when the
    /// Coronado's entrance clears the field (see BossIntro): the skeleton stops
    /// counting as hittable, turns away and walks off, then disappears. OnDied
    /// still fires on the way out so the spawners keep their alive counts
    /// straight. With <paramref name="direction"/> given (BossIntro sends every
    /// skeleton the same way, north into the fog) that direction is used as-is;
    /// left null, it falls back to away-from-the-threat like before.
    /// </summary>
    public void Retreat(Vector3? direction = null)
    {
        if (m_Retreating || !IsAlive)
            return;

        m_Retreating = true;
        m_Hits = 0;
        m_RetreatEndTime = Time.time + m_RetreatDuration;

        if (direction.HasValue)
        {
            Vector3 fixedDirection = direction.Value;
            fixedDirection.y = 0f;
            m_RetreatDirection = fixedDirection.sqrMagnitude > 0.0001f ? fixedDirection.normalized : -transform.forward;
            return;
        }

        Vector3 source = m_HuntsPlayer && m_Player != null && m_Player.Head != null
            ? m_Player.Head.position
            : (m_Campfire != null ? m_Campfire.transform.position : transform.position - transform.forward);

        Vector3 away = transform.position - source;
        away.y = 0f;
        m_RetreatDirection = away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;
    }

    void UpdateRetreat()
    {
        transform.position += m_RetreatDirection * (m_RetreatSpeed * Time.deltaTime);
        FaceToward(m_RetreatDirection);
        AnimateWalk();

        if (Time.time >= m_RetreatEndTime)
        {
            m_OnDied.Invoke();
            Destroy(gameObject);
        }
    }

    /// <summary>A landed arrow counts as one hit (see <see cref="IArrowHittable"/>).</summary>
    public void Hit(Arrow arrow)
    {
        TakeHit(1);
    }

    void Die()
    {
        m_Hits = 0;

        // With an Animator the model plays the death out, so the particle burst
        // is skipped and the object is left in place long enough to be seen.
        bool animated = m_Animator != null;
        SkeletonDeathFx.Play(transform.position + Vector3.up * 0.9f, m_DeathSfx, m_DeathSfxVolume,
            m_DeathParticles && !animated);
        m_OnDied.Invoke();

        if (animated)
        {
            m_Animator.SetTrigger(k_DeathHash);
            Destroy(gameObject, m_DeathAnimDuration);
        }
        else
        {
            Destroy(gameObject);
        }
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
        if (m_Animator != null)
            return;

        if (m_Renderers == null || m_Renderers.Length == 0)
            m_Renderers = GetComponentsInChildren<Renderer>();

        m_BaseColors = new Color[m_Renderers.Length];
        for (int i = 0; i < m_Renderers.Length; i++)
            m_BaseColors[i] = m_Renderers[i].material.color;
    }

    void UpdateHitFlash()
    {
        // The animated model shows the hit with the Hit_A clip instead.
        if (m_Animator != null)
            return;

        bool flash = Time.time < m_HitFlashUntil;
        for (int i = 0; i < m_Renderers.Length; i++)
        {
            if (m_Renderers[i] == null)
                continue;
            m_Renderers[i].material.color = flash ? m_HitFlashColor : m_BaseColors[i];
        }
    }
}

using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The Lanzahuesos. Walks straight toward the campfire exactly like the
/// Caminante, but stops well short - at m_StopDistance (8 m) - and never
/// gets any closer: deliberately out of easy reach (see
/// enemigos.md). From there it lobs a bone at the fire on an interval; each
/// impact drains fuel. Same rule as Skeleton: no reference to the player at
/// all, only to the campfire. Glows brighter than a Caminante so it reads at
/// range in the dark.
///
/// Presentation is either the simple primitive body built by hand (procedural
/// bob/arm swing) or, when <see cref="m_Animator"/> is assigned, one of the
/// animated skeleton models driven by the EnemySkeleton controller. The
/// procedural posing is skipped when an Animator is present.
/// </summary>
[DisallowMultipleComponent]
public class BoneThrower : MonoBehaviour, IArrowHittable
{
    [Header("Stats")]
    [SerializeField] int m_MaxHits = 1;
    [SerializeField] float m_MoveSpeed = 0.8f;
    [SerializeField] float m_TurnSpeed = 540f;
    [SerializeField] float m_StopDistance = 8f;

    [Header("Attack")]
    [SerializeField] float m_ThrowInterval = 7f;
    [SerializeField] float m_ThrowFuelDamage = 1.5f;
    [SerializeField] float m_ThrowArcHeight = 2.5f;
    [SerializeField] float m_ThrowDuration = 1.4f;
    [SerializeField] GameObject m_BonePrefab;
    [SerializeField] Transform m_ThrowOrigin;

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
    [SerializeField] Color m_HitFlashColor = new Color(1f, 0.25f, 0.2f);

    [Header("Death")]
    [SerializeField] AudioClip m_DeathSfx;
    [SerializeField, Range(0f, 1f)] float m_DeathSfxVolume = 0.85f;
    [SerializeField] bool m_DeathParticles = true;
    [Tooltip("How long the death animation is given before the object is removed.")]
    [SerializeField] float m_DeathAnimDuration = 1.5f;

    [Header("Retreat (fire outage)")]
    [Tooltip("How long the Lanzahuesos walks away from the dead fire before it disappears.")]
    [SerializeField] float m_RetreatDuration = 2.5f;
    [SerializeField] float m_RetreatSpeed = 1.6f;

    [Header("Events")]
    [SerializeField] UnityEvent m_OnDied = new UnityEvent();

    // Animator parameter names, shared with the EnemySkeleton controller.
    static readonly int k_SpeedHash = Animator.StringToHash("Speed");
    static readonly int k_AttackHash = Animator.StringToHash("Attack");
    static readonly int k_HitHash = Animator.StringToHash("Hit");
    static readonly int k_DeathHash = Animator.StringToHash("Death");

    int m_Hits;
    float m_BobPhase;
    float m_NextThrowTime;
    float m_ThrowWindupUntil;
    float m_HitFlashUntil;
    Vector3 m_BodyBasePosition;
    Color[] m_BaseColors;
    bool m_Retreating;
    Vector3 m_RetreatDirection;
    float m_RetreatEndTime;
    EnemySteering.State m_Steering;

    public UnityEvent OnDied => m_OnDied;
    public bool IsAlive => m_Hits > 0;

    /// <summary>True while the Lanzahuesos is walking off after the fire went out.</summary>
    public bool IsRetreating => m_Retreating;

    /// <summary>Wired by BoneThrowerSpawner at spawn time; the only thing this enemy ever targets.</summary>
    public void SetCampfire(CampfireFuel campfire) => m_Campfire = campfire;

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

        if (!IsAlive || m_Campfire == null)
            return;

        Vector3 campfirePosition = m_Campfire.transform.position;
        Vector3 toCampfire = campfirePosition - transform.position;
        toCampfire.y = 0f;
        float distance = toCampfire.magnitude;

        if (distance > m_StopDistance)
        {
            MoveToward(toCampfire);
            AnimateWalk();
        }
        else
        {
            FaceToward(toCampfire);
            AnimateThrowIdle();

            if (Time.time >= m_NextThrowTime)
            {
                m_NextThrowTime = Time.time + m_ThrowInterval;
                m_ThrowWindupUntil = Time.time + 0.3f;
                if (m_Animator != null)
                    m_Animator.SetTrigger(k_AttackHash);
                ThrowBone(campfirePosition);
            }
        }

        UpdateHitFlash();
    }

    void MoveToward(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        direction = ResolveWalkDirection(direction);
        FaceToward(direction);
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

    void FaceToward(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion look = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, m_TurnSpeed * Time.deltaTime);
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
        float bob = Mathf.Abs(Mathf.Sin(m_BobPhase)) * m_BobAmplitude;
        if (m_Body != null)
            m_Body.localPosition = m_BodyBasePosition + new Vector3(0f, bob, 0f);

        float swing = Mathf.Sin(m_BobPhase) * m_ArmSwing;
        if (m_LeftArm != null)
            m_LeftArm.localRotation = Quaternion.Euler(swing, 0f, 0f);
        if (m_RightArm != null)
            m_RightArm.localRotation = Quaternion.Euler(-swing, 0f, 0f);
    }

    /// <summary>Standing still with the throwing arm raised in a brief windup before each lob.</summary>
    void AnimateThrowIdle()
    {
        if (m_Animator != null)
        {
            m_Animator.SetFloat(k_SpeedHash, 0f);
            return;
        }

        if (m_Body != null)
            m_Body.localPosition = m_BodyBasePosition;

        bool winding = Time.time < m_ThrowWindupUntil;
        if (m_RightArm != null)
            m_RightArm.localRotation = Quaternion.Euler(winding ? -140f : -20f, 0f, 0f);
        if (m_LeftArm != null)
            m_LeftArm.localRotation = Quaternion.Euler(-20f, 0f, 0f);
    }

    void ThrowBone(Vector3 targetPosition)
    {
        if (m_BonePrefab == null)
            return;

        Vector3 start = m_ThrowOrigin != null ? m_ThrowOrigin.position : transform.position + Vector3.up * 1.3f;
        var boneGo = Instantiate(m_BonePrefab, start, Quaternion.identity);
        var bone = boneGo.GetComponent<ThrownBone>();
        if (bone != null)
            bone.Launch(start, targetPosition, m_ThrowDuration, m_ThrowArcHeight, m_Campfire, m_ThrowFuelDamage);
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
    /// Sent away when the campfire dies (see GameManager's outage) or when the
    /// Coronado's entrance clears the field (see BossIntro): stops counting as
    /// hittable, turns and walks off, then disappears. OnDied still fires on
    /// the way out so the spawner's alive count stays right. With
    /// <paramref name="direction"/> given (BossIntro sends every enemy the same
    /// way, north into the fog) that direction is used as-is; left null, it
    /// falls back to away-from-the-fire like before.
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

        Vector3 source = m_Campfire != null ? m_Campfire.transform.position : transform.position - transform.forward;
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

    /// <summary>
    /// Called by GameManager on victory: freezes AI immediately and topples the
    /// Lanzahuesos into the ground over a second, then removes it. Distinct
    /// from Die() - not a combat kill, so no OnDied.
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

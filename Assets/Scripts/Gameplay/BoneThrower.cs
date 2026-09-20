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
    [SerializeField] Transform m_Body;
    [SerializeField] Transform m_LeftArm;
    [SerializeField] Transform m_RightArm;
    [SerializeField] Renderer[] m_Renderers;

    [Header("Feel")]
    [SerializeField] float m_BobAmplitude = 0.06f;
    [SerializeField] float m_BobFrequency = 6f;
    [SerializeField] float m_ArmSwing = 35f;
    [SerializeField] Color m_HitFlashColor = new Color(1f, 0.25f, 0.2f);

    [Header("Death")]
    [SerializeField] AudioClip m_DeathSfx;
    [SerializeField, Range(0f, 1f)] float m_DeathSfxVolume = 0.85f;
    [SerializeField] bool m_DeathParticles = true;

    [Header("Events")]
    [SerializeField] UnityEvent m_OnDied = new UnityEvent();

    int m_Hits;
    float m_BobPhase;
    float m_NextThrowTime;
    float m_ThrowWindupUntil;
    float m_HitFlashUntil;
    Vector3 m_BodyBasePosition;
    Color[] m_BaseColors;

    public UnityEvent OnDied => m_OnDied;
    public bool IsAlive => m_Hits > 0;

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
                ThrowBone(campfirePosition);
            }
        }

        UpdateHitFlash();
    }

    void MoveToward(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        direction.Normalize();
        FaceToward(direction);
        transform.position += direction * (m_MoveSpeed * Time.deltaTime);
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
        SkeletonDeathFx.Play(transform.position + Vector3.up * 0.9f, m_DeathSfx, m_DeathSfxVolume, m_DeathParticles);
        m_OnDied.Invoke();
        Destroy(gameObject);
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

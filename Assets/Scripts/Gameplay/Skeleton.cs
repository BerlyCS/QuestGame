using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A cursed skeleton. Walks toward the player, attacks when close, and can be
/// damaged (ready for the future sword). Simple primitive-based presentation
/// with a procedural walk bob so it reads as "alive".
/// </summary>
[DisallowMultipleComponent]
public class Skeleton : MonoBehaviour
{
    [Header("Stats")]
    [SerializeField] float m_MaxHealth = 30f;
    [SerializeField] float m_MoveSpeed = 1.35f;
    [SerializeField] float m_TurnSpeed = 540f;
    [SerializeField] float m_AttackDamage = 8f;
    [SerializeField] float m_StopDistance = 1.5f;
    [SerializeField] float m_AttackCooldown = 1.6f;

    [Header("References")]
    [SerializeField] Transform m_Target;
    [SerializeField] Transform m_Body;
    [SerializeField] Transform m_LeftArm;
    [SerializeField] Transform m_RightArm;
    [SerializeField] Renderer[] m_Renderers;

    [Header("Feel")]
    [SerializeField] float m_BobAmplitude = 0.06f;
    [SerializeField] float m_BobFrequency = 6f;
    [SerializeField] float m_ArmSwing = 35f;
    [SerializeField] Color m_HitFlashColor = new Color(1f, 0.25f, 0.2f);

    [Header("Events")]
    [SerializeField] UnityEvent m_OnDied = new UnityEvent();

    float m_Health;
    float m_NextAttackTime;
    float m_BobPhase;
    float m_HitFlashUntil;
    Vector3 m_BodyBasePosition;
    Color[] m_BaseColors;

    public UnityEvent OnDied => m_OnDied;
    public bool IsAlive => m_Health > 0f;

    void Awake()
    {
        m_Health = m_MaxHealth;
        if (m_Body != null)
            m_BodyBasePosition = m_Body.localPosition;
        CacheColors();
    }

    void Start()
    {
        if (m_Target == null && Camera.main != null)
            m_Target = Camera.main.transform;
    }

    void Update()
    {
        if (!IsAlive || m_Target == null)
            return;

        Vector3 toTarget = m_Target.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (toTarget.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, m_TurnSpeed * Time.deltaTime);
        }

        if (distance > m_StopDistance)
        {
            transform.position += toTarget.normalized * (m_MoveSpeed * Time.deltaTime);
            AnimateWalk();
        }
        else
        {
            AnimateIdle();
            if (Time.time >= m_NextAttackTime)
            {
                m_NextAttackTime = Time.time + m_AttackCooldown;
                Attack();
            }
        }

        UpdateHitFlash();
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

    void AnimateIdle()
    {
        float idle = Mathf.Sin(Time.time * 2.5f) * 8f;
        if (m_LeftArm != null)
            m_LeftArm.localRotation = Quaternion.Euler(idle, 0f, 0f);
        if (m_RightArm != null)
            m_RightArm.localRotation = Quaternion.Euler(-idle, 0f, 0f);
    }

    void Attack()
    {
        var playerHealth = m_Target.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
            playerHealth.TakeDamage(m_AttackDamage);
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive)
            return;

        m_Health -= amount;
        m_HitFlashUntil = Time.time + 0.12f;

        if (m_Health <= 0f)
            Die();
    }

    /// <summary>
    /// Removes the skeleton instantly, bypassing the normal damage path.
    /// Used by the banishing cylinder.
    /// </summary>
    public void Banish()
    {
        if (!IsAlive)
            return;

        Die();
    }

    void Die()
    {
        m_Health = 0f;
        m_OnDied.Invoke();
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

using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Minimal player health. Skeletons deal damage here; UI and the lose state
/// will hook into these events later.
/// </summary>
[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour
{
    [SerializeField] float m_MaxHealth = 100f;
    [SerializeField] UnityEvent m_OnDamaged = new UnityEvent();
    [SerializeField] UnityEvent m_OnDied = new UnityEvent();

    float m_Health;

    public float MaxHealth => m_MaxHealth;
    public float Health => m_Health;
    public float Normalized => m_MaxHealth <= 0f ? 0f : m_Health / m_MaxHealth;
    public bool IsAlive => m_Health > 0f;
    public UnityEvent OnDamaged => m_OnDamaged;
    public UnityEvent OnDied => m_OnDied;

    void Awake()
    {
        m_Health = m_MaxHealth;
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive)
            return;

        m_Health = Mathf.Max(0f, m_Health - amount);
        m_OnDamaged.Invoke();

        if (m_Health <= 0f)
            m_OnDied.Invoke();
    }

    public void Heal(float amount)
    {
        m_Health = Mathf.Min(m_MaxHealth, m_Health + amount);
    }
}

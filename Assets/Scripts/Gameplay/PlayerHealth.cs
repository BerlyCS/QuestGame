using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The player's life, taken only by the hunting skeletons (see Skeleton's
/// hunter mode). No health bar: it is read off the screen itself - every hit
/// flashes the edges of the view red, and the more life is
/// gone the redder the vignette stays. At zero, OnDied fires and GameManager
/// ends the run.
/// </summary>
[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour
{
    [SerializeField] float m_MaxHealth = 150f;

    [Header("Pain")]
    [Tooltip("Random grunt played when an enemy lands a hit. Falls back to the clips under Resources/Pain when empty.")]
    [SerializeField] AudioClip[] m_PainSfx;

    [Header("Regeneration")]
    [Tooltip("Seconds after the last hit before health starts coming back.")]
    [SerializeField] float m_RegenDelay = 6f;
    [Tooltip("Health restored per second once the delay has passed, so the damage vignette clears.")]
    [SerializeField] float m_RegenPerSecond = 5f;

    [SerializeField] Transform m_Head;

    [Tooltip("Transparent unlit material for the damage vignette quad (its texture is generated here).")]
    [SerializeField] Material m_VignetteMaterial;

    [SerializeField] UnityEvent m_OnDied = new UnityEvent();

    float m_Health;
    float m_HurtPulse;
    float m_LastDamageTime = float.NegativeInfinity;
    Material m_Instance;
    Renderer m_Vignette;
    AudioClip[] m_PainClips;
    AudioSource m_PainSource;

    public UnityEvent OnDied => m_OnDied;
    public float Health01 => Mathf.Clamp01(m_Health / m_MaxHealth);
    public bool IsAlive => m_Health > 0f;

    /// <summary>The head the hunters walk toward.</summary>
    public Transform Head => m_Head;

    void Awake()
    {
        m_Health = m_MaxHealth;

        // Pain grunts are the player's own voice, so they play non-spatialized
        // (2D) and must survive the early return below when the vignette is
        // unwired. Clips imported under Resources/Pain are found automatically.
        m_PainClips = m_PainSfx != null && m_PainSfx.Length > 0
            ? m_PainSfx
            : Resources.LoadAll<AudioClip>("Pain");
        m_PainSource = gameObject.AddComponent<AudioSource>();
        m_PainSource.playOnAwake = false;
        m_PainSource.spatialBlend = 0f;

        if (m_Head == null && Camera.main != null)
            m_Head = Camera.main.transform;
        if (m_Head == null || m_VignetteMaterial == null)
            return;

        m_Instance = new Material(m_VignetteMaterial);
        m_Instance.SetTexture("_BaseMap", CreateVignetteTexture());

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Damage Vignette";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(m_Head, false);
        quad.transform.localPosition = new Vector3(0f, 0f, 0.35f);
        quad.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
        m_Vignette = quad.GetComponent<Renderer>();
        m_Vignette.sharedMaterial = m_Instance;
        m_Vignette.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        m_Vignette.receiveShadows = false;
        m_Vignette.enabled = false;
    }

    void Update()
    {
        Regenerate();
        UpdateVignette();
    }

    void Regenerate()
    {
        if (!IsAlive || m_Health >= m_MaxHealth)
            return;

        // Health only comes back once the player has been left alone for a
        // while, so a burst of hits still hurts but the red never lingers.
        if (Time.time - m_LastDamageTime < m_RegenDelay)
            return;

        m_Health = Mathf.Min(m_MaxHealth, m_Health + m_RegenPerSecond * Time.deltaTime);
    }

    void UpdateVignette()
    {
        if (m_Vignette == null)
            return;

        m_HurtPulse = Mathf.Max(0f, m_HurtPulse - Time.deltaTime * 1.2f);
        float alpha = Mathf.Clamp01(Mathf.Max(m_HurtPulse, (1f - Health01) * 0.55f));
        m_Vignette.enabled = alpha > 0.01f;
        m_Instance.SetColor("_BaseColor", new Color(0.9f, 0.04f, 0.04f, alpha));
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive)
            return;

        m_Health = Mathf.Max(0f, m_Health - amount);
        m_HurtPulse = 1f;
        m_LastDamageTime = Time.time;
        PlayPain();

        if (m_Health <= 0f)
            m_OnDied.Invoke();
    }

    /// <summary>Picks one of the pain grunts at random so repeated hits do not sound looped.</summary>
    void PlayPain()
    {
        if (m_PainSource == null || m_PainClips == null || m_PainClips.Length == 0)
            return;

        AudioClip clip = m_PainClips[Random.Range(0, m_PainClips.Length)];
        if (clip != null)
            m_PainSource.PlayOneShot(clip);
    }

    void OnDestroy()
    {
        if (m_Instance != null)
            Destroy(m_Instance);
    }

    /// <summary>Radial gradient: clear in the middle, opaque red toward the edges.</summary>
    static Texture2D CreateVignetteTexture()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.SmoothStep(0.35f, 1f, d);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        texture.Apply();
        return texture;
    }
}

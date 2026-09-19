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
    [SerializeField] float m_MaxHealth = 100f;
    [SerializeField] Transform m_Head;

    [Tooltip("Transparent unlit material for the damage vignette quad (its texture is generated here).")]
    [SerializeField] Material m_VignetteMaterial;

    [SerializeField] UnityEvent m_OnDied = new UnityEvent();

    float m_Health;
    float m_HurtPulse;
    Material m_Instance;
    Renderer m_Vignette;

    public UnityEvent OnDied => m_OnDied;
    public float Health01 => Mathf.Clamp01(m_Health / m_MaxHealth);
    public bool IsAlive => m_Health > 0f;

    /// <summary>The head the hunters walk toward.</summary>
    public Transform Head => m_Head;

    void Awake()
    {
        m_Health = m_MaxHealth;
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

        if (m_Health <= 0f)
            m_OnDied.Invoke();
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

using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The player's life, taken only by the hunting skeletons (see Skeleton's
/// hunter mode). No health bar: it is read off the screen itself - every hit
/// bursts a spray of red particles off the player and flashes the edges of the
/// view red, and the more life is gone the redder the vignette stays. At zero
/// the whole screen floods opaque red, OnDied fires and GameManager ends the
/// run.
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
    [SerializeField] float m_RegenDelay = 5f;
    [Tooltip("Health restored per second once the delay has passed, so the damage vignette clears.")]
    [SerializeField] float m_RegenPerSecond = 6f;

    [SerializeField] Transform m_Head;

    [Tooltip("Transparent unlit material for the damage vignette quad (its texture is generated here).")]
    [SerializeField] Material m_VignetteMaterial;

    [Header("Death")]
    [Tooltip("Seconds the whole screen takes to fade to opaque red once life runs out.")]
    [SerializeField] float m_DeathFadeDuration = 1.4f;
    [Tooltip("Colour the screen floods with on death.")]
    [SerializeField] Color m_DeathColor = new Color(0.85f, 0.02f, 0.02f, 1f);

    [SerializeField] UnityEvent m_OnDied = new UnityEvent();

    float m_Health;
    float m_HurtPulse;
    float m_LastDamageTime = float.NegativeInfinity;
    Material m_Instance;
    Renderer m_Vignette;
    Material m_DeathInstance;
    Renderer m_DeathOverlay;
    bool m_DeathStarted;
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

        if (m_Head == null)
            return;

        // The death flood is built regardless of the vignette material so the
        // one cue that matters most can never be missing.
        BuildDeathOverlay();

        if (m_VignetteMaterial == null)
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

    /// <summary>
    /// Builds the full-view quad that floods red on death. It reuses the
    /// vignette's unlit/transparent shader but with a plain white texture, so a
    /// flat red tint can be faded up over the whole view. It is oversized and
    /// parked close to the eye so it always covers the field of view.
    /// </summary>
    void BuildDeathOverlay()
    {
        if (m_VignetteMaterial != null)
        {
            // Clone the vignette material so the death flood inherits its
            // transparent/unlit setup, then swap its gradient for flat white.
            m_DeathInstance = new Material(m_VignetteMaterial) { name = "M_DeathRed (Runtime)" };
        }
        else
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null)
                return;
            m_DeathInstance = new Material(shader) { name = "M_DeathRed (Runtime)" };
            MakeTransparent(m_DeathInstance);
        }

        if (m_DeathInstance.HasProperty("_BaseMap"))
            m_DeathInstance.SetTexture("_BaseMap", Texture2D.whiteTexture);
        m_DeathInstance.SetColor("_BaseColor", new Color(m_DeathColor.r, m_DeathColor.g, m_DeathColor.b, 0f));

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Death Red";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(m_Head, false);
        quad.transform.localPosition = new Vector3(0f, 0f, 0.45f);
        quad.transform.localScale = new Vector3(3.5f, 3.5f, 1f);
        m_DeathOverlay = quad.GetComponent<Renderer>();
        m_DeathOverlay.sharedMaterial = m_DeathInstance;
        m_DeathOverlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        m_DeathOverlay.receiveShadows = false;
        m_DeathOverlay.enabled = false;
    }

    /// <summary>Forces a URP shader instance to alpha-blend, so the fade works even without the vignette material.</summary>
    static void MakeTransparent(Material material)
    {
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
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

    public void TakeDamage(float amount) => TakeDamage(amount, Vector3.zero);

    /// <summary>
    /// Applies damage and, when <paramref name="sourcePosition"/> is given, sprays
    /// red hit particles off the player toward wherever the blow came from. At
    /// zero health the screen floods red and <see cref="OnDied"/> fires.
    /// </summary>
    public void TakeDamage(float amount, Vector3 sourcePosition)
    {
        if (!IsAlive)
            return;

        m_Health = Mathf.Max(0f, m_Health - amount);
        m_HurtPulse = 1f;
        m_LastDamageTime = Time.time;
        PlayPain();
        SpawnHitBurst(BurstPosition(sourcePosition));

        if (m_Health <= 0f)
        {
            BeginDeath();
            m_OnDied.Invoke();
        }
    }

    /// <summary>Where the hit spray appears: on the player, pulled toward the blow.</summary>
    Vector3 BurstPosition(Vector3 sourcePosition)
    {
        Vector3 origin = m_Head != null ? m_Head.position : transform.position;
        if (sourcePosition == Vector3.zero)
            return origin;

        Vector3 direction = origin - sourcePosition;
        if (direction.sqrMagnitude < 0.0001f)
            return origin;

        return origin + direction.normalized * 0.3f;
    }

    void BeginDeath()
    {
        if (m_DeathStarted)
            return;
        m_DeathStarted = true;

        if (m_DeathOverlay == null || m_DeathInstance == null)
            return;

        // The vignette would only muddy the full red flood.
        if (m_Vignette != null)
            m_Vignette.enabled = false;

        StartCoroutine(FadeToRed());
    }

    IEnumerator FadeToRed()
    {
        m_DeathOverlay.enabled = true;

        float elapsed = 0f;
        while (elapsed < m_DeathFadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Clamp01(elapsed / m_DeathFadeDuration);
            m_DeathInstance.SetColor("_BaseColor", new Color(m_DeathColor.r, m_DeathColor.g, m_DeathColor.b, alpha));
            yield return null;
        }

        m_DeathInstance.SetColor("_BaseColor", new Color(m_DeathColor.r, m_DeathColor.g, m_DeathColor.b, 1f));
    }

    /// <summary>
    /// A short-lived burst of red particles so a landed blow reads even before
    /// the vignette flinches. Not parented to the player because it should stay
    /// where the hit landed while the view lurches.
    /// </summary>
    static void SpawnHitBurst(Vector3 position)
    {
        var go = new GameObject("Player Hit FX");
        go.transform.position = position;

        var particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = particles.main;
        main.duration = 0.3f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.72f, 0.02f, 0.02f, 1f),
            new Color(0.95f, 0.15f, 0.1f, 1f));
        main.gravityModifier = 0.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;

        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.08f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Particles/Standard Unlit")
            ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            var material = new Material(shader) { name = "M_PlayerHit (Runtime)" };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            material.color = Color.white;
            renderer.material = material;
        }

        particles.Play();
        go.AddComponent<AutoDestroyAfter>().Lifetime = 1.2f;
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
        if (m_DeathInstance != null)
            Destroy(m_DeathInstance);
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

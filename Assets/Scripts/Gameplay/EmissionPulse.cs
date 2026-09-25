using UnityEngine;

/// <summary>
/// Wordless tutorial cue that calls attention to an object in place instead of
/// parking a floating sphere above it. Two presentation modes:
///
/// - Default: the object's own surface glow-pulses. The pulse rides on a
///   per-renderer <see cref="MaterialPropertyBlock"/> so every cue shares one
///   material and nothing on disk is dirtied, and the emission keyword is
///   turned on for the object's materials the first time the cue wakes so the
///   glow is guaranteed to show even on materials authored without emission.
/// - <see cref="OutlineOnly"/>: a front-culled shell (see
///   <see cref="HaloShells"/>) wraps the object so only its silhouette rims.
///   Used by the logs, whose full-surface glow blew out into an unreadable
///   bright blob.
///
/// <see cref="PrologueController"/> owns the order: only the current step's cue
/// is ever active, so the player sees one object blink at a time (axe, then
/// bow, then the logs) rather than every object at once.
/// </summary>
[DisallowMultipleComponent]
public class EmissionPulse : MonoBehaviour
{
    [SerializeField] Color m_Color = new Color(1f, 0.72f, 0.22f, 1f);
    [SerializeField] float m_Speed = 4.5f;
    [Range(0f, 3f)] [SerializeField] float m_Min = 0.35f;
    [Range(0f, 6f)] [SerializeField] float m_Max = 2.6f;

    [Header("Outline")]
    [Tooltip("When on, the cue is drawn only as a glowing rim around the " +
        "object's silhouette instead of flooding its surface. The logs use this " +
        "so the player can still read the log's shape.")]
    [SerializeField] bool m_OutlineOnly;
    [Tooltip("How far, in metres, the outline shell sticks out past the surface.")]
    [SerializeField] float m_OutlineThickness = 0.012f;

    static readonly int s_EmissionId = Shader.PropertyToID("_EmissionColor");
    static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int s_ColorId = Shader.PropertyToID("_Color");

    const string k_OutlineShellName = "Cue Outline Shell";

    static Material s_OutlineMaterial;

    Renderer[] m_Renderers;
    bool[] m_Glowable;
    MaterialPropertyBlock m_Block;
    bool m_Active;
    bool m_Ready;

    public Color Color
    {
        get => m_Color;
        set => m_Color = value;
    }

    /// <summary>
    /// Switches between the surface flood and the outline rim. Safe to call
    /// after the component has already prepared: it tears down whatever it had
    /// and rebuilds in the new mode.
    /// </summary>
    public void SetOutline(bool outline)
    {
        if (m_OutlineOnly == outline)
            return;

        DestroyOutlineShells();
        m_OutlineOnly = outline;
        m_Ready = false;
        Prepare();
        if (!m_Active)
            Restore();
    }

    void Awake()
    {
        m_Block = new MaterialPropertyBlock();
        Prepare();
        Restore();
    }

    void OnDestroy()
    {
        Restore();
        DestroyOutlineShells();
    }

    /// <summary>Turns the cue on or off. Off restores the object's own look.</summary>
    public void SetActive(bool active)
    {
        m_Active = active;
        if (!m_Ready)
            Prepare();

        if (!active)
            Restore();
        else if (m_OutlineOnly)
            SetShellsEnabled(true);
    }

    void Update()
    {
        if (!m_Active || !m_Ready)
            return;

        if (m_OutlineOnly)
        {
            // The rim material is additive, so the pulse rides mostly on alpha:
            // the edge brightens and fades instead of ever covering the surface.
            // Its peak is held near 1 (unlike the surface mode, which leans on
            // HDR values past 1) so the thin edge stays a cue rather than a flare.
            float rimPulse = Mathf.Lerp(m_Min, Mathf.Min(m_Max, 1.1f),
                0.5f + 0.5f * Mathf.Sin(Time.time * m_Speed));
            Color rim = m_Color * rimPulse;
            rim.a = m_Color.a * Mathf.Clamp01(rimPulse);

            for (int i = 0; i < m_Renderers.Length; i++)
            {
                var shell = m_Renderers[i];
                if (shell == null)
                    continue;

                shell.enabled = true;
                shell.GetPropertyBlock(m_Block);
                m_Block.SetColor(s_BaseColorId, rim);
                m_Block.SetColor(s_ColorId, rim);
                shell.SetPropertyBlock(m_Block);
            }

            return;
        }

        Color emission = m_Color * Mathf.Lerp(m_Min, m_Max,
            0.5f + 0.5f * Mathf.Sin(Time.time * m_Speed));

        for (int i = 0; i < m_Renderers.Length; i++)
        {
            var renderer = m_Renderers[i];
            if (renderer == null || !m_Glowable[i])
                continue;

            renderer.GetPropertyBlock(m_Block);
            // As the pulse dips the emission darkens; as it swells it blows out
            // past 1, so the same value reads against both a bright afternoon
            // sky and the near-black night.
            m_Block.SetColor(s_EmissionId, emission);
            renderer.SetPropertyBlock(m_Block);
        }
    }

    void Prepare()
    {
        m_Ready = true;

        if (m_OutlineOnly)
        {
            PrepareOutline();
            return;
        }

        m_Renderers = GetComponentsInChildren<Renderer>(true);
        m_Glowable = new bool[m_Renderers.Length];

        for (int i = 0; i < m_Renderers.Length; i++)
        {
            var renderer = m_Renderers[i];
            if (renderer == null)
                continue;

            // Only the object's own, normally-visible surfaces glow. The grab
            // highlight's halo shells are disabled until a hand comes near, so
            // they are skipped rather than being forced on by the cue.
            m_Glowable[i] = renderer.enabled;

            foreach (var material in renderer.sharedMaterials)
            {
                if (material != null && material.HasProperty(s_EmissionId))
                    material.EnableKeyword("_EMISSION");
            }
        }
    }

    /// <summary>
    /// Builds the front-culled shell that rims the object, one copy per mesh,
    /// and leaves it off until the cue is active. Because only the enlarged
    /// shell's back faces draw, the object itself keeps its own look and just a
    /// thin edge shows around its silhouette.
    /// </summary>
    void PrepareOutline()
    {
        var material = ResolveOutlineMaterial();
        if (material == null)
        {
            m_Renderers = System.Array.Empty<Renderer>();
            m_Glowable = System.Array.Empty<bool>();
            return;
        }

        m_Renderers = HaloShells.Build(transform, material, m_OutlineThickness, k_OutlineShellName);
        m_Glowable = new bool[m_Renderers.Length];
        for (int i = 0; i < m_Renderers.Length; i++)
        {
            m_Glowable[i] = true;
            m_Renderers[i].enabled = m_Active;
        }
    }

    void Restore()
    {
        if (!m_Ready || m_Block == null)
            return;

        if (m_OutlineOnly)
        {
            SetShellsEnabled(false);
            return;
        }

        for (int i = 0; i < m_Renderers.Length; i++)
        {
            var renderer = m_Renderers[i];
            if (renderer == null || !m_Glowable[i])
                continue;

            renderer.GetPropertyBlock(m_Block);
            m_Block.SetColor(s_EmissionId, Color.black);
            renderer.SetPropertyBlock(m_Block);
        }
    }

    void SetShellsEnabled(bool enabled)
    {
        if (m_Renderers == null)
            return;

        for (int i = 0; i < m_Renderers.Length; i++)
            if (m_Renderers[i] != null)
                m_Renderers[i].enabled = enabled;
    }

    void DestroyOutlineShells()
    {
        if (m_OutlineOnly && m_Renderers != null)
        {
            for (int i = 0; i < m_Renderers.Length; i++)
            {
                if (m_Renderers[i] == null)
                    continue;

                m_Renderers[i].enabled = false;
                Destroy(m_Renderers[i].gameObject);
            }
        }

        m_Renderers = null;
        m_Glowable = null;
    }

    /// <summary>
    /// The shared additive, unlit material the outline shells use. Culling the
    /// front faces is what turns an enlarged copy into a rim: the object hides
    /// the shell's interior, so only the edge past the silhouette draws. Built
    /// once and shared, since each shell's pulse rides on its own property
    /// block.
    /// </summary>
    static Material ResolveOutlineMaterial()
    {
        if (s_OutlineMaterial != null)
            return s_OutlineMaterial;

        var shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Transparent")
            ?? Shader.Find("Standard");
        if (shader == null)
            return null;

        var material = new Material(shader)
        {
            name = "M_CueOutline (Runtime)",
            hideFlags = HideFlags.HideAndDontSave,
        };

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);
        }

        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);
        if (material.HasProperty(s_BaseColorId))
            material.SetColor(s_BaseColorId, Color.white);
        if (material.HasProperty(s_ColorId))
            material.SetColor(s_ColorId, Color.white);

        s_OutlineMaterial = material;
        return material;
    }
}

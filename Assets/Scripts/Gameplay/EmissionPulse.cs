using UnityEngine;

/// <summary>
/// Wordless tutorial cue: makes an object's own surface glow-pulse in place,
/// instead of parking a floating sphere above it. The pulse rides on a
/// per-renderer <see cref="MaterialPropertyBlock"/> so every cue shares one
/// material and nothing on disk is dirtied, and the emission keyword is turned
/// on for the object's materials the first time the cue wakes so the glow is
/// guaranteed to show even on materials authored without emission.
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

    static readonly int s_EmissionId = Shader.PropertyToID("_EmissionColor");
    static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int s_ColorId = Shader.PropertyToID("_Color");

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

    void Awake()
    {
        m_Block = new MaterialPropertyBlock();
        Prepare();
        Restore();
    }

    void OnDestroy() => Restore();

    /// <summary>Turns the cue on or off. Off restores the object's own look.</summary>
    public void SetActive(bool active)
    {
        m_Active = active;
        if (!m_Ready)
            Prepare();

        if (!active)
            Restore();
    }

    void Update()
    {
        if (!m_Active || !m_Ready)
            return;

        float pulse = Mathf.Lerp(m_Min, m_Max, 0.5f + 0.5f * Mathf.Sin(Time.time * m_Speed));
        Color emission = m_Color * pulse;

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
        m_Renderers = GetComponentsInChildren<Renderer>(true);
        m_Glowable = new bool[m_Renderers.Length];
        m_Ready = true;

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

    void Restore()
    {
        if (!m_Ready || m_Block == null)
            return;

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
}

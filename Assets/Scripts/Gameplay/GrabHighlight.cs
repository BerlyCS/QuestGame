using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Perceptual affordance for anything the player can grab (see CLAUDE.md: no
/// haptics, so it has to be seen and heard). A hand coming near makes the
/// object pulse with a soft glow halo and grabbing it turns the
/// glow solid (and, for logs, plays a wooden knock).
/// Reads Grabbable.PointsCount (hover + select) and SelectingPointsCount, the
/// SDK's own state, polled each frame rather than trusting individual events.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Grabbable))]
public class GrabHighlight : MonoBehaviour
{
    public enum GrabSound { None, Wood }

    [SerializeField]
    [Tooltip("Additive transparent material used for the halo shell.")]
    Material m_GlowMaterial;

    [SerializeField] Color m_HoverColor = new Color(1f, 0.7f, 0.2f, 1f);
    [SerializeField] Color m_HeldColor = new Color(1f, 0.95f, 0.65f, 1f);

    [SerializeField]
    [Tooltip("How far, in metres, the halo sticks out past the object's surface.")]
    float m_HaloThickness = 0.025f;

    [SerializeField] GrabSound m_GrabSound = GrabSound.Wood;

    Grabbable m_Grabbable;
    Material m_ShellMaterial;
    Renderer[] m_Shells;
    AudioSource m_Audio;
    bool m_WasHovered;
    bool m_WasHeld;

    /// <summary>
    /// Scene-builder hook: these fields have no Interaction SDK injection point, so
    /// GameSceneBuilder calls this to hand over the halo material.
    /// </summary>
    public void InjectGlowMaterial(Material glowMaterial) => m_GlowMaterial = glowMaterial;

    void Awake()
    {
        m_Grabbable = GetComponent<Grabbable>();
        if (m_GlowMaterial == null)
            return;

        m_ShellMaterial = new Material(m_GlowMaterial);
        m_Shells = HaloShells.Build(transform, m_ShellMaterial, m_HaloThickness);

        if (m_GrabSound != GrabSound.None)
        {
            var audioGo = new GameObject("Grab Audio");
            audioGo.transform.SetParent(transform, false);
            m_Audio = audioGo.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.spatialBlend = 1f;
        }
    }

    void OnDestroy()
    {
        if (m_ShellMaterial != null)
            Destroy(m_ShellMaterial);
    }

    void Update()
    {
        if (m_ShellMaterial == null)
            return;

        bool held = m_Grabbable.SelectingPointsCount > 0;
        bool hovered = m_Grabbable.PointsCount > 0;

        if (held && !m_WasHeld && m_Audio != null)
            m_Audio.PlayOneShot(ProceduralSfx.WoodGrab);

        m_WasHeld = held;
        m_WasHovered = hovered;

        bool glowing = hovered || held;
        Color color;
        if (held)
        {
            color = m_HeldColor * 0.8f;
        }
        else
        {
            float pulse = 0.55f + 0.25f * Mathf.Sin(Time.time * 7f);
            color = m_HoverColor * pulse;
        }

        m_ShellMaterial.SetColor("_BaseColor", color);
        for (int i = 0; i < m_Shells.Length; i++)
            m_Shells[i].enabled = glowing;
    }
}

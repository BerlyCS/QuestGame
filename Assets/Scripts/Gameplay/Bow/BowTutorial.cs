using UnityEngine;

/// <summary>
/// Teaches the two-step bow grip with a pair of soft blinking markers.
///
/// Before the bow is held a marker pulses on the grip (the riser) to show where
/// to take it. As soon as the bow is in hand <see cref="BowNotch"/> puts an
/// arrow on the string, and the marker moves to the string to show where to
/// pinch and pull. Once <see cref="m_ShotsToComplete"/> arrows have been fired
/// the markers stay off for good.
///
/// The markers are additive glow shells built alongside the bow so they read in
/// the dark, and they carry no colliders at all, so they can never be grabbed
/// or deflect an arrow.
/// </summary>
[DisallowMultipleComponent]
public class BowTutorial : MonoBehaviour
{
    [SerializeField]
    [Tooltip("The bow this tutorial belongs to. Auto-found in the parent chain.")]
    Bow m_Bow;

    [SerializeField]
    [Tooltip("Measurer that counts the arrows fired. Auto-found in the children.")]
    BowPullMeasurer m_Measurer;

    [SerializeField]
    [Tooltip("Renderer of the sphere that marks the bow grip.")]
    Renderer m_GripMarker;

    [SerializeField]
    [Tooltip("Renderer of the sphere that marks the string.")]
    Renderer m_StringMarker;

    [Header("Blink")]
    [SerializeField] Color m_MarkerColor = new Color(1f, 0.78f, 0.3f, 1f);
    [SerializeField] float m_BlinkSpeed = 5f;
    [Range(0f, 1f)] [SerializeField] float m_BlinkMin = 0.1f;
    [Range(0f, 1f)] [SerializeField] float m_BlinkMax = 0.85f;

    [Header("Tutorial")]
    [Tooltip("How many arrows have to be fired before the markers disappear.")]
    [SerializeField] int m_ShotsToComplete = 3;

    static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int s_ColorId = Shader.PropertyToID("_Color");

    MaterialPropertyBlock m_Block;

    void Awake()
    {
        m_Block = new MaterialPropertyBlock();

        if (m_Bow == null)
        {
            m_Bow = GetComponentInParent<Bow>();
        }

        if (m_Measurer == null)
        {
            m_Measurer = GetComponentInChildren<BowPullMeasurer>(true);
        }

        Hide(m_GripMarker);
        Hide(m_StringMarker);
    }

    void Update()
    {
        bool held = m_Bow != null && m_Bow.IsHeld;
        bool done = m_Measurer != null && m_Measurer.ShotsFired >= m_ShotsToComplete;

        // Grab first, pull second: the grip marker hands over to the string
        // marker as soon as the bow is being held.
        UpdateMarker(m_GripMarker, !done && !held);
        UpdateMarker(m_StringMarker, !done && held);
    }

    void UpdateMarker(Renderer marker, bool visible)
    {
        if (marker == null)
        {
            return;
        }

        if (!visible)
        {
            Hide(marker);
            return;
        }

        marker.enabled = true;

        // The glow material is additive, so the pulse reads as a brightness
        // change: the sphere sinks towards invisible and swells back up.
        float pulse = Mathf.Lerp(m_BlinkMin, m_BlinkMax,
            0.5f + 0.5f * Mathf.Sin(Time.time * m_BlinkSpeed));
        Color color = m_MarkerColor * pulse;
        color.a = m_MarkerColor.a * pulse;

        marker.GetPropertyBlock(m_Block);
        m_Block.SetColor(s_BaseColorId, color);
        m_Block.SetColor(s_ColorId, color);
        marker.SetPropertyBlock(m_Block);
    }

    static void Hide(Renderer marker)
    {
        if (marker != null)
        {
            marker.enabled = false;
        }
    }

    public void InjectReferences(
        Bow bow,
        BowPullMeasurer measurer,
        Renderer gripMarker,
        Renderer stringMarker)
    {
        m_Bow = bow;
        m_Measurer = measurer;
        m_GripMarker = gripMarker;
        m_StringMarker = stringMarker;
    }
}

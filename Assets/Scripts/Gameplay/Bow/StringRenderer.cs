using UnityEngine;

/// <summary>
/// Draws the bow string as a three-point line (top limb, nock, bottom limb).
/// The nock point is moved around by the <see cref="BowPullMeasurer"/> while the
/// string is pulled. Ported unchanged from the OOT shooting gallery.
/// </summary>
[ExecuteInEditMode]
[DisallowMultipleComponent]
public class StringRenderer : MonoBehaviour
{
    [Header("Render Positions")]
    [SerializeField] Transform m_Start;
    [SerializeField] Transform m_Middle;
    [SerializeField] Transform m_End;

    LineRenderer m_LineRenderer;

    void Awake()
    {
        m_LineRenderer = GetComponent<LineRenderer>();
    }

    void Update()
    {
        if (Application.isEditor && !Application.isPlaying)
        {
            UpdatePositions();
        }
    }

    void OnEnable()
    {
        Application.onBeforeRender += UpdatePositions;
    }

    void OnDisable()
    {
        Application.onBeforeRender -= UpdatePositions;
    }

    void UpdatePositions()
    {
        if (m_LineRenderer == null || m_Start == null || m_Middle == null || m_End == null)
        {
            return;
        }

        m_LineRenderer.SetPositions(new[] { m_Start.position, m_Middle.position, m_End.position });
    }

    public void InjectReferences(Transform start, Transform middle, Transform end)
    {
        m_Start = start;
        m_Middle = middle;
        m_End = end;
    }
}

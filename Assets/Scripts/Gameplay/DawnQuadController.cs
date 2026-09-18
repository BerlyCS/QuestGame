using UnityEngine;

/// <summary>
/// The game's only progress indicator (see CLAUDE.md - cero UI: no bars, no
/// text). A quad on the eastern horizon whose emission brightens as
/// pow(survival progress, 2.2) - almost invisible for the first minute and a
/// half, then climbing fast as dawn actually approaches. Reading the sky is
/// the only way to gauge how close the end of the night is.
/// </summary>
[DisallowMultipleComponent]
public class DawnQuadController : MonoBehaviour
{
    [SerializeField] GameManager m_GameManager;
    [SerializeField] Color m_BaseEmission = new Color(1.6f, 1f, 0.55f);
    [SerializeField] float m_CurveExponent = 2.2f;

    Renderer m_Renderer;

    void Awake()
    {
        m_Renderer = GetComponent<Renderer>();
        m_Renderer.material.EnableKeyword("_EMISSION");
    }

    void Update()
    {
        float t = m_GameManager != null ? m_GameManager.SurvivalNormalized : 0f;
        float intensity = Mathf.Pow(t, m_CurveExponent);
        m_Renderer.material.SetColor("_EmissionColor", m_BaseEmission * intensity);
    }
}

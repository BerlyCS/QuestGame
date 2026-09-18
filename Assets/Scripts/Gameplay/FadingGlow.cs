using UnityEngine;

/// <summary>
/// A small emissive glow that fades out and removes itself. Used for the
/// resortera's momentary feedback (hand charge, shot muzzle flash) and for
/// the ember's miss-glow on the ground - all cheap, Light-free VFX so the
/// campfire stays the only realtime light in the scene (see CLAUDE.md: "una
/// sola luz realtime con sombras").
/// </summary>
[DisallowMultipleComponent]
public class FadingGlow : MonoBehaviour
{
    float m_Duration;
    Color m_BaseEmission;
    float m_Elapsed;
    Material m_Material;

    public static FadingGlow Spawn(Vector3 position, float scale, Color emission, float duration)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Fading Glow";
        go.transform.position = position;
        go.transform.localScale = Vector3.one * scale;
        Destroy(go.GetComponent<Collider>());

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_BaseColor", emission);
        material.SetColor("_EmissionColor", emission);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        go.GetComponent<Renderer>().material = material;

        var glow = go.AddComponent<FadingGlow>();
        glow.m_Duration = Mathf.Max(0.01f, duration);
        glow.m_BaseEmission = emission;
        glow.m_Material = material;
        return glow;
    }

    void Update()
    {
        m_Elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(1f - m_Elapsed / m_Duration);
        m_Material.SetColor("_EmissionColor", m_BaseEmission * t);
        m_Material.SetColor("_BaseColor", m_BaseEmission * t);

        if (m_Elapsed >= m_Duration)
            Destroy(gameObject);
    }
}

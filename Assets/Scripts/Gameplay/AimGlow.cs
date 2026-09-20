using UnityEngine;

/// <summary>
/// Makes an enemy glow while the slingshot's aim would hit it ("this shot
/// lands"): a bright pulsing halo, added lazily to whichever enemy is being
/// aimed at. Slingshot turns it on and off every frame it is drawing.
/// </summary>
[DisallowMultipleComponent]
public class AimGlow : MonoBehaviour
{
    static readonly Color s_Color = new Color(1f, 0.25f, 0.15f, 1f);

    Material m_Material;
    Renderer[] m_Shells;
    bool m_On;

    /// <summary>Gets (or adds) the glow on <paramref name="enemy"/>'s root.</summary>
    public static AimGlow For(GameObject enemy, Material glowMaterial)
    {
        var glow = enemy.GetComponent<AimGlow>();
        if (glow == null)
        {
            glow = enemy.AddComponent<AimGlow>();
            glow.m_Material = new Material(glowMaterial);
            glow.m_Shells = HaloShells.Build(enemy.transform, glow.m_Material, 0.05f);
        }
        return glow;
    }

    public void SetAimed(bool aimed)
    {
        if (m_On == aimed)
            return;

        m_On = aimed;
        foreach (var shell in m_Shells)
            if (shell != null)
                shell.enabled = aimed;
    }

    void Update()
    {
        if (!m_On)
            return;

        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 14f);
        m_Material.SetColor("_BaseColor", s_Color * pulse);
    }

    void OnDestroy()
    {
        if (m_Material != null)
            Destroy(m_Material);
    }
}

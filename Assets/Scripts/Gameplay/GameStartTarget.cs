using UnityEngine;

/// <summary>
/// A ceremonial prop, no longer a gate: the bullseye board standing behind the
/// campfire. An arrow that lands on it makes the board vanish in a puff of
/// white particles - a satisfying bit of target practice for the player's first
/// shots - but it no longer starts the night. The night is handed over by the
/// fire being fed (see <see cref="PrologueController"/> and
/// <see cref="GameManager.BeginNight"/>).
///
/// The hit is delivered by <see cref="IArrowHittable"/>, so the board needs a
/// non-trigger collider on the default layer (wired by GameSceneBuilder).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class GameStartTarget : MonoBehaviour, IArrowHittable
{
    [Header("References")]
    [Tooltip("The whole board: block, bullseye rings and label. Vanishes on the hit. " +
             "Leave empty to vanish the block's parent.")]
    [SerializeField] GameObject m_HideRoot;

    [Header("On hit")]
    [Tooltip("Seconds the struck board stays up before it vanishes.")]
    [SerializeField] float m_HideDelay = 0.35f;
    [Tooltip("Optional extra sound on the hit. The arrow plays its own impact.")]
    [SerializeField] AudioClip m_HitSfx;

    bool m_Hit;

    /// <summary>Called by the arrow that lands on the board.</summary>
    public void Hit(Arrow arrow)
    {
        if (m_Hit)
            return;
        m_Hit = true;

        if (m_HitSfx != null)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.playOnAwake = false;
            source.PlayOneShot(m_HitSfx);
        }

        SpawnWhiteBurst(transform.position + Vector3.up * 1.15f);
        Invoke(nameof(Vanish), Mathf.Max(0f, m_HideDelay));
    }

    void Vanish()
    {
        var board = m_HideRoot != null
            ? m_HideRoot
            : (transform.parent != null ? transform.parent.gameObject : gameObject);
        board.SetActive(false);
    }

    /// <summary>
    /// A short, collider-free puff of white sparks so the board reads as
    /// disintegrating rather than merely switching off.
    /// </summary>
    static void SpawnWhiteBurst(Vector3 position)
    {
        var go = new GameObject("Target Burst");
        go.transform.position = position;

        var particles = go.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.duration = 0.4f;
        main.loop = false;
        main.startLifetime = 0.85f;
        main.startSpeed = 2.6f;
        main.startSize = 0.07f;
        main.gravityModifier = 0.35f;
        main.startColor = Color.white;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 48) });

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.18f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/Unlit");
        var material = new Material(shader) { name = "M_TargetBurst (Runtime)" };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", Color.white);
        renderer.material = material;

        Object.Destroy(go, main.duration + main.startLifetime.constant + 0.5f);
    }
}

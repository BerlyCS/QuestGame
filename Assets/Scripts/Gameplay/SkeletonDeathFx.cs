using UnityEngine;

/// <summary>
/// One-shot death effect for the skeleton enemies (see <see cref="Skeleton"/>
/// and <see cref="BoneThrower"/>). Spawns a short-lived standalone object that
/// plays a breaking sound and a burst of black, purple-tinted "magic" smoke,
/// then removes itself. It is deliberately not parented to the enemy because
/// the enemy is destroyed the instant it dies.
/// </summary>
public static class SkeletonDeathFx
{
    static Material s_ParticleMaterial;
    static bool s_MaterialSearched;

    /// <summary>
    /// Plays the death effect at <paramref name="position"/>. Safe to call with
    /// a null clip (then only the particles are shown).
    /// </summary>
    public static void Play(Vector3 position, AudioClip clip, float volume, bool particles = true)
    {
        if (clip == null && !particles)
            return;

        var go = new GameObject("Skeleton Death FX");
        go.transform.position = position;

        float lifetime = 0f;

        if (clip != null)
        {
            var audio = go.AddComponent<AudioSource>();
            audio.clip = clip;
            audio.volume = Mathf.Clamp01(volume);
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.minDistance = 1.5f;
            audio.maxDistance = 25f;
            audio.dopplerLevel = 0f;
            audio.playOnAwake = false;
            audio.Play();
            lifetime = Mathf.Max(lifetime, clip.length);
        }

        if (particles)
        {
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Configure(ps);
            ps.Play();
            lifetime = Mathf.Max(lifetime,
                ps.main.duration + ps.main.startLifetime.constantMax);
        }

        go.AddComponent<AutoDestroyAfter>().Lifetime = Mathf.Max(lifetime, 0.5f) + 0.3f;
    }

    static void Configure(ParticleSystem ps)
    {
        var main = ps.main;
        main.duration = 1.1f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.22f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        // Near-black with a faint violet edge so it reads as dark magic rather
        // than soot; the alpha is faded out over the particle's lifetime below.
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.04f, 0f, 0.06f, 0.95f),
            new Color(0.32f, 0.05f, 0.45f, 0.9f));
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 220;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.28f;

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.y = new ParticleSystem.MinMaxCurve(0.35f);
        velocity.orbitalY = new ParticleSystem.MinMaxCurve(2.5f);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.04f, 0f, 0.06f), 0f),
                new GradientColorKey(new Color(0.2f, 0.02f, 0.3f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.15f)));

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        var material = GetParticleMaterial();
        if (material != null)
            renderer.sharedMaterial = material;
    }

    static Material GetParticleMaterial()
    {
        if (!s_MaterialSearched)
        {
            s_MaterialSearched = true;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                s_ParticleMaterial = new Material(shader) { name = "M_SkeletonDeath" };
                if (s_ParticleMaterial.HasProperty("_BaseColor"))
                    s_ParticleMaterial.SetColor("_BaseColor", Color.white);
                s_ParticleMaterial.color = Color.white;
            }
        }

        return s_ParticleMaterial;
    }
}

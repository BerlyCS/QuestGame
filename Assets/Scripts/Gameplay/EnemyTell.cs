using System.Collections;
using UnityEngine;

/// <summary>
/// Makes an enemy the player cannot see announce itself every so often, so the
/// forest can stay dense without anything sneaking in unnoticed. Without
/// haptics the tell has to be seen or heard (see CLAUDE.md), so it is all of:
///
/// - a double flare of coloured light at its head (two quick "blinks") that
///   lights the tree trunks and the ground around it through the foliage;
/// - a thin column of glowing sparks that climbs above the canopy, readable
///   even when the body is fully behind a trunk;
/// - a spatialised bone rattle (a low growl for the Coronado), so the player
///   can turn toward it by ear.
///
/// A skeleton only tells while it is hidden: outside the player's view cone,
/// behind something solid, or simply far off in the dark. Once it is close and
/// in plain view it has nothing to announce. The Coronado tells regardless.
///
/// Colour tells the families apart at a glance: pale green-white for the
/// Caminante, amber for the Lanzahuesos, red for the Cazador and a deeper red
/// for the Coronado. The Coronado's tells also speed up and grow stronger as it
/// closes in (see <see cref="ConfigureForBoss"/>), so its approach is felt even
/// while it keeps to the trees.
///
/// Added at runtime by Skeleton, BoneThrower and Coronado, so no prefab or
/// scene wiring is needed. The flare light never casts shadows: only the fire
/// light may (see CLAUDE.md).
/// </summary>
[DisallowMultipleComponent]
public class EnemyTell : MonoBehaviour
{
    static readonly Color k_CaminanteColor = new Color(0.65f, 1f, 0.75f);
    static readonly Color k_LanzahuesosColor = new Color(1f, 0.6f, 0.15f);
    static readonly Color k_CazadorColor = new Color(1f, 0.1f, 0.08f);
    static readonly Color k_CoronadoColor = new Color(0.9f, 0.02f, 0.05f);

    static Material s_SparkMaterial;

    [Tooltip("Seconds between tells, picked at random in this range (far away, for the Coronado).")]
    [SerializeField] Vector2 m_Interval = new Vector2(5f, 9f);
    [Tooltip("Coronado only: interval range once it is at its closest (m_NearDistance).")]
    [SerializeField] Vector2 m_NearInterval = new Vector2(2f, 3.5f);
    [Tooltip("Height of the head above the pivot, where the flare and sparks start.")]
    [SerializeField] float m_HeadHeight = 1.6f;
    [SerializeField] float m_FlareIntensity = 4f;
    [SerializeField] float m_FlareRange = 5f;
    [Tooltip("How high the spark column climbs above the head, in metres. Tall enough to clear the canopy.")]
    [SerializeField] float m_SparkHeight = 7f;
    [SerializeField] float m_SoundVolume = 0.8f;
    [Tooltip("Closer than this to the player's eyes AND in view: already obvious, no tell.")]
    [SerializeField] float m_PlainSightDistance = 6f;
    [Tooltip("Half of this angle, around the gaze, counts as 'in view'.")]
    [SerializeField] float m_ViewConeAngle = 70f;

    Skeleton m_Skeleton;
    BoneThrower m_BoneThrower;
    Coronado m_Coronado;
    Transform m_Camera;
    Light m_Flare;
    float m_NextTellTime;
    bool m_IsBoss;
    float m_BossFarDistance = 15f;
    float m_BossNearDistance = 3f;

    /// <summary>
    /// Coronado settings: deep red, stronger flare, a growl instead of a
    /// rattle, and tells that come faster the closer it is (from
    /// <paramref name="farDistance"/> down to <paramref name="nearDistance"/>).
    /// </summary>
    public void ConfigureForBoss(float headHeight, float farDistance, float nearDistance)
    {
        m_IsBoss = true;
        m_HeadHeight = headHeight;
        m_BossFarDistance = farDistance;
        m_BossNearDistance = nearDistance;
        m_Interval = new Vector2(6f, 9f);
        m_FlareIntensity = 6f;
        m_FlareRange = 7f;
        m_SparkHeight = 9f;
        m_SoundVolume = 1f;
        ScheduleNext();
    }

    void Awake()
    {
        m_Skeleton = GetComponent<Skeleton>();
        m_BoneThrower = GetComponent<BoneThrower>();
        m_Coronado = GetComponent<Coronado>();
    }

    void OnEnable()
    {
        // First tell comes after a full interval, never the instant it spawns.
        ScheduleNext();
    }

    void Update()
    {
        if (Time.time < m_NextTellTime)
            return;

        if (!CanTell())
        {
            ScheduleNext();
            return;
        }

        if (m_Camera == null && Camera.main != null)
            m_Camera = Camera.main.transform;
        if (m_Camera == null)
            return;

        // The Coronado announces its approach whether it is being watched or
        // not: the flare in its hood is part of the dread, not a hint.
        if (m_IsBoss || IsHidden())
            StartCoroutine(TellRoutine());

        ScheduleNext();
    }

    bool CanTell()
    {
        if (m_Skeleton != null)
            return m_Skeleton.IsAlive && !m_Skeleton.IsRetreating;
        if (m_BoneThrower != null)
            return m_BoneThrower.IsAlive && !m_BoneThrower.IsRetreating;
        if (m_Coronado != null)
            return !m_Coronado.IsDefeated;
        return true;
    }

    /// <summary>Out of the view cone, behind something solid, or far off in the dark.</summary>
    bool IsHidden()
    {
        Vector3 head = Head;
        Vector3 toHead = head - m_Camera.position;
        float distance = toHead.magnitude;
        if (distance < 0.01f)
            return false;

        if (Vector3.Angle(m_Camera.forward, toHead) > m_ViewConeAngle * 0.5f)
            return true;
        if (distance > m_PlainSightDistance)
            return true;

        return Physics.Raycast(m_Camera.position, toHead / distance, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore)
               && !hit.transform.IsChildOf(transform);
    }

    Vector3 Head => transform.position + Vector3.up * m_HeadHeight;

    Color TellColor
    {
        get
        {
            if (m_IsBoss)
                return k_CoronadoColor;
            if (m_BoneThrower != null)
                return k_LanzahuesosColor;
            if (m_Skeleton != null && m_Skeleton.IsHunter)
                return k_CazadorColor;
            return k_CaminanteColor;
        }
    }

    /// <summary>0 when the Coronado is at its far ring, 1 when it is right on the player.</summary>
    float BossCloseness()
    {
        if (!m_IsBoss || m_Camera == null)
            return 0f;

        Vector3 flat = transform.position - m_Camera.position;
        flat.y = 0f;
        return Mathf.InverseLerp(m_BossFarDistance, m_BossNearDistance, flat.magnitude);
    }

    void ScheduleNext()
    {
        Vector2 range = m_Interval;
        if (m_IsBoss)
        {
            float closeness = BossCloseness();
            range = Vector2.Lerp(m_Interval, m_NearInterval, closeness);
        }

        m_NextTellTime = Time.time + Random.Range(range.x, range.y);
    }

    IEnumerator TellRoutine()
    {
        float closeness = BossCloseness();
        float strength = m_IsBoss ? Mathf.Lerp(1f, 1.6f, closeness) : 1f;
        Color color = TellColor;

        PlaySound(closeness);
        EmitSparks(color, strength);

        var flare = GetFlare();
        flare.color = color;
        flare.range = m_FlareRange * strength;

        // Two quick blinks: reads as eyes opening in the dark rather than a lamp.
        yield return Pulse(flare, m_FlareIntensity * strength, 0.07f, 0.25f);
        yield return new WaitForSeconds(0.12f);
        yield return Pulse(flare, m_FlareIntensity * strength, 0.07f, 0.55f);

        flare.enabled = false;
    }

    static IEnumerator Pulse(Light light, float peak, float rise, float fall)
    {
        light.enabled = true;
        float t = 0f;
        while (t < rise)
        {
            t += Time.deltaTime;
            light.intensity = peak * Mathf.Clamp01(t / rise);
            yield return null;
        }

        t = 0f;
        while (t < fall)
        {
            t += Time.deltaTime;
            float n = 1f - Mathf.Clamp01(t / fall);
            light.intensity = peak * n * n;
            yield return null;
        }

        light.intensity = 0f;
    }

    Light GetFlare()
    {
        if (m_Flare != null)
            return m_Flare;

        var go = new GameObject("Tell Flare");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * m_HeadHeight;
        m_Flare = go.AddComponent<Light>();
        m_Flare.type = LightType.Point;
        m_Flare.shadows = LightShadows.None;
        m_Flare.intensity = 0f;
        m_Flare.enabled = false;
        return m_Flare;
    }

    void PlaySound(float closeness)
    {
        if (m_IsBoss)
        {
            // A low growl: the scream pitched far down. Louder as it closes in.
            ProceduralSfx.PlayAt(ProceduralSfx.CoronadoScream, Head, Mathf.Lerp(0.45f, 0.9f, closeness), 0.5f);
            return;
        }

        ProceduralSfx.PlayAt(ProceduralSfx.BoneRattle, Head, m_SoundVolume, Random.Range(0.9f, 1.1f));
    }

    /// <summary>A thin, fast column of glowing sparks that climbs from the head up past the tree tops.</summary>
    void EmitSparks(Color color, float strength)
    {
        var go = new GameObject("Tell Sparks");
        go.transform.position = Head;
        go.transform.rotation = Quaternion.LookRotation(Vector3.up);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        float lifetime = 1.6f;
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(m_SparkHeight / lifetime * 0.8f, m_SparkHeight / lifetime * 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f * strength, 0.16f * strength);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(24 * strength)), new ParticleSystem.Burst(0.19f, (short)(16 * strength)) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 6f;
        shape.radius = 0.1f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.25f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var material = GetSparkMaterial();
        if (material != null)
            renderer.sharedMaterial = material;

        ps.Play();
        go.AddComponent<AutoDestroyAfter>().Lifetime = main.duration + lifetime + 0.2f;
    }

    static Material GetSparkMaterial()
    {
        if (s_SparkMaterial != null)
            return s_SparkMaterial;

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        if (shader == null)
            return null;

        s_SparkMaterial = new Material(shader) { name = "M_TellSparks (Runtime)" };
        if (s_SparkMaterial.HasProperty("_BaseColor"))
            s_SparkMaterial.SetColor("_BaseColor", Color.white);
        return s_SparkMaterial;
    }
}

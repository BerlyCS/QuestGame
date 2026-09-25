using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Occasional, unscripted Coronado "apparitions" (see JEFE_FINAL.md): a silent,
/// inert copy of the boss flickers into being the moment the player grabs
/// something, right on their line of sight at face height (see
/// <see cref="ResolveFaceTarget"/>), leans into their face, then vanishes in a
/// burst of black smoke. Placing it along the REAL gaze - not out in front on
/// the floor - is the whole trick: mid-grab the player is looking down at the
/// object in their hands, so the face has to appear exactly there to be seen at
/// all. It is pure dread, not combat - it never attacks, never drains the fire
/// and never touches the real <see cref="Coronado"/>, which stays parked until
/// <see cref="BossIntro"/> calls <see cref="Coronado.Appear"/>.
///
/// The wordless design ("the scare is the message") makes the trigger
/// deliberately unreliable: a low chance per grab, with a guaranteed minimum
/// so a short session still gets at least <see cref="m_GuaranteedApparitions"/>
/// of them. Every <see cref="Grabbable"/> is watched, not just the logs, so it
/// reads as the world being haunted rather than a scripted pickup telling the
/// player what to do.
/// </summary>
[DisallowMultipleComponent]
public class GrabScareSystem : MonoBehaviour
{
    [Header("Rareza")]
    [Tooltip("Probabilidad (0-1) de que un agarre dispare una aparición, una vez agotadas las garantizadas. " +
             "Baja a propósito: si sale demasiado, deja de dar miedo.")]
    [Range(0f, 1f)]
    [SerializeField] float m_ChancePerGrab = 0.15f;

    [Tooltip("Apariciones garantizadas por partida, aunque la probabilidad no salga nunca.")]
    [SerializeField] int m_GuaranteedApparitions = 3;

    [Tooltip("Agarres necesarios desde la última aparición antes de forzar otra de las garantizadas.")]
    [SerializeField] int m_MinGrabsBetweenApparitions = 3;

    [Tooltip("Segundos mínimos entre dos apariciones, para que nunca se solapen.")]
    [SerializeField] float m_CooldownSeconds = 20f;

    [Header("Aparición")]
    [Tooltip("Distancia a la que aparece la cara desde la cámara, medida a lo largo de la mirada REAL " +
             "(con inclinación): si el jugador mira abajo, al objeto que agarra, la cara aparece justo ahí. " +
             "Corta a propósito: tiene que llenar el campo de visión.")]
    [SerializeField] float m_ApparitionDistance = 2f;

    [Tooltip("Distancia a la que termina el acercamiento, ya sobre la cara. Nunca queda más cerca que esto.")]
    [SerializeField] float m_LeanInEndDistance = 0.7f;

    [Tooltip("Cuánto permanece visible antes de desvanecerse en humo negro. Durante ese tiempo se acerca.")]
    [SerializeField] float m_ApparitionLinger = 0.6f;

    [Tooltip("Volumen del grito de la aparición.")]
    [Range(0f, 1f)]
    [SerializeField] float m_ScareVolume = 0.6f;

    [Header("Detección")]
    [Tooltip("Cada cuántos segundos se buscan agarrables nuevos: los troncos del LogSpawner nacen en runtime " +
             "y no existirían en el barrido inicial.")]
    [SerializeField] float m_RescanInterval = 1.5f;

    [Header("Cuerpo (placeholder, mismo aspecto que el Coronado real)")]
    [SerializeField] float m_Height = 2.4f;
    [SerializeField] float m_Radius = 0.35f;

    readonly HashSet<Grabbable> m_Tracked = new HashSet<Grabbable>();

    Transform m_PlayerCamera;
    float m_NextRescanTime;
    float m_NextAllowedTime;
    int m_GrabsSinceApparition;
    int m_ApparitionsPlayed;
    bool m_Playing;

    Material m_BodyMaterial;
    Material m_EyeMaterial;
    Material m_SmokeMaterial;

    void Start()
    {
        ResolveCamera();
        Scan();
    }

    void OnDestroy()
    {
        foreach (var grabbable in m_Tracked)
        {
            if (grabbable != null)
                grabbable.WhenPointerEventRaised -= HandlePointerEvent;
        }
        m_Tracked.Clear();
    }

    void Update()
    {
        ResolveCamera();

        if (Time.time < m_NextRescanTime)
            return;

        m_NextRescanTime = Time.time + Mathf.Max(0.25f, m_RescanInterval);
        Scan();
    }

    void ResolveCamera()
    {
        if (m_PlayerCamera != null)
            return;

        if (Camera.main != null)
        {
            m_PlayerCamera = Camera.main.transform;
            return;
        }

        // XR rigs do not always tag the eye camera "MainCamera"; fall back to
        // the same "highest depth enabled camera" rule CoronadoBuilder uses.
        Camera best = null;
        foreach (var candidate in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (candidate == null || !candidate.enabled)
                continue;
            if (best == null || candidate.depth > best.depth)
                best = candidate;
        }
        if (best != null)
            m_PlayerCamera = best.transform;
    }

    /// <summary>
    /// Subscribes to every grabbable currently in the scene, skipping the ones
    /// already tracked. Idempotent, so the periodic rescan is cheap and cannot
    /// double-subscribe.
    /// </summary>
    void Scan()
    {
        foreach (var grabbable in Object.FindObjectsByType<Grabbable>(FindObjectsSortMode.None))
        {
            if (grabbable == null)
                continue;
            if (m_Tracked.Add(grabbable))
                grabbable.WhenPointerEventRaised += HandlePointerEvent;
        }
    }

    void HandlePointerEvent(PointerEvent evt)
    {
        if (evt.Type != PointerEventType.Select)
            return;

        m_GrabsSinceApparition++;

        if (m_Playing || Time.time < m_NextAllowedTime)
            return;

        bool guaranteed = m_ApparitionsPlayed < m_GuaranteedApparitions
            && m_GrabsSinceApparition >= m_MinGrabsBetweenApparitions;

        if (!guaranteed && Random.value >= m_ChancePerGrab)
            return;

        Trigger();
    }

    void Trigger()
    {
        m_ApparitionsPlayed++;
        m_GrabsSinceApparition = 0;
        m_Playing = true;
        m_NextAllowedTime = Time.time + m_CooldownSeconds;
        StartCoroutine(ApparitionRoutine());
    }

    IEnumerator ApparitionRoutine()
    {
        Vector3 faceTarget = ResolveFaceTarget();
        GameObject ghost = BuildGhost(faceTarget);

        Transform ghostTransform = ghost != null ? ghost.transform : null;
        Vector3 startRoot = ghostTransform != null ? ghostTransform.position : Vector3.zero;
        Vector3 leanRoot = ResolveLeanRoot(faceTarget);

        ProceduralSfx.PlayAt(ProceduralSfx.CoronadoScream, faceTarget, m_ScareVolume);

        // Close the gap while it lingers: the scare is the face rushing in, not
        // a static cutout. Only the ghost moves - the camera never does.
        float linger = Mathf.Max(0.05f, m_ApparitionLinger);
        float t = 0f;
        while (t < linger)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / linger);
            n *= n; // arranca despacio, llega de golpe a la cara
            if (ghostTransform != null)
                ghostTransform.position = Vector3.Lerp(startRoot, leanRoot, n);
            yield return null;
        }

        SpawnBlackBurst(faceTarget);
        Destroy(ghost);

        m_Playing = false;
    }

    /// <summary>Altura local de los ojos (la "cara") dentro del cuerpo, sobre su base.</summary>
    float EyeLocalHeight => m_Height - 0.18f;

    /// <summary>
    /// Dónde va la cara: a lo largo de la dirección REAL de la mirada (con
    /// inclinación, nunca proyectada al suelo). Si el jugador está mirando
    /// abajo al objeto que agarra, la cara aparece justo ahí, dentro de su
    /// campo de visión - que es lo que hace que el susto funcione.
    /// </summary>
    Vector3 ResolveFaceTarget()
    {
        Vector3 forward = m_PlayerCamera != null ? m_PlayerCamera.forward : Vector3.forward;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 origin = m_PlayerCamera != null ? m_PlayerCamera.position : Vector3.up * m_Height;
        return origin + forward * m_ApparitionDistance;
    }

    /// <summary>
    /// Posición de la raíz al final del acercamiento: mismo rumbo desde la
    /// cámara, a <see cref="m_LeanInEndDistance"/>. Se devuelve la raíz (cara
    /// menos la altura de los ojos) para mover el fantasma entero sin deformarlo.
    /// </summary>
    Vector3 ResolveLeanRoot(Vector3 faceTarget)
    {
        Vector3 toFace = m_PlayerCamera != null ? faceTarget - m_PlayerCamera.position : faceTarget;
        if (toFace.sqrMagnitude < 0.0001f)
            toFace = m_PlayerCamera != null ? m_PlayerCamera.forward : Vector3.forward;

        Vector3 endFace = m_PlayerCamera != null
            ? m_PlayerCamera.position + toFace.normalized * Mathf.Min(m_LeanInEndDistance, m_ApparitionDistance)
            : faceTarget;

        return endFace - Vector3.up * EyeLocalHeight;
    }

    GameObject BuildGhost(Vector3 faceTarget)
    {
        var root = new GameObject("Coronado Apparition");
        // La raíz baja lo que miden los ojos, así la cara queda clavada en
        // faceTarget (a la altura y dirección de la mirada), no en el suelo.
        root.transform.position = faceTarget - Vector3.up * EyeLocalHeight;

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, m_Height * 0.5f, 0f);
        body.transform.localScale = new Vector3(m_Radius * 2f, m_Height * 0.5f, m_Radius * 2f);

        var bodyRenderer = body.GetComponent<Renderer>();
        bodyRenderer.sharedMaterial = GetBodyMaterial();
        bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bodyRenderer.receiveShadows = false;

        BuildEye(root.transform, new Vector3(-0.09f, EyeLocalHeight, m_Radius * 0.85f));
        BuildEye(root.transform, new Vector3(0.09f, EyeLocalHeight, m_Radius * 0.85f));

        if (m_PlayerCamera != null)
        {
            Vector3 towardPlayer = Vector3.ProjectOnPlane(m_PlayerCamera.position - root.transform.position, Vector3.up);
            if (towardPlayer.sqrMagnitude > 0.0001f)
                root.transform.rotation = Quaternion.LookRotation(towardPlayer.normalized, Vector3.up);
        }

        return root;
    }

    void BuildEye(Transform parent, Vector3 localPosition)
    {
        var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eye.name = "Eye";
        Destroy(eye.GetComponent<Collider>());
        eye.transform.SetParent(parent, false);
        eye.transform.localPosition = localPosition;
        eye.transform.localScale = Vector3.one * 0.06f;

        var renderer = eye.GetComponent<Renderer>();
        renderer.sharedMaterial = GetEyeMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        // A real light, not just emissive, is what makes the two points read at
        // distance in near-total darkness (same reasoning as CoronadoBuilder).
        var light = eye.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.9f, 0.05f, 0.05f);
        light.range = 4f;
        light.intensity = 3f;
        light.shadows = LightShadows.None;
    }

    Material GetBodyMaterial()
    {
        if (m_BodyMaterial != null)
            return m_BodyMaterial;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null)
            return null;

        var color = new Color(0.05f, 0.05f, 0.06f);
        m_BodyMaterial = new Material(shader) { name = "M_CoronadoApparition_Body (Runtime)" };
        if (m_BodyMaterial.HasProperty("_BaseColor"))
            m_BodyMaterial.SetColor("_BaseColor", color);
        m_BodyMaterial.color = color;
        return m_BodyMaterial;
    }

    Material GetEyeMaterial()
    {
        if (m_EyeMaterial != null)
            return m_EyeMaterial;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null)
            return null;

        m_EyeMaterial = new Material(shader) { name = "M_CoronadoApparition_Eyes (Runtime)" };
        m_EyeMaterial.EnableKeyword("_EMISSION");
        m_EyeMaterial.SetColor("_BaseColor", new Color(0.4f, 0f, 0f));
        m_EyeMaterial.SetColor("_EmissionColor", new Color(3f, 0.03f, 0.03f));
        m_EyeMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        return m_EyeMaterial;
    }

    Material GetSmokeMaterial()
    {
        if (m_SmokeMaterial != null)
            return m_SmokeMaterial;

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Particles/Standard Unlit")
                     ?? Shader.Find("Sprites/Default");
        if (shader == null)
            return null;

        m_SmokeMaterial = new Material(shader) { name = "M_CoronadoApparition_Smoke (Runtime)" };
        if (m_SmokeMaterial.HasProperty("_BaseColor"))
            m_SmokeMaterial.SetColor("_BaseColor", Color.white);
        m_SmokeMaterial.color = Color.white;
        return m_SmokeMaterial;
    }

    /// <summary>
    /// One-shot puff of fully black smoke that hides the moment the ghost
    /// winks out, matching <see cref="SkeletonDeathFx"/> but without the violet
    /// so it reads as the Coronado swallowing the light, not magic.
    /// </summary>
    void SpawnBlackBurst(Vector3 position)
    {
        var go = new GameObject("Coronado Apparition Smoke");
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.9f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.28f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0f, 0f, 0f, 0.95f),
            new Color(0.05f, 0.05f, 0.05f, 0.9f));
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.4f;

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.orbitalY = new ParticleSystem.MinMaxCurve(2f);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.2f)));

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = GetSmokeMaterial();

        ps.Play();

        float lifetime = ps.main.duration + ps.main.startLifetime.constantMax + 0.3f;
        go.AddComponent<AutoDestroyAfter>().Lifetime = Mathf.Max(lifetime, 1.2f);
    }
}

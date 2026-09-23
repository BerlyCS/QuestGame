using Oculus.Interaction.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the Coronado placeholder in Game.unity: a tall capsule with two
/// small emissive, glowing spheres for eyes, nothing else visible (see
/// JEFE_FINAL.md - only the gaze mechanic, the entrance and the three
/// localization channels exist so far). Also builds two of those channels as
/// scene objects that need no runtime logic of their own: a continuous 3D
/// breathing loop (JEFE_FINAL.md 6.1) and a soft, dark, transparent sphere
/// that darkens the clearing in a 2 m radius around it (6.3) - a real
/// negative-intensity light was tried first, but Unity clamps Light.intensity
/// to >= 0, so it never actually did anything. The remaining channel - the
/// flame leaning away from it - lives in CampfireFuel/Coronado instead, since
/// it needs the fire's own transform.
///
/// Starts inactive and off in a corner; wires DebugKeys so 'B' activates and
/// places it 15 m from the player to test the freeze/advance rule.
///
/// Also builds and wires the Boss Intro (see <see cref="BossIntro"/>), which
/// fires the Coronado's real entrance at the middle of the night and is what
/// actually calls <see cref="Coronado.Appear"/>; DebugKeys' 'J' key fires it
/// on demand.
///
/// Idempotent: reuses the existing objects, parts and materials on a second
/// run instead of duplicating them.
/// </summary>
public static class CoronadoBuilder
{
    const string k_ScenePath = "Assets/Scenes/Game.unity";
    const string k_MaterialFolder = "Assets/Materials/Game";
    const float k_Height = 2.4f;
    const float k_Radius = 0.35f;
    const float k_EyeLightRange = 4f;
    const float k_EyeLightIntensity = 3f;
    const float k_LightAbsorbRadius = 2f;
    const float k_LightAbsorbAlpha = 0.55f;
    const float k_BreathVolume = 0.8f;
    const float k_BreathMinDistance = 1.5f;
    const float k_BreathMaxDistance = 30f;
    static readonly Color k_BodyColor = new Color(0.05f, 0.05f, 0.06f);
    static readonly Color k_EyeColor = new Color(0.9f, 0.05f, 0.05f);

    [MenuItem("Tools/Game/Build Coronado")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);

        // The Coronado starts inactive (see below), and GameObject.Find skips
        // inactive objects, so re-running this had been finding nothing and
        // creating a second one every time. Searching by component and
        // including inactive objects is what makes this idempotent.
        var coronado = Object.FindAnyObjectByType<Coronado>(FindObjectsInactive.Include);
        bool created = coronado == null;
        GameObject root;
        if (created)
        {
            root = new GameObject("Coronado");
            Undo.RegisterCreatedObjectUndo(root, "Create Coronado");
            coronado = root.AddComponent<Coronado>();
        }
        else
        {
            root = coronado.gameObject;
        }

        BuildBody(root);
        BuildEyes(root);
        BuildBreath(root, coronado);
        BuildLightAbsorb(root);

        // Parked out of the way until DebugKeys' 'B' key (or the real boss
        // trigger, later) activates it and moves it into place.
        root.transform.position = new Vector3(0f, 0f, -50f);
        root.SetActive(false);

        var camera = FindCamera();
        if (camera != null)
            SetRef(coronado, "m_PlayerCamera", camera.transform);

        WireHands(coronado);

        var campfire = Object.FindAnyObjectByType<CampfireFuel>(FindObjectsInactive.Include);
        if (campfire != null)
            SetRef(coronado, "m_Campfire", campfire);

        var gameManager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gameManager != null)
            SetRef(coronado, "m_GameManager", gameManager);

        var debug = Object.FindAnyObjectByType<DebugKeys>(FindObjectsInactive.Include);
        if (debug != null)
            SetRef(debug, "m_Coronado", coronado);
        else
            Debug.LogWarning("[CoronadoBuilder] No DebugKeys found in the scene; 'B' key was not wired.");

        EditorUtility.SetDirty(root);

        BuildBossIntro(coronado, debug);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[CoronadoBuilder] Coronado {(created ? "created" : "updated")} in Game.unity.");
    }

    /// <summary>
    /// Creates (or reuses) the Boss Intro under "Game Systems" and wires it to
    /// the GameManager, the campfire and the Coronado, plus DebugKeys' 'J' key.
    /// </summary>
    static void BuildBossIntro(Coronado coronado, DebugKeys debug)
    {
        var gameManager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        var campfire = Object.FindAnyObjectByType<CampfireFuel>(FindObjectsInactive.Include);
        if (gameManager == null || campfire == null)
        {
            Debug.LogWarning("[CoronadoBuilder] No GameManager/CampfireFuel found; Boss Intro was not wired.");
            return;
        }

        var bossIntro = Object.FindAnyObjectByType<BossIntro>(FindObjectsInactive.Include);
        if (bossIntro == null)
        {
            var systems = GameObject.Find("Game Systems");
            if (systems == null)
            {
                systems = new GameObject("Game Systems");
                Undo.RegisterCreatedObjectUndo(systems, "Create Game Systems");
            }

            var go = new GameObject("Boss Intro");
            go.transform.SetParent(systems.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Create Boss Intro");
            bossIntro = go.AddComponent<BossIntro>();
        }

        SetRef(bossIntro, "m_GameManager", gameManager);
        SetRef(bossIntro, "m_Campfire", campfire);
        SetRef(bossIntro, "m_Coronado", coronado);
        EditorUtility.SetDirty(bossIntro);

        if (debug != null)
            SetRef(debug, "m_BossIntro", bossIntro);
    }

    static void BuildBody(GameObject root)
    {
        var bodyGo = FindOrCreatePrimitive(root.transform, "Body", PrimitiveType.Capsule);
        bodyGo.transform.localPosition = new Vector3(0f, k_Height * 0.5f, 0f);
        bodyGo.transform.localScale = new Vector3(k_Radius * 2f, k_Height * 0.5f, k_Radius * 2f);
        bodyGo.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("M_Coronado_Body", k_BodyColor, null);

        var capsule = root.GetComponent<CapsuleCollider>();
        if (capsule == null)
            capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, k_Height * 0.5f, 0f);
        capsule.height = k_Height;
        capsule.radius = k_Radius;
    }

    static void BuildEyes(GameObject root)
    {
        var eyeMaterial = GetOrCreateMaterial("M_Coronado_Eyes", k_EyeColor, k_EyeColor * 3f);
        float eyeHeight = k_Height - 0.18f;
        float eyeForward = k_Radius * 0.85f;

        BuildEye(root, "Eye_L", new Vector3(-0.09f, eyeHeight, eyeForward), eyeMaterial);
        BuildEye(root, "Eye_R", new Vector3(0.09f, eyeHeight, eyeForward), eyeMaterial);
    }

    static void BuildEye(GameObject root, string name, Vector3 localPosition, Material material)
    {
        var eye = FindOrCreatePrimitive(root.transform, name, PrimitiveType.Sphere);
        eye.transform.localPosition = localPosition;
        eye.transform.localScale = Vector3.one * 0.06f;
        eye.GetComponent<Renderer>().sharedMaterial = material;

        // A real light, not just an emissive material, is what makes "dos
        // puntos de luz se encienden" read from 15 m away in near-total
        // darkness (see CLAUDE.md: no haptics, so feedback has to be visual
        // or sonic, and more exaggerated than seems necessary).
        var light = eye.GetComponent<Light>();
        if (light == null)
            light = eye.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = k_EyeColor;
        light.range = k_EyeLightRange;
        light.intensity = k_EyeLightIntensity;
        light.shadows = LightShadows.None;
    }

    /// <summary>
    /// Canal 1 (JEFE_FINAL.md 6.1): a continuous, low breathing loop on a 3D
    /// AudioSource with logarithmic rolloff - the main localization channel,
    /// the one the player should be able to turn toward by ear alone. Being a
    /// child transform, it follows every reposition automatically.
    ///
    /// The clip itself is NOT set here: AudioClip.Create() clips are runtime-only
    /// and cannot survive a scene save/reload (same reason CampfireFuel builds
    /// its crackle clip in Awake() instead of here). Coronado wires it in Start()
    /// via the m_BreathAudio reference set below.
    /// </summary>
    static void BuildBreath(GameObject root, Coronado coronado)
    {
        var breathGo = FindOrCreateChild(root.transform, "Breath");
        breathGo.transform.localPosition = new Vector3(0f, k_Height * 0.5f, 0f);

        var audio = breathGo.GetComponent<AudioSource>();
        if (audio == null)
            audio = breathGo.AddComponent<AudioSource>();
        audio.loop = true;
        audio.playOnAwake = false;
        audio.spatialBlend = 1f;
        audio.rolloffMode = AudioRolloffMode.Logarithmic;
        audio.minDistance = k_BreathMinDistance;
        audio.maxDistance = k_BreathMaxDistance;
        audio.volume = k_BreathVolume;

        SetRef(coronado, "m_BreathAudio", audio);
    }

    /// <summary>
    /// Canal 3 (JEFE_FINAL.md 6.3): a soft, dark, transparent sphere (2 m
    /// radius) that visibly dims the ground, fog and trees behind and around
    /// it. It does NOT hide the Coronado itself - its own opaque body already
    /// wrote depth in the opaque pass, so the sphere's near half is culled by
    /// the depth test wherever the body is in front of it, and only the
    /// clearing around him darkens (see CLAUDE.md 9: "no lo hagas invisible").
    /// </summary>
    static void BuildLightAbsorb(GameObject root)
    {
        var absorbGo = FindOrCreateChild(root.transform, "Light Absorb");
        absorbGo.transform.localPosition = new Vector3(0f, k_Height * 0.5f, 0f);
        absorbGo.transform.localScale = Vector3.one * (k_LightAbsorbRadius * 2f);

        // An earlier revision used a Light component here; remove it if present.
        var oldLight = absorbGo.GetComponent<Light>();
        if (oldLight != null)
            Object.DestroyImmediate(oldLight);

        var filter = absorbGo.GetComponent<MeshFilter>();
        if (filter == null)
            filter = absorbGo.AddComponent<MeshFilter>();
        if (filter.sharedMesh == null)
            filter.sharedMesh = GetSpherePrimitiveMesh();

        var renderer = absorbGo.GetComponent<MeshRenderer>();
        if (renderer == null)
            renderer = absorbGo.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = GetOrCreateDarknessMaterial();

        var collider = absorbGo.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);
    }

    /// <summary>Borrows the built-in sphere mesh without leaving a stray primitive behind.</summary>
    static Mesh GetSpherePrimitiveMesh()
    {
        var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(temp);
        return mesh;
    }

    /// <summary>
    /// Same transparent-unlit recipe as M_DamageVignette.mat, the project's
    /// other working transparent overlay: Universal Render Pipeline/Unlit set
    /// to alpha-blended Transparent, no depth write, no shadow casting.
    /// </summary>
    static Material GetOrCreateDarknessMaterial()
    {
        const string name = "M_Coronado_Darkness";
        var path = $"{k_MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
            return material;

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
        material = new Material(shader) { name = name };

        material.SetFloat("_Surface", 1f); // Transparent
        material.SetFloat("_Blend", 0f); // Alpha
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)CullMode.Back);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = 3000;

        var darkness = new Color(0f, 0f, 0f, k_LightAbsorbAlpha);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", darkness);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", darkness);

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>Reuses the child by name if it is already there instead of duplicating it.</summary>
    static GameObject FindOrCreatePrimitive(Transform parent, string name, PrimitiveType type)
    {
        var existing = parent.Find(name);
        if (existing != null)
            return existing.gameObject;

        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    /// <summary>Same as <see cref="FindOrCreatePrimitive"/> but for a bare, non-visual child (audio, lights).</summary>
    static GameObject FindOrCreateChild(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null)
            return existing.gameObject;

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    static Material GetOrCreateMaterial(string name, Color color, Color? emission)
    {
        var path = $"{k_MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
            return material;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        material = new Material(shader) { name = name };
        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (emission.HasValue && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>
    /// Wires the raw hand-tracking data sources (not the Interaction SDK's grab
    /// interactors, nor its derived "synthetic hand" visuals) so the push
    /// defense reads real palm velocity - see Coronado.SampleHandVelocity.
    /// </summary>
    static void WireHands(Coronado coronado)
    {
        var rig = GameObject.Find("OVRCameraRig");
        if (rig == null)
        {
            Debug.LogWarning("[CoronadoBuilder] No OVRCameraRig found; hand push detection was not wired.");
            return;
        }

        var leftSource = FindExactName(rig.transform, "OVRHandDataSourceLeft");
        var rightSource = FindExactName(rig.transform, "OVRHandDataSourceRight");

        if (leftSource != null)
            SetRef(coronado, "m_LeftHand", leftSource.GetComponent<Hand>());
        if (rightSource != null)
            SetRef(coronado, "m_RightHand", rightSource.GetComponent<Hand>());

        if (leftSource == null || rightSource == null)
            Debug.LogWarning("[CoronadoBuilder] OVRHandDataSourceLeft/Right not found under OVRCameraRig; hand push detection was not fully wired.");
    }

    static Transform FindExactName(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name)
                return t;
        return null;
    }

    static Camera FindCamera()
    {
        Camera best = null;
        foreach (var candidate in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
        {
            if (candidate == null || !candidate.enabled)
                continue;
            if (best == null || candidate.depth > best.depth)
                best = candidate;
        }

        return best;
    }

    static void SetRef(Object target, string property, Object value)
    {
        var serialized = new SerializedObject(target);
        var field = serialized.FindProperty(property);
        if (field == null)
        {
            Debug.LogWarning($"[CoronadoBuilder] {target.GetType().Name} has no serialized field '{property}'.");
            return;
        }

        field.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}

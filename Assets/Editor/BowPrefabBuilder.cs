using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the ported OOT bow, arrow and quiver prefabs from the imported
/// models/audio using the Meta Interaction SDK that the rest of the project
/// uses. Run from <b>Tools &gt; QuestGame &gt; Bow &gt; Build Bow Prefabs</b>.
///
/// The generated prefabs are written to Assets/Prefabs/Gameplay/Bow and are
/// safe to re-generate at any time.
/// </summary>
public static class BowPrefabBuilder
{
    const string k_PrefabFolder = "Assets/Prefabs/Gameplay/Bow";
    const string k_ArrowPrefabPath = k_PrefabFolder + "/Arrow.prefab";
    const string k_BowPrefabPath = k_PrefabFolder + "/Bow.prefab";
    const string k_QuiverPrefabPath = k_PrefabFolder + "/Quiver.prefab";

    const string k_BowModelPath = "Assets/Models/Bow/bow.dae";
    const string k_ArrowShaftModelPath = "Assets/Models/Bow/arrow_shaft.dae";
    const string k_ArrowTipModelPath = "Assets/Models/Bow/arrow_tip.dae";

    const string k_BowMaterialPath = "Assets/Materials/Bow/Bow.mat";
    const string k_ArrowMaterialPath = "Assets/Materials/Bow/Arrow.mat";

    const string k_BowTwangClipPath = "Assets/Audio/Bow/OOT_Bow_Twang.wav";
    const string k_BowPullClipPath = "Assets/Audio/Bow/OOT_Bow_Pull.wav";
    const string k_ArrowHitClipPath = "Assets/Audio/Bow/OOT_Arrow_Hit_Cut.wav";
    const string k_ArrowShootClipPath = "Assets/Audio/Bow/OOT_Arrow_Shoot_Cut.wav";

    [MenuItem("Tools/QuestGame/Bow/Build Bow Prefabs")]
    public static void BuildAll()
    {
        EnsureAssetFolder(k_PrefabFolder);

        GameObject arrowPrefab = BuildArrowPrefab();
        BuildBowPrefab();
        BuildQuiverPrefab(arrowPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[BowPrefabBuilder] Built Bow, Arrow and Quiver prefabs in " + k_PrefabFolder);
    }

    // ------------------------------------------------------------------
    // Arrow
    // ------------------------------------------------------------------
    static GameObject BuildArrowPrefab()
    {
        GameObject root = new GameObject("Arrow");
        root.layer = 2; // Ignore Raycast, matches the original OOT arrow.

        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = 1f;
        body.useGravity = true;
        body.isKinematic = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.direction = 2; // Z
        collider.radius = 0.017f;
        collider.height = 0.7f;

        Material arrowMaterial = LoadAsset<Material>(k_ArrowMaterialPath);

        // The original prefab flattens the visual by -45 degrees and then -90
        // degrees again inside "Mesh"; reproducing that keeps the model upright.
        GameObject model = CreateEmpty("Model", root.transform, Vector3.zero, Quaternion.Euler(-45f, 0f, 0f), Vector3.one);
        GameObject mesh = CreateEmpty("Mesh", model.transform, Vector3.zero, Quaternion.Euler(-90f, 0f, 0f), Vector3.one);

        InstantiateModel(k_ArrowShaftModelPath, mesh.transform,
            new Vector3(0f, -0.3f, 0f), Quaternion.Euler(0f, -90f, 0f), Vector3.one * 0.033f, arrowMaterial);

        InstantiateModel(k_ArrowTipModelPath, mesh.transform,
            new Vector3(0f, -0.302f, 0f), Quaternion.Euler(0f, 0f, -90f), Vector3.one * 0.033f, arrowMaterial);

        Transform tip = CreateEmpty("Tip", mesh.transform, new Vector3(0f, -0.449f, 0f), Quaternion.identity, Vector3.one).transform;

        AudioSource hitAudio = root.AddComponent<AudioSource>();
        hitAudio.clip = LoadAsset<AudioClip>(k_ArrowHitClipPath);
        hitAudio.playOnAwake = false;

        AudioSource shotAudio = root.AddComponent<AudioSource>();
        shotAudio.clip = LoadAsset<AudioClip>(k_ArrowShootClipPath);
        shotAudio.playOnAwake = false;

        QuickActionsAPI.AddGrabInteraction(root);

        ArrowCaster caster = root.AddComponent<ArrowCaster>();
        caster.InjectTip(tip);

        Arrow arrow = root.AddComponent<Arrow>();
        arrow.InjectReferences(caster, tip, hitAudio, shotAudio);

        return SaveAsPrefab(root, k_ArrowPrefabPath);
    }

    // ------------------------------------------------------------------
    // Bow
    // ------------------------------------------------------------------
    static void BuildBowPrefab()
    {
        GameObject root = new GameObject("Bow");
        root.layer = 2;

        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = 1f;
        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        // Generous collider so the bow is easy to grab in VR.
        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.08f, 0.9f, 0.25f);
        collider.center = new Vector3(0f, 0f, -0.05f);

        Material bowMaterial = LoadAsset<Material>(k_BowMaterialPath);

        InstantiateModel(k_BowModelPath, root.transform,
            new Vector3(-0.06f, 0f, -0.093f), Quaternion.Euler(-3.49f, 108.54f, 94.25f), Vector3.one * 0.0033f, bowMaterial);

        GameObject stringObject = CreateEmpty("String", root.transform, new Vector3(0f, 0f, -0.047f), Quaternion.identity, Vector3.one);
        LineRenderer lineRenderer = stringObject.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = true;
        lineRenderer.widthMultiplier = 0.006f;
        lineRenderer.numCapVertices = 3;
        lineRenderer.positionCount = 3;
        lineRenderer.sharedMaterial = GetOrCreateLineMaterial();

        Transform stringStart = CreateEmpty("Start", stringObject.transform, new Vector3(0f, 0.4272f, -0.165f), Quaternion.identity, Vector3.one).transform;
        Transform stringMiddle = CreateEmpty("Middle", stringObject.transform, new Vector3(0f, 0f, -0.165f), Quaternion.identity, Vector3.one).transform;
        Transform stringEnd = CreateEmpty("End", stringObject.transform, new Vector3(0f, -0.4421f, -0.165f), Quaternion.identity, Vector3.one).transform;

        StringRenderer stringRenderer = stringObject.AddComponent<StringRenderer>();
        stringRenderer.InjectReferences(stringStart, stringMiddle, stringEnd);

        // Notch: trigger that catches a passing arrow.
        GameObject notchObject = CreateEmpty("Notch", root.transform, new Vector3(-0.023f, 0f, -0.22f), Quaternion.identity, Vector3.one);
        SphereCollider notchCollider = notchObject.AddComponent<SphereCollider>();
        notchCollider.isTrigger = true;
        notchCollider.radius = 0.08f;

        AudioSource twangAudio = notchObject.AddComponent<AudioSource>();
        twangAudio.clip = LoadAsset<AudioClip>(k_BowTwangClipPath);
        twangAudio.playOnAwake = false;

        // Fixed draw axis parented to the notch so it does not follow the hand.
        Transform drawStart = CreateEmpty("DrawStart", notchObject.transform, Vector3.zero, Quaternion.identity, Vector3.one).transform;
        Transform drawEnd = CreateEmpty("DrawEnd", notchObject.transform, new Vector3(0f, 0f, -0.48f), Quaternion.identity, Vector3.one).transform;

        // Draw grip: a small grabbable volume on the string.
        GameObject drawGrip = CreateEmpty("DrawGrip", notchObject.transform, Vector3.zero, Quaternion.identity, Vector3.one);
        BoxCollider drawCollider = drawGrip.AddComponent<BoxCollider>();
        drawCollider.size = new Vector3(0.12f, 0.12f, 0.12f);

        AudioSource pullAudio = drawGrip.AddComponent<AudioSource>();
        pullAudio.clip = LoadAsset<AudioClip>(k_BowPullClipPath);
        pullAudio.playOnAwake = false;

        QuickActionsAPI.AddGrabInteraction(drawGrip);

        // The bow itself must also be grabbable, independent of the draw grip.
        QuickActionsAPI.AddGrabInteraction(root);

        Grabbable bowGrabbable = root.GetComponent<Grabbable>();

        Bow bow = root.AddComponent<Bow>();
        bow.InjectGrabbable(bowGrabbable);

        BowNotch notch = notchObject.AddComponent<BowNotch>();
        notch.InjectReferences(bow, stringMiddle);

        BowPullMeasurer measurer = drawGrip.AddComponent<BowPullMeasurer>();
        measurer.InjectReferences(drawStart, drawEnd, stringMiddle, notch, pullAudio);

        SaveAsPrefab(root, k_BowPrefabPath);
    }

    // ------------------------------------------------------------------
    // Quiver
    // ------------------------------------------------------------------
    static void BuildQuiverPrefab(GameObject arrowPrefab)
    {
        GameObject root = new GameObject("Quiver");

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = new Vector3(0.12f, 0.35f, 0.12f);
        visual.transform.localPosition = new Vector3(0f, 0.175f, 0f);

        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
        {
            Object.DestroyImmediate(visualCollider);
        }

        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.12f, 0.35f, 0.12f);
        collider.center = new Vector3(0f, 0.175f, 0f);

        Material bowMaterial = LoadAsset<Material>(k_BowMaterialPath);
        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer != null && bowMaterial != null)
        {
            renderer.sharedMaterial = bowMaterial;
        }

        Transform spawnPoint = CreateEmpty("SpawnPoint", root.transform, new Vector3(0f, 0.35f, 0f), Quaternion.identity, Vector3.one).transform;

        Quiver quiver = root.AddComponent<Quiver>();
        quiver.Configure(arrowPrefab, spawnPoint);

        SaveAsPrefab(root, k_QuiverPrefabPath);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    static GameObject CreateEmpty(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation;
        go.transform.localScale = localScale;
        return go;
    }

    static void InstantiateModel(string path, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material material)
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (modelAsset == null)
        {
            Debug.LogWarning("[BowPrefabBuilder] Missing model: " + path);
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        instance.transform.SetParent(parent, false);
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = localRotation;
        instance.transform.localScale = localScale;

        if (material != null)
        {
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
            }
        }
    }

    static Material GetOrCreateLineMaterial()
    {
        const string path = "Assets/Materials/Bow/StringLine.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        material = new Material(shader) { color = Color.black };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static GameObject SaveAsPrefab(GameObject root, string path)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static T LoadAsset<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
        {
            Debug.LogWarning("[BowPrefabBuilder] Missing asset: " + path);
        }
        return asset;
    }

    static void EnsureAssetFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(folder);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureAssetFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, leaf);
    }
}

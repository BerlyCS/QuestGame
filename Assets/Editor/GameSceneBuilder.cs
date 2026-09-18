using System.Collections.Generic;
using System.IO;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds a clean, playable foundation scene for the treasure/curse game:
/// XR rig, night lighting, abandoned camp and a refuelling campfire.
/// Re-runnable from Tools > Game > Build Game Scene.
/// </summary>
public static class GameSceneBuilder
{
    const string k_ScenePath = "Assets/Scenes/Game.unity";
    const string k_PrefabFolder = "Assets/Prefabs/Gameplay";
    const string k_MaterialFolder = "Assets/Materials/Game";
    const string k_RigPrefabPath = "Packages/com.meta.xr.sdk.interaction.ovr/Runtime/Prefabs/OVRComprehensiveInteractionRig.prefab";
    const string k_CameraRigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

    // Camp layout, relative to the static player rig (see fogata.md). Everything the
    // player needs sits within arm's reach; nothing here requires taking a step.
    const float k_CampfireDistance = 1.1f;
    const float k_LogPileDistance = 1.3f;
    const float k_ChestDistance = 1.1f;

    // Fixed rings centred on the campfire. Deliberately not randomised: these are
    // gameplay distances (enemy spawn ring, ranged-enemy stand-off, tree-line
    // boundary), not visual dressing.
    const float k_EnemySpawnRadius = 10f;
    const float k_TreeRingRadius = 16f;

    /// <summary>
    /// Stand-off radius for the Lanzahuesos (ranged enemy). Not yet implemented -
    /// no object exists to place at this radius - but kept here as the single
    /// source of truth for when that enemy is built.
    /// </summary>
    const float k_BoneThrowerStopRadius = 8f;

    static Material s_Ground;
    static Material s_Wood;
    static Material s_Stone;
    static Material s_Tent;
    static Material s_Trunk;
    static Material s_Metal;
    static Material s_DarkMetal;
    static Material s_Treasure;
    static Material s_Flame;
    static Material s_Bone;
    static Material s_GlowBone;
    static Material s_BoneTrail;
    static Material s_EmberGlow;
    static Material s_EmberTrail;
    static Material s_SlingBand;
    static Material s_Moon;
    static PhysicsMaterial s_LowFriction;

    [MenuItem("Tools/Game/Build Game Scene")]
    public static void Build()
    {
        EnsureFolders();

        EditorSceneManager.SaveOpenScenes();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        ConfigureLighting();

        var moonLight = CreateMoonLight();

        var rig = CreateXrRig(out var interactionRig);
        ConfigureCamera(rig);
        ConfigureLocomotion(interactionRig);

        CreateMaterials();

        // The whole camp is laid out relative to the rig, which never moves: nothing
        // the player needs requires taking a step. Positions are the rig's ground
        // point plus a flat (Y-projected) offset along its own forward/right, so the
        // layout still makes sense however the rig happens to be placed or facing.
        Vector3 rigPosition = rig != null ? rig.transform.position : Vector3.zero;
        Vector3 rigGroundPosition = new Vector3(rigPosition.x, 0f, rigPosition.z);

        Vector3 rigForward = rig != null ? Vector3.ProjectOnPlane(rig.transform.forward, Vector3.up) : Vector3.forward;
        if (rigForward.sqrMagnitude < 0.0001f)
            rigForward = Vector3.forward;
        rigForward.Normalize();

        Vector3 rigRight = rig != null ? Vector3.ProjectOnPlane(rig.transform.right, Vector3.up) : Vector3.right;
        if (rigRight.sqrMagnitude < 0.0001f)
            rigRight = Vector3.right;
        rigRight.Normalize();

        Vector3 campfirePosition = rigGroundPosition + rigForward * k_CampfireDistance;
        Vector3 logPileGroundPosition = rigGroundPosition + rigRight * k_LogPileDistance;
        Vector3 chestPosition = rigGroundPosition - rigRight * k_ChestDistance;

        var environment = new GameObject("Environment").transform;
        BuildGround(environment);
        BuildPerimeter(environment);
        BuildForest(environment, campfirePosition);
        BuildMoon(environment);
        var campfire = BuildCampfire(environment, campfirePosition);
        var logPile = BuildLogPile(environment, logPileGroundPosition);
        BuildTutorialLog(campfire.transform);
        var tent = BuildTent(environment);
        var campProps = BuildCampProps(environment);
        var treasure = BuildTreasure(environment, chestPosition);

        var leftHand = rig != null ? FindByName(rig.transform, "LeftHandAnchor") : null;
        var rightHand = rig != null ? FindByName(rig.transform, "RightHandAnchor") : null;
        var head = rig != null ? rig.GetComponentInChildren<Camera>()?.transform : null;

        var weaponRack = BuildWeaponRack(environment);
        var axe = BuildAxe(environment, leftHand, rightHand);

        var systems = new GameObject("Game Systems");
        var night = systems.AddComponent<NightEnvironmentController>();
        Wire(night, "m_Campfire", campfire);
        Wire(night, "m_MoonLight", moonLight);

        BuildCampReveal(systems, campfire, tent, campProps, treasure, weaponRack);

        var skeletonSpawner = BuildSkeletonSpawner(systems, rig, campfire);
        var boneThrowerSpawner = BuildBoneThrowerSpawner(systems, rig, campfire);
        BuildLogSpawner(systems, logPile);
        BuildSlingshot(systems, leftHand, rightHand, head, axe, campfire);

        var gameManager = BuildGameManager(systems, campfire, night, skeletonSpawner, boneThrowerSpawner);
        BuildDebugKeys(systems, skeletonSpawner, boneThrowerSpawner, gameManager);

        AssetDatabase.SaveAssets();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, k_ScenePath);
        AddToBuildSettings(k_ScenePath);

        Debug.Log($"[GameSceneBuilder] Built {k_ScenePath}");
    }

    static void EnsureFolders()
    {
        EnsureFolder("Assets/Scenes");
        EnsureFolder("Assets/Prefabs");
        EnsureFolder(k_PrefabFolder);
        EnsureFolder("Assets/Materials");
        EnsureFolder(k_MaterialFolder);
    }

    static void ConfigureLighting()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.02f, 0.03f, 0.05f);
        RenderSettings.ambientIntensity = 0.12f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = new Color(0.01f, 0.01f, 0.02f);
        RenderSettings.fogDensity = 0.012f;
    }

    static Light CreateMoonLight()
    {
        var go = new GameObject("Moon Light");
        go.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.55f, 0.62f, 0.9f);
        light.intensity = 0.1f;
        // The campfire is the only light allowed to cast shadows: its shadows are the
        // gameplay's "shadow radar" for enemies approaching from behind (see Fire Light
        // below). The moon must never compete with that read.
        light.shadows = LightShadows.None;
        return light;
    }

    /// <summary>
    /// Builds the XR rig. The Interaction SDK's comprehensive rig has no camera
    /// of its own: it must be nested under an OVRCameraRig, which supplies the
    /// OVRManager and the CenterEyeAnchor camera, then reference that camera rig.
    /// </summary>
    static GameObject CreateXrRig(out GameObject interactionRig)
    {
        interactionRig = null;

        var cameraRigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_CameraRigPrefabPath);
        if (cameraRigPrefab == null)
        {
            Debug.LogError($"[GameSceneBuilder] Camera rig prefab not found at {k_CameraRigPrefabPath}");
            return null;
        }

        var cameraRig = (GameObject)PrefabUtility.InstantiatePrefab(cameraRigPrefab);
        cameraRig.name = "OVRCameraRig";

        var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_RigPrefabPath);
        if (rigPrefab == null)
        {
            Debug.LogError($"[GameSceneBuilder] Interaction rig prefab not found at {k_RigPrefabPath}");
            return cameraRig;
        }

        interactionRig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, cameraRig.transform);
        interactionRig.name = "OVRComprehensiveInteractionRig";

        var cameraRigComponent = cameraRig.GetComponent<OVRCameraRig>();
        var cameraRigRef = interactionRig.GetComponent<OVRCameraRigRef>();
        if (cameraRigComponent != null && cameraRigRef != null)
            Wire(cameraRigRef, "_ovrCameraRig", cameraRigComponent);

        var manager = cameraRig.GetComponent<OVRManager>();
        if (manager != null)
        {
            var serialized = new SerializedObject(manager);
            var origin = serialized.FindProperty("_trackingOriginType");
            if (origin != null)
                origin.intValue = (int)OVRManager.TrackingOrigin.FloorLevel;

            // FloorLevel derives head height from the headset's Guardian floor
            // calibration, which can sit lower than the player's real eye level.
            // Nudge the head pose up a bit so the view reads as normal standing
            // height instead of a crouched/child's-eye view.
            var headOffset = serialized.FindProperty("_headPoseRelativeOffsetTranslation");
            if (headOffset != null)
                headOffset.vector3Value = new Vector3(0f, 0.15f, 0f);

            // Hands-only game (see OculusProjectConfig's handTrackingSupport): no Touch
            // controllers are tracked, so hand poses should always come from real hand
            // tracking rather than being synthesized from controller input.
            var handPoses = serialized.FindProperty("controllerDrivenHandPosesType");
            if (handPoses != null)
                handPoses.enumValueIndex = (int)OVRManager.ControllerDrivenHandPosesType.None;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        return cameraRig;
    }

    /// <summary>
    /// Only CenterEyeAnchor's camera actually renders (LeftEyeAnchor/RightEyeAnchor stay
    /// disabled until real stereo rendering kicks in on-device), so it must be targeted by
    /// name rather than GetComponentInChildren&lt;Camera&gt;(), which would silently match
    /// LeftEyeAnchor's disabled-but-still-findable camera instead and leave the camera
    /// that's actually used on its default skybox background.
    /// </summary>
    static void ConfigureCamera(GameObject rig)
    {
        if (rig == null)
            return;

        var centerEye = FindByName(rig.transform, "CenterEyeAnchor");
        var camera = centerEye != null ? centerEye.GetComponent<Camera>() : null;
        if (camera == null)
        {
            Debug.LogWarning("[GameSceneBuilder] CenterEyeAnchor camera not found; falling back to first camera in rig.");
            camera = rig.GetComponentInChildren<Camera>();
        }
        if (camera == null)
            return;

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.005f, 0.006f, 0.012f, 1f);
    }

    /// <summary>
    /// The player guards the camp on the spot, so smooth locomotion and
    /// teleport are not used. The comprehensive rig's Locomotor is disabled
    /// because it expects a player origin/eyes and camera that this stationary
    /// setup does not provide, and would otherwise log assertion errors.
    /// </summary>
    static void ConfigureLocomotion(GameObject interactionRig)
    {
        if (interactionRig == null)
            return;

        var locomotor = interactionRig.transform.Find("Locomotor");
        if (locomotor != null)
            locomotor.gameObject.SetActive(false);
        else
            Debug.LogWarning("[GameSceneBuilder] 'Locomotor' not found on the interaction rig.");
    }

    static Transform FindByName(Transform root, string namePart)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.Contains(namePart))
                return t;
        return null;
    }

    static void DisableChild(Transform parent, string path)
    {
        var child = parent.Find(path);
        if (child == null)
        {
            Debug.LogWarning($"[GameSceneBuilder] Could not find '{path}' under '{parent.name}'");
            return;
        }

        child.gameObject.SetActive(false);
    }

    static void CreateMaterials()
    {
        s_Ground = CreateMaterial("M_Ground", new Color(0.15f, 0.13f, 0.1f), 0f, 0.05f);
        s_Wood = CreateMaterial("M_Wood", new Color(0.18f, 0.11f, 0.06f), 0f, 0.1f);
        s_Stone = CreateMaterial("M_Stone", new Color(0.22f, 0.22f, 0.24f), 0f, 0.1f);
        s_Tent = CreateMaterial("M_Tent", new Color(0.16f, 0.13f, 0.11f), 0f, 0.1f);
        s_Trunk = CreateMaterial("M_Trunk", new Color(0.12f, 0.08f, 0.05f), 0f, 0.1f);
        s_Metal = CreateMaterial("M_Metal", new Color(0.6f, 0.55f, 0.3f), 0.8f, 0.6f);
        s_DarkMetal = CreateMaterial("M_DarkMetal", new Color(0.14f, 0.13f, 0.12f), 0.7f, 0.4f);
        s_Treasure = CreateMaterial("M_Treasure", new Color(0.85f, 0.7f, 0.2f), 0.9f, 0.8f, new Color(0.45f, 0.35f, 0.08f));
        s_Flame = CreateMaterial("M_Flame", new Color(1f, 0.6f, 0.15f), 0f, 0f, null, "Universal Render Pipeline/Particles/Unlit");
        s_Bone = CreateMaterial("M_Bone", new Color(0.82f, 0.8f, 0.72f), 0f, 0.15f);
        // Brighter and emissive so the Lanzahuesos reads at range in the dark,
        // clearly distinct from the plain (non-emissive) Caminante bone.
        s_GlowBone = CreateMaterial("M_GlowBone", new Color(0.92f, 0.88f, 0.6f), 0f, 0.25f, new Color(1.4f, 1.1f, 0.5f));
        s_BoneTrail = CreateMaterial("M_BoneTrail", new Color(0.85f, 0.82f, 0.7f), 0f, 0f, null, "Universal Render Pipeline/Particles/Unlit");
        s_EmberGlow = CreateMaterial("M_EmberGlow", new Color(1f, 0.5f, 0.1f), 0f, 0.3f, new Color(1.6f, 0.7f, 0.15f));
        s_EmberTrail = CreateMaterial("M_EmberTrail", new Color(1f, 0.6f, 0.2f), 0f, 0f, null, "Universal Render Pipeline/Particles/Unlit");
        s_SlingBand = CreateMaterial("M_SlingBand", new Color(0.6f, 0.3f, 0.1f), 0f, 0f, null, "Universal Render Pipeline/Particles/Unlit");
        s_Moon = CreateMaterial("M_Moon", new Color(0.9f, 0.92f, 1f), 0f, 0.2f, new Color(1.5f, 1.6f, 2f));
        s_LowFriction = CreatePhysicsMaterial("PM_LowFriction", 0.05f, 0f);
    }

    static void BuildGround(Transform parent)
    {
        CreatePrimitive("Ground", PrimitiveType.Plane, parent, Vector3.zero, new Vector3(6f, 1f, 6f), s_Ground);
    }

    static void BuildMoon(Transform parent)
    {
        var moon = CreatePrimitive("Moon", PrimitiveType.Sphere, parent,
            new Vector3(-35f, 60f, 75f), new Vector3(12f, 12f, 12f), s_Moon);
        moon.GetComponent<Collider>().enabled = false;
    }

    static void BuildPerimeter(Transform parent)
    {
        var rocks = new GameObject("Rocks").transform;
        rocks.SetParent(parent, false);
        var rockPositions = new[]
        {
            new Vector3(-3.4f, 0.15f, 3.2f), new Vector3(3.1f, 0.2f, 2.6f),
            new Vector3(-4.2f, 0.25f, -1.5f), new Vector3(2.7f, 0.12f, 4.6f),
            new Vector3(-1.8f, 0.1f, 5.4f), new Vector3(4.5f, 0.3f, 0.5f)
        };
        for (int i = 0; i < rockPositions.Length; i++)
        {
            float s = 0.5f + (i % 3) * 0.25f;
            CreatePrimitive($"Rock_{i}", PrimitiveType.Sphere, rocks, rockPositions[i],
                new Vector3(s, s * 0.7f, s), s_Stone);
        }
    }

    const string k_ForestFbxPath = "Assets/Models/Bosque/bosque.fbx";

    static readonly string[] k_ForestNamePrefixes =
    {
        "OakTree", "SpruceTree", "DeadOak", "BigRock", "Rock"
    };

    /// <summary>
    /// Scatters copies of the trees and rocks from bosque.fbx (an external forest asset
    /// pack) in a fixed ring around the camp at k_TreeRingRadius, centred on
    /// <paramref name="center"/> (the campfire), replacing the old primitive tree ring.
    /// bosque.fbx itself is a prop kit: every tree/rock sits stacked at the origin rather
    /// than arranged into a scene, so this places multiple randomized instances of each
    /// rather than reusing the source placement. Only the ring's radius is fixed -
    /// angle, rotation and scale are still randomized per instance so the boundary
    /// reads as a forest rather than a mechanical circle. Only the flora/rock pieces are
    /// used; the pack's camp props (barrels, crates) and its hunter character/camera/light
    /// are discarded since they aren't part of the forest.
    /// </summary>
    static void BuildForest(Transform parent, Vector3 center)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(k_ForestFbxPath);
        if (source == null)
        {
            Debug.LogWarning($"[GameSceneBuilder] Forest model not found at {k_ForestFbxPath}; skipping forest.");
            return;
        }

        var sourceInstance = (GameObject)PrefabUtility.InstantiatePrefab(source);

        // Collect only the top-most transform of each placed tree/rock (its mesh lives on
        // material-split children further down, so a whole matching subtree is one piece).
        var templates = new List<Transform>();
        foreach (Transform t in sourceInstance.GetComponentsInChildren<Transform>(true))
        {
            if (t == sourceInstance.transform)
                continue;
            if (!StartsWithAny(t.name, k_ForestNamePrefixes))
                continue;
            if (t.parent != null && StartsWithAny(t.parent.name, k_ForestNamePrefixes))
                continue;
            if (t.GetComponentsInChildren<Renderer>().Length == 0)
                continue;

            templates.Add(t);
        }

        if (templates.Count == 0)
        {
            Debug.LogWarning("[GameSceneBuilder] No tree/rock meshes matched in bosque.fbx; skipping forest.");
            Object.DestroyImmediate(sourceInstance);
            return;
        }

        // Each template's own mesh data already carries a baked vertical offset (the
        // source kit wasn't authored with its pivots on the ground), so measure how far
        // each one needs to be lifted to sit on y = 0 before scattering any copies. The
        // pack is also authored Z-up and only stands upright in the source file thanks to
        // a corrective rotation on an ancestor ("Sketchfab_model"); cloning just the
        // template transform drops that ancestor, so capture the rotation here and
        // reapply it below, or every copy comes out lying on its side.
        var groundOffsets = new Dictionary<Transform, float>();
        var uprightRotations = new Dictionary<Transform, Quaternion>();
        foreach (var template in templates)
        {
            groundOffsets[template] = -LocalMinY(template);
            uprightRotations[template] = template.rotation;
        }

        const int k_InstanceCount = 30;
        const float k_MinScale = 0.8f;
        const float k_MaxScale = 1.3f;

        var forest = new GameObject("Forest (bosque.fbx)").transform;
        forest.SetParent(parent, false);
        forest.localPosition = center;

        var random = new System.Random(1);
        int triangleCount = 0;

        for (int i = 0; i < k_InstanceCount; i++)
        {
            var template = templates[random.Next(templates.Count)];
            var copy = (GameObject)Object.Instantiate(template.gameObject, forest);
            copy.name = template.name;

            float angle = (float)(random.NextDouble() * Mathf.PI * 2.0);
            float scale = Mathf.Lerp(k_MinScale, k_MaxScale, (float)random.NextDouble());

            copy.transform.localPosition = new Vector3(
                Mathf.Cos(angle) * k_TreeRingRadius,
                groundOffsets[template] * scale,
                Mathf.Sin(angle) * k_TreeRingRadius);
            copy.transform.localRotation =
                Quaternion.Euler(0f, (float)(random.NextDouble() * 360.0), 0f) * uprightRotations[template];
            copy.transform.localScale = Vector3.one * scale;

            foreach (var meshFilter in copy.GetComponentsInChildren<MeshFilter>())
                if (meshFilter.sharedMesh != null)
                    triangleCount += meshFilter.sharedMesh.triangles.Length / 3;
        }

        Object.DestroyImmediate(sourceInstance);

        Debug.Log($"[GameSceneBuilder] Placed {k_InstanceCount} forest instances from bosque.fbx " +
            $"(~{triangleCount} triangles). Check the frame rate on device; trim k_InstanceCount if it's too heavy for Quest.");
    }

    static bool StartsWithAny(string name, string[] prefixes)
    {
        foreach (var prefix in prefixes)
            if (name.StartsWith(prefix))
                return true;
        return false;
    }

    /// <summary>
    /// The lowest point of the given (untransformed, origin-local) template's combined
    /// renderer bounds, i.e. how far its baked-in mesh geometry dips below its pivot.
    /// </summary>
    static float LocalMinY(Transform template)
    {
        float minY = float.PositiveInfinity;
        foreach (var renderer in template.GetComponentsInChildren<Renderer>())
            minY = Mathf.Min(minY, renderer.bounds.min.y);
        return minY;
    }

    static CampfireFuel BuildCampfire(Transform parent, Vector3 position)
    {
        var root = new GameObject("Campfire");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = position;
        var fuel = root.AddComponent<CampfireFuel>();

        for (int i = 0; i < 10; i++)
        {
            float angle = i / 10f * Mathf.PI * 2f;
            CreatePrimitive($"Stone_{i}", PrimitiveType.Sphere, root.transform,
                new Vector3(Mathf.Cos(angle) * 0.62f, 0.08f, Mathf.Sin(angle) * 0.62f),
                new Vector3(0.22f, 0.16f, 0.22f), s_Stone);
        }

        for (int i = 0; i < 4; i++)
        {
            var log = CreatePrimitive($"BaseLog_{i}", PrimitiveType.Cylinder, root.transform,
                new Vector3(0f, 0.12f, 0f), new Vector3(0.12f, 0.5f, 0.12f), s_Wood);
            log.transform.localRotation = Quaternion.Euler(90f, i * 45f, 0f);
        }

        BuildCookingTripod(root.transform);

        var flame = new GameObject("Flame");
        flame.transform.SetParent(root.transform, false);
        flame.transform.localPosition = new Vector3(0f, 0.25f, 0f);
        var particles = flame.AddComponent<ParticleSystem>();
        ConfigureFlame(particles);

        var lightGo = new GameObject("Fire Light");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.7f, 0.25f);
        light.range = 16f;
        light.intensity = 6f;
        // The only shadow-casting light in the scene: the long shadows it throws are the
        // player's radar for enemies approaching from behind. A gameplay mechanic, not
        // a look.
        light.shadows = LightShadows.Soft;

        var trigger = new GameObject("Fuel Trigger");
        trigger.transform.SetParent(root.transform, false);
        trigger.transform.localPosition = new Vector3(0f, 0.3f, 0f);
        var triggerCollider = trigger.AddComponent<SphereCollider>();
        triggerCollider.isTrigger = true;
        triggerCollider.radius = 0.9f;

        Wire(fuel, "m_FireLight", light);
        Wire(fuel, "m_FlameParticles", particles);

        return fuel;
    }

    static void BuildCookingTripod(Transform parent)
    {
        var tripod = new GameObject("Cooking Tripod");
        tripod.transform.SetParent(parent, false);

        for (int i = 0; i < 3; i++)
        {
            float angle = i / 3f * Mathf.PI * 2f;
            var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            var pole = CreatePrimitive($"Pole_{i}", PrimitiveType.Cylinder, tripod.transform,
                new Vector3(radial.x * 0.42f, 0.8f, radial.z * 0.42f),
                new Vector3(0.045f, 0.85f, 0.045f), s_Wood);

            var tangent = Vector3.Cross(Vector3.up, radial).normalized;
            pole.transform.localRotation = Quaternion.AngleAxis(20f, tangent);
            RemoveCollider(pole);
        }

        var chain = CreatePrimitive("Chain", PrimitiveType.Cylinder, tripod.transform,
            new Vector3(0f, 1.2f, 0f), new Vector3(0.012f, 0.4f, 0.012f), s_DarkMetal);
        RemoveCollider(chain);

        var pot = CreatePrimitive("Cooking Pot", PrimitiveType.Cylinder, tripod.transform,
            new Vector3(0f, 0.72f, 0f), new Vector3(0.3f, 0.15f, 0.3f), s_DarkMetal);
        RemoveCollider(pot);
    }

    static void ConfigureFlame(ParticleSystem ps)
    {
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.45f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.1f), new Color(1f, 0.85f, 0.35f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;

        var emission = ps.emission;
        emission.rateOverTime = 40f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;
        shape.radius = 0.12f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.8f, 0.3f), 0f),
                new GradientColorKey(new Color(0.8f, 0.15f, 0.02f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.9f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = s_Flame;
    }

    static GameObject s_LogPrefab;

    const float k_LogStandHeight = 0.85f;

    /// <summary>
    /// The pile sits at <paramref name="groundPosition"/> (to the player's right, close
    /// enough to reach without walking, since the player never leaves the campfire).
    /// </summary>
    static Transform BuildLogPile(Transform parent, Vector3 groundPosition)
    {
        s_LogPrefab = BuildLogPrefab();

        BuildLogStand(parent, groundPosition);

        var pile = new GameObject("Log Pile");
        pile.transform.SetParent(parent, false);
        // Logs stack starting at the stand's top surface rather than at y = 0, so the
        // whole pile sits at hand height instead of down at the player's feet.
        pile.transform.localPosition = groundPosition + new Vector3(0f, k_LogStandHeight, 0f);

        for (int i = 0; i < 6; i++)
        {
            int row = i / 3;
            int column = i % 3;
            var log = (GameObject)PrefabUtility.InstantiatePrefab(s_LogPrefab, pile.transform);
            log.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            log.transform.localPosition = new Vector3((column - 1) * 0.16f, 0.08f + row * 0.13f, row % 2 == 1 ? 0.06f : 0f);
        }

        return pile.transform;
    }

    /// <summary>
    /// A stubby stump the pile rests on, purely so the logs sit at hand height instead
    /// of on the ground; its default primitive collider is what the logs' rigidbodies
    /// actually settle on.
    /// </summary>
    static void BuildLogStand(Transform parent, Vector3 groundPosition)
    {
        CreatePrimitive("Log Stand", PrimitiveType.Cylinder, parent,
            groundPosition + new Vector3(0f, k_LogStandHeight * 0.5f, 0f),
            new Vector3(0.6f, k_LogStandHeight * 0.5f, 0.6f), s_Trunk);
    }

    /// <summary>
    /// A single log already shoved partway toward the fire when the player wakes up: a
    /// silent invitation to finish the job instead of a tutorial prompt. It's placed
    /// close enough to already overlap the fuel trigger, but Log requires a grab before
    /// it can be consumed, so it just sits there as a hint until the player picks it up
    /// and it catches.
    /// </summary>
    static void BuildTutorialLog(Transform campfireRoot)
    {
        if (s_LogPrefab == null)
            return;

        // Local to the campfire root, whose origin is the fire itself: on the same
        // (right-hand) side as the log pile, and toward the player rather than past
        // the fire, as if it had been dragged partway in from the pile.
        var localPosition = new Vector3(0.45f, 0.14f, -0.35f);
        var towardFireCenter = -localPosition;
        float yaw = Mathf.Atan2(towardFireCenter.x, towardFireCenter.z) * Mathf.Rad2Deg;

        var log = (GameObject)PrefabUtility.InstantiatePrefab(s_LogPrefab, campfireRoot);
        log.name = "Tutorial Log";
        log.transform.localPosition = localPosition;
        log.transform.localRotation = Quaternion.Euler(90f, yaw, 0f);
    }

    /// <summary>
    /// Wood is meant to be infinite: whenever a log from the pile is burned,
    /// LogSpawner drops a fresh one back in after a short delay so the player
    /// never runs out of fuel to feed the fire.
    /// </summary>
    static void BuildLogSpawner(GameObject systems, Transform logPile)
    {
        var spawnerGo = new GameObject("Log Spawner");
        spawnerGo.transform.SetParent(systems.transform, false);
        var spawner = spawnerGo.AddComponent<LogSpawner>();
        Wire(spawner, "m_LogPrefab", s_LogPrefab);
        Wire(spawner, "m_PileOrigin", logPile);
    }

    static GameObject BuildLogPrefab()
    {
        var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        log.name = "Log";
        log.transform.localScale = new Vector3(0.14f, 0.35f, 0.14f);

        var rigidbody = log.AddComponent<Rigidbody>();
        rigidbody.mass = 0.5f;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var grabbable = log.AddComponent<Grabbable>();

        // Hands-only game: near grab (HandGrabInteractable, pinch while the log is in
        // reach) and distance grab (DistanceHandGrabInteractable, pinch-and-fly from
        // across the camp) both target the same Grabbable/Rigidbody. No controller-only
        // GrabInteractable, since Touch controllers aren't used.
        var handGrab = log.AddComponent<HandGrabInteractable>();
        handGrab.InjectRigidbody(rigidbody);
        handGrab.InjectOptionalPointableElement(grabbable);

        var distanceHandGrab = log.AddComponent<DistanceHandGrabInteractable>();
        distanceHandGrab.InjectRigidbody(rigidbody);
        distanceHandGrab.InjectOptionalPointableElement(grabbable);

        log.AddComponent<Log>();
        log.GetComponent<Renderer>().sharedMaterial = s_Wood;

        var prefab = PrefabUtility.SaveAsPrefabAsset(log, $"{k_PrefabFolder}/Log.prefab");
        Object.DestroyImmediate(log);
        return prefab;
    }

    static GameObject BuildTent(Transform parent)
    {
        var tent = new GameObject("Tent");
        tent.transform.SetParent(parent, false);
        tent.transform.localPosition = new Vector3(-2.8f, 0f, -0.4f);
        tent.transform.localRotation = Quaternion.Euler(0f, 25f, 0f);

        CreateTentPanel(tent.transform, "Roof Left", new Vector3(-0.55f, 0.62f, 0f), 52f);
        CreateTentPanel(tent.transform, "Roof Right", new Vector3(0.55f, 0.62f, 0f), -52f);

        CreatePrimitive("Back Wall", PrimitiveType.Cube, tent.transform,
            new Vector3(0f, 0.5f, -1.25f), new Vector3(1.75f, 1f, 0.06f), s_Tent);

        var ridge = CreatePrimitive("Ridge Pole", PrimitiveType.Cylinder, tent.transform,
            new Vector3(0f, 1.13f, 0f), new Vector3(0.05f, 1.35f, 0.05f), s_Trunk);
        ridge.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var bedroll = CreatePrimitive("Bedroll", PrimitiveType.Cube, tent.transform,
            new Vector3(0f, 0.05f, 0.2f), new Vector3(0.5f, 0.1f, 0.95f), s_Tent);
        RemoveCollider(bedroll);

        return tent;
    }

    static void CreateTentPanel(Transform parent, string name, Vector3 position, float zAngle)
    {
        var panel = CreatePrimitive(name, PrimitiveType.Cube, parent, position,
            new Vector3(1.6f, 0.06f, 2.6f), s_Tent);
        panel.transform.localRotation = Quaternion.Euler(0f, 0f, zAngle);
    }

    static GameObject BuildCampProps(Transform parent)
    {
        var props = new GameObject("Camp Props").transform;
        props.SetParent(parent, false);

        var bench = CreatePrimitive("Log Bench", PrimitiveType.Cylinder, props,
            new Vector3(1.15f, 0.22f, 0.9f), new Vector3(0.22f, 0.6f, 0.22f), s_Wood);
        bench.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

        CreatePrimitive("Barrel", PrimitiveType.Cylinder, props,
            new Vector3(-1.7f, 0.35f, -1.2f), new Vector3(0.5f, 0.35f, 0.5f), s_Wood);

        var crate1 = CreatePrimitive("Crate 1", PrimitiveType.Cube, props,
            new Vector3(-2f, 0.25f, 1.5f), new Vector3(0.5f, 0.5f, 0.5f), s_Wood);
        crate1.transform.localRotation = Quaternion.Euler(0f, 18f, 0f);

        var crate2 = CreatePrimitive("Crate 2", PrimitiveType.Cube, props,
            new Vector3(-1.55f, 0.2f, 1.85f), new Vector3(0.4f, 0.4f, 0.4f), s_Wood);
        crate2.transform.localRotation = Quaternion.Euler(0f, -12f, 0f);

        var fence = new GameObject("Fence").transform;
        fence.SetParent(props, false);
        for (int i = 0; i < 14; i++)
        {
            float angle = Mathf.Lerp(-70f, 70f, i / 13f) * Mathf.Deg2Rad;
            var position = new Vector3(Mathf.Sin(angle) * 5f, 0.45f, Mathf.Cos(angle) * 5f);
            var stake = CreatePrimitive($"Stake_{i}", PrimitiveType.Cylinder, fence, position,
                new Vector3(0.06f, 0.45f, 0.06f), s_Trunk);
            stake.transform.localRotation = Quaternion.Euler((i % 3 - 1) * 4f, 0f, (i % 2) * 4f);
            RemoveCollider(stake);
        }

        return props.gameObject;
    }

    static GameObject BuildSkeletonPrefab()
    {
        var root = new GameObject("Skeleton");
        var skeleton = root.AddComponent<Skeleton>();

        var body = new GameObject("Body");
        body.transform.SetParent(root.transform, false);

        CreatePrimitive("Skull", PrimitiveType.Sphere, body.transform,
            new Vector3(0f, 1.62f, 0f), new Vector3(0.24f, 0.24f, 0.24f), s_Bone);
        CreatePrimitive("Jaw", PrimitiveType.Cube, body.transform,
            new Vector3(0f, 1.52f, 0.04f), new Vector3(0.16f, 0.06f, 0.14f), s_Bone);
        CreatePrimitive("Spine", PrimitiveType.Cylinder, body.transform,
            new Vector3(0f, 1.2f, 0f), new Vector3(0.08f, 0.22f, 0.08f), s_Bone);

        for (int i = 0; i < 3; i++)
            CreatePrimitive($"Rib_{i}", PrimitiveType.Cube, body.transform,
                new Vector3(0f, 1.32f - i * 0.11f, 0f),
                new Vector3(0.34f - i * 0.03f, 0.035f, 0.2f), s_Bone);

        CreatePrimitive("Pelvis", PrimitiveType.Cube, body.transform,
            new Vector3(0f, 0.92f, 0f), new Vector3(0.3f, 0.14f, 0.18f), s_Bone);

        var leftArm = new GameObject("Left Arm");
        leftArm.transform.SetParent(body.transform, false);
        leftArm.transform.localPosition = new Vector3(0.24f, 1.35f, 0f);
        CreatePrimitive("Upper", PrimitiveType.Capsule, leftArm.transform,
            new Vector3(0f, -0.28f, 0f), new Vector3(0.07f, 0.28f, 0.07f), s_Bone);

        var rightArm = new GameObject("Right Arm");
        rightArm.transform.SetParent(body.transform, false);
        rightArm.transform.localPosition = new Vector3(-0.24f, 1.35f, 0f);
        CreatePrimitive("Upper", PrimitiveType.Capsule, rightArm.transform,
            new Vector3(0f, -0.28f, 0f), new Vector3(0.07f, 0.28f, 0.07f), s_Bone);

        CreatePrimitive("Left Leg", PrimitiveType.Capsule, body.transform,
            new Vector3(0.1f, 0.45f, 0f), new Vector3(0.08f, 0.4f, 0.08f), s_Bone);
        CreatePrimitive("Right Leg", PrimitiveType.Capsule, body.transform,
            new Vector3(-0.1f, 0.45f, 0f), new Vector3(0.08f, 0.4f, 0.08f), s_Bone);

        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.height = 1.8f;
        capsule.radius = 0.3f;

        var rigidbody = root.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;

        foreach (var collider in root.GetComponentsInChildren<Collider>())
            if (collider != capsule)
                Object.DestroyImmediate(collider);

        var renderers = root.GetComponentsInChildren<Renderer>();

        Wire(skeleton, "m_Body", body.transform);
        Wire(skeleton, "m_LeftArm", leftArm.transform);
        Wire(skeleton, "m_RightArm", rightArm.transform);

        var serialized = new SerializedObject(skeleton);
        var rendererArray = serialized.FindProperty("m_Renderers");
        rendererArray.arraySize = renderers.Length;
        for (int i = 0; i < renderers.Length; i++)
            rendererArray.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{k_PrefabFolder}/Skeleton.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// The Lanzahuesos: same skeletal build as the Caminante (see
    /// BuildSkeletonPrefab) but in the glowing bone material so it reads at
    /// range in the dark, and carrying BoneThrower instead of Skeleton.
    /// </summary>
    static GameObject BuildBoneThrowerPrefab()
    {
        var root = new GameObject("Bone Thrower");
        var boneThrower = root.AddComponent<BoneThrower>();

        var body = new GameObject("Body");
        body.transform.SetParent(root.transform, false);

        CreatePrimitive("Skull", PrimitiveType.Sphere, body.transform,
            new Vector3(0f, 1.62f, 0f), new Vector3(0.24f, 0.24f, 0.24f), s_GlowBone);
        CreatePrimitive("Jaw", PrimitiveType.Cube, body.transform,
            new Vector3(0f, 1.52f, 0.04f), new Vector3(0.16f, 0.06f, 0.14f), s_GlowBone);
        CreatePrimitive("Spine", PrimitiveType.Cylinder, body.transform,
            new Vector3(0f, 1.2f, 0f), new Vector3(0.08f, 0.22f, 0.08f), s_GlowBone);

        for (int i = 0; i < 3; i++)
            CreatePrimitive($"Rib_{i}", PrimitiveType.Cube, body.transform,
                new Vector3(0f, 1.32f - i * 0.11f, 0f),
                new Vector3(0.34f - i * 0.03f, 0.035f, 0.2f), s_GlowBone);

        CreatePrimitive("Pelvis", PrimitiveType.Cube, body.transform,
            new Vector3(0f, 0.92f, 0f), new Vector3(0.3f, 0.14f, 0.18f), s_GlowBone);

        var leftArm = new GameObject("Left Arm");
        leftArm.transform.SetParent(body.transform, false);
        leftArm.transform.localPosition = new Vector3(0.24f, 1.35f, 0f);
        CreatePrimitive("Upper", PrimitiveType.Capsule, leftArm.transform,
            new Vector3(0f, -0.28f, 0f), new Vector3(0.07f, 0.28f, 0.07f), s_GlowBone);

        var rightArm = new GameObject("Right Arm");
        rightArm.transform.SetParent(body.transform, false);
        rightArm.transform.localPosition = new Vector3(-0.24f, 1.35f, 0f);
        CreatePrimitive("Upper", PrimitiveType.Capsule, rightArm.transform,
            new Vector3(0f, -0.28f, 0f), new Vector3(0.07f, 0.28f, 0.07f), s_GlowBone);

        var throwOrigin = new GameObject("Throw Origin");
        throwOrigin.transform.SetParent(rightArm.transform, false);
        throwOrigin.transform.localPosition = new Vector3(0f, -0.5f, 0.08f);

        CreatePrimitive("Left Leg", PrimitiveType.Capsule, body.transform,
            new Vector3(0.1f, 0.45f, 0f), new Vector3(0.08f, 0.4f, 0.08f), s_GlowBone);
        CreatePrimitive("Right Leg", PrimitiveType.Capsule, body.transform,
            new Vector3(-0.1f, 0.45f, 0f), new Vector3(0.08f, 0.4f, 0.08f), s_GlowBone);

        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.height = 1.8f;
        capsule.radius = 0.3f;

        var rigidbody = root.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;

        foreach (var collider in root.GetComponentsInChildren<Collider>())
            if (collider != capsule)
                Object.DestroyImmediate(collider);

        var renderers = root.GetComponentsInChildren<Renderer>();

        Wire(boneThrower, "m_Body", body.transform);
        Wire(boneThrower, "m_LeftArm", leftArm.transform);
        Wire(boneThrower, "m_RightArm", rightArm.transform);
        Wire(boneThrower, "m_ThrowOrigin", throwOrigin.transform);
        Wire(boneThrower, "m_BonePrefab", BuildBonePrefab());

        var serialized = new SerializedObject(boneThrower);
        var rendererArray = serialized.FindProperty("m_Renderers");
        rendererArray.arraySize = renderers.Length;
        for (int i = 0; i < renderers.Length; i++)
            rendererArray.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{k_PrefabFolder}/BoneThrower.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// The lobbed projectile: a small bone shape with a faint trail (see
    /// enemigos.md: "el hueso en vuelo es visible y deja estela tenue"). No
    /// collider or rigidbody - ThrownBone moves it along a scripted arc, not
    /// physics, so it always lands exactly on target.
    /// </summary>
    static GameObject BuildBonePrefab()
    {
        var root = new GameObject("Bone");

        var shape = CreatePrimitive("Shape", PrimitiveType.Capsule, root.transform,
            Vector3.zero, new Vector3(0.03f, 0.09f, 0.03f), s_Bone);
        Object.DestroyImmediate(shape.GetComponent<Collider>());

        var trail = root.AddComponent<TrailRenderer>();
        trail.time = 0.35f;
        trail.startWidth = 0.03f;
        trail.endWidth = 0f;
        trail.sharedMaterial = s_BoneTrail;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(0.85f, 0.82f, 0.7f), 0f) },
            new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = gradient;

        root.AddComponent<ThrownBone>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{k_PrefabFolder}/Bone.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// Hides everything but the (unlit) campfire and log pile until the player
    /// throws the first log in, then reveals the rest of the camp at once.
    /// </summary>
    static void BuildCampReveal(GameObject systems, CampfireFuel campfire, params GameObject[] objectsToReveal)
    {
        var reveal = systems.AddComponent<CampRevealController>();
        Wire(reveal, "m_Campfire", campfire);

        var serialized = new SerializedObject(reveal);
        var array = serialized.FindProperty("m_ObjectsToReveal");
        array.arraySize = objectsToReveal.Length;
        for (int i = 0; i < objectsToReveal.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = objectsToReveal[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static SkeletonSpawner BuildSkeletonSpawner(GameObject systems, GameObject rig, CampfireFuel campfire)
    {
        var prefab = BuildSkeletonPrefab();

        var spawnerGo = new GameObject("Skeleton Spawner");
        spawnerGo.transform.SetParent(systems.transform, false);
        var spawner = spawnerGo.AddComponent<SkeletonSpawner>();
        Wire(spawner, "m_SkeletonPrefab", prefab);
        Wire(spawner, "m_Campfire", campfire);

        // Single source of truth for the fixed, non-random spawn ring radius.
        var serializedSpawner = new SerializedObject(spawner);
        serializedSpawner.FindProperty("m_SpawnDistance").floatValue = k_EnemySpawnRadius;
        serializedSpawner.ApplyModifiedPropertiesWithoutUndo();

        if (rig != null)
        {
            var camera = rig.GetComponentInChildren<Camera>();
            if (camera != null)
                Wire(spawner, "m_Target", camera.transform);
        }

        return spawner;
    }

    static BoneThrowerSpawner BuildBoneThrowerSpawner(GameObject systems, GameObject rig, CampfireFuel campfire)
    {
        var prefab = BuildBoneThrowerPrefab();

        var spawnerGo = new GameObject("Bone Thrower Spawner");
        spawnerGo.transform.SetParent(systems.transform, false);
        var spawner = spawnerGo.AddComponent<BoneThrowerSpawner>();
        Wire(spawner, "m_BoneThrowerPrefab", prefab);
        Wire(spawner, "m_Campfire", campfire);

        // Same fixed, non-random spawn ring radius as the Caminante.
        var serializedSpawner = new SerializedObject(spawner);
        serializedSpawner.FindProperty("m_SpawnDistance").floatValue = k_EnemySpawnRadius;
        serializedSpawner.ApplyModifiedPropertiesWithoutUndo();

        if (rig != null)
        {
            var camera = rig.GetComponentInChildren<Camera>();
            if (camera != null)
                Wire(spawner, "m_Target", camera.transform);
        }

        return spawner;
    }

    /// <summary>
    /// Debug-only shortcuts (see CLAUDE.md's one exception to "cero UI"): key '1'
    /// force-spawns a Caminante at the fixed 10 m ring. Disable via its checkbox,
    /// never remove.
    /// </summary>
    static void BuildDebugKeys(GameObject systems, SkeletonSpawner skeletonSpawner,
        BoneThrowerSpawner boneThrowerSpawner, GameManager gameManager)
    {
        var debugKeys = systems.AddComponent<DebugKeys>();
        Wire(debugKeys, "m_SkeletonSpawner", skeletonSpawner);
        Wire(debugKeys, "m_BoneThrowerSpawner", boneThrowerSpawner);
        Wire(debugKeys, "m_GameManager", gameManager);
    }

    /// <summary>
    /// Owns victory (survive the clock) and defeat (fire goes out). See
    /// GameManager for the cero-UI end-of-night behaviour.
    /// </summary>
    static GameManager BuildGameManager(GameObject systems, CampfireFuel campfire,
        NightEnvironmentController nightEnvironment, SkeletonSpawner skeletonSpawner,
        BoneThrowerSpawner boneThrowerSpawner)
    {
        var gameManagerGo = new GameObject("Game Manager");
        gameManagerGo.transform.SetParent(systems.transform, false);
        var gameManager = gameManagerGo.AddComponent<GameManager>();
        Wire(gameManager, "m_Campfire", campfire);
        Wire(gameManager, "m_NightEnvironment", nightEnvironment);
        Wire(gameManager, "m_SkeletonSpawner", skeletonSpawner);
        Wire(gameManager, "m_BoneThrowerSpawner", boneThrowerSpawner);
        return gameManager;
    }

    static GameObject BuildTreasure(Transform parent, Vector3 position)
    {
        var treasure = new GameObject("Treasure Pedestal");
        treasure.transform.SetParent(parent, false);
        treasure.transform.localPosition = position;

        CreatePrimitive("Pedestal", PrimitiveType.Cylinder, treasure.transform,
            new Vector3(0f, 0.5f, 0f), new Vector3(0.7f, 0.5f, 0.7f), s_Stone);
        CreatePrimitive("Chest", PrimitiveType.Cube, treasure.transform,
            new Vector3(0f, 1.2f, 0f), new Vector3(0.6f, 0.4f, 0.45f), s_Treasure);

        return treasure;
    }

    // To the player's right, close enough to reach without walking, clear of the
    // log pile and bench on the same side.
    static readonly Vector3 k_WeaponRackPosition = new Vector3(1.6f, 0f, 0f);

    /// <summary>
    /// The stand holding the axe until the camp is revealed. Hidden (along
    /// with its contents) by CampRevealController until the player feeds the
    /// fire for the first time.
    /// </summary>
    /// <summary>
    /// Camp dressing only now: the axe itself no longer lives on the rack, it
    /// starts already in the player's right hand (see BuildAxe).
    /// </summary>
    static GameObject BuildWeaponRack(Transform parent)
    {
        var rack = new GameObject("Weapon Rack");
        rack.transform.SetParent(parent, false);
        rack.transform.localPosition = k_WeaponRackPosition;

        CreatePrimitive("Rack Base", PrimitiveType.Cylinder, rack.transform,
            new Vector3(0f, 0.05f, 0f), new Vector3(0.3f, 0.05f, 0.3f), s_Stone);
        CreatePrimitive("Rack Post", PrimitiveType.Cylinder, rack.transform,
            new Vector3(0f, 0.6f, 0f), new Vector3(0.05f, 0.6f, 0.05f), s_Trunk);

        return rack;
    }

    /// <summary>
    /// The player's melee weapon: banishes any enemy it touches (see
    /// EnemyBanisher) and always flies back to whichever hand last held it
    /// (see Axe). Starts already in the player's right hand, handle first, so
    /// there's nothing to walk to or reach for. The root pivot sits at the axe
    /// head so EnemyBanisher's overlap check (centred on transform.position)
    /// covers the head rather than the handle, and the only collider on the
    /// rigidbody spans the handle so the hand can only grab it there, by the
    /// haft, never by the head.
    /// </summary>
    static Axe BuildAxe(Transform parent, Transform leftHand, Transform rightHand)
    {
        var root = new GameObject("Axe");
        root.transform.SetParent(parent, false);

        const float k_HandleLength = 0.5f;
        var handleGripOffset = new Vector3(0f, -k_HandleLength * 0.58f, 0f);
        var initialRotation = Quaternion.Euler(0f, 0f, 15f);

        // Placed so the handle (not the head, where the root pivots) sits at the
        // right hand, exactly like it needs to be caught after a throw.
        root.transform.rotation = initialRotation;
        root.transform.position = rightHand != null
            ? rightHand.position - initialRotation * handleGripOffset
            : new Vector3(0.1f, 0.75f, 0.05f);

        var handle = CreatePrimitive("Handle", PrimitiveType.Cylinder, root.transform,
            new Vector3(0f, -k_HandleLength * 0.5f, 0f), new Vector3(0.028f, k_HandleLength * 0.5f, 0.028f), s_Trunk);
        Object.DestroyImmediate(handle.GetComponent<Collider>());

        var head = CreatePrimitive("Head", PrimitiveType.Cube, root.transform,
            Vector3.zero, new Vector3(0.05f, 0.08f, 0.05f), s_DarkMetal);
        Object.DestroyImmediate(head.GetComponent<Collider>());

        var blade = CreatePrimitive("Blade", PrimitiveType.Cube, root.transform,
            new Vector3(0.11f, 0.02f, 0f), new Vector3(0.2f, 0.16f, 0.018f), s_Metal);
        blade.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
        Object.DestroyImmediate(blade.GetComponent<Collider>());

        var poll = CreatePrimitive("Poll", PrimitiveType.Cube, root.transform,
            new Vector3(-0.045f, 0f, 0f), new Vector3(0.06f, 0.05f, 0.05f), s_DarkMetal);
        Object.DestroyImmediate(poll.GetComponent<Collider>());

        var rigidbody = root.AddComponent<Rigidbody>();
        rigidbody.mass = 0.9f;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        // Physics never gets to spin this: an uncontrolled tumble (gravity, a knock
        // against nearby clutter) was leaving the axe in random, "flipped" looking
        // orientations. Rotation only ever changes by explicit grab/throw handling.
        rigidbody.constraints = RigidbodyConstraints.FreezeRotation;

        // Only the handle is collidable, so both near-hand and distance-hand
        // grab can only take hold of the axe by its handle, never by the head.
        var collider = root.AddComponent<CapsuleCollider>();
        collider.center = handleGripOffset;
        collider.height = k_HandleLength * 0.84f;
        collider.radius = 0.03f;
        // Low friction so the axe slides off nearby clutter (log pile, rack, campfire
        // stones) instead of catching on it while thrown or homing back.
        collider.material = s_LowFriction;

        var grabbable = root.AddComponent<Grabbable>();

        var handGrab = root.AddComponent<HandGrabInteractable>();
        handGrab.InjectRigidbody(rigidbody);
        handGrab.InjectOptionalPointableElement(grabbable);

        var distanceHandGrab = root.AddComponent<DistanceHandGrabInteractable>();
        distanceHandGrab.InjectRigidbody(rigidbody);
        distanceHandGrab.InjectOptionalPointableElement(grabbable);

        root.AddComponent<EnemyBanisher>();

        var axe = root.AddComponent<Axe>();
        Wire(axe, "m_LeftHand", leftHand);
        Wire(axe, "m_RightHand", rightHand);

        var axeSerialized = new SerializedObject(axe);
        axeSerialized.FindProperty("m_HandleGripOffset").vector3Value = handleGripOffset;
        axeSerialized.ApplyModifiedPropertiesWithoutUndo();

        return axe;
    }

    /// <summary>
    /// Builds the resortera and wires it to both hand anchors, the axe (it can
    /// only appear while the axe is out of hand), the head (aim direction) and
    /// the campfire (ember cost). See Slingshot for the full behaviour.
    /// </summary>
    static void BuildSlingshot(GameObject systems, Transform leftHand, Transform rightHand,
        Transform head, Axe axe, CampfireFuel campfire)
    {
        var slingshotGo = new GameObject("Slingshot");
        slingshotGo.transform.SetParent(systems.transform, false);

        // On its own child, not the Slingshot root: toggling this off must not disable
        // the controller's own Update() along with it.
        var bandGo = new GameObject("Band");
        bandGo.transform.SetParent(slingshotGo.transform, false);
        var band = bandGo.AddComponent<LineRenderer>();
        band.positionCount = 2;
        band.startWidth = 0.015f;
        band.endWidth = 0.015f;
        band.sharedMaterial = s_SlingBand;
        band.useWorldSpace = true;
        bandGo.SetActive(false);

        var emberVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        emberVisual.name = "Ember Visual";
        emberVisual.transform.SetParent(slingshotGo.transform, false);
        emberVisual.transform.localScale = Vector3.one * 0.06f;
        Object.DestroyImmediate(emberVisual.GetComponent<Collider>());
        emberVisual.GetComponent<Renderer>().sharedMaterial = s_EmberGlow;

        var stretchAudioGo = new GameObject("Stretch Audio");
        stretchAudioGo.transform.SetParent(slingshotGo.transform, false);
        var stretchAudio = stretchAudioGo.AddComponent<AudioSource>();

        var oneShotAudio = slingshotGo.AddComponent<AudioSource>();

        var slingshot = slingshotGo.AddComponent<Slingshot>();
        Wire(slingshot, "m_LeftHand", leftHand);
        Wire(slingshot, "m_RightHand", rightHand);
        Wire(slingshot, "m_Target", head);
        Wire(slingshot, "m_Axe", axe);
        Wire(slingshot, "m_Campfire", campfire);
        Wire(slingshot, "m_Band", band);
        Wire(slingshot, "m_EmberVisual", emberVisual.transform);
        Wire(slingshot, "m_StretchAudio", stretchAudio);
        Wire(slingshot, "m_OneShotAudio", oneShotAudio);
        Wire(slingshot, "m_EmberProjectilePrefab", BuildEmberProjectilePrefab());
    }

    /// <summary>
    /// The resortera's shot: no collider/rigidbody are shared with the Bone
    /// prefab even though they look similar, because this one is a real
    /// ballistic projectile (the player aims it) rather than a scripted arc to
    /// a fixed target.
    /// </summary>
    static GameObject BuildEmberProjectilePrefab()
    {
        var root = new GameObject("Ember");

        var shape = CreatePrimitive("Shape", PrimitiveType.Sphere, root.transform,
            Vector3.zero, new Vector3(0.05f, 0.05f, 0.05f), s_EmberGlow);
        Object.DestroyImmediate(shape.GetComponent<Collider>());

        var rigidbody = root.AddComponent<Rigidbody>();
        rigidbody.mass = 0.05f;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var collider = root.AddComponent<SphereCollider>();
        collider.radius = 0.05f;

        var trail = root.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.startWidth = 0.06f;
        trail.endWidth = 0f;
        trail.sharedMaterial = s_EmberTrail;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.7f, 0.2f), 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = gradient;

        root.AddComponent<EmberProjectile>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{k_PrefabFolder}/Ember.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;

        if (material != null)
            go.GetComponent<Renderer>().sharedMaterial = material;

        return go;
    }

    static void RemoveCollider(GameObject go)
    {
        var collider = go.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);
    }

    static Material CreateMaterial(string name, Color color, float metallic, float smoothness, Color? emission = null, string shaderName = "Universal Render Pipeline/Lit")
    {
        var shader = Shader.Find(shaderName) ?? Shader.Find("Standard");
        var material = new Material(shader) { name = name };
        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        var path = $"{k_MaterialFolder}/{name}.mat";
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static PhysicsMaterial CreatePhysicsMaterial(string name, float friction, float bounciness)
    {
        var material = new PhysicsMaterial(name)
        {
            dynamicFriction = friction,
            staticFriction = friction,
            bounciness = bounciness,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        var path = $"{k_MaterialFolder}/{name}.physicMaterial";
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void Wire(Object target, string propertyName, Object value)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogWarning($"[GameSceneBuilder] Property '{propertyName}' not found on {target.name}");
            return;
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void AddToBuildSettings(string scenePath)
    {
        var scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(scenePath, true)
        };

        const string sampleScene = "Assets/Scenes/SampleScene.unity";
        if (File.Exists(sampleScene))
            scenes.Add(new EditorBuildSettingsScene(sampleScene, false));

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var leaf = Path.GetFileName(path);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            return;

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}

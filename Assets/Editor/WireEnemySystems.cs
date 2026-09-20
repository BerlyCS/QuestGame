using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-off editor utility for the soph/mod merge: the enemy and flow systems
/// exist in code but were never wired into Assets/Scenes/Game.unity (main's
/// scene was kept during the merge). This adds and wires:
///   - BoneThrowerSpawner (Lanzahuesos)
///   - HunterSpawner (Cazador)
///   - GameManager (survival clock / win-lose)
///   - DebugKeys (1/2/3 spawn each enemy, N skips the clock)
/// and repairs references the merge left stale: the campfire's serialized fuel
/// fields still use the old normalized schema while the merged CampfireFuel
/// expects seconds, and PlayerHealth's new head/vignette fields are unwired.
///
/// Idempotent: existing objects are reused and only missing references are
/// filled in. Run from Tools > Game > Wire Enemy Systems.
/// </summary>
public static class WireEnemySystems
{
    const string k_ScenePath = "Assets/Scenes/Game.unity";
    const string k_HunterPrefabPath = "Assets/Prefabs/Gameplay/Hunter.prefab";
    const string k_BoneThrowerPrefabPath = "Assets/Prefabs/Gameplay/BoneThrower.prefab";
    const string k_TeethMaterialPath = "Assets/Materials/Game/M_Teeth.mat";
    const string k_VignetteMaterialPath = "Assets/Materials/Game/M_DamageVignette.mat";
    const string k_SkyMaterialPath = "Assets/Day-Night Skyboxes/Materials/SkyMidnight.mat";

    [MenuItem("Tools/Game/Wire Enemy Systems")]
    public static void Wire()
    {
        var scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);

        var campfire = Object.FindAnyObjectByType<CampfireFuel>();
        var night = Object.FindAnyObjectByType<NightEnvironmentController>();
        var skeletonSpawner = Object.FindAnyObjectByType<SkeletonSpawner>();
        var playerHealth = Object.FindAnyObjectByType<PlayerHealth>();

        if (campfire == null || skeletonSpawner == null)
        {
            EditorUtility.DisplayDialog(
                "Wire Enemy Systems",
                "Game.unity needs a CampfireFuel and a SkeletonSpawner first.",
                "OK");
            return;
        }

        var systems = GameObject.Find("Game Systems");
        if (systems == null)
        {
            systems = new GameObject("Game Systems");
            Undo.RegisterCreatedObjectUndo(systems, "Create Game Systems");
        }

        var camera = FindCamera();

        // The scene still carries the pre-merge fuel schema (0-1 fractions with
        // m_StartingFuelNormalized). The merged CampfireFuel works in seconds.
        var campfireSo = new SerializedObject(campfire);
        SetFloat(campfireSo, "m_MaxFuel", 90f);
        SetFloat(campfireSo, "m_StartingFuel", 55f);
        SetFloat(campfireSo, "m_BurnRatePerSecond", 0.55f);
        campfireSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(campfire);

        var boneSpawner = Object.FindAnyObjectByType<BoneThrowerSpawner>();
        if (boneSpawner == null)
        {
            var go = new GameObject("Bone Thrower Spawner");
            go.transform.SetParent(systems.transform, false);
            boneSpawner = go.AddComponent<BoneThrowerSpawner>();
            Undo.RegisterCreatedObjectUndo(go, "Create Bone Thrower Spawner");
        }
        SetRef(boneSpawner, "m_BoneThrowerPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(k_BoneThrowerPrefabPath));
        SetRef(boneSpawner, "m_Campfire", campfire);
        if (camera != null)
            SetRef(boneSpawner, "m_Target", camera.transform);

        var hunterSpawner = Object.FindAnyObjectByType<HunterSpawner>();
        if (hunterSpawner == null)
        {
            var go = new GameObject("Hunter Spawner");
            go.transform.SetParent(systems.transform, false);
            hunterSpawner = go.AddComponent<HunterSpawner>();
            Undo.RegisterCreatedObjectUndo(go, "Create Hunter Spawner");
        }
        SetRef(hunterSpawner, "m_HunterPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(k_HunterPrefabPath));
        if (playerHealth != null)
            SetRef(hunterSpawner, "m_Player", playerHealth);

        if (playerHealth != null)
        {
            if (camera != null)
                SetRef(playerHealth, "m_Head", camera.transform);
            var vignette = AssetDatabase.LoadAssetAtPath<Material>(k_VignetteMaterialPath);
            if (vignette != null)
                SetRef(playerHealth, "m_VignetteMaterial", vignette);
        }

        var gameManager = Object.FindAnyObjectByType<GameManager>();
        if (gameManager == null)
        {
            var go = new GameObject("Game Manager");
            go.transform.SetParent(systems.transform, false);
            gameManager = go.AddComponent<GameManager>();
            Undo.RegisterCreatedObjectUndo(go, "Create Game Manager");
        }
        SetRef(gameManager, "m_Campfire", campfire);
        if (night != null)
            SetRef(gameManager, "m_NightEnvironment", night);
        SetRef(gameManager, "m_SkeletonSpawner", skeletonSpawner);
        SetRef(gameManager, "m_BoneThrowerSpawner", boneSpawner);
        SetRef(gameManager, "m_HunterSpawner", hunterSpawner);
        if (playerHealth != null)
            SetRef(gameManager, "m_PlayerHealth", playerHealth);
        var teeth = AssetDatabase.LoadAssetAtPath<Material>(k_TeethMaterialPath);
        if (teeth != null)
            SetRef(gameManager, "m_TeethMaterial", teeth);
        var chest = FindChestRenderer();
        if (chest != null)
            SetRef(gameManager, "m_ChestRenderer", chest);

        SetRef(hunterSpawner, "m_GameManager", gameManager);
        if (night != null)
        {
            SetRef(night, "m_GameManager", gameManager);
            var sky = AssetDatabase.LoadAssetAtPath<Material>(k_SkyMaterialPath);
            if (sky != null)
                SetRef(night, "m_SkyMaterial", sky);
        }

        var debug = Object.FindAnyObjectByType<DebugKeys>();
        if (debug == null)
        {
            var go = new GameObject("Debug Keys");
            go.transform.SetParent(systems.transform, false);
            debug = go.AddComponent<DebugKeys>();
            Undo.RegisterCreatedObjectUndo(go, "Create Debug Keys");
        }
        SetRef(debug, "m_SkeletonSpawner", skeletonSpawner);
        SetRef(debug, "m_BoneThrowerSpawner", boneSpawner);
        SetRef(debug, "m_HunterSpawner", hunterSpawner);
        SetRef(debug, "m_GameManager", gameManager);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log("[WireEnemySystems] Enemy systems wired into Game.unity.");
    }

    static Camera FindCamera()
    {
        var cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        Camera best = null;
        foreach (var candidate in cameras)
        {
            if (candidate == null || !candidate.enabled)
                continue;
            if (best == null || candidate.depth > best.depth)
                best = candidate;
        }

        return best;
    }

    static Renderer FindChestRenderer()
    {
        var chest = GameObject.Find("Chest");
        if (chest != null)
        {
            var renderer = chest.GetComponent<Renderer>();
            if (renderer != null)
                return renderer;
        }

        var treasure = GameObject.Find("Treasure Pedestal");
        return treasure != null ? treasure.GetComponentInChildren<Renderer>() : null;
    }

    static void SetRef(Object target, string property, Object value)
    {
        var serialized = new SerializedObject(target);
        var field = serialized.FindProperty(property);
        if (field == null)
        {
            Debug.LogWarning($"[WireEnemySystems] {target.GetType().Name} has no serialized field '{property}'.");
            return;
        }

        field.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void SetFloat(SerializedObject serialized, string property, float value)
    {
        var field = serialized.FindProperty(property);
        if (field != null)
            field.floatValue = value;
    }
}

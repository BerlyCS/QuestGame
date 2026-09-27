using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Wires the recorded audio in Assets/Audio into Game.unity, by code (see
/// CLAUDE.md: never hand-edit the .unity file):
///
/// - finalfeliz.mp3: the victory music (GameManager.m_VictoryMusic);
/// - heavybreath.mp3: the player's tired breathing, on a PlayerBreathing
///   component added next to PlayerHealth;
/// - ghostbreath.mp3: the Coronado's spatialised breathing
///   (Coronado.m_BreathClip). Rebuilds the Coronado first so its breath
///   source has the current spatial settings (see CoronadoBuilder.BuildBreath).
///
/// Every clip is preloaded so it sounds the instant it is needed; the short
/// one-shots are decompressed on load. Idempotent: a second run finds the
/// component and writes the same values.
/// </summary>
public static class GameAudioWiring
{
    const string k_ScenePath = "Assets/Scenes/Game.unity";
    const string k_VictoryMusicPath = "Assets/Audio/finalfeliz.mp3";
    const string k_HeavyBreathPath = "Assets/Audio/heavybreath.mp3";
    const string k_GhostBreathPath = "Assets/Audio/ghostbreath.mp3";

    [MenuItem("Tools/Game/Wire Game Audio")]
    public static void Wire()
    {
        var victoryMusic = LoadClip(k_VictoryMusicPath, AudioClipLoadType.DecompressOnLoad);
        var heavyBreath = LoadClip(k_HeavyBreathPath, AudioClipLoadType.CompressedInMemory);
        var ghostBreath = LoadClip(k_GhostBreathPath, AudioClipLoadType.CompressedInMemory);

        // Opens and saves Game.unity itself; its breath source must be current
        // before the ghost breath is assigned to it.
        CoronadoBuilder.Build();

        var scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);

        var gameManager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        var playerHealth = Object.FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Include);
        var coronado = Object.FindAnyObjectByType<Coronado>(FindObjectsInactive.Include);
        if (gameManager == null || playerHealth == null || coronado == null)
        {
            Debug.LogError("[GameAudioWiring] Game.unity is missing the GameManager, PlayerHealth or Coronado; nothing was wired.");
            return;
        }

        if (!playerHealth.TryGetComponent(out PlayerBreathing breathing))
            breathing = Undo.AddComponent<PlayerBreathing>(playerHealth.gameObject);

        SetRef(breathing, "m_HeavyBreath", heavyBreath);
        SetRef(gameManager, "m_VictoryMusic", victoryMusic);
        SetRef(gameManager, "m_PlayerBreathing", breathing);
        SetRef(coronado, "m_BreathClip", ghostBreath);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[GameAudioWiring] Victory music, player breathing and ghost breath wired into Game.unity.");
    }

    static AudioClip LoadClip(string path, AudioClipLoadType loadType)
    {
        if (AssetImporter.GetAtPath(path) is AudioImporter importer)
        {
            var settings = importer.defaultSampleSettings;
            if (settings.loadType != loadType || !settings.preloadAudioData)
            {
                settings.loadType = loadType;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }

        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null)
            Debug.LogWarning($"[GameAudioWiring] No audio clip at {path}; that slot was left empty.");
        return clip;
    }

    static void SetRef(Object target, string property, Object value)
    {
        var serialized = new SerializedObject(target);
        var field = serialized.FindProperty(property);
        if (field == null)
        {
            Debug.LogWarning($"[GameAudioWiring] {target.GetType().Name} has no serialized field '{property}'.");
            return;
        }

        field.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}

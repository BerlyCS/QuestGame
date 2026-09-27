using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tunes how much punishment the player can take, by code (see CLAUDE.md:
/// never hand-edit the .unity file). A Cazador's claw now lands a flat
/// <see cref="k_ClawDamage"/> all night long instead of softening as the night
/// goes on (which had stretched a death to ~35 claws mid-night), so the
/// player always dies after about <see cref="k_ClawsToDie"/> claws: the
/// Cazador prefab hits for 10, scaled by GameManager's player-damage
/// multiplier, which is pinned here with no per-step ramp.
/// Idempotent: a second run writes the same values.
/// </summary>
public static class DifficultyTuner
{
    const string k_ScenePath = "Assets/Scenes/Game.unity";
    const float k_ClawsToDie = 13f;
    const float k_HunterBaseDamage = 10f;
    const float k_ClawDamage = 12f;

    [MenuItem("Tools/Game/Tune Player Damage")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);

        var gameManager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        var playerHealth = Object.FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Include);
        if (gameManager == null || playerHealth == null)
        {
            Debug.LogError("[DifficultyTuner] Game.unity is missing the GameManager or PlayerHealth; nothing was changed.");
            return;
        }

        float multiplier = k_ClawDamage / k_HunterBaseDamage;
        var serialized = new SerializedObject(gameManager);
        serialized.FindProperty("m_PlayerDamageStart").floatValue = multiplier;
        serialized.FindProperty("m_PlayerDamageRampPerStep").floatValue = 0f;
        serialized.FindProperty("m_PlayerDamageFloor").floatValue = multiplier;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(gameManager);

        float maxHealth = new SerializedObject(playerHealth).FindProperty("m_MaxHealth").floatValue;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[DifficultyTuner] Cazador claw = {k_ClawDamage} all night; {maxHealth} health = {Mathf.CeilToInt(maxHealth / k_ClawDamage)} claws to die (target {k_ClawsToDie}).");
    }
}

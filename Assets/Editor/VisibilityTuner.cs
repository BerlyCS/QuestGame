using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes the dawn progressive across the whole night instead of starting at
/// two thirds of the clock: the sky warms from the first minute and the sun is
/// up well before the end. Applied by code (see CLAUDE.md: never hand-edit the
/// .unity file). Idempotent: a second run writes the same values.
///
/// The forest is deliberately left untouched: hidden enemies announce
/// themselves instead (see <see cref="EnemyTell"/>).
/// </summary>
public static class VisibilityTuner
{
    const string k_ScenePath = "Assets/Scenes/Game.unity";
    const float k_DawnStart = 0.05f;
    const float k_DawnCurve = 1.3f;

    [MenuItem("Tools/Game/Apply Visibility Tuning")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);

        var night = Object.FindAnyObjectByType<NightEnvironmentController>(FindObjectsInactive.Include);
        if (night == null)
        {
            Debug.LogWarning("[VisibilityTuner] No NightEnvironmentController found; the dawn was not tuned.");
            return;
        }

        var serialized = new SerializedObject(night);
        serialized.FindProperty("m_DawnStart").floatValue = k_DawnStart;
        serialized.FindProperty("m_DawnCurve").floatValue = k_DawnCurve;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(night);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[VisibilityTuner] Dawn now starts at {k_DawnStart:P0} of the night (curve {k_DawnCurve}).");
    }
}

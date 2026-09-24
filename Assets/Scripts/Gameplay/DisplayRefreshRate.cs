using System.Collections;
using UnityEngine;

/// <summary>
/// Asks the Quest runtime for a target display refresh rate at startup instead of
/// leaving the app at the device default (many apps boot at 72 Hz on Quest 2 or
/// 90 Hz on Quest 3 depending on model). A higher rate shrinks the per-frame
/// budget proportionally, so the target is picked conservatively and silently
/// falls back to the closest rate the device actually reports.
///
/// This goes through <see cref="OVRPlugin.systemDisplayFrequency"/>, which the
/// Meta XR SDK shims onto the OpenXR <c>XR_FB_display_refresh_rate</c> extension
/// (requested by the enabled MetaXRFeature). If the runtime never initializes -
/// or the editor/simulator does not expose the extension - the request is skipped
/// and the app keeps the system default.
///
/// The component bootstraps itself on application start (see <see cref="Bootstrap"/>)
/// so it works without being placed in the generated scene; drop it on a GameObject
/// only if you want to tune the rate per-build in the Inspector.
/// </summary>
[DisallowMultipleComponent]
public class DisplayRefreshRate : MonoBehaviour
{
    /// <summary>Refresh rate (Hz) the app asks for when nothing overrides it.</summary>
    public const float DefaultTargetHz = 90f;

    [Tooltip("Refresh rate (Hz) to request. 90 is the safe Quest default; 72 is " +
        "cheaper, 120 needs the device unlocked for it and a much smaller frame budget.")]
    [SerializeField] float m_TargetHz = DefaultTargetHz;

    [Tooltip("Seconds to wait for the XR runtime to initialize before giving up. " +
        "The request cannot succeed before the OpenXR session exists.")]
    [SerializeField] float m_InitTimeout = 10f;

    [Tooltip("Log the chosen rate and the rates the device offered.")]
    [SerializeField] bool m_Log = true;

    /// <summary>
    /// Creates the driver on application start when the scene does not already
    /// contain one, mirroring how <see cref="HapticsUtility"/> spawns its hidden
    /// helper. Runs once per play session, not on every scene reload.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<DisplayRefreshRate>() != null)
            return;

        var go = new GameObject("[DisplayRefreshRate]");
        DontDestroyOnLoad(go);
        go.AddComponent<DisplayRefreshRate>();
    }

    IEnumerator Start()
    {
        // The OpenXR session does not exist yet, so OVRPlugin refuses to report or
        // set a frequency; poll until it is up (or we time out).
        float deadline = Time.realtimeSinceStartup + Mathf.Max(0f, m_InitTimeout);
        while (!OVRPlugin.initialized && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (!OVRPlugin.initialized)
        {
            if (m_Log)
                Debug.LogWarning("[DisplayRefreshRate] XR runtime never initialized; " +
                    "keeping the system default refresh rate.");
            yield break;
        }

        float[] available = OVRPlugin.systemDisplayFrequenciesAvailable;
        float chosen = PickRate(available, m_TargetHz);
        OVRPlugin.systemDisplayFrequency = chosen;

        if (!m_Log)
            yield break;

        string offered = available.Length > 0
            ? string.Join(", ", System.Array.ConvertAll(available, hz => hz.ToString("0")))
            : "unknown";
        Debug.Log($"[DisplayRefreshRate] Requested {chosen:0} Hz " +
            $"(target {m_TargetHz:0}). Device offers: {offered} Hz.");
    }

    /// <summary>
    /// Chooses the closest rate the device offers without exceeding the target:
    /// exact match wins, otherwise the highest rate below it, otherwise the
    /// slowest rate offered (so a 120/90-only panel is never asked for 72).
    /// Falls back to the raw target when the device reports nothing.
    /// </summary>
    static float PickRate(float[] available, float target)
    {
        if (available == null || available.Length == 0)
            return target;

        float best = float.MaxValue;
        float slowest = float.MaxValue;

        foreach (float hz in available)
        {
            if (hz <= 0f)
                continue;

            slowest = Mathf.Min(slowest, hz);

            if (hz <= target + 0.01f && hz > best)
                best = hz;
        }

        if (best != float.MaxValue)
            return best;
        return slowest != float.MaxValue ? slowest : target;
    }
}

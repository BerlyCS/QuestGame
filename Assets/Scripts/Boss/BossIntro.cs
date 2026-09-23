using System.Collections;
using UnityEngine;

/// <summary>
/// The Coronado's entrance (see JEFE_FINAL.md section 3), fired once the
/// survival clock crosses <see cref="m_TriggerAtSurvival"/> (the middle of the
/// night). Every Caminante, Lanzahuesos and Cazador currently alive stops,
/// turns to face the tree line to the north and flees into the fog instead of
/// dying - the best possible telegraph: if the walking dead are scared,
/// the player should be too. The world then goes quiet (the fire's crackle is
/// the scene's only ambient loop, so ducking it covers both "el audio
/// ambiente desaparece" and "el crepitar se amortigua"), holds three seconds
/// of total silence, and the Coronado's eyes ignite among the trees at the
/// fixed 15 m boss ring, centred on the campfire like every other enemy ring
/// (see CLAUDE.md).
///
/// GameManager.SetBossPhaseActive(true) is what actually stops the spawners -
/// the "WaveManager" - for the rest of the night, including guarding against a
/// mid-fight relight resuming them.
/// </summary>
[DisallowMultipleComponent]
public class BossIntro : MonoBehaviour
{
    /// <summary>World +Z: the horizon the player already faces (see
    /// NightEnvironmentController.m_SunYaw, "180 = the +Z horizon").</summary>
    static readonly Vector3 k_North = Vector3.forward;

    [Header("Trigger")]
    [Tooltip("Fraction of the survival clock (0-1) at which the boss's entrance begins.")]
    [SerializeField] float m_TriggerAtSurvival = 0.5f;

    [Header("References")]
    [SerializeField] GameManager m_GameManager;
    [SerializeField] CampfireFuel m_Campfire;
    [SerializeField] Coronado m_Coronado;

    [Header("Timing")]
    [Tooltip("How long the fire's crackle takes to fade out (\"se amortigua\") before the silence.")]
    [SerializeField] float m_AudioFadeDuration = 0.6f;
    [SerializeField] float m_SilenceDuration = 3f;

    [Header("Placement")]
    [Tooltip("Distance from the campfire the Coronado's eyes ignite at (the fixed Fase 1 / Acecho ring).")]
    [SerializeField] float m_AppearDistance = 15f;

    bool m_Triggered;

    void Update()
    {
        if (m_Triggered || m_GameManager == null || !m_GameManager.HasStarted)
            return;
        if (m_GameManager.SurvivalNormalized < m_TriggerAtSurvival)
            return;

        Trigger();
    }

    /// <summary>Debug-only: fires the entrance immediately, bypassing the survival-clock gate.</summary>
    public void DebugTriggerNow() => Trigger();

    void Trigger()
    {
        if (m_Triggered)
            return;
        m_Triggered = true;
        StartCoroutine(PlayIntro());
    }

    IEnumerator PlayIntro()
    {
        FleeAllEnemies();

        if (m_GameManager != null)
            m_GameManager.SetBossPhaseActive(true);

        yield return FadeFireAudio(1f, 0f, m_AudioFadeDuration);
        yield return new WaitForSeconds(m_SilenceDuration);

        if (m_Coronado != null && m_Campfire != null)
            m_Coronado.Appear(m_Campfire.transform.position + k_North * m_AppearDistance);
    }

    /// <summary>
    /// Stops every Caminante, Lanzahuesos and Cazador in place, turns them to
    /// face north and sends them running into the fog - JEFE_FINAL.md steps 1-3.
    /// </summary>
    void FleeAllEnemies()
    {
        foreach (var skeleton in Object.FindObjectsByType<Skeleton>(FindObjectsInactive.Exclude))
        {
            if (skeleton != null && skeleton.IsAlive && !skeleton.IsRetreating)
                skeleton.Retreat(k_North);
        }

        foreach (var boneThrower in Object.FindObjectsByType<BoneThrower>(FindObjectsInactive.Exclude))
        {
            if (boneThrower != null && boneThrower.IsAlive && !boneThrower.IsRetreating)
                boneThrower.Retreat(k_North);
        }
    }

    IEnumerator FadeFireAudio(float from, float to, float duration)
    {
        if (m_Campfire == null)
            yield break;

        if (duration <= 0f)
        {
            m_Campfire.SetAudioDuckMultiplier(to);
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            m_Campfire.SetAudioDuckMultiplier(Mathf.Lerp(from, to, t / duration));
            yield return null;
        }

        m_Campfire.SetAudioDuckMultiplier(to);
    }
}

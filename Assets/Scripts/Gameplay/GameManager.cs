using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the two ways the night ends. Cero UI (see CLAUDE.md): no text, no Game
/// Over screen, no buttons - the only feedback is the world going bright or
/// black, then the scene reloads.
///
/// Victory: survive <see cref="m_SurvivalDuration"/> seconds. Every Caminante
/// freezes and collapses, the world lights up for
/// <see cref="m_VictoryLitDuration"/> seconds, then restarts.
///
/// Defeat: the campfire goes out (CampfireFuel.OnExtinguished). The world
/// fades to black over <see cref="m_DefeatFadeDuration"/> seconds, then
/// restarts at the <see cref="m_DefeatRestartDelay"/> mark.
/// </summary>
[DisallowMultipleComponent]
public class GameManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] CampfireFuel m_Campfire;
    [SerializeField] NightEnvironmentController m_NightEnvironment;
    [SerializeField] SkeletonSpawner m_SkeletonSpawner;
    [SerializeField] BoneThrowerSpawner m_BoneThrowerSpawner;

    [Header("Victory")]
    [SerializeField] float m_SurvivalDuration = 180f;
    [SerializeField] float m_VictoryLitDuration = 10f;

    [Header("Defeat")]
    [SerializeField] float m_DefeatFadeDuration = 1.5f;
    [SerializeField] float m_DefeatRestartDelay = 3f;

    float m_Elapsed;
    bool m_GameOver;

    void OnEnable()
    {
        if (m_Campfire != null)
            m_Campfire.OnExtinguished.AddListener(HandleExtinguished);
    }

    void OnDisable()
    {
        if (m_Campfire != null)
            m_Campfire.OnExtinguished.RemoveListener(HandleExtinguished);
    }

    void Update()
    {
        if (m_GameOver)
            return;

        m_Elapsed += Time.deltaTime;
        if (m_Elapsed >= m_SurvivalDuration)
            Win();
    }

    /// <summary>Debug-only: jumps the survival clock (see DebugKeys).</summary>
    public void DebugSetElapsed(float seconds)
    {
        m_Elapsed = seconds;
    }

    void HandleExtinguished()
    {
        if (m_GameOver)
            return;

        Lose();
    }

    void Win()
    {
        m_GameOver = true;

        if (m_SkeletonSpawner != null)
            m_SkeletonSpawner.enabled = false;
        if (m_BoneThrowerSpawner != null)
            m_BoneThrowerSpawner.enabled = false;

        foreach (var skeleton in Object.FindObjectsByType<Skeleton>(FindObjectsInactive.Exclude))
            skeleton.Collapse();
        foreach (var boneThrower in Object.FindObjectsByType<BoneThrower>(FindObjectsInactive.Exclude))
            boneThrower.Collapse();

        if (m_NightEnvironment != null)
            m_NightEnvironment.ForcedNormalized = 1f;

        StartCoroutine(RestartAfter(m_VictoryLitDuration));
    }

    void Lose()
    {
        m_GameOver = true;

        if (m_SkeletonSpawner != null)
            m_SkeletonSpawner.enabled = false;
        if (m_BoneThrowerSpawner != null)
            m_BoneThrowerSpawner.enabled = false;

        StartCoroutine(FadeToBlackThenRestart());
    }

    IEnumerator FadeToBlackThenRestart()
    {
        float startNormalized = m_Campfire != null ? m_Campfire.FuelNormalized : 0f;

        float t = 0f;
        while (t < m_DefeatFadeDuration)
        {
            t += Time.deltaTime;
            if (m_NightEnvironment != null)
                m_NightEnvironment.ForcedNormalized = Mathf.Lerp(startNormalized, 0f, t / m_DefeatFadeDuration);
            yield return null;
        }

        if (m_NightEnvironment != null)
            m_NightEnvironment.ForcedNormalized = 0f;

        float remaining = m_DefeatRestartDelay - m_DefeatFadeDuration;
        if (remaining > 0f)
            yield return new WaitForSeconds(remaining);

        Restart();
    }

    IEnumerator RestartAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Restart();
    }

    void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}

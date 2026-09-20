using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the ways the night ends. Cero UI (see CLAUDE.md): no text, no Game
/// Over screen, no buttons - the world goes bright with a calm birdsong on
/// victory, or black with a sinister laugh on defeat, then the scene reloads.
///
/// Victory: survive <see cref="m_SurvivalDuration"/> seconds. Every Caminante
/// freezes and collapses, the world lights up for
/// <see cref="m_VictoryLitDuration"/> seconds, then restarts.
///
/// Defeat: the campfire goes out (CampfireFuel.OnExtinguished) or the player's
/// life runs out (PlayerHealth.OnDied). The world
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
    [SerializeField] HunterSpawner m_HunterSpawner;
    [SerializeField] PlayerHealth m_PlayerHealth;

    [Header("Victory")]
    [SerializeField] float m_SurvivalDuration = 270f;
    [SerializeField] float m_VictoryLitDuration = 10f;

    [Header("Defeat")]
    [SerializeField] float m_DefeatFadeDuration = 1.5f;
    [SerializeField] float m_DefeatRestartDelay = 5f;

    [Header("Ending twist (see DISEÑO.md 1.3)")]
    [SerializeField] Renderer m_ChestRenderer;
    [SerializeField] Material m_TeethMaterial;

    float m_Elapsed;
    bool m_GameOver;

    /// <summary>Survival progress toward victory, 0-1. Read by NightEnvironmentController,
    /// which brings the dawn in across the whole sky.</summary>
    public float SurvivalNormalized => m_SurvivalDuration <= 0f ? 0f : Mathf.Clamp01(m_Elapsed / m_SurvivalDuration);

    void OnEnable()
    {
        if (m_Campfire != null)
            m_Campfire.OnExtinguished.AddListener(HandleExtinguished);
        if (m_PlayerHealth != null)
            m_PlayerHealth.OnDied.AddListener(HandleExtinguished);
    }

    void OnDisable()
    {
        if (m_Campfire != null)
            m_Campfire.OnExtinguished.RemoveListener(HandleExtinguished);
        if (m_PlayerHealth != null)
            m_PlayerHealth.OnDied.RemoveListener(HandleExtinguished);
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
        if (m_HunterSpawner != null)
            m_HunterSpawner.enabled = false;

        foreach (var skeleton in Object.FindObjectsByType<Skeleton>(FindObjectsInactive.Exclude))
            skeleton.Collapse();
        foreach (var boneThrower in Object.FindObjectsByType<BoneThrower>(FindObjectsInactive.Exclude))
            boneThrower.Collapse();

        if (m_NightEnvironment != null)
            m_NightEnvironment.ForcedNormalized = 1f;

        // The twist (DISEÑO.md 1.3): what read as gold coins was teeth all along -
        // an instant swap, revealed by the same light that makes the bone log pile
        // finally readable.
        if (m_ChestRenderer != null && m_TeethMaterial != null)
            m_ChestRenderer.sharedMaterial = m_TeethMaterial;

        PlayEndSound(ProceduralSfx.BirdSong);
        StartCoroutine(RestartAfter(m_VictoryLitDuration));
    }

    void Lose()
    {
        m_GameOver = true;

        if (m_SkeletonSpawner != null)
            m_SkeletonSpawner.enabled = false;
        if (m_BoneThrowerSpawner != null)
            m_BoneThrowerSpawner.enabled = false;
        if (m_HunterSpawner != null)
            m_HunterSpawner.enabled = false;

        PlayEndSound(ProceduralSfx.SinisterLaugh);
        StartCoroutine(FadeToBlackThenRestart());
    }

    void PlayEndSound(AudioClip clip)
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.playOnAwake = false;
        source.PlayOneShot(clip);
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

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
/// Defeat: the player's life runs out (PlayerHealth.OnDied). The world
/// fades to black over <see cref="m_DefeatFadeDuration"/> seconds while the
/// sinister laugh plays, then restarts at the <see cref="m_DefeatRestartDelay"/> mark.
///
/// Fire outage (not a loss): when the campfire goes out
/// (CampfireFuel.OnExtinguished) the survival clock pauses, the laugh plays,
/// the Caminantes and Lanzahuesos retreat off into the dark, and a faster,
/// tougher swarm of Cazadores replaces them. Relighting the fire
/// (CampfireFuel.OnIgnited) kills the swarm with the lego-breaking sound and
/// resumes the night where it left off.
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
    [Tooltip("Sinister laugh: plays when the campfire dies and again when the player dies. " +
        "Falls back to the procedural laugh when empty.")]
    [SerializeField] AudioClip m_LaughSfx;

    [Header("Opening")]
    [Tooltip("Wolf howl played once when the night's scene loads.")]
    [SerializeField] AudioClip m_IntroSfx;

    [Header("Ending twist (see DISEÑO.md 1.3)")]
    [SerializeField] Renderer m_ChestRenderer;
    [SerializeField] Material m_TeethMaterial;

    float m_Elapsed;
    bool m_GameOver;
    bool m_FireOut;

    /// <summary>Survival progress toward victory, 0-1. Read by NightEnvironmentController,
    /// which brings the dawn in across the whole sky.</summary>
    public float SurvivalNormalized => m_SurvivalDuration <= 0f ? 0f : Mathf.Clamp01(m_Elapsed / m_SurvivalDuration);

    void OnEnable()
    {
        if (m_Campfire != null)
        {
            m_Campfire.OnExtinguished.AddListener(HandleExtinguished);
            m_Campfire.OnIgnited.AddListener(HandleRelit);
        }
        if (m_PlayerHealth != null)
            m_PlayerHealth.OnDied.AddListener(HandlePlayerDied);
    }

    void OnDisable()
    {
        if (m_Campfire != null)
        {
            m_Campfire.OnExtinguished.RemoveListener(HandleExtinguished);
            m_Campfire.OnIgnited.RemoveListener(HandleRelit);
        }
        if (m_PlayerHealth != null)
            m_PlayerHealth.OnDied.RemoveListener(HandlePlayerDied);
    }

    void Start()
    {
        // The wolf howl sets the mood the instant the night begins.
        if (m_IntroSfx != null)
            Play2DSound(m_IntroSfx);
    }

    void Update()
    {
        // The clock pauses while the fire is out (see HandleExtinguished): the
        // player cannot outlast the darkness, they have to relight the fire.
        if (m_GameOver || m_FireOut)
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

    /// <summary>
    /// The campfire went out. Not a loss: the night stops advancing (the
    /// survival clock pauses) and the player must relight the fire with logs
    /// while the normal enemies retreat and a faster, tougher swarm closes in.
    /// </summary>
    void HandleExtinguished()
    {
        if (m_GameOver || m_FireOut)
            return;

        m_FireOut = true;
        Play2DSound(m_LaughSfx != null ? m_LaughSfx : ProceduralSfx.SinisterLaugh);

        // The Caminantes and Lanzahuesos walk off and vanish; only the red
        // swarm is left hunting the player.
        if (m_SkeletonSpawner != null)
        {
            m_SkeletonSpawner.enabled = false;
            m_SkeletonSpawner.RetreatAll();
        }

        if (m_BoneThrowerSpawner != null)
        {
            m_BoneThrowerSpawner.enabled = false;
            m_BoneThrowerSpawner.RetreatAll();
        }

        if (m_HunterSpawner != null)
            m_HunterSpawner.EnterSwarmMode();
    }

    /// <summary>
    /// The campfire was relit: the swarm dies with the lego-breaking sound and
    /// the night resumes exactly where it left off.
    /// </summary>
    void HandleRelit()
    {
        if (m_GameOver || !m_FireOut)
            return;

        m_FireOut = false;

        if (m_HunterSpawner != null)
        {
            m_HunterSpawner.KillAllHunters();
            m_HunterSpawner.ExitSwarmMode();
        }

        if (m_SkeletonSpawner != null)
            m_SkeletonSpawner.enabled = true;
        if (m_BoneThrowerSpawner != null)
            m_BoneThrowerSpawner.enabled = true;
    }

    void HandlePlayerDied()
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

        Play2DSound(ProceduralSfx.BirdSong);
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

        Play2DSound(m_LaughSfx != null ? m_LaughSfx : ProceduralSfx.SinisterLaugh);
        StartCoroutine(FadeToBlackThenRestart());
    }

    void Play2DSound(AudioClip clip)
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

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the ways the night ends. Almost cero UI (see CLAUDE.md): no Game Over
/// screen and no buttons - the world goes bright with a calm birdsong on
/// victory, or black with a sinister laugh on defeat, then the scene reloads.
/// The one exception is the "¡Has ganado!" / "¡Has perdido!" card
/// (see <see cref="EndGameBanner"/>) so the outcome is unmistakable.
///
/// Victory: survive <see cref="m_SurvivalDuration"/> seconds. Every Caminante
/// freezes and collapses, the world lights up for
/// <see cref="m_VictoryLitDuration"/> seconds, then restarts.
///
/// Defeat: the player's life runs out (PlayerHealth.OnDied). The world
/// fades to black over <see cref="m_DefeatFadeDuration"/> seconds while the
/// sinister laugh plays, then restarts at the <see cref="m_DefeatRestartDelay"/> mark.
///
/// Prologue: with <see cref="m_WaitForStart"/> on the night does not run yet -
/// the survival clock, the enemy spawners and the fire's fuel drain are all held
/// until <see cref="BeginNight"/>, which the player calls by shooting the start
/// target behind the campfire (see <see cref="GameStartTarget"/>).
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
    [Tooltip("Holds the night until the player shoots the start target behind the campfire " +
        "(GameStartTarget calls BeginNight). Off restores the old behaviour: the night runs " +
        "from scene load.")]
    [SerializeField] bool m_WaitForStart = true;
    [Tooltip("Wolf howl played once when the night begins.")]
    [SerializeField] AudioClip m_IntroSfx;
    [Tooltip("Night sting played once when the night begins, right after the howl. " +
             "A one-shot, so it only ever plays when the prologue gate is shot.")]
    [SerializeField] AudioClip m_NightSfx;

    [Header("Ending twist (see DISEÑO.md 1.3)")]
    [SerializeField] Renderer m_ChestRenderer;
    [SerializeField] Material m_TeethMaterial;

    float m_Elapsed;
    bool m_GameOver;
    bool m_FireOut;
    bool m_Started;

    /// <summary>Survival progress toward victory, 0-1. Read by NightEnvironmentController,
    /// which brings the dawn in across the whole sky.</summary>
    public float SurvivalNormalized => m_SurvivalDuration <= 0f ? 0f : Mathf.Clamp01(m_Elapsed / m_SurvivalDuration);

    /// <summary>True once the night is running (see <see cref="BeginNight"/>).</summary>
    public bool HasStarted => m_Started;

    void Awake()
    {
        // Hold the night. Disabling the spawners in Awake rather than Start keeps
        // their own Start - and the first spawn timer - from running before the
        // player's first arrow lands.
        if (!m_WaitForStart)
            return;

        SetSpawnersRunning(false);
        SetCampfireBurning(false);
    }

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
        // With the prologue gate off there is nothing to wait for.
        if (!m_WaitForStart)
            BeginNight();
    }

    /// <summary>
    /// Starts the night: the wolf howl, the survival clock, the enemy spawners
    /// and the fire's fuel drain. The start target calls this when the player's
    /// first arrow lands (see <see cref="GameStartTarget"/>); with
    /// <see cref="m_WaitForStart"/> off it is called from Start instead.
    /// Idempotent.
    /// </summary>
    public void BeginNight()
    {
        if (m_Started)
            return;
        m_Started = true;

        var campAnchor = m_Campfire != null ? m_Campfire.transform : transform;

        if (m_IntroSfx != null)
        {
            // Positional, not 2D: every other cue in the camp is spatialised, and a
            // howl that comes from the treeline reads better than one inside the
            // player's head.
            ProceduralSfx.PlayAt(m_IntroSfx, campAnchor.position + Vector3.up * 1.5f, 1f, 1f);
        }
        else
        {
            Debug.LogWarning("[GameManager] No intro sfx assigned; the night starts silently.", this);
        }

        // The night sting: only ever heard on the shot that starts the night.
        if (m_NightSfx != null)
            ProceduralSfx.PlayAt(m_NightSfx, campAnchor.position + Vector3.up * 1.5f, 1f, 1f);

        SetCampfireBurning(true);
        SetSpawnersRunning(true);
    }

    void Update()
    {
        // The clock pauses while the fire is out (see HandleExtinguished): the
        // player cannot outlast the darkness, they have to relight the fire. It
        // also does not run at all until the night has been started.
        if (m_GameOver || m_FireOut || !m_Started)
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

        SetSpawnersRunning(true);
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
        EndGameBanner.Show(true, m_PlayerHealth != null ? m_PlayerHealth.Head : null);
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
        EndGameBanner.Show(false, m_PlayerHealth != null ? m_PlayerHealth.Head : null);
        StartCoroutine(FadeToBlackThenRestart());
    }

    /// <summary>Enables or disables the three enemy spawners as one switch: the
    /// prologue gate holds them all off, and the night runs them all at once.</summary>
    void SetSpawnersRunning(bool running)
    {
        if (m_SkeletonSpawner != null)
            m_SkeletonSpawner.enabled = running;
        if (m_BoneThrowerSpawner != null)
            m_BoneThrowerSpawner.enabled = running;
        if (m_HunterSpawner != null)
            m_HunterSpawner.enabled = running;
    }

    /// <summary>Holds or resumes the campfire's fuel drain (see CampfireFuel.SetBurnsOverTime).</summary>
    void SetCampfireBurning(bool burning)
    {
        if (m_Campfire != null)
            m_Campfire.SetBurnsOverTime(burning);
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

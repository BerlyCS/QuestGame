using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the ways the night ends. There is no UI at all (see CLAUDE.md): no Game
/// Over screen, no buttons and no text - the world goes bright with a calm
/// birdsong on victory, or the screen floods red with a sinister laugh on
/// defeat (see <see cref="PlayerHealth"/>), then the scene reloads.
///
/// Victory: survive <see cref="m_SurvivalDuration"/> seconds. Every Caminante
/// freezes and collapses, the world lights up for
/// <see cref="m_VictoryLitDuration"/> seconds, then restarts.
///
/// Defeat: the player's life runs out (PlayerHealth.OnDied). The screen turns
/// completely red over <see cref="m_DefeatFadeDuration"/> seconds while the
/// sinister laugh plays, then restarts at the <see cref="m_DefeatRestartDelay"/> mark.
///
/// Prologue: with <see cref="m_WaitForStart"/> on the night does not run yet -
/// the survival clock, the enemy spawners and the fire's fuel drain are all held
/// until <see cref="BeginNight"/>, which the player calls by shooting the start
/// target behind the campfire (see <see cref="GameStartTarget"/>).
///
/// Fire outage (not a loss): when the campfire goes out
/// (CampfireFuel.OnExtinguished) the survival clock pauses, the fire-out
/// sting plays,
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
    [Tooltip("Sinister laugh: plays when the player dies. " +
        "Falls back to the procedural laugh when empty.")]
    [SerializeField] AudioClip m_LaughSfx;
    [Tooltip("Plays once when the campfire dies (the fire outage, not a loss). " +
        "Falls back to the laugh when empty.")]
    [SerializeField] AudioClip m_FireOutSfx;

    [Header("Derrota por el Coronado (ver JEFE_FINAL.md 7.1)")]
    [Tooltip("Segundos de negro y silencio antes de reiniciar. El jefe ya dejó la pantalla en " +
        "negro cuando llama a LoseToCoronado(); esto es solo la espera.")]
    [SerializeField] float m_JumpscareRestartDelay = 3f;

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
    bool m_BossPhaseActive;

    /// <summary>Survival progress toward victory, 0-1. Read by NightEnvironmentController,
    /// which brings the dawn in across the whole sky.</summary>
    public float SurvivalNormalized => m_SurvivalDuration <= 0f ? 0f : Mathf.Clamp01(m_Elapsed / m_SurvivalDuration);

    /// <summary>True once the night is running (see <see cref="BeginNight"/>).</summary>
    public bool HasStarted => m_Started;

    /// <summary>True from the Coronado's entrance onward (see <see cref="SetBossPhaseActive"/>).</summary>
    public bool BossPhaseActive => m_BossPhaseActive;

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
    /// Called once by BossIntro when the Coronado's entrance begins (see
    /// JEFE_FINAL.md 3). Stops the three spawners - the "WaveManager" - for
    /// the rest of the night and, while active, keeps <see cref="HandleRelit"/>
    /// from waking them back up if the player relights the fire mid-fight.
    /// </summary>
    public void SetBossPhaseActive(bool active)
    {
        m_BossPhaseActive = active;
        if (active)
            SetSpawnersRunning(false);
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
        Play2DSound(m_FireOutSfx != null ? m_FireOutSfx : (m_LaughSfx != null ? m_LaughSfx : ProceduralSfx.SinisterLaugh));

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

        // The Coronado's entrance stops the spawners for good (see
        // SetBossPhaseActive); relighting the fire during the boss phase must
        // not wake them back up.
        if (!m_BossPhaseActive)
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
        StartCoroutine(RestartAfter(m_VictoryLitDuration));
    }

    /// <summary>
    /// The Coronado catching the player (JEFE_FINAL.md 7.1): unlike the normal
    /// <see cref="Lose"/>, this is a silent jumpscare straight to black - no
    /// laugh, no "Has perdido" card ("sin texto"). Coronado plays out the
    /// freeze/lunge/scream sequence and leaves the screen black itself, then
    /// calls this to stop the spawners and schedule the reload after three
    /// seconds of nothing.
    /// </summary>
    public void LoseToCoronado()
    {
        if (m_GameOver)
            return;
        m_GameOver = true;

        SetSpawnersRunning(false);
        StartCoroutine(RestartAfter(m_JumpscareRestartDelay));
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
        // AudioListener.volume is a static, global switch (see Coronado's
        // catch sequence, which mutes it for the freeze and the final black
        // silence) and does not reset itself across a scene reload - without
        // this the whole restarted night would stay silent.
        AudioListener.volume = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}

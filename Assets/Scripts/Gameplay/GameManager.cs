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
/// (CampfireFuel.OnExtinguished) the night keeps running - the survival clock
/// is not paused, so letting the dark drag on is never a way to survive it -
/// the fire-out sting plays, the Caminantes and Lanzahuesos retreat off into
/// the dark, and red-eyed Cazadores swarm the player and keep coming until the
/// fire is relit (CampfireFuel.OnIgnited), which kills the swarm with the
/// lego-breaking sound.
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
    [SerializeField] float m_SurvivalDuration = 240f;
    [SerializeField] float m_VictoryLitDuration = 10f;

    [Header("Night difficulty (steps every 30 s by default)")]
    [Tooltip("Seconds per difficulty step. Every step the enemies walk faster, hit the fire and the player harder, " +
        "and the spawners keep one more enemy of each kind alive at once.")]
    [SerializeField] float m_DifficultyStepSeconds = 30f;
    [Tooltip("Enemy walk-speed multiplier added per step (1 = no ramp).")]
    [SerializeField] float m_SpeedRampPerStep = 0.15f;
    [Tooltip("Multiplier added per step to how much enemy blows drain the campfire.")]
    [SerializeField] float m_FireDamageRampPerStep = 0.3f;
    [Tooltip("Multiplier added per step to how much a Cazador takes off the player.")]
    [SerializeField] float m_PlayerDamageRampPerStep = 0.1f;
    [Tooltip("Passive burn ramp across the whole night: 2 means the fire burns 3x as fast at victory as at the start.")]
    [SerializeField] float m_FireBurnRamp = 2f;

    [Header("Opening (first 30 s)")]
    [Tooltip("Place one slow Caminante, one Lanzahuesos and one Cazador as the night begins.")]
    [SerializeField] bool m_SpawnOpeningWave = true;

    [Header("First fire-out lesson (one time only)")]
    [Tooltip("How long the campfire blinks and the gentle red-eyed horde lingers after the first fire-out. " +
        "The horde keeps replenishing itself until then; after this it escalates to the normal swarm.")]
    [SerializeField] float m_FirstOutageCueDuration = 30f;
    [Tooltip("How many very slow red-eyed Cazadores the first fire-out horde keeps alive around the player at once.")]
    [SerializeField] int m_FirstOutageHordeCount = 8;
    [SerializeField] float m_FirstOutageHordeSpeed = 0.3f;
    [SerializeField] float m_FirstOutageHordeDamage = 5f;
    [Tooltip("Seconds between blinks of the campfire cue while it is out.")]
    [SerializeField] float m_FirstOutageBlinkInterval = 0.3f;
    [SerializeField] Color m_FirstOutageBlinkColor = new Color(1.4f, 0.5f, 0.12f);

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

    [Header("Ambience")]
    [Tooltip("Volume of the looping forest bed that plays under the night. " +
        "The clip is loaded from Resources/Ambience/dark_ambience_forest so the " +
        "scene needs no AudioSource wired by hand.")]
    [SerializeField, Range(0f, 1f)] float m_AmbienceVolume = 0.35f;

    [Header("Ending twist (see DISEÑO.md 1.3)")]
    [SerializeField] Renderer m_ChestRenderer;
    [SerializeField] Material m_TeethMaterial;

    float m_Elapsed;
    bool m_GameOver;
    bool m_FireOut;
    bool m_Started;
    bool m_BossPhaseActive;
    bool m_FirstOutagePlayed;
    AudioSource m_AmbienceSource;
    float m_AmbienceDuck = 1f;

    /// <summary>Survival progress toward victory, 0-1. Read by NightEnvironmentController,
    /// which brings the dawn in across the whole sky.</summary>
    public float SurvivalNormalized => m_SurvivalDuration <= 0f ? 0f : Mathf.Clamp01(m_Elapsed / m_SurvivalDuration);

    /// <summary>True once the night is running (see <see cref="BeginNight"/>).</summary>
    public bool HasStarted => m_Started;

    /// <summary>True from the Coronado's entrance onward (see <see cref="SetBossPhaseActive"/>).</summary>
    public bool BossPhaseActive => m_BossPhaseActive;

    void Awake()
    {
        // A scene reload can keep statics alive when domain reload is disabled;
        // never start the night already ramped up.
        NightDifficulty.Reset();

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

        StartAmbience();

        SetCampfireBurning(true);
        SetSpawnersRunning(true);

        // Step 0 of the difficulty ramp and the gentle opening trio: one slow
        // enemy of each kind, nothing else for the first 30 s (the spawners'
        // start delays keep them from adding to it).
        NightDifficulty.Reset();
        UpdateDifficulty();
        if (m_SpawnOpeningWave)
        {
            if (m_SkeletonSpawner != null)
                m_SkeletonSpawner.SpawnNow();
            if (m_BoneThrowerSpawner != null)
                m_BoneThrowerSpawner.SpawnNow();
            if (m_HunterSpawner != null)
                m_HunterSpawner.SpawnNow();
        }
    }

    void Update()
    {
        // The difficulty is a function of the survival clock, so it is refreshed
        // every frame.
        UpdateDifficulty();

        // The clock keeps running while the fire is out (see HandleExtinguished):
        // an outage costs the player firelight and brings the swarm, but never
        // freezes time, so sitting in the dark cannot outlast the night. It does
        // not run at all until the night has been started.
        if (m_GameOver || !m_Started)
            return;
        m_Elapsed += Time.deltaTime;
        if (m_Elapsed >= m_SurvivalDuration)
            Win();
    }

    /// <summary>
    /// Feeds the night's escalating pressure to every enemy (through
    /// <see cref="NightDifficulty"/>) and to the campfire's passive burn. The
    /// enemy ramp steps once every <see cref="m_DifficultyStepSeconds"/> seconds
    /// of survived night, so the opening is gentle and the last stretch bites.
    /// </summary>
    void UpdateDifficulty()
    {
        int step = m_DifficultyStepSeconds <= 0f ? 0 : Mathf.FloorToInt(m_Elapsed / m_DifficultyStepSeconds);
        NightDifficulty.Stage = step;
        NightDifficulty.SpeedMultiplier = 1f + step * m_SpeedRampPerStep;
        NightDifficulty.FireDamageMultiplier = 1f + step * m_FireDamageRampPerStep;
        NightDifficulty.PlayerDamageMultiplier = 1f + step * m_PlayerDamageRampPerStep;

        if (m_Campfire != null)
            m_Campfire.SetBurnRateMultiplier(1f + m_FireBurnRamp * SurvivalNormalized);
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
    /// The campfire went out. Not a loss: the night keeps advancing (the
    /// survival clock never pauses) and the player must relight the fire with
    /// logs while the normal enemies retreat and a red-eyed swarm closes in and
    /// keeps replenishing until the fire is back.
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

        // The first fire-out is a one-time lesson, not the normal crisis: the
        // campfire blinks and a gentle red-eyed horde hangs back so the player
        // learns to relight. Every later outage goes straight to the swarm.
        if (!m_FirstOutagePlayed)
        {
            m_FirstOutagePlayed = true;
            StartCoroutine(FirstOutageRoutine());
        }
        else if (m_HunterSpawner != null)
        {
            m_HunterSpawner.EnterSwarmMode();
        }
    }

    /// <summary>
    /// The first time the fire dies: fast-blinks the campfire and rings the
    /// player with very slow, red-eyed Cazadores for
    /// <see cref="m_FirstOutageCueDuration"/> seconds. They barely hurt, so the
    /// lesson is "feed the fire", not "fight" - but the ring is kept full (see
    /// HunterSpawner.SpawnSpecialHorde), so it never lets the player stand in
    /// the dark undisturbed. If the player still has not relit by the end, the
    /// normal swarm takes over.
    /// </summary>
    IEnumerator FirstOutageRoutine()
    {
        if (m_HunterSpawner != null)
            m_HunterSpawner.SpawnSpecialHorde(m_FirstOutageHordeCount, m_FirstOutageHordeSpeed, m_FirstOutageHordeDamage);

        Vector3 anchor = (m_Campfire != null ? m_Campfire.transform.position : transform.position) + Vector3.up * 0.4f;

        float end = Time.time + m_FirstOutageCueDuration;
        while (m_FireOut && !m_GameOver && Time.time < end)
        {
            FadingGlow.Spawn(anchor, 0.9f, m_FirstOutageBlinkColor, m_FirstOutageBlinkInterval * 0.9f);
            yield return new WaitForSeconds(m_FirstOutageBlinkInterval);
        }

        if (m_FireOut && !m_GameOver && m_HunterSpawner != null)
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

    /// <summary>
    /// Starts the looping forest bed under the night. Loaded from Resources so
    /// no AudioSource has to be wired in the scene; does nothing (with a warning)
    /// if the clip is missing.
    /// </summary>
    void StartAmbience()
    {
        if (m_AmbienceSource != null)
            return;

        AudioClip clip = Resources.Load<AudioClip>("Ambience/dark_ambience_forest");
        if (clip == null)
        {
            Debug.LogWarning(
                "[GameManager] Forest ambience clip not found under Resources/Ambience; the night stays quiet.",
                this);
            return;
        }

        m_AmbienceSource = gameObject.AddComponent<AudioSource>();
        m_AmbienceSource.clip = clip;
        m_AmbienceSource.loop = true;
        m_AmbienceSource.playOnAwake = false;
        m_AmbienceSource.spatialBlend = 0f;
        m_AmbienceSource.volume = m_AmbienceVolume * m_AmbienceDuck;
        m_AmbienceSource.Play();
    }

    /// <summary>
    /// Scales the ambience bed for systems that want the world quieter (see
    /// <see cref="BossIntro"/>'s "total silence"). The multiplier is remembered
    /// so the designer volume can be restored later.
    /// </summary>
    public void SetAmbienceDuck(float duck)
    {
        m_AmbienceDuck = Mathf.Clamp01(duck);
        if (m_AmbienceSource != null)
            m_AmbienceSource.volume = m_AmbienceVolume * m_AmbienceDuck;
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

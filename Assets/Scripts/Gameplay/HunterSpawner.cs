using UnityEngine;

/// <summary>
/// Spawns the Cazadores: skeletons that walk to the player and attack them
/// (see Skeleton's hunter mode). They start after the first stretch of the
/// night, come mostly from behind the player's gaze, and never more than the
/// stage's cap at once (<see cref="m_MaxAlive"/> plus one per
/// <see cref="NightDifficulty.Stage"/>, up to <see cref="m_MaxAliveCap"/>); a
/// killed Cazador is replaced after <see cref="m_RefillDelay"/>. Spawns on a
/// fixed ring around the player (radius <see cref="m_SpawnDistance"/>),
/// distinct from the Caminante ring that is centred on the campfire.
///
/// During a fire outage every kill also ramps the replacements up
/// (<see cref="m_OutageEscalationPerKill"/>): the next Cazador is faster, hits
/// harder and soaks more hits, so the only way to stop the escalation is to
/// relight the campfire (see GameManager's outage).
/// </summary>
[DisallowMultipleComponent]
public class HunterSpawner : MonoBehaviour
{
    [SerializeField] GameObject m_HunterPrefab;
    [SerializeField] PlayerHealth m_Player;
    [SerializeField] GameManager m_GameManager;

    [Header("Timing")]
    [Tooltip("Fraction of the night (0-1) that must pass before the first Cazador appears.")]
    [SerializeField] float m_StartAtSurvival = 0.13f;
    [SerializeField] float m_SpawnInterval = 25f;
    [SerializeField] float m_MinInterval = 15f;
    [SerializeField] float m_IntervalRampPerSpawn = 0.8f;

    [Header("Placement")]
    [SerializeField] float m_SpawnDistance = 9f;
    [SerializeField] float m_SpreadDegrees = 50f;
    [Range(0f, 1f)]
    [SerializeField] float m_BehindChance = 0.7f;

    [Header("Limits")]
    [Tooltip("How many Cazadores can be alive at once at the start of the night.")]
    [SerializeField] int m_MaxAlive = 1;
    [Tooltip("Extra Cazadores allowed alive per difficulty stage (NightDifficulty.Stage).")]
    [SerializeField] int m_MaxAlivePerStage = 1;
    [Tooltip("Hard ceiling on living Cazadores, however far the night has run.")]
    [SerializeField] int m_MaxAliveCap = 4;
    [Tooltip("Delay before a killed Cazador is replaced while below the per-stage cap.")]
    [SerializeField] float m_RefillDelay = 4f;

    [Header("Fire outage (first-outage horde)")]
    [Tooltip("Seconds between replacements while the gentle first-outage horde is active, " +
        "so the ring never empties until the fire is relit.")]
    [SerializeField] float m_SpecialHordeRefillDelay = 4f;

    [Header("Fire outage escalation")]
    [Tooltip("Extra stat multiplier added to every outage hunter each time one is killed " +
        "(walk speed, claws and hit points), so replacements get deadlier the longer the " +
        "fire stays dead. 0.05 = +5% per kill; resets when the fire is relit.")]
    [SerializeField] float m_OutageEscalationPerKill = 0.05f;

    [Header("Fire outage (swarm)")]
    [Tooltip("Spawn interval while the campfire is out.")]
    [SerializeField] float m_SwarmSpawnInterval = 2.5f;
    [Tooltip("How many swarm hunters can be alive at once.")]
    [SerializeField] int m_SwarmMaxAlive = 6;
    [Tooltip("Swarm hunters walk faster than the normal Cazador.")]
    [SerializeField] float m_SwarmMoveSpeed = 1.2f;
    [Tooltip("Swarm hunters take more hits before they go down (also the number of axe strikes they survive).")]
    [SerializeField] int m_SwarmHits = 1;
    [Tooltip("Swarm hunters spawn closer to the player.")]
    [SerializeField] float m_SwarmSpawnDistance = 10f;

    [Header("Appearance")]
    [Tooltip("Stinger played from the spot a red Cazador appears in, so the " +
        "player hears where it came from. It never retriggers while the " +
        "previous one is still audible.")]
    [SerializeField] AudioClip m_AppearSfx;
    [Range(0f, 1f)]
    [SerializeField] float m_AppearSfxVolume = 0.9f;

    int m_Spawned;
    int m_Alive;
    float m_NextSpawnTime;
    float m_NextAppearSfxTime;
    bool m_Swarm;
    bool m_SpecialHorde;
    int m_SpecialHordeMaxAlive;
    float m_SpecialHordeMoveSpeed;
    float m_SpecialHordePlayerDamage;
    int m_OutageKills;

    /// <summary>Multiplier on every stat of an outage hunter, grown by one step per kill
    /// and reset when the fire is relit (see <see cref="m_OutageEscalationPerKill"/>).</summary>
    float OutageEscalation => 1f + m_OutageKills * m_OutageEscalationPerKill;

    void Update()
    {
        if (m_Player == null || m_Player.Head == null || m_HunterPrefab == null || m_GameManager == null || !m_Player.IsAlive)
            return;

        float survival = m_GameManager.SurvivalNormalized;
        if (!m_Swarm && !m_SpecialHorde && survival < m_StartAtSurvival)
            return;

        int cap = m_Swarm ? m_SwarmMaxAlive : (m_SpecialHorde ? m_SpecialHordeMaxAlive : MaxAliveForStage);
        if (m_Alive >= cap || Time.time < m_NextSpawnTime)
            return;

        if (m_SpecialHorde)
            SpawnSpecial(m_SpecialHordeMoveSpeed, m_SpecialHordePlayerDamage);
        else
            Spawn();

        float interval = m_Swarm
            ? m_SwarmSpawnInterval
            : (m_SpecialHorde
                ? m_SpecialHordeRefillDelay
                : Mathf.Max(m_MinInterval, m_SpawnInterval - m_Spawned * m_IntervalRampPerSpawn));
        m_NextSpawnTime = Time.time + interval;
    }

    /// <summary>How many Cazadores may be alive right now: the base cap plus one
    /// per difficulty stage, never past <see cref="m_MaxAliveCap"/>.</summary>
    int MaxAliveForStage => Mathf.Clamp(m_MaxAlive + NightDifficulty.Stage * m_MaxAlivePerStage, 0, m_MaxAliveCap);

    /// <summary>
    /// Switches to the fire-outage swarm: faster, tougher and more numerous
    /// hunters that keep coming until the campfire is relit. Leaves the gentle
    /// first-outage horde behind.
    /// </summary>
    public void EnterSwarmMode()
    {
        m_SpecialHorde = false;
        m_Swarm = true;
        m_NextSpawnTime = Time.time;
    }

    /// <summary>
    /// Ends every fire-outage mode. Called when the campfire is relit, so no
    /// hunter is replenished while the night is back to normal.
    /// </summary>
    public void ExitSwarmMode()
    {
        m_Swarm = false;
        m_SpecialHorde = false;

        // Each outage starts from the base stats again: the ramp is per fire-out,
        // not a permanent buff that would eventually outpace the player.
        m_OutageKills = 0;
    }

    /// <summary>
    /// Kills every Cazador in the world with the normal death effect (the
    /// lego-breaking sound). Called when the campfire is relit.
    /// </summary>
    public void KillAllHunters()
    {
        foreach (var skeleton in Object.FindObjectsByType<Skeleton>(FindObjectsInactive.Exclude))
        {
            if (skeleton != null && skeleton.IsHunter)
                skeleton.Kill();
        }

        m_Alive = 0;
    }

    void Spawn()
    {
        Vector3 head = m_Player.Head.position;
        Vector3 forward = Vector3.ProjectOnPlane(m_Player.Head.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;

        Vector3 direction = Random.value < m_BehindChance ? -forward : forward;
        direction = Quaternion.Euler(0f, Random.Range(-m_SpreadDegrees, m_SpreadDegrees), 0f) * direction;

        float distance = m_Swarm ? m_SwarmSpawnDistance : m_SpawnDistance;
        Vector3 position = new Vector3(head.x, 0f, head.z) + direction * distance;
        var hunter = Instantiate(m_HunterPrefab, position, Quaternion.LookRotation(-direction, Vector3.up));

        var skeleton = hunter.GetComponent<Skeleton>();
        skeleton.SetPlayer(m_Player);
        if (m_Swarm)
        {
            skeleton.SetMoveSpeed(m_SwarmMoveSpeed);
            skeleton.SetMaxHits(m_SwarmHits);
            skeleton.SetResistsBanish(true);
            skeleton.ApplyOutageEscalation(OutageEscalation);
        }
        skeleton.OnDied.AddListener(HandleHunterDied);

        m_Spawned++;
        m_Alive++;
    }

    void HandleHunterDied()
    {
        m_Alive = Mathf.Max(0, m_Alive - 1);

        // Every outage kill bumps the ramp, so the replacement that takes this
        // one's place is faster, hits harder and soaks more hits. Normal night
        // kills (no outage running) leave the ramp alone.
        if (m_Swarm || m_SpecialHorde)
            m_OutageKills++;

        // A kill opens a slot: line up the stage's replacement soon so the
        // hunter pressure never dries up while the player survives.
        m_NextSpawnTime = Mathf.Min(m_NextSpawnTime, Time.time + m_RefillDelay);
    }

    /// <summary>
    /// Announces a Cazador from where it appeared. The sound is held back while
    /// the previous one is still playing so a swarm does not stack a dozen
    /// copies of the same stinger on top of each other.
    /// </summary>
    void PlayAppearSfx(Vector3 position)
    {
        if (m_AppearSfx == null || Time.time < m_NextAppearSfxTime)
            return;

        m_NextAppearSfxTime = Time.time + m_AppearSfx.length;

        var go = new GameObject("Cazador Appear SFX");
        go.transform.position = position;

        var audio = go.AddComponent<AudioSource>();
        audio.clip = m_AppearSfx;
        audio.volume = Mathf.Clamp01(m_AppearSfxVolume);
        audio.spatialBlend = 1f;
        audio.rolloffMode = AudioRolloffMode.Linear;
        audio.minDistance = 2f;
        audio.maxDistance = 40f;
        audio.dopplerLevel = 0f;
        audio.playOnAwake = false;
        audio.Play();

        go.AddComponent<AutoDestroyAfter>().Lifetime = m_AppearSfx.length + 0.3f;
    }

    /// <summary>
    /// Force-spawns one Cazador now, gates bypassed. Used for the opening beat
    /// and by DebugKeys.
    /// </summary>
    public void SpawnNow()
    {
        if (m_Player != null && m_Player.Head != null && m_HunterPrefab != null)
            Spawn();
    }

    /// <summary>
    /// The one-time first-fire-out lesson: a slow, red-eyed ring of Cazadores
    /// that hang back and claw gently, so the player reads "the fire is out,
    /// feed it" instead of fighting for their life. They stay out of the night
    /// ramp (see Skeleton.SetIgnoresDifficultyRamp) so they never speed up.
    /// The horde is remembered and kept full (see Update) until the fire is
    /// relit or it escalates into the swarm, so the player is never left alone
    /// in the dark to wait the outage out.
    /// </summary>
    public void SpawnSpecialHorde(int count, float moveSpeed, float playerDamage)
    {
        if (m_Player == null || m_Player.Head == null || m_HunterPrefab == null)
            return;

        m_SpecialHorde = true;
        m_SpecialHordeMaxAlive = Mathf.Max(m_Alive, count);
        m_SpecialHordeMoveSpeed = moveSpeed;
        m_SpecialHordePlayerDamage = playerDamage;
        m_NextSpawnTime = Time.time;

        for (int i = 0; i < count; i++)
            SpawnSpecial(moveSpeed, playerDamage);
    }

    void SpawnSpecial(float moveSpeed, float playerDamage)
    {
        Vector3 head = m_Player.Head.position;
        Vector3 forward = Vector3.ProjectOnPlane(m_Player.Head.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;

        Vector3 direction = Random.value < m_BehindChance ? -forward : forward;
        direction = Quaternion.Euler(0f, Random.Range(-m_SpreadDegrees, m_SpreadDegrees), 0f) * direction;

        Vector3 position = new Vector3(head.x, 0f, head.z) + direction * m_SwarmSpawnDistance;
        var hunter = Instantiate(m_HunterPrefab, position, Quaternion.LookRotation(-direction, Vector3.up));

        var skeleton = hunter.GetComponent<Skeleton>();
        skeleton.SetPlayer(m_Player);
        skeleton.SetMoveSpeed(moveSpeed);
        skeleton.SetPlayerDamage(playerDamage);
        skeleton.SetIgnoresDifficultyRamp(true);
        skeleton.EnableRedEyes();
        // The horde already shrugs off the night ramp, but it still takes the
        // outage escalation. Once the kill streak has ramped it up, the axe no
        // longer one-shots it either, so a long outage cannot be cleared with a
        // single swing per hunter.
        if (OutageEscalation > 1f)
            skeleton.SetResistsBanish(true);
        skeleton.ApplyOutageEscalation(OutageEscalation);
        skeleton.OnDied.AddListener(HandleHunterDied);

        m_Alive++;
    }
}

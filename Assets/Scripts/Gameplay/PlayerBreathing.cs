using UnityEngine;

/// <summary>
/// The player's own breathing: the more enemies they kill, the more tired they
/// get and the harder they breathe; rest brings it back down. A looping heavy
/// breath (Assets/Audio/heavybreath.mp3) plays 2D - it is the player's own
/// breath, inside their head, not something in the world - with its volume
/// following a fatigue level that each kill raises and time slowly drains.
/// Deliberately kept in the middle of the mix (see <see cref="m_MaxVolume"/>):
/// present enough to feel the effort, never loud enough to mask the enemies'
/// cues.
///
/// Kills are reported by the enemies themselves through
/// <see cref="ReportKill"/> (arrow or axe kills only: the swarm dying when the
/// fire is relit is not the player's effort). <see cref="Calm"/> fades it out,
/// used on victory (where the relief sigh takes over) and on defeat.
/// </summary>
[DisallowMultipleComponent]
public class PlayerBreathing : MonoBehaviour
{
    static PlayerBreathing s_Instance;

    [Tooltip("Looping heavy breathing (Assets/Audio/heavybreath.mp3). Wired by Tools/Game/Wire Game Audio.")]
    [SerializeField] AudioClip m_HeavyBreath;
    [Tooltip("Volume at full exhaustion. Middle of the mix: heard, never covering the enemies.")]
    [SerializeField, Range(0f, 1f)] float m_MaxVolume = 0.35f;
    [Tooltip("Fatigue (0-1) each kill adds. 0.25 = four quick kills to be fully out of breath.")]
    [SerializeField] float m_FatiguePerKill = 0.25f;
    [Tooltip("Seconds after the last kill before the breathing starts to settle.")]
    [SerializeField] float m_RecoveryDelay = 4f;
    [Tooltip("Fatigue drained per second once resting (0.06 = about 16 s from exhausted to calm).")]
    [SerializeField] float m_RecoveryPerSecond = 0.06f;
    [Tooltip("How fast the volume chases the fatigue level, per second - keeps the breathing from jumping.")]
    [SerializeField] float m_VolumeResponse = 1.5f;

    AudioSource m_Source;
    float m_Fatigue;
    float m_LastKillTime = float.NegativeInfinity;
    bool m_Calmed;

    /// <summary>Current tiredness, 0 (rested) to 1 (out of breath).</summary>
    public float Fatigue => m_Fatigue;

    /// <summary>An enemy just died by the player's hand. Safe to call with no player in the scene.</summary>
    public static void ReportKill()
    {
        if (s_Instance != null)
            s_Instance.AddKill();
    }

    void OnEnable() => s_Instance = this;

    void OnDisable()
    {
        if (s_Instance == this)
            s_Instance = null;
    }

    void Awake()
    {
        m_Source = gameObject.AddComponent<AudioSource>();
        m_Source.clip = m_HeavyBreath;
        m_Source.loop = true;
        m_Source.playOnAwake = false;
        m_Source.spatialBlend = 0f;
        m_Source.volume = 0f;
    }

    void AddKill()
    {
        if (m_Calmed)
            return;

        m_Fatigue = Mathf.Clamp01(m_Fatigue + m_FatiguePerKill);
        m_LastKillTime = Time.time;
    }

    /// <summary>The fight is over: stop gaining fatigue and let the breathing fade out.</summary>
    public void Calm()
    {
        m_Calmed = true;
        m_Fatigue = 0f;
    }

    void Update()
    {
        if (m_Source.clip == null)
            return;

        if (Time.time - m_LastKillTime > m_RecoveryDelay)
            m_Fatigue = Mathf.Max(0f, m_Fatigue - m_RecoveryPerSecond * Time.deltaTime);

        // Smoothstep so a single kill is a light catch of breath and only a
        // run of kills turns into real panting.
        float target = m_MaxVolume * Mathf.SmoothStep(0f, 1f, m_Fatigue);
        m_Source.volume = Mathf.MoveTowards(m_Source.volume, target, m_VolumeResponse * m_MaxVolume * Time.deltaTime);

        if (m_Source.volume > 0.001f)
        {
            if (!m_Source.isPlaying)
            {
                // Start somewhere in the loop so each bout does not open on the same breath.
                m_Source.time = Random.Range(0f, m_Source.clip.length * 0.9f);
                m_Source.Play();
            }
        }
        else if (m_Source.isPlaying)
        {
            m_Source.Stop();
        }
    }
}

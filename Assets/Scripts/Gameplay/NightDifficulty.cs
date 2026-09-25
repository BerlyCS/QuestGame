/// <summary>
/// The one place the night's escalating pressure lives. <see cref="GameManager"/>
/// recomputes these multipliers from the survival clock every frame (stepping up
/// once per <c>m_DifficultyStepSeconds</c>), and every enemy reads them while it
/// moves and attacks. Keeping them in one static holder means a skeleton spawned
/// halfway through a wave already carries the current tier without any per-enemy
/// wiring, and the whole curve can be read or tuned from a single spot. The
/// integer <see cref="Stage"/> is exposed for the spawners, so how many enemies
/// may be alive at once grows with the same steps.
///
/// Multipliers start at their step-0 values and the manager resets them in
/// <see cref="Reset"/> (called from GameManager.Awake) so a scene reload - where
/// statics can survive when domain reload is disabled - never starts already
/// ramped up. Note the damage multipliers start high and fall: speed only ever
/// rises, while fire and player damage soften as the night goes on (see
/// GameManager.UpdateDifficulty).
/// </summary>
public static class NightDifficulty
{
    /// <summary>Scales every enemy's walk speed (Caminante, Lanzahuesos, Cazador).</summary>
    public static float SpeedMultiplier = 1f;

    /// <summary>Scales how much each enemy attack drains from the campfire.</summary>
    public static float FireDamageMultiplier = 1f;

    /// <summary>Scales how much the Cazadores take off the player.</summary>
    public static float PlayerDamageMultiplier = 1f;

    /// <summary>
    /// How many difficulty steps the night has taken (0 at the start, +1 every
    /// <c>m_DifficultyStepSeconds</c>). The spawners use it to raise how many
    /// enemies they keep alive at once, so the pressure scales by stage rather
    /// than by a fixed budget the player can clear.
    /// </summary>
    public static int Stage;

    /// <summary>Returns the night to its easy opening state.</summary>
    public static void Reset()
    {
        SpeedMultiplier = 1f;
        FireDamageMultiplier = 1f;
        PlayerDamageMultiplier = 1f;
        Stage = 0;
    }
}

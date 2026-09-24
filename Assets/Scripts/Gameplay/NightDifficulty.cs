/// <summary>
/// The one place the night's escalating pressure lives. <see cref="GameManager"/>
/// recomputes these multipliers from the survival clock every frame (stepping up
/// once per <c>m_TierSeconds</c>), and every enemy reads them while it moves and
/// attacks. Keeping them in one static holder means a skeleton spawned halfway
/// through a wave already carries the current tier without any per-enemy wiring,
/// and the whole curve can be read or tuned from a single spot.
///
/// Multipliers are 1 at the start of the night. The manager resets them in
/// <see cref="Reset"/> (called from GameManager.Awake) so a scene reload - where
/// statics can survive when domain reload is disabled - never starts already
/// ramped up.
/// </summary>
public static class NightDifficulty
{
    /// <summary>Scales every enemy's walk speed (Caminante, Lanzahuesos, Cazador).</summary>
    public static float SpeedMultiplier = 1f;

    /// <summary>Scales how much each enemy attack drains from the campfire.</summary>
    public static float FireDamageMultiplier = 1f;

    /// <summary>Scales how much the Cazadores take off the player.</summary>
    public static float PlayerDamageMultiplier = 1f;

    /// <summary>Returns the night to its easy opening state.</summary>
    public static void Reset()
    {
        SpeedMultiplier = 1f;
        FireDamageMultiplier = 1f;
        PlayerDamageMultiplier = 1f;
    }
}

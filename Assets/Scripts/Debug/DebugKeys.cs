using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Debug-only keyboard shortcuts for testing in the Editor without a headset.
/// This is the one explicit exception to "no gameplay script reads input
/// directly" (see CLAUDE.md): it isn't gameplay, and it fully disables via
/// <see cref="m_ShortcutsEnabled"/> without removing the component. Reads the
/// new Input System directly since this project's Active Input Handling is
/// set to Input System only.
/// </summary>
[DisallowMultipleComponent]
public class DebugKeys : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Uncheck to disable every debug shortcut without removing this component.")]
    bool m_ShortcutsEnabled = true;

    [SerializeField]
    [Tooltip("'1' force-spawns one Caminante at the fixed 10 m ring.")]
    SkeletonSpawner m_SkeletonSpawner;

    [SerializeField]
    [Tooltip("'2' force-spawns one Lanzahuesos at the fixed 10 m ring.")]
    BoneThrowerSpawner m_BoneThrowerSpawner;

    [SerializeField]
    [Tooltip("'3' force-spawns one Cazador (attacks the player) 9 m away.")]
    HunterSpawner m_HunterSpawner;

    [SerializeField]
    [Tooltip("'N' jumps the survival clock to 170 s, 10 s before victory.")]
    GameManager m_GameManager;

    [SerializeField]
    [Tooltip("Second the 'N' key jumps the survival clock to.")]
    float m_SkipToSeconds = 170f;

    [SerializeField]
    [Tooltip("'B' invokes the Coronado 15 m in front of the player, to test the gaze-freeze rule.")]
    Coronado m_Coronado;

    [SerializeField]
    [Tooltip("Distance the 'B' key places the Coronado at.")]
    float m_CoronadoSummonDistance = 15f;

    [SerializeField]
    [Tooltip("'J' fires the Coronado's entrance immediately, bypassing the survival-clock gate.")]
    BossIntro m_BossIntro;

    void Update()
    {
        if (!m_ShortcutsEnabled)
            return;

        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.digit1Key.wasPressedThisFrame && m_SkeletonSpawner != null)
            m_SkeletonSpawner.SpawnNow();

        if (keyboard.digit2Key.wasPressedThisFrame && m_BoneThrowerSpawner != null)
            m_BoneThrowerSpawner.SpawnNow();

        if (keyboard.digit3Key.wasPressedThisFrame && m_HunterSpawner != null)
            m_HunterSpawner.SpawnNow();

        if (keyboard.nKey.wasPressedThisFrame && m_GameManager != null)
            m_GameManager.DebugSetElapsed(m_SkipToSeconds);

        if (keyboard.bKey.wasPressedThisFrame && m_Coronado != null)
            m_Coronado.DebugSummon(m_CoronadoSummonDistance);

        if (keyboard.jKey.wasPressedThisFrame && m_BossIntro != null)
            m_BossIntro.DebugTriggerNow();

        // '4' instantly burns the fire out, to test the outage (fast swarm,
        // retreating enemies, relight-to-resume) without waiting.
        if (keyboard.digit4Key.wasPressedThisFrame)
        {
            var campfire = Object.FindAnyObjectByType<CampfireFuel>();
            if (campfire != null)
                campfire.AddFuel(-campfire.CurrentFuel);
        }
    }
}

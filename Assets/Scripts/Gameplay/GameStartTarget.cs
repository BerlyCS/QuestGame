using System.Collections;
using UnityEngine;

/// <summary>
/// The ceremonial first shot: a block standing behind the campfire with a
/// floating "INICIAR JUEGO" label. The night does not begin on scene load - the
/// wolf howl, the survival clock and the enemy spawners all wait for the arrow
/// that lands on this block (see <see cref="IArrowHittable"/>), so the player
/// has to shoot to start. It doubles as the last step of the bow's tutorial.
///
/// On the hit the treasure drops beside the fire (see <see cref="TreasureReveal"/>)
/// and, after <see cref="m_HideDelay"/>, the whole board - block, bullseye and
/// label - steps out of the way so the sightline into the forest is clear.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class GameStartTarget : MonoBehaviour, IArrowHittable
{
    [Header("References")]
    [Tooltip("The floating label above the block. Hidden once the night starts.")]
    [SerializeField] GameObject m_Label;
    [Tooltip("The whole board: block, bullseye rings and label. Hidden once the night starts. " +
             "Leave empty to hide the block's parent, so the rings go with the block.")]
    [SerializeField] GameObject m_HideRoot;
    [Tooltip("What the label faces. Defaults to the main camera.")]
    [SerializeField] Transform m_LabelTarget;
    [SerializeField] GameManager m_GameManager;
    [SerializeField] TreasureReveal m_Treasure;
    [Tooltip("Pulsed when the arrow lands. Optional.")]
    [SerializeField] Renderer m_BlockRenderer;

    [Header("On hit")]
    [Tooltip("Seconds the struck block stays up before it is hidden.")]
    [SerializeField] float m_HideDelay = 1.4f;
    [Tooltip("Optional extra sound on the hit. The arrow plays its own impact.")]
    [SerializeField] AudioClip m_HitSfx;

    bool m_Started;
    MaterialPropertyBlock m_BlockProperties;

    /// <summary>True once an arrow has landed: the night is running.</summary>
    public bool HasStarted => m_Started;

    void Awake()
    {
        m_BlockProperties = new MaterialPropertyBlock();
    }

    /// <summary>Called by the arrow that lands on the block.</summary>
    public void Hit(Arrow arrow)
    {
        BeginNight();
    }

    /// <summary>
    /// Starts the night: treasure, clock and spawners. Idempotent, so a second
    /// arrow changes nothing. Public so editor tooling (and DebugKeys) can also
    /// trigger it.
    /// </summary>
    public void BeginNight()
    {
        if (m_Started)
            return;
        m_Started = true;

        if (m_HitSfx != null)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.playOnAwake = false;
            source.PlayOneShot(m_HitSfx);
        }

        if (m_BlockRenderer != null)
        {
            m_BlockRenderer.GetPropertyBlock(m_BlockProperties);
            m_BlockProperties.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.15f) * 2f);
            m_BlockRenderer.SetPropertyBlock(m_BlockProperties);
        }

        if (m_Treasure != null)
            m_Treasure.Reveal();

        if (m_GameManager != null)
            m_GameManager.BeginNight();

        StartCoroutine(HideAfterDelay());
    }

    void Update()
    {
        if (m_Label == null || !m_Label.activeSelf)
            return;

        Transform target = m_LabelTarget != null
            ? m_LabelTarget
            : (Camera.main != null ? Camera.main.transform : null);
        if (target == null)
            return;

        // The readable face of a world canvas points opposite its forward, so
        // aim the forward from the viewer out through the label.
        Vector3 forward = Vector3.ProjectOnPlane(m_Label.transform.position - target.position, Vector3.up);
        if (forward.sqrMagnitude > 0.0001f)
            m_Label.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(m_HideDelay);

        if (m_Label != null)
            m_Label.SetActive(false);

        // The block alone is not the whole target: the bullseye rings hang off the
        // board root, so hiding only this object would leave them floating in the
        // air. Take the whole board down.
        var board = m_HideRoot != null
            ? m_HideRoot
            : (transform.parent != null ? transform.parent.gameObject : gameObject);
        board.SetActive(false);
    }
}

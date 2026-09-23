using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A grabbable log. Once the player has grabbed it at least once, it is
/// consumed and adds fuel to the fire as soon as it overlaps the campfire's
/// fuel trigger. Requiring a grab first means a log staged so it already
/// touches the fire (see GameSceneBuilder's tutorial log) sits there inertly
/// as a visual hint until the player actually picks it up, instead of
/// igniting itself the moment the scene loads. Grab behaviour is provided by
/// the Meta Interaction SDK components attached by the scene builder.
/// </summary>
[RequireComponent(typeof(Grabbable))]
[RequireComponent(typeof(GrabHighlight))]
public class Log : MonoBehaviour
{
    [SerializeField]
        [Tooltip("How much fuel this log adds when thrown into the fire, in seconds. " +
            "One log should visibly revive the dead prologue fire, so it lands around " +
            "half the bar rather than a weak ember.")]
        float m_FuelValue = 45f;

    [SerializeField]
    [Tooltip("Optional effect spawned when the log is consumed.")]
    GameObject m_ConsumeEffect;

    [SerializeField] UnityEvent m_OnConsumed = new UnityEvent();

    [SerializeField]
    [Tooltip("Fired the first time a hand takes hold of this log, before it is ever fed " +
        "to the fire. The prologue uses it to raise the campfire's drop-off cue.")]
    UnityEvent m_OnGrabbed = new UnityEvent();

    Grabbable m_Grabbable;
    bool m_HasBeenGrabbed;
    bool m_Consumed;

    static AudioClip s_FeedClip;

    public UnityEvent OnConsumed => m_OnConsumed;
    public UnityEvent OnGrabbed => m_OnGrabbed;

    void Awake()
    {
        m_Grabbable = GetComponent<Grabbable>();
        m_Grabbable.WhenPointerEventRaised += HandlePointerEvent;
        FitColliderToVisuals();
    }

    /// <summary>
    /// Rebuilds the box collider from the log model's current render bounds, in
    /// this transform's local space. The scene builder fits the collider at
    /// build time, but the visual can be resized afterwards (import scale, a
    /// model swap, a manual tweak) which would otherwise leave an oversized
    /// invisible hitbox. Doing it here keeps the hitbox glued to the mesh the
    /// player actually sees.
    /// </summary>
    void FitColliderToVisuals()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null)
            return;

        bool found = false;
        Bounds localBounds = default;

        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                continue;

            // Mesh corners (not renderer.bounds) so a rotated model can't inflate
            // the fit with a loose world-space AABB.
            Bounds mesh = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f));
                Vector3 local = transform.InverseTransformPoint(renderer.transform.TransformPoint(corner));

                if (!found)
                {
                    localBounds = new Bounds(local, Vector3.zero);
                    found = true;
                }
                else
                {
                    localBounds.Encapsulate(local);
                }
            }
        }

        if (!found)
            return;

        box.center = localBounds.center;
        box.size = localBounds.size;
    }

    void OnDestroy()
    {
        m_Grabbable.WhenPointerEventRaised -= HandlePointerEvent;
    }

    void HandlePointerEvent(PointerEvent evt)
    {
        if (evt.Type != PointerEventType.Select || m_HasBeenGrabbed)
            return;

        m_HasBeenGrabbed = true;
        m_OnGrabbed.Invoke();
    }

    void OnTriggerEnter(Collider other) => TryConsume(other);
    void OnTriggerStay(Collider other) => TryConsume(other);

    void TryConsume(Collider other)
    {
        if (m_Consumed || !m_HasBeenGrabbed)
            return;

        var campfire = other.GetComponentInParent<CampfireFuel>();
        if (campfire == null)
            return;

        m_Consumed = true;
        campfire.AddFuel(m_FuelValue);

        // Without haptics the player has to see and hear the log catch: a whoosh at
        // the fire, a bright flash and a flare-up of the flames. The recorded
        // fire_whoosh asset is used when present (see Assets/Resources/Fire); the
        // code-synthesized whoosh is the fallback so the cue never goes silent.
        campfire.Flare();
        AudioClip feedClip = s_FeedClip ??= Resources.Load<AudioClip>("Fire/fire_whoosh");
        ProceduralSfx.PlayAt(feedClip != null ? feedClip : ProceduralSfx.FireWhoosh,
            campfire.transform.position + Vector3.up * 0.3f, 1f);
        FadingGlow.Spawn(campfire.transform.position + Vector3.up * 0.35f, 0.8f, new Color(1.6f, 0.7f, 0.15f), 0.5f);

        if (m_ConsumeEffect != null)
            Instantiate(m_ConsumeEffect, transform.position, Quaternion.identity);

        m_OnConsumed.Invoke();
        Destroy(gameObject);
    }
}

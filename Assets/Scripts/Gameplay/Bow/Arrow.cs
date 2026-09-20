using System.Collections;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

/// <summary>
/// A grabbable arrow that can be nocked on a <see cref="BowNotch"/>, drawn back
/// with <see cref="BowPullMeasurer"/>, launched, and finally embedded into
/// whatever it hits.
///
/// Rewritten from the XR Interaction Toolkit version used by the OOT shooting
/// gallery to use the Meta Interaction SDK (Grabbable / GrabInteractable /
/// HandGrabInteractable) that the rest of this project uses.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public class Arrow : MonoBehaviour
{
    [Header("Flight")]
    [SerializeField]
    [Tooltip("Multiplier applied to the normalized pull amount when launching.")]
    float m_Speed = 35f;

    [SerializeField]
    [Tooltip("Guaranteed minimum launch speed, even with a very light pull.")]
    float m_MinLaunchSpeed = 5f;

    [Header("Audio")]
    [SerializeField] AudioSource m_HitAudioSource;
    [SerializeField] AudioSource m_ShotAudioSource;

    [Header("Cleanup")]
    [SerializeField]
    [Tooltip("Seconds after being fired before the arrow despawns.")]
    float m_DespawnDelay = 20f;

    [Header("References")]
    [SerializeField]
    [Tooltip("Sweep caster used while the arrow flies. Auto-found when empty.")]
    ArrowCaster m_Caster;

    [SerializeField]
    [Tooltip("Nose transform used to align the arrow with its velocity.")]
    Transform m_Tip;

    Rigidbody m_Rigidbody;
    Grabbable m_Grabbable;
    GrabInteractable m_GrabInteractable;
    HandGrabInteractable m_HandGrabInteractable;
    Collider[] m_Colliders;

    GrabInteractor m_GrabInteractor;
    HandGrabInteractor m_HandGrabInteractor;

    bool m_Launched;
    bool m_Stuck;
    bool m_Nocked;
    Coroutine m_LaunchRoutine;
    Coroutine m_DespawnRoutine;

    public bool IsLaunched => m_Launched;
    public bool IsStuck => m_Stuck;
    public bool IsNocked => m_Nocked;

    public GrabInteractable GrabInteractable => m_GrabInteractable;
    public HandGrabInteractable HandGrabInteractable => m_HandGrabInteractable;

    void Awake()
    {
        m_Rigidbody = GetComponent<Rigidbody>();
        m_Grabbable = GetComponentInChildren<Grabbable>(true);
        m_GrabInteractable = GetComponentInChildren<GrabInteractable>(true);
        m_HandGrabInteractable = GetComponentInChildren<HandGrabInteractable>(true);
        m_Colliders = GetComponentsInChildren<Collider>(true);

        if (m_Caster == null)
        {
            m_Caster = GetComponentInChildren<ArrowCaster>(true);
        }

        if (m_Tip == null && m_Caster != null)
        {
            m_Tip = m_Caster.Tip;
        }
    }

    void OnEnable()
    {
        if (m_GrabInteractable != null)
        {
            m_GrabInteractable.WhenSelectingInteractorAdded.Action += OnGrabInteractorAdded;
            m_GrabInteractable.WhenSelectingInteractorRemoved.Action += OnGrabInteractorRemoved;
        }

        if (m_HandGrabInteractable != null)
        {
            m_HandGrabInteractable.WhenSelectingInteractorAdded.Action += OnHandGrabInteractorAdded;
            m_HandGrabInteractable.WhenSelectingInteractorRemoved.Action += OnHandGrabInteractorRemoved;
        }
    }

    void OnDisable()
    {
        if (m_GrabInteractable != null)
        {
            m_GrabInteractable.WhenSelectingInteractorAdded.Action -= OnGrabInteractorAdded;
            m_GrabInteractable.WhenSelectingInteractorRemoved.Action -= OnGrabInteractorRemoved;
        }

        if (m_HandGrabInteractable != null)
        {
            m_HandGrabInteractable.WhenSelectingInteractorAdded.Action -= OnHandGrabInteractorAdded;
            m_HandGrabInteractable.WhenSelectingInteractorRemoved.Action -= OnHandGrabInteractorRemoved;
        }
    }

    void OnGrabInteractorAdded(GrabInteractor interactor)
    {
        m_GrabInteractor = interactor;
        ReviveIfStuck();
    }

    void OnGrabInteractorRemoved(GrabInteractor interactor)
    {
        if (ReferenceEquals(m_GrabInteractor, interactor))
        {
            m_GrabInteractor = null;
        }
    }

    void OnHandGrabInteractorAdded(HandGrabInteractor interactor)
    {
        m_HandGrabInteractor = interactor;
        ReviveIfStuck();
    }

    void OnHandGrabInteractorRemoved(HandGrabInteractor interactor)
    {
        if (ReferenceEquals(m_HandGrabInteractor, interactor))
        {
            m_HandGrabInteractor = null;
        }
    }

    /// <summary>
    /// Parents the arrow to the bow string and disables its physics/grabbing.
    /// Returns false when the arrow cannot be nocked right now.
    /// </summary>
    public bool TryNock(Transform nockPoint)
    {
        if (m_Nocked || m_Launched || m_Stuck || nockPoint == null)
        {
            return false;
        }

        ReleaseHolders();

        m_Nocked = true;
        SetGrabEnabled(false);
        SetCollidersEnabled(true);
        CancelDespawn();

        m_Rigidbody.linearVelocity = Vector3.zero;
        m_Rigidbody.angularVelocity = Vector3.zero;
        m_Rigidbody.isKinematic = true;
        m_Rigidbody.useGravity = false;

        // worldPositionStays keeps the arrow's own size even though the bow is
        // scaled, then the local pose is snapped onto the string.
        transform.SetParent(nockPoint, true);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        if (m_Caster != null)
        {
            m_Caster.ResetCaster();
        }

        return true;
    }

    /// <summary>Detaches the arrow from the string without firing it.</summary>
    public void Unnock()
    {
        if (!m_Nocked)
        {
            return;
        }

        m_Nocked = false;
        transform.SetParent(null, true);
        m_Rigidbody.isKinematic = false;
        m_Rigidbody.useGravity = true;
        SetCollidersEnabled(true);
        SetGrabEnabled(true);
    }

    /// <summary>
    /// Fires the nocked arrow. <paramref name="pullAmount"/> is the 0..1 draw
    /// amount supplied by the <see cref="BowPullMeasurer"/>.
    /// </summary>
    public void Launch(float pullAmount)
    {
        if (!m_Nocked)
        {
            return;
        }

        m_Nocked = false;
        transform.SetParent(null, true);

        if (m_ShotAudioSource != null)
        {
            m_ShotAudioSource.Play();
        }

        m_Launched = true;
        SetGrabEnabled(false);

        // The flight is resolved by the ArrowCaster line-cast, so the arrow's
        // own collider is disabled to stop it from catching on the bow frame.
        SetCollidersEnabled(false);

        m_Rigidbody.isKinematic = false;
        m_Rigidbody.useGravity = true;
        m_Rigidbody.linearVelocity = Vector3.zero;
        m_Rigidbody.angularVelocity = Vector3.zero;

        if (m_Caster != null)
        {
            m_Caster.ResetCaster();
        }

        float speed = Mathf.Max(pullAmount * m_Speed, m_MinLaunchSpeed);
        m_Rigidbody.AddForce(NoseDirection() * speed, ForceMode.VelocityChange);

        m_LaunchRoutine = StartCoroutine(LaunchRoutine());

        CancelDespawn();
        m_DespawnRoutine = StartCoroutine(DespawnRoutine());
    }

    Vector3 NoseDirection()
    {
        Transform tip = m_Tip != null ? m_Tip : (m_Caster != null ? m_Caster.Tip : null);
        if (tip == null)
        {
            return transform.forward;
        }

        Vector3 direction = tip.position - transform.position;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
    }

    IEnumerator LaunchRoutine()
    {
        if (m_Caster == null)
        {
            yield break;
        }

        RaycastHit hit;
        while (!m_Caster.CheckForCollision(out hit))
        {
            yield return null;
        }

        OnHit(hit);
        m_LaunchRoutine = null;
    }

    void OnHit(RaycastHit hit)
    {
        m_Launched = false;
        m_Stuck = true;

        if (m_HitAudioSource != null)
        {
            m_HitAudioSource.Play();
        }

        m_Rigidbody.linearVelocity = Vector3.zero;
        m_Rigidbody.angularVelocity = Vector3.zero;
        m_Rigidbody.isKinematic = true;
        m_Rigidbody.useGravity = false;

        transform.SetParent(hit.transform, true);

        IArrowHittable hittable = hit.transform.GetComponentInParent<IArrowHittable>();
        if (hittable != null)
        {
            hittable.Hit(this);
        }

        SetCollidersEnabled(true);
        SetGrabEnabled(true);
    }

    void FixedUpdate()
    {
        // Keep the nose pointed along the direction of travel while flying.
        if (!m_Launched)
        {
            return;
        }

        Vector3 velocity = m_Rigidbody.linearVelocity;
        if (velocity.sqrMagnitude > 0.25f)
        {
            transform.rotation = Quaternion.LookRotation(velocity, Vector3.up);
        }
    }

    void ReviveIfStuck()
    {
        if (!m_Stuck)
        {
            return;
        }

        m_Stuck = false;
        if (m_LaunchRoutine != null)
        {
            StopCoroutine(m_LaunchRoutine);
            m_LaunchRoutine = null;
        }

        CancelDespawn();

        transform.SetParent(null, true);
        m_Rigidbody.isKinematic = false;
        m_Rigidbody.useGravity = true;
        SetCollidersEnabled(true);
    }

    void ReleaseHolders()
    {
        if (m_GrabInteractor != null)
        {
            m_GrabInteractor.ForceRelease();
            m_GrabInteractor = null;
        }

        if (m_HandGrabInteractor != null)
        {
            m_HandGrabInteractor.ForceRelease();
            m_HandGrabInteractor = null;
        }
    }

    void SetGrabEnabled(bool enabled)
    {
        if (m_GrabInteractable != null)
        {
            m_GrabInteractable.enabled = enabled;
        }

        if (m_HandGrabInteractable != null)
        {
            m_HandGrabInteractable.enabled = enabled;
        }
    }

    void SetCollidersEnabled(bool enabled)
    {
        if (m_Colliders == null)
        {
            return;
        }

        for (int i = 0; i < m_Colliders.Length; i++)
        {
            if (m_Colliders[i] != null)
            {
                m_Colliders[i].enabled = enabled;
            }
        }
    }

    void CancelDespawn()
    {
        if (m_DespawnRoutine != null)
        {
            StopCoroutine(m_DespawnRoutine);
            m_DespawnRoutine = null;
        }
    }

    IEnumerator DespawnRoutine()
    {
        yield return new WaitForSeconds(m_DespawnDelay);
        m_DespawnRoutine = null;
        Destroy(gameObject);
    }

    public void InjectReferences(
        ArrowCaster caster,
        Transform tip,
        AudioSource hitAudioSource,
        AudioSource shotAudioSource)
    {
        m_Caster = caster;
        m_Tip = tip;
        m_HitAudioSource = hitAudioSource;
        m_ShotAudioSource = shotAudioSource;
    }
}

using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;
using InteractionHandedness = Oculus.Interaction.Input.Handedness;

/// <summary>
/// Vibrates the matching Touch controller when the GrabInteractor or HandGrabInteractor
/// on this GameObject starts hovering, grabs, or releases an interactable. Attach one
/// instance to every per-hand interactor in the XR rig (see GameSceneBuilder); the
/// controller side is resolved lazily at runtime, so the same component works unmodified
/// on either hand.
/// </summary>
[DisallowMultipleComponent]
public class InteractorHaptics : MonoBehaviour
{
    [Header("Pulses")]
    [SerializeField] float m_GrabAmplitude = 0.5f;
    [SerializeField] float m_GrabDuration = 0.07f;
    [SerializeField] float m_ReleaseAmplitude = 0.15f;
    [SerializeField] float m_ReleaseDuration = 0.04f;
    [SerializeField] float m_HoverAmplitude = 0.06f;
    [SerializeField] float m_HoverDuration = 0.015f;

    static readonly Dictionary<GameObject, OVRInput.Controller> s_HeldBy =
        new Dictionary<GameObject, OVRInput.Controller>();

    GrabInteractor m_GrabInteractor;
    HandGrabInteractor m_HandGrabInteractor;
    IInteractorView m_InteractorView;
    OVRInput.Controller m_Controller = OVRInput.Controller.None;

    /// <summary>
    /// The controller currently holding <paramref name="grabbedObject"/>, if any. Lets
    /// gameplay code (e.g. EnemyBanisher) vibrate the hand responsible for an effect.
    /// </summary>
    public static bool TryGetHoldingController(GameObject grabbedObject, out OVRInput.Controller controller)
    {
        return s_HeldBy.TryGetValue(grabbedObject, out controller);
    }

    void Awake()
    {
        m_GrabInteractor = GetComponent<GrabInteractor>();
        m_HandGrabInteractor = GetComponent<HandGrabInteractor>();
        m_InteractorView = GetComponent<IInteractorView>();
    }

    void OnEnable()
    {
        if (m_InteractorView != null)
            m_InteractorView.WhenStateChanged += HandleStateChanged;

        if (m_GrabInteractor != null)
        {
            m_GrabInteractor.WhenInteractableSelected.Action += HandleGrabSelected;
            m_GrabInteractor.WhenInteractableUnselected.Action += HandleGrabUnselected;
        }

        if (m_HandGrabInteractor != null)
        {
            m_HandGrabInteractor.WhenInteractableSelected.Action += HandleHandGrabSelected;
            m_HandGrabInteractor.WhenInteractableUnselected.Action += HandleHandGrabUnselected;
        }
    }

    void OnDisable()
    {
        if (m_InteractorView != null)
            m_InteractorView.WhenStateChanged -= HandleStateChanged;

        if (m_GrabInteractor != null)
        {
            m_GrabInteractor.WhenInteractableSelected.Action -= HandleGrabSelected;
            m_GrabInteractor.WhenInteractableUnselected.Action -= HandleGrabUnselected;
        }

        if (m_HandGrabInteractor != null)
        {
            m_HandGrabInteractor.WhenInteractableSelected.Action -= HandleHandGrabSelected;
            m_HandGrabInteractor.WhenInteractableUnselected.Action -= HandleHandGrabUnselected;
        }
    }

    void HandleStateChanged(InteractorStateChangeArgs args)
    {
        if (args.PreviousState == InteractorState.Normal && args.NewState == InteractorState.Hover)
            HapticsUtility.Pulse(ResolveController(), m_HoverAmplitude, m_HoverDuration);
    }

    void HandleGrabSelected(GrabInteractable interactable) => OnGrabbed(interactable != null ? interactable.gameObject : null);
    void HandleGrabUnselected(GrabInteractable interactable) => OnReleased(interactable != null ? interactable.gameObject : null);
    void HandleHandGrabSelected(HandGrabInteractable interactable) => OnGrabbed(interactable != null ? interactable.gameObject : null);
    void HandleHandGrabUnselected(HandGrabInteractable interactable) => OnReleased(interactable != null ? interactable.gameObject : null);

    void OnGrabbed(GameObject grabbedObject)
    {
        var controller = ResolveController();
        HapticsUtility.Pulse(controller, m_GrabAmplitude, m_GrabDuration);
        if (grabbedObject != null)
            s_HeldBy[grabbedObject] = controller;
    }

    void OnReleased(GameObject grabbedObject)
    {
        HapticsUtility.Pulse(ResolveController(), m_ReleaseAmplitude, m_ReleaseDuration);
        if (grabbedObject != null)
            s_HeldBy.Remove(grabbedObject);
    }

    OVRInput.Controller ResolveController()
    {
        if (m_Controller != OVRInput.Controller.None)
            return m_Controller;

        if (m_HandGrabInteractor != null && m_HandGrabInteractor.Hand != null)
        {
            m_Controller = m_HandGrabInteractor.Hand.Handedness == InteractionHandedness.Left
                ? OVRInput.Controller.LTouch
                : OVRInput.Controller.RTouch;
            return m_Controller;
        }

        if (m_GrabInteractor != null && m_GrabInteractor.Rigidbody != null)
        {
            m_Controller = ResolveFromHierarchy(m_GrabInteractor.Rigidbody.transform);
            return m_Controller;
        }

        return OVRInput.Controller.None;
    }

    static OVRInput.Controller ResolveFromHierarchy(Transform start)
    {
        for (var current = start; current != null; current = current.parent)
        {
            if (current.name.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return OVRInput.Controller.LTouch;
            if (current.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return OVRInput.Controller.RTouch;
        }
        return OVRInput.Controller.None;
    }
}

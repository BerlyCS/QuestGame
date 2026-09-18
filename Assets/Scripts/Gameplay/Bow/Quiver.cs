using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

/// <summary>
/// Spawns a fresh <see cref="Arrow"/> into any empty hand that reaches into the
/// quiver. This reproduces the "grab the quiver to pull an arrow" behaviour of
/// the OOT shooting gallery without fighting the Interaction SDK's single
/// selected-interactable-per-interactor model.
/// </summary>
[DisallowMultipleComponent]
public class Quiver : MonoBehaviour
{
    [SerializeField] GameObject m_ArrowPrefab;

    [SerializeField]
    [Tooltip("Where arrows appear. Defaults to this transform.")]
    Transform m_SpawnPoint;

    [SerializeField]
    [Tooltip("How close a hand must be before an arrow is spawned.")]
    float m_GrabRadius = 0.3f;

    [SerializeField]
    [Tooltip("Minimum seconds between two spawned arrows.")]
    float m_SpawnCooldown = 0.5f;

    readonly List<HandGrabInteractor> m_HandInteractors = new List<HandGrabInteractor>();
    readonly List<GrabInteractor> m_GrabInteractors = new List<GrabInteractor>();
    float m_NextAllowedSpawnTime;

    void Awake()
    {
        if (m_SpawnPoint == null)
        {
            m_SpawnPoint = transform;
        }
    }

    void Update()
    {
        if (m_ArrowPrefab == null || Time.time < m_NextAllowedSpawnTime)
        {
            return;
        }

        if (Time.frameCount % 10 == 0)
        {
            RefreshInteractors();
        }

        Vector3 point = m_SpawnPoint.position;
        float radiusSqr = m_GrabRadius * m_GrabRadius;

        foreach (HandGrabInteractor hand in m_HandInteractors)
        {
            if (!IsFree(hand) || (hand.transform.position - point).sqrMagnitude > radiusSqr)
            {
                continue;
            }

            SpawnIntoHand(hand);
            return;
        }

        foreach (GrabInteractor grab in m_GrabInteractors)
        {
            if (!IsFree(grab) || (grab.transform.position - point).sqrMagnitude > radiusSqr)
            {
                continue;
            }

            SpawnIntoGrab(grab);
            return;
        }
    }

    static bool IsFree(HandGrabInteractor interactor)
    {
        return interactor != null && interactor.isActiveAndEnabled && !interactor.HasSelectedInteractable;
    }

    static bool IsFree(GrabInteractor interactor)
    {
        return interactor != null && interactor.isActiveAndEnabled && !interactor.HasSelectedInteractable;
    }

    void RefreshInteractors()
    {
        m_HandInteractors.Clear();
        m_HandInteractors.AddRange(FindObjectsByType<HandGrabInteractor>(FindObjectsSortMode.None));

        m_GrabInteractors.Clear();
        m_GrabInteractors.AddRange(FindObjectsByType<GrabInteractor>(FindObjectsSortMode.None));
    }

    void SpawnIntoHand(HandGrabInteractor hand)
    {
        Arrow arrow = CreateArrow(hand.transform);
        if (arrow == null || arrow.HandGrabInteractable == null)
        {
            return;
        }

        m_NextAllowedSpawnTime = Time.time + m_SpawnCooldown;
        hand.ForceSelect(arrow.HandGrabInteractable, true);
    }

    void SpawnIntoGrab(GrabInteractor grab)
    {
        Arrow arrow = CreateArrow(grab.transform);
        if (arrow == null || arrow.GrabInteractable == null)
        {
            return;
        }

        m_NextAllowedSpawnTime = Time.time + m_SpawnCooldown;
        grab.ForceSelect(arrow.GrabInteractable);
    }

    Arrow CreateArrow(Transform orientation)
    {
        GameObject arrowObject = Instantiate(
            m_ArrowPrefab,
            orientation.position + orientation.forward * 0.15f,
            orientation.rotation);
        return arrowObject.GetComponentInChildren<Arrow>(true);
    }

    public void Configure(GameObject arrowPrefab, Transform spawnPoint)
    {
        m_ArrowPrefab = arrowPrefab;
        m_SpawnPoint = spawnPoint;
    }
}

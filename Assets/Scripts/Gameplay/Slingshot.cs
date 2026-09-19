using System.Collections;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

/// <summary>
/// The player's only weapon: a Y-shaped slingshot frame resting on the weapon
/// rack. One hand grabs the frame (HandGrabInteractable, same gesture as
/// everything else); the other hand grabs an ember and pulls it back against
/// the fork, and the shot flies along the real ember-to-fork vector (see
/// SlingEmberHandle). Owns the elastic band (slack between the two prongs,
/// stretched to the ember while drawing) and the predicted-arc line that
/// makes aiming with bare hands readable.
///
/// Held/released is polled from Grabbable.SelectingPointsCount, not events
/// (hand-tracking release is irregular, see armas.md). Once released, the frame
/// falls, then always flies back to its spot on the rack after a short delay.
/// </summary>
[RequireComponent(typeof(Grabbable))]
[RequireComponent(typeof(Rigidbody))]
public class Slingshot : MonoBehaviour
{
    [Header("Parts")]
    [SerializeField] Transform m_RackPoint;
    [SerializeField] Transform m_Fork;
    [SerializeField] Transform m_LeftTip;
    [SerializeField] Transform m_RightTip;
    [SerializeField] Material m_BandMaterial;
    [Tooltip("Additive glow material for the enemy the current aim would hit.")]
    [SerializeField] Material m_TargetGlowMaterial;

    [Header("Return to rack")]
    [SerializeField] float m_ReturnDelay = 1.5f;
    [SerializeField] float m_ReturnSpeed = 4f;
    [SerializeField] float m_TurnSpeed = 360f;
    [SerializeField] float m_CatchDistance = 0.05f;

    [Header("Band")]
    [SerializeField] Color m_BandIdleColor = new Color(0.6f, 0.3f, 0.1f);
    [SerializeField] float m_BandWidth = 0.012f;
    [SerializeField] int m_ArcPoints = 24;
    [SerializeField] float m_ArcStep = 0.06f;

    Grabbable m_Grabbable;
    Rigidbody m_Rigidbody;
    HandGrabInteractable m_HandGrab;
    DistanceHandGrabInteractable m_DistanceHandGrab;
    LineRenderer m_Band;
    LineRenderer m_Arc;

    bool m_WasHeld;
    bool m_Pulling;
    Vector3 m_PullPoint;
    Coroutine m_ReturnRoutine;
    AimGlow m_Aimed;

    /// <summary>True while a hand is actually gripping the frame.</summary>
    public bool IsHeld => m_Grabbable.SelectingPointsCount > 0;

    /// <summary>World position the shot leaves from: the band's anchor.</summary>
    public Vector3 ForkPosition => m_Fork.position;

    void Awake()
    {
        m_Grabbable = GetComponent<Grabbable>();
        m_Rigidbody = GetComponent<Rigidbody>();
        m_HandGrab = GetComponent<HandGrabInteractable>();
        m_DistanceHandGrab = GetComponent<DistanceHandGrabInteractable>();

        // Rests on the rack, weightless, until it is first grabbed.
        m_Rigidbody.isKinematic = true;
        m_Rigidbody.useGravity = false;

        m_Band = CreateLine("Sling Band", 3, m_BandWidth);
        m_Arc = CreateLine("Aim Arc", m_ArcPoints, m_BandWidth * 0.6f);
        m_Arc.gameObject.SetActive(false);
    }

    LineRenderer CreateLine(string name, int points, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = points;
        line.startWidth = width;
        line.endWidth = width;
        line.sharedMaterial = m_BandMaterial;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.startColor = m_BandIdleColor;
        line.endColor = m_BandIdleColor;
        return line;
    }

    void Update()
    {
        bool held = IsHeld;

        if (held && !m_WasHeld)
        {
            if (m_ReturnRoutine != null)
            {
                StopCoroutine(m_ReturnRoutine);
                m_ReturnRoutine = null;
            }
            SetGrabbableEnabled(true);
        }
        else if (!held && m_WasHeld)
        {
            m_ReturnRoutine = StartCoroutine(FallThenReturn());
        }

        m_WasHeld = held;
    }

    void LateUpdate()
    {
        Vector3 left = m_LeftTip.position;
        Vector3 right = m_RightTip.position;
        m_Band.SetPosition(0, left);
        m_Band.SetPosition(1, m_Pulling ? m_PullPoint : (left + right) * 0.5f);
        m_Band.SetPosition(2, right);
    }

    /// <summary>Stretches the band's middle to <paramref name="emberPosition"/> and tints it.</summary>
    public void SetPull(Vector3 emberPosition, Color color)
    {
        m_Pulling = true;
        m_PullPoint = emberPosition;
        m_Band.startColor = color;
        m_Band.endColor = color;
    }

    public void ClearPull()
    {
        m_Pulling = false;
        m_Band.startColor = m_BandIdleColor;
        m_Band.endColor = m_BandIdleColor;
        m_Arc.gameObject.SetActive(false);
        SetAimedEnemy(null);
    }

    /// <summary>
    /// Draws the ballistic path a shot with this launch velocity would take, stops it at
    /// the first thing it would hit, and makes an enemy there glow ("this shot lands").
    /// </summary>
    public void ShowArc(Vector3 launchVelocity, Color color)
    {
        m_Arc.gameObject.SetActive(true);
        m_Arc.positionCount = m_ArcPoints;
        m_Arc.startColor = color;
        m_Arc.endColor = new Color(color.r, color.g, color.b, 0.15f);

        Vector3 origin = m_Fork.position;
        Vector3 previous = origin;
        int shown = m_ArcPoints;
        GameObject hitEnemy = null;
        m_Arc.SetPosition(0, origin);

        for (int i = 1; i < m_ArcPoints; i++)
        {
            float t = i * m_ArcStep;
            Vector3 point = origin + launchVelocity * t + 0.5f * Physics.gravity * t * t;

            if (Physics.Linecast(previous, point, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                hitEnemy = EnemyRootOf(hit.collider);
                shown = i + 1;
                m_Arc.SetPosition(i, point);
                break;
            }

            if (point.y < 0f)
            {
                point.y = 0f;
                shown = i + 1;
                m_Arc.SetPosition(i, point);
                break;
            }

            m_Arc.SetPosition(i, point);
            previous = point;
        }

        m_Arc.positionCount = Mathf.Max(2, shown);
        SetAimedEnemy(hitEnemy);
    }

    static GameObject EnemyRootOf(Collider collider)
    {
        var skeleton = collider.GetComponentInParent<Skeleton>();
        if (skeleton != null && skeleton.IsAlive)
            return skeleton.gameObject;

        var thrower = collider.GetComponentInParent<BoneThrower>();
        return thrower != null && thrower.IsAlive ? thrower.gameObject : null;
    }

    void SetAimedEnemy(GameObject enemy)
    {
        if (enemy == (m_Aimed != null ? m_Aimed.gameObject : null))
            return;

        if (m_Aimed != null)
            m_Aimed.SetAimed(false);

        m_Aimed = enemy != null && m_TargetGlowMaterial != null ? AimGlow.For(enemy, m_TargetGlowMaterial) : null;
        if (m_Aimed != null)
            m_Aimed.SetAimed(true);
    }

    void SetGrabbableEnabled(bool value)
    {
        if (m_HandGrab != null)
            m_HandGrab.enabled = value;
        if (m_DistanceHandGrab != null)
            m_DistanceHandGrab.enabled = value;
    }

    /// <summary>Drops under gravity for a moment, then flies home to the rack and waits there to be grabbed.</summary>
    IEnumerator FallThenReturn()
    {
        // One frame for the SDK to finish handing the rigidbody back before we override it.
        yield return null;
        m_Rigidbody.isKinematic = false;
        m_Rigidbody.useGravity = true;

        yield return new WaitForSeconds(m_ReturnDelay);
        if (IsHeld)
            yield break;

        SetGrabbableEnabled(false);
        m_Rigidbody.isKinematic = true;
        m_Rigidbody.useGravity = false;

        float speed = m_ReturnSpeed;
        while (!IsHeld && Vector3.Distance(m_Rigidbody.position, m_RackPoint.position) > m_CatchDistance)
        {
            speed = Mathf.Min(speed + 12f * Time.deltaTime, 12f);
            m_Rigidbody.Move(
                Vector3.MoveTowards(m_Rigidbody.position, m_RackPoint.position, speed * Time.deltaTime),
                Quaternion.RotateTowards(m_Rigidbody.rotation, m_RackPoint.rotation, m_TurnSpeed * Time.deltaTime));
            yield return null;
        }

        SetGrabbableEnabled(true);
        while (!IsHeld)
        {
            m_Rigidbody.Move(m_RackPoint.position, m_RackPoint.rotation);
            yield return null;
        }

        m_ReturnRoutine = null;
    }
}

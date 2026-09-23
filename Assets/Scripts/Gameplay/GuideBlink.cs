using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// A wordless tutorial cue: a soft additive sphere that pulses above an object
/// to draw the eye to it, then gets out of the way once its lesson is learned.
///
/// It follows the same idea as the bow's grip/string markers
/// (see <see cref="BowTutorial"/>): the glow is built from the shared indicator
/// material so it reads in the dark, it carries no collider so it can never be
/// grabbed, and it lives on the Ignore Raycast layer so it stays out of the
/// arrow's flight sweep.
///
/// Two optional stop conditions cover the tutorial beats: the axe's marker
/// disappears the moment the player grabs the axe, and the logs' markers stay
/// up while the fire is low and vanish once the fire has been fed back to
/// health (see <see cref="CampfireFuel"/>).
/// </summary>
[DisallowMultipleComponent]
public class GuideBlink : MonoBehaviour
{
    [Header("Glow")]
    [SerializeField]
    [Tooltip("Additive transparent material used for the marker shell. Supplied by the scene builder.")]
    Material m_GlowMaterial;

    [SerializeField] Color m_Color = new Color(1f, 0.78f, 0.3f, 1f);
    [SerializeField] float m_BlinkSpeed = 5f;
    [Range(0f, 1f)] [SerializeField] float m_BlinkMin = 0.1f;
    [Range(0f, 1f)] [SerializeField] float m_BlinkMax = 0.85f;

    [Header("Placement")]
    [Tooltip("Diameter of the marker sphere, in metres.")]
    [SerializeField] float m_SphereScale = 0.16f;

    [Tooltip("Gap between the object's top and the bottom of the marker sphere, in metres.")]
    [SerializeField] float m_Margin = 0.06f;

    [Header("Stop conditions")]
    [Tooltip("Hides the marker for good once the object has been grabbed.")]
    [SerializeField] bool m_StopOnGrab = true;

    [Tooltip("Hides the marker for good once the campfire has been fed back up to a healthy level.")]
    [SerializeField] bool m_StopWhenFireHealthy;

    [Range(0f, 1f)]
    [Tooltip("Fuel (0-1) the fire must reach before a Stop When Fire Healthy marker goes away.")]
    [SerializeField] float m_FireHealthy = 0.72f;

    static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int s_ColorId = Shader.PropertyToID("_Color");
    static CampfireFuel s_Campfire;

    Grabbable m_Grabbable;
    CampfireFuel m_Campfire;
    Renderer m_Marker;
    MaterialPropertyBlock m_Block;
    bool m_Done;

    /// <summary>
    /// Scene-builder hook: the marker material has no Interaction SDK injection
    /// point, so the builder hands it over explicitly.
    /// </summary>
    public void InjectGlowMaterial(Material glowMaterial) => m_GlowMaterial = glowMaterial;

    void Start()
    {
        // Built in Start, not Awake, so GrabHighlight's halo shells (also built in
        // Awake) never wrap the marker itself.
        m_Block = new MaterialPropertyBlock();
        m_Grabbable = GetComponentInParent<Grabbable>();

        if (m_StopWhenFireHealthy)
        {
            if (s_Campfire == null)
                s_Campfire = FindAnyObjectByType<CampfireFuel>();
            m_Campfire = s_Campfire;
        }

        BuildMarker();
    }

    void Update()
    {
        if (m_Marker == null)
            return;

        if (m_Done)
            return;

        if (ShouldStop())
        {
            m_Done = true;
            m_Marker.enabled = false;
            Destroy(m_Marker.gameObject);
            return;
        }

        m_Marker.enabled = true;

        // The glow material is additive, so the pulse reads as a brightness
        // change: the sphere sinks towards invisible and swells back up.
        float pulse = Mathf.Lerp(m_BlinkMin, m_BlinkMax,
            0.5f + 0.5f * Mathf.Sin(Time.time * m_BlinkSpeed));
        Color color = m_Color * pulse;
        color.a = m_Color.a * pulse;

        m_Marker.GetPropertyBlock(m_Block);
        m_Block.SetColor(s_BaseColorId, color);
        m_Block.SetColor(s_ColorId, color);
        m_Marker.SetPropertyBlock(m_Block);
    }

    void BuildMarker()
    {
        if (m_GlowMaterial == null)
            return;

        // Built without a collider on purpose. The marker is parented under the
        // grabbable's Rigidbody, and the Interaction SDK caches every collider in
        // that hierarchy once, in Start (HandGrabInteractable.Colliders). A
        // primitive collider destroyed a frame later leaves a dead reference in
        // that cache, which the distance-grab scan dereferences every frame and
        // throws, disabling grabbing for the whole hand. So never create one.
        var sphere = new GameObject("Guide Marker");
        sphere.layer = 2; // Ignore Raycast
        sphere.transform.SetParent(transform, false);

        var filter = sphere.AddComponent<MeshFilter>();
        filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        m_Marker = sphere.AddComponent<MeshRenderer>();
        // The pulse rides on a per-renderer property block, so every marker can
        // share the one indicator material instead of cloning it.
        m_Marker.sharedMaterial = m_GlowMaterial;
        m_Marker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        m_Marker.receiveShadows = false;

        // Placed after the renderer exists so the placement matches the previous
        // primitive-based build exactly.
        sphere.transform.localPosition = ComputeMarkerLocalPosition();
        sphere.transform.localRotation = Quaternion.identity;
        sphere.transform.localScale = Vector3.one * m_SphereScale;
    }

    /// <summary>
    /// Floats the marker just above the top of the object's combined renderer
    /// bounds, then converts back into local space so it rides along whatever
    /// the object is parented to.
    /// </summary>
    Vector3 ComputeMarkerLocalPosition()
    {
        float lift = m_Margin + m_SphereScale * 0.5f;
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return Vector3.up * lift;

        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        Vector3 above = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) + Vector3.up * lift;
        return transform.InverseTransformPoint(above);
    }

    bool ShouldStop()
    {
        if (m_StopOnGrab && m_Grabbable != null && m_Grabbable.SelectingPointsCount > 0)
            return true;

        if (m_StopWhenFireHealthy && m_Campfire != null && m_Campfire.Fuel01 >= m_FireHealthy)
            return true;

        return false;
    }
}

using Oculus.Interaction;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The wordless prologue that replaces the old "shoot the board to start" gate.
///
/// The camp opens in late afternoon with a dead fire. The only cue is the log
/// pile: every log glows on its own surface (see <see cref="EmissionPulse"/>),
/// so the player is pulled to the natural first action - carry one log into the
/// fire - and nothing else blinks at the same time to overload them. The axe and
/// the bow are held back (hidden, not cued) until the night so nothing competes
/// with that beat. Picking a
/// log up raises a sphere on the campfire (see <see cref="GuideBlink"/>); the
/// two cues stay up together, even if the log is dropped, until the fire is
/// actually fed, so the beat reads as a two-step tutorial. As soon as
/// the fire is fed it swells back to life (see <see cref="CampfireFuel"/>'s
/// visual ramp), the dusk rushes the rest of the way to night, and the moon
/// opens red (see <see cref="NightEnvironmentController"/>); if the player never
/// touches a log the two-minute dusk simply finishes on its own.
///
/// Only once the night has fallen do the axe, the bow and their cues appear -
/// the axe as a surface highlight, the bow with its existing grip/string markers
/// - so the lesson stays one beat at a time. No text is used anywhere.
/// </summary>
[DisallowMultipleComponent]
public class PrologueController : MonoBehaviour
{
    [SerializeField] GameManager m_GameManager;
    [SerializeField] NightEnvironmentController m_Night;
    [SerializeField] CampfireFuel m_Campfire;

    [Header("Cues")]
    [Tooltip("Bow tutorial markers, held back until the game begins.")]
    [SerializeField] BowTutorial m_BowTutorial;

    [Header("Held back until night")]
    [Tooltip("Weapons and loose props that would distract during the prologue. They are " +
        "switched off at scene start and switched back on, exactly where they are, when " +
        "the night begins. Leave empty to auto-resolve the axe and the bow.")]
    [SerializeField] GameObject[] m_ObjectsHiddenUntilNight;

    [Header("Camp drop-in")]
    [Tooltip("When on, the logs tumble into the pile as the prologue opens.")]
    [SerializeField] bool m_DropInLogs = true;
    [Tooltip("How high above their resting spot the logs are released.")]
    [SerializeField] float m_LogDropHeight = 0.55f;

    bool m_CuesOver;
    bool m_NightStarted;
    EmissionPulse m_AxeHighlight;
    Grabbable m_AxeGrabbable;
    GuideBlink m_CampfireCue;
    GameObject m_CampfireCueRoot;
    readonly List<GameObject> m_NightObjects = new List<GameObject>();
    GameObject m_AxeObject;

    public void Inject(GameManager gameManager, NightEnvironmentController night, CampfireFuel campfire)
    {
        m_GameManager = gameManager;
        m_Night = night;
        m_Campfire = campfire;
    }

    IEnumerator Start()
    {
        ResolveReferences();

        // Only the logs are cued during the prologue. The bow's markers wait for
        // the night, and the axe's old floating sphere is stripped outright.
        if (m_BowTutorial != null)
            m_BowTutorial.SetCuesEnabled(false);

        // Everything that would pull the eye away from the log pile - the axe,
        // the bow and friends - stays off until the night and then appears right
        // where it always stood.
        CacheAndHideNightObjects();

        SetLogCues(true);

        if (m_Campfire != null)
            m_Campfire.OnFuelChanged.AddListener(HandleFuelChanged);

        if (m_DropInLogs)
            yield return DropInLogs();
    }

    void OnDestroy()
    {
        if (m_Campfire != null)
            m_Campfire.OnFuelChanged.RemoveListener(HandleFuelChanged);

        foreach (var log in FindObjectsByType<Log>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            log.OnGrabbed.RemoveListener(HandleLogGrabbed);
    }

    void Update()
    {
        if (!m_CuesOver)
        {
            // No fuel? The two-minute dusk finishing is the fallback that still
            // opens the night (and a scene that starts at night skips the cue).
            if (m_Night == null || !m_Night.StartedInAfternoon || m_Night.DuskComplete)
                EndCues();
            return;
        }

        if (!m_NightStarted)
            StartNightRunning();

        // The axe highlight has done its job once the axe is in hand.
        if (m_AxeHighlight != null && m_AxeGrabbable != null && m_AxeGrabbable.SelectingPointsCount > 0)
        {
            m_AxeHighlight.SetActive(false);
            m_AxeHighlight = null;
        }
    }

    /// <summary>
    /// Releases the logs a little above the pile, one after another, so the camp
    /// reads as "the firewood just landed" rather than a pile that was always
    /// there.
    /// </summary>
    IEnumerator DropInLogs()
    {
        yield return new WaitForSeconds(0.4f);

        foreach (var log in FindObjectsByType<Log>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var body = log.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic)
            {
                body.position += Vector3.up * m_LogDropHeight;
                body.angularVelocity = Random.insideUnitSphere * 3f;
            }

            yield return new WaitForSeconds(0.12f);
        }
    }

    void HandleFuelChanged()
    {
        EndCues();
    }

    /// <summary>
    /// The learn-to-play beat is over: the fire has been fed (or the dusk simply
    /// ran out). Clears the log cue and rushes what is left of the dusk; the
    /// night only arrives once the sky has actually reached night.
    /// </summary>
    void EndCues()
    {
        if (m_CuesOver)
            return;

        m_CuesOver = true;

        SetLogCues(false);
        ClearCampfireCue();

        if (m_Night != null)
            m_Night.BeginDuskRush();
    }

    /// <summary>
    /// Nightfall: hand the game over to the <see cref="GameManager"/> and only
    /// now bring in the axe, the bow and their cues.
    /// </summary>
    void StartNightRunning()
    {
        if (m_Night != null && !m_Night.DuskComplete)
            return;

        m_NightStarted = true;

        if (m_GameManager != null)
            m_GameManager.BeginNight();

        RevealNightObjects();
        ShowNightCues();
    }

    void ShowNightCues()
    {
        if (m_BowTutorial != null)
            m_BowTutorial.SetCuesEnabled(true);

        if (m_AxeHighlight == null && m_AxeObject != null)
        {
            var axe = m_AxeObject;
            StripGuideBlink(axe);
            m_AxeGrabbable = axe.GetComponent<Grabbable>();
            if (m_AxeGrabbable == null)
                m_AxeGrabbable = axe.GetComponentInChildren<Grabbable>(true);

            m_AxeHighlight = axe.GetComponent<EmissionPulse>();
            if (m_AxeHighlight == null)
                m_AxeHighlight = axe.AddComponent<EmissionPulse>();
        }

        if (m_AxeHighlight != null)
            m_AxeHighlight.SetActive(true);
    }

    /// <summary>
    /// Finds the weapons and props the prologue should not show yet, remembers
    /// them and hides them. The list is taken from the inspector when set;
    /// otherwise it auto-resolves every bow and every axe in the scene, so the
    /// prologue stays quiet without any scene wiring.
    /// </summary>
    void CacheAndHideNightObjects()
    {
        if (m_ObjectsHiddenUntilNight == null || m_ObjectsHiddenUntilNight.Length == 0)
            m_ObjectsHiddenUntilNight = ResolveNightObjects();

        foreach (var go in m_ObjectsHiddenUntilNight)
        {
            if (go == null || m_NightObjects.Contains(go))
                continue;

            if (m_AxeObject == null && go.name.StartsWith("Axe"))
            {
                m_AxeObject = go;
                StripGuideBlink(go);
            }

            m_NightObjects.Add(go);
            go.SetActive(false);
        }
    }

    /// <summary>Switches the held-back objects back on, where they are.</summary>
    void RevealNightObjects()
    {
        foreach (var go in m_NightObjects)
            if (go != null)
                go.SetActive(true);
    }

    static GameObject[] ResolveNightObjects()
    {
        var objects = new List<GameObject>();

        foreach (var bow in FindObjectsByType<Bow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            objects.Add(bow.gameObject);

        // Every axe is a banisher; filter by name so the loose "banishing
        // cylinder" prop, which is meant to stay out, is not swept up with them.
        foreach (var banisher in FindObjectsByType<EnemyBanisher>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (banisher.gameObject.name.StartsWith("Axe"))
                objects.Add(banisher.gameObject);

        return objects.ToArray();
    }

    /// <summary>Enables or clears the glow on every log currently in the camp.</summary>
    void SetLogCues(bool active)
    {
        foreach (var log in FindObjectsByType<Log>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            StripGuideBlink(log.gameObject);

            var pulse = log.GetComponent<EmissionPulse>();
            if (pulse == null)
                pulse = log.gameObject.AddComponent<EmissionPulse>();
            pulse.SetActive(active);

            // Taking a log raises the campfire's drop-off cue (HandleLogGrabbed).
            // Dropping it changes nothing, so the log glow and the campfire
            // sphere stay up together until the fire is actually fed.
            log.OnGrabbed.RemoveListener(HandleLogGrabbed);
            if (active)
                log.OnGrabbed.AddListener(HandleLogGrabbed);
        }
    }

    /// <summary>
    /// First log in hand: raise the campfire's drop-off sphere so the player
    /// knows where to carry it. It stays until the fire is fed, even if the log
    /// is dropped - that persistence is what makes the beat teach itself.
    /// </summary>
    void HandleLogGrabbed()
    {
        if (m_CuesOver || m_Campfire == null || m_CampfireCue != null)
            return;

        // The cue sits on its own anchor rather than on the campfire itself, so
        // tearing it down takes the marker sphere with it (destroying a
        // GuideBlink component alone leaves its "Guide Marker" child behind).
        var anchor = new GameObject("Campfire Cue");
        anchor.transform.SetParent(m_Campfire.transform, false);

        m_CampfireCueRoot = anchor;
        m_CampfireCue = anchor.AddComponent<GuideBlink>();
        m_CampfireCue.SetActive(true);
    }

    /// <summary>Removes the campfire's drop-off cue once the log is delivered.</summary>
    void ClearCampfireCue()
    {
        if (m_CampfireCueRoot == null)
            return;

        m_CampfireCue.SetActive(false);
        Destroy(m_CampfireCueRoot);

        m_CampfireCue = null;
        m_CampfireCueRoot = null;
    }

    /// <summary>
    /// Removes the old floating guide spheres from objects the prologue now cues
    /// with an emission pulse instead, so nothing is left blinking overhead. Any
    /// marker the component already built is destroyed with it (destroying the
    /// component alone leaves the sphere child behind).
    /// </summary>
    static void StripGuideBlink(GameObject go)
    {
        foreach (var guide in go.GetComponentsInChildren<GuideBlink>(true))
        {
            guide.enabled = false;
            Destroy(guide);
        }

        foreach (var child in go.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.name == "Guide Marker")
                Destroy(child.gameObject);
        }
    }

    void ResolveReferences()
    {
        // Include inactive so the bow (which can sit disabled until it is offered)
        // is still found for the night cues.
        if (m_GameManager == null)
            m_GameManager = FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (m_Night == null)
            m_Night = FindAnyObjectByType<NightEnvironmentController>(FindObjectsInactive.Include);
        if (m_Campfire == null)
            m_Campfire = FindAnyObjectByType<CampfireFuel>(FindObjectsInactive.Include);
        if (m_BowTutorial == null)
            m_BowTutorial = FindAnyObjectByType<BowTutorial>(FindObjectsInactive.Include);
    }
}

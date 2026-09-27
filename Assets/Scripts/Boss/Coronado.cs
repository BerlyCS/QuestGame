using System.Collections;
using Oculus.Interaction.Input;
using UnityEngine;

/// <summary>
/// The Coronado, the level's final boss (see JEFE_FINAL.md). The regla
/// central never changes across phases: it only moves while it is outside the
/// player's view cone, freezing and resuming in the same frame, no smoothing
/// either way. What changes by phase is HOW it moves and what a hit does:
///
/// - Fase 1 Acecho (15-8 m): 0.6 m/s, circles the clearing instead of walking
///   a straight line, slowly spiralling in. A slingshot hit pushes it back 2 m.
/// - Fase 2 Cerco (8-3 m): 1.2 m/s, same circling; if the player leaves it
///   unseen for 2 s straight it also teleports to a new angle (never closer
///   than 3 m, never in the wedge right in front of the player's face). A hit
///   pushes it back 1 m.
/// - Fase 3 Caza (&lt;3 m): 2.0 m/s, walks straight at the player - no more
///   circling, no time to aim the slingshot here (nothing blocks the shot in
///   code; the boss just keeps closing the whole time it takes to draw one,
///   which is the actual punishment). Its closeness burns the campfire twice
///   as fast. The only defense is shoving it with both open hands: a palm
///   moving faster than <see cref="m_PushMinHandSpeed"/> within
///   <see cref="m_PushContactRadius"/> of its centre launches it 4 m back,
///   past the Fase 3 boundary and into Fase 2. Detected from raw hand-tracking
///   palm velocity (see <see cref="SampleHandVelocity"/>), never from the
///   Interaction SDK's grab events - a shove isn't a grab.
/// - Si te alcanza a &lt;0.8 m: medio segundo de congelación, luego se abalanza
///   y llena el campo de visión en 0.2 s (nunca a menos de 0.3 m de la cámara,
///   y esta rutina jamás mueve la cámara del jugador), grito + golpe seco. El
///   golpe es no letal (PlayerHealth.TakeDamageNonLethal): duele muchísimo pero
///   nunca es lo que termina la partida, y acto seguido sale despedido de
///   vuelta a Fase 2. Ser alcanzado tiene que dar miedo, no reiniciar.
///
/// NO health: the distance IS the health bar (see JEFE_FINAL.md 5). Every hit
/// only pushes it away; nothing here ever reduces a hit-point counter.
///
/// Derrota: lo que sí se cuenta son los retrocesos. Cada vez que el jugador lo
/// hace retroceder (flecha que lo empuja en Fase 1/2, o manotazo en Fase 3)
/// suma uno; al llegar a <see cref="m_PushbacksToDefeat"/> se congela, se
/// abalanza hasta quedar muy cerca de la cara del jugador y ahí lo alcanza el
/// amanecer: GameManager fuerza la salida del sol y el sol lo desintegra en
/// ceniza (<see cref="DisintegrateBySun"/>). Ser alcanzado por él (el zarpazo)
/// no cuenta como retroceso.
///
/// The model is placeholder on purpose (see CLAUDE.md): a tall capsule with
/// two small emissive spheres for eyes, built by CoronadoBuilder.
/// </summary>
[DisallowMultipleComponent]
public class Coronado : MonoBehaviour, IArrowHittable
{
    enum Phase { Acecho, Cerco, Caza }

    [Header("Mirada")]
    [Tooltip("Ángulo total (no el medio-ángulo) del cono de visión medido desde la cámara. " +
             "Dentro de él y sin nada por medio, el jefe se congela.")]
    [SerializeField] float m_ViewConeAngle = 50f;

    [Tooltip("Altura del centro del jefe sobre su base (pivote), el punto que se compara " +
             "contra el cono de visión.")]
    [SerializeField] float m_CenterHeight = 1.1f;

    [Tooltip("Capas que cuentan como algo que tapa la línea de visión entre la cámara y el jefe.")]
    [SerializeField] LayerMask m_OcclusionMask = ~0;

    [Header("Fases por distancia (JEFE_FINAL.md 4)")]
    [Tooltip("Por debajo de esta distancia, Fase 2 (Cerco). Por encima, Fase 1 (Acecho).")]
    [SerializeField] float m_CercoDistance = 8f;

    [Tooltip("Por debajo de esta distancia, Fase 3 (Caza).")]
    [SerializeField] float m_CazaDistance = 3f;

    [Header("Velocidad por fase (m/s)")]
    [Tooltip("Multiplicador aplicado a las tres velocidades. Como solo se mueve cuando NO se le mira, " +
             "es la velocidad real de persecución: subirlo acelera todo el combate sin romper la regla de la mirada.")]
    [SerializeField] float m_UnseenSpeedMultiplier = 2f;
    [SerializeField] float m_AcechoSpeed = 0.6f;
    [SerializeField] float m_CercoSpeed = 1.2f;
    [SerializeField] float m_CazaSpeed = 2.0f;

    [Header("Fase 1/2 - órbita alrededor del claro")]
    [Tooltip("Fracción de la velocidad de fase dedicada a cerrar distancia; el resto es giro " +
             "alrededor del jugador. JEFE_FINAL.md no fija esta cifra, es un ajuste de sensación.")]
    [Range(0f, 1f)]
    [SerializeField] float m_OrbitInwardRatio = 0.3f;

    [Header("Fase 2 - reposicionamiento")]
    [Tooltip("Segundos seguidos sin ser visto (fuera del cono) antes de reaparecer en otra dirección.")]
    [SerializeField] float m_RepositionAfterUnseen = 2f;

    [Tooltip("Ángulo total, centrado en hacia-donde-mira-el-jugador, donde nunca reaparece " +
             "(\"nunca justo delante de la cara\").")]
    [SerializeField] float m_RepositionFrontExclusion = 100f;

    [Tooltip("Nunca reaparece a menos de esta distancia (el mínimo del diseño es 3 m; se deja margen).")]
    [SerializeField] float m_MinRepositionDistance = 3.5f;

    [Header("Empuje por impacto - la distancia ES la vida (JEFE_FINAL.md 5)")]
    [SerializeField] float m_AcechoPushback = 2f;
    [SerializeField] float m_CercoPushback = 1f;
    [Tooltip("Fase 3 todavía no tiene golpe cuerpo a cuerpo (el hacha se quitó del proyecto, " +
             "ver [[project_fogata_slingshot_only]]): un impacto aquí solo lo hace gritar, sin empuje.")]
    [SerializeField] float m_CazaPushback = 0f;

    [Header("Fase 3 - apaga la fogata al doble")]
    [SerializeField] CampfireFuel m_Campfire;
    [SerializeField] float m_CazaFireDrainMultiplier = 2f;

    [Header("Fase 3 - defensa: empujarlo con las manos (JEFE_FINAL.md 4)")]
    [Tooltip("Fuente de datos de la mano izquierda (hand tracking crudo, no un interactor de grab).")]
    [SerializeField] Hand m_LeftHand;

    [Tooltip("Fuente de datos de la mano derecha (hand tracking crudo, no un interactor de grab).")]
    [SerializeField] Hand m_RightHand;

    [Tooltip("La palma debe estar a esta distancia o menos de su centro para que el empujón cuente.")]
    [SerializeField] float m_PushContactRadius = 0.5f;

    [Tooltip("Velocidad mínima de la palma (m/s) para que cuente como empujón.")]
    [SerializeField] float m_PushMinHandSpeed = 1.5f;

    [Tooltip("Empuje del manotazo: JEFE_FINAL.md dice 4 m, justo lo suficiente para devolverlo a Fase 2.")]
    [SerializeField] float m_HandPushbackDistance = 4f;

    [Tooltip("Margen entre dos empujones seguidos, para no contar el mismo manotazo dos veces.")]
    [SerializeField] float m_PushCooldown = 0.4f;

    [Header("Feedback del empujón (sin hápticos: todo visual/sonoro)")]
    [SerializeField] float m_PushFlashDuration = 0.12f;
    [SerializeField] float m_LaunchDuration = 0.25f;
    [SerializeField] float m_LaunchHopHeight = 0.6f;

    [Header("Al alcanzarte: golpe no letal + empujón de vuelta")]
    [SerializeField] float m_CatchDistance = 0.8f;
    [Tooltip("Medio segundo de congelación antes del zarpazo. Es lo que hace que el susto " +
             "funcione - no acortar esto.")]
    [SerializeField] float m_CatchFreezeDuration = 0.5f;
    [Tooltip("Cuánto tarda en \"llenar el campo de visión\" tras la congelación.")]
    [SerializeField] float m_CatchLungeDuration = 0.2f;
    [Tooltip("LÍMITE DE SEGURIDAD: nunca a menos de esta distancia de la cámara.")]
    [SerializeField] float m_CatchMinCameraDistance = 0.3f;
    [SerializeField] float m_CatchLungeScale = 1.6f;
    [Tooltip("Daño del zarpazo. No letal: PlayerHealth lo aplica pero nunca deja al jugador a 0.")]
    [SerializeField] float m_CatchDamage = 90f;
    [Tooltip("GameObject con PlayerHealth; si se deja vacío se busca en la escena.")]
    [SerializeField] PlayerHealth m_PlayerHealth;

    [Header("Derrota: 3 retrocesos y el sol lo desintegra")]
    [Tooltip("Retrocesos (flecha que lo empuja o manotazo) necesarios para vencerlo.")]
    [SerializeField] int m_PushbacksToDefeat = 3;
    [Tooltip("GameManager al que se avisa de la victoria. Si se deja vacío se busca en la escena.")]
    [SerializeField] GameManager m_GameManager;
    [Tooltip("Congelación antes de la embestida final: el mismo medio segundo que hace funcionar el susto.")]
    [SerializeField] float m_FinalFreezeDuration = 0.6f;
    [Tooltip("Duración de la embestida final hacia la cara del jugador.")]
    [SerializeField] float m_FinalLungeDuration = 0.35f;
    [Tooltip("Distancia a la cámara donde se queda su centro tras la embestida final. " +
             "Muy cerca, pero nunca por debajo de m_CatchMinCameraDistance.")]
    [SerializeField] float m_FinalLungeDistance = 0.8f;
    [Tooltip("Cuánto tarda en deshacerse en ceniza cuando le da el sol.")]
    [SerializeField] float m_DisintegrateDuration = 2.8f;
    [SerializeField] Color m_BurnColor = new Color(1f, 0.55f, 0.15f);
    [SerializeField] Color m_BurnHotColor = new Color(1f, 0.95f, 0.8f);

    [Header("Referencias")]
    [Tooltip("Cámara del jugador (CenterEyeAnchor). Si se deja vacío, usa Camera.main.")]
    [SerializeField] Transform m_PlayerCamera;

    [Tooltip("AudioSource 3D de la respiración (canal 1, JEFE_FINAL.md 6.1). Construido por CoronadoBuilder.")]
    [SerializeField] AudioSource m_BreathAudio;

    [Tooltip("Respiración del fantasma (Assets/Audio/ghostbreath.mp3), en bucle y espacializada: dice por " +
             "dónde anda - delante, detrás, lejos, cerca. Vacío = la respiración procedural.")]
    [SerializeField] AudioClip m_BreathClip;

    bool m_Frozen;
    float m_UnseenTimer;
    float m_OrbitDirection = 1f;

    bool m_Launching;
    float m_NextPushAllowedTime;
    float m_FlashUntil;
    Vector3? m_LeftPalmLastPosition;
    Vector3? m_RightPalmLastPosition;
    Renderer[] m_Renderers;
    Color[] m_BaseColors;

    bool m_Caught;
    int m_Pushbacks;
    bool m_Defeated;
    bool m_Disintegrating;

    /// <summary>True mientras el jugador lo tiene dentro del cono y nada lo tapa.</summary>
    public bool IsFrozen => m_Frozen;

    /// <summary>True desde el tercer retroceso: ya no persigue, solo le queda la embestida final y el sol.</summary>
    public bool IsDefeated => m_Defeated;

    /// <summary>El punto de este jefe que cuenta para el cono de visión.</summary>
    Vector3 Center => transform.position + Vector3.up * m_CenterHeight;

    void Start()
    {
        if (m_PlayerCamera == null && Camera.main != null)
            m_PlayerCamera = Camera.main.transform;
        if (m_Campfire == null)
            m_Campfire = Object.FindAnyObjectByType<CampfireFuel>();
        if (m_PlayerHealth == null)
            m_PlayerHealth = Object.FindAnyObjectByType<PlayerHealth>();
        if (m_GameManager == null)
            m_GameManager = Object.FindAnyObjectByType<GameManager>();

        m_OrbitDirection = Random.value < 0.5f ? 1f : -1f;

        // El clip procedural no sobrevive guardar/recargar la escena (por eso
        // CoronadoBuilder no lo asigna): se crea aquí, igual que el crepitar
        // de CampfireFuel.Awake(). Empieza a sonar en cuanto el jefe aparece.
        if (m_BreathAudio != null)
        {
            m_BreathAudio.clip = m_BreathClip != null ? m_BreathClip : ProceduralSfx.CoronadoBreath;
            m_BreathAudio.Play();
        }

        CacheRenderers();

        // Its approach is announced by flares in the hood and a growl that
        // come faster and stronger as it closes in (see EnemyTell).
        if (!TryGetComponent(out EnemyTell tell))
            tell = gameObject.AddComponent<EnemyTell>();
        tell.ConfigureForBoss(m_CenterHeight + 0.85f, 15f, m_CazaDistance);
    }

    /// <summary>Para el destello blanco del empujón: guarda el color base de cada renderer (cuerpo y ojos).</summary>
    void CacheRenderers()
    {
        m_Renderers = GetComponentsInChildren<Renderer>();
        m_BaseColors = new Color[m_Renderers.Length];
        for (int i = 0; i < m_Renderers.Length; i++)
            m_BaseColors[i] = m_Renderers[i].material.color;
    }

    void Update()
    {
        // Ya lo tiene. La secuencia del zarpazo manda a partir de aquí; no hay
        // nada más que hacer hasta que el propio zarpazo lo devuelva a Fase 2.
        // Vencido, manda la secuencia final hasta que el sol lo desintegre.
        if (m_Caught || m_Defeated)
            return;

        UpdateFlash();

        // Palm velocity has to be sampled every frame regardless of anything
        // else below, so the "last position" cache never goes stale and spikes
        // into a bogus reading the moment it is next needed.
        Vector3 leftVelocity = SampleHandVelocity(m_LeftHand, ref m_LeftPalmLastPosition, out Vector3 leftPos, out bool leftValid);
        Vector3 rightVelocity = SampleHandVelocity(m_RightHand, ref m_RightPalmLastPosition, out Vector3 rightPos, out bool rightValid);

        // Mid-launch: the shove already answered for this frame, everything
        // else (gaze, movement, campfire effects) is suspended until it lands.
        if (m_Launching)
            return;

        if (m_PlayerCamera == null)
            return;

        // Sin suavizado: la congelación y la reanudación son el mismo booleano
        // leído cada fotograma, nunca un valor que se acerca a él con el tiempo.
        // Esta regla es universal: da igual la fase, mirarlo siempre lo congela.
        m_Frozen = IsInsideViewCone();

        Phase phase = GetPhase(FlatDistanceToPlayer());
        UpdateCampfireEffects(phase);

        if (phase != Phase.Cerco || m_Frozen)
            m_UnseenTimer = 0f;

        // The push works regardless of whether the player is looking at it -
        // there is no time to freeze it and then shove it, it has to be one
        // motion. Only live in Fase 3, and only once the previous shove has
        // finished its cooldown.
        if (phase == Phase.Caza && Time.time >= m_NextPushAllowedTime)
        {
            bool leftPushes = leftValid && IsPush(leftPos, leftVelocity);
            bool rightPushes = rightValid && IsPush(rightPos, rightVelocity);
            if (leftPushes || rightPushes)
            {
                ApplyHandPush();
                return;
            }
        }

        // JEFE_FINAL.md 7.1: te alcanza. Se comprueba después del empujón a
        // propósito - un manotazo a tiempo, en el mismo fotograma, todavía
        // salva la partida en vez de perderla por medio metro.
        if (FlatDistanceToPlayer() <= m_CatchDistance)
        {
            TriggerCatch();
            return;
        }

        if (m_Frozen)
            return;

        switch (phase)
        {
            case Phase.Acecho:
                OrbitAndCloseIn(m_AcechoSpeed * m_UnseenSpeedMultiplier);
                break;

            case Phase.Cerco:
                m_UnseenTimer += Time.deltaTime;
                if (m_UnseenTimer >= m_RepositionAfterUnseen)
                    Reposition();
                else
                    OrbitAndCloseIn(m_CercoSpeed * m_UnseenSpeedMultiplier);
                break;

            case Phase.Caza:
                AdvanceTowardPlayer(m_CazaSpeed * m_UnseenSpeedMultiplier);
                break;
        }
    }

    /// <summary>Blanco cegador de golpe, sin desvanecido: mismo lenguaje "corte en seco" que el resto del jefe.</summary>
    void UpdateFlash()
    {
        if (m_Renderers == null)
            return;

        bool flashing = Time.time < m_FlashUntil;
        for (int i = 0; i < m_Renderers.Length; i++)
        {
            if (m_Renderers[i] != null)
                m_Renderers[i].material.color = flashing ? Color.white : m_BaseColors[i];
        }
    }

    /// <summary>
    /// Velocidad de la palma (aprox. la muñeca - el SDK no expone una
    /// articulación de palma separada) por diferencia finita entre fotogramas.
    /// Se apoya en <see cref="IHand"/> directamente, nunca en un
    /// HandGrabInteractor: un manotazo no es un agarre.
    /// </summary>
    Vector3 SampleHandVelocity(Hand hand, ref Vector3? lastPosition, out Vector3 currentPosition, out bool valid)
    {
        currentPosition = Vector3.zero;
        valid = false;

        if (hand == null || !hand.GetJointPose(HandJointId.HandWristRoot, out Pose pose))
        {
            lastPosition = null;
            return Vector3.zero;
        }

        currentPosition = pose.position;
        valid = true;

        if (!lastPosition.HasValue || Time.deltaTime <= 0f)
        {
            lastPosition = currentPosition;
            return Vector3.zero;
        }

        Vector3 velocity = (currentPosition - lastPosition.Value) / Time.deltaTime;
        lastPosition = currentPosition;
        return velocity;
    }

    /// <summary>Palma rápida (JEFE_FINAL.md: &gt; 1.5 m/s) y a tocarlo (radio de contacto alrededor de su centro).</summary>
    bool IsPush(Vector3 palmPosition, Vector3 palmVelocity)
    {
        if (palmVelocity.magnitude < m_PushMinHandSpeed)
            return false;

        return Vector3.Distance(palmPosition, Center) <= m_PushContactRadius;
    }

    /// <summary>Destello blanco + golpe seco + vuelo exagerado de 4 m - todo el feedback obligatorio sin hápticos.</summary>
    void ApplyHandPush()
    {
        m_NextPushAllowedTime = Time.time + m_PushCooldown;
        m_FlashUntil = Time.time + m_PushFlashDuration;
        ProceduralSfx.PlayAt(ProceduralSfx.EnemyHit, Center);
        if (CountPushback())
            return;
        StartCoroutine(LaunchBackRoutine(ComputePushedPosition(m_HandPushbackDistance)));
    }

    /// <summary>
    /// Suma un retroceso y, si era el último, arranca la secuencia final en vez
    /// del empuje normal. Devuelve true cuando ha empezado esa secuencia.
    /// </summary>
    bool CountPushback()
    {
        if (m_Defeated)
            return true;

        m_Pushbacks++;
        if (m_Pushbacks < m_PushbacksToDefeat)
            return false;

        m_Defeated = true;
        StopAllCoroutines();
        m_Launching = false;
        m_Caught = false;
        StartCoroutine(FinalLungeSequence());
        return true;
    }

    /// <summary>
    /// El tercer retroceso: se congela, grita y se abalanza hasta quedar a
    /// <see cref="m_FinalLungeDistance"/> de la cara del jugador (con el mismo
    /// límite de seguridad del zarpazo: nunca más cerca de
    /// <see cref="m_CatchMinCameraDistance"/>, y nunca mueve la cámara). No hace
    /// daño. Justo ahí amanece: GameManager saca el sol y llama a
    /// <see cref="DisintegrateBySun"/>.
    /// </summary>
    IEnumerator FinalLungeSequence()
    {
        m_Frozen = true;
        if (m_Campfire != null)
            m_Campfire.SetProximityDrainMultiplier(1f);

        yield return new WaitForSeconds(m_FinalFreezeDuration);

        ProceduralSfx.PlayAt(ProceduralSfx.CoronadoScream, Center, 1f, 0.8f);

        Vector3 startPosition = transform.position;
        Vector3 targetPosition = FinalLungeTargetPosition();
        Quaternion startRotation = transform.rotation;
        Quaternion targetRotation = startRotation;
        if (m_PlayerCamera != null)
        {
            Vector3 towardPlayer = Vector3.ProjectOnPlane(m_PlayerCamera.position - targetPosition, Vector3.up);
            if (towardPlayer.sqrMagnitude > 0.0001f)
                targetRotation = Quaternion.LookRotation(towardPlayer.normalized, Vector3.up);
        }

        float t = 0f;
        while (t < m_FinalLungeDuration)
        {
            t += Time.deltaTime;
            float eased = Mathf.Clamp01(t / m_FinalLungeDuration);
            eased *= eased; // arranca despacio, embiste de golpe al final
            transform.position = Vector3.Lerp(startPosition, targetPosition, eased);
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, eased);
            yield return null;
        }

        transform.position = targetPosition;
        transform.rotation = targetRotation;

        if (m_GameManager != null)
            m_GameManager.WinByBossDefeat();
        else
            DisintegrateBySun();
    }

    /// <summary>Delante de la cara, en la dirección a la que mira el jugador, a la distancia final (con el tope de seguridad).</summary>
    Vector3 FinalLungeTargetPosition()
    {
        if (m_PlayerCamera == null)
            return transform.position;

        Vector3 forward = Vector3.ProjectOnPlane(m_PlayerCamera.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        float distance = Mathf.Max(m_FinalLungeDistance, m_CatchMinCameraDistance);
        Vector3 centerPosition = m_PlayerCamera.position + forward * distance;
        return centerPosition - Vector3.up * m_CenterHeight;
    }

    /// <summary>
    /// El sol lo alcanza: se pone al rojo, luego al blanco, se encoge y sube
    /// deshaciéndose en ceniza y brasas mientras chisporrotea, y desaparece.
    /// Lo llama GameManager en cualquier victoria mientras el jefe esté en
    /// escena (derrotado o no). Idempotente.
    /// </summary>
    public void DisintegrateBySun()
    {
        if (m_Disintegrating || !gameObject.activeInHierarchy)
            return;

        m_Disintegrating = true;
        m_Defeated = true;
        StopAllCoroutines();
        StartCoroutine(DisintegrateRoutine());
    }

    IEnumerator DisintegrateRoutine()
    {
        m_Frozen = true;
        m_Launching = false;
        m_Caught = false;

        if (m_BreathAudio != null)
            m_BreathAudio.Stop();
        if (m_Campfire != null)
        {
            m_Campfire.SetProximityDrainMultiplier(1f);
            m_Campfire.SetThreatPosition(null);
        }

        ProceduralSfx.PlayAt(ProceduralSfx.CoronadoScream, Center, 1f, 0.7f);
        ProceduralSfx.PlayAt(ProceduralSfx.SunBurn, Center);

        // La esfera de oscuridad se va con él de golpe: al sol no le queda nada que absorber.
        var absorb = transform.Find("Light Absorb");
        if (absorb != null)
            absorb.gameObject.SetActive(false);

        var ash = BuildAshParticles();
        var lights = GetComponentsInChildren<Light>();
        float[] lightIntensities = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
            lightIntensities[i] = lights[i].intensity;

        if (m_Renderers == null)
            CacheRenderers();
        foreach (var r in m_Renderers)
        {
            if (r != null && r.material.HasProperty("_EmissionColor"))
            {
                // The ghost's emission map only lights its face; drop it so the
                // whole body glows as it burns.
                r.material.EnableKeyword("_EMISSION");
                if (r.material.HasProperty("_EmissionMap"))
                    r.material.SetTexture("_EmissionMap", null);
            }
        }

        Vector3 startPosition = transform.position;
        Vector3 startScale = transform.localScale;

        float t = 0f;
        while (t < m_DisintegrateDuration)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / m_DisintegrateDuration);

            // Primero arde (naranja), después se vuelve blanco y se deshace.
            Color glow = n < 0.5f
                ? Color.Lerp(Color.black, m_BurnColor * 3f, n / 0.5f)
                : Color.Lerp(m_BurnColor * 3f, m_BurnHotColor * 5f, (n - 0.5f) / 0.5f);
            for (int i = 0; i < m_Renderers.Length; i++)
            {
                var r = m_Renderers[i];
                if (r == null)
                    continue;
                if (r.material.HasProperty("_EmissionColor"))
                    r.material.SetColor("_EmissionColor", glow);
                r.material.color = Color.Lerp(m_BaseColors[i], m_BurnHotColor, n);
            }

            // Tiembla, se encoge y sube como si el calor se lo llevara.
            float shrink = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, n));
            Vector3 jitter = Random.insideUnitSphere * 0.03f * (1f - n);
            transform.position = startPosition + Vector3.up * (0.6f * n * n) + jitter;
            transform.localScale = startScale * Mathf.Max(0.001f, shrink);

            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null)
                    lights[i].intensity = lightIntensities[i] * (1f - n);

            if (ash != null)
                ash.transform.position = startPosition + Vector3.up * m_CenterHeight;

            yield return null;
        }

        if (ash != null)
        {
            var emission = ash.emission;
            emission.rateOverTime = 0f;
            ash.Emit(120);
            ash.transform.SetParent(null, true);
            ash.gameObject.AddComponent<AutoDestroyAfter>().Lifetime = 3f;
        }

        gameObject.SetActive(false);
    }

    /// <summary>Ceniza gris y brasas naranjas que suben con el calor: el cuerpo deshaciéndose, sin hápticos, a la vista.</summary>
    ParticleSystem BuildAshParticles()
    {
        var go = new GameObject("Coronado Ash");
        go.transform.position = Center;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = m_DisintegrateDuration;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.09f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.25f, 0.23f, 0.22f, 1f),
            new Color(1f, 0.55f, 0.15f, 1f));
        main.gravityModifier = -0.25f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 600;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 160f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.45f;

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.4f;
        noise.frequency = 0.8f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.3f, 0.3f, 0.3f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        if (shader != null)
            renderer.sharedMaterial = new Material(shader) { name = "M_CoronadoAsh" };

        ps.Play();
        return ps;
    }

    /// <summary>
    /// Vuelo exagerado hacia atrás: un salto corto con voltereta, no un simple
    /// salto de posición - "sale despedido", no "retrocede". Congela la IA
    /// normal mientras dura (<see cref="m_Launching"/>) y aterriza mirando de
    /// nuevo al jugador. 4 m desde &lt; 3 m siempre cae en la banda de Fase 2
    /// (3-8 m), así que "lo devuelve a Fase 2" no necesita lógica aparte.
    /// </summary>
    IEnumerator LaunchBackRoutine(Vector3 targetPosition)
    {
        m_Launching = true;
        m_Frozen = true;

        Vector3 startPosition = transform.position;
        Quaternion startRotation = transform.rotation;
        Quaternion tumble = startRotation * Quaternion.Euler(-360f, 0f, 0f);

        float t = 0f;
        while (t < m_LaunchDuration)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / m_LaunchDuration);
            float eased = 1f - (1f - n) * (1f - n);

            Vector3 position = Vector3.Lerp(startPosition, targetPosition, eased);
            position.y += Mathf.Sin(eased * Mathf.PI) * m_LaunchHopHeight;
            transform.position = position;
            transform.rotation = Quaternion.Slerp(startRotation, tumble, eased);

            yield return null;
        }

        transform.position = targetPosition;

        if (m_PlayerCamera != null)
        {
            Vector3 towardPlayer = Vector3.ProjectOnPlane(m_PlayerCamera.position - targetPosition, Vector3.up);
            if (towardPlayer.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(towardPlayer.normalized, Vector3.up);
        }

        m_Launching = false;
    }

    void TriggerCatch()
    {
        if (m_Caught)
            return;
        m_Caught = true;
        StartCoroutine(CatchSequence());
    }

    /// <summary>
    /// Te alcanza (JEFE_FINAL.md 7.1, reconvertido en golpe en vez de derrota):
    /// 1) medio segundo de congelación - el susto sigue;
    /// 2) se abalanza y llena el campo de visión en 0.2 s;
    /// 3) grito agudo + golpe seco;
    /// 4) el golpe entra por <see cref="PlayerHealth.TakeDamageNonLethal"/>:
    ///    duele muchísimo pero nunca deja al jugador a cero;
    /// 5) sale despedido de vuelta a Fase 2 con el mismo vuelo del manotazo, así
    ///    que la partida continúa y hay que volver a ganarse la distancia.
    ///
    /// Límites de seguridad NO NEGOCIABLES: el modelo nunca queda a menos de
    /// <see cref="m_CatchMinCameraDistance"/> de la cámara, el salto entero
    /// dura <see cref="m_CatchLungeDuration"/> (0.2 s, muy por debajo del
    /// tope de 0.3 s en pantalla), y esta rutina jamás toca la rotación ni la
    /// posición de la cámara del jugador - solo mueve al propio jefe.
    /// </summary>
    IEnumerator CatchSequence()
    {
        m_Launching = true;
        m_Frozen = true;

        yield return new WaitForSeconds(m_CatchFreezeDuration);

        ProceduralSfx.PlayAt(ProceduralSfx.CoronadoScream, Center); // grito agudo
        ProceduralSfx.PlayAt(ProceduralSfx.EnemyHit, Center); // golpe seco

        // La misma risa siniestra de cuando te matan los esqueletos: que te
        // alcance tiene que sonar a perder, aunque el golpe no sea letal.
        if (m_GameManager != null)
            m_GameManager.PlayLaugh();

        if (m_PlayerHealth != null)
        {
            Vector3 source = m_PlayerCamera != null ? m_PlayerCamera.position : transform.position;
            m_PlayerHealth.TakeDamageNonLethal(m_CatchDamage, source);
        }

        Vector3 startPosition = transform.position;
        Vector3 startScale = transform.localScale;
        Vector3 targetPosition = LungeTargetPosition();
        Vector3 targetScale = startScale * m_CatchLungeScale;

        float t = 0f;
        while (t < m_CatchLungeDuration)
        {
            t += Time.deltaTime;
            float eased = Mathf.Clamp01(t / m_CatchLungeDuration);
            eased *= eased; // arranca despacio, embiste de golpe al final

            transform.position = Vector3.Lerp(startPosition, targetPosition, eased);
            transform.localScale = Vector3.Lerp(startScale, targetScale, eased);
            yield return null;
        }

        transform.position = targetPosition;
        transform.localScale = targetScale;

        // De vuelta a Fase 2: el mismo vuelo exagerado del manotazo, así el
        // zarpazo se lee como "te dio Y te lo quitaste de encima". Se rearma
        // m_Caught para que la persecución siga.
        transform.localScale = startScale;
        m_Caught = false;
        m_Launching = false;
        StartCoroutine(LaunchBackRoutine(ComputePushedPosition(m_HandPushbackDistance)));
    }

    /// <summary>
    /// Dónde queda su centro para "llenar el campo de visión": justo delante
    /// de la cámara, en la dirección exacta a la que mira en ese instante, al
    /// límite de seguridad de <see cref="m_CatchMinCameraDistance"/>. Nunca
    /// más cerca que eso.
    /// </summary>
    Vector3 LungeTargetPosition()
    {
        if (m_PlayerCamera == null)
            return transform.position;

        Vector3 centerPosition = m_PlayerCamera.position + m_PlayerCamera.forward * m_CatchMinCameraDistance;
        return centerPosition - Vector3.up * m_CenterHeight;
    }

    Phase GetPhase(float flatDistance)
    {
        if (flatDistance >= m_CercoDistance)
            return Phase.Acecho;
        if (flatDistance >= m_CazaDistance)
            return Phase.Cerco;
        return Phase.Caza;
    }

    /// <summary>
    /// Congelado si su centro cae dentro del cono de visión de la cámara y no
    /// hay nada sólido entre medias (ver JEFE_FINAL.md 2.2). Nada de raycast a
    /// un hueso concreto: si hay que adivinar si se le está mirando, se siente
    /// injusto.
    /// </summary>
    bool IsInsideViewCone()
    {
        Vector3 toSelf = Center - m_PlayerCamera.position;
        if (toSelf.sqrMagnitude <= 0.0001f)
            return true;

        float angle = Vector3.Angle(m_PlayerCamera.forward, toSelf);
        if (angle > m_ViewConeAngle * 0.5f)
            return false;

        return !IsOccluded(toSelf);
    }

    /// <summary>Cualquier collider que no sea del propio jefe entre la cámara y su centro cuenta como tapado.</summary>
    bool IsOccluded(Vector3 toSelf)
    {
        float distance = toSelf.magnitude;
        bool hitSomething = Physics.Raycast(m_PlayerCamera.position, toSelf.normalized,
            out RaycastHit hit, distance, m_OcclusionMask, QueryTriggerInteraction.Ignore);

        return hitSomething && !hit.transform.IsChildOf(transform);
    }

    /// <summary>
    /// Fases 1 y 2: no en línea recta (JEFE_FINAL.md 4). Gira alrededor del
    /// jugador a un radio que se va cerrando poco a poco, en vez de caminar
    /// directo hacia él - así aparece "a tu izquierda, luego a tu espalda,
    /// luego al frente" según da la vuelta.
    /// </summary>
    void OrbitAndCloseIn(float speed)
    {
        Vector3 flatSelf = FlatPosition(transform.position);
        Vector3 flatPlayer = FlatPosition(m_PlayerCamera.position);
        Vector3 outward = flatSelf - flatPlayer;
        float radius = outward.magnitude;

        if (radius < 0.05f)
        {
            AdvanceTowardPlayer(speed);
            return;
        }

        outward /= radius;

        // The tangential step is a rotation of the outward vector around the
        // player (angular speed = tangentSpeed / radius) rather than an
        // explicit sideways vector - it stays exact at any radius as the
        // spiral closes in, instead of drifting off a fixed-length tangent.
        float inwardSpeed = speed * m_OrbitInwardRatio;
        float tangentSpeed = speed * (1f - m_OrbitInwardRatio);

        float newRadius = Mathf.Max(0.1f, radius - inwardSpeed * Time.deltaTime);
        float angleStep = (tangentSpeed / radius) * Mathf.Rad2Deg * Time.deltaTime * m_OrbitDirection;

        Vector3 newOutward = Quaternion.AngleAxis(angleStep, Vector3.up) * outward;
        Vector3 newFlatSelf = flatPlayer + newOutward * newRadius;

        transform.position = new Vector3(newFlatSelf.x, transform.position.y, newFlatSelf.z);

        // Face the player while it circles, not the direction it is walking:
        // it is stalking, not wandering.
        transform.rotation = Quaternion.LookRotation(-newOutward, Vector3.up);
    }

    /// <summary>Fase 3: línea recta hacia el jugador, sin esquivar obstáculos - ya está encima.</summary>
    void AdvanceTowardPlayer(float speed)
    {
        Vector3 toPlayer = m_PlayerCamera.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude <= 0.0001f)
            return;

        Vector3 direction = toPlayer.normalized;
        transform.position += direction * (speed * Time.deltaTime);
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    /// <summary>
    /// Fase 2, tras <see cref="m_RepositionAfterUnseen"/> segundos sin ser
    /// visto: reaparece en otra dirección, nunca más cerca que
    /// <see cref="m_MinRepositionDistance"/> ni en el ángulo frontal que
    /// cubre <see cref="m_RepositionFrontExclusion"/>. Mantiene la distancia
    /// que ya tenía (solo cambia de lado), así que sigue en Fase 2.
    /// </summary>
    void Reposition()
    {
        Vector3 cameraForwardFlat = Vector3.ProjectOnPlane(m_PlayerCamera.forward, Vector3.up).normalized;
        if (cameraForwardFlat.sqrMagnitude < 0.0001f)
            cameraForwardFlat = Vector3.forward;

        float half = m_RepositionFrontExclusion * 0.5f;
        float angle = Random.Range(half, 360f - half);
        Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * cameraForwardFlat;

        float distance = Mathf.Clamp(FlatDistanceToPlayer(), m_MinRepositionDistance, m_CercoDistance);

        Vector3 flatPlayer = FlatPosition(m_PlayerCamera.position);
        Vector3 newFlat = flatPlayer + direction * distance;
        transform.position = new Vector3(newFlat.x, transform.position.y, newFlat.z);
        transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);

        // Alternate orbit direction from the new spot so it does not just
        // keep sweeping the same arc every time it disappears and reappears.
        m_OrbitDirection = Random.value < 0.5f ? 1f : -1f;

        m_UnseenTimer = 0f;
    }

    /// <summary>
    /// Fase 3: su cercanía apaga la fogata al doble de rápido (JEFE_FINAL.md 4).
    /// Además, mientras exista, la llama se inclina apartándose de él en
    /// cualquier fase - el canal 2 de localización (JEFE_FINAL.md 6).
    /// </summary>
    void UpdateCampfireEffects(Phase phase)
    {
        if (m_Campfire == null)
            return;

        m_Campfire.SetProximityDrainMultiplier(phase == Phase.Caza ? m_CazaFireDrainMultiplier : 1f);
        m_Campfire.SetThreatPosition(transform.position);
    }

    /// <summary>A landed ember counts as one impact (see <see cref="IArrowHittable"/>). No puntos de vida.</summary>
    public void Hit(Arrow arrow)
    {
        if (m_Defeated)
            return;
        TakeImpact();
    }

    /// <summary>Grita y retrocede visiblemente; el empuje depende de la fase actual.</summary>
    void TakeImpact()
    {
        Phase phase = GetPhase(FlatDistanceToPlayer());
        float pushback = phase switch
        {
            Phase.Acecho => m_AcechoPushback,
            Phase.Cerco => m_CercoPushback,
            _ => m_CazaPushback,
        };

        ProceduralSfx.PlayAt(ProceduralSfx.CoronadoScream, Center);
        if (pushback > 0f && CountPushback())
            return;
        PushBack(pushback);
    }

    /// <summary>Empuje instantáneo, sin suavizado, directamente lejos del jugador - el mismo lenguaje visual que la congelación.</summary>
    void PushBack(float distance)
    {
        if (distance <= 0f)
            return;

        transform.position = ComputePushedPosition(distance);
    }

    /// <summary>Dónde caería si se le empujara <paramref name="distance"/> m en línea recta, lejos de la cámara.</summary>
    Vector3 ComputePushedPosition(float distance)
    {
        Vector3 flatSelf = FlatPosition(transform.position);
        Vector3 flatPlayer = m_PlayerCamera != null ? FlatPosition(m_PlayerCamera.position) : flatSelf - FlatPosition(transform.forward);

        Vector3 away = flatSelf - flatPlayer;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;

        Vector3 newFlat = flatSelf + away * distance;
        return new Vector3(newFlat.x, transform.position.y, newFlat.z);
    }

    float FlatDistanceToPlayer()
    {
        if (m_PlayerCamera == null)
            return float.PositiveInfinity;
        return Vector3.Distance(FlatPosition(transform.position), FlatPosition(m_PlayerCamera.position));
    }

    static Vector3 FlatPosition(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>
    /// The real entrance (see JEFE_FINAL.md 3 and BossIntro): places the
    /// Coronado at <paramref name="position"/> (ground level), faces it toward
    /// the player and activates it - this is the moment its eyes ignite among
    /// the trees.
    /// </summary>
    public void Appear(Vector3 position)
    {
        position.y = 0f;
        transform.position = position;
        m_UnseenTimer = 0f;
        m_OrbitDirection = Random.value < 0.5f ? 1f : -1f;

        if (m_PlayerCamera == null && Camera.main != null)
            m_PlayerCamera = Camera.main.transform;

        if (m_PlayerCamera != null)
        {
            Vector3 towardPlayer = Vector3.ProjectOnPlane(m_PlayerCamera.position - position, Vector3.up);
            if (towardPlayer.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(towardPlayer.normalized, Vector3.up);
        }

        gameObject.SetActive(true);
    }

    /// <summary>
    /// Debug-only (tecla 'K'): cuenta un retroceso como si lo hubiera empujado
    /// una flecha. Si todavía no ha salido, lo invoca a 6 m primero.
    /// </summary>
    public void DebugPushback()
    {
        if (m_Defeated)
            return;
        if (!gameObject.activeInHierarchy)
            DebugSummon(6f);
        if (!CountPushback())
            PushBack(m_CercoPushback);
    }

    /// <summary>
    /// Debug-only: lo coloca a <paramref name="distance"/> m del jugador, mirándolo,
    /// y lo activa (ver DebugKeys, tecla 'B').
    /// </summary>
    public void DebugSummon(float distance)
    {
        if (m_PlayerCamera == null && Camera.main != null)
            m_PlayerCamera = Camera.main.transform;
        if (m_PlayerCamera == null)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(m_PlayerCamera.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;

        Vector3 flatCamera = new Vector3(m_PlayerCamera.position.x, 0f, m_PlayerCamera.position.z);
        Appear(flatCamera + forward * distance);
    }
}

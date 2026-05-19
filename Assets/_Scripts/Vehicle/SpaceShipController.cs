// ============================================================
//  SpaceShipController.cs
//  Vol 6DOF : tangage/lacet/roulis souris + propulsion complète.
//  Détecte l'OzoneLayer pour basculer entre physique spatiale
//  (inertie pure, trainée nulle) et atmosphérique (gravité + trainée).
//
//  Contrôles :
//    W / Z       — Poussée avant
//    S           — Frein / poussée arrière
//    A           — Roulis gauche (strafe avec Q pour ZQSD)
//    D           — Roulis droite
//    Q           — Roulis gauche
//    E           — Roulis droite
//    A           — Dérive gauche
//    D           — Dérive droite
//    ← / →       — Lacet gauche / droite
//    ↑ / ↓       — Tangage haut / bas
//    Espace      — Poussée haut (local)
//    Ctrl gauche — Poussée bas (local)
//    Shift gauche— Boost (× boostMultiplier)
//    Souris X/Y  — Lacet / Tangage
//    F           — Embarquer / Débarquer
// ============================================================

using UnityEngine;
using AstroVoxel.Physics;
using AstroVoxel.VoxelEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AstroVoxel.Vehicle
{
    /// <summary>
    /// Contrôleur de vaisseau spatial 6DOF.
    /// Ne dépend PAS de <see cref="GravityBody"/> : la gravité planétaire
    /// et l'alignement d'orientation sont gérés manuellement ici,
    /// ce qui permet de préserver l'assiette libre du vaisseau.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SpaceShipController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────

        [Header("Propulsion")]
        [Tooltip("Accélération de poussée principale en m/s² (avant / arrière). Doit dépasser la gravité planétaire ~9.81.")]
        [SerializeField] private float mainThrust      = 35f;

        [Tooltip("Accélération de dérive et poussée verticale en m/s².")]
        [SerializeField] private float lateralThrust   = 25f;

        [Tooltip("Multiplicateur boost (Shift).")]
        [SerializeField] private float boostMultiplier = 3f;

        [Tooltip("Vitesse maximale autorisée (unités/s).")]
        [SerializeField] private float maxSpeed        = 200f;

        [Header("Rotation")]
        [Tooltip("Vitesse de tangage (deg/s · delta souris).")]
        [SerializeField] private float pitchSpeed      = 120f;

        [Tooltip("Vitesse de lacet (deg/s · delta souris).")]
        [SerializeField] private float yawSpeed        = 120f;

        [Tooltip("Vitesse de roulis (deg/s, touches Q / E).")]
        [SerializeField] private float rollSpeed       = 80f;

        [Tooltip("Sensibilité souris.")]
        [SerializeField] private float mouseSensitivity = 0.12f;

        [Tooltip("Lissage de la vitesse angulaire (Lerp).")]
        [SerializeField] private float rotationSmoothing = 10f;

        [Header("Physique spatiale")]
        [Tooltip("Trainée linéaire en espace (0 = inertie newtonienne pure).")]
        [SerializeField] private float spaceDragLinear  = 0f;

        [Tooltip("Trainée angulaire en espace.")]
        [SerializeField] private float spaceDragAngular = 1.5f;

        [Header("Physique atmosphérique")]
        [Tooltip("Trainée linéaire dans l'atmosphère (résistance de l'air).")]
        [SerializeField] private float atmosphereDragLinear  = 2f;

        [Tooltip("Trainée angulaire dans l'atmosphère.")]
        [SerializeField] private float atmosphereDragAngular = 4f;

        [Header("Flight Assist")]
        [Tooltip("Active le frein automatique sur les axes sans input (Tab pour toggle).")]
        [SerializeField] private bool  flightAssistEnabled = true;

        [Tooltip("Force de contre-poussée du Flight Assist (m/s² par m/s de vitesse résiduelle).")]
        [SerializeField] private float assistStrength      = 6f;

        [Header("Embarquement")]
        [Tooltip("Point de sortie : là où le joueur réapparaît après débarquement.")]
        [SerializeField] public Transform exitPoint;

        [Tooltip("Rayon maximum pour embarquer (unités).")]
        [SerializeField] private float boardingRadius = 8f;

        [Header("Références")]
        [Tooltip("Caméra 3e personne du vaisseau (inactive par défaut).")]
        [SerializeField] public Camera shipCamera;

        [Header("Trainée moteur")]
        [Tooltip("ParticleSystem(s) de tuyères. Laisser vide = création automatique en arrière du vaisseau.")]
        [SerializeField] private ParticleSystem[] thrusterParticles;

        [Tooltip("Vitesse de référence pour la trainée à pleine puissance (m/s).")]
        [SerializeField, Min(1f)] private float trailMaxSpeed = 80f;

        [Tooltip("Nombre maximum de particules simultanées.")]
        [SerializeField] private int trailMaxParticles = 600;

        [Tooltip("Matériau des particules (laisser vide = défaut Unity). Recommandé : Particles/Additive ou URP Unlit.")]
        [SerializeField] private Material thrusterMaterial;

        // ── Composants ────────────────────────────────────────

        private Rigidbody        _rb;
        private OzoneLayer       _ozone;
        private GravityAttractor _attractor;
        private PlanetWorld      _world;

        // ── État ──────────────────────────────────────────────

        private bool      _piloting;
        private Transform _player;
        private Camera    _playerCamera;

        /// <summary>Cible de vitesse angulaire locale (rad/s), interpolée chaque FixedUpdate.</summary>
        private Vector3 _targetLocalAngVel;

        // ── Propriétés publiques ──────────────────────────────

        /// <summary>Vrai si le joueur est actuellement aux commandes.</summary>
        public bool IsPiloting => _piloting;

        /// <summary>Vrai si n'importe quel vaisseau est en cours de pilotage (partagé entre toutes les instances).</summary>
        public static bool IsAnyShipPiloted { get; private set; }

        /// <summary>Vitesse scalaire en unités/s.</summary>
        public float Speed => _rb != null ? _rb.linearVelocity.magnitude : 0f;

        /// <summary>Altitude au-dessus de la surface planétaire (unités).</summary>
        public float Altitude
        {
            get
            {
                if (_attractor == null) return 0f;
                return Vector3.Distance(transform.position, _attractor.transform.position)
                       - PlanetChunkGenerator.PlanetCoreRadius;
            }
        }

        /// <summary>Vrai si le vaisseau se trouve dans l'atmosphère.</summary>
        public bool IsInAtmosphere =>
            _ozone != null && _ozone.IsInsideAtmosphere(transform.position);

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity         = false;
            _rb.freezeRotation     = false;   // rotation gérée via angularVelocity
            _rb.mass               = 1000f;
            _rb.interpolation      = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            _ozone     = FindAnyObjectByType<OzoneLayer>();
            _attractor = FindAnyObjectByType<GravityAttractor>();
        }

        private void Start()
        {
            if (thrusterParticles == null || thrusterParticles.Length == 0)
                thrusterParticles = new[] { CreateDefaultThrusterPS() };
            else
                foreach (var ps in thrusterParticles)
                    ConfigureThrusterPS(ps);
        }

        /// <summary>
        /// Câble les références joueur / caméra joueur.
        /// Appelé depuis <see cref="AstroVoxel.Bootstrap.GameBootstrap"/>.
        /// </summary>
        public void SetPlayerReferences(Transform player, Camera playerCam)
        {
            _player       = player;
            _playerCamera = playerCam;
        }

        /// <summary>
        /// Câble la référence PlanetWorld pour le chargement des chunks.
        /// </summary>
        public void SetPlanetWorld(PlanetWorld world) => _world = world;

        private void Update()
        {
            if (!_piloting)
            {
                // Permettre l'embarquement si le joueur est proche et presse F
                if (_player != null
                    && Vector3.Distance(transform.position, _player.position) <= boardingRadius
                    && GetKeyDown_Board())
                {
                    Board();
                }
                return;
            }

            // Toggle Flight Assist
            if (GetKeyDown_FlightAssist())
                flightAssistEnabled = !flightAssistEnabled;

            // Re-verrouiller le curseur si nécessaire (perte de focus, clic hors jeu, éditeur…)
            // Même logique que PlayerCamera : clic gauche pour re-capturer.
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                if (GetAnyMouseButtonDown())
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible   = false;
                }
            }

            // Calcule la vitesse angulaire cible depuis les entrées souris + touches.
            // La souris n'est lue que si le curseur est verrouillé (évite les deltas
            // aberrants quand le curseur est libre et bute sur les bords d'écran).
            bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
            float mouseX     = cursorLocked ? GetMouseX() * mouseSensitivity : 0f;
            float mouseY     = cursorLocked ? GetMouseY() * mouseSensitivity : 0f;
            float roll       = GetRoll();
            float arrowYaw   = GetArrowYaw();
            float arrowPitch = GetArrowPitch();

            _targetLocalAngVel = new Vector3(
                -(mouseY + arrowPitch) * pitchSpeed,
                (mouseX + arrowYaw)   * yawSpeed,
                -roll                 * rollSpeed
            ) * Mathf.Deg2Rad;

            // Débarquer
            if (GetKeyDown_Board())
                Disembark();
        }

        /// <summary>
        /// Re-verrouille automatiquement le curseur quand l'application reprend le focus
        /// (alt-tab, clic hors éditeur, etc.).
        /// </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (_piloting && hasFocus)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible   = false;
            }
        }

        private void LateUpdate() => UpdateThrusterTrail();

        private void FixedUpdate()
        {
            // Ajuste la trainée selon le régime physique
            bool inAtmo = IsInAtmosphere;
            _rb.linearDamping  = inAtmo ? atmosphereDragLinear  : spaceDragLinear;
            _rb.angularDamping = inAtmo ? atmosphereDragAngular : spaceDragAngular;

            // Gravité planétaire uniquement dans l'atmosphère
            if (inAtmo && _attractor != null)
            {
                Vector3 gravDir = (_attractor.transform.position - _rb.position).normalized;
                _rb.AddForce(gravDir * _attractor.GravityForce, ForceMode.Acceleration);
            }

            if (!_piloting) return;

            ApplyThrust();
            ApplyFlightAssist();
            ApplyRotation();
            ClampSpeed();
        }

        // ── Propulsion ────────────────────────────────────────

        private void ApplyThrust()
        {
            float fwd    = GetForward();
            float strafe = GetLateral();
            float vert   = GetVerticalThrust();
            float mult   = GetBoost() ? boostMultiplier : 1f;

            Vector3 force =
                  transform.forward * (fwd    * mainThrust)
                + transform.right   * (strafe * lateralThrust)
                + transform.up      * (vert   * lateralThrust);

            if (force.sqrMagnitude > 0f)
                _rb.AddForce(force * mult, ForceMode.Acceleration);
        }

        private void ClampSpeed()
        {
            float sqrMax = maxSpeed * maxSpeed;
            if (_rb.linearVelocity.sqrMagnitude > sqrMax)
                _rb.linearVelocity = _rb.linearVelocity.normalized * maxSpeed;
        }

        // ── Flight Assist ─────────────────────────────────────

        private void ApplyFlightAssist()
        {
            if (!flightAssistEnabled) return;

            // Projette la vitesse dans le repère local du vaisseau
            Vector3 localVel = transform.InverseTransformDirection(_rb.linearVelocity);

            float fwd    = GetForward();
            float strafe = GetLateral();
            float vert   = GetVerticalThrust();

            // Sur chaque axe sans input, applique une contre-force proportionnelle à la vitesse résiduelle
            Vector3 assistLocal = Vector3.zero;
            if (Mathf.Abs(fwd)    < 0.1f) assistLocal.z -= localVel.z * assistStrength;
            if (Mathf.Abs(strafe) < 0.1f) assistLocal.x -= localVel.x * assistStrength;
            if (Mathf.Abs(vert)   < 0.1f) assistLocal.y -= localVel.y * assistStrength;

            if (assistLocal.sqrMagnitude > 0f)
                _rb.AddForce(transform.TransformDirection(assistLocal), ForceMode.Acceleration);
        }

        // ── Rotation ──────────────────────────────────────────

        private void ApplyRotation()
        {
            // Convertit la cible locale (deg/s → rad/s) en espace monde
            Vector3 worldTarget = transform.TransformDirection(_targetLocalAngVel);

            // Interpolation lissée vers la cible
            _rb.angularVelocity = Vector3.Lerp(
                _rb.angularVelocity,
                worldTarget,
                Time.fixedDeltaTime * rotationSmoothing
            );
        }

        // ── Embarquement / Débarquement ───────────────────────

        /// <summary>Le joueur monte dans le vaisseau.</summary>
        public void Board()
        {
            _piloting        = true;
            IsAnyShipPiloted = true;

            // Le vaisseau devient le viewer pour le chargement des chunks
            if (_world != null) _world.SetViewer(transform);

            // Masquer le joueur
            if (_player != null) _player.gameObject.SetActive(false);

            // Activer la caméra vaisseau, éteindre la caméra joueur
            if (shipCamera    != null) shipCamera.gameObject.SetActive(true);
            if (_playerCamera != null) _playerCamera.gameObject.SetActive(false);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            Debug.Log("[SpaceShip] Embarqué !");
        }

        /// <summary>Le joueur quitte le vaisseau.</summary>
        public void Disembark()
        {
            _piloting        = false;
            IsAnyShipPiloted = false;

            // Rend la main au joueur pour le chargement des chunks
            if (_world != null && _player != null) _world.SetViewer(_player);

            // Téléporter le joueur au point de sortie
            if (_player != null && exitPoint != null)
            {
                _player.position = exitPoint.position;
                _player.rotation = exitPoint.rotation;
            }

            // Réactiver le joueur
            if (_player != null) _player.gameObject.SetActive(true);

            // Basculer les caméras
            if (shipCamera    != null) shipCamera.gameObject.SetActive(false);
            if (_playerCamera != null) _playerCamera.gameObject.SetActive(true);

            // Relâcher le curseur (PlayerCamera le recapture au prochain clic)
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;

            Debug.Log("[SpaceShip] Débarqué !");
        }

        // ── Trainée moteur ────────────────────────────────────

        private ParticleSystem CreateDefaultThrusterPS()
        {
            var go = new GameObject("ThrusterTrail");
            go.transform.SetParent(transform, false);
            // Centré en arrière du vaisseau (axe -Z local)
            go.transform.localPosition = Vector3.back * 1.5f;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            ConfigureThrusterPS(ps);
            return ps;
        }

        private void ConfigureThrusterPS(ParticleSystem ps)
        {
            // ── Main ──────────────────────────────────────────
            var main = ps.main;
            main.loop            = true;
            main.playOnAwake     = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = trailMaxParticles;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(4f, 18f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.12f, 0.55f);
            // Blanc chaud au cœur → orange vif (tuyère classique)
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 0.85f, 0.6f, 1.0f),
                new Color(1.0f, 0.55f, 0.1f, 0.9f)
            );

            // ── Emission (piloté via UpdateThrusterTrail) ──────
            var emission = ps.emission;
            emission.rateOverTime = 0f;

            // ── Shape : cône large vers l'arrière ─────────────
            var shape = ps.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle     = 22f;
            shape.radius    = 0.4f;

            // ── Color over lifetime : blanc → orange → rouge sombre → transparent
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f,   0.95f, 0.8f),  0f),
                    new GradientColorKey(new Color(1f,   0.45f, 0.05f), 0.3f),
                    new GradientColorKey(new Color(0.5f, 0.08f, 0.01f), 0.7f),
                    new GradientColorKey(new Color(0.1f, 0.02f, 0f),    1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f,   0f),
                    new GradientAlphaKey(0.8f, 0.3f),
                    new GradientAlphaKey(0.3f, 0.7f),
                    new GradientAlphaKey(0f,   1f),
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            // ── Size over lifetime : s'élargit en se dissipant ─
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f,    0.15f, 0f, 6f),
                new Keyframe(0.12f, 1f,    6f, 1f),
                new Keyframe(1f,    4.0f,  1f, 0f)
            ));

            // ── Noise : turbulence organique ───────────────────
            var noise = ps.noise;
            noise.enabled     = true;
            noise.strength    = 0.12f;
            noise.frequency   = 0.9f;
            noise.scrollSpeed = 0.4f;
            noise.damping     = true;

            // ── Renderer ──────────────────────────────────────
            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.sortingFudge = -5f;
            rend.material     = thrusterMaterial != null
                ? thrusterMaterial
                : CreateThrusterMaterial();
        }

        /// <summary>
        /// Crée un matériau additif compatible URP et Built-in RP.
        /// Évite le matériau "rose" par défaut de Unity.
        /// </summary>
        private static Material CreateThrusterMaterial()
        {
            // Priorité : shader URP particles → legacy Additive → Sprites/Default
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Particles/Additive")
                     ?? Shader.Find("Sprites/Default");

            var mat = new Material(sh) { name = "ThrusterParticle_Auto" };

            // Blending additif : les particules s'additionnent (glow naturel)
            mat.SetFloat("_Surface",   1f);   // Transparent
            mat.SetFloat("_BlendMode", 3f);   // Additive
            mat.SetFloat("_SrcBlend",  1f);   // One
            mat.SetFloat("_DstBlend",  1f);   // One
            mat.SetInt  ("_ZWrite",    0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        /// <summary>
        /// Ajuste le débit d'émission de chaque ParticleSystem de tuyère
        /// en fonction de la vitesse courante du vaisseau.
        /// </summary>
        private void UpdateThrusterTrail()
        {
            if (thrusterParticles == null) return;

            // Rampe quadratique : rien à l'arrêt, plein régime à trailMaxSpeed
            float t    = Mathf.Clamp01(Speed / trailMaxSpeed);
            float rate = t * t * 260f;

            foreach (var ps in thrusterParticles)
            {
                if (ps == null) continue;
                var emission = ps.emission;
                emission.rateOverTime = rate;

                // Durée de vie et vitesse s'étirent proportionnellement
                var main = ps.main;
                main.startSpeedMultiplier    = Mathf.Lerp(0.35f, 1f, t);
                main.startLifetimeMultiplier = Mathf.Lerp(0.3f,  1f, t);
            }
        }

        // ── Abstraction Input ─────────────────────────────────

        private static float GetForward()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return 0f;
            float v = 0f;
            if (kb.wKey.isPressed || kb.zKey.isPressed) v += 1f;
            if (kb.sKey.isPressed)                      v -= 1f;
            return v;
#else
            float v = 0f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Z)) v += 1f;
            if (Input.GetKey(KeyCode.S))                             v -= 1f;
            return v;
#endif
        }

        private static float GetLateral()
        {
            // ZQSD : A = gauche, D = droite
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return 0f;
            float v = 0f;
            if (kb.aKey.isPressed) v -= 1f;
            if (kb.dKey.isPressed) v += 1f;
            return v;
#else
            float v = 0f;
            if (Input.GetKey(KeyCode.A)) v -= 1f;
            if (Input.GetKey(KeyCode.D)) v += 1f;
            return v;
#endif
        }

        private static float GetArrowYaw()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return 0f;
            float v = 0f;
            if (kb.leftArrowKey.isPressed)  v -= 1f;
            if (kb.rightArrowKey.isPressed) v += 1f;
            return v;
#else
            float v = 0f;
            if (Input.GetKey(KeyCode.LeftArrow))  v -= 1f;
            if (Input.GetKey(KeyCode.RightArrow)) v += 1f;
            return v;
#endif
        }

        private static float GetArrowPitch()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return 0f;
            float v = 0f;
            if (kb.upArrowKey.isPressed)   v -= 1f;
            if (kb.downArrowKey.isPressed) v += 1f;
            return v;
#else
            float v = 0f;
            if (Input.GetKey(KeyCode.UpArrow))   v -= 1f;
            if (Input.GetKey(KeyCode.DownArrow)) v += 1f;
            return v;
#endif
        }

        private static float GetVerticalThrust()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return 0f;
            float v = 0f;
            if (kb.spaceKey.isPressed)     v += 1f;
            if (kb.leftCtrlKey.isPressed)  v -= 1f;
            return v;
#else
            float v = 0f;
            if (Input.GetKey(KeyCode.Space))       v += 1f;
            if (Input.GetKey(KeyCode.LeftControl)) v -= 1f;
            return v;
#endif
        }

        private static float GetRoll()
        {
            // Q = roulis gauche, E = roulis droite
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return 0f;
            float v = 0f;
            if (kb.eKey.isPressed) v += 1f;
            if (kb.qKey.isPressed) v -= 1f;
            return v;
#else
            float v = 0f;
            if (Input.GetKey(KeyCode.E)) v += 1f;
            if (Input.GetKey(KeyCode.Q)) v -= 1f;
            return v;
#endif
        }

        private static bool GetKeyDown_FlightAssist()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.tabKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Tab);
#endif
        }

        private static bool GetBoost()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.leftShiftKey.isPressed;
#else
            return Input.GetKey(KeyCode.LeftShift);
#endif
        }

        private static bool GetKeyDown_Board()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.fKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F);
#endif
        }

        private static bool GetAnyMouseButtonDown()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            return m != null && (m.leftButton.wasPressedThisFrame ||
                                 m.rightButton.wasPressedThisFrame);
#else
            return Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
#endif
        }

        private static float GetMouseX()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            return m != null ? m.delta.x.ReadValue() : 0f;
#else
            return Input.GetAxisRaw("Mouse X");
#endif
        }

        private static float GetMouseY()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            return m != null ? m.delta.y.ReadValue() : 0f;
#else
            return Input.GetAxisRaw("Mouse Y");
#endif
        }

        // ── Gizmos ────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Rayon d'embarquement (vert)
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, boardingRadius);

            // Point de sortie (rouge)
            if (exitPoint != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(exitPoint.position, 0.4f);
            }
        }
#endif
    }
}

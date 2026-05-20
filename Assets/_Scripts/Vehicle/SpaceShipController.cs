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

        [Header("Survie — Collision")]
        [Tooltip("Vitesse d'impact minimale (m/s) pour déclencher l'explosion en mode Survie.\n" +
                 "Valeur par défaut : 100 m/s ≈ 50 % de maxSpeed (200). " +
                 "Un atterrissage normal (< 50 m/s) reste sans danger ; " +
                 "seul un impact à pleine vitesse boost provoque l'explosion.")]
        [SerializeField] private float crashSpeedThreshold = 100f;

        [Header("Trainée moteur")]
        [Tooltip("ParticleSystem(s) de tuyères. Laisser vide = création automatique en arrière du vaisseau.")]
        [SerializeField] private ParticleSystem[] thrusterParticles;

        [Tooltip("Vitesse de référence pour la trainée à pleine puissance (m/s).")]
        [SerializeField, Min(1f)] private float trailMaxSpeed = 80f;

        [Tooltip("Nombre maximum de particules simultanées.")]
        [SerializeField] private int trailMaxParticles = 600;

        [Tooltip("Matériau des particules (laisser vide = défaut Unity). Recommandé : Particles/Additive ou URP Unlit.")]
        [SerializeField] private Material thrusterMaterial;

        [Header("Propulsion verticale (Espace)")]
        [Tooltip("ParticleSystem(s) de tuyère verticale (Espace → poussée haut). Laisser vide = création auto.")]
        [SerializeField] private ParticleSystem[] verticalThrusterParticles;

        [Tooltip("Décalage vers le bas du point d'émission vertical (unités locales).")]
        [SerializeField, Min(0f)] private float verticalThrusterOffset = 1.5f;

        [Header("Trainées d'ailes (Boost)")]
        [Tooltip("Trainée de l'aile gauche. Laisser vide = création auto.")]
        [SerializeField] private ParticleSystem wingTrailLeft;

        [Tooltip("Trainée de l'aile droite. Laisser vide = création auto.")]
        [SerializeField] private ParticleSystem wingTrailRight;

        [Tooltip("Écartement latéral des trainées depuis l'axe central (unités).")]
        [SerializeField] private float wingSpan               = 4.5f;

        [Tooltip("Décalage vertical des trainées d'ailes (négatif = sous les ailes).")]
        [SerializeField] private float wingVerticalOffset     = -0.2f;

        [Tooltip("Décalage longitudinal des trainées d'ailes (négatif = vers l'arrière).")]
        [SerializeField] private float wingLongitudinalOffset = -0.5f;

        // ── Composants ────────────────────────────────────────

        private Rigidbody        _rb;
        private OzoneLayer       _ozone;
        private GravityAttractor _attractor;
        private PlanetWorld      _world;
        // Cache des attracteurs d'astéroïdes (rafraîchi périodiquement — évite
        // un FindObjectsByType par FixedUpdate). 50 frames ≈ 1 s à 50 Hz.
        private GravityAttractor[] _asteroidAttractors = new GravityAttractor[0];
        private int               _attractorRefreshTimer = 0;
        private const int         AttractorRefreshFrames = 60;
        // ── État ──────────────────────────────────────────────

        private bool      _piloting;
        private bool      _exploded;
        private Transform _player;
        private Camera    _playerCamera;

        /// <summary>Cible de vitesse angulaire locale (rad/s), interpolée chaque FixedUpdate.</summary>
        private Vector3 _targetLocalAngVel;

        // ── Propriétés publiques ──────────────────────────────

        /// <summary>Vrai si le joueur est actuellement aux commandes.</summary>
        public bool IsPiloting => _piloting;

        /// <summary>Vrai si n'importe quel vaisseau est en cours de pilotage (partagé entre toutes les instances).</summary>
        public static bool IsAnyShipPiloted { get; private set; }

        /// <summary>Le vaisseau actuellement piloté (null si aucun).</summary>
        public static SpaceShipController ActiveShip { get; private set; }

        /// <summary>
        /// Identifiant unique de ce vaisseau dans la session courante (0, 1, 2…).
        /// Assigné une fois dans Awake via <see cref="_nextShipId"/>.
        /// Appeler <see cref="ResetIdCounter"/> avant de créer les vaisseaux au démarrage
        /// de scène pour garantir la cohérence avec les données de sauvegarde.
        /// </summary>
        public int ShipId { get; private set; }
        private static int _nextShipId = 0;

        /// <summary>Remet le compteur d'ID à zéro. Appelé par GameBootstrap avant la création des vaisseaux.</summary>
        public static void ResetIdCounter() => _nextShipId = 0;

        /// <summary>Vitesse scalaire en unités/s.</summary>
        public float Speed => _rb != null ? _rb.linearVelocity.magnitude : 0f;

        /// <summary>Vrai si le boost (Shift gauche) est actif et le joueur pilote.</summary>
        public bool IsBoostActive => _piloting && GetBoost();

        /// <summary>Vrai si la poussée verticale (Espace) est active.</summary>
        public bool IsVerticalThrustActive => _piloting && GetVerticalThrust() > 0.1f;

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
            ShipId = _nextShipId++;

            _rb = GetComponent<Rigidbody>();
            _rb.useGravity         = false;
            _rb.freezeRotation     = false;   // rotation gérée via angularVelocity
            _rb.mass               = 1000f;
            _rb.interpolation      = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            _ozone     = FindAnyObjectByType<OzoneLayer>();
            // Récupère spécifiquement l'attracteur PLANÉTAIRE (InfluenceRadius == 0,
            // les astéroïdes ont une zone d'influence finie >0).
            var allAttractors = FindObjectsByType<GravityAttractor>(FindObjectsInactive.Exclude);
            for (int i = 0; i < allAttractors.Length; i++)
            {
                if (allAttractors[i] != null && allAttractors[i].InfluenceRadius <= 0f)
                {
                    _attractor = allAttractors[i];
                    break;
                }
            }
        }

        private void Start()
        {
            if (thrusterParticles == null || thrusterParticles.Length == 0)
                thrusterParticles = new[] { CreateDefaultThrusterPS() };
            else
                foreach (var ps in thrusterParticles)
                    ConfigureThrusterPS(ps);

            if (verticalThrusterParticles == null || verticalThrusterParticles.Length == 0)
                verticalThrusterParticles = new[] { CreateDefaultVerticalThrusterPS() };
            else
                foreach (var ps in verticalThrusterParticles)
                    ConfigureVerticalThrusterPS(ps);

            if (wingTrailLeft  == null) wingTrailLeft  = CreateDefaultWingTrailPS(leftSide: true);
            if (wingTrailRight == null) wingTrailRight = CreateDefaultWingTrailPS(leftSide: false);
        }

        /// <summary>
        /// Câble les références joueur / caméra joueur.
        /// Appelé depuis <see cref="AstroVoxel.Bootstrap.GameBootstrap"/>.
        /// </summary>
        public void SetPlayerReferences(Transform player, Camera playerCam)
        {
            _player       = player;
            _playerCamera = playerCam;

            // S'assurer que la caméra du vaisseau est taguée MainCamera afin que
            // Camera.main la retourne lors du pilotage (sinon le LOD des astéroïdes
            // se base sur une référence figée, le joueur étant désactivé).
            if (shipCamera != null)
            {
                try { shipCamera.tag = "MainCamera"; }
                catch (UnityException) { /* tag absent du projet */ }
            }
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
                    && GetKeyDown_Board()
                    && !AstroVoxel.Network.ServerManager.IsShipPilotedByRemote)
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

        private void LateUpdate()
        {
            UpdateThrusterTrail();
            UpdateVerticalThrusterTrail();
            UpdateWingTrails();
        }

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

            // Gravité des astéroïdes : actif en permanence dès qu'on entre dans la
            // zone d'influence d'un astéroïde (même en atmosphère, faible impact).
            ApplyAsteroidGravity();

            if (!_piloting) return;

            ApplyThrust();
            ApplyFlightAssist();
            ApplyRotation();
            ClampSpeed();
        }

        // ── Gravité multi-astéroïdes ──────────────────────────

        /// <summary>
        /// Trouve l'astéroïde le plus proche dans sa zone d'influence et applique
        /// sa gravité. Cache la liste pour éviter un FindObjectsByType par tick.
        /// </summary>
        private void ApplyAsteroidGravity()
        {
            // Rafraîchit la liste périodiquement (les astéroïdes peuvent apparaître/disparaître).
            if (_attractorRefreshTimer <= 0)
            {
                _attractorRefreshTimer = AttractorRefreshFrames;
                var all = FindObjectsByType<GravityAttractor>(FindObjectsInactive.Exclude);
                int count = 0;
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].InfluenceRadius > 0f) count++;
                _asteroidAttractors = new GravityAttractor[count];
                int k = 0;
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].InfluenceRadius > 0f) _asteroidAttractors[k++] = all[i];
            }
            _attractorRefreshTimer--;

            GravityAttractor best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _asteroidAttractors.Length; i++)
            {
                var a = _asteroidAttractors[i];
                if (a == null) continue;
                float d = Vector3.Distance(_rb.position, a.transform.position);
                if (d <= a.InfluenceRadius && d < bestDist)
                {
                    best     = a;
                    bestDist = d;
                }
            }

            if (best != null)
            {
                Vector3 dir = (best.transform.position - _rb.position).normalized;
                _rb.AddForce(dir * best.GravityForce, ForceMode.Acceleration);
            }
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
            ActiveShip       = this;

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
            if (ActiveShip == this) ActiveShip = null;

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

        // ── Collision / Explosion (Survie) ─────────────────

        private void OnCollisionEnter(Collision collision)
        {
            if (!AstroVoxel.Player.GameModeManager.IsSurvival) return;
            if (!_piloting) return;
            if (_exploded) return;

            // Seuil de vitesse : on vérifie la vitesse relative à l'impact
            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed < crashSpeedThreshold) return;

            TriggerCrashExplosion();
        }

        /// <summary>
        /// Déclenche l'explosion du vaisseau : particules, éjection du joueur, mort.
        /// Peut être appelé depuis l'extérieur (ex. mort par le soleil).
        /// </summary>
        /// <param name="deathMessage">Message affiché sur l'écran de mort (null = message aléatoire).</param>
        public void TriggerCrashExplosion(string deathMessage = null)
        {
            if (_exploded) return;
            _exploded = true;

            // Débarquer le joueur AVANT de cacher le vaisseau
            if (_piloting) Disembark();

            // Particules d'explosion à la position du vaisseau
            SpawnExplosionEffect(transform.position);

            // Stopper le vaisseau
            _rb.linearVelocity  = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            // Cacher le vaisseau
            gameObject.SetActive(false);

            // Tuer le joueur (déclenchera DeathScreen via PlayerHealth)
            if (_player != null)
            {
                var health = _player.GetComponent<AstroVoxel.Player.PlayerHealth>();
                if (health != null)
                {
                    if (!string.IsNullOrEmpty(deathMessage))
                        health.KillWithMessage(deathMessage);
                    else
                        health.TakeDamage(AstroVoxel.Player.PlayerHealth.MaxHealth);
                }
            }
        }

        private static void SpawnExplosionEffect(Vector3 position)
        {
            // ── Boule de feu centrale ─────────────────────────
            var fireGO = new GameObject("ShipExplosion_Fire");
            fireGO.transform.position = position;
            var firePS = fireGO.AddComponent<ParticleSystem>();
            ConfigureExplosionFirePS(firePS);
            firePS.Play();
            UnityEngine.Object.Destroy(fireGO, 4f);

            // ── Nuage de fumée ────────────────────────────────
            var smokeGO = new GameObject("ShipExplosion_Smoke");
            smokeGO.transform.position = position;
            var smokePS = smokeGO.AddComponent<ParticleSystem>();
            ConfigureExplosionSmokePS(smokePS);
            smokePS.Play();
            UnityEngine.Object.Destroy(smokeGO, 6f);
        }

        private static void ConfigureExplosionFirePS(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop            = false;
            main.duration        = 0.4f;
            main.playOnAwake     = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = 1500;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.5f, 2.2f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(8f, 90f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.4f, 4.5f);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 0.85f, 0.3f, 1f),
                new Color(1.0f, 0.20f, 0.0f, 1f)
            );
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.04f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1500, 1, 0.05f) });

            var shape = ps.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius    = 1.8f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.0f, 0.97f, 0.7f), 0.00f),
                    new GradientColorKey(new Color(1.0f, 0.45f, 0.05f), 0.20f),
                    new GradientColorKey(new Color(0.45f, 0.06f, 0.01f), 0.50f),
                    new GradientColorKey(new Color(0.10f, 0.10f, 0.10f), 1.00f),
                },
                new[]
                {
                    new GradientAlphaKey(1.0f,  0.00f),
                    new GradientAlphaKey(0.9f,  0.20f),
                    new GradientAlphaKey(0.55f, 0.50f),
                    new GradientAlphaKey(0.0f,  1.00f),
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f,    0.2f, 0f, 8f),
                new Keyframe(0.15f, 1.0f, 8f, 1f),
                new Keyframe(1f,    3.0f, 1f, 0f)
            ));

            var noise = ps.noise;
            noise.enabled     = true;
            noise.strength    = 1.8f;
            noise.frequency   = 0.6f;
            noise.scrollSpeed = 1.2f;
            noise.damping     = true;

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.sortingFudge = -10f;
            rend.material     = CreateThrusterMaterial();
        }

        private static void ConfigureExplosionSmokePS(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop            = false;
            main.duration        = 0.6f;
            main.playOnAwake     = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = 600;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(2.0f, 5.0f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(2f, 18f);
            main.startSize       = new ParticleSystem.MinMaxCurve(1.5f, 8.0f);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                new Color(0.18f, 0.18f, 0.18f, 0.8f),
                new Color(0.06f, 0.06f, 0.06f, 0.6f)
            );
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.05f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 600, 1, 0.1f) });

            var shape = ps.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius    = 2.5f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.25f, 0.22f, 0.18f), 0.0f),
                    new GradientColorKey(new Color(0.12f, 0.12f, 0.12f), 0.4f),
                    new GradientColorKey(new Color(0.06f, 0.06f, 0.06f), 1.0f),
                },
                new[]
                {
                    new GradientAlphaKey(0.7f,  0.0f),
                    new GradientAlphaKey(0.4f,  0.4f),
                    new GradientAlphaKey(0.0f,  1.0f),
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f,   0.3f),
                new Keyframe(0.3f, 1.0f),
                new Keyframe(1f,   2.5f)
            ));

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.sortingFudge = -9f;
            rend.material     = CreateThrusterMaterial();
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
        /// Crée un matériau additif pour les particules de tuyère.
        /// Utilise AstroVoxel/ThrusterParticle (garanti inclus dans le build)
        /// pour éviter le rose "shader manquant".
        /// </summary>
        private static Material CreateThrusterMaterial()
        {
            // AstroVoxel/ThrusterParticle est toujours compilé (dans Assets/_Shaders/).
            // Repli sur les shaders URP/Legacy si absent (éditeur uniquement).
            Shader sh = Shader.Find("AstroVoxel/ThrusterParticle")
                     ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Particles/Additive")
                     ?? Shader.Find("Sprites/Default");

            var mat = new Material(sh) { name = "ThrusterParticle_Auto" };
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

        private ParticleSystem CreateDefaultVerticalThrusterPS()
        {
            var go = new GameObject("VerticalThrusterTrail");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.down * verticalThrusterOffset;
            // Euler(90,0,0) oriente le forward du PS vers le bas local (-Y)
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ConfigureVerticalThrusterPS(ps);
            return ps;
        }

        private void ConfigureVerticalThrusterPS(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop            = true;
            main.playOnAwake     = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = 400;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(6f, 22f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.10f, 0.48f);
            // Cyan-blanc électrique (tuyère ionique / RCS) — contraste fort avec l'orange principal
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.7f, 0.95f, 1.0f, 1.0f),
                new Color(0.2f, 0.55f, 1.0f, 0.9f)
            );

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle     = 18f;
            shape.radius    = 0.35f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.9f,  1.0f,  1.0f),  0f),
                    new GradientColorKey(new Color(0.3f,  0.7f,  1.0f),  0.4f),
                    new GradientColorKey(new Color(0.05f, 0.2f,  0.8f),  0.75f),
                    new GradientColorKey(new Color(0.0f,  0.05f, 0.3f),  1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f,    0f),
                    new GradientAlphaKey(0.7f,  0.4f),
                    new GradientAlphaKey(0.25f, 0.75f),
                    new GradientAlphaKey(0f,    1f),
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f,    0.1f, 0f, 5f),
                new Keyframe(0.15f, 1f,   5f, 1f),
                new Keyframe(1f,    3.5f, 1f, 0f)
            ));

            var noise = ps.noise;
            noise.enabled     = true;
            noise.strength    = 0.10f;
            noise.frequency   = 1.1f;
            noise.scrollSpeed = 0.5f;
            noise.damping     = true;

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.sortingFudge = -5f;
            rend.material     = thrusterMaterial != null
                ? thrusterMaterial
                : CreateThrusterMaterial();
        }

        private ParticleSystem CreateDefaultWingTrailPS(bool leftSide)
        {
            string goName = leftSide ? "WingTrailLeft" : "WingTrailRight";
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            float side = leftSide ? -wingSpan : wingSpan;
            go.transform.localPosition = new Vector3(side, wingVerticalOffset, wingLongitudinalOffset);
            // Identique à la tuyère principale : forward vers l'arrière
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ConfigureWingTrailPS(ps);
            return ps;
        }

        private void ConfigureWingTrailPS(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop            = true;
            main.playOnAwake     = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = 300;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(3f, 10f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
            // Cyan plasma — trainée d'aile aérodynamique
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.4f, 0.9f, 1.0f, 0.8f),
                new Color(0.1f, 0.5f, 1.0f, 0.6f)
            );

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            // Cône très étroit → trainée fine et précise
            var shape = ps.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle     = 5f;
            shape.radius    = 0.08f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.8f, 1.0f, 1.0f), 0f),
                    new GradientColorKey(new Color(0.2f, 0.6f, 1.0f), 0.35f),
                    new GradientColorKey(new Color(0.0f, 0.2f, 0.7f), 0.7f),
                    new GradientColorKey(new Color(0.0f, 0.0f, 0.2f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.9f,  0f),
                    new GradientAlphaKey(0.5f,  0.35f),
                    new GradientAlphaKey(0.15f, 0.7f),
                    new GradientAlphaKey(0f,    1f),
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f,   0.2f, 0f, 3f),
                new Keyframe(0.2f, 1f,   3f, 1f),
                new Keyframe(1f,   2.5f, 1f, 0f)
            ));

            // Pas de noise : trainées aérodynamiques propres et nettes
            var noise = ps.noise;
            noise.enabled = false;

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.sortingFudge = -4f;
            rend.material     = thrusterMaterial != null
                ? thrusterMaterial
                : CreateThrusterMaterial();
        }

        private void UpdateVerticalThrusterTrail()
        {
            if (verticalThrusterParticles == null) return;

            bool active = _piloting && GetVerticalThrust() > 0.1f;

            foreach (var ps in verticalThrusterParticles)
            {
                if (ps == null) continue;
                var emission = ps.emission;
                emission.rateOverTime = active ? 180f : 0f;
                var main = ps.main;
                main.startSpeedMultiplier    = active ? 1f : 0.4f;
                main.startLifetimeMultiplier = active ? 1f : 0.4f;
            }
        }

        private void UpdateWingTrails()
        {
            bool boostActive = _piloting && GetBoost();
            float fwd        = _piloting ? GetForward() : 0f;
            // Les trainées s'activent uniquement si on avance en mode boost
            bool wingActive = boostActive && fwd > 0.1f;

            float t    = wingActive ? Mathf.Clamp01(Speed / trailMaxSpeed) : 0f;
            float rate = wingActive ? Mathf.Lerp(40f, 130f, t * t) : 0f;

            SetWingTrailRate(wingTrailLeft,  rate, t);
            SetWingTrailRate(wingTrailRight, rate, t);
        }

        private static void SetWingTrailRate(ParticleSystem ps, float rate, float t)
        {
            if (ps == null) return;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var main = ps.main;
            main.startSpeedMultiplier    = Mathf.Lerp(0.3f, 1f, t);
            main.startLifetimeMultiplier = Mathf.Lerp(0.3f, 1f, t);
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

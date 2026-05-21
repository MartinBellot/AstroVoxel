// ============================================================
//  EnemySpaceShipController.cs
//  Vaisseau ennemi rouge, piloté par IA (host-authoritative).
//
//  Architecture multijoueur :
//    • L'IA ne tourne que sur le HOST (ou en standalone).
//    • Les CLIENTS reçoivent les positions via EnemySyncManager
//      (av.enemy_batch, 20 Hz) et interpolent.
//
//  Machine à états :
//    PATROL  → se balade en espace libre, esquive les obstacles
//    CHASE   → fonce vers le joueur détecté
//    ATTACK  → tire des missiles à portée
//    EVADE   → évite un obstacle imminent
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using AstroVoxel.Physics;
using AstroVoxel.Network;
using AstroVoxel.Player;
using AstroVoxel.Space;
using AstroVoxel.VoxelEngine;

namespace AstroVoxel.Vehicle
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class EnemySpaceShipController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────

        [Header("Stats")]
        [SerializeField] public int   maxHealth          = 100;
        [SerializeField] private float detectionRange    = 800f;
        [SerializeField] private float attackRange       = 400f;
        [SerializeField] private float missileFireInterval = 3f;

        [Header("Mouvement")]
        [SerializeField] private float mainThrust      = 45f;
        [SerializeField] private float maxSpeed        = 160f;
        [SerializeField] private float rotationSpeed   = 85f;   // deg/s angulaire

        [Header("Patrouille")]
        [SerializeField] private float patrolRadius    = 2800f;
        [SerializeField] private float patrolTimeout   = 20f;

        [Header("Esquive obstacles")]
        [SerializeField] private float avoidanceCheckDist  = 400f;
        [SerializeField] private float avoidanceStrength   = 55f;
        [SerializeField] private float planetSafetyBuffer  = 300f;

        [Header("Particules tuyère")]
        [SerializeField, Min(1f)] private float trailMaxSpeed    = 80f;
        [SerializeField]          private int   trailMaxParticles = 400;

        // ── Identité ──────────────────────────────────────────

        public int  EnemyId        { get; set; }
        public bool IsDead         { get; private set; }
        public int  CurrentHealth  { get; private set; }

        public static readonly List<EnemySpaceShipController> AllEnemies = new();

        // ── Composants ────────────────────────────────────────

        private Rigidbody        _rb;
        private OzoneLayer       _ozone;
        private GravityAttractor _attractor;

        private GravityAttractor[] _asteroidAttractors     = System.Array.Empty<GravityAttractor>();
        private int                _attractorRefreshTimer  = 0;
        private const int          AttractorRefreshFrames  = 60;

        // ── Physique (mêmes constantes que SpaceShipController) ──

        private const float SpaceDragLinear  = 0f;
        private const float SpaceDragAngular = 2f;
        private const float AtmoDragLinear   = 3f;
        private const float AtmoDragAngular  = 5f;

        // ── Machine à états ───────────────────────────────────

        private enum AIState { Patrol, Chase, Attack, Evade }
        private AIState _state            = AIState.Patrol;
        private AIState _stateBeforeEvade = AIState.Patrol;

        // ── Cibles ────────────────────────────────────────────

        private Transform _target;
        private bool      _targetIsLocal;
        private ulong     _targetClientId;
        private float     _targetLostTimer;
        private const float TargetLostTimeout = 6f;

        // ── Patrouille ────────────────────────────────────────

        private Vector3 _patrolTarget;
        private float   _patrolTimer;

        // ── Combat ────────────────────────────────────────────

        private float _missileCooldown;

        // ── Esquive ───────────────────────────────────────────

        private float   _evadeTimer;
        private Vector3 _avoidDir;
        private const float EvadeDuration = 3f;

        // ── Centre planète (pour spawn et esquive) ────────────

        private Vector3 _planetCenter   = Vector3.zero;
        private float   _atmosphereRadius = 100f;

        // ── Particules ────────────────────────────────────────

        private ParticleSystem _thrusterPS;

        // ── Réseau : interpolation côté client ────────────────

        private Vector3    _netTargetPos;
        private Quaternion _netTargetRot = Quaternion.identity;
        private bool       _hasNetTarget;

        // ── Vitesse visuelle (client, Rigidbody kinematic) ────

        private Vector3 _prevPos;
        private float   _visualSpeed;

        // ── Événements ────────────────────────────────────────

        public event System.Action<int> OnDied;   // (enemyId)

        // ─────────────────────────────────────────────────────
        // Cycle de vie
        // ─────────────────────────────────────────────────────

        private void Awake()
        {
            AllEnemies.Add(this);
            CurrentHealth = maxHealth;

            _rb                           = GetComponent<Rigidbody>();
            _rb.useGravity                = false;
            _rb.freezeRotation            = false;
            _rb.mass                      = 800f;
            _rb.interpolation             = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode    = CollisionDetectionMode.ContinuousDynamic;

            // Configurer le BoxCollider
            var box    = GetComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size   = new Vector3(2f, 1.2f, 5f);

            // Attracteur planétaire
            var allAttractors = FindObjectsByType<GravityAttractor>(FindObjectsInactive.Exclude);
            foreach (var a in allAttractors)
            {
                if (a != null && a.InfluenceRadius <= 0f)
                {
                    _attractor    = a;
                    _planetCenter = a.transform.position;
                    break;
                }
            }

            _ozone = FindAnyObjectByType<OzoneLayer>();
            if (_ozone != null)
                _atmosphereRadius = _ozone.AtmosphereRadius;

            // CLIENT : physique désactivée (visuel uniquement)
            if (!IsHostOrStandalone())
            {
                _rb.isKinematic   = true;
                _rb.interpolation = RigidbodyInterpolation.None;
            }

            _prevPos      = transform.position;
            _patrolTarget = GetRandomSpacePosition();
        }

        private void Start()
        {
            BuildMesh();
            _thrusterPS = CreateThrusterPS();
        }

        private void OnDestroy()
        {
            AllEnemies.Remove(this);
        }

        private void FixedUpdate()
        {
            if (IsDead) return;

            if (!IsHostOrStandalone())
            {
                // CLIENT : interpolation vers cible réseau
                if (_hasNetTarget)
                {
                    transform.position = Vector3.Lerp(
                        transform.position, _netTargetPos, Time.fixedDeltaTime * 20f);
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation, _netTargetRot, Time.fixedDeltaTime * 20f);
                }
                return;
            }

            // HOST : physique + IA
            bool inAtmo = _ozone != null && _ozone.IsInsideAtmosphere(transform.position);
            _rb.linearDamping  = inAtmo ? AtmoDragLinear  : SpaceDragLinear;
            _rb.angularDamping = inAtmo ? AtmoDragAngular : SpaceDragAngular;

            if (inAtmo && _attractor != null)
            {
                Vector3 gravDir = (_attractor.transform.position - _rb.position).normalized;
                _rb.AddForce(gravDir * _attractor.GravityForce, ForceMode.Acceleration);
            }

            ApplyAsteroidGravity();
            UpdateAI();
            ClampSpeed();
        }

        private void LateUpdate()
        {
            if (IsDead) return;
            _visualSpeed = (transform.position - _prevPos).magnitude / Mathf.Max(Time.deltaTime, 0.001f);
            _prevPos     = transform.position;
            UpdateThrusterTrail();
        }

        // ─────────────────────────────────────────────────────
        // API réseau (côté client)
        // ─────────────────────────────────────────────────────

        public void SetNetworkTarget(Vector3 pos, Quaternion rot)
        {
            _netTargetPos = pos;
            _netTargetRot = rot;
            _hasNetTarget = true;
        }

        // ─────────────────────────────────────────────────────
        // Dégâts & mort (host uniquement)
        // ─────────────────────────────────────────────────────

        public void TakeDamage(int amount)
        {
            if (IsDead || !IsHostOrStandalone()) return;
            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
            if (CurrentHealth <= 0) Die();
        }

        private void Die()
        {
            if (IsDead) return;
            IsDead = true;

            SpawnExplosionEffect(transform.position);

            // Notifie EnemySyncManager pour broadcast aux clients
            EnemySyncManager.Instance?.BroadcastEnemyDestroy(EnemyId, transform.position);

            OnDied?.Invoke(EnemyId);
            gameObject.SetActive(false);
        }

        // ─────────────────────────────────────────────────────
        // Machine à états (host)
        // ─────────────────────────────────────────────────────

        private void UpdateAI()
        {
            if (_missileCooldown > 0f) _missileCooldown -= Time.fixedDeltaTime;

            // Check obstacles (prioritaire)
            if (_state != AIState.Evade && CheckObstacles(out var obstDir))
            {
                _stateBeforeEvade = _state;
                _state            = AIState.Evade;
                _avoidDir         = obstDir;
                _evadeTimer       = EvadeDuration;
            }

            switch (_state)
            {
                case AIState.Patrol: UpdatePatrol(); break;
                case AIState.Chase:  UpdateChase();  break;
                case AIState.Attack: UpdateAttack(); break;
                case AIState.Evade:  UpdateEvade();  break;
            }
        }

        private void UpdatePatrol()
        {
            _patrolTimer += Time.fixedDeltaTime;

            // Cherche joueur dans la portée
            FindBestTarget(detectionRange, out var t, out var isLocal, out var cId);
            if (t != null)
            {
                _target         = t;
                _targetIsLocal  = isLocal;
                _targetClientId = cId;
                _targetLostTimer = 0f;
                _state = AIState.Chase;
                return;
            }

            float distToTarget = Vector3.Distance(transform.position, _patrolTarget);
            if (distToTarget < 60f || _patrolTimer > patrolTimeout)
            {
                _patrolTarget = GetRandomSpacePosition();
                _patrolTimer  = 0f;
            }

            Vector3 dir = (_patrolTarget - transform.position).normalized;
            SteerAndThrust(dir, mainThrust * 0.55f);
        }

        private void UpdateChase()
        {
            RefreshTarget(detectionRange * 1.25f);
            if (_target == null)
            {
                _targetLostTimer += Time.fixedDeltaTime;
                if (_targetLostTimer > TargetLostTimeout)
                {
                    _state        = AIState.Patrol;
                    _patrolTarget = GetRandomSpacePosition();
                }
                return;
            }
            _targetLostTimer = 0f;

            float dist = Vector3.Distance(transform.position, _target.position);
            if (dist <= attackRange) { _state = AIState.Attack; return; }

            SteerAndThrust((_target.position - transform.position).normalized, mainThrust);
        }

        private void UpdateAttack()
        {
            RefreshTarget(detectionRange * 1.5f);
            if (_target == null)
            {
                _targetLostTimer += Time.fixedDeltaTime;
                if (_targetLostTimer > TargetLostTimeout) _state = AIState.Patrol;
                return;
            }
            _targetLostTimer = 0f;

            float dist = Vector3.Distance(transform.position, _target.position);
            if (dist > attackRange * 1.35f) { _state = AIState.Chase; return; }

            // Orbite autour de la cible
            Vector3 toTarget = (_target.position - transform.position).normalized;
            SteerAndThrust(toTarget, mainThrust * 0.45f);

            if (_missileCooldown <= 0f)
            {
                FireMissile();
                _missileCooldown = missileFireInterval;
            }
        }

        private void UpdateEvade()
        {
            _evadeTimer -= Time.fixedDeltaTime;
            if (_evadeTimer <= 0f) { _state = _stateBeforeEvade; return; }
            SteerAndThrust(_avoidDir, mainThrust * 1.25f);
        }

        // ─────────────────────────────────────────────────────
        // Physique helpers
        // ─────────────────────────────────────────────────────

        private void SteerAndThrust(Vector3 worldDir, float thrust)
        {
            if (worldDir.sqrMagnitude < 0.001f) return;

            // Angular velocity → face worldDir
            Quaternion targetRot = Quaternion.LookRotation(worldDir, transform.up);
            Quaternion rotDiff   = targetRot * Quaternion.Inverse(transform.rotation);
            rotDiff.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (axis.sqrMagnitude > 0.001f)
            {
                Vector3 targetAngVel = axis.normalized * (angle * Mathf.Deg2Rad * rotationSpeed);
                _rb.angularVelocity  = Vector3.Lerp(
                    _rb.angularVelocity, targetAngVel, Time.fixedDeltaTime * 8f);
            }

            // Poussée — proportionnelle à l'alignement
            float alignment  = Mathf.Clamp01(Vector3.Dot(transform.forward, worldDir));
            float thrustMult = alignment * alignment;
            _rb.AddForce(transform.forward * thrust * thrustMult, ForceMode.Acceleration);
        }

        private void ClampSpeed()
        {
            if (_rb.linearVelocity.sqrMagnitude > maxSpeed * maxSpeed)
                _rb.linearVelocity = _rb.linearVelocity.normalized * maxSpeed;
        }

        private void ApplyAsteroidGravity()
        {
            if (_attractorRefreshTimer <= 0)
            {
                _attractorRefreshTimer = AttractorRefreshFrames;
                var all   = FindObjectsByType<GravityAttractor>(FindObjectsInactive.Exclude);
                int count = 0;
                foreach (var a in all) if (a != null && a.InfluenceRadius > 0f) count++;
                _asteroidAttractors = new GravityAttractor[count];
                int k = 0;
                foreach (var a in all) if (a != null && a.InfluenceRadius > 0f) _asteroidAttractors[k++] = a;
            }
            _attractorRefreshTimer--;

            GravityAttractor best     = null;
            float            bestDist = float.MaxValue;
            foreach (var a in _asteroidAttractors)
            {
                if (a == null) continue;
                float d = Vector3.Distance(_rb.position, a.transform.position);
                if (d <= a.InfluenceRadius && d < bestDist) { best = a; bestDist = d; }
            }
            if (best != null)
            {
                Vector3 dir = (best.transform.position - _rb.position).normalized;
                _rb.AddForce(dir * best.GravityForce, ForceMode.Acceleration);
            }
        }

        // ─────────────────────────────────────────────────────
        // Esquive obstacles
        // ─────────────────────────────────────────────────────

        private bool CheckObstacles(out Vector3 avoidDir)
        {
            avoidDir = Vector3.zero;
            bool found = false;

            // Planète
            if (_attractor != null)
            {
                float distToCenter = Vector3.Distance(transform.position, _planetCenter);
                float safeRadius   = _atmosphereRadius + planetSafetyBuffer;
                if (distToCenter < safeRadius + avoidanceCheckDist)
                {
                    float urgency = 1f - Mathf.Clamp01((distToCenter - safeRadius) / avoidanceCheckDist);
                    avoidDir += (transform.position - _planetCenter).normalized * urgency * avoidanceStrength;
                    found = true;
                }
            }

            // Astéroïdes
            foreach (var a in _asteroidAttractors)
            {
                if (a == null) continue;
                float d         = Vector3.Distance(transform.position, a.transform.position);
                float checkDist = a.InfluenceRadius + 100f;
                if (d < checkDist)
                {
                    float urgency = 1f - Mathf.Clamp01((d - a.InfluenceRadius) / 100f);
                    avoidDir += (transform.position - a.transform.position).normalized * urgency;
                    found = true;
                }
            }

            if (found) avoidDir = avoidDir.normalized;
            return found;
        }

        // ─────────────────────────────────────────────────────
        // Détection joueur
        // ─────────────────────────────────────────────────────

        private void FindBestTarget(float range,
            out Transform best, out bool bestIsLocal, out ulong bestClientId)
        {
            best           = null;
            bestIsLocal    = true;
            bestClientId   = ulong.MaxValue;
            float bestDist = range;

            // Vaisseau piloté localement (prioritaire)
            if (SpaceShipController.IsAnyShipPiloted && SpaceShipController.ActiveShip != null)
            {
                float d = Vector3.Distance(transform.position,
                                           SpaceShipController.ActiveShip.transform.position);
                if (d < bestDist)
                {
                    bestDist     = d;
                    best         = SpaceShipController.ActiveShip.transform;
                    bestIsLocal  = true;
                    bestClientId = ulong.MaxValue;
                }
            }

            // Joueur local à pied
            var localHealth = PlayerHealth.Instance;
            if (localHealth != null && localHealth.gameObject.activeInHierarchy)
            {
                float d = Vector3.Distance(transform.position, localHealth.transform.position);
                if (d < bestDist)
                {
                    bestDist     = d;
                    best         = localHealth.transform;
                    bestIsLocal  = true;
                    bestClientId = ulong.MaxValue;
                }
            }

            // Joueurs distants (mannequins réseau)
            // Note : les paramètres out ne peuvent pas être capturés dans un lambda —
            // on utilise des variables locales intermédiaires.
            Transform  remoteT      = null;
            ulong      remoteCId    = ulong.MaxValue;
            float      remoteDist   = bestDist;
            PlayerNetworkSync.ForEachRemote(sync =>
            {
                var t = sync.RemoteTransform;
                if (t == null || !t.gameObject.activeInHierarchy) return;
                float d = Vector3.Distance(transform.position, t.position);
                if (d < remoteDist)
                {
                    remoteDist = d;
                    remoteT    = t;
                    remoteCId  = sync.ClientId;
                }
            });
            if (remoteT != null && remoteDist < bestDist)
            {
                bestDist     = remoteDist;
                best         = remoteT;
                bestIsLocal  = false;
                bestClientId = remoteCId;
            }

            // Vaisseaux pilotés par des joueurs distants
            foreach (var ship in SpaceShipController.AllShips)
            {
                if (ship == null || !ServerManager.IsShipPilotedByRemote(ship.ShipId)) continue;
                float d = Vector3.Distance(transform.position, ship.transform.position);
                if (d < bestDist)
                {
                    bestDist     = d;
                    best         = ship.transform;
                    bestIsLocal  = false;
                    bestClientId = ulong.MaxValue; // clientId du pilote inconnu ici
                }
            }
        }

        private void RefreshTarget(float range)
        {
            FindBestTarget(range, out var t, out var isLocal, out var cId);
            _target         = t;
            _targetIsLocal  = isLocal;
            _targetClientId = cId;
        }

        // ─────────────────────────────────────────────────────
        // Tir de missile (host)
        // ─────────────────────────────────────────────────────

        private void FireMissile()
        {
            if (_target == null) return;
            EnemySpaceShipSpawner.Instance?.SpawnMissile(
                transform.position + transform.forward * 3.5f,
                _rb.linearVelocity,
                _target,
                _targetIsLocal ? ulong.MaxValue : _targetClientId);
        }

        // ─────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────

        private Vector3 GetRandomSpacePosition()
        {
            float minDist = _atmosphereRadius + planetSafetyBuffer + 200f;
            float dist    = Mathf.Max(minDist, patrolRadius) + Random.Range(-600f, 600f);
            return _planetCenter + Random.onUnitSphere * dist;
        }

        private static bool IsHostOrStandalone()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm == null || !nm.IsListening || nm.IsHost;
        }

        // ─────────────────────────────────────────────────────
        // Visuel : mesh rouge
        // ─────────────────────────────────────────────────────

        private void BuildMesh()
        {
            var model = new GameObject("Model");
            model.transform.SetParent(transform, false);

            AddPart(model.transform, PrimitiveType.Cube,     "Hull",
                Vector3.zero,                   new Vector3(2f,   1f,   5f),
                new Color(0.72f, 0.04f, 0.04f));
            AddPart(model.transform, PrimitiveType.Cube,     "WingLeft",
                new Vector3(-3.5f, -0.2f, 0f),  new Vector3(5f,   0.18f, 2.5f),
                new Color(0.60f, 0.03f, 0.03f));
            AddPart(model.transform, PrimitiveType.Cube,     "WingRight",
                new Vector3( 3.5f, -0.2f, 0f),  new Vector3(5f,   0.18f, 2.5f),
                new Color(0.60f, 0.03f, 0.03f));
            AddPart(model.transform, PrimitiveType.Cube,     "Cockpit",
                new Vector3(0f,   0.65f, 1.5f), new Vector3(1.2f, 0.55f, 1.4f),
                new Color(0.25f, 0.00f, 0.00f));

            var eng = AddPart(model.transform, PrimitiveType.Cylinder, "Engine",
                new Vector3(0f, 0f, -2.8f),     new Vector3(0.6f, 0.75f, 0.6f),
                new Color(0.18f, 0.02f, 0.02f));
            eng.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private static GameObject AddPart(Transform parent, PrimitiveType type,
            string partName, Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = partName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = localScale;

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = CreateMaterial(color);

            return go;
        }

        private static Material CreateMaterial(Color color)
        {
            var sh  = Shader.Find("AstroVoxel/BlockUnlit")
                   ?? Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(sh);
            mat.color = color;
            return mat;
        }

        // ─────────────────────────────────────────────────────
        // Particules tuyère rouge
        // ─────────────────────────────────────────────────────

        private ParticleSystem CreateThrusterPS()
        {
            var go = new GameObject("EnemyThruster");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.back * 2.8f;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            ConfigureThrusterPS(ps);
            return ps;
        }

        private void ConfigureThrusterPS(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop            = true;
            main.playOnAwake     = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = trailMaxParticles;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(5f, 18f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            // Rouge sang → orange sombre
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 0.15f, 0.05f, 1f),
                new Color(0.8f, 0.05f, 0.00f, 0.9f));

            var em = ps.emission;
            em.rateOverTime = 0f;

            var sh = ps.shape;
            sh.enabled   = true;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle     = 20f;
            sh.radius    = 0.35f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.0f, 0.40f, 0.10f), 0.00f),
                    new GradientColorKey(new Color(1.0f, 0.05f, 0.00f), 0.30f),
                    new GradientColorKey(new Color(0.40f, 0.00f, 0.00f), 0.70f),
                    new GradientColorKey(new Color(0.05f, 0.00f, 0.00f), 1.00f),
                },
                new[]
                {
                    new GradientAlphaKey(1.0f, 0.00f),
                    new GradientAlphaKey(0.8f, 0.30f),
                    new GradientAlphaKey(0.3f, 0.70f),
                    new GradientAlphaKey(0.0f, 1.00f),
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0.00f, 0.15f, 0f, 6f),
                new Keyframe(0.12f, 1.00f, 6f, 1f),
                new Keyframe(1.00f, 3.50f, 1f, 0f)));

            var noise = ps.noise;
            noise.enabled     = true;
            noise.strength    = 0.1f;
            noise.frequency   = 0.9f;
            noise.scrollSpeed = 0.4f;
            noise.damping     = true;

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.sortingFudge = -5f;
            rend.material     = CreateThrusterMaterial();
        }

        private void UpdateThrusterTrail()
        {
            if (_thrusterPS == null) return;
            float speed = IsHostOrStandalone() ? _rb.linearVelocity.magnitude : _visualSpeed;
            float t     = Mathf.Clamp01(speed / trailMaxSpeed);

            var em = _thrusterPS.emission;
            em.rateOverTime = t * t * 220f;

            var main = _thrusterPS.main;
            main.startSpeedMultiplier    = Mathf.Lerp(0.3f, 1f, t);
            main.startLifetimeMultiplier = Mathf.Lerp(0.3f, 1f, t);
        }

        internal static Material CreateThrusterMaterial()
        {
            var sh = Shader.Find("AstroVoxel/ThrusterParticle")
                  ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Particles/Additive")
                  ?? Shader.Find("Sprites/Default");
            return new Material(sh) { name = "EnemyThruster_Auto" };
        }

        // ─────────────────────────────────────────────────────
        // Explosion
        // ─────────────────────────────────────────────────────

        public static void SpawnExplosionEffect(Vector3 position)
        {
            var go = new GameObject("EnemyExplosion");
            go.transform.position = position;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ConfigureExplosionPS(ps);
            ps.Play();
            Destroy(go, 4f);
        }

        private static void ConfigureExplosionPS(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop            = false;
            main.duration        = 0.4f;
            main.playOnAwake     = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = 900;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.5f, 2.2f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(10f, 85f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.4f, 4f);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 0.12f, 0.0f, 1f),
                new Color(0.6f, 0.00f, 0.0f, 1f));

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 900, 1, 0.05f) });

            var sh = ps.shape;
            sh.enabled   = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius    = 2f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.0f, 0.5f, 0.1f), 0.00f),
                    new GradientColorKey(new Color(0.8f, 0.1f, 0.0f), 0.30f),
                    new GradientColorKey(new Color(0.2f, 0.0f, 0.0f), 1.00f),
                },
                new[]
                {
                    new GradientAlphaKey(1.0f, 0.00f),
                    new GradientAlphaKey(0.5f, 0.50f),
                    new GradientAlphaKey(0.0f, 1.00f),
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = CreateThrusterMaterial();
        }
    }
}

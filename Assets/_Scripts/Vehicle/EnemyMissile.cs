// ============================================================
//  EnemyMissile.cs
//  Projectile guidé tiré par les vaisseaux ennemis.
//
//  Architecture multijoueur :
//    • Simulation (homing, collision) : HOST uniquement.
//    • CLIENT : reçoit pos à 10 Hz via EnemySyncManager
//      (av.missile_batch) → visuel pur, pas de dégâts.
//    • Dégâts joueur local  : PlayerHealth.TakeDamage()
//    • Dégâts joueur remote : EnemySyncManager.SendMissileHit()
//
//  SphereCollider isTrigger=true → OnTriggerEnter pour detection.
//  Fusée de proximité sur _targetTransform dans FixedUpdate.
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using AstroVoxel.Network;
using AstroVoxel.Player;

namespace AstroVoxel.Vehicle
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public sealed class EnemyMissile : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────

        [SerializeField] private int   damage        = 5;
        [SerializeField] private float speed         = 140f;
        [SerializeField] private float turnSpeed     = 95f;   // deg/s
        [SerializeField] private float hitRadius     = 3.5f;
        [SerializeField] private float lifetime      = 10f;
        [SerializeField] private float homingDuration = 4.5f;  // secondes de guidage actif

        // ── Identité ──────────────────────────────────────────

        public int  MissileId  { get; set; }
        public bool IsExploded { get; private set; }

        public static readonly List<EnemyMissile> AllMissiles = new();

        // ── État ──────────────────────────────────────────────

        private Rigidbody        _rb;
        private SphereCollider   _sphere;
        private Transform        _targetTransform;
        private ulong            _targetClientId;     // ulong.MaxValue = local player
        private float            _lifeTimer;
        private float            _homingTimer;
        private bool             _isClientVisual;
        private Collider         _sourceEnemyCollider;

        // ── Réseau (client) ───────────────────────────────────

        private Vector3 _netTargetPos;
        private bool    _hasNetTarget;

        // ─────────────────────────────────────────────────────
        // Cycle de vie
        // ─────────────────────────────────────────────────────

        private void Awake()
        {
            AllMissiles.Add(this);

            _rb = GetComponent<Rigidbody>();
            _rb.useGravity  = false;
            _rb.mass        = 10f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            _sphere           = GetComponent<SphereCollider>();
            _sphere.radius    = 0.35f;
            _sphere.isTrigger = true;
        }

        private void Start()
        {
            BuildMesh();
            CreateTrailRenderer();
        }

        private void OnDestroy()
        {
            AllMissiles.Remove(this);
        }

        // ─────────────────────────────────────────────────────
        // Initialisation
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Initialise le missile côté HOST.
        /// </summary>
        public void Initialize(Vector3 startVelocity,
                               Transform target,
                               ulong targetClientId,
                               Collider sourceCollider)
        {
            _targetTransform  = target;
            _targetClientId   = targetClientId;
            _lifeTimer        = lifetime;
            _homingTimer      = homingDuration;
            _isClientVisual   = false;

            _rb.linearVelocity = startVelocity.magnitude > 1f
                ? startVelocity.normalized * speed
                : transform.forward * speed;

            // Ne pas percuter le vaisseau source
            _sourceEnemyCollider = sourceCollider;
            if (_sourceEnemyCollider != null)
                UnityEngine.Physics.IgnoreCollision(_sphere, _sourceEnemyCollider, true);
        }

        /// <summary>
        /// Initialise le missile côté CLIENT (visuel pur).
        /// </summary>
        public void InitializeClientVisual(Vector3 startPos, Vector3 startVel)
        {
            _isClientVisual = true;
            _lifeTimer      = lifetime;

            _rb.isKinematic      = true;
            _sphere.enabled      = false;
            transform.position   = startPos;

            if (startVel.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(startVel.normalized);
        }

        // ─────────────────────────────────────────────────────
        // Simulation (host uniquement)
        // ─────────────────────────────────────────────────────

        private void FixedUpdate()
        {
            if (IsExploded) return;

            _lifeTimer -= Time.fixedDeltaTime;
            if (_lifeTimer <= 0f) { Explode(false); return; }

            if (_isClientVisual)
            {
                // Client : interpoler vers position réseau
                if (_hasNetTarget)
                    transform.position = Vector3.Lerp(
                        transform.position, _netTargetPos, Time.fixedDeltaTime * 25f);
                return;
            }

            // HOST
            HomingUpdate();

            // Fusée de proximité
            if (_targetTransform != null)
            {
                float dist = Vector3.Distance(transform.position, _targetTransform.position);
                if (dist <= hitRadius)
                {
                    DealDamage();
                    Explode(true);
                }
            }
        }

        private void HomingUpdate()
        {
            // Guidage désactivé après homingDuration secondes → le missile vole en ligne droite
            if (_homingTimer <= 0f) return;
            _homingTimer -= Time.fixedDeltaTime;

            if (_targetTransform == null) return;

            Vector3 toTarget  = (_targetTransform.position - transform.position).normalized;
            Vector3 currentVel = _rb.linearVelocity;
            float   currentSpd = currentVel.magnitude;

            Vector3 newDir = Vector3.RotateTowards(
                currentVel.normalized, toTarget,
                turnSpeed * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);

            float finalSpeed   = Mathf.Max(currentSpd, speed);
            _rb.linearVelocity = newDir * finalSpeed;

            if (newDir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(newDir);
        }

        // ─────────────────────────────────────────────────────
        // Trigger (host)
        // ─────────────────────────────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            if (IsExploded || _isClientVisual) return;
            if (!IsHostOrStandalone())          return;

            // Ignorer ennemis et missiles alliés
            if (other.GetComponentInParent<EnemySpaceShipController>() != null) return;
            if (other.GetComponentInParent<EnemyMissile>() != null)             return;

            // Joueur local à pied
            var health = other.GetComponentInParent<PlayerHealth>();
            if (health != null)
            {
                health.TakeDamage(damage);
                Explode(true);
                return;
            }

            // Vaisseau piloté localement
            var ship = other.GetComponentInParent<SpaceShipController>();
            if (ship != null && ship.IsPiloting)
            {
                PlayerHealth.Instance?.TakeDamage(damage);
                Explode(true);
                return;
            }

            // Terrain ou autre → explose sans dégâts
            Explode(false);
        }

        // ─────────────────────────────────────────────────────
        // Dégâts & explosion (host)
        // ─────────────────────────────────────────────────────

        private void DealDamage()
        {
            if (_targetClientId == ulong.MaxValue)
            {
                // Cible locale (joueur ou vaisseau local)
                var ship = _targetTransform != null
                    ? _targetTransform.GetComponentInParent<SpaceShipController>()
                    : null;
                if (ship != null && ship.IsPiloting)
                    PlayerHealth.Instance?.TakeDamage(damage);
                else
                    PlayerHealth.Instance?.TakeDamage(damage);
            }
            else
            {
                // Cible réseau → envoyer dégâts au client concerné
                EnemySyncManager.Instance?.SendMissileHit(_targetClientId, damage);
            }
        }

        /// <summary>Déclenche l'explosion. Appelé en host OU depuis EnemySyncManager sur client.</summary>
        public void Explode(bool hitTarget)
        {
            if (IsExploded) return;
            IsExploded = true;

            // Notifie le sync pour que les clients détruisent leur visuel
            if (IsHostOrStandalone())
                EnemySyncManager.Instance?.OnMissileExploded(MissileId, transform.position);

            SpawnExplosionEffect(transform.position);
            Destroy(gameObject);
        }

        // ─────────────────────────────────────────────────────
        // API réseau (client)
        // ─────────────────────────────────────────────────────

        public void SetNetworkPosition(Vector3 pos)
        {
            _netTargetPos = pos;
            _hasNetTarget = true;
        }

        // ─────────────────────────────────────────────────────
        // Visuel
        // ─────────────────────────────────────────────────────

        private void BuildMesh()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "MissileBody";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale    = new Vector3(0.15f, 0.5f, 0.15f);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var col = body.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var rend = body.GetComponent<Renderer>();
            if (rend != null)
            {
                var sh  = Shader.Find("AstroVoxel/BlockUnlit")
                       ?? Shader.Find("Universal Render Pipeline/Lit");
                var mat = new Material(sh);
                mat.color = new Color(0.9f, 0.1f, 0.0f);
                rend.sharedMaterial = mat;
            }
        }

        private void CreateTrailRenderer()
        {
            var tr = gameObject.AddComponent<TrailRenderer>();
            tr.time        = 0.3f;
            tr.startWidth  = 0.18f;
            tr.endWidth    = 0.0f;
            tr.startColor  = new Color(1.0f, 0.3f, 0.05f, 1.0f);
            tr.endColor    = new Color(0.5f, 0.0f, 0.0f, 0.0f);
            tr.minVertexDistance = 0.2f;
            tr.autodestruct = false;

            var sh  = Shader.Find("AstroVoxel/ThrusterParticle")
                   ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                   ?? Shader.Find("Sprites/Default");
            if (sh != null) tr.sharedMaterial = new Material(sh);
        }

        public static void SpawnMissileExplosionFx(Vector3 position)
        {
            SpawnExplosionEffect(position);
        }

        private static void SpawnExplosionEffect(Vector3 position)
        {
            var go = new GameObject("MissileExplosion");
            go.transform.position = position;
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake     = false;
            main.loop            = false;
            main.duration        = 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = 250;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.3f, 1.0f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(5f, 40f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.2f, 1.8f);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 0.5f, 0.1f, 1f),
                new Color(0.7f, 0.1f, 0.0f, 1f));

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 250, 1, 0.05f) });

            var sh = ps.shape;
            sh.enabled   = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius    = 0.6f;

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = EnemySpaceShipController.CreateThrusterMaterial();

            ps.Play();
            Destroy(go, 2f);
        }

        // ─────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────

        private static bool IsHostOrStandalone()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm == null || !nm.IsListening || nm.IsHost;
        }
    }
}

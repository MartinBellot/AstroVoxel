// ============================================================
//  PlayerMissile.cs
//  Missile à tête chercheuse tiré par le vaisseau joueur.
//
//  Architecture miroir d'EnemyMissile (host-authoritative) :
//    • Guidage actif pendant homingDuration secondes.
//    • SphereCollider isTrigger=true → OnTriggerEnter.
//    • Fusée de proximité dans FixedUpdate.
//    • 1 missile = 1 ennemi détruit (damage = maxHealth ennemi).
//    • Multijoueur : dégâts envoyés via EnemySyncManager côté client.
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using AstroVoxel.Network;

namespace AstroVoxel.Vehicle
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public sealed class PlayerMissile : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────

        [SerializeField] private int   damage        = 100;   // one-shot (enemyMaxHealth = 100)
        [SerializeField] private float speed         = 160f;
        [SerializeField] private float turnSpeed     = 110f;  // deg/s de guidage
        [SerializeField] private float hitRadius     = 4f;
        [SerializeField] private float lifetime      = 12f;
        [SerializeField] private float homingDuration = 8f;

        // ── État ──────────────────────────────────────────────

        public bool IsExploded { get; private set; }

        public static readonly List<PlayerMissile> AllMissiles = new();

        private Rigidbody      _rb;
        private SphereCollider _sphere;
        private Transform      _target;
        private float          _lifeTimer;
        private float          _homingTimer;
        private Collider       _sourceCollider;

        // ─────────────────────────────────────────────────────
        // Cycle de vie
        // ─────────────────────────────────────────────────────

        private void Awake()
        {
            AllMissiles.Add(this);

            _rb               = GetComponent<Rigidbody>();
            _rb.useGravity    = false;
            _rb.mass          = 10f;
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
        /// Lance le missile.
        /// <paramref name="target"/> peut être null : le missile vole en ligne droite.
        /// </summary>
        public void Initialize(Vector3 startVelocity, Transform target, Collider sourceCollider)
        {
            _target         = target;
            _lifeTimer      = lifetime;
            _homingTimer    = homingDuration;
            _sourceCollider = sourceCollider;

            // On conserve la vélocité composée (vitesse vaisseau + impulsion du missile).
            // Cela garantit que le missile est toujours plus rapide que le vaisseau tireur.
            _rb.linearVelocity = startVelocity.sqrMagnitude > 1f
                ? startVelocity
                : transform.forward * speed;

            // Ne pas percuter le vaisseau d'origine
            if (_sourceCollider != null)
                UnityEngine.Physics.IgnoreCollision(_sphere, _sourceCollider, true);
        }

        // ─────────────────────────────────────────────────────
        // Simulation
        // ─────────────────────────────────────────────────────

        private void FixedUpdate()
        {
            if (IsExploded) return;

            _lifeTimer -= Time.fixedDeltaTime;
            if (_lifeTimer <= 0f) { Explode(false); return; }

            HomingUpdate();

            // Fusée de proximité
            if (_target != null)
            {
                float dist = Vector3.Distance(transform.position, _target.position);
                if (dist <= hitRadius)
                {
                    DealDamage(_target.GetComponentInParent<EnemySpaceShipController>());
                    Explode(true);
                }
            }
        }

        private void HomingUpdate()
        {
            if (_homingTimer <= 0f) return;
            _homingTimer -= Time.fixedDeltaTime;

            if (_target == null || !_target.gameObject.activeInHierarchy) return;

            Vector3 toTarget   = (_target.position - transform.position).normalized;
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
        // Trigger
        // ─────────────────────────────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            if (IsExploded) return;

            // Ignorer les missiles alliés
            if (other.GetComponentInParent<PlayerMissile>() != null) return;
            // Ignorer le vaisseau joueur source
            if (other.GetComponentInParent<SpaceShipController>() != null) return;
            // Ignorer les missiles ennemis
            if (other.GetComponentInParent<EnemyMissile>() != null) return;

            var enemy = other.GetComponentInParent<EnemySpaceShipController>();
            if (enemy != null && !enemy.IsDead)
            {
                DealDamage(enemy);
                Explode(true);
                return;
            }

            // Terrain ou autre obstacle
            Explode(false);
        }

        // ─────────────────────────────────────────────────────
        // Dégâts & explosion
        // ─────────────────────────────────────────────────────

        private void DealDamage(EnemySpaceShipController enemy)
        {
            if (enemy == null || enemy.IsDead) return;

            if (IsHostOrStandalone())
                enemy.TakeDamage(damage);
            else
                EnemySyncManager.Instance?.SendShipShoot(enemy.EnemyId, damage);
        }

        public void Explode(bool hitTarget)
        {
            if (IsExploded) return;
            IsExploded = true;

            EnemyMissile.SpawnMissileExplosionFx(transform.position);
            Destroy(gameObject);
        }

        // ─────────────────────────────────────────────────────
        // Visuel
        // ─────────────────────────────────────────────────────

        private void BuildMesh()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "PlayerMissileBody";
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
                mat.color = new Color(0.2f, 0.7f, 1.0f);  // cyan — distinct du rouge ennemi
                rend.sharedMaterial = mat;
            }
        }

        private void CreateTrailRenderer()
        {
            var tr = gameObject.AddComponent<TrailRenderer>();
            tr.time              = 0.35f;
            tr.startWidth        = 0.20f;
            tr.endWidth          = 0f;
            tr.startColor        = new Color(0.3f, 0.85f, 1.0f, 1.0f);  // cyan
            tr.endColor          = new Color(0.0f, 0.35f, 0.8f, 0.0f);
            tr.minVertexDistance = 0.2f;
            tr.autodestruct      = false;

            // AstroVoxel/ThrusterParticle est garanti inclus dans le build
            var sh = Shader.Find("AstroVoxel/ThrusterParticle")
                  ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Sprites/Default");
            if (sh != null)
            {
                var mat = new Material(sh) { name = "PlayerMissileTrail_Auto" };
                mat.SetColor("_TintColor", Color.white);   // pas de tinte orange du shader
                tr.sharedMaterial = mat;
            }
        }

        // ─────────────────────────────────────────────────────
        // Helper
        // ─────────────────────────────────────────────────────

        private static bool IsHostOrStandalone()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm == null || !nm.IsListening || nm.IsHost;
        }
    }
}

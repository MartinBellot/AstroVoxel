// ============================================================
//  EnemySpaceShipSpawner.cs
//  Gère le spawn initial et le respawn des vaisseaux ennemis.
//  Crée aussi les missiles (hôte) et les visuels clients.
//
//  Singleton — un seul par scène.
//  Actif en standalone ET en multijoueur (hôte uniquement
//  pour le gameplay ; clients reçoivent les spawns via réseau).
// ============================================================

using System.Collections;
using UnityEngine;
using AstroVoxel.Vehicle;
using AstroVoxel.Network;

namespace AstroVoxel.Space
{
    public sealed class EnemySpaceShipSpawner : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────
        public static EnemySpaceShipSpawner Instance { get; private set; }

        // ── Inspector ─────────────────────────────────────────

        [Header("Spawn")]
        [SerializeField] private int   maxEnemies    = 5;
        [SerializeField] private float spawnDistMin  = 1500f;
        [SerializeField] private float spawnDistMax  = 3500f;
        [SerializeField] private float respawnDelay  = 30f;

        // ── Références ────────────────────────────────────────

        private Transform _playerRef;

        // ── Compteurs ─────────────────────────────────────────

        private static int _nextEnemyId  = 0;
        private static int _nextMissileId = 0;

        // ─────────────────────────────────────────────────────
        // Cycle de vie
        // ─────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Init(Transform player)
        {
            _playerRef = player;
        }

        private void Start()
        {
            if (!IsHostOrStandalone()) return;
            for (int i = 0; i < maxEnemies; i++)
                SpawnEnemy();
        }

        // ─────────────────────────────────────────────────────
        // Spawn ennemi (host)
        // ─────────────────────────────────────────────────────

        private void SpawnEnemy()
        {
            Vector3    pos = GetSpawnPosition();
            Quaternion rot = Random.rotation;
            var enemy = CreateEnemy(_nextEnemyId, pos, rot);

            // Informer les clients connectés
            EnemySyncManager.Instance?.BroadcastEnemySpawn(enemy);

            // Abonnement pour respawn
            enemy.OnDied += OnEnemyDied;
        }

        private void OnEnemyDied(int enemyId)
        {
            StartCoroutine(CoRespawn());
        }

        private IEnumerator CoRespawn()
        {
            yield return new WaitForSeconds(respawnDelay);
            if (!IsHostOrStandalone()) yield break;
            SpawnEnemy();
        }

        // ─────────────────────────────────────────────────────
        // Création enemy (partagée host + client)
        // ─────────────────────────────────────────────────────

        private EnemySpaceShipController CreateEnemy(int id, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject($"EnemyShip_{id}");
            go.transform.position = pos;
            go.transform.rotation = rot;

            var ctrl    = go.AddComponent<EnemySpaceShipController>();
            ctrl.EnemyId = id;

            // Incrémente APRÈS avoir assigné, pour la cohérence
            if (_nextEnemyId <= id) _nextEnemyId = id + 1;

            return ctrl;
        }

        // ─────────────────────────────────────────────────────
        // Création enemy côté CLIENT (appelée depuis EnemySyncManager)
        // ─────────────────────────────────────────────────────

        public void CreateClientEnemy(int enemyId, Vector3 pos, Quaternion rot)
        {
            // Vérifie si l'ennemi n'existe pas déjà
            foreach (var e in EnemySpaceShipController.AllEnemies)
                if (e != null && e.EnemyId == enemyId) return;

            CreateEnemy(enemyId, pos, rot);
        }

        // ─────────────────────────────────────────────────────
        // Spawn missile (host)
        // ─────────────────────────────────────────────────────

        public void SpawnMissile(Vector3 spawnPos, Vector3 sourceVelocity,
                                 Transform target, ulong targetClientId)
        {
            if (!IsHostOrStandalone()) return;

            var go      = new GameObject($"EnemyMissile_{_nextMissileId}");
            go.transform.position = spawnPos;
            if (sourceVelocity.sqrMagnitude > 0.001f)
                go.transform.rotation = Quaternion.LookRotation(sourceVelocity.normalized);

            var missile      = go.AddComponent<EnemyMissile>();
            missile.MissileId = _nextMissileId;
            _nextMissileId++;

            // Collider du vaisseau source pour ignorer la collision immédiate
            Collider sourceCol = null;
            foreach (var e in EnemySpaceShipController.AllEnemies)
            {
                if (e != null && Vector3.Distance(e.transform.position, spawnPos) < 10f)
                {
                    sourceCol = e.GetComponent<Collider>();
                    break;
                }
            }

            missile.Initialize(sourceVelocity, target, targetClientId, sourceCol);

            // Broadcast aux clients
            EnemySyncManager.Instance?.BroadcastMissileSpawn(missile);
        }

        // ─────────────────────────────────────────────────────
        // Création missile côté CLIENT (visuel)
        // ─────────────────────────────────────────────────────

        public void CreateClientMissile(int missileId, Vector3 pos, Vector3 vel)
        {
            // Vérifier si pas déjà créé
            foreach (var m in EnemyMissile.AllMissiles)
                if (m != null && m.MissileId == missileId) return;

            var go = new GameObject($"EnemyMissile_{missileId}");
            var missile      = go.AddComponent<EnemyMissile>();
            missile.MissileId = missileId;
            missile.InitializeClientVisual(pos, vel);
        }

        // ─────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────

        private Vector3 GetSpawnPosition()
        {
            // Spawn hors atmosphère
            Vector3 dir  = Random.onUnitSphere;
            float   dist = Random.Range(spawnDistMin, spawnDistMax);
            return dir * dist;   // planète à Vector3.zero
        }

        private static bool IsHostOrStandalone()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm == null || !nm.IsListening || nm.IsHost;
        }
    }
}

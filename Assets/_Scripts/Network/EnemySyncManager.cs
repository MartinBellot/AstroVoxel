// ============================================================
//  EnemySyncManager.cs
//  Synchronisation réseau de tous les vaisseaux ennemis et
//  missiles via Unity Netcode (CustomMessagingManager).
//
//  Canaux de messages :
//    av.enemy_spawn    — host → client(s)   : spawn ennemi
//    av.enemy_destroy  — host → tous        : mort ennemi + pos explosion
//    av.enemy_batch    — host → tous        : positions (20 Hz)
//    av.missile_spawn  — host → tous        : nouveau missile
//    av.missile_batch  — host → tous        : positions missiles (10 Hz)
//    av.missile_destroy— host → tous        : missile détruit + pos
//    av.missile_hit    — host → clientX     : dégâts au joueur distant
//    av.ship_shoot     — client → host      : joueur distant touche ennemi
//
//  Singleton — créé par GameBootstrap.
//  RegisterHandlers() appelé depuis ServerManager.RegisterHandlers().
// ============================================================

using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using AstroVoxel.Vehicle;
using AstroVoxel.Space;

namespace AstroVoxel.Network
{
    public sealed class EnemySyncManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────
        public static EnemySyncManager Instance { get; private set; }

        // ── Canaux de messages ────────────────────────────────
        internal const string MsgEnemySpawn    = "av.enemy_spawn";
        internal const string MsgEnemyDestroy  = "av.enemy_destroy";
        internal const string MsgEnemyBatch    = "av.enemy_batch";
        internal const string MsgMissileSpawn  = "av.missile_spawn";
        internal const string MsgMissileBatch  = "av.missile_batch";
        internal const string MsgMissileDestroy= "av.missile_destroy";
        internal const string MsgMissileHit    = "av.missile_hit";
        internal const string MsgShipShoot     = "av.ship_shoot";

        // ── Timers sync ───────────────────────────────────────
        private float _enemySyncTimer;
        private float _missileSyncTimer;
        private const float EnemySyncInterval   = 0.05f;   // 20 Hz
        private const float MissileSyncInterval = 0.10f;   // 10 Hz

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

        private void Update()
        {
            if (!ServerManager.IsHost) return;
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;
            // Ne broadcaster que s'il y a des clients connectés
            if (nm.ConnectedClientsIds.Count <= 1) return; // host seul = pas besoin

            _enemySyncTimer += Time.deltaTime;
            if (_enemySyncTimer >= EnemySyncInterval)
            {
                _enemySyncTimer = 0f;
                BroadcastEnemyPositions(nm);
            }

            _missileSyncTimer += Time.deltaTime;
            if (_missileSyncTimer >= MissileSyncInterval)
            {
                _missileSyncTimer = 0f;
                BroadcastMissilePositions(nm);
            }
        }

        // ─────────────────────────────────────────────────────
        // Enregistrement des handlers réseau
        // ─────────────────────────────────────────────────────

        public void RegisterHandlers()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            var cmm = nm.CustomMessagingManager;

            if (nm.IsServer)
            {
                // HOST reçoit les tirs des clients
                cmm.RegisterNamedMessageHandler(MsgShipShoot, HandleShipShootFromClient);
            }
            else
            {
                // CLIENT reçoit tout l'état ennemi
                cmm.RegisterNamedMessageHandler(MsgEnemySpawn,     HandleEnemySpawn);
                cmm.RegisterNamedMessageHandler(MsgEnemyDestroy,   HandleEnemyDestroy);
                cmm.RegisterNamedMessageHandler(MsgEnemyBatch,     HandleEnemyBatch);
                cmm.RegisterNamedMessageHandler(MsgMissileSpawn,   HandleMissileSpawn);
                cmm.RegisterNamedMessageHandler(MsgMissileBatch,   HandleMissileBatch);
                cmm.RegisterNamedMessageHandler(MsgMissileDestroy, HandleMissileDestroy);
                cmm.RegisterNamedMessageHandler(MsgMissileHit,     HandleMissileHit);
            }
        }

        // ─────────────────────────────────────────────────────
        // Envoi — spawn ennemi
        // ─────────────────────────────────────────────────────

        /// <summary>Broadcast à tous les clients (nouveau spawn).</summary>
        public void BroadcastEnemySpawn(EnemySpaceShipController enemy)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;

            using var w = new FastBufferWriter(36, Allocator.Temp);
            w.WriteValueSafe(enemy.EnemyId);
            w.WriteValueSafe(enemy.transform.position);
            w.WriteValueSafe(enemy.transform.rotation);
            nm.CustomMessagingManager.SendNamedMessageToAll(
                MsgEnemySpawn, w, NetworkDelivery.ReliableSequenced);
        }

        /// <summary>Envoi ciblé vers un nouveau client (onboarding).</summary>
        public void SendEnemyStateToNewClient(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            foreach (var enemy in EnemySpaceShipController.AllEnemies)
            {
                if (enemy == null || enemy.IsDead || !enemy.gameObject.activeInHierarchy) continue;

                using var w = new FastBufferWriter(36, Allocator.Temp);
                w.WriteValueSafe(enemy.EnemyId);
                w.WriteValueSafe(enemy.transform.position);
                w.WriteValueSafe(enemy.transform.rotation);
                nm.CustomMessagingManager.SendNamedMessage(
                    MsgEnemySpawn, clientId, w, NetworkDelivery.ReliableSequenced);
            }

            // Missiles en vol
            foreach (var missile in EnemyMissile.AllMissiles)
            {
                if (missile == null || missile.IsExploded || !missile.gameObject.activeInHierarchy) continue;

                var rb  = missile.GetComponent<Rigidbody>();
                var vel = rb != null ? rb.linearVelocity : Vector3.zero;

                using var w = new FastBufferWriter(28, Allocator.Temp);
                w.WriteValueSafe(missile.MissileId);
                w.WriteValueSafe(missile.transform.position);
                w.WriteValueSafe(vel);
                nm.CustomMessagingManager.SendNamedMessage(
                    MsgMissileSpawn, clientId, w, NetworkDelivery.ReliableSequenced);
            }
        }

        // ─────────────────────────────────────────────────────
        // Envoi — mort ennemi
        // ─────────────────────────────────────────────────────

        public void BroadcastEnemyDestroy(int enemyId, Vector3 explosionPos)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;

            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe(enemyId);
            w.WriteValueSafe(explosionPos);
            nm.CustomMessagingManager.SendNamedMessageToAll(
                MsgEnemyDestroy, w, NetworkDelivery.ReliableSequenced);
        }

        // ─────────────────────────────────────────────────────
        // Envoi — spawn missile
        // ─────────────────────────────────────────────────────

        public void BroadcastMissileSpawn(EnemyMissile missile)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;

            var rb  = missile.GetComponent<Rigidbody>();
            var vel = rb != null ? rb.linearVelocity : Vector3.zero;

            using var w = new FastBufferWriter(28, Allocator.Temp);
            w.WriteValueSafe(missile.MissileId);
            w.WriteValueSafe(missile.transform.position);
            w.WriteValueSafe(vel);
            nm.CustomMessagingManager.SendNamedMessageToAll(
                MsgMissileSpawn, w, NetworkDelivery.ReliableSequenced);
        }

        // ─────────────────────────────────────────────────────
        // Envoi — destruction missile
        // ─────────────────────────────────────────────────────

        public void OnMissileExploded(int missileId, Vector3 pos)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;

            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe(missileId);
            w.WriteValueSafe(pos);
            nm.CustomMessagingManager.SendNamedMessageToAll(
                MsgMissileDestroy, w, NetworkDelivery.ReliableSequenced);
        }

        // ─────────────────────────────────────────────────────
        // Envoi — dégâts joueur distant
        // ─────────────────────────────────────────────────────

        public void SendMissileHit(ulong targetClientId, int damage)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;

            using var w = new FastBufferWriter(4, Allocator.Temp);
            w.WriteValueSafe(damage);
            nm.CustomMessagingManager.SendNamedMessage(
                MsgMissileHit, targetClientId, w, NetworkDelivery.Reliable);
        }

        // ─────────────────────────────────────────────────────
        // Envoi — tir joueur distant sur ennemi (client → host)
        // ─────────────────────────────────────────────────────

        public void SendShipShoot(int enemyId, int damage)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;

            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(enemyId);
            w.WriteValueSafe(damage);
            nm.CustomMessagingManager.SendNamedMessage(
                MsgShipShoot, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }

        // ─────────────────────────────────────────────────────
        // Broadcast positions (20 Hz ennemis, 10 Hz missiles)
        // ─────────────────────────────────────────────────────

        private static void BroadcastEnemyPositions(NetworkManager nm)
        {
            var enemies = EnemySpaceShipController.AllEnemies;
            int count   = 0;
            foreach (var e in enemies)
                if (e != null && !e.IsDead && e.gameObject.activeInHierarchy) count++;
            if (count == 0) return;

            // 4 + count * (4+12+16) = 4 + count * 32
            using var w = new FastBufferWriter(4 + count * 32 + 8, Allocator.Temp);
            w.WriteValueSafe(count);
            foreach (var e in enemies)
            {
                if (e == null || e.IsDead || !e.gameObject.activeInHierarchy) continue;
                w.WriteValueSafe(e.EnemyId);
                w.WriteValueSafe(e.transform.position);
                w.WriteValueSafe(e.transform.rotation);
            }
            nm.CustomMessagingManager.SendNamedMessageToAll(
                MsgEnemyBatch, w, NetworkDelivery.Unreliable);
        }

        private static void BroadcastMissilePositions(NetworkManager nm)
        {
            var missiles = EnemyMissile.AllMissiles;
            int count    = 0;
            foreach (var m in missiles)
                if (m != null && !m.IsExploded && m.gameObject.activeInHierarchy) count++;
            if (count == 0) return;

            // 4 + count * (4+12) = 4 + count * 16
            using var w = new FastBufferWriter(4 + count * 16 + 8, Allocator.Temp);
            w.WriteValueSafe(count);
            foreach (var m in missiles)
            {
                if (m == null || m.IsExploded || !m.gameObject.activeInHierarchy) continue;
                w.WriteValueSafe(m.MissileId);
                w.WriteValueSafe(m.transform.position);
            }
            nm.CustomMessagingManager.SendNamedMessageToAll(
                MsgMissileBatch, w, NetworkDelivery.Unreliable);
        }

        // ─────────────────────────────────────────────────────
        // Handlers (côté CLIENT)
        // ─────────────────────────────────────────────────────

        private void HandleEnemySpawn(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int      enemyId);
            reader.ReadValueSafe(out Vector3  pos);
            reader.ReadValueSafe(out Quaternion rot);

            EnemySpaceShipSpawner.Instance?.CreateClientEnemy(enemyId, pos, rot);
        }

        private void HandleEnemyDestroy(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int     enemyId);
            reader.ReadValueSafe(out Vector3 explosionPos);

            // Explosion visuelle sur le client
            EnemySpaceShipController.SpawnExplosionEffect(explosionPos);

            // Supprimer le GO ennemi
            foreach (var e in EnemySpaceShipController.AllEnemies)
            {
                if (e != null && e.EnemyId == enemyId)
                {
                    Destroy(e.gameObject);
                    return;
                }
            }
        }

        private void HandleEnemyBatch(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int count);
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out int      enemyId);
                reader.ReadValueSafe(out Vector3  pos);
                reader.ReadValueSafe(out Quaternion rot);

                foreach (var e in EnemySpaceShipController.AllEnemies)
                {
                    if (e != null && e.EnemyId == enemyId)
                    {
                        e.SetNetworkTarget(pos, rot);
                        break;
                    }
                }
            }
        }

        private void HandleMissileSpawn(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int     missileId);
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out Vector3 vel);

            EnemySpaceShipSpawner.Instance?.CreateClientMissile(missileId, pos, vel);
        }

        private void HandleMissileBatch(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int count);
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out int     missileId);
                reader.ReadValueSafe(out Vector3 pos);

                foreach (var m in EnemyMissile.AllMissiles)
                {
                    if (m != null && m.MissileId == missileId)
                    {
                        m.SetNetworkPosition(pos);
                        break;
                    }
                }
            }
        }

        private void HandleMissileDestroy(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int     missileId);
            reader.ReadValueSafe(out Vector3 pos);

            // Explosion visuelle
            EnemyMissile.SpawnMissileExplosionFx(pos);

            // Supprimer le visuel missile
            foreach (var m in EnemyMissile.AllMissiles)
            {
                if (m != null && m.MissileId == missileId)
                {
                    Destroy(m.gameObject);
                    return;
                }
            }
        }

        private void HandleMissileHit(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int damage);
            var health = AstroVoxel.Player.PlayerHealth.Instance;
            if (health == null) return;
            // Dégâts progressifs : pas d'instant kill sauf si damage ≥ HP restants
            if (damage >= health.CurrentHealth)
                health.KillWithMessage("Touché par un missile ennemi !");
            else
                health.TakeDamage(damage);
        }

        // ── Handler host — tir d'un joueur distant ────────────

        private void HandleShipShootFromClient(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int enemyId);
            reader.ReadValueSafe(out int damage);

            foreach (var e in EnemySpaceShipController.AllEnemies)
            {
                if (e != null && e.EnemyId == enemyId)
                {
                    e.TakeDamage(damage);
                    return;
                }
            }
        }
    }
}

// ============================================================
//  BlockSyncManager.cs
//  Synchronise les break/place de blocs entre host et clients.
//
//  Protocole :
//   - CLIENT brise/pose → envoie "av.blocks" au serveur (batch)
//   - SERVEUR valide    → applique localement
//                       → broadcast "av.blocks" à tous les clients
//   - CLIENT reçoit broadcast → si senderId != soi → applique
//
//  Batching : accumule jusqu'à 32 changements, flush à 20 Hz.
//  Résultat : ~640 bytes/s max au lieu d'un paquet par bloc.
// ============================================================

using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using AstroVoxel.VoxelEngine;

namespace AstroVoxel.Network
{
    /// <summary>
    /// Gère la synchronisation réseau des modifications de blocs.
    /// Créé par <see cref="AstroVoxel.Bootstrap.GameBootstrap"/>.
    /// </summary>
    public sealed class BlockSyncManager : MonoBehaviour
    {
        public static BlockSyncManager Instance { get; private set; }

        private PlanetWorld _world;

        // Buffer côté client : blocs en attente d'envoi au serveur
        private readonly List<BlockChangeData> _outboundQueue = new(32);
        private float _flushTimer;
        private const float FlushInterval = 0.05f; // 20 Hz

        // ── Cycle de vie ──────────────────────────────────────

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
            if (!ServerManager.IsNetworkActive || _outboundQueue.Count == 0) return;
            _flushTimer += Time.deltaTime;
            if (_flushTimer >= FlushInterval) FlushOutbound();
        }

        public void SetWorld(PlanetWorld world)
        {
            _world = world;
            // Le handler MsgBlocks est enregistré après démarrage réseau
            // via RegisterBroadcastHandler() appelé par ServerManager.RegisterHandlers().
        }

        /// <summary>Enregistre le handler côté client (broadcast du serveur).</summary>
        internal void RegisterBroadcastHandler()
        {
            NetworkManager.Singleton?.CustomMessagingManager
                ?.RegisterNamedMessageHandler(ServerManager.MsgBlocks, HandleBlocksBroadcast);
        }

        // ── API publique (appelée par BlockInteraction) ───────

        /// <summary>Demande la casse d'un bloc. Routé via réseau si actif.</summary>
        public void RequestBreakBlock(ChunkRenderer cr, Vector3 worldPos)
        {
            if (!ServerManager.IsNetworkActive)
            {
                _world?.BreakBlock(cr, worldPos);
                return;
            }

            if (ServerManager.IsHost)
            {
                // Host : applique directement + broadcast
                BlockChangeData d = default;
                bool ok = _world != null && ApplyBreak(cr, worldPos, out d);
                if (ok) BroadcastToClients(d);
            }
            else
            {
                // Client : optimistic apply + enqueue pour envoi serveur
                if (BuildBreakData(cr, worldPos, out BlockChangeData d))
                {
                    ApplyLocally(d);
                    _outboundQueue.Add(d);
                    if (_outboundQueue.Count >= 32) FlushOutbound();
                }
            }
        }

        /// <summary>Demande la pose d'un bloc. Routé via réseau si actif.</summary>
        public void RequestPlaceBlock(Vector3 worldPos, BlockType type)
        {
            if (!ServerManager.IsNetworkActive)
            {
                _world?.PlaceBlock(worldPos, type);
                return;
            }

            if (ServerManager.IsHost)
            {
                BlockChangeData d = default;
                bool ok = _world != null && ApplyPlace(worldPos, type, out d);
                if (ok) BroadcastToClients(d);
            }
            else
            {
                if (BuildPlaceData(worldPos, type, out BlockChangeData d))
                {
                    ApplyLocally(d);
                    _outboundQueue.Add(d);
                    if (_outboundQueue.Count >= 32) FlushOutbound();
                }
            }
        }

        // ── Flush (client → serveur) ──────────────────────────

        private void FlushOutbound()
        {
            _flushTimer = 0f;
            if (_outboundQueue.Count == 0) return;

            int count   = _outboundQueue.Count;
            int bufSize = 4 + count * 20 + 8;
            var w = new FastBufferWriter(bufSize, Allocator.Temp);
            w.WriteValueSafe(count);
            foreach (var d in _outboundQueue) BlockChangeData.Write(ref w, d);
            _outboundQueue.Clear();

            NetworkManager.Singleton?.CustomMessagingManager.SendNamedMessage(
                ServerManager.MsgBlocks,
                NetworkManager.ServerClientId,
                w,
                NetworkDelivery.ReliableSequenced);
            w.Dispose();
        }

        // ── Broadcast (serveur → tous clients) ────────────────

        private static void BroadcastToClients(BlockChangeData d)
        {
            var w = new FastBufferWriter(4 + 20 + 8, Allocator.Temp);
            w.WriteValueSafe(1);
            BlockChangeData.Write(ref w, d);
            NetworkManager.Singleton?.CustomMessagingManager.SendNamedMessageToAll(
                ServerManager.MsgBlocks, w, NetworkDelivery.ReliableSequenced);
            w.Dispose();
        }

        // ── Handler broadcast (reçu par les clients) ──────────

        private void HandleBlocksBroadcast(ulong senderId, FastBufferReader reader)
        {
            // Ce handler est appelé sur TOUS les peers, y compris le host.
            // Le host a déjà appliqué, et les clients qui ont appliqué
            // de façon optimiste skippent via senderId.
            var nm = NetworkManager.Singleton;
            if (nm == null || _world == null) return;

            reader.ReadValueSafe(out int count);
            ulong myId = nm.LocalClientId;

            for (int i = 0; i < count; i++)
            {
                BlockChangeData.Read(ref reader, out var d);
                // Host : déjà appliqué
                if (nm.IsHost) continue;
                // Client auteur : déjà appliqué de façon optimiste
                if (d.senderId == myId) continue;
                ApplyLocally(d);
            }
        }

        // ── Apply helpers ─────────────────────────────────────

        private bool ApplyBreak(ChunkRenderer cr, Vector3 worldPos, out BlockChangeData d)
        {
            d = default;
            if (!BuildBreakData(cr, worldPos, out d)) return false;
            return _world.ApplyNetworkBreak(d.ToCoord(), d.lx, d.ly, d.lz);
        }

        private bool ApplyPlace(Vector3 worldPos, BlockType type, out BlockChangeData d)
        {
            d = default;
            if (!BuildPlaceData(worldPos, type, out d)) return false;
            return _world.ApplyNetworkPlace(d.ToCoord(), d.lx, d.ly, d.lz, type);
        }

        private void ApplyLocally(BlockChangeData d)
        {
            if (_world == null) return;
            var coord = d.ToCoord();
            if (d.isBreak == 1)
                _world.ApplyNetworkBreak(coord, d.lx, d.ly, d.lz);
            else
                _world.ApplyNetworkPlace(coord, d.lx, d.ly, d.lz, (BlockType)d.blockType);
        }

        // ── Build data helpers ────────────────────────────────

        private bool BuildBreakData(ChunkRenderer cr, Vector3 worldPos, out BlockChangeData d)
        {
            d = default;
            FaceChunkCoord coord;
            int lx, ly, lz;

            if (cr != null)
            {
                coord = cr.ChunkCoord;
                Vector3 local = cr.transform.InverseTransformPoint(worldPos);
                lx = Mathf.Clamp(Mathf.FloorToInt(local.x), 0, VoxelData.ChunkWidth  - 1);
                ly = Mathf.Clamp(Mathf.FloorToInt(local.y), 0, VoxelData.ChunkHeight - 1);
                lz = Mathf.Clamp(Mathf.FloorToInt(local.z), 0, VoxelData.ChunkWidth  - 1);
            }
            else if (_world != null)
            {
                coord = _world.WorldToFaceChunk(worldPos);
                var lb = _world.WorldToLocalBlock(worldPos);
                lx = lb.x; ly = lb.y; lz = lb.z;
            }
            else return false;

            ulong myId = NetworkManager.Singleton?.LocalClientId ?? ulong.MaxValue;
            d = new BlockChangeData
            {
                senderId  = myId,
                face      = (byte)coord.Face,
                chunkU    = (short)coord.U,
                chunkV    = (short)coord.V,
                chunkR    = (short)coord.R,
                lx        = (byte)lx,
                ly        = (byte)ly,
                lz        = (byte)lz,
                blockType = (byte)BlockType.Air,
                isBreak   = 1
            };
            return true;
        }

        private bool BuildPlaceData(Vector3 worldPos, BlockType type, out BlockChangeData d)
        {
            d = default;
            if (_world == null) return false;
            var coord = _world.WorldToFaceChunk(worldPos);
            var lb    = _world.WorldToLocalBlock(worldPos);
            ulong myId = NetworkManager.Singleton?.LocalClientId ?? ulong.MaxValue;
            d = new BlockChangeData
            {
                senderId  = myId,
                face      = (byte)coord.Face,
                chunkU    = (short)coord.U,
                chunkV    = (short)coord.V,
                chunkR    = (short)coord.R,
                lx        = (byte)lb.x,
                ly        = (byte)lb.y,
                lz        = (byte)lb.z,
                blockType = (byte)type,
                isBreak   = 0
            };
            return true;
        }
    }
}

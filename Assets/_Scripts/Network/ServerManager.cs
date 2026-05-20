// ============================================================
//  ServerManager.cs
//  Gère le host / join multi-joueur via Unity Transport (LAN).
//
//  /server host → démarre le host, affiche un code de 10 chars
//  /server join CODE → connecte en client
//
//  Protocole de synchronisation à la connexion :
//   1. Serveur → client : "av.seed"       (int seed)
//   2. Client vérifie seed ; si différente → reload scène + reconnect
//   3. Serveur → client : "av.world_mod"  (batches de BlockChangeData)
//   4. Serveur → client : "av.world_done" (fin)
//   5. Sync continue via BlockSyncManager (blocs en temps réel)
//   6. Sync vaisseau : "av.ship_pos" (20 Hz, host → tous clients)
// ============================================================

using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using AstroVoxel.Space;
using AstroVoxel.Vehicle;
using AstroVoxel.VoxelEngine;

namespace AstroVoxel.Network
{
    /// <summary>
    /// Singleton MonoBehaviour : point d'entrée du système multijoueur.
    /// Créé par <see cref="AstroVoxel.Bootstrap.GameBootstrap"/>.
    /// </summary>
    public sealed class ServerManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────
        public static ServerManager Instance { get; private set; }

        /// <summary>Code de reconnexion persistant entre rechargements de scène.</summary>
        public static string PendingJoinCode { get; set; }

        // ── État réseau ───────────────────────────────────────
        public static bool IsNetworkActive =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        public static bool IsHost =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        // ── Événements (utilisés par GameConsole) ─────────────
        public event Action<string> OnHostReady;
        public event Action<string> OnStatusMessage;
        public event Action<string> OnError;

        // ── Références scène ──────────────────────────────────
        private PlanetWorld          _world;
        private SpaceShipController  _ship;
        private string               _currentCode;

        // ── Canaux de messages ────────────────────────────────
        internal const string MsgSeed      = "av.seed";
        internal const string MsgWorldMod  = "av.world_mod";
        internal const string MsgWorldDone = "av.world_done";
        internal const string MsgBlocks    = "av.blocks";
        internal const string MsgShipPos    = "av.ship_pos";
        internal const string MsgShipReq    = "av.ship_req";
        internal const string MsgPlayerPos  = "av.player_pos";
        internal const string MsgPlayerJoin = "av.player_join";
        internal const string MsgPlayerLeave = "av.player_leave";
        internal const string MsgPlayerList  = "av.player_list";

        // ── Encode/Decode (base36, 10 chars = IPv4 + port) ───
        private const ushort Port  = 7777;
        private const string B36   = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        // ── Ship sync ─────────────────────────────────────────
        private float      _shipSyncTimer;
        private float      _lastShipPosReceived = -100f;
        private const float ShipSyncInterval    = 0.05f; // 20 Hz

        // Cible d'interpolation (mis à jour dans les handlers, consommé dans Update)
        private Vector3    _targetShipPos;
        private Quaternion _targetShipRot       = Quaternion.identity;
        private bool       _hasRemoteShipTarget;

        /// <summary>True quand un autre joueur (distant) pilote le vaisseau.</summary>
        public static bool IsShipPilotedByRemote =>
            IsNetworkActive && !IsHost && Instance != null
            && (Time.time - Instance._lastShipPosReceived) < 2f;

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

        private void OnApplicationQuit()
        {
            // Force la fermeture du socket UDP avant que le process se termine.
            // Sans ça, le port 7777 reste occupé entre deux sessions Play Mode.
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
                nm.Shutdown();
        }

        private void Update()
        {
            if (!IsNetworkActive) return;
            if (_ship != null) SyncShipIfPiloting();

            // Interpolation fluide du vaisseau quand piloté par un joueur distant
            // (lerp chaque frame à 60 Hz au lieu de seulement lors de la réception du paquet)
            if (_hasRemoteShipTarget && _ship != null && !_ship.IsPiloting)
            {
                _ship.transform.position = Vector3.Lerp(
                    _ship.transform.position, _targetShipPos, Time.deltaTime * 25f);
                _ship.transform.rotation = Quaternion.Slerp(
                    _ship.transform.rotation, _targetShipRot, Time.deltaTime * 25f);
            }
        }

        // ── Init ─────────────────────────────────────────────

        public void SetWorld(PlanetWorld world) => _world = world;
        public void SetShip(SpaceShipController ship) => _ship = ship;

        // ── Gestion des syncs joueur ──────────────────────────

        private static PlayerNetworkSync CreatePlayerSync(ulong clientId, bool isLocal)
        {
            var go   = new GameObject(isLocal ? "PlayerNet_Local" : $"PlayerNet_{clientId}");
            var sync = go.AddComponent<PlayerNetworkSync>();
            sync.Init(clientId, isLocal);
            return sync;
        }

        private static void RemovePlayerSync(ulong clientId)
        {
            PlayerNetworkSync.GetById(clientId)?.Cleanup();
        }

        // ── Host ─────────────────────────────────────────────

        public void HostServer()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)        { OnError?.Invoke("NetworkManager introuvable."); return; }
            if (nm.IsListening)    { OnError?.Invoke("Déjà en ligne."); return; }

            string ip = GetLocalIP();
            if (ip == null) { OnError?.Invoke("IP locale introuvable (pas de réseau ?)."); return; }

            var transport = nm.GetComponent<UnityTransport>();
            if (transport == null) { OnError?.Invoke("UnityTransport introuvable."); return; }

            transport.SetConnectionData(ip, Port);

            nm.OnClientConnectedCallback  += OnClientConnectedAsHost;
            nm.OnClientDisconnectCallback += OnClientDisconnected;

            if (!nm.StartHost())
            {
                OnError?.Invoke("Échec du démarrage (port occupé ?).");
                return;
            }

            RegisterHandlers();

            // Créer le sync local du host
            CreatePlayerSync(nm.LocalClientId, isLocal: true);

            _currentCode = EncodeIP(ip, Port);
            OnHostReady?.Invoke(_currentCode);
        }

        // ── Join ─────────────────────────────────────────────

        public void JoinServer(string code)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)     { OnError?.Invoke("NetworkManager introuvable."); return; }
            if (nm.IsListening) { OnError?.Invoke("Déjà connecté."); return; }

            code = (code ?? "").Trim().ToUpperInvariant();
            if (!DecodeIP(code, out string ip, out ushort port))
            {
                OnError?.Invoke($"Code invalide : {code}  (10 caractères alphanumériques)");
                return;
            }

            var transport = nm.GetComponent<UnityTransport>();
            if (transport == null) { OnError?.Invoke("UnityTransport introuvable."); return; }

            transport.SetConnectionData(ip, port);
            _currentCode = code;

            nm.OnClientConnectedCallback  += OnClientConnectedAsClient;
            nm.OnClientDisconnectCallback += OnClientDisconnected;

            if (!nm.StartClient())
            {
                OnError?.Invoke($"Échec de la connexion à {ip}:{port}.");
                return;
            }

            RegisterHandlers();

            OnStatusMessage?.Invoke($"Connexion à {ip}:{port}…");
        }

        public void Disconnect()
        {
            PlayerNetworkSync.DestroyAll();
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;
            nm.OnClientConnectedCallback  -= OnClientConnectedAsHost;
            nm.OnClientConnectedCallback  -= OnClientConnectedAsClient;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
            nm.Shutdown();
            _currentCode = null;
        }

        // ── Callbacks connexion ───────────────────────────────

        private void OnClientConnectedAsHost(ulong clientId)
        {
            if (clientId == NetworkManager.Singleton.LocalClientId) return;
            var nm = NetworkManager.Singleton;

            // Créer le sync distant pour le nouveau client (côté host)
            CreatePlayerSync(clientId, isLocal: false);

            // Envoyer la liste des joueurs déjà connectés au nouveau client
            // (tous sauf lui-même)
            var existingIds = new System.Collections.Generic.List<ulong>();
            foreach (var cid in nm.ConnectedClientsIds)
            {
                if (cid != clientId)
                    existingIds.Add(cid);
            }
            if (existingIds.Count > 0)
            {
                using var listW = new FastBufferWriter(4 + existingIds.Count * 8, Allocator.Temp);
                listW.WriteValueSafe(existingIds.Count);
                foreach (var cid in existingIds)
                    listW.WriteValueSafe(cid);
                nm.CustomMessagingManager.SendNamedMessage(
                    MsgPlayerList, clientId, listW, NetworkDelivery.Reliable);
            }

            // Annoncer le nouveau client aux joueurs distants déjà connectés
            {
                using var joinW = new FastBufferWriter(8, Allocator.Temp);
                joinW.WriteValueSafe(clientId);
                foreach (var cid in nm.ConnectedClientsIds)
                {
                    if (cid == clientId || cid == nm.LocalClientId) continue;
                    nm.CustomMessagingManager.SendNamedMessage(
                        MsgPlayerJoin, cid, joinW, NetworkDelivery.Reliable);
                }
            }

            // Envoyer l'état du monde au nouveau client
            StartCoroutine(CoSendWorldState(clientId));
        }

        private void OnClientConnectedAsClient(ulong clientId)
        {
            // On ne réagit qu'à notre propre connexion
            if (clientId != NetworkManager.Singleton.LocalClientId) return;
            OnStatusMessage?.Invoke("Connecté ! En attente de la seed…");
            // Créer le sync local du client
            CreatePlayerSync(clientId, isLocal: true);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            if (nm.IsServer)
            {
                // Un client s'est déconnecté : supprimer son sync et avertir les autres
                RemovePlayerSync(clientId);
                using var w = new FastBufferWriter(8, Allocator.Temp);
                w.WriteValueSafe(clientId);
                foreach (var cid in nm.ConnectedClientsIds)
                {
                    if (cid == nm.LocalClientId) continue;
                    nm.CustomMessagingManager.SendNamedMessage(
                        MsgPlayerLeave, cid, w, NetworkDelivery.Reliable);
                }
            }
            else if (clientId == nm.LocalClientId)
            {
                // Déconnexion propre du client
                OnStatusMessage?.Invoke("Déconnecté du serveur.");
            }
        }

        // ── Envoi état monde → nouveau client ─────────────────

        private IEnumerator CoSendWorldState(ulong clientId)
        {
            // Laisse le client s'initialiser
            yield return new WaitForSeconds(0.5f);

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) yield break;

            // 1. Seed
            using (var w = new FastBufferWriter(8, Allocator.Temp))
            {
                w.WriteValueSafe(WorldSeedManager.Seed);
                nm.CustomMessagingManager.SendNamedMessage(
                    MsgSeed, clientId, w, NetworkDelivery.ReliableSequenced);
            }

            if (_world == null)
            {
                SendWorldDone(clientId);
                yield break;
            }

            // 2. Modifications de blocs en batches de 32
            var mods = _world.GetModifications();
            const int BatchSize = 32;

            for (int i = 0; i < mods.Count; i += BatchSize)
            {
                int count = Mathf.Min(BatchSize, mods.Count - i);
                int bufSize = 4 + count * 20; // 4 bytes count + 20 bytes/mod
                var w = new FastBufferWriter(bufSize + 8, Allocator.Temp);
                w.WriteValueSafe(count);
                for (int j = 0; j < count; j++)
                {
                    var mod = mods[i + j];
                    var d = new BlockChangeData
                    {
                        senderId  = ulong.MaxValue,
                        face      = (byte)mod.face,
                        chunkU    = (short)mod.cu,
                        chunkV    = (short)mod.cv,
                        chunkR    = (short)mod.cr,
                        lx        = mod.lx,
                        ly        = mod.ly,
                        lz        = mod.lz,
                        blockType = mod.block,
                        isBreak   = (byte)(mod.block == (byte)BlockType.Air ? 1 : 0)
                    };
                    BlockChangeData.Write(ref w, d);
                }
                nm.CustomMessagingManager.SendNamedMessage(
                    MsgWorldMod, clientId, w, NetworkDelivery.ReliableFragmentedSequenced);
                w.Dispose();
                yield return null; // étale les paquets
            }

            // 3. Done
            SendWorldDone(clientId);

            // 4. Position initiale du vaisseau (si présent)
            //    Le client n'a pas encore reçu de av.ship_pos (pilotage inactif)
            //    → on lui envoie la position actuelle pour qu'il le positionne correctement.
            if (_ship != null)
            {
                using var w = new FastBufferWriter(40, Allocator.Temp);
                w.WriteValueSafe(_ship.transform.position);
                w.WriteValueSafe(_ship.transform.rotation);
                w.WriteValueSafe(0f);      // speed = 0 (non piloté au moment de la connexion)
                w.WriteValueSafe((byte)0); // flags = 0
                nm.CustomMessagingManager.SendNamedMessage(
                    MsgShipPos, clientId, w, NetworkDelivery.ReliableSequenced);
            }
        }

        private static void SendWorldDone(ulong clientId)
        {
            using var w = new FastBufferWriter(4, Allocator.Temp);
            w.WriteValueSafe((byte)1);
            NetworkManager.Singleton?.CustomMessagingManager.SendNamedMessage(
                MsgWorldDone, clientId, w, NetworkDelivery.ReliableSequenced);
        }

        // ── Ship position sync ────────────────────────────────

        private void SyncShipIfPiloting()
        {
            if (!_ship.IsPiloting) return;
            _shipSyncTimer += Time.deltaTime;
            if (_shipSyncTimer < ShipSyncInterval) return;
            _shipSyncTimer = 0f;

            float speed = _ship.Speed;
            byte  flags = (byte)((_ship.IsVerticalThrustActive ? 1 : 0)
                                | (_ship.IsWingTrailActive      ? 2 : 0));

            using var w = new FastBufferWriter(40, Allocator.Temp);
            w.WriteValueSafe(_ship.transform.position);
            w.WriteValueSafe(_ship.transform.rotation);
            w.WriteValueSafe(speed);
            w.WriteValueSafe(flags);

            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            if (nm.IsServer)
                nm.CustomMessagingManager.SendNamedMessageToAll(
                    MsgShipPos, w, NetworkDelivery.Unreliable);
            else
                nm.CustomMessagingManager.SendNamedMessage(
                    MsgShipReq, NetworkManager.ServerClientId, w, NetworkDelivery.Unreliable);
        }

        // ── Handlers de messages ──────────────────────────────

        private void RegisterHandlers()
        {
            var cmm = NetworkManager.Singleton.CustomMessagingManager;
            cmm.RegisterNamedMessageHandler(MsgSeed,      HandleSeed);
            cmm.RegisterNamedMessageHandler(MsgWorldMod,  HandleWorldMod);
            cmm.RegisterNamedMessageHandler(MsgWorldDone, HandleWorldDone);
            // MsgBlocks : HOST reçoit les blocs des clients ; CLIENT reçoit les broadcasts du serveur.
            // Un seul handler par canal — ne pas enregistrer les deux ou le second écrase le premier.
            if (NetworkManager.Singleton.IsServer)
                cmm.RegisterNamedMessageHandler(MsgBlocks, HandleBlocksFromClient);
            else
                BlockSyncManager.Instance?.RegisterBroadcastHandler();
            cmm.RegisterNamedMessageHandler(MsgShipPos,   HandleShipPos);
            cmm.RegisterNamedMessageHandler(MsgShipReq,   HandleShipReqFromClient);
            cmm.RegisterNamedMessageHandler(MsgPlayerPos, HandlePlayerPos);

            // Handlers join/leave/list : côté CLIENT uniquement
            if (!NetworkManager.Singleton.IsServer)
            {
                cmm.RegisterNamedMessageHandler(MsgPlayerList,  HandlePlayerList);
                cmm.RegisterNamedMessageHandler(MsgPlayerJoin,  HandlePlayerJoin);
                cmm.RegisterNamedMessageHandler(MsgPlayerLeave, HandlePlayerLeave);
            }
        }

        private void HandlePlayerList(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int count);
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out ulong clientId);
                CreatePlayerSync(clientId, isLocal: false);
            }
        }

        private void HandlePlayerJoin(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong clientId);
            CreatePlayerSync(clientId, isLocal: false);
        }

        private void HandlePlayerLeave(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong clientId);
            RemovePlayerSync(clientId);
        }

        // Reçu depuis un client (sur le serveur) ou depuis le serveur (sur un client)
        private void HandlePlayerPos(ulong senderId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong clientId);
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out Quaternion rot);
            reader.ReadValueSafe(out float headPitch);
            reader.ReadValueSafe(out float speed);
            reader.ReadValueSafe(out byte inShipByte);
            bool inShip = inShipByte != 0;

            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            // Appliquer localement : mettre à jour le mannequin du joueur
            PlayerNetworkSync.GetById(clientId)?.SetRemotePosition(pos, rot, headPitch, speed, inShip);

            // Si on est le serveur et que c'est un client qui a envoyé,
            // relayer à tous les AUTRES clients connectés
            if (nm.IsServer && senderId != nm.LocalClientId)
            {
                // Buffer : clientId(8)+pos(12)+rot(16)+headPitch(4)+speed(4)+inShip(1) = 45 B
                using var w = new FastBufferWriter(49, Allocator.Temp);
                w.WriteValueSafe(clientId);
                w.WriteValueSafe(pos);
                w.WriteValueSafe(rot);
                w.WriteValueSafe(headPitch);
                w.WriteValueSafe(speed);
                w.WriteValueSafe(inShipByte);
                foreach (var cid in nm.ConnectedClientsIds)
                {
                    if (cid == senderId || cid == nm.LocalClientId) continue;
                    nm.CustomMessagingManager.SendNamedMessage(
                        MsgPlayerPos, cid, w, NetworkDelivery.UnreliableSequenced);
                }
            }
        }

        // Reçu par le CLIENT depuis le host
        private void HandleSeed(ulong senderId, FastBufferReader reader)
        {
            if (NetworkManager.Singleton.IsServer) return;
            reader.ReadValueSafe(out int seed);

            if (WorldSeedManager.Seed == seed)
            {
                OnStatusMessage?.Invoke("Seed OK. Réception du monde…");
            }
            else
            {
                // Seed différente → stocker le code + rechargement de scène
                WorldSeedManager.ForceInitialize(seed);
                PendingJoinCode = _currentCode;
                OnStatusMessage?.Invoke("Rechargement du terrain (seed différente)…");
                NetworkManager.Singleton.Shutdown();
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        private void HandleWorldMod(ulong senderId, FastBufferReader reader)
        {
            if (NetworkManager.Singleton.IsServer || _world == null) return;
            reader.ReadValueSafe(out int count);
            for (int i = 0; i < count; i++)
            {
                BlockChangeData.Read(ref reader, out var d);
                var coord = d.ToCoord();
                if (d.isBreak == 1)
                    _world.ApplyNetworkBreak(coord, d.lx, d.ly, d.lz);
                else
                    _world.ApplyNetworkPlace(coord, d.lx, d.ly, d.lz, (BlockType)d.blockType);
            }
        }

        private void HandleWorldDone(ulong senderId, FastBufferReader reader)
        {
            if (NetworkManager.Singleton.IsServer) return;
            OnStatusMessage?.Invoke("Monde synchronisé ! Bienvenue !");
        }

        // Reçu par le SERVEUR depuis un client (requête break/place)
        private void HandleBlocksFromClient(ulong senderId, FastBufferReader reader)
        {
            if (!NetworkManager.Singleton.IsServer || _world == null) return;

            reader.ReadValueSafe(out int count);

            // Capacité max : 4 + 64 * 20 = 1284 bytes
            using var broadcastWriter = new FastBufferWriter(4 + count * 20 + 8, Allocator.Temp);
            int valid = 0;
            // Reserve space for count (we write it at end)
            // Use a temp array
            var results = new BlockChangeData[count];

            for (int i = 0; i < count; i++)
            {
                BlockChangeData.Read(ref reader, out var d);
                d.senderId = senderId;
                var coord  = d.ToCoord();
                bool ok    = d.isBreak == 1
                    ? _world.ApplyNetworkBreak(coord, d.lx, d.ly, d.lz)
                    : _world.ApplyNetworkPlace(coord, d.lx, d.ly, d.lz, (BlockType)d.blockType);
                if (ok) results[valid++] = d;
            }

            if (valid > 0)
            {
                var bw = new FastBufferWriter(4 + valid * 20 + 8, Allocator.Temp);
                bw.WriteValueSafe(valid);
                for (int i = 0; i < valid; i++) BlockChangeData.Write(ref bw, results[i]);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(
                    MsgBlocks, bw, NetworkDelivery.ReliableSequenced);
                bw.Dispose();
            }
        }

        // Reçu par les CLIENTS depuis le host : stocker la cible (interpolée dans Update)
        private void HandleShipPos(ulong senderId, FastBufferReader reader)
        {
            if (NetworkManager.Singleton.IsServer || _ship == null) return;
            if (_ship.IsPiloting) return; // ce client pilote, ignorer
            _lastShipPosReceived = Time.time;
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out Quaternion rot);
            reader.ReadValueSafe(out float speed);
            reader.ReadValueSafe(out byte  flags);
            _targetShipPos       = pos;
            _targetShipRot       = rot;
            _hasRemoteShipTarget = true;
            _ship.SetRemoteThrusterState(speed, (flags & 1) != 0, (flags & 2) != 0);
        }

        // Reçu par le SERVEUR depuis un CLIENT qui pilote : relayer aux autres
        private void HandleShipReqFromClient(ulong senderId, FastBufferReader reader)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out Quaternion rot);
            reader.ReadValueSafe(out float speed);
            reader.ReadValueSafe(out byte  flags);
            // Stocker la cible → appliquée en Update pour un mouvement fluide
            if (_ship != null && !_ship.IsPiloting)
            {
                _targetShipPos       = pos;
                _targetShipRot       = rot;
                _hasRemoteShipTarget = true;
                _ship.SetRemoteThrusterState(speed, (flags & 1) != 0, (flags & 2) != 0);
            }
            // Relay to all other clients (avec vitesse + flags)
            using var w = new FastBufferWriter(40, Allocator.Temp);
            w.WriteValueSafe(pos);
            w.WriteValueSafe(rot);
            w.WriteValueSafe(speed);
            w.WriteValueSafe(flags);
            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (clientId == senderId || clientId == NetworkManager.Singleton.LocalClientId) continue;
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                    MsgShipPos, clientId, w, NetworkDelivery.Unreliable);
            }
        }

        // ── Encode / Decode IP + Port (base36, 10 chars) ──────

        public static string EncodeIP(string ip, ushort port)
        {
            try
            {
                var p = ip.Split('.');
                ulong v = ((ulong)byte.Parse(p[0]) << 24)
                        | ((ulong)byte.Parse(p[1]) << 16)
                        | ((ulong)byte.Parse(p[2]) <<  8)
                        |  (ulong)byte.Parse(p[3]);
                v = (v << 16) | port;
                char[] buf = new char[10];
                for (int i = 9; i >= 0; i--) { buf[i] = B36[(int)(v % 36)]; v /= 36; }
                return new string(buf);
            }
            catch { return "0000000000"; }
        }

        public static bool DecodeIP(string code, out string ip, out ushort port)
        {
            ip = null; port = 0;
            if (code == null || code.Length != 10) return false;
            ulong v = 0;
            foreach (char c in code)
            {
                int idx = B36.IndexOf(c);
                if (idx < 0) return false;
                v = v * 36 + (ulong)idx;
            }
            port = (ushort)(v & 0xFFFF);
            v >>= 16;
            ip = $"{(v >> 24) & 0xFF}.{(v >> 16) & 0xFF}.{(v >> 8) & 0xFF}.{v & 0xFF}";
            return port > 0;
        }

        // ── Utilitaire IP locale ──────────────────────────────

        private static string GetLocalIP()
        {
            try
            {
                using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Connect("8.8.8.8", 65530);
                return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
            }
            catch
            {
                foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == AddressFamily.InterNetwork)
                        return a.ToString();
                return null;
            }
        }
    }
}

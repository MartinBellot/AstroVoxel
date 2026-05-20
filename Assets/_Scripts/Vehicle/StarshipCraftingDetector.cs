// ============================================================
//  StarshipCraftingDetector.cs
//  Détecte le pattern de craft du vaisseau spatial :
//
//  Pattern (vue de dessus, Y constant) :
//
//    O O O O O O   ← 6 blocs de profondeur  (frontAxis)
//    O O O O O O   ← 3 blocs de large       (sideAxis)
//    O O O O O O
//
//       [D]        ← 1 DiamondBlock, 1 case devant le centre
//
//  Fonctionne sur n'importe quelle IVoxelWorld (planète, astéroïde, planète infinie).
//  Chaque craft CRÉE un nouveau vaisseau via le délégué SpawnShip.
//  Synchronisé en multijoueur via "av.ship_craft".
// ============================================================

using System;
using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using AstroVoxel.VoxelEngine;
using AstroVoxel.Network;
using AstroVoxel.Player;

namespace AstroVoxel.Vehicle
{
    public sealed class StarshipCraftingDetector : MonoBehaviour
    {
        public static StarshipCraftingDetector Instance { get; private set; }

        /// <summary>
        /// Fabrique un nouveau vaisseau à la position/rotation donnée sur le monde indiqué.
        /// Câblé par <see cref="AstroVoxel.Bootstrap.GameBootstrap"/> au démarrage.
        /// </summary>
        public static Func<Vector3, Quaternion, IVoxelWorld, SpaceShipController> SpawnShip;

        private bool _isCrafting;

        // ── Initialisation ────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()  => BlockInteraction.OnBlockPlaced += HandleBlockPlaced;
        private void OnDisable() => BlockInteraction.OnBlockPlaced -= HandleBlockPlaced;

        // ── Détection ─────────────────────────────────────────

        private void HandleBlockPlaced(Vector3 worldPos, BlockType type, IVoxelWorld world)
        {
            if (world == null || _isCrafting) return;
            if (SpaceShipController.IsAnyShipPiloted) return;

            if (type == BlockType.DiamondBlock)
                CheckPatternFromDiamond(worldPos, world);
            else if (type == BlockType.Obsidian)
                CheckPatternFromObsidian(worldPos, world);
        }

        // ── Recherche depuis le bloc diamant ──────────────────

        private void CheckPatternFromDiamond(Vector3 diamondPos, IVoxelWorld world)
        {
            ChunkRenderer cr = world.GetChunkAt(diamondPos);
            if (cr == null) return;

            Vector3 r = cr.transform.right;
            Vector3 f = cr.transform.forward;

            if (TryOrientation(diamondPos,  r,  f, world)) return;
            if (TryOrientation(diamondPos, -f,  r, world)) return;
            if (TryOrientation(diamondPos, -r, -f, world)) return;
               TryOrientation(diamondPos,  f, -r, world);
        }

        // ── Recherche depuis un bloc d'obsidienne ─────────────

        private void CheckPatternFromObsidian(Vector3 obsPos, IVoxelWorld world)
        {
            ChunkRenderer cr = world.GetChunkAt(obsPos);
            if (cr == null) return;

            Vector3 r = cr.transform.right;
            Vector3 f = cr.transform.forward;

            (Vector3 side, Vector3 front)[] orientations =
            {
                ( r,  f),
                (-f,  r),
                (-r, -f),
                ( f, -r),
            };

            foreach (var (sideAxis, frontAxis) in orientations)
            {
                for (int s = -1; s <= 1; s++)
                for (int ft = 0; ft <= 5; ft++)
                {
                    Vector3 candidateDiamond = obsPos - sideAxis * s - frontAxis * (ft + 1);
                    if (ReadBlock(candidateDiamond, world) != BlockType.DiamondBlock) continue;
                    if (TryOrientation(candidateDiamond, sideAxis, frontAxis, world)) return;
                }
            }
        }

        // ── Validation ────────────────────────────────────────

        private bool TryOrientation(Vector3 diamondPos, Vector3 sideAxis, Vector3 frontAxis, IVoxelWorld world)
        {
            if (!ValidatePattern(diamondPos, sideAxis, frontAxis, world)) return false;
            TriggerCraft(diamondPos, sideAxis, frontAxis, world);
            return true;
        }

        private bool ValidatePattern(Vector3 diamondPos, Vector3 sideAxis, Vector3 frontAxis, IVoxelWorld world)
        {
            for (int s = -1; s <= 1; s++)
            for (int ft = 0; ft <= 5; ft++)
            {
                Vector3 pos = diamondPos + sideAxis * s + frontAxis * (ft + 1);
                if (ReadBlock(pos, world) != BlockType.Obsidian) return false;
            }
            return true;
        }

        // ── Déclenchement ─────────────────────────────────────

        private void TriggerCraft(Vector3 diamondPos, Vector3 sideAxis, Vector3 frontAxis, IVoxelWorld world)
        {
            _isCrafting = true;

            if (!ServerManager.IsNetworkActive)
            {
                ApplyCraft(diamondPos, sideAxis, frontAxis, world);
                return;
            }

            byte orientation = GetOrientation(frontAxis, world.GetChunkAt(diamondPos));

            if (ServerManager.IsHost)
            {
                ApplyCraft(diamondPos, sideAxis, frontAxis, world);
                BroadcastShipCraft(diamondPos, orientation);
            }
            else
            {
                // Client : envoie la demande au serveur et attend le broadcast
                SendShipCraftRequest(diamondPos, orientation);
                _isCrafting = false;
            }
        }

        /// <summary>
        /// Applique le craft : casse les 19 blocs, lance les particules, crée le vaisseau.
        /// Appelé directement en solo/host et sur réception du broadcast côté client.
        /// </summary>
        public void ApplyCraft(Vector3 diamondPos, Vector3 sideAxis, Vector3 frontAxis, IVoxelWorld world)
        {
            _isCrafting = true;

            // Sur host réseau + planète : passer par BlockSyncManager pour propager aux clients.
            // Sur astéroïde ou solo : appel direct sur le monde.
            bool useNetSync = ServerManager.IsNetworkActive && ServerManager.IsHost
                              && world is PlanetWorld;

            void BreakAt(Vector3 p)
            {
                ChunkRenderer cr = world.GetChunkAt(p);
                if (useNetSync)
                {
                    var bsm = BlockSyncManager.Instance;
                    if (bsm != null) { bsm.RequestBreakBlock(cr, p); return; }
                }
                world.BreakBlock(p);
            }

            BreakAt(diamondPos);
            for (int s = -1; s <= 1; s++)
            for (int ft = 0; ft <= 5; ft++)
                BreakAt(diamondPos + sideAxis * s + frontAxis * (ft + 1));

            Vector3 center = diamondPos + frontAxis * 3.5f;
            ChunkRenderer centerCr = world.GetChunkAt(diamondPos);
            Vector3 up = centerCr != null ? centerCr.transform.up : center.normalized;

            StartCoroutine(CoSpawnEffectAndShip(center, up, frontAxis, world));
        }

        private IEnumerator CoSpawnEffectAndShip(Vector3 center, Vector3 up, Vector3 frontAxis, IVoxelWorld world)
        {
            SpawnCraftParticles(center, up);
            yield return new WaitForSeconds(0.65f);

            if (SpawnShip != null)
            {
                Vector3    shipPos = center + up * 2.5f;
                Quaternion shipRot = Quaternion.LookRotation(-frontAxis, up);
                var newShip = SpawnShip(shipPos, shipRot, world);
                if (newShip != null)
                    ServerManager.Instance?.SetShip(newShip);
            }

            _isCrafting = false;
        }

        // ── Particules ────────────────────────────────────────

        private void SpawnCraftParticles(Vector3 center, Vector3 up)
        {
            var psGO = new GameObject("CraftParticles");
            psGO.transform.position = center;
            var ps = psGO.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var psRenderer = psGO.GetComponent<ParticleSystemRenderer>();
            psRenderer.material = CreateParticleMaterial(new Color(0.45f, 0.85f, 1f));

            var main = ps.main;
            main.duration        = 2f;
            main.loop            = false;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(4f, 14f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.08f, 0.30f);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                new Color(0.40f, 0.85f, 1.00f, 1f),
                new Color(1.00f, 0.92f, 0.35f, 1f));
            main.maxParticles    = 400;
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 250) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius    = 2.0f;

            ps.Play();
            Destroy(psGO, 4f);

            var flashGO = new GameObject("CraftFlash");
            flashGO.transform.position = center;
            var light = flashGO.AddComponent<Light>();
            light.type      = LightType.Point;
            light.color     = new Color(0.45f, 0.85f, 1f);
            light.intensity = 10f;
            light.range     = 25f;
            Destroy(flashGO, 1.2f);
        }

        private static Material CreateParticleMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Standard");
            var mat = new Material(shader ?? Shader.Find("Standard"));
            mat.color = color;
            return mat;
        }

        // ── Lecture de blocs ──────────────────────────────────

        private static BlockType ReadBlock(Vector3 worldPos, IVoxelWorld world)
        {
            ChunkRenderer cr = world.GetChunkAt(worldPos);
            if (cr == null) return BlockType.Air;
            Vector3Int lb = world.WorldToLocalBlock(worldPos);
            return (BlockType)cr.GetBlock(lb.x, lb.y, lb.z);
        }

        // ── Encodage / décodage d'orientation (0–3) ──────────

        private static byte GetOrientation(Vector3 frontAxis, ChunkRenderer cr)
        {
            if (cr == null) return 0;
            Vector3 r = cr.transform.right;
            Vector3 f = cr.transform.forward;
            if (Vector3.Dot(frontAxis, f) >  0.5f) return 0;
            if (Vector3.Dot(frontAxis, r) >  0.5f) return 1;
            if (Vector3.Dot(frontAxis, f) < -0.5f) return 2;
            return 3;
        }

        private static (Vector3 sideAxis, Vector3 frontAxis) DecodeOrientation(byte orientation, Vector3 diamondPos, IVoxelWorld world)
        {
            ChunkRenderer cr = world.GetChunkAt(diamondPos);
            if (cr == null) return (Vector3.right, Vector3.forward);
            Vector3 r = cr.transform.right;
            Vector3 f = cr.transform.forward;
            return orientation switch
            {
                0 => ( r,  f),
                1 => (-f,  r),
                2 => (-r, -f),
                3 => ( f, -r),
                _ => ( r,  f),
            };
        }

        // ── Localisation du monde par position ────────────────

        /// <summary>
        /// Trouve l'IVoxelWorld (planète ou astéroïde) qui contient la position donnée.
        /// Utilisé lors de la réception des messages réseau pour reconstituer le contexte.
        /// </summary>
        private static IVoxelWorld FindWorldAt(Vector3 worldPos)
        {
            var planets = FindObjectsByType<PlanetWorld>(FindObjectsSortMode.None);
            foreach (var p in planets)
                if (p.GetChunkAt(worldPos) != null) return p;

            var asteroids = FindObjectsByType<AstroVoxel.Space.AsteroidWorld>(FindObjectsSortMode.None);
            foreach (var a in asteroids)
                if (a.GetChunkAt(worldPos) != null) return a;

            return null;
        }

        // ── Réseau ────────────────────────────────────────────

        internal void RegisterNetworkHandlers()
        {
            var cmm = NetworkManager.Singleton?.CustomMessagingManager;
            if (cmm == null) return;
            if (NetworkManager.Singleton.IsServer)
                cmm.RegisterNamedMessageHandler(ServerManager.MsgShipCraft, HandleShipCraftFromClient);
            else
                cmm.RegisterNamedMessageHandler(ServerManager.MsgShipCraft, HandleShipCraftBroadcast);
        }

        private void SendShipCraftRequest(Vector3 diamondPos, byte orientation)
        {
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe(diamondPos);
            w.WriteValueSafe(orientation);
            NetworkManager.Singleton?.CustomMessagingManager.SendNamedMessage(
                ServerManager.MsgShipCraft,
                NetworkManager.ServerClientId,
                w,
                NetworkDelivery.Reliable);
        }

        private void BroadcastShipCraft(Vector3 diamondPos, byte orientation)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe(diamondPos);
            w.WriteValueSafe(orientation);
            foreach (var clientId in nm.ConnectedClientsIds)
            {
                if (clientId == nm.LocalClientId) continue;
                nm.CustomMessagingManager.SendNamedMessage(
                    ServerManager.MsgShipCraft, clientId, w, NetworkDelivery.Reliable);
            }
        }

        private void HandleShipCraftFromClient(ulong senderId, FastBufferReader reader)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            reader.ReadValueSafe(out Vector3 diamondPos);
            reader.ReadValueSafe(out byte orientation);

            IVoxelWorld world = FindWorldAt(diamondPos);
            if (world == null) { _isCrafting = false; return; }

            var (sideAxis, frontAxis) = DecodeOrientation(orientation, diamondPos, world);
            if (!ValidatePattern(diamondPos, sideAxis, frontAxis, world)) { _isCrafting = false; return; }

            ApplyCraft(diamondPos, sideAxis, frontAxis, world);
            BroadcastShipCraft(diamondPos, orientation);
        }

        private void HandleShipCraftBroadcast(ulong senderId, FastBufferReader reader)
        {
            if (NetworkManager.Singleton.IsServer) return;
            reader.ReadValueSafe(out Vector3 diamondPos);
            reader.ReadValueSafe(out byte orientation);

            IVoxelWorld world = FindWorldAt(diamondPos);
            if (world == null) return;

            var (sideAxis, frontAxis) = DecodeOrientation(orientation, diamondPos, world);
            ApplyCraft(diamondPos, sideAxis, frontAxis, world);
        }
    }
}

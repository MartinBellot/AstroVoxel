// ============================================================
//  AsteroidWorld.cs
//  Monde voxel sphérique pour un astéroïde.
//
//  Architecture (v2 — simple cubic grid) :
//    • Grille de chunks 16³ AXIS-ALIGNED dans le repère LOCAL de l'astéroïde.
//    • Chaque bloc world est COUVERT PAR UN SEUL CHUNK → zéro z-fighting,
//      zéro géométrie redondante (vs l'ancien système 6-faces cube-sphère
//      qui faisait se chevaucher jusqu'à 6 chunks au centre d'un petit
//      astéroïde, multipliant la géométrie par 6 et causant un flicker).
//    • Skip des chunks vides AVANT instanciation → grosse économie GPU/CPU.
//    • Suit la rotation propre de l'astéroïde (chunks enfants).
//    • Expose IVoxelWorld → compatible BlockInteraction & MeteoriteController.
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using AstroVoxel.VoxelEngine;
using AstroVoxel.Physics;

namespace AstroVoxel.Space
{
    /// <summary>
    /// MonoBehaviour racine d'un astéroïde voxel.
    /// Utilise une grille cubique simple (pas le 18-face cube-sphère).
    /// </summary>
    [RequireComponent(typeof(GravityAttractor))]
    public sealed class AsteroidWorld : MonoBehaviour, IVoxelWorld
    {
        // ── Inspector ─────────────────────────────────────────
        [Header("Astéroïde")]
        [Tooltip("Rayon du noyau en blocs (avant déformation de surface).")]
        [SerializeField] public float coreRadius = 12f;

        [Tooltip("Seed de génération procédurale.")]
        [SerializeField] public int seed = 42;

        [Tooltip("Matériaux par type de bloc (partagés avec la planète).")]
        [SerializeField] public Material[] blockMaterials;

        // ── État interne ──────────────────────────────────────
        private readonly Dictionary<Vector3Int, ChunkRenderer> _chunks
            = new Dictionary<Vector3Int, ChunkRenderer>();

        private bool _loaded = false;
        private Coroutine _loadCo;
        private Material  _fallbackMaterial;   // cache (évite Shader.Find par chunk)

        // Nombre max de chunks instanciés par frame pendant un chargement.
        // Plus haut = chargement plus rapide mais hitch plus visible.
        private const int ChunksPerFrame = 4;

        // ── Propriétés publiques ──────────────────────────────
        public Vector3 AsteroidCenter => transform.position;
        public bool    IsLoaded       => _loaded;
        public int     ChunkCount     => _chunks.Count;

        // IVoxelWorld
        public Vector3 WorldCenter    => transform.position;

        // ── IVoxelWorld ───────────────────────────────────────

        /// <inheritdoc/>
        public ChunkRenderer GetChunkAt(Vector3 worldPos)
        {
            _chunks.TryGetValue(WorldToChunkCoord(worldPos), out ChunkRenderer cr);
            return cr;
        }

        /// <inheritdoc/>
        public Vector3Int WorldToLocalBlock(Vector3 worldPos)
        {
            // Convertit en repère local de l'astéroïde (gère rotation propre).
            Vector3 local = transform.InverseTransformPoint(worldPos);
            int cs = VoxelData.ChunkWidth;
            int gx = Mathf.FloorToInt(local.x);
            int gy = Mathf.FloorToInt(local.y);
            int gz = Mathf.FloorToInt(local.z);
            int lx = ((gx % cs) + cs) % cs;
            int ly = ((gy % cs) + cs) % cs;
            int lz = ((gz % cs) + cs) % cs;
            return new Vector3Int(lx, ly, lz);
        }

        /// <inheritdoc/>
        public bool BreakBlock(Vector3 worldPos)
        {
            ChunkRenderer cr = GetChunkAt(worldPos);
            if (cr == null) return false;
            Vector3Int lb = WorldToLocalBlock(worldPos);
            if (!BlockProperties.IsSolid(cr.GetBlock(lb.x, lb.y, lb.z))) return false;
            cr.SetBlock(lb.x, lb.y, lb.z, BlockType.Air);
            RebuildNeighbourChunks(worldPos);
            return true;
        }

        /// <inheritdoc/>
        public bool PlaceBlock(Vector3 worldPos, BlockType type)
        {
            ChunkRenderer cr = GetChunkAt(worldPos);
            if (cr == null) return false;
            Vector3Int lb = WorldToLocalBlock(worldPos);
            if (BlockProperties.IsSolid(cr.GetBlock(lb.x, lb.y, lb.z))) return false;
            cr.SetBlock(lb.x, lb.y, lb.z, type);
            RebuildNeighbourChunks(worldPos);
            return true;
        }

        // ── Chargement / déchargement ─────────────────────────

        /// <summary>
        /// Génère tous les chunks nécessaires pour couvrir le volume de l'astéroïde.
        /// Skip automatiquement les chunks 100 % Air (économie majeure).
        /// Le travail est étalé sur plusieurs frames pour éviter un freeze.
        /// </summary>
        public void LoadChunks()
        {
            if (_loaded || _loadCo != null) return;
            _loadCo = StartCoroutine(LoadChunksCoroutine());
        }

        private System.Collections.IEnumerator LoadChunksCoroutine()
        {
            int   cs         = VoxelData.ChunkWidth;
            float amplitude  = coreRadius * 0.40f;
            float shellMax   = coreRadius + amplitude + 1f;          // rayon englobant
            float shellMin   = Mathf.Max(0f, coreRadius - amplitude - 1f);
            int   range      = Mathf.CeilToInt(shellMax / cs) + 1;   // rayon en chunks
            float chunkDiag  = cs * 1.7321f;                          // sqrt(3)*16

            int spawned = 0, skipped = 0, spawnedThisFrame = 0;

            for (int cz = -range; cz <= range; cz++)
            for (int cy = -range; cy <= range; cy++)
            for (int cx = -range; cx <= range; cx++)
            {
                // Distance min entre la boîte du chunk et le centre local (0,0,0).
                float boxMinX = cx * cs, boxMaxX = (cx + 1) * cs;
                float boxMinY = cy * cs, boxMaxY = (cy + 1) * cs;
                float boxMinZ = cz * cs, boxMaxZ = (cz + 1) * cs;
                float ncX = Mathf.Clamp(0f, boxMinX, boxMaxX);
                float ncY = Mathf.Clamp(0f, boxMinY, boxMaxY);
                float ncZ = Mathf.Clamp(0f, boxMinZ, boxMaxZ);
                float nearest = Mathf.Sqrt(ncX * ncX + ncY * ncY + ncZ * ncZ);
                if (nearest > shellMax) continue;

                // Distance max (coin opposé) : si elle est sous shellMin, le chunk est
                // 100 % à l'intérieur du noyau. Comme tous ses voisins le seront aussi,
                // aucune face visible → skip purement (gain énorme sur gros astéroïdes).
                float fcX = Mathf.Max(Mathf.Abs(boxMinX), Mathf.Abs(boxMaxX));
                float fcY = Mathf.Max(Mathf.Abs(boxMinY), Mathf.Abs(boxMaxY));
                float fcZ = Mathf.Max(Mathf.Abs(boxMinZ), Mathf.Abs(boxMaxZ));
                float farthest = Mathf.Sqrt(fcX * fcX + fcY * fcY + fcZ * fcZ);
                bool deepInterior = farthest < shellMin - chunkDiag;
                if (deepInterior) { skipped++; continue; }

                var coord = new Vector3Int(cx, cy, cz);
                if (_chunks.ContainsKey(coord)) continue;

                int tris = TrySpawnChunk(coord);
                if (tris > 0) { spawned++; spawnedThisFrame++; }
                else           skipped++;

                if (spawnedThisFrame >= ChunksPerFrame)
                {
                    spawnedThisFrame = 0;
                    yield return null;
                }
            }

            _loaded = true;
            _loadCo = null;
        }

        /// <summary>
        /// Détruit tous les chunks. Appelé par AsteroidLOD quand le joueur s'éloigne.
        /// </summary>
        public void UnloadChunks()
        {
            if (_loadCo != null) { StopCoroutine(_loadCo); _loadCo = null; }
            foreach (var kv in _chunks)
                if (kv.Value != null)
                    Destroy(kv.Value.gameObject);
            _chunks.Clear();
            _loaded = false;
        }

        // ── Conversion world ↔ chunk ──────────────────────────

        private Vector3Int WorldToChunkCoord(Vector3 worldPos)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);
            int cs = VoxelData.ChunkWidth;
            return new Vector3Int(
                Mathf.FloorToInt(local.x / cs),
                Mathf.FloorToInt(local.y / cs),
                Mathf.FloorToInt(local.z / cs));
        }

        // ── Rebuild des voisins après modification de bloc ───

        private void RebuildNeighbourChunks(Vector3 modifiedWorldPos)
        {
            Vector3Int self = WorldToChunkCoord(modifiedWorldPos);
            for (int face = 0; face < 6; face++)
            {
                var off = new Vector3Int(
                    VoxelData.FaceChecks[face, 0],
                    VoxelData.FaceChecks[face, 1],
                    VoxelData.FaceChecks[face, 2]);
                Vector3Int nc = self + off;
                if (nc.Equals(self)) continue;
                if (_chunks.TryGetValue(nc, out ChunkRenderer nb))
                    nb.RebuildMesh();
            }
        }

        // ── Spawn d'un chunk (skip si vide) ──────────────────

        /// <summary>
        /// Génère les données du chunk, vérifie qu'il contient au moins un bloc
        /// solide, puis instancie le GameObject. Retourne le nombre de triangles
        /// (0 si le chunk a été skippé car vide).
        /// </summary>
        private int TrySpawnChunk(Vector3Int coord)
        {
            int cs = VoxelData.ChunkWidth;

            // Position du chunk dans le repère LOCAL de l'astéroïde (rotation = identity locale).
            Vector3 localOrigin = new Vector3(coord.x * cs, coord.y * cs, coord.z * cs);

            // Génère le contenu voxel en repère LOCAL (asteroidCenter = origin locale = 0).
            // → rotation-invariant : pas de re-génération si l'astéroïde tourne.
            var chunkData = new ChunkData();
            AsteroidChunkGenerator.Generate(
                chunkData, localOrigin, Vector3.zero, Quaternion.identity, coreRadius, seed);

            // Skip si 100 % Air → grosse économie (sphère inscrite dans cube : ~50 % cubes vides).
            if (IsChunkAllAir(chunkData)) return 0;

            // Instancie le GameObject enfant de l'astéroïde (suit position + rotation).
            var go = new GameObject($"AChunk_{coord.x}_{coord.y}_{coord.z}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = localOrigin;
            go.transform.localRotation = Quaternion.identity;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = blockMaterials != null && blockMaterials.Length > 1 && blockMaterials[1] != null
                ? blockMaterials[1]
                : GetFallbackMaterial();

            // OOB provider : pour le mesh-builder, retourne le bloc voisin hors-chunk
            // depuis le GÉNÉRATEUR (en repère local rotation-invariant).
            float r = coreRadius;
            int   s = seed;
            Transform tr = transform;
            System.Func<Vector3, byte> oob = worldPos =>
            {
                // worldPos vient du mesh-builder via transform.TransformPoint → world.
                // On le ramène en local pour interroger le générateur.
                Vector3 localPos = tr.InverseTransformPoint(worldPos);
                return AsteroidChunkGenerator.GetBlockType(localPos, Vector3.zero, r, s);
            };

            var cr = go.AddComponent<ChunkRenderer>();
            cr.InitFromWorld(
                go.transform.position,   // origin world courant
                AsteroidCenter,          // centre courant (dynamique via WorldCenter en rebuild)
                this,
                Quaternion.identity,     // pas de rotation par chunk (grille axis-aligned locale)
                FaceIndex.PosY,          // valeur factice (non utilisée en mode non-radial)
                blockMaterials,
                oobProvider:        oob,
                preGeneratedData:   chunkData,
                useRadialOrientation: false);   // ← clé : pas de logique radiale sur petits astéroïdes

            _chunks[coord] = cr;

            // Compte les triangles pour le log diagnostic.
            var mf = go.GetComponent<MeshFilter>();
            return (mf != null && mf.sharedMesh != null)
                ? mf.sharedMesh.triangles.Length / 3
                : 0;
        }

        private static bool IsChunkAllAir(ChunkData data)
        {
            int w = data.Width, h = data.Height;
            for (int z = 0; z < w; z++)
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (data.GetBlock(x, y, z) != (byte)BlockType.Air) return false;
            return true;
        }

        private Material GetFallbackMaterial()
        {
            if (_fallbackMaterial == null)
                _fallbackMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            return _fallbackMaterial;
        }
    }
}

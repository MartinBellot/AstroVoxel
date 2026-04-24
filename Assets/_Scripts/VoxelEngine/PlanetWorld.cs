// ============================================================
//  PlanetWorld.cs
//  Grille 3D de chunks cubiques autour d'une planète sphérique.
//  Approche correcte : chunks axis-aligned, sphère déterminée
//  uniquement par Vector3.Distance dans le générateur.
//  Pas de face-based logic — couvre toute la sphère sans zones mortes.
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace AstroVoxel.VoxelEngine
{
    /// <summary>
    /// Coordonnée 3D d'un chunk dans la grille monde.
    /// Un chunk couvre les blocs [X*16, X*16+16) × [Y*16 ...] × [Z*16 ...]
    /// </summary>
    public readonly struct ChunkCoord : System.IEquatable<ChunkCoord>
    {
        public readonly int X, Y, Z;

        public ChunkCoord(int x, int y, int z) { X = x; Y = y; Z = z; }

        public bool Equals(ChunkCoord o) => X == o.X && Y == o.Y && Z == o.Z;
        public override bool Equals(object obj) => obj is ChunkCoord c && Equals(c);
        public override int GetHashCode()
        {
            unchecked { return (X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791); }
        }
        public override string ToString() => $"({X},{Y},{Z})";
    }

    /// <summary>
    /// MonoBehaviour racine de la planète.
    /// Gère la grille 3D de chunks cubiques autour du viewer.
    /// Expose l'API de lecture/écriture de blocs en world-space.
    /// </summary>
    public sealed class PlanetWorld : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────
        [Header("Planète")]
        [SerializeField] public float planetRadius = 165f;

        [Header("Chunks")]
        [Tooltip("Distance en chunks autour du joueur (recommandé : 3-4).")]
        [SerializeField] private int renderDistanceChunks = 3;

        [Tooltip("Matériau appliqué à chaque chunk.")]
        [SerializeField] public Material[] blockMaterials;   // index = (byte)BlockType

        // ── État interne ───────────────────────────────────────
        private readonly Dictionary<ChunkCoord, ChunkRenderer> _chunks
            = new Dictionary<ChunkCoord, ChunkRenderer>();

        private Transform _viewer;

        // ── API publique ──────────────────────────────────────

        public Vector3 PlanetCenter => transform.position;

        public void SetViewer(Transform viewer) => _viewer = viewer;

        /// <summary>Appelé à chaque frame pour charger/décharger les chunks.</summary>
        public void UpdateChunks()
        {
            if (_viewer == null) return;

            int   cs    = VoxelData.ChunkWidth;          // 16
            float coreR = PlanetChunkGenerator.PlanetCoreRadius;
            float amp   = PlanetChunkGenerator.SurfaceAmplitude;
            float crust = PlanetChunkGenerator.CrustThickness;

            // Rayon de la demi-diagonale d'un chunk cubique
            float halfDiag = cs * 0.8660254f; // sqrt(3)/2 * cs

            // Un chunk est "surface" si sa sphère englobante intersecte le shell de terrain
            float shellMin = coreR - crust - halfDiag;
            float shellMax = coreR + amp + 2f + halfDiag;

            // Coordonnées de chunk du viewer
            Vector3 vw = _viewer.position - PlanetCenter;
            int vcx = Mathf.FloorToInt(vw.x / cs);
            int vcy = Mathf.FloorToInt(vw.y / cs);
            int vcz = Mathf.FloorToInt(vw.z / cs);

            int rd      = renderDistanceChunks;
            var toKeep  = new HashSet<ChunkCoord>();

            for (int dz = -rd; dz <= rd; dz++)
            for (int dy = -rd; dy <= rd; dy++)
            for (int dx = -rd; dx <= rd; dx++)
            {
                int cx = vcx + dx;
                int cy = vcy + dy;
                int cz = vcz + dz;

                // Centre du chunk en coordonnées locales planète
                Vector3 chunkCenter = new Vector3(
                    (cx + 0.5f) * cs,
                    (cy + 0.5f) * cs,
                    (cz + 0.5f) * cs) - PlanetCenter;

                float dist = chunkCenter.magnitude;

                // Ne charger que les chunks qui intersectent le shell de terrain
                if (dist < shellMin || dist > shellMax) continue;

                ChunkCoord coord = new ChunkCoord(cx, cy, cz);
                toKeep.Add(coord);

                if (!_chunks.ContainsKey(coord))
                    SpawnChunk(coord);
            }

            // Décharge les chunks hors range
            var toRemove = new List<ChunkCoord>();
            foreach (var kv in _chunks)
                if (!toKeep.Contains(kv.Key))
                    toRemove.Add(kv.Key);

            foreach (var coord in toRemove)
            {
                if (_chunks.TryGetValue(coord, out ChunkRenderer cr))
                    Destroy(cr.gameObject);
                _chunks.Remove(coord);
            }
        }

        // ── Lecture / Écriture de blocs (world-space) ─────────

        public ChunkRenderer GetChunkAt(Vector3 worldPos)
        {
            _chunks.TryGetValue(WorldToChunkCoord(worldPos), out ChunkRenderer cr);
            return cr;
        }

        public Vector3Int WorldToLocalBlock(Vector3 worldPos)
        {
            int cs = VoxelData.ChunkWidth;
            ChunkCoord cc = WorldToChunkCoord(worldPos);
            int bx = Mathf.FloorToInt(worldPos.x);
            int by = Mathf.FloorToInt(worldPos.y);
            int bz = Mathf.FloorToInt(worldPos.z);
            return new Vector3Int(
                bx - cc.X * cs,
                by - cc.Y * cs,
                bz - cc.Z * cs);
        }

        public bool BreakBlock(Vector3 worldPos)
        {
            ChunkRenderer cr = GetChunkAt(worldPos);
            if (cr == null) return false;
            Vector3Int lb = WorldToLocalBlock(worldPos);
            if (!BlockProperties.IsSolid(cr.GetBlock(lb.x, lb.y, lb.z))) return false;
            cr.SetBlock(lb.x, lb.y, lb.z, BlockType.Air);
            return true;
        }

        public bool PlaceBlock(Vector3 worldPos, BlockType type)
        {
            ChunkRenderer cr = GetChunkAt(worldPos);
            if (cr == null) return false;
            Vector3Int lb = WorldToLocalBlock(worldPos);
            if (BlockProperties.IsSolid(cr.GetBlock(lb.x, lb.y, lb.z))) return false;
            cr.SetBlock(lb.x, lb.y, lb.z, type);
            return true;
        }

        // ── Interne ───────────────────────────────────────────

        private void SpawnChunk(ChunkCoord coord)
        {
            int     cs       = VoxelData.ChunkWidth;
            Vector3 worldPos = PlanetCenter + new Vector3(coord.X, coord.Y, coord.Z) * cs;

            var go = new GameObject($"Chunk_{coord}");
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = worldPos;
            // Pas de rotation — chunks axis-aligned

            var mr = go.AddComponent<MeshRenderer>();
            // Le MeshRenderer est configuré par ChunkRenderer.RebuildMesh via blockMaterials.
            // Un matériau par défaut est requis pour éviter un warning Unity.
            mr.sharedMaterial = (blockMaterials != null && blockMaterials.Length > 0 && blockMaterials[0] != null)
                ? blockMaterials[0]
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));

            var cr = go.AddComponent<ChunkRenderer>();
            cr.InitFromWorld(worldPos, PlanetCenter, blockMaterials);

            _chunks[coord] = cr;
        }

        private ChunkCoord WorldToChunkCoord(Vector3 worldPos)
        {
            int cs = VoxelData.ChunkWidth;
            // Soustrait le centre planète pour rester dans le référentiel local
            Vector3 local = worldPos - PlanetCenter;
            return new ChunkCoord(
                Mathf.FloorToInt(local.x / cs),
                Mathf.FloorToInt(local.y / cs),
                Mathf.FloorToInt(local.z / cs));
        }

        // ── Cycle de vie ──────────────────────────────────────

        private void Update() => UpdateChunks();
    }
}

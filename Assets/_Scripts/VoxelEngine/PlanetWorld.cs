// ============================================================
//  PlanetWorld.cs
//  Gère la grille de Chunks sur une planète CubeSphere.
//  Génère les chunks autour du joueur (render distance).
//  Fournit l'API mondiale pour lire/modifier des blocs.
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace AstroVoxel.VoxelEngine
{
    /// <summary>
    /// Coordonnée unique d'un chunk dans le référentiel de la planète.
    /// Utilisée comme clé de dictionnaire.
    /// </summary>
    public readonly struct ChunkCoord : System.IEquatable<ChunkCoord>
    {
        public readonly int FaceIndex, X, Z;   // FaceIndex 0-5 = face de la CubeSphere

        public ChunkCoord(int face, int x, int z) { FaceIndex = face; X = x; Z = z; }

        public bool Equals(ChunkCoord o) => FaceIndex == o.FaceIndex && X == o.X && Z == o.Z;
        public override bool Equals(object obj) => obj is ChunkCoord c && Equals(c);
        public override int GetHashCode()
        {
            unchecked { return (FaceIndex * 1000000) ^ (X * 73856093) ^ (Z * 83492791); }
        }
        public override string ToString() => $"(f{FaceIndex},{X},{Z})";
    }

    /// <summary>
    /// MonoBehaviour racine de la planète.
    /// Construit les chunks visibles, les détruit hors range,
    /// et expose l'API de lecture/écriture de blocs en world-space.
    /// </summary>
    public sealed class PlanetWorld : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────
        [Header("Planète")]
        [Tooltip("Rayon de la planète en unités Unity (doit correspondre à PlanetCoreRadius × BlockScale).")]
        [SerializeField] public float planetRadius = 80f;

        [Header("Chunks")]
        [Tooltip("Distance en nombre de chunks à générer autour du joueur.")]
        [SerializeField] private int renderDistanceChunks = 3;

        [Tooltip("Material appliqué à chaque chunk.")]
        [SerializeField] public Material chunkMaterial;

        // ── État interne ───────────────────────────────────────
        private readonly Dictionary<ChunkCoord, ChunkRenderer> _chunks
            = new Dictionary<ChunkCoord, ChunkRenderer>();

        private Transform _viewer;   // transform du joueur (mis à jour via SetViewer)

        // Faces de la CubeSphere : 6 directions cardinales
        private static readonly Vector3[] CubeFaceNormals = new Vector3[]
        {
            Vector3.up, Vector3.down,
            Vector3.forward, Vector3.back,
            Vector3.right, Vector3.left,
        };

        // ── API publique ──────────────────────────────────────

        public Vector3 PlanetCenter => transform.position;

        public void SetViewer(Transform viewer) => _viewer = viewer;

        /// <summary>Appelé à chaque frame pour charger/décharger les chunks.</summary>
        public void UpdateChunks()
        {
            if (_viewer == null) return;

            Vector3 viewerLocal = _viewer.position - PlanetCenter;

            // Charge les chunks sur TOUTES les 6 faces.
            // Nécessaire : le joueur voit toujours les bords de 2-3 faces simultanément.
            // Les chunks trop loin sont déchargés dans LoadChunksAroundViewer.
            foreach (var faceNormal in CubeFaceNormals)
                LoadChunksAroundViewer(viewerLocal, faceNormal);
        }

        // ── Lecture / Écriture de blocs (world-space) ─────────

        /// <summary>
        /// Retourne le ChunkRenderer contenant la position world donnée, ou null.
        /// </summary>
        public ChunkRenderer GetChunkAt(Vector3 worldPos)
        {
            ChunkCoord coord = WorldToChunkCoord(worldPos);
            _chunks.TryGetValue(coord, out ChunkRenderer cr);
            return cr;
        }

        /// <summary>
        /// Convertit une position world en coordonnées locales dans son chunk.
        /// </summary>
        public Vector3Int WorldToLocalBlock(Vector3 worldPos)
        {
            ChunkCoord cc = WorldToChunkCoord(worldPos);
            if (!_chunks.TryGetValue(cc, out ChunkRenderer cr)) return Vector3Int.zero;

            // Transforme dans l'espace local du chunk
            Vector3 local = cr.transform.InverseTransformPoint(worldPos);
            return new Vector3Int(
                Mathf.FloorToInt(local.x),
                Mathf.FloorToInt(local.y),
                Mathf.FloorToInt(local.z));
        }

        /// <summary>
        /// Casse (Air) le bloc à la position world donnée.
        /// </summary>
        public bool BreakBlock(Vector3 worldPos)
        {
            ChunkRenderer cr = GetChunkAt(worldPos);
            if (cr == null) return false;
            Vector3Int lb = WorldToLocalBlock(worldPos);
            byte current = cr.GetBlock(lb.x, lb.y, lb.z);
            if (!BlockProperties.IsSolid(current)) return false;
            cr.SetBlock(lb.x, lb.y, lb.z, BlockType.Air);
            return true;
        }

        /// <summary>
        /// Place un bloc à la position world donnée (si la case est de l'Air).
        /// </summary>
        public bool PlaceBlock(Vector3 worldPos, BlockType type)
        {
            ChunkRenderer cr = GetChunkAt(worldPos);
            if (cr == null) return false;
            Vector3Int lb = WorldToLocalBlock(worldPos);
            byte current = cr.GetBlock(lb.x, lb.y, lb.z);
            if (BlockProperties.IsSolid(current)) return false;
            cr.SetBlock(lb.x, lb.y, lb.z, type);
            return true;
        }

        // ── Chargement des chunks ─────────────────────────────

        private void LoadChunksAroundViewer(Vector3 viewerLocal, Vector3 faceNormal)
        {
            // ── RÈGLE FONDAMENTALE ────────────────────────────────────────────────
            // Tous les chunks d'une même face partagent EXACTEMENT la même orientation.
            // → Les chunks sont des tuiles plates qui s'assemblent sans fente.
            // La forme sphérique vient du générateur (test distance au centre),
            // PAS de la rotation individuelle de chaque chunk.
            // ─────────────────────────────────────────────────────────────────────

            Vector3 chunkUp      = faceNormal;
            Vector3 chunkRight   = Vector3.Cross(faceNormal,
                                       Mathf.Abs(faceNormal.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
            Vector3 chunkForward = Vector3.Cross(chunkRight, faceNormal).normalized;

            int   cw    = VoxelData.ChunkWidth;
            float coreR = PlanetChunkGenerator.PlanetCoreRadius;
            float depth = PlanetChunkGenerator.ChunkDepthBelowSurface;

            // Coordonnées de chunk du viewer projetées sur la face
            float vr = Vector3.Dot(viewerLocal, chunkRight);
            float vf = Vector3.Dot(viewerLocal, chunkForward);
            int viewerCR = Mathf.FloorToInt(vr / cw);
            int viewerCF = Mathf.FloorToInt(vf / cw);

            int faceIdx = GetFaceIndex(faceNormal);
            int rd      = renderDistanceChunks;
            var toKeep  = new HashSet<ChunkCoord>();

            for (int df = -rd; df <= rd; df++)
            for (int dr = -rd; dr <= rd; dr++)
            {
                int ci = viewerCR + dr;
                int cj = viewerCF + df;

                // Chunks plats : tous à la même profondeur le long de faceNormal.
                // La forme sphérique vient du générateur (Vector3.Distance), pas de
                // la rotation des chunks. C'est la méthode correcte pour du voxel.
                Vector3 chunkOriginWorld = PlanetCenter
                    + chunkUp      * (coreR - depth)
                    + chunkRight   * (ci * cw)
                    + chunkForward * (cj * cw);

                ChunkCoord coord = new ChunkCoord(faceIdx, ci, cj);
                toKeep.Add(coord);

                if (!_chunks.ContainsKey(coord))
                    SpawnChunk(coord, chunkOriginWorld, chunkUp, chunkRight, chunkForward);
            }

            // Décharge les chunks trop loin
            var toRemove = new List<ChunkCoord>();
            foreach (var kv in _chunks)
                if (!toKeep.Contains(kv.Key)) toRemove.Add(kv.Key);

            foreach (var coord in toRemove)
            {
                if (_chunks.TryGetValue(coord, out ChunkRenderer cr))
                    Destroy(cr.gameObject);
                _chunks.Remove(coord);
            }
        }

        private void SpawnChunk(
            ChunkCoord coord,
            Vector3 originWorld,
            Vector3 up, Vector3 right, Vector3 forward)
        {
            var go = new GameObject($"Chunk_{coord}");
            go.transform.SetParent(transform, worldPositionStays: true);

            // Oriente le chunk : Y local = up planétaire
            go.transform.rotation = Quaternion.LookRotation(forward, up);
            go.transform.position = originWorld;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = chunkMaterial != null
                ? chunkMaterial
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));

            var cr = go.AddComponent<ChunkRenderer>();
            cr.InitFromWorld(up, right, forward, PlanetCenter);

            _chunks[coord] = cr;
        }

        // ── Utilitaires ───────────────────────────────────────

        private ChunkCoord WorldToChunkCoord(Vector3 worldPos)
        {
            Vector3 local = worldPos - PlanetCenter;
            Vector3 face  = GetDominantFace(local.normalized);
            Vector3 right   = Vector3.Cross(face, (Mathf.Abs(face.y) < 0.9f ? Vector3.up : Vector3.forward)).normalized;
            Vector3 forward = Vector3.Cross(right, face).normalized;

            int cw = VoxelData.ChunkWidth;
            int cr = Mathf.FloorToInt(Vector3.Dot(local, right)   / cw);
            int cf = Mathf.FloorToInt(Vector3.Dot(local, forward) / cw);
            return new ChunkCoord(GetFaceIndex(face), cr, cf);
        }

        private static int GetFaceIndex(Vector3 faceNormal)
        {
            for (int i = 0; i < CubeFaceNormals.Length; i++)
                if (CubeFaceNormals[i] == faceNormal) return i;
            return 0;
        }

        private static Vector3 GetDominantFace(Vector3 dir)
        {
            float ax = Mathf.Abs(dir.x), ay = Mathf.Abs(dir.y), az = Mathf.Abs(dir.z);
            if (ax >= ay && ax >= az) return dir.x > 0 ? Vector3.right   : Vector3.left;
            if (ay >= ax && ay >= az) return dir.y > 0 ? Vector3.up      : Vector3.down;
            return                         dir.z > 0 ? Vector3.forward : Vector3.back;
        }

        // ── Cycle de vie ──────────────────────────────────────

        private void Update() => UpdateChunks();
    }
}

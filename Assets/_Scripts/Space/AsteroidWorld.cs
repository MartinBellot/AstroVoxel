// ============================================================
//  AsteroidWorld.cs
//  Monde voxel sphérique pour un astéroïde.
//
//  Architecture calquée sur PlanetWorld mais :
//    • Rayon et seed configurables par instance.
//    • Utilise AsteroidChunkGenerator (Blackstone/Obsidian/minerais).
//    • 18-Face Cube-Sphère (même système que la planète).
//    • Chargement/déchargement piloté par AsteroidLOD (pas d'auto-start).
//    • Expose IVoxelWorld → compatible BlockInteraction & ChunkRenderer.
//    • Possède un GravityAttractor (gravité faible, portée limitée).
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using AstroVoxel.VoxelEngine;
using AstroVoxel.Physics;

namespace AstroVoxel.Space
{
    /// <summary>
    /// MonoBehaviour racine d'un astéroïde voxel.
    /// Gère la grille de chunks orientés (18-Face Cube-Sphère).
    /// Expose l'API BreakBlock / PlaceBlock compatible <see cref="IVoxelWorld"/>.
    /// Le chargement des chunks est déclenché depuis <see cref="AsteroidLOD"/>.
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
        private readonly Dictionary<FaceChunkCoord, ChunkRenderer> _chunks
            = new Dictionary<FaceChunkCoord, ChunkRenderer>();

        private bool _loaded = false;

        // ── Propriété publique ────────────────────────────────
        public Vector3 AsteroidCenter => transform.position;
        public bool    IsLoaded       => _loaded;
        public int     ChunkCount     => _chunks.Count;

        // IVoxelWorld
        public Vector3 WorldCenter    => transform.position;

        // ── IVoxelWorld ───────────────────────────────────────

        /// <inheritdoc/>
        public ChunkRenderer GetChunkAt(Vector3 worldPos)
        {
            _chunks.TryGetValue(WorldToFaceChunk(worldPos), out ChunkRenderer cr);
            return cr;
        }

        /// <inheritdoc/>
        public Vector3Int WorldToLocalBlock(Vector3 worldPos)
        {
            int cs = VoxelData.ChunkWidth;
            FaceChunkCoord fc = WorldToFaceChunk(worldPos);
            int fi = (int)fc.Face;

            Vector3 rel = worldPos - AsteroidCenter;
            int gu = Mathf.FloorToInt(Vector3.Dot(rel, SphereFace.Rights  [fi]));
            int gr = Mathf.FloorToInt(Vector3.Dot(rel, SphereFace.Normals [fi]));
            int gv = Mathf.FloorToInt(Vector3.Dot(rel, SphereFace.Forwards[fi]));

            return new Vector3Int(
                gu - fc.U * cs,
                gr - fc.R * cs,
                gv - fc.V * cs);
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
        /// Génère tous les chunks de la coque de l'astéroïde.
        /// Appelé par <see cref="AsteroidLOD"/> quand le joueur s'approche.
        /// </summary>
        public void LoadChunks()
        {
            if (_loaded) return;

            int   cs       = VoxelData.ChunkWidth;
            float amplitude = coreRadius * 0.40f;
            float halfDiag  = cs * 0.8660254f;        // √3/2 * cs
            float shellMax  = coreRadius + amplitude + 1f + halfDiag;
            // shellMin négatif : couvre le noyau jusqu'à r = 0
            float shellMin  = -halfDiag;

            float step = cs * 0.5f;
            int   maxS = Mathf.CeilToInt(shellMax / step) + 2;

            for (int dz = -maxS; dz <= maxS; dz++)
            for (int dy = -maxS; dy <= maxS; dy++)
            for (int dx = -maxS; dx <= maxS; dx++)
            {
                float fx   = (dx + 0.5f) * step;
                float fy   = (dy + 0.5f) * step;
                float fz   = (dz + 0.5f) * step;
                float dist = Mathf.Sqrt(fx * fx + fy * fy + fz * fz);

                if (dist > shellMax || dist < shellMin) continue;

                var       sample = new Vector3(fx, fy, fz);
                FaceIndex fi     = SphereFace.GetFace(sample);
                int       f      = (int)fi;

                int U = Mathf.FloorToInt(Vector3.Dot(sample, SphereFace.Rights  [f]) / cs);
                int V = Mathf.FloorToInt(Vector3.Dot(sample, SphereFace.Forwards[f]) / cs);
                int R = Mathf.FloorToInt(Vector3.Dot(sample, SphereFace.Normals [f]) / cs);

                var coord = new FaceChunkCoord(fi, U, V, R);
                if (!_chunks.ContainsKey(coord))
                    SpawnChunk(coord);
            }

            _loaded = true;

            // Diagnostic : compte les chunks réellement visibles (au moins un vertex)
            int visible = 0;
            int totalTris = 0;
            foreach (var kv in _chunks)
            {
                var mf = kv.Value != null ? kv.Value.GetComponent<MeshFilter>() : null;
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0)
                {
                    visible++;
                    totalTris += mf.sharedMesh.triangles.Length / 3;
                }
            }
            Debug.Log($"[AsteroidWorld] {name} LoadChunks: spawned={_chunks.Count}, nonEmpty={visible}, tris={totalTris}, coreR={coreRadius:F1}, center={AsteroidCenter}");
        }

        /// <summary>
        /// Détruit tous les chunks. Appelé par AsteroidLOD quand le joueur s'éloigne.
        /// </summary>
        public void UnloadChunks()
        {
            foreach (var kv in _chunks)
                if (kv.Value != null)
                    Destroy(kv.Value.gameObject);
            _chunks.Clear();
            _loaded = false;
        }

        // ── Lookup interne ────────────────────────────────────

        private FaceChunkCoord WorldToFaceChunk(Vector3 worldPos)
        {
            int    cs   = VoxelData.ChunkWidth;
            Vector3 rel  = worldPos - AsteroidCenter;
            FaceIndex fi = SphereFace.GetFace(rel);
            int       f  = (int)fi;

            int U = Mathf.FloorToInt(Vector3.Dot(rel, SphereFace.Rights  [f]) / cs);
            int V = Mathf.FloorToInt(Vector3.Dot(rel, SphereFace.Forwards[f]) / cs);
            int R = Mathf.FloorToInt(Vector3.Dot(rel, SphereFace.Normals [f]) / cs);

            return new FaceChunkCoord(fi, U, V, R);
        }

        private void RebuildNeighbourChunks(Vector3 modifiedWorldPos)
        {
            FaceChunkCoord selfCoord = WorldToFaceChunk(modifiedWorldPos);
            Quaternion     rot       = SphereFace.GetRotation(selfCoord.Face);

            for (int face = 0; face < 6; face++)
            {
                Vector3 localOffset = new Vector3(
                    VoxelData.FaceChecks[face, 0],
                    VoxelData.FaceChecks[face, 1],
                    VoxelData.FaceChecks[face, 2]);

                Vector3        neighbourWorld = modifiedWorldPos + rot * localOffset;
                FaceChunkCoord neighbourCoord = WorldToFaceChunk(neighbourWorld);

                if (neighbourCoord.Equals(selfCoord)) continue;

                if (_chunks.TryGetValue(neighbourCoord, out ChunkRenderer nb))
                    nb.RebuildMesh();
            }
        }

        private void SpawnChunk(FaceChunkCoord coord)
        {
            int cs   = VoxelData.ChunkWidth;
            int fi   = (int)coord.Face;

            // Origine world du chunk (coin bas-gauche en espace face-local)
            Vector3 origin = AsteroidCenter
                + SphereFace.Rights  [fi] * (coord.U * cs)
                + SphereFace.Normals [fi] * (coord.R * cs)
                + SphereFace.Forwards[fi] * (coord.V * cs);

            Quaternion rot = SphereFace.GetRotation(coord.Face);

            var go = new GameObject($"AChunk_{coord}");
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = origin;
            go.transform.rotation = rot;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = blockMaterials != null && blockMaterials.Length > 0 && blockMaterials[0] != null
                ? blockMaterials[0]
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));

            // Capture des paramètres pour le provider OOB
            float  r      = coreRadius;
            int    s      = seed;
            Vector3 center = AsteroidCenter;

            // Génère les données voxel avec le bon générateur (Blackstone/Obsidian),
            // PAS PlanetChunkGenerator (qui produirait herbe/arbres).
            var chunkData = new ChunkData();
            AsteroidChunkGenerator.Generate(chunkData, origin, center, rot, r, s);

            var cr = go.AddComponent<ChunkRenderer>();
            cr.InitFromWorld(
                origin, center, this, rot, coord.Face, blockMaterials,
                oobProvider: pos => AsteroidChunkGenerator.GetBlockType(pos, center, r, s),
                preGeneratedData: chunkData);

            _chunks[coord] = cr;
        }
    }
}

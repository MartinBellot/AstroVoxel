// ============================================================
//  PlanetWorld.cs
//  Système 18-Face Cube-Sphère Octaédrique.
//
//  ARCHITECTURE :
//  • Clé de chunk = FaceChunkCoord (face + U/V/R en face-local).
//    Chaque chunk a une rotation alignée avec sa face → blocs
//    orientés radialement → pas d'effet escalier.
//  • Spawning : parcours de la SPHÈRE COMPLÈTE (grille world-alignée
//    couvrant shellMin..shellMax). Pour une petite planète (~300
//    chunks) on charge tout d'un coup au démarrage ; UpdateChunks
//    ne refait le test que si un chunk manque.
//  • Lookups (GetChunkAt, WorldToLocalBlock) utilisent les axes
//    face-locaux → cohérents avec les données générées.
// ============================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AstroVoxel.Save;
using AstroVoxel.Space;

namespace AstroVoxel.VoxelEngine
{
    /// <summary>
    /// MonoBehaviour racine de la planète.
    /// Gère la grille de chunks orientés (18-Face Cube-Sphère Octaédrique).
    /// Expose l'API de lecture/écriture de blocs en world-space.
    /// </summary>
    public sealed class PlanetWorld : MonoBehaviour, IVoxelWorld
    {
        // ── Inspector ─────────────────────────────────────────
        [Header("Planète")]
        [SerializeField] public float planetRadius = 165f;

        [Tooltip("Matériaux par type de bloc (index = (byte)BlockType).")]
        [SerializeField] public Material[] blockMaterials;

        // ── Config procédurale (optionnelle) ──────────────────────
        /// <summary>
        /// Config de génération procédurale. Null = home planet (constantes par défaut).
        /// Assignée par InfinitePlanetSystem avant que Start() s'exécute.
        /// </summary>
        [System.NonSerialized] public PlanetGenerationConfig? generationConfig = null;

        /// <summary>
        /// Si true, Start() ne lance pas UpdateChunks() (InfinitePlanetSystem
        /// démarre le chargement async lui-même).
        /// </summary>
        [System.NonSerialized] public bool manualLoad = false;

        // ── État interne ───────────────────────────────────────────
        private readonly Dictionary<FaceChunkCoord, ChunkRenderer> _chunks
            = new Dictionary<FaceChunkCoord, ChunkRenderer>();

        private Transform _viewer;          // non utilisé pour le spawning, gardé pour l'API
        private bool _planetLoaded = false; // évite de respawner tous les chunks à chaque frame

        // Clé packed = lx | (ly << 8) | (lz << 16)
        private readonly Dictionary<FaceChunkCoord, Dictionary<int, byte>> _modifications
            = new Dictionary<FaceChunkCoord, Dictionary<int, byte>>();

        /// <summary>Vrai quand tous les chunks sont chargés.</summary>
        public bool IsFullyLoaded => _planetLoaded;

        // ── API publique ──────────────────────────────────────

        public Vector3 PlanetCenter => transform.position;

        // IVoxelWorld
        public Vector3 WorldCenter   => transform.position;

        public void SetViewer(Transform viewer) => _viewer = viewer;

        // ── Gestion des chunks ────────────────────────────────

        /// <summary>
        /// Charge toute la coque planétaire en un seul passage.
        ///
        /// ALGORITHME : grille world-alignée au pas cs/2 (= 8 blocs).
        ///
        ///   Garantie mathématique de couverture complète :
        ///   • Chaque chunk face-local est un cube de côté cs=16 dans
        ///     l'espace face-local (les axes Rights/Normals/Forwards sont
        ///     orthonormaux, axial ET diagonal).
        ///   • Rayon de la sphère inscrite = cs/2 = 8.
        ///   • Distance max d'un point quelconque à son voisin de grille
        ///     avec pas h = cs/2 : h·√3/2 ≈ 6,93.
        ///   • 6,93 < 8 → tout cube face-local contient TOUJOURS au moins
        ///     un point de la grille à cs/2.
        ///   → Aucun chunk canonique ne peut être manqué.
        ///
        ///   Avec cs=16, pas=8 : maxS=10, boucle 21³=9261, ~2 000 dans la
        ///   coque après filtrage → totalement négligeable au démarrage.
        /// </summary>
        public void UpdateChunks()
        {
            if (_planetLoaded) return;

            int   cs       = VoxelData.ChunkWidth;
            float coreR    = generationConfig.HasValue ? generationConfig.Value.CoreRadius    : PlanetChunkGenerator.PlanetCoreRadius;
            float amp      = generationConfig.HasValue ? generationConfig.Value.SurfaceAmplitude : PlanetChunkGenerator.SurfaceAmplitude;
            float crust    = generationConfig.HasValue ? generationConfig.Value.CrustThickness   : PlanetChunkGenerator.CrustThickness;
            float halfDiag = cs * 0.8660254f;   // √3/2 * cs
            float shellMax = coreR + amp + 2f + halfDiag;
            float shellMin = coreR - crust - halfDiag;

            // Pas d'échantillonnage = cs/2 pour garantir la couverture complète.
            // +2 au lieu de +1 pour garantir les extrémités des faces axiales.
            float step = cs * 0.5f;
            int   maxS = Mathf.CeilToInt(shellMax / step) + 2;

            for (int dz = -maxS; dz <= maxS; dz++)
            for (int dy = -maxS; dy <= maxS; dy++)
            for (int dx = -maxS; dx <= maxS; dx++)
            {
                float fx = (dx + 0.5f) * step;
                float fy = (dy + 0.5f) * step;
                float fz = (dz + 0.5f) * step;
                float dist = Mathf.Sqrt(fx * fx + fy * fy + fz * fz);

                if (dist > shellMax || dist < shellMin) continue;

                // Chunk canonique du point (18-faces) — plusieurs points peuvent
                // mapper vers le même chunk : ContainsKey le déduplique.
                var sample = new Vector3(fx, fy, fz);
                FaceIndex fi = SphereFace.GetFace(sample);
                int       f  = (int)fi;

                int U = Mathf.FloorToInt(Vector3.Dot(sample, SphereFace.Rights  [f]) / cs);
                int V = Mathf.FloorToInt(Vector3.Dot(sample, SphereFace.Forwards[f]) / cs);
                int R = Mathf.FloorToInt(Vector3.Dot(sample, SphereFace.Normals [f]) / cs);

                var coord = new FaceChunkCoord(fi, U, V, R);
                if (!_chunks.ContainsKey(coord))
                    SpawnChunk(coord);
            }

            _planetLoaded = true;
        }

        // ── Lecture / Écriture de blocs (world-space) ─────────

        /// <summary>Retourne le ChunkRenderer qui contient worldPos, ou null.</summary>
        public ChunkRenderer GetChunkAt(Vector3 worldPos)
        {
            _chunks.TryGetValue(WorldToFaceChunk(worldPos), out ChunkRenderer cr);
            return cr;
        }

        /// <summary>
        /// Convertit une position world en coordonnées locales (lx, ly, lz)
        /// dans le chunk canonique de cette position.
        /// Propriété algébrique : retourne toujours [0, ChunkWidth-1]³.
        /// </summary>
        public Vector3Int WorldToLocalBlock(Vector3 worldPos)
        {
            int cs = VoxelData.ChunkWidth;
            FaceChunkCoord fc = WorldToFaceChunk(worldPos);
            int fi = (int)fc.Face;

            Vector3 rel = worldPos - PlanetCenter;
            int gu = Mathf.FloorToInt(Vector3.Dot(rel, SphereFace.Rights  [fi]));
            int gr = Mathf.FloorToInt(Vector3.Dot(rel, SphereFace.Normals [fi]));
            int gv = Mathf.FloorToInt(Vector3.Dot(rel, SphereFace.Forwards[fi]));

            return new Vector3Int(
                gu - fc.U * cs,   // local X  (selon Rights)
                gr - fc.R * cs,   // local Y  (radial, selon Normals)
                gv - fc.V * cs);  // local Z  (selon Forwards)
        }

        public bool BreakBlock(Vector3 worldPos)
        {
            ChunkRenderer cr = GetChunkAt(worldPos);
            if (cr == null) return false;
            Vector3Int lb = WorldToLocalBlock(worldPos);
            if (!BlockProperties.IsRenderable(cr.GetBlock(lb.x, lb.y, lb.z))) return false;
            cr.SetBlock(lb.x, lb.y, lb.z, BlockType.Air);
            RecordModification(WorldToFaceChunk(worldPos), lb.x, lb.y, lb.z, (byte)BlockType.Air);
            RebuildNeighbourChunks(worldPos);
            return true;
        }

        /// <summary>
        /// Variante directe pour détruire un bloc en utilisant le <see cref="ChunkRenderer"/>
        /// du collider touché par le raycast.
        /// Évite le bug de frontière de face : les troncs d'arbres sont placés dans le chunk
        /// générateur (via TrySetBlock) qui peut différer du chunk canonique renvoyé par
        /// <see cref="WorldToFaceChunk"/> (utilisant <see cref="SphereFace.GetFace"/>).
        /// </summary>
        public bool BreakBlock(ChunkRenderer cr, Vector3 worldPos)
        {
            if (cr == null) return BreakBlock(worldPos);   // fallback sûr

            Vector3 local = cr.transform.InverseTransformPoint(worldPos);
            int lx = Mathf.Clamp(Mathf.FloorToInt(local.x), 0, VoxelData.ChunkWidth  - 1);
            int ly = Mathf.Clamp(Mathf.FloorToInt(local.y), 0, VoxelData.ChunkHeight - 1);
            int lz = Mathf.Clamp(Mathf.FloorToInt(local.z), 0, VoxelData.ChunkWidth  - 1);

            if (!BlockProperties.IsRenderable(cr.GetBlock(lx, ly, lz))) return false;
            cr.SetBlock(lx, ly, lz, BlockType.Air);
            RecordModification(cr.ChunkCoord, lx, ly, lz, (byte)BlockType.Air);
            RebuildNeighbourChunks(worldPos);
            return true;
        }

        public bool PlaceBlock(Vector3 worldPos, BlockType type)
        {
            ChunkRenderer cr = GetChunkAt(worldPos);
            if (cr == null) return false;
            Vector3Int lb = WorldToLocalBlock(worldPos);
            if (BlockProperties.IsSolid(cr.GetBlock(lb.x, lb.y, lb.z))) return false;
            cr.SetBlock(lb.x, lb.y, lb.z, type);
            RecordModification(WorldToFaceChunk(worldPos), lb.x, lb.y, lb.z, (byte)type);
            RebuildNeighbourChunks(worldPos);
            return true;
        }

        // ── Sync réseau ───────────────────────────────────────

        /// <summary>
        /// Casse un bloc identifié par ses coordonnées réseau (FaceChunkCoord + local XYZ).
        /// Utilisé par le réseau (ServerManager / BlockSyncManager) pour appliquer
        /// une modification validée par le serveur.
        /// </summary>
        public bool ApplyNetworkBreak(FaceChunkCoord coord, int lx, int ly, int lz)
        {
            if (!_chunks.TryGetValue(coord, out ChunkRenderer cr)) return false;
            if (!BlockProperties.IsRenderable(cr.GetBlock(lx, ly, lz))) return false;
            cr.SetBlock(lx, ly, lz, BlockType.Air);
            RecordModification(coord, lx, ly, lz, (byte)BlockType.Air);
            Vector3 worldPos = cr.transform.TransformPoint(lx + 0.5f, ly + 0.5f, lz + 0.5f);
            cr.RebuildMesh();
            RebuildNeighbourChunks(worldPos);
            return true;
        }

        /// <summary>
        /// Pose un bloc identifié par ses coordonnées réseau.
        /// </summary>
        public bool ApplyNetworkPlace(FaceChunkCoord coord, int lx, int ly, int lz, BlockType type)
        {
            if (!_chunks.TryGetValue(coord, out ChunkRenderer cr)) return false;
            if (BlockProperties.IsSolid(cr.GetBlock(lx, ly, lz))) return false;
            cr.SetBlock(lx, ly, lz, type);
            RecordModification(coord, lx, ly, lz, (byte)type);
            Vector3 worldPos = cr.transform.TransformPoint(lx + 0.5f, ly + 0.5f, lz + 0.5f);
            cr.RebuildMesh();
            RebuildNeighbourChunks(worldPos);
            return true;
        }

        // ── Suivi des modifications (pour save/load) ──────────

        private void RecordModification(FaceChunkCoord coord, int lx, int ly, int lz, byte block)
        {
            if (!_modifications.TryGetValue(coord, out var dict))
            {
                dict = new Dictionary<int, byte>();
                _modifications[coord] = dict;
            }
            dict[lx | (ly << 8) | (lz << 16)] = block;
        }

        /// <summary>
        /// Retourne la liste de toutes les modifications appliquées à cette planète
        /// (blocs posés ou détruits par le joueur).
        /// </summary>
        public List<PlanetBlockMod> GetModifications()
        {
            var result = new List<PlanetBlockMod>();
            foreach (var kv in _modifications)
            foreach (var bkv in kv.Value)
            {
                int packed = bkv.Key;
                result.Add(new PlanetBlockMod
                {
                    face  = (int)kv.Key.Face,
                    cu    = kv.Key.U,
                    cv    = kv.Key.V,
                    cr    = kv.Key.R,
                    lx    = (byte)( packed        & 0xFF),
                    ly    = (byte)((packed >>  8) & 0xFF),
                    lz    = (byte)((packed >> 16) & 0xFF),
                    block = bkv.Value,
                });
            }
            return result;
        }

        /// <summary>
        /// Applique une liste de modifications enregistrées (chargement d'une save).
        /// Utilise SetBlockSilent + rebuild en batch pour minimiser les recomputs de mesh.
        /// </summary>
        public void ApplyModifications(List<PlanetBlockMod> mods)
        {
            if (mods == null || mods.Count == 0) return;

            var dirtyChunks = new HashSet<FaceChunkCoord>();

            foreach (var mod in mods)
            {
                var coord = new FaceChunkCoord((FaceIndex)mod.face, mod.cu, mod.cv, mod.cr);
                if (!_chunks.TryGetValue(coord, out ChunkRenderer cr)) continue;

                cr.SetBlockSilent(mod.lx, mod.ly, mod.lz, (BlockType)mod.block);
                RecordModification(coord, mod.lx, mod.ly, mod.lz, mod.block);
                dirtyChunks.Add(coord);
            }

            // Rebuild tous les chunks impactés + leurs voisins
            var toRebuild = new HashSet<FaceChunkCoord>(dirtyChunks);
            foreach (var coord in dirtyChunks)
            {
                Quaternion rot = SphereFace.GetRotation(coord.Face);
                Vector3 cs_vec = Vector3.one * VoxelData.ChunkWidth;
                // Centre approximatif du chunk
                int fi = (int)coord.Face;
                Vector3 center = PlanetCenter
                    + SphereFace.Rights  [fi] * (coord.U * VoxelData.ChunkWidth + VoxelData.ChunkWidth * 0.5f)
                    + SphereFace.Normals [fi] * (coord.R * VoxelData.ChunkWidth + VoxelData.ChunkWidth * 0.5f)
                    + SphereFace.Forwards[fi] * (coord.V * VoxelData.ChunkWidth + VoxelData.ChunkWidth * 0.5f);
                for (int f = 0; f < 6; f++)
                {
                    Vector3 offset = new Vector3(
                        VoxelData.FaceChecks[f, 0],
                        VoxelData.FaceChecks[f, 1],
                        VoxelData.FaceChecks[f, 2]);
                    FaceChunkCoord nb = WorldToFaceChunk(center + rot * offset);
                    if (!nb.Equals(coord)) toRebuild.Add(nb);
                }
            }

            foreach (var coord in toRebuild)
                if (_chunks.TryGetValue(coord, out ChunkRenderer cr))
                    cr.RebuildMesh();
        }

        // ── Interne ───────────────────────────────────────────

        /// <summary>
        /// Convertit une position world en FaceChunkCoord (face canonique + U/V/R).
        /// </summary>
        public FaceChunkCoord WorldToFaceChunk(Vector3 worldPos)
        {
            int cs = VoxelData.ChunkWidth;
            Vector3 rel = worldPos - PlanetCenter;
            FaceIndex fi = SphereFace.GetFace(rel);
            int face = (int)fi;

            int U = Mathf.FloorToInt(Vector3.Dot(rel, SphereFace.Rights  [face]) / cs);
            int V = Mathf.FloorToInt(Vector3.Dot(rel, SphereFace.Forwards[face]) / cs);
            int R = Mathf.FloorToInt(Vector3.Dot(rel, SphereFace.Normals [face]) / cs);

            return new FaceChunkCoord(fi, U, V, R);
        }

        /// <summary>
        /// Reconstruit les chunks voisins qui partagent une face avec le bloc modifié.
        /// Les offsets FaceChecks sont en espace local du chunk (local +Y = radial).
        /// Convertis en world-space via la rotation du chunk.
        /// </summary>
        private void RebuildNeighbourChunks(Vector3 modifiedWorldPos)
        {
            FaceChunkCoord selfCoord = WorldToFaceChunk(modifiedWorldPos);
            Quaternion rot = SphereFace.GetRotation(selfCoord.Face);

            for (int f = 0; f < 6; f++)
            {
                Vector3 localOffset = new Vector3(
                    VoxelData.FaceChecks[f, 0],
                    VoxelData.FaceChecks[f, 1],
                    VoxelData.FaceChecks[f, 2]);

                Vector3 neighbourWorldPos = modifiedWorldPos + rot * localOffset;
                FaceChunkCoord neighbourCoord = WorldToFaceChunk(neighbourWorldPos);

                if (neighbourCoord.Equals(selfCoord)) continue;

                if (_chunks.TryGetValue(neighbourCoord, out ChunkRenderer neighbour))
                    neighbour.RebuildMesh();
            }
        }

        private void SpawnChunk(FaceChunkCoord coord)
        {
            int cs   = VoxelData.ChunkWidth;
            int fi   = (int)coord.Face;

            Vector3 origin = PlanetCenter
                + SphereFace.Rights  [fi] * (coord.U * cs)
                + SphereFace.Normals [fi] * (coord.R * cs)
                + SphereFace.Forwards[fi] * (coord.V * cs);

            Quaternion rot = SphereFace.GetRotation(coord.Face);

            var go = new GameObject($"Chunk_{coord}");
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = origin;
            go.transform.rotation = rot;

            var mr = go.AddComponent<MeshRenderer>();
            if (blockMaterials != null && blockMaterials.Length > 0 && blockMaterials[0] != null)
            {
                mr.sharedMaterial = blockMaterials[0];
            }
            else
            {
                var sh = Shader.Find("AstroVoxel/BlockUnlit")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Texture");
                mr.sharedMaterial = new Material(sh);
            }

            var cr = go.AddComponent<ChunkRenderer>();
            cr.InitFromWorld(origin, PlanetCenter, this, rot, coord.Face, blockMaterials,
                             null, null, true, generationConfig);
            cr.ChunkCoord = coord;

            _chunks[coord] = cr;
        }

        // ── Chargement async (pour planètes distantes) ────────────

        /// <summary>
        /// Charge les chunks progressivement sur plusieurs frames.
        /// Utilisé par InfinitePlanetSystem pour éviter les gels.
        /// </summary>
        public IEnumerator LoadChunksAsync(int chunksPerFrame = 6)
        {
            if (_planetLoaded) yield break;

            int   cs       = VoxelData.ChunkWidth;
            float coreR    = generationConfig.HasValue ? generationConfig.Value.CoreRadius      : PlanetChunkGenerator.PlanetCoreRadius;
            float amp      = generationConfig.HasValue ? generationConfig.Value.SurfaceAmplitude : PlanetChunkGenerator.SurfaceAmplitude;
            float crust    = generationConfig.HasValue ? generationConfig.Value.CrustThickness   : PlanetChunkGenerator.CrustThickness;

            float halfDiag = cs * 0.8660254f;
            float shellMax = coreR + amp + 2f + halfDiag;
            float shellMin = coreR - crust - halfDiag;

            float step = cs * 0.5f;
            int   maxS = Mathf.CeilToInt(shellMax / step) + 2;

            int spawned = 0;
            for (int dz = -maxS; dz <= maxS; dz++)
            for (int dy = -maxS; dy <= maxS; dy++)
            for (int dx = -maxS; dx <= maxS; dx++)
            {
                float fx = (dx + 0.5f) * step;
                float fy = (dy + 0.5f) * step;
                float fz = (dz + 0.5f) * step;
                float dist = Mathf.Sqrt(fx * fx + fy * fy + fz * fz);

                if (dist > shellMax || dist < shellMin) continue;

                var sample = new Vector3(fx, fy, fz);
                FaceIndex fi2 = SphereFace.GetFace(sample);
                int f2 = (int)fi2;

                int U = Mathf.FloorToInt(Vector3.Dot(sample, SphereFace.Rights  [f2]) / cs);
                int V = Mathf.FloorToInt(Vector3.Dot(sample, SphereFace.Forwards[f2]) / cs);
                int R = Mathf.FloorToInt(Vector3.Dot(sample, SphereFace.Normals [f2]) / cs);

                var coord = new FaceChunkCoord(fi2, U, V, R);
                if (!_chunks.ContainsKey(coord))
                {
                    SpawnChunk(coord);
                    spawned++;
                    if (spawned % chunksPerFrame == 0) yield return null;
                }
            }

            _planetLoaded = true;
        }

        // ── Cycle de vie ──────────────────────────────────────

        private void Start()
        {
            if (!manualLoad) UpdateChunks();
        }
        private void Update() { /* chunks statiques ; rien à faire chaque frame */ }
    }
}

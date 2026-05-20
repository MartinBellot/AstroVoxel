// ============================================================
//  ChunkRenderer.cs
//  MonoBehaviour : initialise, génère et affiche un Chunk.
//  Respecte le principe : 1 Chunk = 1 GameObject + 1 MeshFilter
//  + 1 MeshCollider. Zéro Instantiate de blocs.
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstroVoxel.VoxelEngine
{
    /// <summary>
    /// Point d'entrée Unity pour un Chunk.
    /// Coordonne <see cref="ChunkData"/> (données) et
    /// <see cref="ChunkMeshBuilder"/> (vue) conformément au
    /// principe de séparation Modèle / Vue.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public sealed class ChunkRenderer : MonoBehaviour
    {
        // ── Références composants ─────────────────────────────
        private MeshFilter   _meshFilter;
        private MeshRenderer _meshRenderer;
        private MeshCollider _meshCollider;

        // ── Données / View ───────────────────────────────────
        private ChunkData _chunkData;
        private MeshData  _meshData;
        private Mesh      _mesh;
        // Mesh de collision séparé : solides uniquement, sans les cross-blocks (plantes).
        // Le joueur traversera les ShortGrass sans collision physique.
        private Mesh              _collisionMesh;
        private readonly List<Vector3> _collisionVerts = new List<Vector3>(2048);
        private readonly List<int>     _collisionTris  = new List<int>(6144);

        // Liste des renderingIds actifs (réutilisée pour éviter les allocations)
        private readonly List<int> _activeRids = new List<int>(32);

        // Matériau de repli (gris neutre) si le registry n'est pas encore construit
        private static Material _fallbackMat;

        // ── Mode monde planétaire ─────────────────────────────
        private Vector3    _planetCenter;
        private Material[] _blockMaterials;   // index = (byte)BlockType
        private IVoxelWorld _world;           // référence pour la lookup des voisins réels
        private Quaternion _chunkRotation;    // rotation de ce chunk (local +Y = radial sortant)
        private FaceIndex  _chunkFace;        // face canonique (masque de génération)

        /// <summary>
        /// Coordonnée de chunk dans la grille planétaire.
        /// Assignée par <see cref="PlanetWorld.SpawnChunk"/> après création.
        /// Utilisée pour enregistrer les modifications (BreakBlock/PlaceBlock) dans
        /// le bon chunk même lorsque la position world est proche d'une frontière de face.
        /// </summary>
        public FaceChunkCoord ChunkCoord { get; set; }
        private System.Func<Vector3, byte> _oobBlockProvider;  // fournisseur de blocs hors-chunk
        private bool       _useRadialOrientation = true;       // false pour astéroïdes (chunks cubiques)
        // ── Inspector (mode plat standalone) ─────────────────
        [Header("Génération du terrain (mode standalone)")]
        [Tooltip("Nombre de couches solides depuis le bas du Chunk.")]
        [SerializeField] private int solidLayers = 3;

        [Tooltip("Type de bloc utilisé pour les couches solides.")]
        [SerializeField] private BlockType fillBlockType = BlockType.Stone;

        // ── Cycle de vie Unity ────────────────────────────────

        private void Awake()
        {
            _meshFilter   = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _meshCollider = GetComponent<MeshCollider>();

            _mesh          = new Mesh { name = "ChunkMesh" };
            _collisionMesh = new Mesh { name = "ChunkCollision" };
            _meshData      = new MeshData();

            if (_fallbackMat == null)
            {
                // Utilise le même shader que le BlockTextureRegistry pour éviter les matériaux roses
                var fbShader = Shader.Find("AstroVoxel/BlockUnlit")
                            ?? Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Unlit/Texture");
                _fallbackMat = new Material(fbShader) { color = new Color(0.5f, 0.45f, 0.4f) };
            }
        }

        private void Start()
        {
            // Si InitFromWorld a déjà été appelé, la génération est faite.
            if (_chunkData == null)
            {
                GenerateChunk();
                RebuildMesh();
            }
        }

        // ── API publique ──────────────────────────────────────

        /// <summary>
        /// Remplace un bloc à la coordonnée locale et recalcule le mesh.
        /// Prévu pour la destruction/construction en jeu.
        /// </summary>
        public void SetBlock(int x, int y, int z, BlockType type)
        {
            _chunkData.SetBlock(x, y, z, type);
            RebuildMesh();
        }

        /// <summary>Retourne l'ID du bloc à la coordonnée locale.</summary>
        public byte GetBlock(int x, int y, int z) => _chunkData.GetBlock(x, y, z);

        /// <summary>
        /// Définit un bloc SANS reconstruire le mesh.
        /// À utiliser lors de chargements en batch ; appeler <see cref="RebuildMesh"/> ensuite.
        /// </summary>
        public void SetBlockSilent(int x, int y, int z, BlockType type)
            => _chunkData?.SetBlock(x, y, z, type);

        /// <summary>
        /// Initialise ce chunk en mode planétaire (18-Face Cube-Sphère Octaédrique).
        /// Appelé par <see cref="PlanetWorld"/> juste après AddComponent.
        /// </summary>
        public void InitFromWorld(Vector3 worldOrigin, Vector3 planetCenter, IVoxelWorld world, Quaternion chunkRotation, FaceIndex chunkFace, Material[] blockMaterials = null, System.Func<Vector3, byte> oobProvider = null, ChunkData preGeneratedData = null, bool useRadialOrientation = true, AstroVoxel.VoxelEngine.PlanetGenerationConfig? genConfig = null)
        {
            _planetCenter   = planetCenter;
            _blockMaterials = blockMaterials;
            _world          = world;
            _chunkRotation  = chunkRotation;
            _chunkFace      = chunkFace;
            _useRadialOrientation = useRadialOrientation;

            // Résoudre le fournisseur OOB : priorité au genConfig, puis au oobProvider explicite
            if (genConfig.HasValue)
            {
                var cfg    = genConfig.Value;
                var center = planetCenter;
                _oobBlockProvider = oobProvider ?? (pos => (byte)PlanetChunkGenerator.GetBlockType(pos, center, cfg));
            }
            else
            {
                _oobBlockProvider = oobProvider ?? (pos => (byte)PlanetChunkGenerator.GetBlockType(pos, _planetCenter));
            }

            // Awake peut ne pas encore avoir été appelé si AddComponent vient de se faire
            if (_mesh == null)
            {
                _meshFilter   = GetComponent<MeshFilter>();
                _meshRenderer = GetComponent<MeshRenderer>();
                _meshCollider = GetComponent<MeshCollider>();
                _mesh          = new Mesh { name = "ChunkMesh" };
                _collisionMesh = new Mesh { name = "ChunkCollision" };
                _meshData      = new MeshData();
                if (_fallbackMat == null)
                {
                    _fallbackMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Diffuse"));
                    _fallbackMat.color = new Color(0.5f, 0.45f, 0.4f);
                }
            }

            if (preGeneratedData != null)
            {
                _chunkData = preGeneratedData;
            }
            else if (genConfig.HasValue)
            {
                _chunkData = new ChunkData();
                var cfg = genConfig.Value;
                PlanetChunkGenerator.Generate(_chunkData, worldOrigin, _planetCenter, _chunkRotation, _chunkFace, in cfg);
            }
            else
            {
                _chunkData = new ChunkData();
                PlanetChunkGenerator.Generate(_chunkData, worldOrigin, _planetCenter, _chunkRotation, _chunkFace);
            }

            RebuildMesh();
        }

        // ── Génération ────────────────────────────────────────

        /// <summary>
        /// Délégué passé à ChunkMeshBuilder pour résoudre les voxels hors-chunk.
        /// Interroge les données RÉELLES du chunk voisin chargé (incluant les
        /// modifications du joueur). Repli sur le générateur si non chargé.
        /// Paramètres : coordonnées locales OOB (peuvent être négatives ou >= Width).
        /// </summary>
        private byte GetNeighbourForMesh(int lx, int ly, int lz)
        {
            // Convertit coords locales OOB → position world du centre du voxel.
            // TransformPoint applique position + rotation du chunk, sans mise à l'échelle.
            Vector3 worldPos = transform.TransformPoint(lx + 0.5f, ly + 0.5f, lz + 0.5f);

            if (_world != null)
            {
                ChunkRenderer neighbour = _world.GetChunkAt(worldPos);
                if (neighbour != null)
                {
                    Vector3Int lb = _world.WorldToLocalBlock(worldPos);
                    return neighbour.GetBlock(lb.x, lb.y, lb.z);
                }
            }

            // Chunk voisin non chargé → repli sur le fournisseur OOB
            return _oobBlockProvider != null
                ? _oobBlockProvider(worldPos)
                : (byte)PlanetChunkGenerator.GetBlockType(worldPos, _planetCenter);
        }

        /// <summary>
        /// Remplit le ChunkData : <see cref="solidLayers"/> couches de blocs
        /// solides en bas, Air au-dessus.
        /// </summary>
        private void GenerateChunk()
        {
            _chunkData = new ChunkData();

            int topSolidLayer = solidLayers - 1;
            for (int y = 0; y <= topSolidLayer && y < VoxelData.ChunkHeight; y++)
            {
                byte blockId;
                if (y == topSolidLayer)
                    blockId = (byte)BlockType.Grass;         // surface
                else if (y == topSolidLayer - 1)
                    blockId = (byte)BlockType.Dirt;          // sous-surface
                else
                    blockId = (byte)fillBlockType;           // profondeur

                _chunkData.FillLayer(y, blockId);
            }
        }

        /// <summary>
        /// Recalcule le mesh à partir des données et l'applique aux composants.
        /// Public pour permettre à PlanetWorld de forcer le rebuild des chunks voisins
        /// après une modification de bloc en bordure de chunk.
        /// </summary>
        public void RebuildMesh()
        {
            _meshData.Clear();
            // En mode planétaire, on passe la rotation + l'origine world du chunk
            // et le centre de la planète afin que ChunkMeshBuilder oriente les
            // blocs multi-faces (Grass, etc.) par rapport au CENTRE de la planète
            // — la face « top » du grass pointe toujours radialement vers
            // l'extérieur, indépendamment de la face cube-sphère du chunk.
            if (_world != null)
            {
                // Utilise le centre courant du monde (dynamique pour les astéroïdes en orbite)
                // plutôt que _planetCenter qui est fixé à l'initialisation.
                Vector3 currentCenter = _world.WorldCenter;
                ChunkMeshBuilder.Build(_chunkData, _meshData, GetNeighbourForMesh,
                    useRadialOrientation: _useRadialOrientation,
                    chunkRotation:        _chunkRotation,
                    chunkOriginWorld:     transform.position,
                    planetCenter:         currentCenter);
            }
            else
            {
                ChunkMeshBuilder.Build(_chunkData, _meshData, GetNeighbourForMesh);
            }

            // ── Collecte des sous-meshes non vides ───────────────
            // Seuls les renderingIds ayant des triangles deviennent des submeshes Unity.
            // Évite d'avoir 256 submeshes vides qui pèsent inutilement sur le GPU.
            _activeRids.Clear();
            for (int i = 0; i < _meshData.Triangles.Length; i++)
                if (_meshData.Triangles[i].Count > 0)
                    _activeRids.Add(i);

            _mesh.Clear();
            _mesh.SetVertices(_meshData.Vertices);
            _mesh.subMeshCount = _activeRids.Count;
            for (int s = 0; s < _activeRids.Count; s++)
                _mesh.SetTriangles(_meshData.Triangles[_activeRids[s]], s);
            _mesh.SetUVs(0, _meshData.UVs);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            // _mesh.Optimize() retiré : très coûteux et appelé à chaque rebuild (placement/destruction de blocs).
            // Doit être invoqué manuellement une seule fois sur un mesh final/statique.

            _meshFilter.sharedMesh = _mesh;

            // ── Applique les matériaux (un par submesh actif) ────
            if (_meshRenderer != null)
            {
                var mats = new Material[_activeRids.Count];
                for (int s = 0; s < _activeRids.Count; s++)
                {
                    int rid = _activeRids[s];
                    mats[s] = (_blockMaterials != null && rid < _blockMaterials.Length && _blockMaterials[rid] != null)
                        ? _blockMaterials[rid]
                        : _fallbackMat;
                }
                _meshRenderer.sharedMaterials = mats;
            }

            // Force le re-bake du MeshCollider.
            // Utilise un mesh de collision séparé (solides uniquement) pour que
            // le joueur traverse les plantes (ShortGrass…) sans être bloqué.
            _collisionVerts.Clear();
            _collisionTris.Clear();
            ChunkMeshBuilder.BuildCollision(_chunkData, _collisionVerts, _collisionTris, GetNeighbourForMesh);
            _collisionMesh.Clear();
            if (_collisionVerts.Count > 0)
            {
                _collisionMesh.SetVertices(_collisionVerts);
                _collisionMesh.SetTriangles(_collisionTris, 0);
                _collisionMesh.RecalculateBounds();
            }
            _meshCollider.sharedMesh = null;
            if (_collisionVerts.Count > 0)
                _meshCollider.sharedMesh = _collisionMesh;
        }

        // ── Nettoyage ─────────────────────────────────────────

        private void OnDestroy()
        {
            if (_mesh != null)          Destroy(_mesh);
            if (_collisionMesh != null) Destroy(_collisionMesh);
        }
    }
}

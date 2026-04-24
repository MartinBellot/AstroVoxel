// ============================================================
//  ChunkRenderer.cs
//  MonoBehaviour : initialise, génère et affiche un Chunk.
//  Respecte le principe : 1 Chunk = 1 GameObject + 1 MeshFilter
//  + 1 MeshCollider. Zéro Instantiate de blocs.
// ============================================================

using System;
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

        // ── Mode monde planétaire ─────────────────────────────
        private Vector3    _planetCenter;
        private Material[] _blockMaterials;   // index = (byte)BlockType
        private PlanetWorld _world;           // référence pour la lookup des voisins réels

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

            _mesh     = new Mesh { name = "ChunkMesh" };
            _meshData = new MeshData();
            _meshData = new MeshData();
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
        /// Initialise ce chunk en mode planétaire (grille 3D axis-aligned).
        /// Appelé par <see cref="PlanetWorld"/> juste après AddComponent.
        /// </summary>
        public void InitFromWorld(Vector3 worldOrigin, Vector3 planetCenter, PlanetWorld world, Material[] blockMaterials = null)
        {
            _planetCenter   = planetCenter;
            _blockMaterials = blockMaterials;
            _world          = world;

            // Awake peut ne pas encore avoir été appelé si AddComponent vient de se faire
            if (_mesh == null)
            {
                _meshFilter   = GetComponent<MeshFilter>();
                _meshRenderer = GetComponent<MeshRenderer>();
                _meshCollider = GetComponent<MeshCollider>();
                _mesh     = new Mesh { name = "ChunkMesh" };
                _meshData = new MeshData();
            }

            _chunkData = new ChunkData();
            PlanetChunkGenerator.Generate(_chunkData, worldOrigin, _planetCenter);

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
            // Convertit coords locales OOB → position world du centre du voxel
            Vector3 worldPos = transform.position + new Vector3(lx + 0.5f, ly + 0.5f, lz + 0.5f);

            if (_world != null)
            {
                ChunkRenderer neighbour = _world.GetChunkAt(worldPos);
                if (neighbour != null)
                {
                    Vector3Int lb = _world.WorldToLocalBlock(worldPos);
                    return neighbour.GetBlock(lb.x, lb.y, lb.z);
                }
            }

            // Chunk voisin non chargé → repli sur le générateur
            return PlanetChunkGenerator.GetBlockType(worldPos, _planetCenter);
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
            ChunkMeshBuilder.Build(_chunkData, _meshData, GetNeighbourForMesh);

            int subCount = _meshData.Triangles.Length;

            _mesh.Clear();
            _mesh.SetVertices(_meshData.Vertices);
            _mesh.subMeshCount = subCount;
            for (int i = 0; i < subCount; i++)
                _mesh.SetTriangles(_meshData.Triangles[i], i);
            _mesh.SetUVs(0, _meshData.UVs);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            _mesh.Optimize();

            _meshFilter.sharedMesh = _mesh;

            // Applique les matériaux par bloc (un par submesh)
            if (_meshRenderer != null && _blockMaterials != null)
                _meshRenderer.sharedMaterials = _blockMaterials;

            // Force le re-bake du MeshCollider.
            _meshCollider.sharedMesh = null;
            _meshCollider.sharedMesh = _mesh;
        }

        // ── Nettoyage ─────────────────────────────────────────

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}

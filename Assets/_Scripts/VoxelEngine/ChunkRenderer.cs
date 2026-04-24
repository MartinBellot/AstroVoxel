// ============================================================
//  ChunkRenderer.cs
//  MonoBehaviour : initialise, génère et affiche un Chunk.
//  Respecte le principe : 1 Chunk = 1 GameObject + 1 MeshFilter
//  + 1 MeshCollider. Zéro Instantiate de blocs.
// ============================================================

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
        private MeshCollider _meshCollider;

        // ── Données / View ────────────────────────────────────
        private ChunkData _chunkData;
        private MeshData  _meshData;
        private Mesh      _mesh;

        // ── Mode monde planétaire ─────────────────────────────
        private Vector3 _chunkUp, _chunkRight, _chunkForward, _planetCenter;

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
            _meshCollider = GetComponent<MeshCollider>();

            _mesh     = new Mesh { name = "ChunkMesh" };
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
        /// Initialise ce chunk en mode planétaire et génère immédiatement le mesh.
        /// Appelé par <see cref="PlanetWorld"/> à la création du chunk.
        /// </summary>
        public void InitFromWorld(Vector3 up, Vector3 right, Vector3 forward, Vector3 planetCenter)
        {
            _chunkUp      = up;
            _chunkRight   = right;
            _chunkForward = forward;
            _planetCenter = planetCenter;

            // Awake peut ne pas encore avoir été appelé si AddComponent vient de se faire
            if (_mesh == null)
            {
                _meshFilter   = GetComponent<MeshFilter>();
                _meshCollider = GetComponent<MeshCollider>();
                _mesh     = new Mesh { name = "ChunkMesh" };
                _meshData = new MeshData();
            }

            _chunkData = new ChunkData();
            PlanetChunkGenerator.Generate(
                _chunkData,
                transform.position,
                _planetCenter,
                _chunkUp,
                _chunkRight,
                _chunkForward);

            RebuildMesh();
        }

        // ── Génération ────────────────────────────────────────

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
        /// </summary>
        private void RebuildMesh()
        {
            _meshData.Clear();
            ChunkMeshBuilder.Build(_chunkData, _meshData);

            _mesh.Clear();
            _mesh.SetVertices(_meshData.Vertices);
            _mesh.SetTriangles(_meshData.Triangles, 0);
            _mesh.SetUVs(0, _meshData.UVs);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            // Optimise le mesh pour la lecture GPU (utile avec le collider).
            _mesh.Optimize();

            _meshFilter.sharedMesh   = _mesh;
            _meshCollider.sharedMesh = _mesh;
        }

        // ── Nettoyage ─────────────────────────────────────────

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}

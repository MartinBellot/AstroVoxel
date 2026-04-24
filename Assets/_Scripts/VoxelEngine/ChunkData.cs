// ============================================================
//  ChunkData.cs
//  Modèle de données pur d'un Chunk : tableau de blocs + accès
//  sécurisé. AUCUNE dépendance à UnityEngine → migratable Job System.
// ============================================================

namespace AstroVoxel.VoxelEngine
{
    /// <summary>
    /// Contient les données brutes d'un Chunk sous forme d'un tableau 1D
    /// de bytes (ID de blocs). Accès via coordonnées locales (x, y, z).
    /// Cette classe est volontairement découplée de tout MonoBehaviour
    /// afin de faciliter la migration vers le C# Job System.
    /// </summary>
    public sealed class ChunkData
    {
        // Tableau 1D — layout : index = x + ChunkWidth * (y + ChunkHeight * z)
        private readonly byte[] _blocks;

        // Dimensions mises en cache pour éviter les lectures statiques répétées
        private readonly int _width;
        private readonly int _height;

        public int Width  => _width;
        public int Height => _height;

        // ── Constructeurs ─────────────────────────────────────

        /// <summary>Crée un Chunk vide (tout Air).</summary>
        public ChunkData()
        {
            _width  = VoxelData.ChunkWidth;
            _height = VoxelData.ChunkHeight;
            _blocks = new byte[VoxelData.ChunkVolume];
        }

        /// <summary>Crée un Chunk avec des dimensions personnalisées (utile pour les tests).</summary>
        public ChunkData(int width, int height)
        {
            _width  = width;
            _height = height;
            _blocks = new byte[width * height * width];
        }

        // ── Indexation ────────────────────────────────────────

        /// <summary>
        /// Convertit des coordonnées (x, y, z) locales en index 1D.
        /// Pas de vérification des limites — utiliser IsInBounds au préalable.
        /// </summary>
        private int IndexOf(int x, int y, int z) => x + _width * (y + _height * z);

        // ── Accès sécurisés ───────────────────────────────────

        /// <summary>Vérifie si les coordonnées locales sont dans les limites du Chunk.</summary>
        public bool IsInBounds(int x, int y, int z)
        {
            return x >= 0 && x < _width
                && y >= 0 && y < _height
                && z >= 0 && z < _width;
        }

        /// <summary>
        /// Retourne l'ID de bloc à la position locale.
        /// Retourne <see cref="BlockType.Air"/> (0) si hors limites.
        /// </summary>
        public byte GetBlock(int x, int y, int z)
        {
            if (!IsInBounds(x, y, z)) return (byte)BlockType.Air;
            return _blocks[IndexOf(x, y, z)];
        }

        /// <summary>
        /// Définit l'ID de bloc à la position locale.
        /// Silencieux si hors limites (pas d'exception → thread-safe en lecture seule).
        /// </summary>
        public void SetBlock(int x, int y, int z, byte blockId)
        {
            if (!IsInBounds(x, y, z)) return;
            _blocks[IndexOf(x, y, z)] = blockId;
        }

        /// <summary>Surcharge pratique acceptant un <see cref="BlockType"/> typé.</summary>
        public void SetBlock(int x, int y, int z, BlockType type) => SetBlock(x, y, z, (byte)type);

        // ── Utilitaires de génération ─────────────────────────

        /// <summary>
        /// Remplit une tranche horizontale entière avec un type de bloc.
        /// </summary>
        public void FillLayer(int y, byte blockId)
        {
            if (y < 0 || y >= _height) return;
            for (int z = 0; z < _width; z++)
            for (int x = 0; x < _width; x++)
                _blocks[IndexOf(x, y, z)] = blockId;
        }

        /// <summary>
        /// Remplit toutes les couches de 0 à <paramref name="topY"/> inclus avec un type de bloc.
        /// </summary>
        public void FillLayers(int topY, byte blockId)
        {
            int clampedTop = topY < _height ? topY : _height - 1;
            for (int y = 0; y <= clampedTop; y++)
                FillLayer(y, blockId);
        }

        /// <summary>
        /// Remet tout le Chunk à l'état Air.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _blocks.Length; i++)
                _blocks[i] = (byte)BlockType.Air;
        }
    }
}

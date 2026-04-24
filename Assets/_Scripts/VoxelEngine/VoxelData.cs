// ============================================================
//  VoxelData.cs
//  Constantes globales et définitions des types de blocs.
//  Aucun état mutant — utilisable depuis n'importe quel thread.
// ============================================================

namespace AstroVoxel.VoxelEngine
{
    /// <summary>
    /// Constantes immuables du moteur de voxels.
    /// Toutes les dimensions sont en nombre de blocs.
    /// </summary>
    public static class VoxelData
    {
        // ── Dimensions d'un Chunk ────────────────────────────
        public const int ChunkWidth  = 16;   // X & Z
        public const int ChunkHeight = 32;   // Y — hauteur adaptée à l'épaisseur de croûte planétaire

        // Nombre total de blocs dans un Chunk (utilisé pour les tableaux 1D).
        public const int ChunkVolume = ChunkWidth * ChunkHeight * ChunkWidth;

        // ── Texture Atlas ────────────────────────────────────
        // Nombre de tuiles par rangée dans l'atlas de textures.
        public const int TextureAtlasSizeInBlocks = 4;
        public const float NormalizedBlockTextureSize = 1f / TextureAtlasSizeInBlocks;

        // ── Directions des 6 faces (ordre canonique) ─────────
        // 0=Top  1=Bottom  2=Front(+Z)  3=Back(-Z)  4=Right(+X)  5=Left(-X)
        public static readonly int[,] FaceChecks = new int[6, 3]
        {
            {  0,  1,  0 },   // Top
            {  0, -1,  0 },   // Bottom
            {  0,  0,  1 },   // Front
            {  0,  0, -1 },   // Back
            {  1,  0,  0 },   // Right
            { -1,  0,  0 },   // Left
        };

        // Vertices locaux des 6 faces (sens anti-horaire vu de l'extérieur).
        // Chaque face : 4 vertices, index dans le tableau de vertices de base.
        public static readonly int[,] VoxelTris = new int[6, 4]
        {
            { 1, 5, 0, 4 },   // Top
            { 3, 7, 2, 6 },   // Bottom  (ordre inversé = face vers le bas)
            { 4, 5, 7, 6 },   // Front
            { 0, 1, 3, 2 },   // Back
            { 5, 1, 6, 2 },   // Right
            { 4, 0, 7, 3 },   // Left
        };

        // 8 coins d'un cube unité.
        public static readonly float[,] VoxelVerts = new float[8, 3]
        {
            { 0, 0, 0 },   // 0
            { 1, 0, 0 },   // 1
            { 0, 1, 0 },   // 2
            { 1, 1, 0 },   // 3
            { 0, 0, 1 },   // 4
            { 1, 0, 1 },   // 5
            { 0, 1, 1 },   // 6
            { 1, 1, 1 },   // 7
        };

        // UVs des 4 vertices d'une face (ordre correspondant à VoxelTris).
        public static readonly float[,] VoxelUVs = new float[4, 2]
        {
            { 0, 0 },
            { 0, 1 },
            { 1, 0 },
            { 1, 1 },
        };
    }

    // ============================================================
    //  BlockType
    //  Définit tous les types de blocs du jeu.
    //  Le byte ID doit correspondre à l'index dans la liste.
    // ============================================================

    /// <summary>
    /// Identifiants de blocs sur 8 bits (max 255 types de blocs).
    /// </summary>
    public enum BlockType : byte
    {
        Air   = 0,
        Stone = 1,
        Dirt  = 2,
        Grass = 3,
        Sand  = 4,
        Wood  = 5,
        Leaves = 6,
    }

    /// <summary>
    /// Propriétés statiques associées à chaque type de bloc.
    /// Évite les branchements en remplaçant les switch par un tableau.
    /// </summary>
    public static class BlockProperties
    {
        private static readonly bool[] _isSolid = new bool[]
        {
            false, // Air
            true,  // Stone
            true,  // Dirt
            true,  // Grass
            true,  // Sand
            true,  // Wood
            true,  // Leaves
        };

        /// <summary>Retourne true si le bloc doit bloquer la génération des faces adjacentes.</summary>
        public static bool IsSolid(byte blockId)
        {
            if (blockId >= _isSolid.Length) return false;
            return _isSolid[blockId];
        }

        public static bool IsSolid(BlockType type) => IsSolid((byte)type);
    }
}

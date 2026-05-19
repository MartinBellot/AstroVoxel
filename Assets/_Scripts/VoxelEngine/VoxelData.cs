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
        public const int ChunkWidth  = 16;   // X
        public const int ChunkHeight = 16;   // Y — cube uniforme pour grille 3D sphérique
        public const int ChunkDepth  = 16;   // Z

        // Nombre total de blocs dans un Chunk (utilisé pour les tableaux 1D).
        public const int ChunkVolume = ChunkWidth * ChunkHeight * ChunkDepth;

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

        // Vertices locaux des 6 faces (CCW vu de l'extérieur, winding cohérent
        // avec le pattern de triangulation (0,1,2)+(2,1,3) de AddFace).
        // Ordre : v0, v0+A, v0+B, v0+A+B  où  A×B = normale sortante.
        // Vérifié par cross-product pour chaque face.
        public static readonly int[,] VoxelTris = new int[6, 4]
        {
            { 2, 6, 3, 7 },   // Top    (+Y)  normal=(0,+1,0) ✓
            { 0, 1, 4, 5 },   // Bottom (-Y)  normal=(0,-1,0) ✓
            { 4, 5, 6, 7 },   // Front  (+Z)  normal=(0,0,+1) ✓
            { 0, 2, 1, 3 },   // Back   (-Z)  normal=(0,0,-1) ✓
            { 1, 3, 5, 7 },   // Right  (+X)  normal=(+1,0,0) ✓
            { 0, 4, 2, 6 },   // Left   (-X)  normal=(-1,0,0) ✓
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
    //  Identifiants de blocs sur 8 bits (max 255 types).
    //  0-91   : blocs stockés dans les chunks (données monde).
    //  200-216: IDs de rendu internes (face-variants, jamais stockés).
    // ============================================================

    /// <summary>
    /// Identifiants de blocs sur 8 bits (max 255 types de blocs).
    /// </summary>
    public enum BlockType : byte
    {
        // ── Blocs de base ─────────────────────────────────────
        Air   = 0,
        Stone = 1,
        Dirt  = 2,
        Grass = 3,
        Sand  = 4,
        Wood  = 5,   // OakLog (compatibilité ascendante)
        Leaves = 6,  // OakLeaves

        // ── Pierre et variantes ───────────────────────────────
        Cobblestone        = 7,
        MossyCobblestone   = 8,
        Andesite           = 9,
        PolishedAndesite   = 10,
        Diorite            = 11,
        PolishedDiorite    = 12,
        Granite            = 13,
        PolishedGranite    = 14,
        StoneBricks        = 15,
        CrackedStoneBricks = 16,
        MossyStoneBricks   = 17,
        ChiseledStoneBricks= 18,
        SmoothStone        = 19,

        // ── Terre et sol ──────────────────────────────────────
        Gravel    = 20,
        Clay      = 21,
        CoarseDirt= 22,
        Podzol    = 23,   // multi-face
        Mud       = 24,
        MudBricks = 25,

        // ── Sable et grès ─────────────────────────────────────
        RedSand       = 26,
        Sandstone     = 27,   // multi-face
        RedSandstone  = 28,   // multi-face

        // ── Blocs de bois – planches ──────────────────────────
        OakPlanks    = 29,
        SprucePlanks = 30,
        BirchPlanks  = 31,
        JunglePlanks = 32,
        AcaciaPlanks = 33,
        DarkOakPlanks= 34,
        MangrovePlanks=35,
        CherryPlanks = 36,
        CrimsonPlanks= 37,
        WarpedPlanks = 38,
        BambooPlanks = 39,

        // ── Blocs de bois – troncs ────────────────────────────
        // Wood=5 = OakLog (conservé)
        SpruceLog  = 40,
        BirchLog   = 41,
        JungleLog  = 42,
        AcaciaLog  = 43,
        DarkOakLog = 44,
        MangroveLog= 45,
        CherryLog  = 46,

        // ── Feuillages ────────────────────────────────────────
        // Leaves=6 = OakLeaves (conservé)
        SpruceLeaves  = 47,
        BirchLeaves   = 48,
        JungleLeaves  = 49,
        AcaciaLeaves  = 50,
        DarkOakLeaves = 51,
        MangroveLeaves= 52,
        CherryLeaves  = 53,

        // ── Blocs spéciaux (overworld) ────────────────────────
        Bedrock   = 54,
        Obsidian  = 55,
        Bricks    = 56,
        Ice       = 57,
        PackedIce = 58,
        BlueIce   = 59,

        // ── Nether ────────────────────────────────────────────
        NetherBricks   = 60,
        RedNetherBricks= 61,
        Netherrack     = 62,
        SoulSand       = 63,
        SoulSoil       = 64,
        Glowstone      = 65,

        // ── End ───────────────────────────────────────────────
        EndStone    = 66,
        Blackstone  = 67,
        PurpurBlock = 68,
        QuartzBricks= 69,

        // ── Deepslate ─────────────────────────────────────────
        Deepslate       = 70,   // multi-face
        CobbledDeepslate= 71,
        DeepSlateBricks = 72,
        DeepSlateTiles  = 73,

        // ── Minerais ──────────────────────────────────────────
        CoalOre    = 74,
        IronOre    = 75,
        CopperOre  = 76,
        GoldOre    = 77,
        LapisOre   = 78,
        RedstoneOre= 79,
        DiamondOre = 80,
        EmeraldOre = 81,

        // ── Blocs de minerais ─────────────────────────────────
        CoalBlock    = 82,
        IronBlock    = 83,
        GoldBlock    = 84,
        DiamondBlock = 85,
        EmeraldBlock = 86,
        LapisBlock   = 87,
        RedstoneBlock= 88,
        NetheriteBlock=89,
        CopperBlock  = 90,
        AmethystBlock= 91,

        // ── Blocs spéciaux biomes (cactus, magma, mousse, champignons…) ────
        Cactus             = 92,   // multi-face (top/side/bottom)
        MagmaBlock         = 93,   // surface volcanique
        MossBlock          = 94,   // biome Mossy
        MushroomStem       = 95,   // troncs des champignons géants
        BrownMushroomBlock = 96,   // chapeau marron (forêts humides)
        RedMushroomBlock   = 97,   // chapeau rouge (mossy/nether)
        Snow               = 98,   // cap neigeux des sommets
        Calcite            = 99,   // veines crystal/mountain
        Tuff               = 100,  // sous-couches mountain
        Basalt             = 101,  // surface volcanique alterne
        Dripstone          = 102,  // grottes/sous-sol
        RootedDirt         = 103,  // mossy/cherry
        NetherWartBlock    = 104,  // nether
        ShroomLight        = 105,  // points lumineux mossy/nether
        PaleOakLog         = 106,  // arbres pâles cherry/mountain
        PaleOakLeaves      = 107,

        // ── IDs de rendu internes (face-variants) ─────────────
        // Ces valeurs ne sont JAMAIS stockées dans les chunks.
        // Elles servent uniquement d'index de sous-mesh dans MeshData.
        GrassSide         = 200,
        GrassBottom       = 201,
        OakLogTop         = 202,
        SpruceLogTop      = 203,
        BirchLogTop       = 204,
        JungleLogTop      = 205,
        AcaciaLogTop      = 206,
        DarkOakLogTop     = 207,
        MangroveLogTop    = 208,
        CherryLogTop      = 209,
        SandstoneTop      = 210,
        SandstoneBottom   = 211,
        RedSandstoneTop   = 212,
        RedSandstoneBottom= 213,
        DeepslateTop      = 214,
        PodzolTop         = 215,
        PodzolBottom      = 216,
        CactusTop         = 217,
        CactusBottom      = 218,
        BasaltTop         = 219,
        PaleOakLogTop     = 220,
    }

    /// <summary>
    /// Propriétés statiques associées à chaque type de bloc.
    /// </summary>
    public static class BlockProperties
    {
        // Plage des IDs de blocs solides réels (stockés dans les chunks).
        private const byte SolidMin = 1;
        private const byte SolidMax = 107;

        /// <summary>Retourne true si le bloc doit bloquer la génération des faces adjacentes.</summary>
        public static bool IsSolid(byte blockId) => blockId >= SolidMin && blockId <= SolidMax;

        public static bool IsSolid(BlockType type) => IsSolid((byte)type);
    }
}

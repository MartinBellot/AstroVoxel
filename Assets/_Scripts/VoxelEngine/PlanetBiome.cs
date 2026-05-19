// ============================================================
//  PlanetBiome.cs
//  Types de biomes pour les planètes procédurales.
// ============================================================

namespace AstroVoxel.VoxelEngine
{
    /// <summary>
    /// Détermine l'apparence visuelle, les blocs de surface et
    /// les règles de génération d'une planète procédurale.
    /// </summary>
    public enum PlanetBiome : byte
    {
        Terran   = 0,   // Herbe / Terre / Pierre — planète type
        Desert   = 1,   // Sable / Grès / RedSand — dunes, pas d'arbres
        Snow     = 2,   // PackedIce / Pierre / BlueIce — épicéas rares
        Volcanic = 3,   // Netherrack / Blackstone / Obsidian — cratères
        Forest   = 4,   // Forêt dense — JungleLog / JungleLeaves
        Mountain = 5,   // Montagnes pierreuses — Granite / Andesite — amplitude max
        Endstone = 6,   // EndStone / PurpurBlock — planète End
        Crystal  = 7,   // QuartzBricks / PurpurBlock / Deepslate — cristallin
        Nether   = 8,   // SoulSand / Netherrack / Glowstone — nether
        Cherry   = 9,   // CherryLog / CherryLeaves / Grass
        Mossy    = 10,  // MossyCobblestone / DarkOak — vieux et mystérieux
    }
}

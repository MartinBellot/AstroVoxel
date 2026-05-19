// ============================================================
//  BlockFaceData.cs
//  Données statiques des textures par bloc et par face.
//
//  Architecture :
//   - GetRenderingId(blockId, face) : retourne l'index de sous-mesh
//     Unity à utiliser pour cette combinaison bloc+face.
//     Pour les blocs simples (toutes faces identiques), renvoie blockId.
//     Pour les blocs multi-faces (Grass, Logs, Sandstone…), renvoie
//     un ID de face-variant (200-216) ou le blockId lui-même selon la face.
//   - GetTextureName(renderingId) : retourne le nom du fichier PNG
//     (sans extension) dans Assets/textures/block/.
//   - GetIconRenderingId(blockId) : renvoie l'ID de rendu préféré
//     pour l'icône (vue du dessus ou face la plus lisible).
//   - GetDisplayName(blockId) : nom lisible pour l'UI.
//   - AllBlockIds : tableau de tous les IDs de blocs placables.
// ============================================================

namespace AstroVoxel.VoxelEngine
{
    public static class BlockFaceData
    {
        // ── Tableau de noms de textures (256 slots) ───────────────
        // Index = renderingId (byte cast de BlockType)
        private static readonly string[] _textureNames = new string[256];

        // ── Tableau de noms d'affichage (256 slots) ───────────────
        private static readonly string[] _displayNames = new string[256];

        static BlockFaceData()
        {
            // ── Blocs de base ────────────────────────────────────
            Reg(BlockType.Stone,              "stone",               "Pierre");
            Reg(BlockType.Dirt,               "dirt",                "Terre");
            Reg(BlockType.Grass,              "grass_block_top",     "Herbe");
            Reg(BlockType.Sand,               "sand",                "Sable");
            Reg(BlockType.Wood,               "oak_log",             "Bûche de Chêne");
            Reg(BlockType.Leaves,             "oak_leaves",          "Feuilles de Chêne");

            // ── Pierre et variantes ──────────────────────────────
            Reg(BlockType.Cobblestone,        "cobblestone",         "Cobblestone");
            Reg(BlockType.MossyCobblestone,   "mossy_cobblestone",   "Cobblestone Moussu");
            Reg(BlockType.Andesite,           "andesite",            "Andésite");
            Reg(BlockType.PolishedAndesite,   "polished_andesite",   "Andésite Polie");
            Reg(BlockType.Diorite,            "diorite",             "Diorite");
            Reg(BlockType.PolishedDiorite,    "polished_diorite",    "Diorite Polie");
            Reg(BlockType.Granite,            "granite",             "Granite");
            Reg(BlockType.PolishedGranite,    "polished_granite",    "Granite Poli");
            Reg(BlockType.StoneBricks,        "stone_bricks",        "Briques de Pierre");
            Reg(BlockType.CrackedStoneBricks, "cracked_stone_bricks","Briques Fissurées");
            Reg(BlockType.MossyStoneBricks,   "mossy_stone_bricks",  "Briques Mousseuses");
            Reg(BlockType.ChiseledStoneBricks,"chiseled_stone_bricks","Briques Ciselées");
            Reg(BlockType.SmoothStone,        "smooth_stone",        "Pierre Lisse");

            // ── Terre et sol ─────────────────────────────────────
            Reg(BlockType.Gravel,             "gravel",              "Gravier");
            Reg(BlockType.Clay,               "clay",                "Argile");
            Reg(BlockType.CoarseDirt,         "coarse_dirt",         "Terre Grossière");
            Reg(BlockType.Podzol,             "podzol_side",         "Podzol");   // face côtés
            Reg(BlockType.Mud,                "mud",                 "Boue");
            Reg(BlockType.MudBricks,          "mud_bricks",          "Briques de Boue");

            // ── Sable et grès ────────────────────────────────────
            Reg(BlockType.RedSand,            "red_sand",            "Sable Rouge");
            Reg(BlockType.Sandstone,          "sandstone",           "Grès");         // face côtés
            Reg(BlockType.RedSandstone,       "red_sandstone",       "Grès Rouge");   // face côtés

            // ── Planches ─────────────────────────────────────────
            Reg(BlockType.OakPlanks,          "oak_planks",          "Planches de Chêne");
            Reg(BlockType.SprucePlanks,       "spruce_planks",       "Planches d'Épicéa");
            Reg(BlockType.BirchPlanks,        "birch_planks",        "Planches de Bouleau");
            Reg(BlockType.JunglePlanks,       "jungle_planks",       "Planches de Jungle");
            Reg(BlockType.AcaciaPlanks,       "acacia_planks",       "Planches d'Acacia");
            Reg(BlockType.DarkOakPlanks,      "dark_oak_planks",     "Planches de Chêne Noir");
            Reg(BlockType.MangrovePlanks,     "mangrove_planks",     "Planches de Mangrove");
            Reg(BlockType.CherryPlanks,       "cherry_planks",       "Planches de Cerisier");
            Reg(BlockType.CrimsonPlanks,      "crimson_planks",      "Planches Cramoisis");
            Reg(BlockType.WarpedPlanks,       "warped_planks",       "Planches Déformées");
            Reg(BlockType.BambooPlanks,       "bamboo_planks",       "Planches de Bambou");

            // ── Troncs (face côtés) ──────────────────────────────
            // Wood=5 = OakLog déjà déclaré ci-dessus
            Reg(BlockType.SpruceLog,          "spruce_log",          "Bûche d'Épicéa");
            Reg(BlockType.BirchLog,           "birch_log",           "Bûche de Bouleau");
            Reg(BlockType.JungleLog,          "jungle_log",          "Bûche de Jungle");
            Reg(BlockType.AcaciaLog,          "acacia_log",          "Bûche d'Acacia");
            Reg(BlockType.DarkOakLog,         "dark_oak_log",        "Bûche de Chêne Noir");
            Reg(BlockType.MangroveLog,        "mangrove_log",        "Bûche de Mangrove");
            Reg(BlockType.CherryLog,          "cherry_log",          "Bûche de Cerisier");

            // ── Feuillages ───────────────────────────────────────
            // Leaves=6 = OakLeaves déjà déclaré
            Reg(BlockType.SpruceLeaves,       "spruce_leaves",       "Feuilles d'Épicéa");
            Reg(BlockType.BirchLeaves,        "birch_leaves",        "Feuilles de Bouleau");
            Reg(BlockType.JungleLeaves,       "jungle_leaves",       "Feuilles de Jungle");
            Reg(BlockType.AcaciaLeaves,       "acacia_leaves",       "Feuilles d'Acacia");
            Reg(BlockType.DarkOakLeaves,      "dark_oak_leaves",     "Feuilles de Chêne Noir");
            Reg(BlockType.MangroveLeaves,     "mangrove_leaves",     "Feuilles de Mangrove");
            Reg(BlockType.CherryLeaves,       "cherry_leaves",       "Feuilles de Cerisier");

            // ── Blocs spéciaux ────────────────────────────────────
            Reg(BlockType.Bedrock,            "bedrock",             "Bedrock");
            Reg(BlockType.Obsidian,           "obsidian",            "Obsidienne");
            Reg(BlockType.Bricks,             "bricks",              "Briques");
            Reg(BlockType.Ice,                "ice",                 "Glace");
            Reg(BlockType.PackedIce,          "packed_ice",          "Glace Compressée");
            Reg(BlockType.BlueIce,            "blue_ice",            "Glace Bleue");

            // ── Nether ───────────────────────────────────────────
            Reg(BlockType.NetherBricks,       "nether_bricks",       "Briques du Nether");
            Reg(BlockType.RedNetherBricks,    "red_nether_bricks",   "Briques Rouges du Nether");
            Reg(BlockType.Netherrack,         "netherrack",          "Netherrack");
            Reg(BlockType.SoulSand,           "soul_sand",           "Sable des Âmes");
            Reg(BlockType.SoulSoil,           "soul_soil",           "Terre des Âmes");
            Reg(BlockType.Glowstone,          "glowstone",           "Pierre Lumineuse");

            // ── End ──────────────────────────────────────────────
            Reg(BlockType.EndStone,           "end_stone",           "Pierre de l'End");
            Reg(BlockType.Blackstone,         "blackstone",          "Blackstone");
            Reg(BlockType.PurpurBlock,        "purpur_block",        "Bloc de Purpur");
            Reg(BlockType.QuartzBricks,       "quartz_bricks",       "Briques de Quartz");

            // ── Deepslate ────────────────────────────────────────
            Reg(BlockType.Deepslate,          "deepslate",           "Deepslate");    // face côtés
            Reg(BlockType.CobbledDeepslate,   "cobbled_deepslate",   "Cobbled Deepslate");
            Reg(BlockType.DeepSlateBricks,    "deepslate_bricks",    "Briques de Deepslate");
            Reg(BlockType.DeepSlateTiles,     "deepslate_tiles",     "Carreaux de Deepslate");

            // ── Minerais ─────────────────────────────────────────
            Reg(BlockType.CoalOre,            "coal_ore",            "Minerai de Charbon");
            Reg(BlockType.IronOre,            "iron_ore",            "Minerai de Fer");
            Reg(BlockType.CopperOre,          "copper_ore",          "Minerai de Cuivre");
            Reg(BlockType.GoldOre,            "gold_ore",            "Minerai d'Or");
            Reg(BlockType.LapisOre,           "lapis_ore",           "Minerai de Lapis");
            Reg(BlockType.RedstoneOre,        "redstone_ore",        "Minerai de Redstone");
            Reg(BlockType.DiamondOre,         "diamond_ore",         "Minerai de Diamant");
            Reg(BlockType.EmeraldOre,         "emerald_ore",         "Minerai d'Émeraude");

            // ── Blocs de minerais ─────────────────────────────────
            Reg(BlockType.CoalBlock,          "coal_block",          "Bloc de Charbon");
            Reg(BlockType.IronBlock,          "iron_block",          "Bloc de Fer");
            Reg(BlockType.GoldBlock,          "gold_block",          "Bloc d'Or");
            Reg(BlockType.DiamondBlock,       "diamond_block",       "Bloc de Diamant");
            Reg(BlockType.EmeraldBlock,       "emerald_block",       "Bloc d'Émeraude");
            Reg(BlockType.LapisBlock,         "lapis_block",         "Bloc de Lapis");
            Reg(BlockType.RedstoneBlock,      "redstone_block",      "Bloc de Redstone");
            Reg(BlockType.NetheriteBlock,     "netherite_block",     "Bloc de Netherite");
            Reg(BlockType.CopperBlock,        "copper_block",        "Bloc de Cuivre");
            Reg(BlockType.AmethystBlock,      "amethyst_block",      "Bloc d'Améthyste");

            // ── Blocs spéciaux biomes ────────────────────────────
            Reg(BlockType.Cactus,             "cactus_side",         "Cactus");          // face côtés
            Reg(BlockType.MagmaBlock,         "magma",               "Bloc de Magma");
            Reg(BlockType.MossBlock,          "moss_block",          "Bloc de Mousse");
            Reg(BlockType.MushroomStem,       "mushroom_stem",       "Pied de Champignon");
            Reg(BlockType.BrownMushroomBlock, "brown_mushroom_block","Chapeau Marron");
            Reg(BlockType.RedMushroomBlock,   "red_mushroom_block",  "Chapeau Rouge");
            Reg(BlockType.Snow,               "snow",                "Neige");
            Reg(BlockType.Calcite,            "calcite",             "Calcite");
            Reg(BlockType.Tuff,               "tuff",                "Tuf");
            Reg(BlockType.Basalt,             "basalt_side",         "Basalte");         // face côtés
            Reg(BlockType.Dripstone,          "dripstone_block",     "Roche Spéléothème");
            Reg(BlockType.RootedDirt,         "rooted_dirt",         "Terre Racinaire");
            Reg(BlockType.NetherWartBlock,    "nether_wart_block",   "Verrue du Nether");
            Reg(BlockType.ShroomLight,        "shroomlight",         "Champilumière");
            Reg(BlockType.PaleOakLog,         "pale_oak_log",        "Bûche de Chêne Pâle");
            Reg(BlockType.PaleOakLeaves,      "pale_oak_leaves",     "Feuilles de Chêne Pâle");

            // ── Face-variants (IDs de rendu internes) ────────────
            // Ces entrées ont un nom de texture mais pas de nom d'affichage.
            RegTex(BlockType.GrassSide,          "grass_block_side");
            RegTex(BlockType.GrassBottom,        "dirt");
            RegTex(BlockType.OakLogTop,          "oak_log_top");
            RegTex(BlockType.SpruceLogTop,       "spruce_log_top");
            RegTex(BlockType.BirchLogTop,        "birch_log_top");
            RegTex(BlockType.JungleLogTop,       "jungle_log_top");
            RegTex(BlockType.AcaciaLogTop,       "acacia_log_top");
            RegTex(BlockType.DarkOakLogTop,      "dark_oak_log_top");
            RegTex(BlockType.MangroveLogTop,     "mangrove_log_top");
            RegTex(BlockType.CherryLogTop,       "cherry_log_top");
            RegTex(BlockType.SandstoneTop,       "sandstone_top");
            RegTex(BlockType.SandstoneBottom,    "sandstone_bottom");
            RegTex(BlockType.RedSandstoneTop,    "red_sandstone_top");
            RegTex(BlockType.RedSandstoneBottom, "red_sandstone_bottom");
            RegTex(BlockType.DeepslateTop,       "deepslate_top");
            RegTex(BlockType.PodzolTop,          "podzol_top");
            RegTex(BlockType.PodzolBottom,       "dirt");
            RegTex(BlockType.CactusTop,          "cactus_top");
            RegTex(BlockType.CactusBottom,       "cactus_bottom");
            RegTex(BlockType.BasaltTop,          "basalt_top");
            RegTex(BlockType.PaleOakLogTop,      "pale_oak_log_top");
        }

        private static void Reg(BlockType t, string tex, string display)
        {
            _textureNames[(byte)t] = tex;
            _displayNames[(byte)t] = display;
        }

        private static void RegTex(BlockType t, string tex)
            => _textureNames[(byte)t] = tex;

        // ── GetRenderingId ────────────────────────────────────────
        /// <summary>
        /// Retourne l'ID de sous-mesh (renderingId) pour un bloc et une face donnés.
        /// face : 0=Top  1=Bottom  2=Front  3=Back  4=Right  5=Left
        /// </summary>
        public static byte GetRenderingId(byte blockId, int face)
        {
            switch ((BlockType)blockId)
            {
                case BlockType.Grass:
                    if (face == 0) return blockId;                         // Top → grass_block_top
                    if (face == 1) return (byte)BlockType.GrassBottom;     // Bottom → dirt
                    return (byte)BlockType.GrassSide;                      // Côtés → grass_block_side

                case BlockType.Wood:   // OakLog
                    if (face == 0 || face == 1) return (byte)BlockType.OakLogTop;
                    return blockId;

                case BlockType.SpruceLog:
                    if (face == 0 || face == 1) return (byte)BlockType.SpruceLogTop;
                    return blockId;

                case BlockType.BirchLog:
                    if (face == 0 || face == 1) return (byte)BlockType.BirchLogTop;
                    return blockId;

                case BlockType.JungleLog:
                    if (face == 0 || face == 1) return (byte)BlockType.JungleLogTop;
                    return blockId;

                case BlockType.AcaciaLog:
                    if (face == 0 || face == 1) return (byte)BlockType.AcaciaLogTop;
                    return blockId;

                case BlockType.DarkOakLog:
                    if (face == 0 || face == 1) return (byte)BlockType.DarkOakLogTop;
                    return blockId;

                case BlockType.MangroveLog:
                    if (face == 0 || face == 1) return (byte)BlockType.MangroveLogTop;
                    return blockId;

                case BlockType.CherryLog:
                    if (face == 0 || face == 1) return (byte)BlockType.CherryLogTop;
                    return blockId;

                case BlockType.Sandstone:
                    if (face == 0) return (byte)BlockType.SandstoneTop;
                    if (face == 1) return (byte)BlockType.SandstoneBottom;
                    return blockId;

                case BlockType.RedSandstone:
                    if (face == 0) return (byte)BlockType.RedSandstoneTop;
                    if (face == 1) return (byte)BlockType.RedSandstoneBottom;
                    return blockId;

                case BlockType.Deepslate:
                    if (face == 0 || face == 1) return (byte)BlockType.DeepslateTop;
                    return blockId;

                case BlockType.Podzol:
                    if (face == 0) return (byte)BlockType.PodzolTop;
                    if (face == 1) return (byte)BlockType.PodzolBottom;
                    return blockId;   // côtés → podzol_side (texture du blockId)

                case BlockType.Cactus:
                    if (face == 0) return (byte)BlockType.CactusTop;
                    if (face == 1) return (byte)BlockType.CactusBottom;
                    return blockId;   // côtés → cactus_side

                case BlockType.Basalt:
                    if (face == 0 || face == 1) return (byte)BlockType.BasaltTop;
                    return blockId;

                case BlockType.PaleOakLog:
                    if (face == 0 || face == 1) return (byte)BlockType.PaleOakLogTop;
                    return blockId;

                default:
                    return blockId;
            }
        }

        // ── GetTextureName ────────────────────────────────────────
        /// <summary>
        /// Retourne le nom de fichier PNG (sans extension) pour un renderingId.
        /// Repli sur "stone" si non défini.
        /// </summary>
        public static string GetTextureName(byte renderingId)
        {
            var name = _textureNames[renderingId];
            return string.IsNullOrEmpty(name) ? "stone" : name;
        }

        // ── GetIconRenderingId ────────────────────────────────────
        /// <summary>
        /// Retourne le renderingId à utiliser pour l'icône d'un bloc
        /// (vue du dessus ou texture la plus représentative).
        /// </summary>
        public static byte GetIconRenderingId(byte blockId)
        {
            switch ((BlockType)blockId)
            {
                // Pour les troncs, l'icône montre la face du dessus (anneaux)
                case BlockType.Wood:       return (byte)BlockType.OakLogTop;
                case BlockType.SpruceLog:  return (byte)BlockType.SpruceLogTop;
                case BlockType.BirchLog:   return (byte)BlockType.BirchLogTop;
                case BlockType.JungleLog:  return (byte)BlockType.JungleLogTop;
                case BlockType.AcaciaLog:  return (byte)BlockType.AcaciaLogTop;
                case BlockType.DarkOakLog: return (byte)BlockType.DarkOakLogTop;
                case BlockType.MangroveLog:return (byte)BlockType.MangroveLogTop;
                case BlockType.CherryLog:  return (byte)BlockType.CherryLogTop;
                // Pour les blocs multi-faces, montrer la face du dessus
                case BlockType.Sandstone:    return (byte)BlockType.SandstoneTop;
                case BlockType.RedSandstone: return (byte)BlockType.RedSandstoneTop;
                case BlockType.Deepslate:    return (byte)BlockType.DeepslateTop;
                case BlockType.Podzol:       return (byte)BlockType.PodzolTop;
                case BlockType.Cactus:       return (byte)BlockType.CactusTop;
                case BlockType.Basalt:       return (byte)BlockType.BasaltTop;
                case BlockType.PaleOakLog:   return (byte)BlockType.PaleOakLogTop;
                // Pour Grass, la face du dessus (blockId lui-même) est déjà grass_block_top
                default: return blockId;
            }
        }

        // ── GetDisplayName ────────────────────────────────────────
        /// <summary>Nom lisible du bloc pour l'UI.</summary>
        public static string GetDisplayName(byte blockId)
        {
            var name = _displayNames[blockId];
            return string.IsNullOrEmpty(name) ? ((BlockType)blockId).ToString() : name;
        }

        // ── AllBlockIds ───────────────────────────────────────────
        /// <summary>
        /// Tableau de tous les IDs de blocs placables (hors Air, hors face-variants).
        /// Ordonné par catégorie pour l'inventaire créatif.
        /// </summary>
        public static readonly byte[] AllBlockIds = new byte[]
        {
            // Terre & sol
            (byte)BlockType.Stone,   (byte)BlockType.Dirt,   (byte)BlockType.Grass,
            (byte)BlockType.Sand,    (byte)BlockType.RedSand,(byte)BlockType.Gravel,
            (byte)BlockType.Clay,    (byte)BlockType.CoarseDirt,
            (byte)BlockType.Podzol,  (byte)BlockType.Mud,
            // Pierre
            (byte)BlockType.Cobblestone,     (byte)BlockType.MossyCobblestone,
            (byte)BlockType.Andesite,        (byte)BlockType.PolishedAndesite,
            (byte)BlockType.Diorite,         (byte)BlockType.PolishedDiorite,
            (byte)BlockType.Granite,         (byte)BlockType.PolishedGranite,
            (byte)BlockType.StoneBricks,     (byte)BlockType.CrackedStoneBricks,
            (byte)BlockType.MossyStoneBricks,(byte)BlockType.ChiseledStoneBricks,
            (byte)BlockType.SmoothStone,
            // Grès
            (byte)BlockType.Sandstone,       (byte)BlockType.RedSandstone,
            // Bois – planches
            (byte)BlockType.OakPlanks,       (byte)BlockType.SprucePlanks,
            (byte)BlockType.BirchPlanks,     (byte)BlockType.JunglePlanks,
            (byte)BlockType.AcaciaPlanks,    (byte)BlockType.DarkOakPlanks,
            (byte)BlockType.MangrovePlanks,  (byte)BlockType.CherryPlanks,
            (byte)BlockType.CrimsonPlanks,   (byte)BlockType.WarpedPlanks,
            (byte)BlockType.BambooPlanks,
            // Bois – troncs
            (byte)BlockType.Wood,       (byte)BlockType.SpruceLog,
            (byte)BlockType.BirchLog,   (byte)BlockType.JungleLog,
            (byte)BlockType.AcaciaLog,  (byte)BlockType.DarkOakLog,
            (byte)BlockType.MangroveLog,(byte)BlockType.CherryLog,
            // Feuillages
            (byte)BlockType.Leaves,          (byte)BlockType.SpruceLeaves,
            (byte)BlockType.BirchLeaves,     (byte)BlockType.JungleLeaves,
            (byte)BlockType.AcaciaLeaves,    (byte)BlockType.DarkOakLeaves,
            (byte)BlockType.MangroveLeaves,  (byte)BlockType.CherryLeaves,
            // Deepslate
            (byte)BlockType.Deepslate,       (byte)BlockType.CobbledDeepslate,
            (byte)BlockType.DeepSlateBricks, (byte)BlockType.DeepSlateTiles,
            // Spéciaux overworld
            (byte)BlockType.Bedrock, (byte)BlockType.Obsidian,
            (byte)BlockType.Bricks,
            (byte)BlockType.Ice,     (byte)BlockType.PackedIce, (byte)BlockType.BlueIce,
            (byte)BlockType.MudBricks,
            // Nether
            (byte)BlockType.NetherBricks,    (byte)BlockType.RedNetherBricks,
            (byte)BlockType.Netherrack,      (byte)BlockType.SoulSand,
            (byte)BlockType.SoulSoil,        (byte)BlockType.Glowstone,
            // End & Divers
            (byte)BlockType.EndStone,        (byte)BlockType.Blackstone,
            (byte)BlockType.PurpurBlock,     (byte)BlockType.QuartzBricks,
            // Minerais
            (byte)BlockType.CoalOre,    (byte)BlockType.IronOre,
            (byte)BlockType.CopperOre,  (byte)BlockType.GoldOre,
            (byte)BlockType.LapisOre,   (byte)BlockType.RedstoneOre,
            (byte)BlockType.DiamondOre, (byte)BlockType.EmeraldOre,
            // Blocs de minerais
            (byte)BlockType.CoalBlock,     (byte)BlockType.IronBlock,
            (byte)BlockType.GoldBlock,     (byte)BlockType.DiamondBlock,
            (byte)BlockType.EmeraldBlock,  (byte)BlockType.LapisBlock,
            (byte)BlockType.RedstoneBlock, (byte)BlockType.NetheriteBlock,
            (byte)BlockType.CopperBlock,   (byte)BlockType.AmethystBlock,
            // Nouveaux blocs biomes
            (byte)BlockType.Cactus,           (byte)BlockType.MagmaBlock,
            (byte)BlockType.MossBlock,        (byte)BlockType.MushroomStem,
            (byte)BlockType.BrownMushroomBlock,(byte)BlockType.RedMushroomBlock,
            (byte)BlockType.Snow,             (byte)BlockType.Calcite,
            (byte)BlockType.Tuff,             (byte)BlockType.Basalt,
            (byte)BlockType.Dripstone,        (byte)BlockType.RootedDirt,
            (byte)BlockType.NetherWartBlock,  (byte)BlockType.ShroomLight,
            (byte)BlockType.PaleOakLog,       (byte)BlockType.PaleOakLeaves,
        };

        // ── Couleur de repli (si la registry n'est pas encore construite) ─
        public static UnityEngine.Color GetFallbackColor(BlockType t)
        {
            switch (t)
            {
                case BlockType.Stone:              return new UnityEngine.Color(0.55f, 0.55f, 0.58f);
                case BlockType.Dirt:               return new UnityEngine.Color(0.60f, 0.38f, 0.16f);
                case BlockType.Grass:              return new UnityEngine.Color(0.25f, 0.68f, 0.20f);
                case BlockType.Sand:               return new UnityEngine.Color(0.92f, 0.84f, 0.42f);
                case BlockType.Wood:               return new UnityEngine.Color(0.62f, 0.40f, 0.18f);
                case BlockType.Leaves:             return new UnityEngine.Color(0.16f, 0.50f, 0.12f);
                case BlockType.Cobblestone:        return new UnityEngine.Color(0.50f, 0.50f, 0.50f);
                case BlockType.Sandstone:          return new UnityEngine.Color(0.88f, 0.82f, 0.60f);
                case BlockType.Glowstone:          return new UnityEngine.Color(0.95f, 0.80f, 0.40f);
                case BlockType.DiamondBlock:       return new UnityEngine.Color(0.30f, 0.85f, 0.85f);
                case BlockType.GoldBlock:          return new UnityEngine.Color(0.95f, 0.80f, 0.10f);
                case BlockType.IronBlock:          return new UnityEngine.Color(0.80f, 0.80f, 0.80f);
                case BlockType.Bedrock:            return new UnityEngine.Color(0.25f, 0.25f, 0.28f);
                case BlockType.Obsidian:           return new UnityEngine.Color(0.12f, 0.06f, 0.18f);
                case BlockType.Netherrack:         return new UnityEngine.Color(0.60f, 0.18f, 0.18f);
                default: return new UnityEngine.Color(0.45f, 0.42f, 0.40f);
            }
        }
    }
}

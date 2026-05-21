// ============================================================
//  CraftingSystem.cs
//  Recettes de craft style Minecraft.
//  Recettes de base (2×2) disponibles dans l'inventaire de survie.
//  Recettes avancées (3×3) nécessitent un établi.
// ============================================================

using System;
using System.Collections.Generic;

namespace AstroVoxel.Player
{
    // ============================================================
    //  CraftingRecipe
    // ============================================================
    public sealed class CraftingRecipe
    {
        public string DisplayName;
        public ItemType ResultItem;
        public int ResultCount;
        public Dictionary<ItemType, int> Ingredients;
        public bool RequiresCraftingTable;

        public CraftingRecipe(
            string name,
            ItemType result, int resultCount,
            Dictionary<ItemType, int> ingredients,
            bool requiresTable = false)
        {
            DisplayName           = name;
            ResultItem            = result;
            ResultCount           = resultCount;
            Ingredients           = ingredients;
            RequiresCraftingTable = requiresTable;
        }

        /// <summary>Retourne true si l'inventaire contient tous les ingrédients.</summary>
        public bool CanCraft(SurvivalInventoryData inv)
        {
            foreach (var kv in Ingredients)
                if (!inv.Has(kv.Key, kv.Value)) return false;
            return true;
        }

        /// <summary>Consomme les ingrédients et ajoute le résultat. Retourne false si pas assez de matériaux.</summary>
        public bool TryCraft(SurvivalInventoryData inv)
        {
            if (!CanCraft(inv)) return false;
            foreach (var kv in Ingredients)
                inv.RemoveItem(kv.Key, kv.Value);
            inv.AddItem(ResultItem, ResultCount);
            return true;
        }
    }

    // ============================================================
    //  CraftingSystem
    // ============================================================
    public static class CraftingSystem
    {
        // ── Toutes les recettes ───────────────────────────────
        public static readonly List<CraftingRecipe> AllRecipes = new List<CraftingRecipe>
        {
            // ── Recettes de base (pas d'établi) ───────────────

            // 1 OakLog → 4 OakPlanks
            new CraftingRecipe(
                "Planches de Chêne",
                ItemType.OakPlanks, 4,
                new Dictionary<ItemType, int> { [ItemType.OakLog] = 1 }),

            // 1 SpruceLog → 4 SprucePlanks
            new CraftingRecipe(
                "Planches d'Épicéa",
                ItemType.SprucePlanks, 4,
                new Dictionary<ItemType, int> { [ItemType.SpruceLog] = 1 }),

            // 1 BirchLog → 4 BirchPlanks
            new CraftingRecipe(
                "Planches de Bouleau",
                ItemType.BirchPlanks, 4,
                new Dictionary<ItemType, int> { [ItemType.BirchLog] = 1 }),

            // 1 JungleLog → 4 JunglePlanks
            new CraftingRecipe(
                "Planches de Jungle",
                ItemType.JunglePlanks, 4,
                new Dictionary<ItemType, int> { [ItemType.JungleLog] = 1 }),

            // 1 AcaciaLog → 4 AcaciaPlanks
            new CraftingRecipe(
                "Planches d'Acacia",
                ItemType.AcaciaPlanks, 4,
                new Dictionary<ItemType, int> { [ItemType.AcaciaLog] = 1 }),

            // 1 DarkOakLog → 4 DarkOakPlanks
            new CraftingRecipe(
                "Planches de Chêne Noir",
                ItemType.DarkOakPlanks, 4,
                new Dictionary<ItemType, int> { [ItemType.DarkOakLog] = 1 }),

            // 2 OakPlanks (vertical) → 4 Sticks
            new CraftingRecipe(
                "Bâtons",
                ItemType.Stick, 4,
                new Dictionary<ItemType, int> { [ItemType.OakPlanks] = 2 }),

            // 4 OakPlanks → 1 CraftingTable
            new CraftingRecipe(
                "Établi",
                ItemType.CraftingTable, 1,
                new Dictionary<ItemType, int> { [ItemType.OakPlanks] = 4 }),

            // ── Recettes établi (3×3) ──────────────────────────

            // 3 OakPlanks + 2 Sticks → 1 WoodenPickaxe
            new CraftingRecipe(
                "Pioche en Bois",
                ItemType.WoodenPickaxe, 1,
                new Dictionary<ItemType, int>
                {
                    [ItemType.OakPlanks] = 3,
                    [ItemType.Stick]     = 2,
                },
                requiresTable: true),

            // 3 Cobblestone + 2 Sticks → 1 StonePickaxe
            new CraftingRecipe(
                "Pioche en Pierre",
                ItemType.StonePickaxe, 1,
                new Dictionary<ItemType, int>
                {
                    [ItemType.Cobblestone] = 3,
                    [ItemType.Stick]       = 2,
                },
                requiresTable: true),

            // 3 OakPlanks + 2 Sticks → 1 WoodenAxe
            new CraftingRecipe(
                "Hache en Bois",
                ItemType.WoodenAxe, 1,
                new Dictionary<ItemType, int>
                {
                    [ItemType.OakPlanks] = 3,
                    [ItemType.Stick]     = 2,
                },
                requiresTable: true),

            // 3 Cobblestone + 2 Sticks → 1 StoneAxe
            new CraftingRecipe(
                "Hache en Pierre",
                ItemType.StoneAxe, 1,
                new Dictionary<ItemType, int>
                {
                    [ItemType.Cobblestone] = 3,
                    [ItemType.Stick]       = 2,
                },
                requiresTable: true),

            // 2 OakPlanks + 2 Sticks → 1 WoodenShovel
            new CraftingRecipe(
                "Pelle en Bois",
                ItemType.WoodenShovel, 1,
                new Dictionary<ItemType, int>
                {
                    [ItemType.OakPlanks] = 1,
                    [ItemType.Stick]     = 2,
                },
                requiresTable: true),

            // 1 Cobblestone + 2 Sticks → 1 StoneShovel
            new CraftingRecipe(
                "Pelle en Pierre",
                ItemType.StoneShovel, 1,
                new Dictionary<ItemType, int>
                {
                    [ItemType.Cobblestone] = 1,
                    [ItemType.Stick]       = 2,
                },
                requiresTable: true),

            // 3 Blackstone + 2 Sticks → 1 Propulseur (établi requis)
            // Blackstone = BlockType.Blackstone = 67, mappé en ItemType via son ID
            new CraftingRecipe(
                "Propulseur",
                ItemType.Propulseur, 1,
                new Dictionary<ItemType, int>
                {
                    [(ItemType)(int)AstroVoxel.VoxelEngine.BlockType.Blackstone] = 3,
                    [ItemType.Stick] = 2,
                },
                requiresTable: true),
        };

        // ── API publique ──────────────────────────────────────

        /// <summary>
        /// Retourne les recettes craftables avec l'inventaire actuel.
        /// Si hasCraftingTable=false, exclut les recettes d'établi.
        /// </summary>
        public static List<CraftingRecipe> GetAvailableRecipes(
            SurvivalInventoryData inv,
            bool hasCraftingTable)
        {
            var result = new List<CraftingRecipe>();
            foreach (var r in AllRecipes)
            {
                if (r.RequiresCraftingTable && !hasCraftingTable) continue;
                if (r.CanCraft(inv)) result.Add(r);
            }
            return result;
        }

        /// <summary>
        /// Retourne toutes les recettes affichables dans l'UI (craftables ou non),
        /// filtrées selon la disponibilité de l'établi.
        /// </summary>
        public static List<CraftingRecipe> GetAllCraftableRecipes(bool hasCraftingTable)
        {
            var result = new List<CraftingRecipe>();
            foreach (var r in AllRecipes)
                if (!r.RequiresCraftingTable || hasCraftingTable) result.Add(r);
            return result;
        }
    }
}

// ============================================================
//  ItemStack.cs
//  Définit :
//    • ItemType  – identifiants d'items (blocs + outils/crafts)
//    • ItemStack – paire (type, quantité)
//    • ItemTypeHelper    – conversions et noms
//    • SurvivalInventoryData – inventaire de survie (singleton)
// ============================================================

using System;
using System.Collections.Generic;
using AstroVoxel.VoxelEngine;

namespace AstroVoxel.Player
{
    // ============================================================
    //  ItemType
    //  IDs 1-108  → correspondent directement à (int)BlockType
    //  IDs >= 300 → items non-bloc (outils, crafts intermédiaires)
    // ============================================================
    public enum ItemType : int
    {
        None = -1,

        // ── Blocs de base ─────────────────────────────────
        Stone          = 1,
        Dirt           = 2,
        Grass          = 3,
        Sand           = 4,
        OakLog         = 5,    // = BlockType.Wood
        OakLeaves      = 6,    // = BlockType.Leaves
        Cobblestone    = 7,
        Gravel         = 20,

        // ── Planches ──────────────────────────────────────
        OakPlanks      = 29,
        SprucePlanks   = 30,
        BirchPlanks    = 31,
        JunglePlanks   = 32,
        AcaciaPlanks   = 33,
        DarkOakPlanks  = 34,

        // ── Troncs ────────────────────────────────────────
        SpruceLog      = 40,
        BirchLog       = 41,
        JungleLog      = 42,
        AcaciaLog      = 43,
        DarkOakLog     = 44,
        MangroveLog    = 45,
        CherryLog      = 46,

        // ── Minerais & minéraux ───────────────────────────
        CoalOre        = 74,
        IronOre        = 75,
        CopperOre      = 76,
        GoldOre        = 77,

        // ── Établi (se pose dans le monde) ────────────────
        CraftingTable  = 108,   // = BlockType.CraftingTable

        // ── Items non-blocs ───────────────────────────────
        Stick           = 300,
        WoodenPickaxe   = 301,
        StonePickaxe    = 302,
        IronPickaxe     = 303,
        WoodenAxe       = 304,
        StoneAxe        = 305,
        WoodenShovel    = 306,
        StoneShovel     = 307,
    }

    // ============================================================
    //  ItemStack
    // ============================================================
    public struct ItemStack
    {
        public ItemType itemType;
        public int      count;

        public bool IsEmpty => count <= 0 || itemType == ItemType.None;

        public ItemStack(ItemType t, int c) { itemType = t; count = c; }
        public static readonly ItemStack Empty = new ItemStack(ItemType.None, 0);

        /// <summary>Retourne le BlockType correspondant, ou Air si c'est un outil.</summary>
        public BlockType ToBlockType()
        {
            int id = (int)itemType;
            if (id >= 1 && id <= 108) return (BlockType)id;
            return BlockType.Air;
        }

        /// <summary>True si l'item peut être posé comme bloc dans le monde.</summary>
        public bool IsBlock()
        {
            int id = (int)itemType;
            return id >= 1 && id <= 108;
        }

        /// <summary>True si l'item est un outil (pioche, hache…).</summary>
        public bool IsTool() => (int)itemType >= 300;
    }

    // ============================================================
    //  ItemTypeHelper
    // ============================================================
    public static class ItemTypeHelper
    {
        /// <summary>Convertit un BlockType en ItemType.</summary>
        public static ItemType FromBlockType(BlockType bt)
        {
            int id = (int)bt;
            if (id >= 1 && id <= 108) return (ItemType)id;
            return ItemType.None;
        }

        /// <summary>Nom lisible pour l'UI.</summary>
        public static string GetDisplayName(ItemType t)
        {
            switch (t)
            {
                case ItemType.Stick:          return "Bâton";
                case ItemType.WoodenPickaxe:  return "Pioche en Bois";
                case ItemType.StonePickaxe:   return "Pioche en Pierre";
                case ItemType.IronPickaxe:    return "Pioche en Fer";
                case ItemType.WoodenAxe:      return "Hache en Bois";
                case ItemType.StoneAxe:       return "Hache en Pierre";
                case ItemType.WoodenShovel:   return "Pelle en Bois";
                case ItemType.StoneShovel:    return "Pelle en Pierre";
                default:
                    int id = (int)t;
                    if (id >= 1 && id <= 255)
                        return BlockFaceData.GetDisplayName((byte)id);
                    return t.ToString();
            }
        }
    }

    // ============================================================
    //  SurvivalInventoryData
    //  Singleton statique – survit entre les frames, reset au restart.
    //
    //  Architecture :
    //   • _bag : compte total de chaque item (source de vérité)
    //   • Hotbar[9] : vue de la hotbar (mise à jour automatiquement)
    //     - Un item nouvellement ramassé va au 1er slot vide du hotbar
    //       s'il n'y est pas déjà.
    //     - Quand count tombe à 0, le slot est vidé.
    // ============================================================
    public sealed class SurvivalInventoryData
    {
        // ── Singleton ─────────────────────────────────────────
        public static SurvivalInventoryData Instance { get; private set; } = new SurvivalInventoryData();

        // ── Données ───────────────────────────────────────────
        private readonly Dictionary<ItemType, int> _bag = new Dictionary<ItemType, int>();

        /// <summary>9 slots de la hotbar survie.</summary>
        public readonly ItemStack[] Hotbar = new ItemStack[9];

        /// <summary>Déclenché à chaque modification (pour rafraîchir l'UI).</summary>
        public event Action OnChanged;

        public SurvivalInventoryData()
        {
            for (int i = 0; i < 9; i++) Hotbar[i] = ItemStack.Empty;
        }

        // ── API publique ──────────────────────────────────────

        public void Reset()
        {
            _bag.Clear();
            for (int i = 0; i < 9; i++) Hotbar[i] = ItemStack.Empty;
            OnChanged?.Invoke();
        }

        public void AddItem(ItemType t, int count = 1)
        {
            if (t == ItemType.None || count <= 0) return;
            _bag.TryGetValue(t, out int cur);
            _bag[t] = cur + count;
            SyncHotbar(t);
            OnChanged?.Invoke();
        }

        /// <summary>
        /// Retire l'item du sac et du hotbar.
        /// Retourne false si le sac n'a pas assez.
        /// </summary>
        public bool RemoveItem(ItemType t, int count = 1)
        {
            if (!_bag.TryGetValue(t, out int cur) || cur < count) return false;
            int newCount = cur - count;
            if (newCount <= 0) _bag.Remove(t);
            else               _bag[t] = newCount;
            SyncHotbar(t);
            OnChanged?.Invoke();
            return true;
        }

        public int GetCount(ItemType t)
        {
            _bag.TryGetValue(t, out int c);
            return c;
        }

        public bool Has(ItemType t, int count = 1) => GetCount(t) >= count;

        /// <summary>Liste de tous les stacks (pour l'UI inventaire).</summary>
        public List<ItemStack> GetAllStacks()
        {
            var list = new List<ItemStack>();
            foreach (var kv in _bag)
                if (kv.Value > 0) list.Add(new ItemStack(kv.Key, kv.Value));
            return list;
        }

        /// <summary>Définit manuellement un slot du hotbar (drag depuis l'inventaire).</summary>
        public void SetHotbarSlot(int slot, ItemType t)
        {
            if (slot < 0 || slot >= 9) return;
            int count = GetCount(t);
            Hotbar[slot] = count > 0 ? new ItemStack(t, count) : ItemStack.Empty;
            OnChanged?.Invoke();
        }

        // ── Sync interne ──────────────────────────────────────

        private void SyncHotbar(ItemType t)
        {
            int total = GetCount(t);

            // Met à jour les slots qui ont déjà cet item
            bool found = false;
            for (int i = 0; i < 9; i++)
            {
                if (Hotbar[i].itemType == t)
                {
                    Hotbar[i] = total > 0 ? new ItemStack(t, total) : ItemStack.Empty;
                    found = true;
                    // Ne break pas : plusieurs slots peuvent avoir le même type après un clear manuel
                }
            }

            // Si l'item vient d'être ajouté et n'est pas encore dans le hotbar, le place dans le premier slot vide
            if (!found && total > 0)
            {
                for (int i = 0; i < 9; i++)
                {
                    if (Hotbar[i].IsEmpty)
                    {
                        Hotbar[i] = new ItemStack(t, total);
                        break;
                    }
                }
            }
        }
    }
}

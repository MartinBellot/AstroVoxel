// ============================================================
//  BlockTextureRegistry.cs
//  ScriptableObject contenant un Material[] de 256 slots
//  (indexé par renderingId = (byte)BlockType).
//
//  Usage :
//    1. Ouvrir le menu AstroVoxel → Rebuild Block Texture Registry
//       dans l'éditeur Unity pour générer ce fichier.
//    2. Le fichier est placé dans Assets/Resources/ afin d'être
//       accessible via Resources.Load<BlockTextureRegistry>(...).
// ============================================================

using UnityEngine;

namespace AstroVoxel.VoxelEngine
{
    [CreateAssetMenu(
        fileName = "BlockTextureRegistry",
        menuName  = "AstroVoxel/Block Texture Registry")]
    public sealed class BlockTextureRegistry : ScriptableObject
    {
        [Tooltip("256 matériaux indexés par renderingId (cast byte de BlockType). " +
                 "Généré automatiquement via AstroVoxel → Rebuild Block Texture Registry.")]
        public Material[] materials = new Material[256];

        [Tooltip("Matériaux items (outils, bâton…) indexés par (ItemType - 300). " +
                 "Généré automatiquement via AstroVoxel → Rebuild Block Texture Registry.")]
        public Material[] itemMaterials = new Material[20];
    }
}

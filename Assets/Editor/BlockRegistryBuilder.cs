// ============================================================
//  BlockRegistryBuilder.cs  (Editor uniquement)
//  Menu : AstroVoxel → Rebuild Block Texture Registry
//
//  Pour chaque renderingId défini dans BlockFaceData :
//   1. Charge la texture PNG depuis Assets/textures/block/<name>.png
//   2. Crée (ou réutilise) un Material URP/Lit dans Assets/Resources/BlockMaterials/
//   3. Met à jour BlockTextureRegistry.asset dans Assets/Resources/
// ============================================================

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using AstroVoxel.VoxelEngine;

namespace AstroVoxel.Editor
{
    public static class BlockRegistryBuilder
    {
        private const string TextureBasePath  = "Assets/textures/block";
        private const string MaterialsFolder  = "Assets/Resources/BlockMaterials";
        private const string RegistryPath     = "Assets/Resources/BlockTextureRegistry.asset";
        // Shader custom — Unlit, fonctionne avec URP et Built-In RP.
        // Évite les problèmes d'initialisation des matériaux URP/Lit créés par code.
        private const string ShaderName       = "AstroVoxel/BlockUnlit";
        // Chemin du shader custom (pour le localiser via AssetDatabase si Shader.Find échoue)
        private const string ShaderPath       = "Assets/_Shaders/BlockVoxelUnlit.shader";

        [MenuItem("AstroVoxel/Rebuild Block Texture Registry")]
        public static void Rebuild()
        {
            // Assure que le dossier Resources existe
            EnsureFolder("Assets/Resources");
            EnsureFolder(MaterialsFolder);

            // Charge ou crée le ScriptableObject registry
            var registry = AssetDatabase.LoadAssetAtPath<BlockTextureRegistry>(RegistryPath);
            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<BlockTextureRegistry>();
                AssetDatabase.CreateAsset(registry, RegistryPath);
            }
            if (registry.materials == null || registry.materials.Length != 256)
                registry.materials = new Material[256];

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                // Tente de charger le shader depuis son chemin direct
                shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            }
            if (shader == null)
            {
                // Repli sur URP/Unlit (pas de mots-clés requis)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (shader == null)
            {
                // Repli Built-In
                shader = Shader.Find("Unlit/Texture");
            }
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Erreur", "Aucun shader compatible trouvé.\nVérifiez que Assets/_Shaders/BlockVoxelUnlit.shader est importé.", "OK");
                return;
            }
            Debug.Log($"[BlockRegistry] Shader utilisé : {shader.name}");

            int updated = 0;

            // Itère sur tous les renderingIds définis dans BlockFaceData
            for (int rid = 0; rid < 256; rid++)
            {
                string texName = BlockFaceData.GetTextureName((byte)rid);

                // Slot non défini (GetTextureName retourne "stone" par défaut)
                // → skip les IDs qui n'ont pas d'entrée explicite
                if (IsUndefinedSlot((byte)rid)) continue;

                // Charge la texture
                string texPath = $"{TextureBasePath}/{texName}.png";
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (texture == null)
                {
                    Debug.LogWarning($"[BlockRegistry] Texture introuvable : {texPath}");
                    continue;
                }

                // Nom du material
                string matName = $"Block_{rid:000}_{texName}";
                string matPath = $"{MaterialsFolder}/{matName}.mat";

                // Charge ou crée le material
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(shader) { name = matName };
                    AssetDatabase.CreateAsset(mat, matPath);
                }

                // Configure le material
                mat.shader = shader;
                // Assigner la texture explicitement via les deux méthodes pour garantir la sauvegarde
                mat.SetTexture("_BaseMap", texture);
                mat.mainTexture = texture;
                // Teinte biome pour les textures en niveaux de gris vanilla Minecraft.
                // Les textures grass/leaves sont nativement grises et doivent être tintées
                // par la couleur du biome (plains par défaut ici).
                mat.color = GetBiomeTint(texName);
                // _Smoothness et _Metallic absents sur le shader Unlit custom — vérifier avant d'assigner
                if (mat.HasProperty("_Smoothness"))
                    mat.SetFloat("_Smoothness", 0f);
                if (mat.HasProperty("_Metallic"))
                    mat.SetFloat("_Metallic", 0f);

                registry.materials[rid] = mat;
                EditorUtility.SetDirty(mat);
                updated++;
            }

            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[BlockRegistry] {updated} matériaux mis à jour → {RegistryPath}");
            EditorUtility.DisplayDialog(
                "Block Texture Registry",
                $"Registry reconstruit avec succès.\n{updated} matériaux créés/mis à jour.",
                "OK");
        }

        // ── Helpers ───────────────────────────────────────────────

        /// <summary>
        /// Retourne la teinte biome Minecraft à appliquer sur _BaseColor pour les
        /// textures qui sont nativement en niveaux de gris dans le resource pack vanilla.
        ///
        /// Valeurs de référence :
        ///   Grass (plains)    #79C05A – colormap[temp=0.8, hum=0.4]
        ///   Foliage (plains)  #59AE30 – colormap foliage par défaut
        ///   Birch (fixe)      #80A755 – tinte fixe, pas biome-dépendant
        ///   Spruce (fixe)     #619961 – tinte fixe
        /// </summary>
        private static Color GetBiomeTint(string texName)
        {
            switch (texName)
            {
                // ── Grass ────────────────────────────────────────
                case "grass_block_top":
                    return new Color(0.475f, 0.753f, 0.353f);   // #79C05A plains

                // ── Feuillages dépendants du biome ───────────────
                case "oak_leaves":
                case "jungle_leaves":
                case "acacia_leaves":
                case "dark_oak_leaves":
                case "mangrove_leaves":
                    return new Color(0.349f, 0.682f, 0.188f);   // #59AE30 foliage plains

                // ── Feuillages à teinte fixe ─────────────────────
                case "birch_leaves":
                    return new Color(0.502f, 0.655f, 0.333f);   // #80A755

                case "spruce_leaves":
                    return new Color(0.380f, 0.600f, 0.380f);   // #619961

                // ── Feuilles pâles (chêne pâle) — teinte gris-vert claire ──
                case "pale_oak_leaves":
                    return new Color(0.780f, 0.800f, 0.720f);

                default:
                    return Color.white;
            }
        }

        /// <summary>
        /// Retourne true si ce slot n'a pas d'entrée explicite dans BlockFaceData
        /// (i.e. c'est un ID de "remplissage" non utilisé entre 92 et 199).
        /// </summary>
        private static bool IsUndefinedSlot(byte rid)
        {
            // Plage des IDs réels (0-107) + face-variants (200-220) = définis.
            // Les plages 108-199 et >220 sont réservées et non utilisées.
            // Air (0) n'a pas de texture.
            if (rid == 0) return true;
            if (rid > 107 && rid < 200) return true;
            if (rid > 220) return true;
            return false;
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = Path.GetDirectoryName(path).Replace('\\', '/');
                string folder = Path.GetFileName(path);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }
    }
}
#endif

// ============================================================
//  AsteroidChunkGenerator.cs
//  Génère le contenu voxel d'un chunk d'astéroïde.
//  Aucune dépendance MonoBehaviour — appellable hors thread.
//
//  Composition des couches (de l'extérieur vers le noyau) :
//    Surface (depth < 1.5)  : Blackstone majoritaire + taches Obsidian
//    Sub-surface (1.5-4)    : mix Blackstone/Obsidian
//    Profondeur (4+)        : Obsidian + veines de minerais
//    Noyau                  : Obsidian pur
//
//  La surface est ultra-rugueuse (amplitude 40 % du rayon) pour
//  donner des formes irrégulières réalistes.
// ============================================================

using UnityEngine;
using AstroVoxel.VoxelEngine;

namespace AstroVoxel.Space
{
    public static class AsteroidChunkGenerator
    {
        // ── API publique ──────────────────────────────────────

        /// <summary>
        /// Calcule le type de bloc pour TOUTE position world de l'astéroïde.
        /// Paramétrique : chaque astéroïde a son propre rayon et sa seed.
        /// </summary>
        public static byte GetBlockType(
            Vector3 worldPos,
            Vector3 asteroidCenter,
            float   coreRadius,
            int     seed)
        {
            float dx = worldPos.x - asteroidCenter.x;
            float dy = worldPos.y - asteroidCenter.y;
            float dz = worldPos.z - asteroidCenter.z;
            float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

            if (dist < 0.001f) return (byte)BlockType.Obsidian;

            // Décalage de seed pour isoler chaque astéroïde
            float so = (seed * 1.31f) % 100f;
            float f  = 0.08f;

            // 3 octaves de bruit pour une surface très irrégulière
            float n1 = Mathf.PerlinNoise(dx * f + so + 10f, dz * f + so + 10f);
            float n2 = Mathf.PerlinNoise(dy * f + so + 20f, dx * f + so + 20f);
            float n3 = Mathf.PerlinNoise(dz * f + so + 30f, dy * f + so + 30f);
            // Détail haute-fréquence (aspect crayeux)
            float n4 = Mathf.PerlinNoise(dx * f * 2.5f + so + 40f, dz * f * 2.5f + so + 40f) * 0.4f;

            float noise = (n1 + n2 + n3) / 3f + n4;

            // Amplitude élevée = forme très irrégulière
            float amplitude     = coreRadius * 0.40f;
            float surfaceRadius = coreRadius + amplitude * (noise - 0.5f) * 2f;
            surfaceRadius = Mathf.Max(surfaceRadius, coreRadius * 0.35f);

            if (dist > surfaceRadius + 0.5f) return (byte)BlockType.Air;

            float depth = surfaceRadius - dist;

            // ── Surface : Blackstone + taches Obsidian ────────
            if (depth < 1.5f)
            {
                float patch = Mathf.PerlinNoise(dx * 0.18f + so + 50f, dz * 0.18f + so + 55f);
                return patch > 0.78f ? (byte)BlockType.Obsidian : (byte)BlockType.Blackstone;
            }

            // ── Sous-surface : mix ────────────────────────────
            if (depth < 4f)
            {
                float mix = Mathf.PerlinNoise(dx * 0.22f + so + 65f, dy * 0.22f + so + 70f);
                return mix > 0.52f ? (byte)BlockType.Obsidian : (byte)BlockType.Blackstone;
            }

            // ── Profondeur : Obsidian + minerais ──────────────
            float oreN = Mathf.PerlinNoise(dx * 0.28f + so + 85f,  dy * 0.28f + so + 90f);
            if (oreN > 0.83f) return (byte)BlockType.IronOre;

            float diaN = Mathf.PerlinNoise(dz * 0.32f + so + 105f, dx * 0.32f + so + 110f);
            if (diaN > 0.91f && depth > coreRadius * 0.25f) return (byte)BlockType.DiamondOre;

            float goldN = Mathf.PerlinNoise(dy * 0.30f + so + 125f, dz * 0.30f + so + 130f);
            if (goldN > 0.88f) return (byte)BlockType.GoldOre;

            return (byte)BlockType.Obsidian;
        }

        /// <summary>
        /// Remplit un ChunkData complet pour un astéroïde.
        /// Appelé par AsteroidWorld.SpawnChunk().
        /// </summary>
        public static void Generate(
            ChunkData  chunkData,
            Vector3    chunkOriginWorld,
            Vector3    asteroidCenter,
            Quaternion chunkRotation,
            float      coreRadius,
            int        seed)
        {
            int w = chunkData.Width;
            int h = chunkData.Height;

            for (int z = 0; z < w; z++)
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector3 blockPos = chunkOriginWorld
                    + chunkRotation * new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);

                chunkData.SetBlock(x, y, z,
                    GetBlockType(blockPos, asteroidCenter, coreRadius, seed));
            }
        }
    }
}

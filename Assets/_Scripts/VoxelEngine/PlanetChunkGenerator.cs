// ============================================================
//  PlanetChunkGenerator.cs
//  Remplit un ChunkData selon sa position sur la CubeSphere.
//  Aucune dépendance MonoBehaviour → appellable hors thread principal.
// ============================================================

using UnityEngine;

namespace AstroVoxel.VoxelEngine
{
    /// <summary>
    /// Génère le contenu d'un chunk en fonction de sa position 3D
    /// sur une planète sphérique. Utilise du bruit de Perlin pour
    /// moduler la hauteur de la surface.
    /// </summary>
    public static class PlanetChunkGenerator
    {
        // ── Paramètres de génération ──────────────────────────

        /// <summary>Rayon intérieur de la planète en blocs (core plein).</summary>
        public const float PlanetCoreRadius = 100f;

        /// <summary>Épaisseur de la croûte (blocs de terrain au-dessus du core).</summary>
        public const int CrustThickness = 12;

        /// <summary>Amplitude max de la déformation de surface en blocs.</summary>
        public const float SurfaceAmplitude = 4f;

        /// <summary>Fréquence du bruit de surface (plus petit = collines plus larges).</summary>
        public const float SurfaceFrequency = 0.025f;

        /// <summary>
        /// Profondeur en blocs en-dessous de la surface où commence le chunk (y=0).
        /// Doit être > CrustThickness + SurfaceAmplitude pour garantir des blocs solides en bas.
        /// </summary>
        public const float ChunkDepthBelowSurface = 22f;  // 12 croûte + 4 amplitude + 6 marge

        // ── Point d'entrée ────────────────────────────────────

        /// <summary>
        /// Remplit <paramref name="data"/> en fonction de la position
        /// world-space du coin (0,0,0) local du chunk.
        /// L'axe Y local du chunk pointe vers l'extérieur de la planète.
        /// </summary>
        /// <param name="data">ChunkData à remplir.</param>
        /// <param name="chunkOriginWorld">Position world du coin bas-gauche-avant du chunk.</param>
        /// <param name="planetCenter">Centre de la planète en world-space.</param>
        /// <param name="chunkUp">Direction "haut" planétaire pour ce chunk (normalisée).</param>
        /// <param name="chunkRight">Direction "droite" pour ce chunk (normalisée).</param>
        /// <param name="chunkForward">Direction "avant" pour ce chunk (normalisée).</param>
        public static void Generate(
            ChunkData data,
            Vector3   chunkOriginWorld,
            Vector3   planetCenter,
            Vector3   chunkUp,
            Vector3   chunkRight,
            Vector3   chunkForward)
        {
            int w = data.Width;
            int h = data.Height;

            for (int z = 0; z < w; z++)
            for (int x = 0; x < w; x++)
            {
                // Centre de la colonne en world-space (y=0 = bas du chunk = surface planète)
                Vector3 colBase = chunkOriginWorld
                                + chunkRight   * (x + 0.5f)
                                + chunkForward * (z + 0.5f);

                // Bruit 3D simulé avec PerlinNoise sur les coordonnées world absolues
                // → cohérent quelle que soit la face de la CubeSphere
                float nx = colBase.x * SurfaceFrequency;
                float nz = colBase.z * SurfaceFrequency;
                float ny = colBase.y * SurfaceFrequency;
                float noise = (Mathf.PerlinNoise(nx + 100f, nz + 100f)
                             + Mathf.PerlinNoise(ny + 200f, nx + 200f)) * 0.5f; // [0,1]

                // Rayon de la surface pour cette colonne
                float surfaceRadius = PlanetCoreRadius + SurfaceAmplitude * (noise - 0.5f);

                for (int y = 0; y < h; y++)
                {
                    // Position world exacte de ce bloc
                    Vector3 blockPos = chunkOriginWorld
                                     + chunkRight   * (x + 0.5f)
                                     + chunkUp      * (y + 0.5f)
                                     + chunkForward * (z + 0.5f);

                    // Distance radiale réelle au centre de la planète
                    float dist = Vector3.Distance(blockPos, planetCenter);

                    byte blockId;
                    if      (dist > surfaceRadius + 0.5f) blockId = (byte)BlockType.Air;
                    else if (dist > surfaceRadius - 0.5f) blockId = (byte)BlockType.Grass;
                    else if (dist > surfaceRadius - 3.5f) blockId = (byte)BlockType.Dirt;
                    else                                  blockId = (byte)BlockType.Stone;

                    data.SetBlock(x, y, z, blockId);
                }
            }
        }
    }
}

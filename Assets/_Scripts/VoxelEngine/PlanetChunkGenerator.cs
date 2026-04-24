// ============================================================
//  PlanetChunkGenerator.cs
//  Remplit un ChunkData cubique (16x16x16) selon sa position
//  dans la grille 3D de la planète.
//  Aucune dépendance MonoBehaviour → appellable hors thread.
// ============================================================

using UnityEngine;

namespace AstroVoxel.VoxelEngine
{
    public static class PlanetChunkGenerator
    {
        // ── Paramètres de génération ──────────────────────────

        /// <summary>Rayon de la surface de la planète en blocs.</summary>
        public const float PlanetCoreRadius = 50f;

        /// <summary>Épaisseur de la croûte solide sous la surface.</summary>
        public const int CrustThickness = 12;

        /// <summary>Amplitude max du bruit de surface (±blocs).</summary>
        public const float SurfaceAmplitude = 3f;

        /// <summary>Fréquence spatiale du bruit de terrain.</summary>
        public const float SurfaceFrequency = 0.03f;

        // ── Méthode centrale partagée ─────────────────────────

        /// <summary>
        /// Calcule le type de bloc pour TOUTE position world.
        /// Appelée à la fois par Generate et par ChunkMeshBuilder (culling OOB).
        /// UNE SEULE implémentation → cohérence garantie, zéro désynchronisation FP.
        /// </summary>
        public static byte GetBlockType(Vector3 worldPos, Vector3 planetCenter)
        {
            float dx = worldPos.x - planetCenter.x;
            float dy = worldPos.y - planetCenter.y;
            float dz = worldPos.z - planetCenter.z;
            float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

            if (dist < 0.001f) return (byte)BlockType.Stone;

            // Normalisation manuelle — IDENTIQUE à ce que fait Vector3.normalized
            float invDist = 1f / dist;
            float ndx = dx * invDist;
            float ndy = dy * invDist;
            float ndz = dz * invDist;

            float nx = ndx * SurfaceFrequency * PlanetCoreRadius + 100f;
            float ny = ndy * SurfaceFrequency * PlanetCoreRadius + 200f;
            float nz = ndz * SurfaceFrequency * PlanetCoreRadius + 300f;
            float noise = (Mathf.PerlinNoise(nx, nz) + Mathf.PerlinNoise(ny, nx)) * 0.5f;

            float surfaceRadius = PlanetCoreRadius + SurfaceAmplitude * (noise - 0.5f);

            if      (dist > surfaceRadius + 0.5f) return (byte)BlockType.Air;
            else if (dist > surfaceRadius - 0.5f) return (byte)BlockType.Grass;
            else if (dist > surfaceRadius - 3.5f) return (byte)BlockType.Dirt;
            else                                  return (byte)BlockType.Stone;
        }

        // ── Remplissage d'un chunk complet ────────────────────

        /// <summary>
        /// Remplit <paramref name="data"/> en appelant <see cref="GetBlockType"/>
        /// pour chaque bloc. Même résultat que le culling inter-chunks.
        /// </summary>
        public static void Generate(
            ChunkData data,
            Vector3   chunkOriginWorld,
            Vector3   planetCenter)
        {
            int w = data.Width;
            int h = data.Height;

            for (int z = 0; z < w; z++)
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector3 blockPos = chunkOriginWorld + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                data.SetBlock(x, y, z, GetBlockType(blockPos, planetCenter));
            }
        }
    }
}

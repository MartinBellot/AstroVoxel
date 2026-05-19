// ============================================================
//  PlanetGenerationConfig.cs
//  Paramètres complets de génération d'une planète procédurale.
//  Passé à PlanetChunkGenerator et PlanetWorld.
// ============================================================

using AstroVoxel.Space;
using UnityEngine;

namespace AstroVoxel.VoxelEngine
{
    /// <summary>
    /// Regroupe tous les paramètres de génération d'une planète.
    /// Struct immuable (passée par valeur/ref) → zéro allocation.
    /// </summary>
    public struct PlanetGenerationConfig
    {
        // ── Géométrie ─────────────────────────────────────────
        /// <summary>Rayon de la surface en blocs (avant bruit de terrain).</summary>
        public float CoreRadius;

        /// <summary>Épaisseur de la croûte solide sous la surface.</summary>
        public int   CrustThickness;

        /// <summary>Amplitude max de la variation de surface (±blocs).</summary>
        public float SurfaceAmplitude;

        /// <summary>Fréquence spatiale du bruit de terrain.</summary>
        public float SurfaceFrequency;

        // ── Biome ─────────────────────────────────────────────
        /// <summary>Type de biome (détermine l'aspect visuel et les blocs).</summary>
        public PlanetBiome Biome;

        // ── Seed ──────────────────────────────────────────────
        /// <summary>
        /// Décalage flottant dans l'espace bruit (dérivé de la world seed).
        /// Assure que deux planètes avec les mêmes paramètres aient des
        /// terrains différents.
        /// </summary>
        public float NoiseOffset;

        // ── Grottes ───────────────────────────────────────────
        public bool  HasCaves;
        public float CaveFrequency;
        public float CaveTubeRadius;
        public float CaveEntryRadius;
        public float CavePresenceThreshold;

        // ── Arbres ────────────────────────────────────────────
        public bool HasTrees;
        /// <summary>1 = normal, 2 = dense, 3 = rare.</summary>
        public int  TreeDensity;

        // ── Factories ─────────────────────────────────────────

        /// <summary>
        /// Config pour la planète de base (home planet).
        /// Équivalent exact des constantes originales codées en dur.
        /// </summary>
        public static PlanetGenerationConfig HomePlanet(int worldSeed)
            => ForBiome(PlanetBiome.Terran, PlanetChunkGenerator.PlanetCoreRadius, worldSeed, -1);

        /// <summary>
        /// Construit une config pour une planète procédurale selon son biome et sa taille.
        /// </summary>
        /// <param name="biome">Type de planète.</param>
        /// <param name="coreRadius">Rayon de la surface en blocs.</param>
        /// <param name="worldSeed">Seed globale du monde (WorldSeedManager.Seed).</param>
        /// <param name="planetIndex">Index unique de la planète (pour la seed dérivée).</param>
        public static PlanetGenerationConfig ForBiome(
            PlanetBiome biome,
            float       coreRadius,
            int         worldSeed,
            int         planetIndex)
        {
            // Pour la home planet (index -1), noiseOffset est dérivé directement du worldSeed
            float noiseOffset = planetIndex < 0
                ? ((worldSeed & 0xFFFF) * 0.00390625f)
                : WorldSeedManager.GetNoiseOffset(planetIndex);

            var cfg = new PlanetGenerationConfig
            {
                CoreRadius        = coreRadius,
                CrustThickness    = Mathf.Max(6, Mathf.RoundToInt(coreRadius * 0.24f)),
                SurfaceFrequency  = 0.03f,
                Biome             = biome,
                NoiseOffset       = noiseOffset,
                HasCaves          = true,
                CaveFrequency     = 0.045f,
                CaveTubeRadius    = 0.110f,
                CaveEntryRadius   = 0.068f,
                CavePresenceThreshold = 0.70f,
                HasTrees          = true,
                TreeDensity       = 1,
            };

            // ── Paramètres spécifiques par biome ──────────────
            switch (biome)
            {
                case PlanetBiome.Terran:
                    cfg.SurfaceAmplitude = coreRadius * 0.06f;
                    break;

                case PlanetBiome.Desert:
                    cfg.SurfaceAmplitude  = coreRadius * 0.14f;
                    cfg.SurfaceFrequency  = 0.025f;   // grandes dunes
                    cfg.HasCaves          = false;
                    cfg.HasTrees          = false;
                    break;

                case PlanetBiome.Snow:
                    cfg.SurfaceAmplitude = coreRadius * 0.07f;
                    cfg.TreeDensity      = 3;          // épicéas rares
                    break;

                case PlanetBiome.Volcanic:
                    cfg.SurfaceAmplitude      = coreRadius * 0.16f;
                    cfg.SurfaceFrequency      = 0.042f;
                    cfg.HasTrees              = false;
                    cfg.CaveTubeRadius        = 0.14f;   // tunnels de lave larges
                    cfg.CavePresenceThreshold = 0.55f;   // grottes partout
                    break;

                case PlanetBiome.Forest:
                    cfg.SurfaceAmplitude = coreRadius * 0.05f;
                    cfg.TreeDensity      = 2;          // forêt dense
                    break;

                case PlanetBiome.Mountain:
                    cfg.SurfaceAmplitude = coreRadius * 0.22f;  // très hautes montagnes
                    cfg.SurfaceFrequency = 0.022f;              // larges massifs
                    cfg.TreeDensity      = 3;                   // rares
                    break;

                case PlanetBiome.Endstone:
                    cfg.SurfaceAmplitude = coreRadius * 0.04f;
                    cfg.HasCaves         = false;
                    cfg.HasTrees         = false;
                    break;

                case PlanetBiome.Crystal:
                    cfg.SurfaceAmplitude = coreRadius * 0.05f;
                    cfg.HasTrees         = false;
                    cfg.CaveTubeRadius   = 0.13f;
                    break;

                case PlanetBiome.Nether:
                    cfg.SurfaceAmplitude      = coreRadius * 0.17f;
                    cfg.SurfaceFrequency      = 0.045f;
                    cfg.HasTrees              = false;
                    cfg.CaveTubeRadius        = 0.13f;
                    cfg.CavePresenceThreshold = 0.52f;
                    break;

                case PlanetBiome.Cherry:
                    cfg.SurfaceAmplitude = coreRadius * 0.07f;
                    cfg.TreeDensity      = 1;
                    break;

                case PlanetBiome.Mossy:
                    cfg.SurfaceAmplitude = coreRadius * 0.08f;
                    cfg.TreeDensity      = 2;
                    break;
            }

            return cfg;
        }
    }
}

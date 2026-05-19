// ============================================================
//  WorldSeedManager.cs
//  Gestionnaire de seed global — persistant entre rechargements.
//  Static pur : zéro MonoBehaviour, zéro DontDestroyOnLoad.
// ============================================================

namespace AstroVoxel.Space
{
    /// <summary>
    /// Stocke la seed du monde courant et fournit des seeds dérivées
    /// pour chaque planète procédurale.
    /// </summary>
    public static class WorldSeedManager
    {
        // ── Seed ──────────────────────────────────────────────

        /// <summary>Seed actuelle du monde (lue par tous les générateurs).</summary>
        public static int Seed { get; private set; } = 0;

        /// <summary>Vrai si la seed a été initialisée au moins une fois.</summary>
        public static bool IsInitialized { get; private set; } = false;

        // ── Initialisation ────────────────────────────────────

        /// <summary>
        /// Initialise la seed. Appelé par GameBootstrap au démarrage.
        /// Si appelé avec seed=0 et pas encore initialisé, génère une seed aléatoire.
        /// </summary>
        public static void Initialize(int seed = 0)
        {
            if (seed == 0 && !IsInitialized)
                seed = GenerateRandom();
            Seed          = seed;
            IsInitialized = true;
        }

        /// <summary>
        /// Génère et applique une nouvelle seed aléatoire.
        /// Appelé par la commande /restart.
        /// </summary>
        public static void GenerateNewSeed()
        {
            Seed          = GenerateRandom();
            IsInitialized = true;
        }

        // ── Seeds dérivées ────────────────────────────────────

        /// <summary>
        /// Retourne une seed déterministe pour la planète d'index <paramref name="planetIndex"/>.
        /// Deux appels avec le même index et la même world seed retournent toujours
        /// la même valeur.
        /// </summary>
        public static int GetPlanetSeed(int planetIndex)
        {
            unchecked
            {
                int h = Seed ^ (planetIndex * 1234567891);
                h ^= h >> 13;
                h *= 1540483477;
                h ^= h >> 15;
                return h;
            }
        }

        /// <summary>
        /// Retourne un décalage de bruit (float) pour la planète
        /// <paramref name="planetIndex"/>.
        /// Plage : [0, 256).
        /// </summary>
        public static float GetNoiseOffset(int planetIndex)
        {
            int ps = GetPlanetSeed(planetIndex);
            // Prend les 16 bits bas, normalise → [0, 256)
            return (ps & 0xFFFF) * 0.00390625f;   // /256
        }

        /// <summary>
        /// Retourne un générateur aléatoire initialisé avec Seed + <paramref name="salt"/>.
        /// </summary>
        public static System.Random GetRng(int salt = 0)
            => new System.Random(unchecked(Seed + salt));

        // ── Utilitaire ────────────────────────────────────────

        private static int GenerateRandom()
        {
            // Tick count XOR avec un multiple premier → distribution large
            int t = System.Environment.TickCount;
            unchecked { return (t * 1664525 + 1013904223) ^ (t >> 16); }
        }
    }
}

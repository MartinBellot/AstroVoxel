// ============================================================
//  AsteroidSystemManager.cs
//  Singleton central du système astéroïde.
//
//  Responsabilités :
//    1. Crée les 3 champs d'astéroïdes (Close, MainBelt, OuterBelt).
//    2. Instancie le MeteoriteSpawner.
//    3. Gère le budget voxel global (max astéroïdes chargés en voxels).
// ============================================================

using UnityEngine;

namespace AstroVoxel.Space
{
    /// <summary>
    /// Gestionnaire global du système astéroïde.
    /// Un seul instance par scène — créé par <c>GameBootstrap</c>.
    /// </summary>
    public sealed class AsteroidSystemManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────
        public static AsteroidSystemManager Instance { get; private set; }

        // ── Budget voxel ──────────────────────────────────────
        /// <summary>Nombre max d'astéroïdes pouvant être en mode voxel simultanément.</summary>
        private const int MaxVoxelAsteroids = 4;

        // ── Champs créés ──────────────────────────────────────
        private AsteroidField _closeCluster;
        private AsteroidField _mainBelt;
        private AsteroidField _outerBelt;

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Initialisation publique ───────────────────────────

        /// <summary>
        /// Appelé par <c>GameBootstrap</c> après la construction du joueur.
        /// </summary>
        /// <param name="player">Transform du joueur (pour le LOD).</param>
        /// <param name="blockMaterials">Matériaux partagés.</param>
        /// <param name="sunPosition">Position du soleil dans le monde.</param>
        public void Init(
            Transform  player,
            Material[] blockMaterials,
            Vector3    sunPosition)
        {
            Vector3 planetCenter = Vector3.zero;

            // ─ Champ 1 : Close Cluster (orbite basse autour de la planète) ─
            // innerOrbitRadius >= playerSpawnDist(60) + DistVoxel(90) + marge = 160+
            _closeCluster               = CreateField("CloseCluster");
            _closeCluster.orbitCenter   = planetCenter;
            _closeCluster.innerOrbitRadius = 160f;
            _closeCluster.outerOrbitRadius = 220f;
            _closeCluster.asteroidCount  = 15;
            _closeCluster.seed           = 100;
            _closeCluster.sizeRange      = new Vector2(3f,  8f);
            _closeCluster.tiltRange      = new Vector2(10f, 80f);   // large spread 3D
            _closeCluster.blockMaterials = blockMaterials;
            _closeCluster.player         = player;
            _closeCluster.Build();

            // ─ Champ 2 : Main Belt (ceinture principale) ─
            _mainBelt                   = CreateField("MainBelt");
            _mainBelt.orbitCenter        = planetCenter;
            _mainBelt.innerOrbitRadius   = 280f;
            _mainBelt.outerOrbitRadius   = 420f;
            _mainBelt.asteroidCount      = 80;
            _mainBelt.seed               = 200;
            _mainBelt.sizeRange          = new Vector2(5f, 22f);
            _mainBelt.tiltRange          = new Vector2(5f, 90f);    // très inclinés, distribution sphérique
            _mainBelt.blockMaterials     = blockMaterials;
            _mainBelt.player             = player;
            _mainBelt.Build();

            // ─ Champ 3 : Outer Belt (orbite lointaine autour du soleil) ─
            _outerBelt                  = CreateField("OuterBelt");
            _outerBelt.orbitCenter       = sunPosition;
            _outerBelt.innerOrbitRadius  = 300f;
            _outerBelt.outerOrbitRadius  = 500f;
            _outerBelt.asteroidCount     = 30;
            _outerBelt.seed              = 300;
            _outerBelt.sizeRange         = new Vector2(15f, 50f);
            _outerBelt.tiltRange         = new Vector2(-35f, 35f);
            _outerBelt.blockMaterials    = blockMaterials;
            _outerBelt.player            = player;
            _outerBelt.Build();

            // ─ Spawner de météorites ─
            var msGO = new GameObject("MeteoriteSpawner");
            msGO.transform.SetParent(transform, worldPositionStays: true);
            var spawner = msGO.AddComponent<MeteoriteSpawner>();
            spawner.Init(player, blockMaterials);
        }

        // ── Utilitaires ───────────────────────────────────────

        private AsteroidField CreateField(string fieldName)
        {
            var go = new GameObject($"AsteroidField_{fieldName}");
            go.transform.SetParent(transform, worldPositionStays: true);
            return go.AddComponent<AsteroidField>();
        }
    }
}

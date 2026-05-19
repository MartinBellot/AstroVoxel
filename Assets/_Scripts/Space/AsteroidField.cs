// ============================================================
//  AsteroidField.cs
//  Génère procéduralement un champ d'astéroïdes en anneau orbital.
//
//  Un "champ" = N astéroïdes répartis sur un anneau
//  (innerOrbitRadius → outerOrbitRadius) autour d'un corps central.
//
//  Chaque astéroïde reçoit :
//    • AsteroidOrbit   — mouvement orbital + tumble
//    • GravityAttractor — micro-gravité surfacique
//    • AsteroidWorld   — monde voxel (piloté par AsteroidLOD)
//    • AsteroidLOD     — LOD automatique selon distance joueur
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using AstroVoxel.Physics;

namespace AstroVoxel.Space
{
    /// <summary>
    /// Champ d'astéroïdes en anneau orbital.
    /// Instancier puis appeler <see cref="Build"/> pour peupler le champ.
    /// </summary>
    public sealed class AsteroidField : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────

        [Header("Orbite")]
        [Tooltip("Centre de l'orbite (planète = Vector3.zero, ou position du soleil).")]
        public Vector3 orbitCenter      = Vector3.zero;

        [Tooltip("Rayon intérieur de l'anneau.")]
        public float   innerOrbitRadius = 120f;

        [Tooltip("Rayon extérieur de l'anneau.")]
        public float   outerOrbitRadius = 200f;

        [Tooltip("Nombre d'astéroïdes.")]
        public int     asteroidCount    = 80;

        [Header("Paramètres de génération")]
        [Tooltip("Seed global du champ.")]
        public int     seed             = 1234;

        [Tooltip("Plage de taille (coreRadius min/max).")]
        public Vector2 sizeRange        = new Vector2(5f, 22f);

        [Tooltip("Plage d'inclinaison du plan orbital (degrés).")]
        public Vector2 tiltRange        = new Vector2(-20f, 20f);

        [Header("Matériaux")]
        public Material[] blockMaterials;

        [Header("Joueur")]
        [Tooltip("Transform du joueur (passé à AsteroidLOD).")]
        public Transform player;

        // ── État ──────────────────────────────────────────────
        private readonly List<GameObject> _asteroids = new List<GameObject>();

        // ── API publique ──────────────────────────────────────

        /// <summary>
        /// Construit tous les astéroïdes du champ.
        /// Idempotent : détruit l'existant si appelé deux fois.
        /// </summary>
        public void Build()
        {
            foreach (var a in _asteroids)
                if (a != null) Destroy(a);
            _asteroids.Clear();

            var rng = new System.Random(seed);

            for (int i = 0; i < asteroidCount; i++)
            {
                int   aSeed       = rng.Next(0, int.MaxValue);
                float radius      = Mathf.Lerp(sizeRange.x, sizeRange.y, (float)rng.NextDouble());
                float orbitR      = Mathf.Lerp(innerOrbitRadius, outerOrbitRadius, (float)rng.NextDouble());
                float phase       = (float)(rng.NextDouble() * 360.0);
                float tilt        = Mathf.Lerp(tiltRange.x, tiltRange.y, (float)rng.NextDouble());
                float orbitSpeed  = Mathf.Lerp(0.03f, 0.09f, (float)rng.NextDouble());

                var go = CreateAsteroid($"Asteroid_{i}", aSeed, radius, orbitR, phase, tilt, orbitSpeed);
                _asteroids.Add(go);
            }
        }

        // ── Création unitaire ─────────────────────────────────

        private GameObject CreateAsteroid(
            string name,
            int    aSeed,
            float  radius,
            float  orbitR,
            float  phase,
            float  tilt,
            float  orbitSpeed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: true);

            // ─ Orbit ─
            var orbit          = go.AddComponent<AsteroidOrbit>();
            orbit.orbitCenter  = orbitCenter;
            orbit.orbitRadius  = orbitR;
            orbit.orbitSpeed   = orbitSpeed;
            orbit.orbitTilt    = tilt;
            orbit.orbitPhase   = phase;

            // Rotation propre pseudo-aléatoire basée sur la seed
            var rng = new System.Random(aSeed);
            orbit.selfRotationSpeed = new Vector3(
                Mathf.Lerp(0.5f, 2.5f, (float)rng.NextDouble()),
                Mathf.Lerp(0.3f, 2.0f, (float)rng.NextDouble()),
                Mathf.Lerp(0.2f, 1.5f, (float)rng.NextDouble()));

            // Axe d'inclinaison orbitale aléatoire → distribution sphérique 3D
            // Distribution uniforme sur la sphère via la méthode Marsaglia
            float axPhi  = (float)(rng.NextDouble() * 2.0 * System.Math.PI);
            float axCosT = (float)(rng.NextDouble() * 2.0 - 1.0);
            float axSinT = Mathf.Sqrt(Mathf.Max(0f, 1f - axCosT * axCosT));
            orbit.orbitTiltAxis = new Vector3(
                axSinT * Mathf.Cos(axPhi),
                axCosT,
                axSinT * Mathf.Sin(axPhi));

            // ─ GravityAttractor ─
            var grav = go.AddComponent<GravityAttractor>();
            // Force gravitationnelle proportionnelle à la masse (rayon³)
            float gravForce    = Mathf.Lerp(0.8f, 4.0f, (radius - sizeRange.x) / Mathf.Max(0.01f, sizeRange.y - sizeRange.x));
            float influence    = radius * 2.5f;
            grav.SetAsteroidParams(gravForce, influence);

            // ─ AsteroidWorld ─
            var world          = go.AddComponent<AsteroidWorld>();
            world.coreRadius   = radius;
            world.seed         = aSeed;
            world.blockMaterials = blockMaterials;

            // ─ AsteroidLOD ─
            var lod = go.AddComponent<AsteroidLOD>();
            lod.Init(player);

            return go;
        }
    }
}

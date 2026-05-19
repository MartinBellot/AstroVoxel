// ============================================================
//  MeteoriteSpawner.cs
//  Gère le pool de météorites et les lance périodiquement.
//
//  Pool de 8 météorites pré-alloués, recyclés à la demande.
//  Les météorites sont tirés de loin (450u) en direction
//  de la planète avec une légère déviation aléatoire (±15°).
// ============================================================

using UnityEngine;

namespace AstroVoxel.Space
{
    /// <summary>
    /// Spawner de météorites à intervalles aléatoires.
    /// Instancié et initialisé par <see cref="AsteroidSystemManager"/>.
    /// </summary>
    public sealed class MeteoriteSpawner : MonoBehaviour
    {
        // ── Configuration ─────────────────────────────────────
        private const int   PoolSize       = 8;
        private const float SpawnDistMin   = 400f;
        private const float SpawnDistMax   = 500f;
        private const float SpeedMin       = 80f;
        private const float SpeedMax       = 200f;
        private const float RadiusMin      = 0.4f;
        private const float RadiusMax      = 2.0f;
        private const float IntervalMin    = 8f;
        private const float IntervalMax    = 25f;
        private const float AimDeviation   = 15f;   // degrés de déviation max

        // ── État ──────────────────────────────────────────────
        private MeteoriteController[] _pool;
        private Transform             _player;
        private Material[]            _blockMaterials;
        private AstroVoxel.VoxelEngine.PlanetWorld _planet;
        private float                 _timer;
        private float                 _nextInterval;

        // ── API publique ──────────────────────────────────────

        /// <summary>
        /// Initialisation appelée par AsteroidSystemManager.
        /// </summary>
        public void Init(Transform player, Material[] blockMaterials)
        {
            _player         = player;
            _blockMaterials = blockMaterials;
            _planet         = FindAnyObjectByType<AstroVoxel.VoxelEngine.PlanetWorld>();

            // Pré-allocation du pool
            _pool = new MeteoriteController[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject($"Meteorite_{i}");
                go.transform.SetParent(transform, worldPositionStays: true);
                _pool[i] = go.AddComponent<MeteoriteController>();
                go.SetActive(false);
            }

            _nextInterval = Random.Range(IntervalMin, IntervalMax);
        }

        // ── Cycle de vie ──────────────────────────────────────

        private void Update()
        {
            if (_player == null) return;

            _timer += Time.deltaTime;
            if (_timer >= _nextInterval)
            {
                _timer        = 0f;
                _nextInterval = Random.Range(IntervalMin, IntervalMax);
                SpawnMeteorite();
            }
        }

        // ── Spawn ─────────────────────────────────────────────

        private void SpawnMeteorite()
        {
            MeteoriteController ctrl = GetFromPool();
            if (ctrl == null) return;  // pool épuisé

            // Origine : position aléatoire dans une sphère autour du joueur
            float     spawnDist    = Random.Range(SpawnDistMin, SpawnDistMax);
            Vector3   randomDir    = Random.onUnitSphere;
            Vector3   startPos     = _player.position + randomDir * spawnDist;

            // Direction cible : vers Vector3.zero (planète) ±déviation
            Vector3   toTarget     = (Vector3.zero - startPos).normalized;
            Quaternion deviation   = Quaternion.AngleAxis(
                Random.Range(-AimDeviation, AimDeviation),
                Random.onUnitSphere);
            Vector3 direction      = deviation * toTarget;

            float speed  = Random.Range(SpeedMin, SpeedMax);
            float radius = Random.Range(RadiusMin, RadiusMax);

            ctrl.Launch(startPos, direction, speed, radius, _planet, _blockMaterials);
        }

        private MeteoriteController GetFromPool()
        {
            foreach (var c in _pool)
                if (c != null && c.IsAvailable) return c;
            return null;
        }
    }
}

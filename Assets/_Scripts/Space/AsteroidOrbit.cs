// ============================================================
//  AsteroidOrbit.cs
//  Mouvement orbital képlérien simplifié + rotation propre (tumble).
//
//  Fonctionnement :
//    • Déplace transform.position sur une ellipse dans Update().
//    • Expose OrbitalVelocity (u/s) pour que GravityAttractor puisse
//      l'utiliser si besoin.
//    • La rotation propre (tumble) simule la rotation chaotique
//      des corps non stabilisés.
// ============================================================

using UnityEngine;

namespace AstroVoxel.Space
{
    /// <summary>
    /// Orbite képlérienne simplifiée + tumble pour un astéroïde.
    /// Le centre de l'orbite est défini en world-space (planète ou soleil).
    /// </summary>
    public sealed class AsteroidOrbit : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────

        [Header("Orbite")]
        [Tooltip("Position monde du corps central (planète = Vector3.zero, ou soleil).")]
        public Vector3 orbitCenter = Vector3.zero;

        [Tooltip("Rayon orbital en unités.")]
        public float orbitRadius = 150f;

        [Tooltip("Vitesse orbitale en degrés/seconde. Valeur faible pour les astéroïdes.")]
        public float orbitSpeed  = 0.04f;

        [Tooltip("Inclinaison du plan orbital (degrés). ±30° max pour les champs.")]
        public float orbitTilt   = 5f;

        [Tooltip("Phase initiale en degrés (position de départ sur l'orbite).")]
        public float orbitPhase  = 0f;

        [Header("Rotation propre (tumble)")]
        [Tooltip("Vitesse de rotation propre sur chaque axe local (degrés/s).")]
        public Vector3 selfRotationSpeed = new Vector3(2f, 1.3f, 0.7f);

        /// <summary>
        /// Axe autour duquel le plan orbital est incliné.
        /// Assigné par <see cref="AsteroidField"/> pour une distribution 3D sphérique.
        /// </summary>
        [HideInInspector]
        public Vector3 orbitTiltAxis = Vector3.forward;

        // ── État interne ──────────────────────────────────────

        private float   _angle;
        private Vector3 _tiltAxis;   // axe d'inclinaison du plan orbital
        private Vector3 _prevPos;

        // ── Propriétés publiques ──────────────────────────────

        /// <summary>
        /// Vitesse orbitale courante en unités/seconde (delta position / dt).
        /// Exposée pour GravityAttractor (déplacement du joueur avec l'astéroïde).
        /// </summary>
        public Vector3 OrbitalVelocity { get; private set; }

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            // Initialisation minimale — les champs publics ne sont PAS encore assignés
            // (AddComponent appelle Awake immédiatement, avant les assignments de AsteroidField).
            // La position réelle est fixée dans Start(), après assignment de tous les champs.
            _tiltAxis       = Vector3.forward;
            OrbitalVelocity = Vector3.zero;
        }

        private void Start()
        {
            // Ici tous les champs (orbitPhase, orbitRadius, orbitTiltAxis…) sont assignés.
            _angle    = orbitPhase;
            _tiltAxis = orbitTiltAxis.sqrMagnitude > 0.0001f
                ? orbitTiltAxis.normalized
                : Vector3.forward;

            _prevPos           = ComputePosition(_angle);
            transform.position = _prevPos;
        }

        private void Update()
        {
            _angle = (_angle + orbitSpeed * Time.deltaTime) % 360f;

            Vector3 newPos = ComputePosition(_angle);

            // Vitesse orbitale (pour les systèmes qui en ont besoin)
            if (Time.deltaTime > 0f)
                OrbitalVelocity = (newPos - _prevPos) / Time.deltaTime;

            _prevPos           = newPos;
            transform.position = newPos;

            // Rotation propre (tumble chaotique)
            transform.Rotate(selfRotationSpeed * Time.deltaTime, UnityEngine.Space.Self);
        }

        // ── Calcul de position ────────────────────────────────

        private Vector3 ComputePosition(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;

            // Plan orbital de base (XZ)
            Vector3 flatPos = new Vector3(
                Mathf.Cos(rad) * orbitRadius,
                0f,
                Mathf.Sin(rad) * orbitRadius);

            // Inclinaison du plan orbital autour de _tiltAxis
            Vector3 tiltedPos = Quaternion.AngleAxis(orbitTilt, _tiltAxis) * flatPos;

            return orbitCenter + tiltedPos;
        }
    }
}

// ============================================================
//  GravityBody.cs
//  Corps soumis à la gravité d'un GravityAttractor.
//  Désactive la gravité Unity standard et s'aligne vers le bas
//  planétaire à chaque FixedUpdate.
// ============================================================

using UnityEngine;

namespace AstroVoxel.Physics
{
    /// <summary>
    /// À placer sur tout objet physique (joueur, objet, ennemi…)
    /// devant être attiré vers un <see cref="GravityAttractor"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class GravityBody : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────

        [Tooltip("L'attracteur planétaire cible. Si null, cherché automatiquement dans la scène.")]
        [SerializeField] private GravityAttractor attractor;

        [Tooltip("Vitesse (degrés/s) de rotation pour aligner 'up' avec la verticale planétaire.")]
        [SerializeField, Range(1f, 360f)] private float alignmentSpeed = 180f;

        // ── Composants ────────────────────────────────────────

        private Rigidbody _rb;

        // ── Cycle de vie Unity ────────────────────────────────

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            // Désactive la gravité globale de Unity — c'est l'attracteur qui s'en charge.
            _rb.useGravity = false;

            // Empêche Unity de faire tourner le Rigidbody via la physique :
            // la rotation est gérée manuellement pour le bas planétaire.
            _rb.freezeRotation = true;

            // Auto-détection si non assigné dans l'Inspector.
            if (attractor == null)
                attractor = FindAnyObjectByType<GravityAttractor>();

            if (attractor == null)
                Debug.LogWarning($"[GravityBody] Aucun GravityAttractor trouvé pour {gameObject.name}.", this);
        }

        private void FixedUpdate()
        {
            if (attractor == null) return;

            // 1. Applique la force d'attraction.
            attractor.Attract(_rb);

            // 2. Aligne doucement transform.up vers le "haut" planétaire (= opposé de la gravité).
            AlignUpToPlanetSurface();
        }

        // ── Alignement de l'orientation ───────────────────────

        /// <summary>
        /// Tourne le transform pour que son axe Y local ("up") pointe
        /// à l'opposé du centre de la planète, simulant le "sol sous les pieds".
        /// Interpolation sphérique limitée par <see cref="alignmentSpeed"/>.
        /// </summary>
        private void AlignUpToPlanetSurface()
        {
            // Direction "vers le haut" depuis la surface de la planète.
            Vector3 planetUp = (_rb.position - attractor.transform.position).normalized;

            // Rotation cible : aligne l'axe Y local sur planetUp, conserve l'orientation X/Z.
            Quaternion targetRotation = Quaternion.FromToRotation(transform.up, planetUp) * transform.rotation;

            // Interpolation angulaire douce, indépendante du framerate.
            float maxDeg = alignmentSpeed * Time.fixedDeltaTime;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, maxDeg);
        }
    }
}

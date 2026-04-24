// ============================================================
//  GravityBody.cs
//  Corps soumis à la gravité d'un GravityAttractor.
//  Désactive la gravité Unity standard et s'aligne vers le bas
//  planétaire à chaque FixedUpdate.
//
//  Supporte la couche d'ozone (OzoneLayer) :
//   • À l'intérieur de l'atmosphère → gravité planétaire + alignement.
//   • En dehors (espace) → inertie pure, aucune force, amortissement nul.
// ============================================================

using UnityEngine;

namespace AstroVoxel.Physics
{
    /// <summary>
    /// À placer sur tout objet physique (joueur, objet, ennemi…)
    /// devant être attiré vers un <see cref="GravityAttractor"/>.
    /// Prend en compte la <see cref="OzoneLayer"/> pour basculer entre
    /// physique planétaire et physique spatiale (inertie pure).
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

        private Rigidbody  _rb;
        private OzoneLayer _ozoneLayer;

        // ── État physique ─────────────────────────────────────

        /// <summary>
        /// Amortissement linéaire nominal (sauvegardé au démarrage depuis le Rigidbody).
        /// Restauré automatiquement quand le corps ré-entre dans l'atmosphère.
        /// </summary>
        private float _atmosphereDamping;

        /// <summary>Amortissement en espace : 0 = inertie newtonienne pure.</summary>
        private const float SpaceDamping = 0f;

        /// <summary>Indique si le corps est actuellement dans l'atmosphère planétaire.</summary>
        private bool _inAtmosphere = true;

        // ── Propriétés publiques ──────────────────────────────

        /// <summary><c>true</c> = dans l'atmosphère (gravité planétaire active).</summary>
        public bool IsInAtmosphere => _inAtmosphere;

        // ── Cycle de vie Unity ────────────────────────────────

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            // Désactive la gravité globale de Unity — c'est l'attracteur qui s'en charge.
            _rb.useGravity = false;

            // Empêche Unity de faire tourner le Rigidbody via la physique :
            // la rotation est gérée manuellement pour le bas planétaire.
            _rb.freezeRotation = true;

            // Sauvegarde l'amortissement configuré dans GameBootstrap (ex. 0.5f)
            _atmosphereDamping = _rb.linearDamping;

            // Auto-détection si non assigné dans l'Inspector.
            if (attractor == null)
                attractor = FindAnyObjectByType<GravityAttractor>();

            if (attractor == null)
                Debug.LogWarning($"[GravityBody] Aucun GravityAttractor trouvé pour {gameObject.name}.", this);

            // Récupère la couche d'ozone (facultative : si absente, toujours en atmosphère).
            _ozoneLayer = FindAnyObjectByType<OzoneLayer>();
        }

        private void FixedUpdate()
        {
            if (attractor == null) return;

            // ── Détecte les transitions atmosphère ↔ espace ───
            bool nowInAtmosphere = _ozoneLayer == null
                                || _ozoneLayer.IsInsideAtmosphere(_rb.position);

            if (nowInAtmosphere != _inAtmosphere)
            {
                _inAtmosphere = nowInAtmosphere;
                OnAtmosphereChanged(_inAtmosphere);
            }

            // ── Physique spatiale : inertie pure ───────────────
            if (!_inAtmosphere) return;

            // ── Physique planétaire ────────────────────────────
            // 1. Applique la force d'attraction.
            attractor.Attract(_rb);

            // 2. Aligne doucement transform.up vers le "haut" planétaire (= opposé de la gravité).
            AlignUpToPlanetSurface();
        }

        // ── Transition atmosphère / espace ────────────────────

        /// <summary>
        /// Appelé à chaque changement de régime.
        /// Ajuste l'amortissement du Rigidbody et logge la transition.
        /// </summary>
        private void OnAtmosphereChanged(bool enteredAtmosphere)
        {
            if (enteredAtmosphere)
            {
                // Ré-entre dans l'atmosphère : restaure l'amortissement et la gravité.
                _rb.linearDamping = _atmosphereDamping;
                Debug.Log($"[GravityBody] {gameObject.name} est entré dans l'atmosphère.", this);
            }
            else
            {
                // Quitte l'atmosphère : amortissement nul = inertie newtonienne pure.
                _rb.linearDamping = SpaceDamping;
                Debug.Log($"[GravityBody] {gameObject.name} a quitté l'atmosphère → espace.", this);
            }
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

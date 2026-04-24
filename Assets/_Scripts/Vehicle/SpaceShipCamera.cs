// ============================================================
//  SpaceShipCamera.cs
//  Caméra 3e personne qui suit le vaisseau depuis l'arrière.
//  Fonctionne en tout référentiel (surface planétaire et espace)
//  car elle utilise les axes locaux du vaisseau comme référence.
//
//  Paramètres Inspector :
//    followDistance  — recul depuis le vaisseau
//    followHeight    — décalage vertical local
//    followSmooth    — inertie de la position (0 = instantané)
//    lookSmooth      — inertie de l'orientation
// ============================================================

using UnityEngine;

namespace AstroVoxel.Vehicle
{
    /// <summary>
    /// Caméra Spring-Arm 3e personne pour le vaisseau.
    /// Se positionne derrière le vaisseau sur son axe -forward
    /// et regarde vers lui (légèrement devant son pivot).
    /// </summary>
    public sealed class SpaceShipCamera : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────

        [Header("Cible")]
        [Tooltip("Transform du vaisseau à suivre.")]
        [SerializeField] public Transform target;

        [Header("Bras ressort")]
        [Tooltip("Recul depuis le vaisseau (axe -forward local).")]
        [SerializeField] private float followDistance = 25f;

        [Tooltip("Décalage vertical local (axe +up).")]
        [SerializeField] private float followHeight   = 7f;

        [Header("Lissage")]
        [Tooltip("Vitesse de suivi de la position (0 = collé, ∞ = instantané).")]
        [SerializeField] private float followSmooth   = 8f;

        [Tooltip("Vitesse d'orientation vers le vaisseau.")]
        [SerializeField] private float lookSmooth     = 12f;

        [Header("Cible visuelle")]
        [Tooltip("Offset avant pour que la caméra regarde légèrement devant le vaisseau.")]
        [SerializeField] private float lookAheadOffset = 8f;

        // ── Cycle de vie ──────────────────────────────────────

        private void LateUpdate()
        {
            if (target == null) return;

            // ── Position désirée : derrière et au-dessus du vaisseau ──
            Vector3 desiredPos = target.position
                                 - target.forward * followDistance
                                 + target.up      * followHeight;

            transform.position = Vector3.Lerp(
                transform.position,
                desiredPos,
                Time.deltaTime * followSmooth
            );

            // ── Orientation : regarde vers le vaisseau (légèrement devant) ──
            Vector3 lookTarget = target.position + target.forward * lookAheadOffset;
            Vector3 lookDir    = lookTarget - transform.position;

            if (lookDir.sqrMagnitude > 0.001f)
            {
                Quaternion desiredRot = Quaternion.LookRotation(lookDir, target.up);
                transform.rotation   = Quaternion.Slerp(
                    transform.rotation,
                    desiredRot,
                    Time.deltaTime * lookSmooth
                );
            }
        }
    }
}

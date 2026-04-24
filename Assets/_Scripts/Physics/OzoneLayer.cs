// ============================================================
//  OzoneLayer.cs
//  Définit la couche d'ozone (frontière atmosphère / espace)
//  autour d'une planète sphérique.
//
//  Règle : si le joueur est à moins de AtmosphereRadius du centre
//  de la planète → gravité planétaire (GravityBody actif).
//  Au-delà → gravité spatiale (inertie pure, aucune attraction).
// ============================================================

using UnityEngine;
using AstroVoxel.VoxelEngine;

namespace AstroVoxel.Physics
{
    /// <summary>
    /// Composant posé sur le GameObject planète.
    /// Fournit <see cref="IsInsideAtmosphere"/> pour les <see cref="GravityBody"/>
    /// qui doivent décider de quel régime physique appliquer.
    /// </summary>
    public sealed class OzoneLayer : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────

        [Tooltip("Hauteur de la couche d'ozone au-dessus de la surface planétaire (en blocs). " +
                 "La surface est à PlanetCoreRadius ≈ 50 blocs du centre.")]
        [SerializeField, Range(5f, 100f)] private float atmosphereHeight = 30f;

        [Tooltip("Épaisseur de la zone de transition (progressivité de la frontière).")]
        [SerializeField, Range(0f, 10f)] private float transitionThickness = 3f;

        // ── Propriétés ────────────────────────────────────────

        /// <summary>Rayon de la couche d'ozone = surface + hauteur atmosphérique.</summary>
        public float AtmosphereRadius =>
            PlanetChunkGenerator.PlanetCoreRadius + atmosphereHeight;

        /// <summary>Centre de la planète (position monde du GameObject).</summary>
        public Vector3 Center => transform.position;

        // ── API publique ──────────────────────────────────────

        /// <summary>
        /// Retourne <c>true</c> si <paramref name="worldPosition"/> est
        /// à l'intérieur (ou sur la frontière) de l'atmosphère planétaire.
        /// </summary>
        public bool IsInsideAtmosphere(Vector3 worldPosition)
        {
            float dist = Vector3.Distance(transform.position, worldPosition);
            return dist <= AtmosphereRadius + transitionThickness * 0.5f;
        }

        /// <summary>
        /// Retourne un coefficient [0,1] indiquant la position du corps
        /// dans la zone de transition : 0 = plein espace, 1 = plein atmosphere.
        /// Utile pour des transitions visuelles progressives.
        /// </summary>
        public float GetAtmosphereBlend(Vector3 worldPosition)
        {
            if (transitionThickness <= 0f)
                return IsInsideAtmosphere(worldPosition) ? 1f : 0f;

            float dist  = Vector3.Distance(transform.position, worldPosition);
            float inner = AtmosphereRadius - transitionThickness * 0.5f;
            float outer = AtmosphereRadius + transitionThickness * 0.5f;
            return 1f - Mathf.Clamp01((dist - inner) / transitionThickness);
        }

        // ── Gizmos ────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Sphère verte = limite intérieure de la transition
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
            Gizmos.DrawWireSphere(transform.position,
                AtmosphereRadius - transitionThickness * 0.5f);

            // Sphère bleue = limite extérieure de la transition (couche d'ozone visible)
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position,
                AtmosphereRadius + transitionThickness * 0.5f);
        }
#endif
    }
}

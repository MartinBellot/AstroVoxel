// ============================================================
//  GravityAttractor.cs
//  Définit un puits de gravité centripète (planète, astéroïde…).
//  S'attache au GameObject représentant le corps céleste.
// ============================================================

using UnityEngine;

namespace AstroVoxel.Physics
{
    /// <summary>
    /// Composant posé sur le corps céleste.
    /// Attire tout <see cref="Rigidbody"/> vers son centre via
    /// <see cref="Attract"/>.
    /// </summary>
    public sealed class GravityAttractor : MonoBehaviour
    {
        [Tooltip("Accélération gravitationnelle en m/s². " +
                 "13.54 = équivalent continu de la gravité Minecraft " +
                 "(hauteur de saut 1.252 bloc, apex à 0.43 s).")]
        [SerializeField] private float gravityForce = 13.54f;

        /// <summary>Accélération gravitationnelle (lecture publique).</summary>
        public float GravityForce => gravityForce;

        /// <summary>
        /// Applique une force d'attraction vers le centre de cet objet
        /// sur le <see cref="Rigidbody"/> fourni.
        /// À appeler depuis <see cref="GravityBody.FixedUpdate"/> ou
        /// un Job System équivalent.
        /// </summary>
        /// <param name="body">Le corps à attirer.</param>
        public void Attract(Rigidbody body)
        {
            // Direction normalisée du corps vers le centre de l'attracteur.
            Vector3 gravityDirection = (transform.position - body.position).normalized;

            // Force = masse × accélération (F = m·g).
            // On utilise ForceMode.Acceleration pour s'affranchir de la masse
            // et obtenir le même comportement quel que soit le Rigidbody.
            body.AddForce(gravityDirection * gravityForce, ForceMode.Acceleration);
        }
    }
}

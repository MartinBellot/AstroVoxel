// ============================================================
//  IVoxelWorld.cs
//  Interface commune aux mondes voxel (planète, astéroïde…).
//  Permet à ChunkRenderer et BlockInteraction de fonctionner
//  avec n'importe quel type de monde sans couplage direct.
// ============================================================

using UnityEngine;

namespace AstroVoxel.VoxelEngine
{
    /// <summary>
    /// Contrat minimal d'un monde voxel sphérique.
    /// Implémenté par <see cref="PlanetWorld"/> et
    /// <see cref="AstroVoxel.Space.AsteroidWorld"/>.
    /// </summary>
    public interface IVoxelWorld
    {
        /// <summary>Retourne le ChunkRenderer qui contient worldPos, ou null.</summary>
        ChunkRenderer GetChunkAt(Vector3 worldPos);

        /// <summary>
        /// Convertit une position world en coordonnées locales dans son chunk canonique.
        /// Retourne toujours [0, ChunkWidth-1]³.
        /// </summary>
        Vector3Int WorldToLocalBlock(Vector3 worldPos);

        /// <summary>Détruit le bloc solide à worldPos. Retourne true si réussi.</summary>
        bool BreakBlock(Vector3 worldPos);

        /// <summary>Place un bloc de type type à worldPos. Retourne true si réussi.</summary>
        bool PlaceBlock(Vector3 worldPos, BlockType type);

        /// <summary>
        /// Centre du monde en world-space. Dynamique : suit le déplacement de l'objet
        /// (toujours (0,0,0) pour la planète, position orbitale courante pour un astéroïde).
        /// Utilisé par ChunkRenderer.RebuildMesh pour l'orientation radiale des blocs.
        /// </summary>
        Vector3 WorldCenter { get; }
    }
}

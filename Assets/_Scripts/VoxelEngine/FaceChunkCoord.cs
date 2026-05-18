// ============================================================
//  FaceChunkCoord.cs
//  Identifiant unique d'un chunk dans le système 18-Face Cube-Sphère
//  du cube-sphere planétaire.
//
//  Chaque face possède sa propre grille 3D :
//    • U  = position selon Rights[Face]   (axe tangentiel droit)
//    • V  = position selon Forwards[Face] (axe tangentiel avant)
//    • R  = position selon Normals[Face]  (axe radial sortant)
//
//  Les coordonnées U, V, R sont en unités de chunk (ChunkWidth).
// ============================================================

using System;

namespace AstroVoxel.VoxelEngine
{
    /// <summary>
    /// Coordonnée d'un chunk dans la grille d'une face du cube-sphere.
    /// </summary>
    public readonly struct FaceChunkCoord : IEquatable<FaceChunkCoord>
    {
        public readonly FaceIndex Face;
        public readonly int       U;   // index selon Rights[Face]
        public readonly int       V;   // index selon Forwards[Face]
        public readonly int       R;   // index radial selon Normals[Face]

        public FaceChunkCoord(FaceIndex face, int u, int v, int r)
        {
            Face = face;
            U    = u;
            V    = v;
            R    = r;
        }

        // ── IEquatable ────────────────────────────────────────

        public bool Equals(FaceChunkCoord other) =>
            Face == other.Face && U == other.U && V == other.V && R == other.R;

        public override bool Equals(object obj) =>
            obj is FaceChunkCoord other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Face;
                h = h * 31 + U;
                h = h * 31 + V;
                h = h * 31 + R;
                return h;
            }
        }

        public override string ToString() =>
            $"FaceChunkCoord({Face}, U={U}, V={V}, R={R})";
    }
}

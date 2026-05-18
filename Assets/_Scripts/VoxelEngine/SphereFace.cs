// ============================================================
//  SphereFace.cs
//  Définit les 18 faces du Cube-Sphère Octaédrique utilisées
//  pour orienter les chunks autour d'une planète sphérique.
//
//  18 faces = 6 faces axiales (±X, ±Y, ±Z)
//           + 12 faces diagonales (arêtes du cube : plans XY, XZ, YZ)
//
//  Avec 18 faces l'écart max entre normale de face et direction
//  radiale réelle est de 22.5° (contre 45° avec 6 faces).
//  Le sol paraît parfaitement plat aux 6 points cardinaux ET
//  aux 12 directions diagonales à 45°.
//
//  Convention par face :
//    Normals[f]  = direction radiale sortante (local +Y du chunk)
//    Rights[f]   = direction tangentielle droite (local +X)
//    Forwards[f] = direction tangentielle avant (local +Z)
//    Rights[f]   = Normals[f] × Forwards[f]  (produit vectoriel)
// ============================================================

using UnityEngine;

namespace AstroVoxel.VoxelEngine
{
    /// <summary>Index des 18 faces du cube-sphère octaédrique.</summary>
    public enum FaceIndex : byte
    {
        // 6 faces axiales
        PosY =  0,  // Pôle nord     (+Y)
        NegY =  1,  // Pôle sud      (-Y)
        PosX =  2,  // Est           (+X)
        NegX =  3,  // Ouest         (-X)
        PosZ =  4,  // Avant         (+Z)
        NegZ =  5,  // Arrière       (-Z)
        // 12 faces diagonales — plan XY
        XY_PP =  6, // (+X+Y) : n=(1, 1,0)/√2
        XY_PN =  7, // (+X-Y) : n=(1,-1,0)/√2
        XY_NP =  8, // (-X+Y) : n=(-1,1,0)/√2
        XY_NN =  9, // (-X-Y) : n=(-1,-1,0)/√2
        // 12 faces diagonales — plan XZ
        XZ_PP = 10, // (+X+Z) : n=(1,0, 1)/√2
        XZ_PN = 11, // (+X-Z) : n=(1,0,-1)/√2
        XZ_NP = 12, // (-X+Z) : n=(-1,0,1)/√2
        XZ_NN = 13, // (-X-Z) : n=(-1,0,-1)/√2
        // 12 faces diagonales — plan YZ
        YZ_PP = 14, // (+Y+Z) : n=(0, 1, 1)/√2
        YZ_PN = 15, // (+Y-Z) : n=(0, 1,-1)/√2
        YZ_NP = 16, // (-Y+Z) : n=(0,-1, 1)/√2
        YZ_NN = 17, // (-Y-Z) : n=(0,-1,-1)/√2
    }

    public static class SphereFace
    {
        public const int FaceCount = 18;

        private const float D = 0.7071068f; // 1/√2

        // ── Normales : direction radiale sortante (local +Y) ──
        public static readonly Vector3[] Normals = new Vector3[FaceCount]
        {
            // faces axiales
            Vector3.up,                  //  0 PosY
            Vector3.down,                //  1 NegY
            Vector3.right,               //  2 PosX
            Vector3.left,                //  3 NegX
            Vector3.forward,             //  4 PosZ
            Vector3.back,                //  5 NegZ
            // diagonales XY  (forward = +Z)
            new Vector3( D,  D, 0f),     //  6 XY_PP
            new Vector3( D, -D, 0f),     //  7 XY_PN
            new Vector3(-D,  D, 0f),     //  8 XY_NP
            new Vector3(-D, -D, 0f),     //  9 XY_NN
            // diagonales XZ  (forward = +Y)
            new Vector3( D, 0f,  D),     // 10 XZ_PP
            new Vector3( D, 0f, -D),     // 11 XZ_PN
            new Vector3(-D, 0f,  D),     // 12 XZ_NP
            new Vector3(-D, 0f, -D),     // 13 XZ_NN
            // diagonales YZ  (forward = +X)
            new Vector3(0f,  D,  D),     // 14 YZ_PP
            new Vector3(0f,  D, -D),     // 15 YZ_PN
            new Vector3(0f, -D,  D),     // 16 YZ_NP
            new Vector3(0f, -D, -D),     // 17 YZ_NN
        };

        // ── Forwards : direction locale +Z ─────────────────────
        // Règle de choix : pour les faces diagonales du plan PQ,
        // on prend comme Forward le 3e axe mondial (perpendiculaire au plan).
        public static readonly Vector3[] Forwards = new Vector3[FaceCount]
        {
            // axiales (inchangé)
            Vector3.forward,  //  0 PosY
            Vector3.back,     //  1 NegY
            Vector3.up,       //  2 PosX
            Vector3.up,       //  3 NegX
            Vector3.up,       //  4 PosZ
            Vector3.up,       //  5 NegZ
            // XY diagonales → forward = +Z
            Vector3.forward,  //  6 XY_PP
            Vector3.forward,  //  7 XY_PN
            Vector3.forward,  //  8 XY_NP
            Vector3.forward,  //  9 XY_NN
            // XZ diagonales → forward = +Y
            Vector3.up,       // 10 XZ_PP
            Vector3.up,       // 11 XZ_PN
            Vector3.up,       // 12 XZ_NP
            Vector3.up,       // 13 XZ_NN
            // YZ diagonales → forward = +X
            Vector3.right,    // 14 YZ_PP
            Vector3.right,    // 15 YZ_PN
            Vector3.right,    // 16 YZ_NP
            Vector3.right,    // 17 YZ_NN
        };

        // ── Rights : direction locale +X = Normal × Forward ───
        public static readonly Vector3[] Rights = new Vector3[FaceCount]
        {
            // axiales (inchangé)
            Vector3.right,              //  0 PosY  : (0,1,0)×(0,0,1)   = (+1,0,0)
            Vector3.right,              //  1 NegY  : (0,-1,0)×(0,0,-1) = (+1,0,0)
            Vector3.forward,            //  2 PosX  : (1,0,0)×(0,1,0)   = (0,0,+1)
            Vector3.back,               //  3 NegX  : (-1,0,0)×(0,1,0)  = (0,0,-1)
            Vector3.left,               //  4 PosZ  : (0,0,1)×(0,1,0)   = (-1,0,0)
            Vector3.right,              //  5 NegZ  : (0,0,-1)×(0,1,0)  = (+1,0,0)
            // XY diagonales × (0,0,1)
            new Vector3( D, -D, 0f),    //  6 XY_PP : ( D, D,0)×(0,0,1) = ( D,-D,0)
            new Vector3(-D, -D, 0f),    //  7 XY_PN : ( D,-D,0)×(0,0,1) = (-D,-D,0)
            new Vector3( D,  D, 0f),    //  8 XY_NP : (-D, D,0)×(0,0,1) = ( D, D,0)
            new Vector3(-D,  D, 0f),    //  9 XY_NN : (-D,-D,0)×(0,0,1) = (-D, D,0)
            // XZ diagonales × (0,1,0)
            new Vector3(-D, 0f,  D),    // 10 XZ_PP : ( D,0, D)×(0,1,0) = (-D,0, D)
            new Vector3( D, 0f,  D),    // 11 XZ_PN : ( D,0,-D)×(0,1,0) = ( D,0, D)
            new Vector3(-D, 0f, -D),    // 12 XZ_NP : (-D,0, D)×(0,1,0) = (-D,0,-D)
            new Vector3( D, 0f, -D),    // 13 XZ_NN : (-D,0,-D)×(0,1,0) = ( D,0,-D)
            // YZ diagonales × (1,0,0)
            new Vector3(0f,  D, -D),    // 14 YZ_PP : (0, D, D)×(1,0,0) = (0, D,-D)
            new Vector3(0f, -D, -D),    // 15 YZ_PN : (0, D,-D)×(1,0,0) = (0,-D,-D)
            new Vector3(0f,  D,  D),    // 16 YZ_NP : (0,-D, D)×(1,0,0) = (0, D, D)
            new Vector3(0f, -D,  D),    // 17 YZ_NN : (0,-D,-D)×(1,0,0) = (0,-D, D)
        };

        // ── Rotations pré-calculées ───────────────────────────
        private static readonly Quaternion[] _rotations;

        static SphereFace()
        {
            _rotations = new Quaternion[FaceCount];
            for (int i = 0; i < FaceCount; i++)
                _rotations[i] = Quaternion.LookRotation(Forwards[i], Normals[i]);
        }

        public static Quaternion GetRotation(FaceIndex face) => _rotations[(int)face];
        public static Quaternion GetRotation(int face)      => _rotations[face];

        /// <summary>
        /// Retourne la face dont la normale est la plus alignée avec
        /// <paramref name="relPos"/> (position relative au centre planète).
        /// Comparaison dot-product sur les 18 normales → O(18).
        /// </summary>
        public static FaceIndex GetFace(Vector3 relPos)
        {
            Vector3 dir = relPos.normalized;
            float bestDot = float.MinValue;
            FaceIndex best = FaceIndex.PosY;
            for (int i = 0; i < FaceCount; i++)
            {
                float d = Vector3.Dot(dir, Normals[i]);
                if (d > bestDot) { bestDot = d; best = (FaceIndex)i; }
            }
            return best;
        }
    }
}

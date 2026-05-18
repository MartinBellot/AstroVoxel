// ============================================================
//  ChunkMeshBuilder.cs
//  Génère le mesh d'un Chunk avec Face Culling strict.
//  Pas de dépendance à MonoBehaviour — appelable depuis un thread.
//  Retourne un MeshData (struct) que le thread principal applique.
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstroVoxel.VoxelEngine
{
    /// <summary>
    /// Contient les listes brutes du mesh prêtes à être appliquées
    /// à un <see cref="Mesh"/> Unity. Séparé de ChunkMeshBuilder pour
    /// permettre le transfert inter-thread (pas de ref UnityEngine dans
    /// le corps du calcul).
    /// </summary>
    public sealed class MeshData
    {
        public readonly List<Vector3> Vertices = new List<Vector3>(4096);
        /// <summary>
        /// 256 sous-tableaux de triangles, indexés par renderingId (byte).
        /// renderingId = (byte)BlockType pour les blocs simples,
        /// ou un ID de face-variant (200-216) pour les blocs multi-faces.
        /// Permet d'avoir un submesh par matériau/face sans post-traitement.
        /// </summary>
        public readonly List<int>[] Triangles = new List<int>[256];
        public readonly List<Vector2> UVs = new List<Vector2>(4096);

        public MeshData()
        {
            for (int i = 0; i < 256; i++)
                Triangles[i] = new List<int>();
        }

        public void Clear()
        {
            Vertices.Clear();
            for (int i = 0; i < 256; i++) Triangles[i].Clear();
            UVs.Clear();
        }

        public bool IsEmpty => Vertices.Count == 0;
    }

    /// <summary>
    /// Génère le <see cref="MeshData"/> d'un <see cref="ChunkData"/> en
    /// appliquant un Face Culling strict :
    /// une face n'est émise que si le voisin adjacent est de l'Air (ou
    /// hors des limites du Chunk).
    /// </summary>
    public static class ChunkMeshBuilder
    {
        // ── Point d'entrée public ─────────────────────────────

        /// <summary>
        /// Remplit <paramref name="output"/> avec la géométrie visible du Chunk.
        /// Appeler <see cref="MeshData.Clear"/> avant si réutilisation.
        /// </summary>
        /// <param name="getNeighbour">
        /// Délégué appelé pour les voxels hors-chunk (coordonnées locales OOB).
        /// Doit retourner le type de bloc réel du chunk voisin chargé,
        /// ou le type généré si ce chunk n'est pas encore chargé.
        /// </param>
        public static void Build(ChunkData data, MeshData output,
                                 Func<int, int, int, byte> getNeighbour)
        {
            Build(data, output, getNeighbour, useRadialOrientation: false,
                  Quaternion.identity, Vector3.zero, Vector3.zero);
        }

        /// <summary>
        /// Variante qui oriente les blocs multi-faces (Grass, Logs, Sandstone…)
        /// en fonction de la direction radiale réelle de CHAQUE bloc par rapport
        /// au centre de la planète. Le « top » du grass pointera ainsi toujours
        /// vers l'extérieur de la planète, même quand le chunk est sur une face
        /// diagonale ou que le bloc se trouve à un coin de chunk où la direction
        /// radiale s'écarte du +Y local du chunk.
        /// </summary>
        public static void Build(ChunkData data, MeshData output,
                                 Func<int, int, int, byte> getNeighbour,
                                 bool useRadialOrientation,
                                 Quaternion chunkRotation,
                                 Vector3 chunkOriginWorld,
                                 Vector3 planetCenter)
        {
            int width  = data.Width;
            int height = data.Height;

            // Inverse de la rotation du chunk : permet de convertir une direction
            // world (radial sortant) vers l'espace local du chunk pour comparer
            // avec les normales des 6 faces (qui sont en local).
            Quaternion invRot = useRadialOrientation
                ? Quaternion.Inverse(chunkRotation)
                : Quaternion.identity;

            for (int z = 0; z < width;  z++)
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width;  x++)
            {
                byte blockId = data.GetBlock(x, y, z);
                if (!BlockProperties.IsSolid(blockId)) continue;

                // ── Détermine la face « top » et « bottom » LOGIQUES ──
                // Logique = par rapport au centre de la planète, pas par
                // rapport au +Y local du chunk.
                // Par défaut (mode plat) : top=0 (+Y), bottom=1 (-Y).
                int topFace    = 0;
                int bottomFace = 1;
                Vector3 localUp = new Vector3(0f, 1f, 0f);
                if (useRadialOrientation)
                {
                    Vector3 worldCenter = chunkOriginWorld
                        + chunkRotation * new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                    Vector3 radial = worldCenter - planetCenter;
                    if (radial.sqrMagnitude > 1e-6f)
                    {
                        localUp = invRot * radial.normalized;
                        float bestTop    = float.MinValue;
                        float bestBottom = float.MaxValue;
                        for (int f = 0; f < 6; f++)
                        {
                            float d = localUp.x * VoxelData.FaceChecks[f, 0]
                                    + localUp.y * VoxelData.FaceChecks[f, 1]
                                    + localUp.z * VoxelData.FaceChecks[f, 2];
                            if (d > bestTop)    { bestTop = d;    topFace    = f; }
                            if (d < bestBottom) { bestBottom = d; bottomFace = f; }
                        }
                    }
                }

                // Teste les 6 faces
                for (int face = 0; face < 6; face++)
                {
                    int nx = x + VoxelData.FaceChecks[face, 0];
                    int ny = y + VoxelData.FaceChecks[face, 1];
                    int nz = z + VoxelData.FaceChecks[face, 2];

                    byte neighbour;
                    if (data.IsInBounds(nx, ny, nz))
                    {
                        // Voisin dans le même chunk → lecture directe (données réelles)
                        neighbour = data.GetBlock(nx, ny, nz);
                    }
                    else
                    {
                        // Voisin hors-chunk → délégué : interroge le chunk voisin chargé
                        // (données réelles incluant les modifications du joueur).
                        neighbour = getNeighbour(nx, ny, nz);
                    }

                    if (BlockProperties.IsSolid(neighbour)) continue;

                    // Map face géométrique → face logique (top/bottom/côté) :
                    //   - face == topFace    → 0 (Top)
                    //   - face == bottomFace → 1 (Bottom)
                    //   - sinon              → 2 (côté, mais on garde face d'origine
                    //     pour les blocs dont la texture varie selon le côté précis
                    //     — non applicable aux blocs actuels qui partagent toutes
                    //     les faces latérales).
                    int logicalFace =
                        face == topFace    ? 0 :
                        face == bottomFace ? 1 :
                        face;   // côté

                    AddFace(output, x, y, z, face, logicalFace, blockId,
                            useRadialOrientation, localUp);
                }
            }
        }

        // ── Génération d'une face ─────────────────────────────

        private static void AddFace(MeshData output, int x, int y, int z,
                                    int geomFace, int logicalFace, byte blockId,
                                    bool useRadialOrientation, Vector3 localUp)
        {
            // Détermine l'ID de sous-mesh (renderingId) pour cette combinaison bloc+face.
            // Pour les blocs simples, renderingId == blockId.
            // Pour les blocs multi-faces (Grass, Logs, Sandstone…), un ID variant (200+) est retourné.
            // On passe la face LOGIQUE (orientée par rapport au centre planète) plutôt
            // que la face géométrique locale du chunk.
            byte renderingId = BlockFaceData.GetRenderingId(blockId, logicalFace);
            int face = geomFace;   // alias pour le reste du code qui utilise face pour la géométrie

            int vertexBase = output.Vertices.Count;

            // 4 vertices de la face — on garde les offsets locaux pour calculer
            // ensuite des UVs orientées vers le « haut » radial.
            Vector3 v0 = default, v1 = default, v2 = default, v3 = default;
            for (int i = 0; i < 4; i++)
            {
                int vi = VoxelData.VoxelTris[face, i];
                var local = new Vector3(
                    VoxelData.VoxelVerts[vi, 0],
                    VoxelData.VoxelVerts[vi, 1],
                    VoxelData.VoxelVerts[vi, 2]
                );
                if      (i == 0) v0 = local;
                else if (i == 1) v1 = local;
                else if (i == 2) v2 = local;
                else             v3 = local;

                output.Vertices.Add(new Vector3(x + local.x, y + local.y, z + local.z));
            }

            // ── Calcul des UVs ────────────────────────────────────
            // En mode radial : V=1 doit toujours pointer vers l'extérieur de la
            // planète (direction radiale sortante projetée sur le plan de la face).
            // Cela garantit que le brin d'herbe de grass_block_side, la transition
            // sandstone_top/bottom, etc., apparaissent toujours sur le bord supérieur
            // de la face, peu importe l'orientation locale du chunk.
            //
            // Pour les faces logiques « top » (0) et « bottom » (1), le vecteur
            // vertical est colinéaire à la normale → on garde un layout UV simple.
            bool needOrientedUVs = useRadialOrientation && logicalFace >= 2;
            if (needOrientedUVs)
            {
                Vector3 N = new Vector3(
                    VoxelData.FaceChecks[face, 0],
                    VoxelData.FaceChecks[face, 1],
                    VoxelData.FaceChecks[face, 2]);
                // Projection de localUp sur le plan de la face
                Vector3 vert = localUp - Vector3.Dot(localUp, N) * N;
                if (vert.sqrMagnitude < 1e-6f)
                {
                    // Cas dégénéré (ne devrait pas survenir pour une face logique
                    // de côté, mais on protège) : UVs par défaut.
                    output.UVs.Add(new Vector2(0f, 0f));
                    output.UVs.Add(new Vector2(0f, 1f));
                    output.UVs.Add(new Vector2(1f, 0f));
                    output.UVs.Add(new Vector2(1f, 1f));
                }
                else
                {
                    vert.Normalize();
                    Vector3 horiz = Vector3.Cross(N, vert);   // tangentielle gauche/droite
                    Vector3 center = new Vector3(0.5f, 0.5f, 0.5f);
                    AppendOrientedUV(output, v0, center, horiz, vert);
                    AppendOrientedUV(output, v1, center, horiz, vert);
                    AppendOrientedUV(output, v2, center, horiz, vert);
                    AppendOrientedUV(output, v3, center, horiz, vert);
                }
            }
            else
            {
                // Mode plat ou face top/bottom logique : UVs simples.
                output.UVs.Add(new Vector2(0f, 0f));
                output.UVs.Add(new Vector2(0f, 1f));
                output.UVs.Add(new Vector2(1f, 0f));
                output.UVs.Add(new Vector2(1f, 1f));
            }

            // 2 triangles (quad) → sous-mesh du renderingId
            var tris = output.Triangles[renderingId];
            tris.Add(vertexBase + 0);
            tris.Add(vertexBase + 1);
            tris.Add(vertexBase + 2);
            tris.Add(vertexBase + 2);
            tris.Add(vertexBase + 1);
            tris.Add(vertexBase + 3);
        }

        // Calcule une UV (u,v) ∈ {0,1}² selon le signe des projections
        // de l'offset du vertex sur les axes horizontal/vertical de la face.
        private static void AppendOrientedUV(MeshData output, Vector3 vert,
                                             Vector3 center, Vector3 horiz, Vector3 vertical)
        {
            Vector3 off = vert - center;
            float u = Vector3.Dot(off, horiz)    > 0f ? 1f : 0f;
            float v = Vector3.Dot(off, vertical) > 0f ? 1f : 0f;
            output.UVs.Add(new Vector2(u, v));
        }
    }
}

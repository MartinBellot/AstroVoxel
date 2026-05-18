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
            int width  = data.Width;
            int height = data.Height;

            for (int z = 0; z < width;  z++)
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width;  x++)
            {
                byte blockId = data.GetBlock(x, y, z);
                if (!BlockProperties.IsSolid(blockId)) continue;

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

                    AddFace(output, x, y, z, face, blockId);
                }
            }
        }

        // ── Génération d'une face ─────────────────────────────

        private static void AddFace(MeshData output, int x, int y, int z, int face, byte blockId)
        {
            // Détermine l'ID de sous-mesh (renderingId) pour cette combinaison bloc+face.
            // Pour les blocs simples, renderingId == blockId.
            // Pour les blocs multi-faces (Grass, Logs, Sandstone…), un ID variant (200+) est retourné.
            byte renderingId = BlockFaceData.GetRenderingId(blockId, face);

            int vertexBase = output.Vertices.Count;

            // 4 vertices de la face
            for (int i = 0; i < 4; i++)
            {
                int vi = VoxelData.VoxelTris[face, i];
                output.Vertices.Add(new Vector3(
                    x + VoxelData.VoxelVerts[vi, 0],
                    y + VoxelData.VoxelVerts[vi, 1],
                    z + VoxelData.VoxelVerts[vi, 2]
                ));
            }

            // UVs simples [0,1] — chaque renderingId possède son propre material,
            // donc pas d'atlas : on échantillonne toute la texture.
            output.UVs.Add(new Vector2(0f, 0f));
            output.UVs.Add(new Vector2(0f, 1f));
            output.UVs.Add(new Vector2(1f, 0f));
            output.UVs.Add(new Vector2(1f, 1f));

            // 2 triangles (quad) → sous-mesh du renderingId
            var tris = output.Triangles[renderingId];
            tris.Add(vertexBase + 0);
            tris.Add(vertexBase + 1);
            tris.Add(vertexBase + 2);
            tris.Add(vertexBase + 2);
            tris.Add(vertexBase + 1);
            tris.Add(vertexBase + 3);
        }
    }
}

// ============================================================
//  ChunkMeshBuilder.cs
//  Génère le mesh d'un Chunk avec Face Culling strict.
//  Pas de dépendance à MonoBehaviour — appelable depuis un thread.
//  Retourne un MeshData (struct) que le thread principal applique.
// ============================================================

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
        /// Un sous-tableau de triangles par type de bloc (index = (byte)BlockType).
        /// Permet d'avoir un submesh par matériau sans post-traitement.
        /// </summary>
        public readonly List<int>[] Triangles;
        public readonly List<Vector2> UVs = new List<Vector2>(4096);

        public MeshData()
        {
            int count = System.Enum.GetValues(typeof(BlockType)).Length;
            Triangles = new List<int>[count];
            for (int i = 0; i < count; i++)
                Triangles[i] = new List<int>(512);
        }

        public void Clear()
        {
            Vertices.Clear();
            foreach (var t in Triangles) t.Clear();
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
        /// <param name="chunkOrigin">Coin (0,0,0) du chunk en world-space. Nécessaire pour
        /// le culling inter-chunks : les voisins OOB sont interrogés via le générateur.</param>
        /// <param name="planetCenter">Centre de la planète. Transmis au générateur pour OOB.</param>
        public static void Build(ChunkData data, MeshData output,
                                 Vector3 chunkOrigin, Vector3 planetCenter)
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
                        // Voisin dans le même chunk
                        neighbour = data.GetBlock(nx, ny, nz);
                    }
                    else
                    {
                        // Voisin hors chunk : interroge le générateur directement.
                        // Résultat identique à ce qu'aurait généré le chunk voisin.
                        // → Culling inter-chunks correct sans coordination runtime.
                        Vector3 worldPos = chunkOrigin
                            + new Vector3(nx + 0.5f, ny + 0.5f, nz + 0.5f);
                        neighbour = PlanetChunkGenerator.GetBlockType(worldPos, planetCenter);
                    }

                    if (BlockProperties.IsSolid(neighbour)) continue;

                    AddFace(output, x, y, z, face, blockId);
                }
            }
        }

        // ── Génération d'une face ─────────────────────────────

        private static void AddFace(MeshData output, int x, int y, int z, int face, byte blockId)
        {
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

            // UVs (récupération depuis l'atlas)
            Vector2 tileOffset = GetTextureOffset(blockId, face);
            float ts = VoxelData.NormalizedBlockTextureSize;

            output.UVs.Add(tileOffset + new Vector2(0,  0));
            output.UVs.Add(tileOffset + new Vector2(0,  ts));
            output.UVs.Add(tileOffset + new Vector2(ts, 0));
            output.UVs.Add(tileOffset + new Vector2(ts, ts));

            // 2 triangles (quad) — route vers le submesh du bon type de bloc
            var tris = output.Triangles[blockId];
            tris.Add(vertexBase + 0);
            tris.Add(vertexBase + 1);
            tris.Add(vertexBase + 2);
            tris.Add(vertexBase + 2);
            tris.Add(vertexBase + 1);
            tris.Add(vertexBase + 3);
        }

        // ── Mappage bloc → tuile d'atlas ─────────────────────

        /// <summary>
        /// Retourne le coin bas-gauche (UV normalisé) de la tuile de texture
        /// pour un bloc et une face donnés.
        /// Étendre ici pour des textures différentes par face (ex: Grass top ≠ côtés).
        /// </summary>
        private static Vector2 GetTextureOffset(byte blockId, int face)
        {
            int tileX, tileY;

            // Convention : tuile (col=0, row=0) = coin bas-gauche de l'atlas.
            switch ((BlockType)blockId)
            {
                case BlockType.Grass:
                    if (face == 0)        { tileX = 0; tileY = 3; } // Top : herbe
                    else if (face == 1)   { tileX = 2; tileY = 0; } // Bottom : terre
                    else                  { tileX = 1; tileY = 3; } // Côtés : herbe-dirt
                    break;
                case BlockType.Dirt:
                    tileX = 2; tileY = 0;
                    break;
                case BlockType.Stone:
                    tileX = 0; tileY = 0;
                    break;
                case BlockType.Sand:
                    tileX = 2; tileY = 1;
                    break;
                case BlockType.Wood:
                    tileX = face == 0 || face == 1 ? 2 : 1; tileY = 1;
                    break;
                case BlockType.Leaves:
                    tileX = 1; tileY = 0;
                    break;
                default:
                    tileX = 0; tileY = 0;
                    break;
            }

            float ts = VoxelData.NormalizedBlockTextureSize;
            return new Vector2(tileX * ts, tileY * ts);
        }
    }
}

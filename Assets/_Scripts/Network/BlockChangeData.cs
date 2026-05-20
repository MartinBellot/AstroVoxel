// ============================================================
//  BlockChangeData.cs
//  Struct compacte (20 bytes) pour la synchronisation réseau
//  des modifications de blocs. Unmanaged → compatible
//  FastBufferWriter.WriteValueSafe<T>.
// ============================================================

using AstroVoxel.VoxelEngine;
using Unity.Netcode;

namespace AstroVoxel.Network
{
    /// <summary>
    /// Représente un seul changement de bloc (casse ou pose) à transmettre
    /// sur le réseau. 20 octets par changement.
    /// </summary>
    public struct BlockChangeData
    {
        public ulong senderId;   // 8 bytes — client à l'origine (ulong.MaxValue = server)
        public byte  face;       // 1 byte  — FaceIndex (0-17)
        public short chunkU;     // 2 bytes — FaceChunkCoord.U
        public short chunkV;     // 2 bytes — FaceChunkCoord.V
        public short chunkR;     // 2 bytes — FaceChunkCoord.R
        public byte  lx;         // 1 byte  — coord locale X [0-15]
        public byte  ly;         // 1 byte  — coord locale Y [0-15]
        public byte  lz;         // 1 byte  — coord locale Z [0-15]
        public byte  blockType;  // 1 byte  — BlockType (0 = Air = casse)
        public byte  isBreak;    // 1 byte  — 1 = casse, 0 = pose
        // Total = 20 bytes, fully unmanaged ✓

        public FaceChunkCoord ToCoord() =>
            new FaceChunkCoord((FaceIndex)face, chunkU, chunkV, chunkR);

        // ── Helpers de sérialisation ──────────────────────────
        // Évite l'ambiguïté entre les surcharges WriteValueSafe<T>
        // (ForStructs vs ForNetworkSerializable) en écrivant chaque
        // champ primitif individuellement via les overloads explicites.

        public static void Write(ref FastBufferWriter w, in BlockChangeData d)
        {
            w.WriteValueSafe(d.senderId);
            w.WriteValueSafe(d.face);
            w.WriteValueSafe(d.chunkU);
            w.WriteValueSafe(d.chunkV);
            w.WriteValueSafe(d.chunkR);
            w.WriteValueSafe(d.lx);
            w.WriteValueSafe(d.ly);
            w.WriteValueSafe(d.lz);
            w.WriteValueSafe(d.blockType);
            w.WriteValueSafe(d.isBreak);
        }

        public static void Read(ref FastBufferReader r, out BlockChangeData d)
        {
            d = default;
            r.ReadValueSafe(out d.senderId);
            r.ReadValueSafe(out d.face);
            r.ReadValueSafe(out d.chunkU);
            r.ReadValueSafe(out d.chunkV);
            r.ReadValueSafe(out d.chunkR);
            r.ReadValueSafe(out d.lx);
            r.ReadValueSafe(out d.ly);
            r.ReadValueSafe(out d.lz);
            r.ReadValueSafe(out d.blockType);
            r.ReadValueSafe(out d.isBreak);
        }
    }
}

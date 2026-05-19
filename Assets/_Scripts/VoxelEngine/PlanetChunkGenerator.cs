// ============================================================
//  PlanetChunkGenerator.cs
//  Remplit un ChunkData cubique (16x16x16) selon sa position
//  dans la grille 3D de la planète.
//  Aucune dépendance MonoBehaviour → appellable hors thread.
// ============================================================

using UnityEngine;

namespace AstroVoxel.VoxelEngine
{
    public static class PlanetChunkGenerator
    {
        // ── Paramètres de génération ──────────────────────────

        /// <summary>Rayon de la surface de la planète en blocs.</summary>
        public const float PlanetCoreRadius = 50f;

        /// <summary>Épaisseur de la croûte solide sous la surface.</summary>
        public const int CrustThickness = 12;

        /// <summary>Amplitude max du bruit de surface (±blocs).</summary>
        public const float SurfaceAmplitude = 3f;

        /// <summary>Fréquence spatiale du bruit de terrain.</summary>
        public const float SurfaceFrequency = 0.03f;

        // ── Paramètres des grottes ────────────────────────────

        /// <summary>Fréquence spatiale des tunnels.</summary>
        public const float CaveFrequency = 0.045f;

        /// <summary>Rayon de l'isosurface en profondeur (~5 blocs).</summary>
        public const float CaveTubeRadius = 0.110f;

        /// <summary>Rayon de l'isosurface à la surface (entrées ~3 blocs).</summary>
        public const float CaveEntryRadius = 0.068f;

        /// <summary>Profondeur sur laquelle le rayon passe de CaveEntryRadius à CaveTubeRadius.</summary>
        public const float CaveTransitionDepth = 8f;

        /// <summary>
        /// Fréquence du bruit de présence des grottes (très basse = vastes zones sans grottes).
        /// </summary>
        public const float CavePresenceFrequency = 0.012f;

        /// <summary>
        /// Seuil du bruit de présence : seules les zones où ce bruit dépasse ce seuil
        /// peuvent contenir des grottes. Plus élevé = grottes plus rares.
        /// 0.70 → ~15 % de la planète contient des grottes.
        /// </summary>
        public const float CavePresenceThreshold = 0.70f;

        // ── Méthode centrale partagée ─────────────────────────

        /// <summary>
        /// Calcule le type de bloc pour TOUTE position world.
        /// Appelée à la fois par Generate et par ChunkMeshBuilder (culling OOB).
        /// UNE SEULE implémentation → cohérence garantie, zéro désynchronisation FP.
        /// </summary>
        public static byte GetBlockType(Vector3 worldPos, Vector3 planetCenter)
        {
            float dx = worldPos.x - planetCenter.x;
            float dy = worldPos.y - planetCenter.y;
            float dz = worldPos.z - planetCenter.z;
            float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

            if (dist < 0.001f) return (byte)BlockType.Stone;

            // Normalisation manuelle — IDENTIQUE à ce que fait Vector3.normalized
            float invDist = 1f / dist;
            float ndx = dx * invDist;
            float ndy = dy * invDist;
            float ndz = dz * invDist;

            float nx = ndx * SurfaceFrequency * PlanetCoreRadius + 100f;
            float ny = ndy * SurfaceFrequency * PlanetCoreRadius + 200f;
            float nz = ndz * SurfaceFrequency * PlanetCoreRadius + 300f;
            float noise = (Mathf.PerlinNoise(nx, nz) + Mathf.PerlinNoise(ny, nx)) * 0.5f;

            float surfaceRadius = PlanetCoreRadius + SurfaceAmplitude * (noise - 0.5f);

            // Les grottes s'appliquent à TOUS les blocs solides (y compris surface)
            // pour que les tunnels débouchent naturellement en surface.
            float depth = surfaceRadius - dist;   // > 0 sous la surface
            // Les grottes ne percent pas au-delà de la croûte : on les coupe
            // dès que depth > CrustThickness + quelques blocs de marge.
            bool cave = (depth <= CrustThickness + 3f) && IsCave(worldPos, depth);

            if (dist > surfaceRadius + 0.5f) return (byte)BlockType.Air;
            if (dist > surfaceRadius - 0.5f) return cave ? (byte)BlockType.Air : (byte)BlockType.Grass;
            if (dist > surfaceRadius - 3.5f) return cave ? (byte)BlockType.Air : (byte)BlockType.Dirt;
            if (cave)                        return (byte)BlockType.Air;
            return (byte)BlockType.Stone;
        }

        // ── Bruit de grotte ───────────────────────────────────

        /// <summary>
        /// Retourne true si la position est à l'intérieur d'une galerie.
        ///
        /// Technique : intersection de paires de plans de Perlin orthogonaux.
        ///   - Plan XY ∩ Plan YZ  → tunnels orientés Z
        ///   - Plan YZ ∩ Plan XZ  → tunnels orientés X
        ///   - Plan XY ∩ Plan XZ  → tunnels orientés Y
        /// L'union des trois familles forme un réseau 3D connecté (galeries de fourmis).
        ///
        /// Le rayon rétrécit linéairement de CaveTubeRadius (profond) vers CaveEntryRadius
        /// (surface) sur CaveTransitionDepth blocs → entrées étroites, galeries larges.
        /// </summary>
        private static bool IsCave(Vector3 p, float depth)
        {
            if (depth <= 0f) return false;

            // Bruit de présence basse fréquence : délimite des zones avec/sans grottes.
            // Évite que les tunnels soient uniformément répartis sur toute la planète.
            float pf = CavePresenceFrequency;
            float presence = Mathf.PerlinNoise(p.x * pf + 50f, p.z * pf + 50f);
            if (presence < CavePresenceThreshold) return false;

            float f = CaveFrequency;

            // Trois plans orthogonaux avec décalages distincts
            float a = Mathf.PerlinNoise(p.x * f + 500f, p.y * f + 500f);  // plan XY
            float b = Mathf.PerlinNoise(p.y * f + 700f, p.z * f + 700f);  // plan YZ
            float c = Mathf.PerlinNoise(p.x * f + 300f, p.z * f + 300f);  // plan XZ

            // Rayon croissant avec la profondeur → petites entrées, larges galeries
            float t = Mathf.Clamp01(depth / CaveTransitionDepth);
            float r = Mathf.Lerp(CaveEntryRadius, CaveTubeRadius, t);

            // Chaque paire de plans forme une famille de tunnels
            bool ab = Mathf.Abs(a - 0.5f) < r && Mathf.Abs(b - 0.5f) < r;
            bool bc = Mathf.Abs(b - 0.5f) < r && Mathf.Abs(c - 0.5f) < r;
            bool ac = Mathf.Abs(a - 0.5f) < r && Mathf.Abs(c - 0.5f) < r;

            return ab || bc || ac;
        }

        // ── Remplissage d'un chunk complet ────────────────────

        /// <summary>
        /// Marge de tolérance pour le masque de face (en dot-product normalisé).
        /// Les blocs proches de la frontière entre deux faces sont générés par les
        /// DEUX face chunks adjacents pour éviter la tranchée de couture.
        /// Valeur calibrée : diff réel observé ≈ 0.005–0.04 aux frontières.
        /// Une marge de 0.05 couvre ~2.5 voxels à r=50, fermant toute tranchée.
        /// </summary>
        private const float kSeamMargin = 0.10f;

        /// <summary>
        /// Remplit <paramref name="data"/> en appelant <see cref="GetBlockType"/>
        /// pour chaque bloc, puis applique le masque de face avec tolérance.
        /// </summary>
        public static void Generate(
            ChunkData  data,
            Vector3    chunkOriginWorld,
            Vector3    planetCenter,
            Quaternion chunkRotation,
            FaceIndex  chunkFace)
        {
            int w = data.Width;
            int h = data.Height;

            for (int z = 0; z < w; z++)
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector3 blockPos = chunkOriginWorld + chunkRotation * new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                Vector3 rel      = blockPos - planetCenter;

                // Masque de face : seul le chunk canonique génère le bloc.
                // Tolérance kSeamMargin pour éviter les trous de couture :
                // les deux grilles tournées laissent un vide de ~0.003 en
                // dot-product près de la frontière → on inclut les blocs
                // dont le dot de chunkFace est dans les kSeamMargin du meilleur.
                FaceIndex blockFace = SphereFace.GetFace(rel);
                if (blockFace != chunkFace)
                {
                    Vector3 dir      = rel.normalized;
                    float   dotSelf  = Vector3.Dot(dir, SphereFace.Normals[(int)chunkFace]);
                    float   dotBest  = Vector3.Dot(dir, SphereFace.Normals[(int)blockFace]);
                    // dotBest > dotSelf par définition de GetFace.
                    // Si la différence dépasse la marge, le bloc est clairement
                    // dans un autre territoire → Air.
                    if (dotBest - dotSelf > kSeamMargin)
                    {
                        data.SetBlock(x, y, z, (byte)BlockType.Air);
                        continue;
                    }
                }

                data.SetBlock(x, y, z, GetBlockType(blockPos, planetCenter));
            }

            PlaceTrees(data, chunkOriginWorld, planetCenter, chunkRotation);
        }

        // ── Génération des arbres ─────────────────────────────

        private const int   TreeCellSize     = 8;
        private const int   MinTrunkHeight   = 3;
        private const int   MaxTrunkHeight   = 6;
        private const int   MinCanopyRadius  = 2;
        private const int   MaxCanopyRadius  = 3;
        // Probabilité d'arbre : 1 cellule sur 3
        private const int   TreeSpawnRatio   = 3;

        /// <summary>
        /// Place des arbres déterministes dans le chunk courant.
        /// On itère sur les cellules de grille voisines pouvant déborder dans ce chunk.
        /// </summary>
        private static void PlaceTrees(ChunkData data, Vector3 chunkOrigin, Vector3 planetCenter, Quaternion chunkRotation)
        {
            int S = TreeCellSize;
            int expand = MaxTrunkHeight + MaxCanopyRadius + S;

            // Bounding sphere du chunk pour calculer les cellules d'arbres à vérifier.
            // Demi-diagonale du cube : sqrt(3)/2 * cs.
            int cs = data.Width;
            Vector3 chunkCenter = chunkOrigin + chunkRotation * new Vector3(cs * 0.5f, cs * 0.5f, cs * 0.5f);
            float totalRadius = cs * 0.8660254f + expand + S;

            int gcMinX = Mathf.FloorToInt((chunkCenter.x - totalRadius) / S);
            int gcMaxX = Mathf.CeilToInt ((chunkCenter.x + totalRadius) / S);
            int gcMinY = Mathf.FloorToInt((chunkCenter.y - totalRadius) / S);
            int gcMaxY = Mathf.CeilToInt ((chunkCenter.y + totalRadius) / S);
            int gcMinZ = Mathf.FloorToInt((chunkCenter.z - totalRadius) / S);
            int gcMaxZ = Mathf.CeilToInt ((chunkCenter.z + totalRadius) / S);

            for (int gx = gcMinX; gx <= gcMaxX; gx++)
            for (int gy = gcMinY; gy <= gcMaxY; gy++)
            for (int gz = gcMinZ; gz <= gcMaxZ; gz++)
            {
                int hash = HashTreeCell(gx, gy, gz);

                // Seulement 1 cellule sur TreeSpawnRatio génère un arbre
                if ((hash % TreeSpawnRatio) != 0) continue;

                // Centre de la cellule en world
                float cx = (gx + 0.5f) * S;
                float cy = (gy + 0.5f) * S;
                float cz = (gz + 0.5f) * S;

                float distFromCenter = Mathf.Sqrt(
                    (cx - planetCenter.x) * (cx - planetCenter.x) +
                    (cy - planetCenter.y) * (cy - planetCenter.y) +
                    (cz - planetCenter.z) * (cz - planetCenter.z));

                // Seules les cellules proches de la surface peuvent porter un arbre
                float surfMin = PlanetCoreRadius - 2f;
                float surfMax = PlanetCoreRadius + SurfaceAmplitude + 6f;
                if (distFromCenter < surfMin || distFromCenter > surfMax) continue;

                // Direction radiale (vers l'extérieur de la planète)
                float ddx = cx - planetCenter.x;
                float ddy = cy - planetCenter.y;
                float ddz = cz - planetCenter.z;
                float inv = 1f / distFromCenter;
                Vector3 up = new Vector3(ddx * inv, ddy * inv, ddz * inv);

                // Trouver la surface réelle au-dessus du centre de cellule
                // On cherche le premier bloc Grass depuis distFromCenter en descente radiale
                Vector3 searchBase = FindSurfaceBlock(planetCenter, up);
                if (searchBase == Vector3.positiveInfinity) continue;

                // Vérifier que ce bloc est bien dans ou proche du chunk
                // (perf : skip si trop loin pour affecter ce chunk)
                int bx = Mathf.FloorToInt(searchBase.x);
                int by = Mathf.FloorToInt(searchBase.y);
                int bz = Mathf.FloorToInt(searchBase.z);

                // Paramètres de l'arbre depuis le hash
                int trunkH    = MinTrunkHeight + (int)((uint)hash >> 4)  % (MaxTrunkHeight  - MinTrunkHeight  + 1);
                int canopyR   = MinCanopyRadius + (int)((uint)hash >> 8)  % (MaxCanopyRadius - MinCanopyRadius + 1);

                // Tronc en bois : DDA pas fin (0.45) → tous les blocs se touchent
                // Un pas < 0.5 garantit qu'on ne rate aucun franchissement de grille
                // même sur une direction purement diagonale.
                {
                    const float dda = 0.45f;
                    int steps = Mathf.CeilToInt(trunkH / dda);
                    int prevTx = int.MinValue, prevTy = int.MinValue, prevTz = int.MinValue;
                    for (int s = 1; s <= steps; s++)
                    {
                        float t  = Mathf.Min(s * dda, (float)trunkH);
                        int   tx = Mathf.FloorToInt(searchBase.x + up.x * t);
                        int   ty = Mathf.FloorToInt(searchBase.y + up.y * t);
                        int   tz = Mathf.FloorToInt(searchBase.z + up.z * t);
                        if (tx == prevTx && ty == prevTy && tz == prevTz) continue;
                        TrySetBlock(data, chunkOrigin, tx, ty, tz, (byte)BlockType.Wood, overwrite: true, chunkRotation);
                        prevTx = tx; prevTy = ty; prevTz = tz;
                    }
                }

                // Canopy de feuilles en haut du tronc
                Vector3 canopyCenter = new Vector3(
                    searchBase.x + up.x * (trunkH + 0.5f),
                    searchBase.y + up.y * (trunkH + 0.5f),
                    searchBase.z + up.z * (trunkH + 0.5f));

                int cr = canopyR;
                for (int lx = -cr; lx <= cr; lx++)
                for (int ly = -cr; ly <= cr; ly++)
                for (int lz = -cr; lz <= cr; lz++)
                {
                    if (lx * lx + ly * ly + lz * lz > cr * cr) continue;
                    int wx = Mathf.FloorToInt(canopyCenter.x) + lx;
                    int wy = Mathf.FloorToInt(canopyCenter.y) + ly;
                    int wz = Mathf.FloorToInt(canopyCenter.z) + lz;
                    TrySetBlock(data, chunkOrigin, wx, wy, wz, (byte)BlockType.Leaves, overwrite: false, chunkRotation);
                }
            }
        }

        /// <summary>
        /// Cherche le premier bloc Grass/Dirt/Stone sur la surface de la planète
        /// en descendant depuis l'extérieur dans la direction <paramref name="up"/>.
        /// Retourne la position world du centre de ce bloc, ou +infinity si non trouvé.
        /// </summary>
        private static Vector3 FindSurfaceBlock(Vector3 planetCenter, Vector3 up)
        {
            float maxR = PlanetCoreRadius + SurfaceAmplitude + 4f;
            float minR = PlanetCoreRadius - 2f;

            // Recherche grossière : on part de maxR et descend par pas de 1
            for (float r = maxR; r >= minR; r -= 1f)
            {
                Vector3 worldPos = planetCenter + up * r;
                byte bt = GetBlockType(worldPos, planetCenter);
                if (bt == (byte)BlockType.Grass)
                    return worldPos;
            }
            return Vector3.positiveInfinity;
        }

        /// <summary>
        /// Tente de placer un bloc en coordonnées world dans ce chunk.
        /// Si <paramref name="overwrite"/> est false, ne remplace pas Air uniquement.
        /// </summary>
        private static void TrySetBlock(
            ChunkData data, Vector3 chunkOrigin,
            int wx, int wy, int wz,
            byte blockId, bool overwrite,
            Quaternion chunkRotation)
        {
            // Convertit les coordonnées world entières en espace local du chunk orienté.
            // On utilise le centre du bloc (wx+0.5, wy+0.5, wz+0.5) pour éviter les
            // erreurs de bord quand chunkOrigin n'est pas entier.
            Quaternion invRot = Quaternion.Inverse(chunkRotation);
            Vector3 localPos  = invRot * (new Vector3(wx + 0.5f, wy + 0.5f, wz + 0.5f) - chunkOrigin);
            int lx = Mathf.FloorToInt(localPos.x);
            int ly = Mathf.FloorToInt(localPos.y);
            int lz = Mathf.FloorToInt(localPos.z);

            if (lx < 0 || lx >= data.Width  ||
                ly < 0 || ly >= data.Height  ||
                lz < 0 || lz >= data.Width)
                return;

            if (!overwrite && data.GetBlock(lx, ly, lz) != (byte)BlockType.Air)
                return;

            data.SetBlock(lx, ly, lz, blockId);
        }

        /// <summary>Hash déterministe d'une cellule de grille.</summary>
        private static int HashTreeCell(int x, int y, int z)
        {
            unchecked
            {
                int h = (x * 1000003) ^ (y * 2000003) ^ (z * 3000003);
                h ^= h >> 13;
                h *= 1540483477;
                h ^= h >> 15;
                return (int)((uint)h & 0x7FFFFFFF);
            }
        }

        // ═══════════════════════════════════════════════════════
        //  GÉNÉRATION PROCÉDURALE AVEC CONFIG (SEED + BIOME)
        // ═══════════════════════════════════════════════════════

        // ── Masque de couture (partagé) ───────────────────────
        private const float kSeamMarginCfg = 0.10f;

        /// <summary>
        /// Version avec config complète : seed + biome + rayon arbitraire.
        /// Point d'entrée pour toutes les planètes procédurales et la home planet.
        /// </summary>
        public static byte GetBlockType(Vector3 worldPos, Vector3 planetCenter, in PlanetGenerationConfig cfg)
        {
            float dx   = worldPos.x - planetCenter.x;
            float dy   = worldPos.y - planetCenter.y;
            float dz   = worldPos.z - planetCenter.z;
            float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

            if (dist < 0.001f) return GetDeepBlock(cfg.Biome);

            float invDist = 1f / dist;
            float ndx = dx * invDist;
            float ndy = dy * invDist;
            float ndz = dz * invDist;

            float so = cfg.NoiseOffset;
            float freq = cfg.SurfaceFrequency * cfg.CoreRadius;

            float nx = ndx * freq + 100f + so;
            float ny = ndy * freq + 200f + so;
            float nz = ndz * freq + 300f + so;
            float noise = (Mathf.PerlinNoise(nx, nz) + Mathf.PerlinNoise(ny, nx)) * 0.5f;

            float surfaceRadius = cfg.CoreRadius + cfg.SurfaceAmplitude * (noise - 0.5f);

            float depth = surfaceRadius - dist;

            bool cave = cfg.HasCaves
                && (depth > 0f && depth <= cfg.CrustThickness + 3f)
                && IsCaveWithConfig(worldPos, depth, in cfg);

            if (dist > surfaceRadius + 0.5f) return (byte)BlockType.Air;

            // Surface (top block)
            if (dist > surfaceRadius - 0.5f)
                return cave ? (byte)BlockType.Air : GetSurfaceBlock(cfg.Biome, worldPos, so);

            // Sub-surface (~2-3 blocs)
            if (dist > surfaceRadius - 3.5f)
                return cave ? (byte)BlockType.Air : GetSubSurfaceBlock(cfg.Biome);

            if (cave) return (byte)BlockType.Air;

            // Deep (including ore veins)
            return GetDeepBlockWithOres(cfg.Biome, worldPos, depth, so);
        }

        /// <summary>
        /// Génère un chunk complet avec la config complète (seed + biome).
        /// </summary>
        public static void Generate(
            ChunkData  data,
            Vector3    chunkOriginWorld,
            Vector3    planetCenter,
            Quaternion chunkRotation,
            FaceIndex  chunkFace,
            in PlanetGenerationConfig cfg)
        {
            int w = data.Width;
            int h = data.Height;

            for (int z = 0; z < w; z++)
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector3 blockPos = chunkOriginWorld + chunkRotation * new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                Vector3 rel      = blockPos - planetCenter;

                // Masque de face (couture)
                FaceIndex blockFace = SphereFace.GetFace(rel);
                if (blockFace != chunkFace)
                {
                    Vector3 dir     = rel.normalized;
                    float dotSelf   = Vector3.Dot(dir, SphereFace.Normals[(int)chunkFace]);
                    float dotBest   = Vector3.Dot(dir, SphereFace.Normals[(int)blockFace]);
                    if (dotBest - dotSelf > kSeamMarginCfg)
                    {
                        data.SetBlock(x, y, z, (byte)BlockType.Air);
                        continue;
                    }
                }

                data.SetBlock(x, y, z, GetBlockType(blockPos, planetCenter, in cfg));
            }

            if (cfg.HasTrees)
                PlaceTreesWithConfig(data, chunkOriginWorld, planetCenter, chunkRotation, in cfg);
        }

        // ── Blocs de surface par biome ────────────────────────

        private static byte GetSurfaceBlock(PlanetBiome biome, Vector3 pos, float so)
        {
            switch (biome)
            {
                case PlanetBiome.Terran:   return (byte)BlockType.Grass;
                case PlanetBiome.Desert:
                    // Mix sable / sable rouge selon bruit
                    return Mathf.PerlinNoise(pos.x * 0.05f + so, pos.z * 0.05f + so) > 0.65f
                        ? (byte)BlockType.RedSand
                        : (byte)BlockType.Sand;
                case PlanetBiome.Snow:     return (byte)BlockType.PackedIce;
                case PlanetBiome.Volcanic: return (byte)BlockType.Netherrack;
                case PlanetBiome.Forest:   return (byte)BlockType.Grass;
                case PlanetBiome.Mountain:
                    // Pierre nue au sommet, herbe plus bas
                    return Mathf.PerlinNoise(pos.x * 0.04f + so, pos.z * 0.04f + so) > 0.55f
                        ? (byte)BlockType.Stone
                        : (byte)BlockType.Grass;
                case PlanetBiome.Endstone: return (byte)BlockType.EndStone;
                case PlanetBiome.Crystal:  return (byte)BlockType.QuartzBricks;
                case PlanetBiome.Nether:   return (byte)BlockType.Netherrack;
                case PlanetBiome.Cherry:   return (byte)BlockType.Grass;
                case PlanetBiome.Mossy:    return (byte)BlockType.MossyCobblestone;
                default:                   return (byte)BlockType.Grass;
            }
        }

        private static byte GetSubSurfaceBlock(PlanetBiome biome)
        {
            switch (biome)
            {
                case PlanetBiome.Terran:   return (byte)BlockType.Dirt;
                case PlanetBiome.Desert:   return (byte)BlockType.Sandstone;
                case PlanetBiome.Snow:     return (byte)BlockType.BlueIce;
                case PlanetBiome.Volcanic: return (byte)BlockType.Blackstone;
                case PlanetBiome.Forest:   return (byte)BlockType.Dirt;
                case PlanetBiome.Mountain: return (byte)BlockType.Granite;
                case PlanetBiome.Endstone: return (byte)BlockType.EndStone;
                case PlanetBiome.Crystal:  return (byte)BlockType.PurpurBlock;
                case PlanetBiome.Nether:   return (byte)BlockType.SoulSand;
                case PlanetBiome.Cherry:   return (byte)BlockType.CoarseDirt;
                case PlanetBiome.Mossy:    return (byte)BlockType.MossyStoneBricks;
                default:                   return (byte)BlockType.Dirt;
            }
        }

        private static byte GetDeepBlock(PlanetBiome biome)
        {
            switch (biome)
            {
                case PlanetBiome.Terran:   return (byte)BlockType.Stone;
                case PlanetBiome.Desert:   return (byte)BlockType.RedSandstone;
                case PlanetBiome.Snow:     return (byte)BlockType.Stone;
                case PlanetBiome.Volcanic: return (byte)BlockType.Obsidian;
                case PlanetBiome.Forest:   return (byte)BlockType.Stone;
                case PlanetBiome.Mountain: return (byte)BlockType.Andesite;
                case PlanetBiome.Endstone: return (byte)BlockType.PurpurBlock;
                case PlanetBiome.Crystal:  return (byte)BlockType.Deepslate;
                case PlanetBiome.Nether:   return (byte)BlockType.Blackstone;
                case PlanetBiome.Cherry:   return (byte)BlockType.Stone;
                case PlanetBiome.Mossy:    return (byte)BlockType.Cobblestone;
                default:                   return (byte)BlockType.Stone;
            }
        }

        private static byte GetDeepBlockWithOres(PlanetBiome biome, Vector3 pos, float depth, float so)
        {
            // Veines de Glowstone pour Volcanic et Nether
            if (biome == PlanetBiome.Volcanic || biome == PlanetBiome.Nether)
            {
                float glow = Mathf.PerlinNoise(pos.x * 0.30f + so + 80f, pos.y * 0.30f + so + 80f);
                if (glow > 0.88f) return (byte)BlockType.Glowstone;
            }

            // Minerais standard pour toutes les planètes rocheuses
            if (depth > 3f)
            {
                float coalN = Mathf.PerlinNoise(pos.x * 0.22f + so + 10f, pos.z * 0.22f + so + 10f);
                if (coalN > 0.87f) return (byte)BlockType.CoalOre;

                float ironN = Mathf.PerlinNoise(pos.y * 0.25f + so + 30f, pos.x * 0.25f + so + 30f);
                if (ironN > 0.88f) return (byte)BlockType.IronOre;

                if (depth > 6f)
                {
                    float goldN = Mathf.PerlinNoise(pos.z * 0.28f + so + 50f, pos.y * 0.28f + so + 50f);
                    if (goldN > 0.90f) return (byte)BlockType.GoldOre;

                    float diaN = Mathf.PerlinNoise(pos.x * 0.32f + so + 70f, pos.z * 0.32f + so + 70f);
                    if (diaN > 0.93f) return (byte)BlockType.DiamondOre;
                }
            }

            return GetDeepBlock(biome);
        }

        // ── Grottes avec config ───────────────────────────────

        private static bool IsCaveWithConfig(Vector3 p, float depth, in PlanetGenerationConfig cfg)
        {
            if (depth <= 0f) return false;

            float pf       = CavePresenceFrequency;
            float so       = cfg.NoiseOffset;
            float presence = Mathf.PerlinNoise(p.x * pf + 50f + so, p.z * pf + 50f + so);
            if (presence < cfg.CavePresenceThreshold) return false;

            float f = cfg.CaveFrequency;
            float a = Mathf.PerlinNoise(p.x * f + 500f + so, p.y * f + 500f + so);
            float b = Mathf.PerlinNoise(p.y * f + 700f + so, p.z * f + 700f + so);
            float c = Mathf.PerlinNoise(p.x * f + 300f + so, p.z * f + 300f + so);

            float t = Mathf.Clamp01(depth / CaveTransitionDepth);
            float r = Mathf.Lerp(cfg.CaveEntryRadius, cfg.CaveTubeRadius, t);

            bool ab = Mathf.Abs(a - 0.5f) < r && Mathf.Abs(b - 0.5f) < r;
            bool bc = Mathf.Abs(b - 0.5f) < r && Mathf.Abs(c - 0.5f) < r;
            bool ac = Mathf.Abs(a - 0.5f) < r && Mathf.Abs(c - 0.5f) < r;

            return ab || bc || ac;
        }

        // ── Arbres avec config ────────────────────────────────

        private static void PlaceTreesWithConfig(
            ChunkData data, Vector3 chunkOrigin, Vector3 planetCenter,
            Quaternion chunkRotation, in PlanetGenerationConfig cfg)
        {
            int S      = TreeCellSize;
            int expand = MaxTrunkHeight + MaxCanopyRadius + S;
            int cs     = data.Width;

            Vector3 chunkCenter  = chunkOrigin + chunkRotation * new Vector3(cs * 0.5f, cs * 0.5f, cs * 0.5f);
            float   totalRadius  = cs * 0.8660254f + expand + S;

            int gcMinX = Mathf.FloorToInt((chunkCenter.x - totalRadius) / S);
            int gcMaxX = Mathf.CeilToInt ((chunkCenter.x + totalRadius) / S);
            int gcMinY = Mathf.FloorToInt((chunkCenter.y - totalRadius) / S);
            int gcMaxY = Mathf.CeilToInt ((chunkCenter.y + totalRadius) / S);
            int gcMinZ = Mathf.FloorToInt((chunkCenter.z - totalRadius) / S);
            int gcMaxZ = Mathf.CeilToInt ((chunkCenter.z + totalRadius) / S);

            // spawnRatio : 1=dense, 2=normal, 3=rare
            int spawnRatio = cfg.TreeDensity == 2 ? 2 :
                             cfg.TreeDensity == 3 ? 5 : 3;

            // Type d'arbre selon biome
            byte logType, leafType;
            GetTreeBlocks(cfg.Biome, out logType, out leafType);

            float so = cfg.NoiseOffset;

            for (int gx = gcMinX; gx <= gcMaxX; gx++)
            for (int gy = gcMinY; gy <= gcMaxY; gy++)
            for (int gz = gcMinZ; gz <= gcMaxZ; gz++)
            {
                // Hash seed-dépendant : intègre le noiseOffset comme salt entier
                int salt = (int)(so * 100f);
                int hash = HashTreeCell(gx ^ salt, gy, gz);

                if ((hash % spawnRatio) != 0) continue;

                float cx = (gx + 0.5f) * S;
                float cy = (gy + 0.5f) * S;
                float cz = (gz + 0.5f) * S;

                float distFromCenter = Mathf.Sqrt(
                    (cx - planetCenter.x) * (cx - planetCenter.x) +
                    (cy - planetCenter.y) * (cy - planetCenter.y) +
                    (cz - planetCenter.z) * (cz - planetCenter.z));

                float surfMin = cfg.CoreRadius - 2f;
                float surfMax = cfg.CoreRadius + cfg.SurfaceAmplitude + 6f;
                if (distFromCenter < surfMin || distFromCenter > surfMax) continue;

                float ddx = cx - planetCenter.x;
                float ddy = cy - planetCenter.y;
                float ddz = cz - planetCenter.z;
                float inv = 1f / distFromCenter;
                Vector3 up = new Vector3(ddx * inv, ddy * inv, ddz * inv);

                Vector3 searchBase = FindSurfaceBlockBiome(planetCenter, up, in cfg);
                if (searchBase == Vector3.positiveInfinity) continue;

                int trunkH  = MinTrunkHeight + (int)((uint)hash >> 4)  % (MaxTrunkHeight  - MinTrunkHeight  + 1);
                int canopyR = MinCanopyRadius + (int)((uint)hash >> 8)  % (MaxCanopyRadius - MinCanopyRadius + 1);

                // Jungle trees : plus hauts
                if (cfg.Biome == PlanetBiome.Forest)
                {
                    trunkH  = 5 + (int)((uint)hash >> 4) % 5;
                    canopyR = 3 + (int)((uint)hash >> 8) % 2;
                }

                // Tronc
                {
                    const float dda = 0.45f;
                    int steps = Mathf.CeilToInt(trunkH / dda);
                    int prevTx = int.MinValue, prevTy = int.MinValue, prevTz = int.MinValue;
                    for (int s = 1; s <= steps; s++)
                    {
                        float t  = Mathf.Min(s * dda, (float)trunkH);
                        int   tx = Mathf.FloorToInt(searchBase.x + up.x * t);
                        int   ty = Mathf.FloorToInt(searchBase.y + up.y * t);
                        int   tz = Mathf.FloorToInt(searchBase.z + up.z * t);
                        if (tx == prevTx && ty == prevTy && tz == prevTz) continue;
                        TrySetBlock(data, chunkOrigin, tx, ty, tz, logType, overwrite: true, chunkRotation);
                        prevTx = tx; prevTy = ty; prevTz = tz;
                    }
                }

                // Canopy
                Vector3 canopyCenter = new Vector3(
                    searchBase.x + up.x * (trunkH + 0.5f),
                    searchBase.y + up.y * (trunkH + 0.5f),
                    searchBase.z + up.z * (trunkH + 0.5f));

                int cr2 = canopyR;
                for (int lx = -cr2; lx <= cr2; lx++)
                for (int ly = -cr2; ly <= cr2; ly++)
                for (int lz = -cr2; lz <= cr2; lz++)
                {
                    if (lx * lx + ly * ly + lz * lz > cr2 * cr2) continue;
                    int wx = Mathf.FloorToInt(canopyCenter.x) + lx;
                    int wy = Mathf.FloorToInt(canopyCenter.y) + ly;
                    int wz = Mathf.FloorToInt(canopyCenter.z) + lz;
                    TrySetBlock(data, chunkOrigin, wx, wy, wz, leafType, overwrite: false, chunkRotation);
                }
            }
        }

        /// <summary>Détermine les blocs log/feuilles selon le biome.</summary>
        private static void GetTreeBlocks(PlanetBiome biome, out byte log, out byte leaf)
        {
            switch (biome)
            {
                case PlanetBiome.Snow:
                    log  = (byte)BlockType.SpruceLog;
                    leaf = (byte)BlockType.SpruceLeaves;
                    break;
                case PlanetBiome.Forest:
                    log  = (byte)BlockType.JungleLog;
                    leaf = (byte)BlockType.JungleLeaves;
                    break;
                case PlanetBiome.Mountain:
                    log  = (byte)BlockType.Wood;
                    leaf = (byte)BlockType.Leaves;
                    break;
                case PlanetBiome.Cherry:
                    log  = (byte)BlockType.CherryLog;
                    leaf = (byte)BlockType.CherryLeaves;
                    break;
                case PlanetBiome.Mossy:
                    log  = (byte)BlockType.DarkOakLog;
                    leaf = (byte)BlockType.DarkOakLeaves;
                    break;
                default:
                    log  = (byte)BlockType.Wood;
                    leaf = (byte)BlockType.Leaves;
                    break;
            }
        }

        /// <summary>Cherche la surface dans un biome donné.</summary>
        private static Vector3 FindSurfaceBlockBiome(
            Vector3 planetCenter, Vector3 up, in PlanetGenerationConfig cfg)
        {
            float maxR = cfg.CoreRadius + cfg.SurfaceAmplitude + 4f;
            float minR = cfg.CoreRadius - 2f;

            for (float r = maxR; r >= minR; r -= 1f)
            {
                Vector3 worldPos = planetCenter + up * r;
                byte bt = GetBlockType(worldPos, planetCenter, in cfg);
                // Surface = premier bloc solide non-sous-sol (herbe ou équivalent)
                if (bt != (byte)BlockType.Air && bt != (byte)BlockType.Dirt
                    && bt != (byte)BlockType.Stone && bt != (byte)BlockType.Deepslate
                    && bt != (byte)BlockType.Andesite && bt != (byte)BlockType.Granite
                    && bt != (byte)BlockType.Sandstone && bt != (byte)BlockType.RedSandstone)
                    return worldPos;
            }
            return Vector3.positiveInfinity;
        }
    }
}

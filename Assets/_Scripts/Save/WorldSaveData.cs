// ============================================================
//  WorldSaveData.cs
//  Modèle de données JSON-sérialisable pour les sauvegardes AstroVoxel.
//
//  Contenu d'une save :
//    • seed du monde
//    • date de sauvegarde
//    • position / rotation du joueur
//    • modifications de la home planet (deltas vs génération procédurale)
//    • modifications des planètes infinies (bufferisées, par index)
//    • état des astéroïdes : angle orbital courant + modifications de blocs
// ============================================================

using System;
using System.Collections.Generic;

namespace AstroVoxel.Save
{
    // ── Données globales de la sauvegarde ──────────────────────

    /// <summary>
    /// Données complètes d'une sauvegarde de monde.
    /// Sérialisé / désérialisé via <see cref="UnityEngine.JsonUtility"/>.
    /// </summary>
    [Serializable]
    public sealed class WorldSaveData
    {
        /// <summary>Seed du monde.</summary>
        public int seed;

        /// <summary>Date ISO-8601 de la sauvegarde (affichage uniquement).</summary>
        public string saveDate = "";

        // ── Joueur ────────────────────────────────────────────
        public float playerPosX, playerPosY, playerPosZ;
        public float playerRotX, playerRotY, playerRotZ, playerRotW;

        /// <summary>
        /// ShipId du vaisseau que le joueur pilotait au moment de la sauvegarde.
        /// -1 = joueur à pied ; ≥ 0 = joueur embarqué dans ce vaisseau.
        /// </summary>
        public int playerInShipId = -1;

        // ── Vaisseau(x) ──────────────────────────────────────────
        public List<ShipSaveEntry> ships = new List<ShipSaveEntry>();

        // ── Planète de départ ─────────────────────────────────
        public List<PlanetBlockMod> homePlanetMods = new List<PlanetBlockMod>();

        // ── Planètes infinies (index 0-511) ───────────────────
        public List<InfinitePlanetModEntry> infinitePlanetMods = new List<InfinitePlanetModEntry>();

        // ── Astéroïdes (identifiés par seed unique) ───────────
        public List<AsteroidModEntry> asteroidMods = new List<AsteroidModEntry>();
    }

    // ── Modification d'un bloc sur une planète sphérique ─────

    /// <summary>
    /// Un seul bloc modifié sur une planète utilisant la grille 18-Face.
    /// La clé est (face, cu, cv, cr) = <see cref="AstroVoxel.VoxelEngine.FaceChunkCoord"/> ;
    /// (lx, ly, lz) sont les coordonnées locales dans le chunk.
    /// </summary>
    [Serializable]
    public struct PlanetBlockMod
    {
        /// <summary>(int)FaceIndex du chunk.</summary>
        public int  face;
        /// <summary>Index U (selon Rights[face]) du chunk.</summary>
        public int  cu;
        /// <summary>Index V (selon Forwards[face]) du chunk.</summary>
        public int  cv;
        /// <summary>Index R (radial, selon Normals[face]) du chunk.</summary>
        public int  cr;
        /// <summary>Coordonnée locale X dans le chunk [0, ChunkWidth-1].</summary>
        public byte lx;
        /// <summary>Coordonnée locale Y dans le chunk [0, ChunkHeight-1].</summary>
        public byte ly;
        /// <summary>Coordonnée locale Z dans le chunk [0, ChunkDepth-1].</summary>
        public byte lz;
        /// <summary>(byte)BlockType du bloc après modification.</summary>
        public byte block;
    }

    // ── Planète infinie ───────────────────────────────────────

    /// <summary>
    /// Ensemble de modifications pour une planète infinie identifiée par son index (0-511).
    /// </summary>
    [Serializable]
    public sealed class InfinitePlanetModEntry
    {
        /// <summary>Index de la planète dans le tableau _planets d'InfinitePlanetSystem.</summary>
        public int planetIndex;

        /// <summary>Liste des blocs modifiés.</summary>
        public List<PlanetBlockMod> mods = new List<PlanetBlockMod>();
    }

    // ── Astéroïde ─────────────────────────────────────────────
    /// <summary>
    /// Position et orientation sauvegardées d'un vaisseau.
    /// Identifié par <see cref="shipId"/> (auto-incrémenté par
    /// <see cref="AstroVoxel.Vehicle.SpaceShipController"/>).
    /// </summary>
    [Serializable]
    public sealed class ShipSaveEntry
    {
        /// <summary>ID unique du vaisseau dans la session (0, 1, 2…).</summary>
        public int   shipId;
        public float posX, posY, posZ;
        public float rotX, rotY, rotZ, rotW;
    }

    // ── Astéroïde ──────────────────────────────────────────────
    /// <summary>
    /// Un seul bloc modifié sur un astéroïde (grille cubique axis-aligned locale).
    /// (cx, cy, cz) = coord du chunk en unités de chunk ; (lx, ly, lz) = local dans le chunk.
    /// </summary>
    [Serializable]
    public struct AsteroidBlockMod
    {
        public int  cx, cy, cz;
        public byte lx, ly, lz;
        public byte block;
    }

    /// <summary>
    /// Etat complet d'un astéroïde : phase orbitale + modifications de blocs.
    /// Identifié par <see cref="asteroidSeed"/> (seed unique assigné par AsteroidField).
    /// </summary>
    [Serializable]
    public sealed class AsteroidModEntry
    {
        /// <summary>Seed unique de l'astéroïde (= AsteroidWorld.seed).</summary>
        public int   asteroidSeed;

        /// <summary>Angle orbital courant en degrés au moment de la sauvegarde.</summary>
        public float orbitalAngle;

        /// <summary>Blocs modifiés par le joueur (vide si aucun).</summary>
        public List<AsteroidBlockMod> mods = new List<AsteroidBlockMod>();
    }
}

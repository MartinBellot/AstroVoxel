// ============================================================
//  SaveSystem.cs
//  Singleton MonoBehaviour gérant la sauvegarde et le chargement
//  des mondes AstroVoxel.
//
//  Commandes (via GameConsole) :
//    /save NOM  → sérialise l'état courant en JSON
//    /load NOM  → désérialise, force la seed, recharge la scène
//    /saves     → liste les fichiers de save disponibles
//
//  Flux de chargement :
//    1. LoadWorld() → ForceInitialize(seed) + PendingLoad = data
//    2. SceneManager.LoadScene(...)
//    3. GameBootstrap.Awake() → seed déjà fixée, crée SaveSystem
//    4. SaveSystem.Init() → ApplyPendingLoad() :
//         - home planet  : mods appliquées (monde déjà généré)
//         - planètes inf. : stockées dans InfinitePlanetSystem (appliquées au chargement voxel)
//         - astéroïdes   : angle orbital restauré + mods queued (appliquées après LOD load)
//         - joueur       : position restaurée après 2 frames (physique stable)
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using AstroVoxel.VoxelEngine;
using AstroVoxel.Space;

namespace AstroVoxel.Save
{
    /// <summary>
    /// Gestionnaire de sauvegardes. Un seul par scène, créé par GameBootstrap.
    /// </summary>
    public sealed class SaveSystem : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────
        public static SaveSystem Instance { get; private set; }

        // ── Données en attente (survit au rechargement de scène) ──
        /// <summary>
        /// Sauvegarde en attente d'application après rechargement de scène.
        /// Champ static → survit à SceneManager.LoadScene.
        /// Remis à null dans <see cref="Init"/> après application.
        /// </summary>
        public static WorldSaveData PendingLoad { get; private set; }

        // ── Références scène ──────────────────────────────────
        private PlanetWorld          _homePlanet;
        private Transform            _player;
        private InfinitePlanetSystem _infinitePlanetSystem;

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Init (appelé par GameBootstrap) ───────────────────

        /// <summary>
        /// Initialise le système avec les références de scène et applique
        /// la sauvegarde en attente si elle existe.
        /// Doit être appelé APRÈS <c>world.UpdateChunks()</c>.
        /// </summary>
        public void Init(PlanetWorld homePlanet, Transform player, InfinitePlanetSystem infinitePlanetSystem)
        {
            _homePlanet           = homePlanet;
            _player               = player;
            _infinitePlanetSystem = infinitePlanetSystem;

            if (PendingLoad != null)
            {
                ApplyPendingLoad(PendingLoad);
                PendingLoad = null;
            }
        }

        // ── Sauvegarde ────────────────────────────────────────

        /// <summary>
        /// Collecte l'état courant et l'écrit dans saves/&lt;name&gt;.json.
        /// Lance une exception si l'écriture échoue.
        /// </summary>
        public void SaveWorld(string saveName)
        {
            WorldSaveData data = CollectSaveData();
            string json        = JsonUtility.ToJson(data, prettyPrint: true);
            string path        = GetSavePath(saveName);

            Directory.CreateDirectory(GetSaveDirectory());
            File.WriteAllText(path, json);
        }

        /// <summary>Retourne true si un fichier de save portant ce nom existe déjà.</summary>
        public bool SaveExists(string saveName) => File.Exists(GetSavePath(saveName));

        /// <summary>Liste les noms de toutes les sauvegardes disponibles.</summary>
        public string[] GetSaveNames()
        {
            string dir = GetSaveDirectory();
            if (!Directory.Exists(dir)) return Array.Empty<string>();

            string[] files = Directory.GetFiles(dir, "*.json");
            string[] names = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
                names[i] = Path.GetFileNameWithoutExtension(files[i]);
            return names;
        }

        // ── Chargement ────────────────────────────────────────

        /// <summary>
        /// Lit le fichier de save, force la seed dans WorldSeedManager,
        /// stocke les données dans <see cref="PendingLoad"/>, puis recharge la scène.
        /// Lance <see cref="FileNotFoundException"/> si le fichier est absent.
        /// </summary>
        public void LoadWorld(string saveName)
        {
            string path = GetSavePath(saveName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Sauvegarde introuvable : {saveName}", path);

            string       json = File.ReadAllText(path);
            WorldSaveData data = JsonUtility.FromJson<WorldSaveData>(json);
            if (data == null)
                throw new InvalidDataException($"Fichier de sauvegarde corrompu : {saveName}");

            // Stocker avant reload (champ static)
            PendingLoad = data;

            // Fixer la seed avant reload (WorldSeedManager est static)
            WorldSeedManager.ForceInitialize(data.seed);

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // ── Collecte des données ──────────────────────────────

        private WorldSaveData CollectSaveData()
        {
            var data = new WorldSaveData
            {
                seed     = WorldSeedManager.Seed,
                saveDate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            };

            // Joueur
            if (_player != null)
            {
                data.playerPosX = _player.position.x;
                data.playerPosY = _player.position.y;
                data.playerPosZ = _player.position.z;
                data.playerRotX = _player.rotation.x;
                data.playerRotY = _player.rotation.y;
                data.playerRotZ = _player.rotation.z;
                data.playerRotW = _player.rotation.w;
            }

            // Home planet
            if (_homePlanet != null)
                data.homePlanetMods = _homePlanet.GetModifications();

            // Planètes infinies (active + buffer)
            if (_infinitePlanetSystem != null)
                data.infinitePlanetMods = _infinitePlanetSystem.CollectModifications();

            // Astéroïdes
            CollectAsteroidData(data);

            return data;
        }

        private static void CollectAsteroidData(WorldSaveData data)
        {
            // Tous les AsteroidWorld de la scène (actifs ou non)
            var worlds = FindObjectsByType<AsteroidWorld>(FindObjectsInactive.Include);
            foreach (var world in worlds)
            {
                var   orbit = world.GetComponent<AsteroidOrbit>();
                float angle = orbit != null ? orbit.CurrentAngle : 0f;
                var   mods  = world.GetModifications();

                // Sauvegarder tous les astéroïdes (angle orbital + mods éventuelles)
                data.asteroidMods.Add(new AsteroidModEntry
                {
                    asteroidSeed = world.seed,
                    orbitalAngle = angle,
                    mods         = mods,
                });
            }
        }

        // ── Application d'une sauvegarde ─────────────────────

        private void ApplyPendingLoad(WorldSaveData data)
        {
            // Home planet (monde déjà généré par UpdateChunks)
            if (_homePlanet != null && data.homePlanetMods != null && data.homePlanetMods.Count > 0)
                _homePlanet.ApplyModifications(data.homePlanetMods);

            // Planètes infinies → buffering dans InfinitePlanetSystem
            if (_infinitePlanetSystem != null && data.infinitePlanetMods != null)
                _infinitePlanetSystem.LoadPendingMods(data.infinitePlanetMods);

            // Astéroïdes → angle orbital (avant Start()) + mods queued
            if (data.asteroidMods != null && data.asteroidMods.Count > 0)
                ApplyAsteroidData(data.asteroidMods);

            // Position joueur → différée (physique doit se stabiliser)
            StartCoroutine(RestorePlayerPosition(data));
        }

        private static void ApplyAsteroidData(List<AsteroidModEntry> entries)
        {
            if (entries == null || entries.Count == 0) return;

            // Table de lookup : asteroidSeed → entry
            var lookup = new Dictionary<int, AsteroidModEntry>(entries.Count);
            foreach (var e in entries)
                lookup[e.asteroidSeed] = e;

            // FindObjectsInactive.Include : les astéroïdes dont le voxel
            // n'est pas encore chargé (chunk GO désactivé par AsteroidLOD) sont inclus.
            var worlds = FindObjectsByType<AsteroidWorld>(FindObjectsInactive.Include);
            foreach (var world in worlds)
            {
                if (!lookup.TryGetValue(world.seed, out var entry)) continue;

                // Restaurer l'angle orbital AVANT Start() (Start n'a pas encore tourné
                // car nous sommes dans le même Awake que le bootstrap).
                var orbit = world.GetComponent<AsteroidOrbit>();
                if (orbit != null)
                    orbit.SetOrbitAngle(entry.orbitalAngle);

                // Queuer les modifications de blocs (appliquées après LoadChunksCoroutine)
                if (entry.mods != null && entry.mods.Count > 0)
                    world.QueueModifications(entry.mods);
            }
        }

        private IEnumerator RestorePlayerPosition(WorldSaveData data)
        {
            // Attendre 2 frames : la physique doit être stable et les colliders générés
            yield return null;
            yield return null;

            if (_player == null) yield break;

            var pos = new Vector3(data.playerPosX, data.playerPosY, data.playerPosZ);
            var rot = new Quaternion(data.playerRotX, data.playerRotY, data.playerRotZ, data.playerRotW);

            var rb = _player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position         = pos;
                rb.rotation         = rot;
                rb.linearVelocity   = Vector3.zero;
                rb.angularVelocity  = Vector3.zero;
            }
            else
            {
                _player.SetPositionAndRotation(pos, rot);
            }
        }

        // ── Chemins de fichiers ───────────────────────────────

        private static string GetSaveDirectory()
            => Path.Combine(Application.persistentDataPath, "saves");

        private static string GetSavePath(string name)
            => Path.Combine(GetSaveDirectory(), name + ".json");
    }
}

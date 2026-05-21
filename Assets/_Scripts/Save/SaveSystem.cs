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
using AstroVoxel.Vehicle;
using AstroVoxel.Player;

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

        // ── Nom de la save en cours de chargement (static : survit au reload) ──
        private static string _pendingLoadName;

        // ── Overlay HUD de chargement ─────────────────────────
        private string _overlayMsg       = null;
        private float  _overlayAlpha     = 0f;
        private float  _overlayFadeTimer = -1f;   // < 0 = affiché fixe ; ≥ 0 = fondu en cours
        private const  float kOverlayFadeDuration = 1.5f;

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

        /// <summary>
        /// Supprime le fichier de sauvegarde portant ce nom.
        /// Lance <see cref="FileNotFoundException"/> si la sauvegarde est introuvable.
        /// </summary>
        public void DeleteSave(string saveName)
        {
            string path = GetSavePath(saveName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Sauvegarde introuvable : {saveName}", path);
            File.Delete(path);
        }

        /// <summary>
        /// Ouvre le dossier des sauvegardes dans l'explorateur de fichiers (éditeur / standalone).
        /// Sans effet en WebGL.
        /// </summary>
        public void OpenSavesFolder()
        {
            string dir = GetSaveDirectory();
            Directory.CreateDirectory(dir);   // crée le dossier s'il n'existe pas encore
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log($"[SaveSystem] Dossier saves : {dir}");
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            System.Diagnostics.Process.Start("open", "\"" + dir + "\"");
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            System.Diagnostics.Process.Start("explorer.exe", "\"" + dir.Replace('/', '\\') + "\"");
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            System.Diagnostics.Process.Start("xdg-open", "\"" + dir + "\"");
#else
            Application.OpenURL("file://" + dir.Replace('\\', '/'));
#endif
        }

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

            // Stocker avant reload (champs static)
            _pendingLoadName = saveName;
            PendingLoad      = data;

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

            // Mode de jeu
            data.gameMode = (int)GameModeManager.Current;

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

            // Le joueur était-il dans un vaisseau ?
            data.playerInShipId = -1;
            var allShipsCheck = FindObjectsByType<SpaceShipController>(FindObjectsInactive.Include);
            foreach (var s in allShipsCheck)
            {
                if (s.IsPiloting) { data.playerInShipId = s.ShipId; break; }
            }

            // Home planet
            if (_homePlanet != null)
                data.homePlanetMods = _homePlanet.GetModifications();

            // Planètes infinies (active + buffer)
            if (_infinitePlanetSystem != null)
                data.infinitePlanetMods = _infinitePlanetSystem.CollectModifications();

            // Vaisseau(x)
            var ships = FindObjectsByType<SpaceShipController>(FindObjectsInactive.Include);
            foreach (var ship in ships)
            {
                data.ships.Add(new ShipSaveEntry
                {
                    shipId = ship.ShipId,
                    posX   = ship.transform.position.x,
                    posY   = ship.transform.position.y,
                    posZ   = ship.transform.position.z,
                    rotX   = ship.transform.rotation.x,
                    rotY   = ship.transform.rotation.y,
                    rotZ   = ship.transform.rotation.z,
                    rotW   = ship.transform.rotation.w,
                });
            }

            // Astéroïdes
            CollectAsteroidData(data);

            // Hotbar créatif
            var blockInteract = _player != null ? _player.GetComponent<BlockInteraction>() : null;
            if (blockInteract != null)
            {
                data.creativeHotbarSlots = new int[9];
                var cHotbar = blockInteract.Hotbar;
                for (int i = 0; i < 9 && i < cHotbar.Length; i++)
                    data.creativeHotbarSlots[i] = (int)cHotbar[i];
            }

            // Inventaire de survie (sac + layout hotbar)
            var survData = SurvivalInventoryData.Instance;
            var allStacks = survData.GetAllStacks();
            data.survivalBag = new List<ItemSaveEntry>(allStacks.Count);
            foreach (var stack in allStacks)
                data.survivalBag.Add(new ItemSaveEntry { itemTypeId = (int)stack.itemType, count = stack.count });

            data.survivalHotbarSlots = new int[9];
            for (int i = 0; i < 9; i++)
                data.survivalHotbarSlots[i] = (int)survData.Hotbar[i].itemType;  // -1 si vide

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
            // Mode de jeu
            GameModeManager.SetMode((GameMode)data.gameMode);

            // Home planet (monde déjà généré par UpdateChunks)
            if (_homePlanet != null && data.homePlanetMods != null && data.homePlanetMods.Count > 0)
                _homePlanet.ApplyModifications(data.homePlanetMods);

            // Planètes infinies → buffering dans InfinitePlanetSystem
            if (_infinitePlanetSystem != null && data.infinitePlanetMods != null)
                _infinitePlanetSystem.LoadPendingMods(data.infinitePlanetMods);

            // Astéroïdes → angle orbital (avant Start()) + mods queued
            if (data.asteroidMods != null && data.asteroidMods.Count > 0)
                ApplyAsteroidData(data.asteroidMods);

            // Hotbar créatif
            var blockInteract = _player != null ? _player.GetComponent<BlockInteraction>() : null;
            if (blockInteract != null && data.creativeHotbarSlots != null && data.creativeHotbarSlots.Length == 9)
                for (int i = 0; i < 9; i++)
                    blockInteract.SetHotbarSlot(i, (BlockType)data.creativeHotbarSlots[i]);

            // Inventaire de survie
            var bag = new Dictionary<ItemType, int>();
            if (data.survivalBag != null)
                foreach (var entry in data.survivalBag)
                    if (entry.count > 0) bag[(ItemType)entry.itemTypeId] = entry.count;

            ItemType[] hotbarSlots = null;
            if (data.survivalHotbarSlots != null && data.survivalHotbarSlots.Length == 9)
            {
                hotbarSlots = new ItemType[9];
                for (int i = 0; i < 9; i++)
                    hotbarSlots[i] = (ItemType)data.survivalHotbarSlots[i];
            }
            SurvivalInventoryData.Instance.LoadFromSave(bag, hotbarSlots);

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
            // Attendre 2 frames minimum (physique stable, composants awake)
            yield return null;
            yield return null;

            // Afficher l'overlay de chargement
            ShowOverlay("Chargement en cours\u2026");

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 1 — Geler immédiatement tout à la position sauvegardée.
            //
            // Le problème : entre la fin de Awake() et le chargement async des
            // chunks, les Rigidbody du joueur et des vaisseaux subissent la
            // gravité et tombent dans le vide.
            // Solution : passer en kinematic + désactiver les colliders des
            // vaisseaux (pour ne pas bloquer le joueur pendant le chargement),
            // puis tout dégeler une fois le monde prêt.
            // ═══════════════════════════════════════════════════════════════════

            var savedPlayerPos = new Vector3(data.playerPosX, data.playerPosY, data.playerPosZ);
            var savedPlayerRot = new Quaternion(data.playerRotX, data.playerRotY, data.playerRotZ, data.playerRotW);

            // -- Joueur --
            Rigidbody playerRb           = _player != null ? _player.GetComponent<Rigidbody>() : null;
            bool      playerWasKinematic = playerRb != null && playerRb.isKinematic;
            if (playerRb != null)
            {
                playerRb.isKinematic = true;
                playerRb.position    = savedPlayerPos;
                playerRb.rotation    = savedPlayerRot;
            }
            else if (_player != null)
            {
                _player.SetPositionAndRotation(savedPlayerPos, savedPlayerRot);
            }

            // -- Vaisseaux : geler + désactiver colliders --
            Dictionary<int, ShipSaveEntry>             shipLookup      = null;
            SpaceShipController[]                      allShips        = System.Array.Empty<SpaceShipController>();
            Dictionary<SpaceShipController, bool>      frozenKinematic = new Dictionary<SpaceShipController, bool>();
            Dictionary<SpaceShipController, Collider[]> frozenColliders = new Dictionary<SpaceShipController, Collider[]>();

            if (data.ships != null && data.ships.Count > 0)
            {
                shipLookup = new Dictionary<int, ShipSaveEntry>(data.ships.Count);
                foreach (var e in data.ships) shipLookup[e.shipId] = e;

                allShips = FindObjectsByType<SpaceShipController>(FindObjectsInactive.Include);
                foreach (var s in allShips)
                {
                    if (!shipLookup.TryGetValue(s.ShipId, out var e)) continue;
                    var sPos   = new Vector3(e.posX, e.posY, e.posZ);
                    var sRot   = new Quaternion(e.rotX, e.rotY, e.rotZ, e.rotW);
                    var shipRb = s.GetComponent<Rigidbody>();
                    if (shipRb != null)
                    {
                        frozenKinematic[s] = shipRb.isKinematic;
                        shipRb.isKinematic = true;
                        shipRb.position    = sPos;
                        shipRb.rotation    = sRot;
                    }
                    else
                    {
                        s.transform.SetPositionAndRotation(sPos, sRot);
                    }

                    // Désactiver les colliders : le vaisseau kinematic geler
                    // ne doit pas bloquer le joueur pendant que le monde se
                    // construit autour de lui.
                    var cols = s.GetComponentsInChildren<Collider>(includeInactive: true);
                    frozenColliders[s] = cols;
                    foreach (var col in cols) col.enabled = false;
                }
            }

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 2 — Attendre le chargement du monde autour du joueur.
            // ═══════════════════════════════════════════════════════════════════
            const float kTimeout = 90f;
            float elapsed = 0f;
            while (elapsed < kTimeout && !IsVoxelWorldLoadedNear(savedPlayerPos))
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            // Laisser 2 frames supplémentaires pour que Unity mette à jour
            // les MeshCollider des chunks fraîchement reconstruits.
            yield return null;
            yield return null;

            // ── Dégeler le joueur ─────────────────────────────────────────────
            if (playerRb != null)
            {
                playerRb.linearVelocity  = Vector3.zero;
                playerRb.angularVelocity = Vector3.zero;
                playerRb.isKinematic     = playerWasKinematic;
            }

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 3 — Attendre + dégeler + réactiver colliders pour chaque vaisseau.
            // ═══════════════════════════════════════════════════════════════════
            foreach (var s in allShips)
            {
                if (shipLookup == null || !shipLookup.TryGetValue(s.ShipId, out var e)) continue;
                if (!frozenKinematic.ContainsKey(s)) continue;

                var sPos = new Vector3(e.posX, e.posY, e.posZ);

                float shipElapsed = 0f;
                while (shipElapsed < kTimeout && !IsVoxelWorldLoadedNear(sPos))
                {
                    shipElapsed += Time.deltaTime;
                    yield return null;
                }
                yield return null;   // frame physique avant de dégeler

                var shipRb = s.GetComponent<Rigidbody>();
                if (shipRb != null)
                {
                    shipRb.linearVelocity  = Vector3.zero;
                    shipRb.angularVelocity = Vector3.zero;
                    shipRb.isKinematic     = frozenKinematic[s];
                }

                // Réactiver les colliders du vaisseau
                if (frozenColliders.TryGetValue(s, out var cols))
                    foreach (var col in cols) col.enabled = true;
            }

            // ═══════════════════════════════════════════════════════════════════
            // PHASE 4 — Embarquer le joueur si nécessaire.
            // ═══════════════════════════════════════════════════════════════════
            if (data.playerInShipId >= 0)
            {
                var allShipsFinal = FindObjectsByType<SpaceShipController>(FindObjectsInactive.Include);
                foreach (var s in allShipsFinal)
                {
                    if (s.ShipId == data.playerInShipId) { s.Board(); break; }
                }
            }

            // Afficher le message de confirmation, puis fondu
            string displayName = string.IsNullOrEmpty(_pendingLoadName) ? "monde" : _pendingLoadName;
            ShowOverlay($"Monde chargé : {displayName}");
            yield return new WaitForSeconds(2f);
            StartFadeOverlay();
        }

        /// <summary>
        /// Retourne true si le monde voxel au niveau de <paramref name="pos"/> est
        /// complètement chargé (ou si aucun monde particulier n'est attendu à cet endroit).
        /// </summary>
        private bool IsVoxelWorldLoadedNear(Vector3 pos)
        {
            // ── Planètes (home planet + planète infinie active) ────────────────
            // Marge = 4 chunks au-dessus du coreRadius pour couvrir l'atmosphère basse.
            var planets = FindObjectsByType<PlanetWorld>(FindObjectsInactive.Exclude);
            foreach (var p in planets)
            {
                float margin = VoxelData.ChunkWidth * 4f;
                if (Vector3.Distance(pos, p.PlanetCenter) < p.planetRadius + margin)
                {
                    if (!p.IsFullyLoaded) return false;
                    // Home planet : mods appliquées synchroniquement avant la coroutine.
                    if (p == _homePlanet) return true;
                    // Planète infinie : attendre aussi la fin de ApplyModifications
                    // + la mise à jour des MeshColliders (flag géré par InfinitePlanetSystem).
                    return _infinitePlanetSystem == null
                        || _infinitePlanetSystem.ActiveWorldReadyForPlayer;
                }
            }

            // ── Astéroïdes ─────────────────────────────────────────────────────
            // On cherche le plus proche dans une portée de 3× son rayon.
            var asteroids = FindObjectsByType<AsteroidWorld>(FindObjectsInactive.Exclude);
            AsteroidWorld nearest = null;
            float nearestDist = float.MaxValue;
            foreach (var a in asteroids)
            {
                float d = Vector3.Distance(pos, a.AsteroidCenter);
                if (d < nearestDist) { nearestDist = d; nearest = a; }
            }
            if (nearest != null && nearestDist < nearest.coreRadius * 3f)
                return nearest.IsLoaded;

            // ── Espace ouvert : aucun monde voxel attendu → on peut téléporter ─
            return true;
        }

        // ── Overlay HUD ───────────────────────────────────────

        private void ShowOverlay(string msg)
        {
            _overlayMsg       = msg;
            _overlayAlpha     = 1f;
            _overlayFadeTimer = -1f;
        }

        private void StartFadeOverlay() => _overlayFadeTimer = 0f;

        private void Update()
        {
            if (_overlayMsg == null || _overlayFadeTimer < 0f) return;
            _overlayFadeTimer += Time.deltaTime;
            _overlayAlpha = Mathf.Clamp01(1f - _overlayFadeTimer / kOverlayFadeDuration);
            if (_overlayAlpha <= 0f) { _overlayMsg = null; _overlayFadeTimer = -1f; }
        }

        private void OnGUI()
        {
            if (_overlayMsg == null || _overlayAlpha <= 0f) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 32,
                fontStyle = FontStyle.Bold,
            };
            Color tc = Color.white; tc.a = _overlayAlpha;
            style.normal.textColor = tc;

            var shadow = new GUIStyle(style);
            Color sc = Color.black; sc.a = _overlayAlpha * 0.75f;
            shadow.normal.textColor = sc;

            float y = (Screen.height - 60f) * 0.45f;
            GUI.Label(new Rect(2f, y + 2f, Screen.width, 60f), _overlayMsg, shadow);
            GUI.Label(new Rect(0f, y,       Screen.width, 60f), _overlayMsg, style);
        }

        // ── Chemins de fichiers ───────────────────────────────

        private static string GetSaveDirectory()
            => Path.Combine(Application.persistentDataPath, "saves");

        private static string GetSavePath(string name)
            => Path.Combine(GetSaveDirectory(), name + ".json");

        // ── API statique (utilisable avant la création de l'instance) ──
        // Appelée par MainMenu avant que GameBootstrap ait construit le monde.

        /// <summary>Liste tous les noms de saves sans instance.</summary>
        public static string[] GetAllSaveNames()
        {
            string dir = GetSaveDirectory();
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            string[] files = Directory.GetFiles(dir, "*.json");
            string[] names = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
                names[i] = Path.GetFileNameWithoutExtension(files[i]);
            return names;
        }

        /// <summary>
        /// Lit les métadonnées d'une save pour affichage dans le menu (seed, date, gameMode).
        /// Retourne null si le fichier est absent ou corrompu.
        /// </summary>
        public static WorldSaveData ReadSaveMetadata(string saveName)
        {
            string path = GetSavePath(saveName);
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<WorldSaveData>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Charge une save sans instance : fixe la seed, stocke PendingLoad
        /// et recharge la scène. Appelé par MainMenu.
        /// </summary>
        public static void LoadWorldStatic(string saveName)
        {
            string path = GetSavePath(saveName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Sauvegarde introuvable : {saveName}", path);

            string        json = File.ReadAllText(path);
            WorldSaveData data = JsonUtility.FromJson<WorldSaveData>(json);
            if (data == null)
                throw new InvalidDataException($"Fichier de sauvegarde corrompu : {saveName}");

            _pendingLoadName = saveName;
            PendingLoad      = data;
            WorldSeedManager.ForceInitialize(data.seed);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>Supprime une save sans instance.</summary>
        public static void DeleteSaveFile(string saveName)
        {
            string path = GetSavePath(saveName);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

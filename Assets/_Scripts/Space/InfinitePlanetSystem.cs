// ============================================================
//  InfinitePlanetSystem.cs
//  Gère un nombre infini de planètes procédurales dans l'espace.
//
//  ARCHITECTURE :
//  • Positions et configs des planètes calculées UNE FOIS à l'init
//    via WorldSeedManager → zéro allocation en runtime.
//  • Planètes lointaines (> ImpostorRange) : non affichées.
//  • Planètes visibles (< ImpostorRange) : sphère colorée rendue
//    via Graphics.DrawMesh (zéro GameObject, zéro GC).
//  • Planète la plus proche (< VoxelActivate) : PlanetWorld complet
//    généré en async (chunksPerFrame pour éviter les gels).
//  • Seule la home planet utilise le chargement synchrone initial.
//
//  PERFORMANCE :
//  • O(N) distance check par Update → négligeable (512 float mults).
//  • Graphics.DrawMesh : pas de Camera.Render overhead.
//  • Un seul PlanetWorld distant à la fois.
// ============================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AstroVoxel.Save;
using AstroVoxel.VoxelEngine;
using AstroVoxel.Physics;
using AstroVoxel.Environment;

namespace AstroVoxel.Space
{
    /// <summary>
    /// Système de planètes procédurales infinies.
    /// Créé par <see cref="AstroVoxel.Bootstrap.GameBootstrap"/>.
    /// </summary>
    public sealed class InfinitePlanetSystem : MonoBehaviour
    {
        // ── Paramètres de distance ────────────────────────────

        /// <summary>Distance max pour afficher l'impostor sphérique.</summary>
        public const float ImpostorRange   = 80000f;

        /// <summary>Marge AU-DESSUS du rayon pour déclencher le chargement voxel.
        /// Le joueur ne devra plus traverser l'impostor : les voxels démarrent
        /// bien avant.</summary>
        public const float VoxelActivateBuffer   = 600f;

        /// <summary>Marge AU-DESSUS du rayon pour décharger les voxels (hystérésis).</summary>
        public const float VoxelDeactivateBuffer = 900f;

        /// <summary>Rayon max pour le chargement voxel (au-delà = impostor uniquement).</summary>
        /// <remarks>Plus de limite : toutes les planètes peuvent charger des voxels.</remarks>
        public const float MaxVoxelRadius  = float.MaxValue;

        /// <summary>Échelle visuelle de l'impostor par rapport au rayon réel.
        /// 1.0 = la sphère colorée a la même taille que la planète voxel.</summary>
        private const float ImpostorScale = 1.0f;

        /// <summary>Nombre de planètes dans le monde (assure l'infini en exploration).</summary>
        private const int PlanetCount = 512;

        /// <summary>Chunks chargés par frame lors du chargement async d'une planète distante.</summary>
        private const int AsyncChunksPerFrame = 6;

        // ── Données planètes ──────────────────────────────────

        private struct PlanetData
        {
            public Vector3              Position;
            public PlanetGenerationConfig Config;
            public Color                ImpostorColor;
        }

        private PlanetData[] _planets;

        // ── Ressources de rendu des impostors ─────────────────

        private Mesh       _sphereMesh;
        private Material[] _impostorMaterials;   // 1 par biome (11 biomes)
        private static readonly Color[] BiomeColors = new Color[]
        {
            new Color(0.25f, 0.55f, 0.20f),  // Terran   (vert)
            new Color(0.88f, 0.70f, 0.30f),  // Desert   (sable)
            new Color(0.80f, 0.90f, 1.00f),  // Snow     (blanc bleuté)
            new Color(0.55f, 0.12f, 0.08f),  // Volcanic (rouge sombre)
            new Color(0.08f, 0.42f, 0.10f),  // Forest   (vert foncé)
            new Color(0.52f, 0.50f, 0.54f),  // Mountain (gris)
            new Color(0.82f, 0.80f, 0.62f),  // Endstone (beige)
            new Color(0.68f, 0.38f, 0.88f),  // Crystal  (violet)
            new Color(0.48f, 0.08f, 0.05f),  // Nether   (rouge-brun)
            new Color(0.95f, 0.62f, 0.72f),  // Cherry   (rose)
            new Color(0.32f, 0.46f, 0.28f),  // Mossy    (vert mousse)
        };

        // ── Planète voxel active ──────────────────────────────

        private int         _activeIndex  = -1;
        private GameObject  _activeGO;
        private PlanetWorld _activeWorld;
        private Coroutine   _loadCoroutine;
        // Vrai quand le monde actif est chargé ET que ses mods de blocs sont appliquées
        // (les colliders de mesh ont eu le temps d'être mis à jour par le moteur physique).
        // Utilisé par SaveSystem pour ne pas téléporter trop tôt.
        private bool _activeWorldReady = true;
        public  bool ActiveWorldReadyForPlayer => _activeWorldReady;
        // Buffer de modifications planètes infinies (save/load).
        // Persiste entre les sessions d'activation/désactivation.
        private readonly Dictionary<int, List<PlanetBlockMod>> _pendingPlanetMods
            = new Dictionary<int, List<PlanetBlockMod>>();
        // ── Références ────────────────────────────────────────

        private Transform  _player;
        private Camera     _activeCamera;   // caméra active (joueur ou vaisseau)
        private Material[] _blockMaterials;

        // ── Init ──────────────────────────────────────────────

        private void Awake()
        {
            // Détecte une instance créée depuis la scène sans appel à Init()
            // (composant traîné dans l'Inspector au lieu d'être créé par GameBootstrap).
            // La vérification se fait dans Update() via le guard null.
        }

        /// <summary>
        /// Appelé par GameBootstrap après la construction du joueur.
        /// </summary>
        public void Init(Transform player, Material[] blockMaterials)
        {
            _player         = player;
            // Toujours charger depuis Resources pour garantir l'intégrité des références
            var registry = UnityEngine.Resources.Load<BlockTextureRegistry>("BlockTextureRegistry");
            _blockMaterials = (registry != null && registry.materials != null && registry.materials.Length == 256)
                ? registry.materials
                : blockMaterials;

            if (_blockMaterials == null)
                Debug.LogError("[InfinitePlanetSystem] blockMaterials est null — BlockTextureRegistry introuvable dans Resources/.");

            GeneratePlanetData();
            BuildSphereMesh();
            BuildImpostorMaterials();
        }

        // ── Génération des données planètes ───────────────────

        private void GeneratePlanetData()
        {
            _planets = new PlanetData[PlanetCount];
            var rng  = WorldSeedManager.GetRng(salt: 42);

            for (int i = 0; i < PlanetCount; i++)
            {
                // Direction uniforme sur la sphère (méthode Marsaglia)
                float x, y, z, r2;
                do
                {
                    x  = (float)(rng.NextDouble() * 2.0 - 1.0);
                    y  = (float)(rng.NextDouble() * 2.0 - 1.0);
                    z  = (float)(rng.NextDouble() * 2.0 - 1.0);
                    r2 = x*x + y*y + z*z;
                } while (r2 >= 1f || r2 < 1e-6f);

                float inv  = 1f / Mathf.Sqrt(r2);
                float dir_x = x * inv;
                float dir_y = y * inv;
                float dir_z = z * inv;

                // Distance : distribution quadratique → plus de planètes proches
                // Min 2500u / Max 25000u — 5× la valeur précédente, espace cohérent sans collision
                float t    = (float)rng.NextDouble();
                float dist = Mathf.Lerp(2500f, 25000f, t * t);

                Vector3 pos = new Vector3(dir_x * dist, dir_y * dist, dir_z * dist);

                // Biome aléatoire (11 biomes possibles)
                var biome = (PlanetBiome)(rng.Next(0, 11));

                // Taille : petites à grandes planètes (min 12, max 140)
                // Les très grandes planètes (> MaxVoxelRadius) restent des impostors
                float sizeT  = (float)rng.NextDouble();
                // Distribution biaisée : beaucoup de petites, quelques grandes
                float size   = Mathf.Lerp(12f, 140f, sizeT * sizeT);

                var cfg = PlanetGenerationConfig.ForBiome(
                    biome, size, WorldSeedManager.Seed, i);

                _planets[i] = new PlanetData
                {
                    Position      = pos,
                    Config        = cfg,
                    ImpostorColor = BiomeColors[(int)biome],
                };
            }

            // ── Anti-collision : relaxation par passes ────────
            // Pousse les planètes qui se chevauchent pour éviter toute interpénétration.
            // Marge large + plus de passes : aucune planète ne doit toucher visuellement.
            // O(N²) sur N=512 → ~131k paires/passe ; 6 passes ≈ 800k tests : négligeable
            // car ne s'exécute qu'UNE seule fois à l'init.
            const float CollisionMargin = 400f;   // espace libre minimum entre deux surfaces
            const int   RelaxPasses    = 6;
            for (int pass = 0; pass < RelaxPasses; pass++)
            {
                for (int i = 0; i < _planets.Length; i++)
                {
                    for (int j = i + 1; j < _planets.Length; j++)
                    {
                        float minDist = _planets[i].Config.CoreRadius
                                      + _planets[j].Config.CoreRadius
                                      + CollisionMargin;
                        Vector3 delta = _planets[j].Position - _planets[i].Position;
                        float   dist  = delta.magnitude;
                        if (dist < minDist && dist > 0.001f)
                        {
                            // Sépare les deux planètes symétriquement
                            Vector3 push = delta * ((minDist - dist) * 0.5f / dist);
                            _planets[i] = new PlanetData
                            {
                                Position      = _planets[i].Position - push,
                                Config        = _planets[i].Config,
                                ImpostorColor = _planets[i].ImpostorColor,
                            };
                            _planets[j] = new PlanetData
                            {
                                Position      = _planets[j].Position + push,
                                Config        = _planets[j].Config,
                                ImpostorColor = _planets[j].ImpostorColor,
                            };
                        }
                    }
                }
            }
        }

        // ── Update principal ──────────────────────────────────

        private void Update()
        {
            if (_player == null || _sphereMesh == null || _planets == null || _impostorMaterials == null) return;

            // Résolution de la caméra active — identique à AsteroidLOD.
            // Couvre le cas vaisseau : la caméra du ship devient active, pas le corps du joueur.
            if (_activeCamera == null || !_activeCamera.isActiveAndEnabled)
                _activeCamera = ResolveActiveCamera();

            // Référence de position : caméra active en priorité, sinon corps du joueur
            Vector3 refPos = (_activeCamera != null)
                ? _activeCamera.transform.position
                : _player.position;

            // ── Hot loop optimisé : tout en distance² (zéro sqrt) ─
            // 512 planètes × 1 sqMag + branches = ~quelques µs/frame.
            const float impostorRangeSq = ImpostorRange * ImpostorRange;

            int   closest    = -1;
            float closestSqD = float.MaxValue;
            float refX = refPos.x, refY = refPos.y, refZ = refPos.z;

            for (int i = 0; i < _planets.Length; i++)
            {
                Vector3 p   = _planets[i].Position;
                float   dx  = p.x - refX;
                float   dy  = p.y - refY;
                float   dz  = p.z - refZ;
                float   sqd = dx * dx + dy * dy + dz * dz;

                // Seuil voxel-activate au carré (par planète)
                float radius      = _planets[i].Config.CoreRadius;
                float activateD   = radius + VoxelActivateBuffer;
                float activateDSq = activateD * activateD;

                // Dessine l'impostor si visible et que la planète voxel n'est pas
                // déjà active à courte distance (évite le double-rendu).
                if (sqd < impostorRangeSq && !(_activeIndex == i && sqd < activateDSq))
                {
                    int bIdx = (int)_planets[i].Config.Biome;
                    Graphics.DrawMesh(
                        _sphereMesh,
                        Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * (radius * ImpostorScale)),
                        _impostorMaterials[bIdx],
                        0);
                }

                if (sqd < closestSqD)
                {
                    closestSqD = sqd;
                    closest    = i;
                }
            }

            // ── Gestion de la planète voxel (par-planète, hystérésis) ─
            if (closest >= 0)
            {
                float closestR    = _planets[closest].Config.CoreRadius;
                float actDist     = closestR + VoxelActivateBuffer;
                float deactDist   = closestR + VoxelDeactivateBuffer;
                float actSq       = actDist   * actDist;
                float deactSq     = deactDist * deactDist;

                if (closestSqD < actSq && closest != _activeIndex)
                {
                    ActivateVoxelPlanet(closest);
                }
                else if (_activeIndex >= 0 && closestSqD > deactSq)
                {
                    DeactivateVoxelPlanet();
                }
            }
        }

        /// <summary>
        /// Trouve la caméra active : d'abord Camera.main, sinon la première caméra
        /// activée dans la scène (couvre le cas où la shipCamera n'est pas MainCamera).
        /// Identique à AsteroidLOD.ResolveActiveCamera().
        /// </summary>
        private static Camera ResolveActiveCamera()
        {
            var main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;

            var all = Camera.allCameras;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].isActiveAndEnabled)
                    return all[i];

            return null;
        }

        // ── Activation / déactivation planète voxel ──────────

        private void ActivateVoxelPlanet(int index)
        {
            DeactivateVoxelPlanet();
            _activeIndex = index;

            var entry = _planets[index];

            _activeGO = new GameObject($"Planet_{index}_{entry.Config.Biome}");
            _activeGO.transform.position = entry.Position;

            // Gravité surfacique.
            // InfluenceRadius fini → traité comme un très grand astéroïde par
            // GravityBody.FindBestAttractor() et SpaceShipController.ApplyAsteroidGravity().
            // Force = même que la planète de base (13.54 m/s²), portée ∝ rayon.
            var attractor = _activeGO.AddComponent<GravityAttractor>();
            attractor.SetAsteroidParams(
                force:     13.54f,
                influence: entry.Config.CoreRadius * 10f + 300f);

            // Couche d'ozone adaptée au rayon de cette planète
            var ozoneLayer = _activeGO.AddComponent<OzoneLayer>();
            ozoneLayer.SetCoreRadius(entry.Config.CoreRadius);

            // Atmosphère visuelle : ciel + anneau d'ozone
            var atmGO = new GameObject("Atmosphere");
            atmGO.transform.SetParent(_activeGO.transform, false);
            atmGO.transform.localPosition = Vector3.zero;
            var atm = atmGO.AddComponent<AtmosphereRenderer>();
            atm.Init(_player, ozoneLayer);

            // Monde voxel
            _activeWorld = _activeGO.AddComponent<PlanetWorld>();
            _activeWorld.planetRadius     = entry.Config.CoreRadius;
            // Re-fetch depuis Resources pour garantir que les matériaux sont valides
            var reg = UnityEngine.Resources.Load<BlockTextureRegistry>("BlockTextureRegistry");
            _activeWorld.blockMaterials   = (reg != null && reg.materials != null && reg.materials.Length == 256)
                ? reg.materials
                : _blockMaterials;
            _activeWorld.generationConfig = entry.Config;
            _activeWorld.manualLoad       = true;    // empêche Start() de charger sync

            _activeWorld.SetViewer(_player);

            // Monde pas encore prêt pour le joueur (les mods seront appliquées async)
            _activeWorldReady = false;

            // Chargement async pour éviter le freeze + application des mods en attente
            _loadCoroutine = StartCoroutine(LoadPlanetAndApplyMods(index));
        }

        private IEnumerator LoadPlanetAndApplyMods(int index)
        {
            // Attendre la fin du chargement async (coroutine sur le PlanetWorld)
            yield return _activeWorld.StartCoroutine(_activeWorld.LoadChunksAsync(AsyncChunksPerFrame));

            // Appliquer les modifications en attente si elles existent
            if (_activeWorld != null
                && _pendingPlanetMods.TryGetValue(index, out var mods)
                && mods.Count > 0)
            {
                _activeWorld.ApplyModifications(mods);
                // WaitForFixedUpdate garantit que PhysX a terminé le bake asynchrone
                // des MeshColliders modifiés avant que le joueur/vaisseau soient dégelés.
                // (yield return null ne suffit pas : le bake tourne en background thread
                //  et n'est appliqué qu'avant la prochaine simulation physique.)
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
            }

            // Le monde est prêt — SaveSystem peut maintenant téléporter le joueur.
            _activeWorldReady = true;
        }

        private void DeactivateVoxelPlanet()
        {
            if (_loadCoroutine != null)
            {
                // La coroutine tourne sur 'this' (pas sur _activeWorld)
                StopCoroutine(_loadCoroutine);
                _loadCoroutine = null;
            }

            // Sauvegarder les modifications avant de détruire le monde
            if (_activeWorld != null)
            {
                var mods = _activeWorld.GetModifications();
                if (mods.Count > 0)
                    _pendingPlanetMods[_activeIndex] = mods;
            }

            if (_activeGO != null)
            {
                Destroy(_activeGO);
                _activeGO    = null;
                _activeWorld = null;
            }

            _activeIndex      = -1;
            _activeWorldReady = true;   // plus de monde en attente
        }

        // ── Ressources de rendu ───────────────────────────────

        private void BuildSphereMesh()
        {
            _sphereMesh = new Mesh { name = "PlanetImpostor" };

            const int lat = 18;
            const int lon = 24;
            int stride    = lon + 1;

            var verts = new List<Vector3>((lat + 1) * stride);
            var tris  = new List<int>(lat * lon * 6);
            var uvs   = new List<Vector2>((lat + 1) * stride);

            for (int la = 0; la <= lat; la++)
            {
                float theta = Mathf.PI * la / lat;
                for (int lo = 0; lo <= lon; lo++)
                {
                    float phi = 2f * Mathf.PI * lo / lon;
                    float sx  = Mathf.Sin(theta) * Mathf.Cos(phi);
                    float sy  = Mathf.Cos(theta);
                    float sz  = Mathf.Sin(theta) * Mathf.Sin(phi);
                    verts.Add(new Vector3(sx, sy, sz));
                    uvs.Add(new Vector2((float)lo / lon, (float)la / lat));
                }
            }

            for (int la = 0; la < lat; la++)
            for (int lo = 0; lo < lon; lo++)
            {
                int v0 = la * stride + lo;
                int v1 = v0 + 1;
                int v2 = v0 + stride;
                int v3 = v2 + 1;
                tris.Add(v0); tris.Add(v2); tris.Add(v1);
                tris.Add(v1); tris.Add(v2); tris.Add(v3);
            }

            _sphereMesh.SetVertices(verts);
            _sphereMesh.SetTriangles(tris, 0);
            _sphereMesh.SetUVs(0, uvs);
            _sphereMesh.RecalculateNormals();
            _sphereMesh.RecalculateBounds();
        }

        private void BuildImpostorMaterials()
        {
            _impostorMaterials = new Material[BiomeColors.Length];
            // Shader translucide dédié → impostors discrets, n'occultent ni le ciel
            // ni les étoiles. Fallback compatible si introuvable.
            var shader = Shader.Find("AstroVoxel/PlanetImpostor")
                      ?? Shader.Find("AstroVoxel/BlockUnlit")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color");

            // Alpha bas → sphère discrète, suggère un volume sans masquer
            const float ImpostorAlpha = 0.32f;

            for (int i = 0; i < BiomeColors.Length; i++)
            {
                var c = BiomeColors[i];
                var mat = new Material(shader)
                {
                    name = $"PlanetImpostor_{(PlanetBiome)i}"
                };
                var tint = new Color(c.r, c.g, c.b, ImpostorAlpha);
                mat.color = tint;
                if (mat.HasProperty("_Color"))    mat.SetColor("_Color", tint);
                if (mat.HasProperty("_RimColor")) mat.SetColor("_RimColor", Color.Lerp(c, Color.white, 0.6f));
                _impostorMaterials[i] = mat;
            }
        }

        // ── Save / Load (pour SaveSystem) ─────────────────────

        /// <summary>
        /// Collecte toutes les modifications planètes infinies (planète active + buffer).
        /// Appelé par <see cref="AstroVoxel.Save.SaveSystem"/> lors d'une sauvegarde.
        /// </summary>
        public List<InfinitePlanetModEntry> CollectModifications()
        {
            // Mettre à jour le buffer avec les mods de la planète active
            if (_activeWorld != null && _activeIndex >= 0)
            {
                var activeMods = _activeWorld.GetModifications();
                if (activeMods.Count > 0)
                    _pendingPlanetMods[_activeIndex] = activeMods;
            }

            var result = new List<InfinitePlanetModEntry>();
            foreach (var kv in _pendingPlanetMods)
            {
                if (kv.Value != null && kv.Value.Count > 0)
                    result.Add(new InfinitePlanetModEntry { planetIndex = kv.Key, mods = kv.Value });
            }
            return result;
        }

        /// <summary>
        /// Charge les modifications en attente depuis une sauvegarde.
        /// Appelé par <see cref="AstroVoxel.Save.SaveSystem"/> lors du chargement.
        /// Les mods seront appliquées après le prochain LoadChunksAsync.
        /// </summary>
        public void LoadPendingMods(List<InfinitePlanetModEntry> entries)
        {
            if (entries == null) return;
            foreach (var entry in entries)
            {
                if (entry?.mods != null && entry.mods.Count > 0)
                    _pendingPlanetMods[entry.planetIndex] = entry.mods;
            }
        }

        // ── Accesseur public (pour debug / console) ───────────

        /// <summary>Retourne la config de la planète la plus proche (pour debug).</summary>
        public bool TryGetClosestPlanet(Vector3 from, out PlanetGenerationConfig cfg, out Vector3 pos)
        {
            float best = float.MaxValue;
            int   idx  = -1;
            for (int i = 0; i < _planets.Length; i++)
            {
                float d = (from - _planets[i].Position).sqrMagnitude;
                if (d < best) { best = d; idx = i; }
            }

            if (idx >= 0)
            {
                cfg = _planets[idx].Config;
                pos = _planets[idx].Position;
                return true;
            }

            cfg = default;
            pos = Vector3.zero;
            return false;
        }
    }
}

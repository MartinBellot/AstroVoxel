// ============================================================
//  GameBootstrap.cs
//  Crée toute la scène par code : planète, joueur, caméra.
//  Attache ce script à un GameObject vide "Bootstrap" dans la scène.
//  C'est le seul MonoBehaviour à placer manuellement.
// ============================================================

using UnityEngine;
using UnityEngine.UI;
using AstroVoxel.VoxelEngine;
using AstroVoxel.Physics;
using AstroVoxel.Player;
using AstroVoxel.Environment;
using AstroVoxel.Vehicle;
using AstroVoxel.Space;

namespace AstroVoxel.Bootstrap
{
    /// <summary>
    /// Point d'entrée de la scène.
    /// Construit la hiérarchie complète : Planète → Joueur → Caméra.
    /// Câble toutes les références entre composants.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────
        [Header("Planète")]
        [SerializeField] private float planetRadius = 60f;

        [Header("Joueur")]
        [SerializeField] private float playerHeight  = 1.8f;
        [SerializeField] private float playerRadius  = 0.4f;
        [SerializeField] private float spawnAltitude = 10f;   // blocs au-dessus de la surface

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            // ── Seed globale du monde ─────────────────────────
            WorldSeedManager.Initialize();  // aléatoire au premier lancement, conservée après /restart

            // ── Skybox spatiale ───────────────────────────────
            BuildEnvironment();

            // ── Soleil orbital ──────────────────────────
            var sunOrbit = BuildSun();

            var builtMaterials = LoadBlockMaterials();
            BuildPlanet(out PlanetWorld world, out GravityAttractor attractor, builtMaterials);
            BuildPlayer(world, attractor, builtMaterials, out Transform playerBody, out Camera playerCam);

            // Injecte le joueur dans les systèmes qui en ont besoin
            sunOrbit.SetPlayer(playerBody);
            BuildAtmosphere(playerBody);

            // Vaisseau spatial (spawn au sol, à côté du joueur)
            BuildSpaceShip(playerBody, playerCam, world);

            // Système d'astéroïdes et météorites
            BuildAsteroidSystem(playerBody, builtMaterials, sunOrbit);

            // Système de planètes infinies
            BuildInfinitePlanets(playerBody, builtMaterials);

            world.SetViewer(playerBody);

            // Force le chargement synchrone des chunks AVANT que la physique ne simule
            // le premier FixedUpdate, sinon le joueur traverse la planète.
            world.UpdateChunks();
            UnityEngine.Physics.SyncTransforms();
        }

        // ── Construction du soleil ──────────────────────────

        private static SunOrbit BuildSun()
        {
            var sunGO = new GameObject("Sun");
            return sunGO.AddComponent<SunOrbit>();
        }

        // ── Construction de l'atmosphère ─────────────────

        private static void BuildAtmosphere(Transform player)
        {
            var atmGO = new GameObject("Atmosphere");
            atmGO.transform.position = Vector3.zero;  // centré sur la planète
            var atm = atmGO.AddComponent<AtmosphereRenderer>();
            atm.Init(player);
        }

        // ── Construction de l'environnement (skybox) ──────────

        private static void BuildEnvironment()
        {
            var envGO = new GameObject("Environment");
            envGO.AddComponent<SpaceSkyboxController>();
        }

        // ── Construction de la planète ────────────────────────

        private void BuildPlanet(out PlanetWorld world, out GravityAttractor attractor, Material[] builtMaterials)
        {
            var planetGO = new GameObject("Planet");
            planetGO.transform.position = Vector3.zero;

            attractor = planetGO.AddComponent<GravityAttractor>();

            // Couche d'ozone : frontière atmosphère / espace
            // (GravityBody la trouve automatiquement via FindAnyObjectByType)
            planetGO.AddComponent<OzoneLayer>();

            world = planetGO.AddComponent<PlanetWorld>();
            world.planetRadius       = planetRadius;
            world.blockMaterials     = builtMaterials;
            // Applique la config seed-based pour que le terrain change avec la seed
            world.generationConfig   = PlanetGenerationConfig.HomePlanet(WorldSeedManager.Seed);
        }

        /// <summary>
        /// Charge les matériaux de blocs depuis la BlockTextureRegistry (Assets/Resources/).
        /// Repli sur des matériaux gris si la registry n'est pas encore construite
        /// (lancer AstroVoxel → Rebuild Block Texture Registry dans l'éditeur).
        /// </summary>
        private static Material[] LoadBlockMaterials()
        {
            var registry = UnityEngine.Resources.Load<AstroVoxel.VoxelEngine.BlockTextureRegistry>("BlockTextureRegistry");
            if (registry != null && registry.materials != null && registry.materials.Length == 256)
                return registry.materials;

            // Repli : 256 matériaux gris
            Debug.LogWarning("[GameBootstrap] BlockTextureRegistry introuvable. " +
                             "Lancez AstroVoxel → Rebuild Block Texture Registry dans l'éditeur Unity.");
            var mats = new Material[256];
            for (int i = 0; i < 256; i++)
                mats[i] = CreateDefaultMaterial(new Color(0.5f, 0.45f, 0.4f));
            return mats;
        }

        // ── Construction du joueur ────────────────────────────

        private void BuildPlayer(
            PlanetWorld world,
            GravityAttractor attractor,
            Material[] builtMaterials,
            out Transform playerBody,
            out Camera playerCam)
        {
            // ── Corps du joueur ───────────────────────────────
            var playerGO = new GameObject("Player");
            playerGO.layer = LayerMask.NameToLayer("Player") >= 0
                ? LayerMask.NameToLayer("Player") : 0;

            // Position de spawn : au-dessus du pôle nord de la planète.
            // La surface est à PlanetCoreRadius ± SurfaceAmplitude/2 du centre.
            float surfaceApprox = PlanetChunkGenerator.PlanetCoreRadius + 2f;
            float spawnDist = surfaceApprox + spawnAltitude;
            // Spawn à l'équateur (+X) pour valider l'alignement des blocs latéraux.
            // Revenir à Vector3.up pour le spawn normal au pôle nord.
            Vector3 spawnDir = Vector3.right;
            playerGO.transform.position = spawnDir * spawnDist;
            playerGO.transform.up       = spawnDir;

            // Capsule collider
            var capsule = playerGO.AddComponent<CapsuleCollider>();
            capsule.height = playerHeight;
            capsule.radius = playerRadius;
            capsule.center = Vector3.up * (playerHeight * 0.5f);

            // Rigidbody
            var rb = playerGO.AddComponent<Rigidbody>();
            rb.useGravity     = false;
            rb.freezeRotation = true;
            rb.mass           = 70f;
            // Pas d'amortissement Unity : PlayerController applique sa propre friction
            // pour stopper net dès que les touches sont relâchées (anti-dérive).
            rb.linearDamping  = 0f;
            rb.angularDamping = 0.05f;
            rb.interpolation  = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            // GravityBody
            var gravBody = playerGO.AddComponent<GravityBody>();

            // PlayerController
            var controller = playerGO.AddComponent<PlayerController>();

            // ── Caméra ────────────────────────────────────────
            var cameraGO = new GameObject("PlayerCamera");
            cameraGO.transform.SetParent(playerGO.transform);
            cameraGO.transform.localPosition = new Vector3(0f, playerHeight * 0.9f, 0f);
            cameraGO.transform.localRotation = Quaternion.identity;

            playerCam = cameraGO.AddComponent<Camera>();
            playerCam.nearClipPlane = 0.1f;
            playerCam.farClipPlane  = 150_000f;  // doit couvrir toutes les planètes (ImpostorRange 80 000u)
            playerCam.fieldOfView   = 70f;

            // Active la post-processing URP (requis pour que Bloom/autres effets s'appliquent)
            var urpData = cameraGO.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (urpData == null)
                urpData = cameraGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            urpData.renderPostProcessing = true;

            // AudioListener sur la caméra
            cameraGO.AddComponent<AudioListener>();

            // PlayerCamera (mouse look)
            var camScript = cameraGO.AddComponent<PlayerCamera>();
            camScript.SetPlayerBody(playerGO.transform);

            // BlockInteraction
            var blockInteract = playerGO.AddComponent<BlockInteraction>();
            blockInteract.Init(playerCam, world);

            // Câblage final
            controller.SetCamera(cameraGO.transform);

            playerBody = playerGO.transform;

            // ── Cube de sélection 3D (à la Minecraft) ───────────────
            var highlight = BuildBlockHighlight();
            blockInteract.InitHighlight(highlight);

            // ── HUD : crosshair + hotbar + overlay ────────────────
            BuildHUD(blockInteract, playerGO.transform, builtMaterials);
        }

        private static void CreateCrosshairBar(Transform parent, Vector2 size)
        {
            var go  = new GameObject("CrosshairBar");
            go.transform.SetParent(parent, false);

            var img  = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.85f);

            var rt   = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
        }

        private void BuildPlayerInternal() { } // dummy pour fermer la région
        private static void BuildCrosshair()
        {
            // Canvas en Screen Space Overlay
            var canvasGO = new GameObject("HUD");
            var canvas   = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // Crosshair : deux barres blanches semi-transparentes
            CreateCrosshairBar(canvasGO.transform, new Vector2(20f, 2f));  // horizontal
            CreateCrosshairBar(canvasGO.transform, new Vector2(2f, 20f));  // vertical
        }
        // ── Cube de sélection 3D ──────────────────────────────

        private static Transform BuildBlockHighlight()
        {
            var go = new GameObject("BlockHighlight");
            // BlockWireframe dessine les arêtes via GL (Hidden/Internal-Colored)
            // → fonctionne avec tous les pipelines, aucun souci de shader pink.
            go.AddComponent<BlockWireframe>();
            go.SetActive(false);
            return go.transform;
        }

        // ── HUD ─────────────────────────────────────────────

        private static void BuildHUD(BlockInteraction blockInteract, Transform playerBody, Material[] builtMaterials)
        {
            // EventSystem (indispensable pour InputField, ScrollRect, EventTrigger…)
            EnsureEventSystem();

            // Canvas Screen Space Overlay
            var canvasGO = new GameObject("HUD");
            var canvas   = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // HUD Apple Dark Theme (crosshair + hotbar + info panel)
            var hudBuilderGO = new GameObject("HudBuilder");
            hudBuilderGO.transform.SetParent(canvasGO.transform, false);
            var hud = hudBuilderGO.AddComponent<HudBuilder>();
            hud.Init(canvas, blockInteract, playerBody, builtMaterials);

            // Inventaire créatif (touche E)
            // On passe les RectTransforms des slots hotbar pour le drag&drop ciblé par slot.
            var invGO = new GameObject("CreativeInventory");
            invGO.transform.SetParent(canvasGO.transform, false);
            var inv = invGO.AddComponent<CreativeInventory>();
            inv.Init(canvas, blockInteract, builtMaterials, hud.HotbarSlotRects);

            // Console de commandes (touche T ou /)
            var consoleGO = new GameObject("GameConsole");
            consoleGO.transform.SetParent(canvasGO.transform, false);
            var console = consoleGO.AddComponent<GameConsole>();
            console.Init(canvas, blockInteract);
        }

        // ── EventSystem ─────────────────────────────────────

        private static void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;

            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
            esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }

        // ── Construction du vaisseau spatial ─────────────────

        private static void BuildSpaceShip(Transform playerBody, Camera playerCam, PlanetWorld world)
        {
            var shipGO = new GameObject("SpaceShip");

            // Spawn juste au-dessus de la surface, légèrement décalé du joueur
            float surfaceDist = PlanetChunkGenerator.PlanetCoreRadius + 4f;
            Vector3 spawnDir  = (Vector3.up * 0.98f + Vector3.right * 0.2f).normalized;
            shipGO.transform.position = spawnDir * surfaceDist;

            // Orienter le vaisseau tangent à la surface (nez pointe vers +forward planétaire)
            Vector3 planetUp   = spawnDir;
            Vector3 shipForward = Vector3.Cross(planetUp, Vector3.forward).normalized;
            if (shipForward.sqrMagnitude < 0.01f)
                shipForward = Vector3.Cross(planetUp, Vector3.right).normalized;
            shipGO.transform.rotation = Quaternion.LookRotation(shipForward, planetUp);

            // Rigidbody
            var rb                    = shipGO.AddComponent<Rigidbody>();
            rb.useGravity             = false;
            rb.freezeRotation         = false;
            rb.mass                   = 1000f;
            rb.linearDamping          = 0f;
            rb.angularDamping         = 1.5f;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Collider fuselage
            var box    = shipGO.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size   = new Vector3(2f, 1.2f, 5f);

            // ── Modèle visuel (primitives) ───────────────────
            var model = new GameObject("Model");
            model.transform.SetParent(shipGO.transform, false);

            AddShipPart(model.transform, PrimitiveType.Cube,     "Hull",
                Vector3.zero,                  new Vector3(2f,  1f,  5f),  new Color(0.25f, 0.27f, 0.32f));
            AddShipPart(model.transform, PrimitiveType.Cube,     "WingLeft",
                new Vector3(-3.5f, -0.2f, 0f), new Vector3(5f,  0.2f, 2.5f), new Color(0.22f, 0.24f, 0.30f));
            AddShipPart(model.transform, PrimitiveType.Cube,     "WingRight",
                new Vector3( 3.5f, -0.2f, 0f), new Vector3(5f,  0.2f, 2.5f), new Color(0.22f, 0.24f, 0.30f));
            AddShipPart(model.transform, PrimitiveType.Cube,     "Cockpit",
                new Vector3(0f, 0.65f, 1.5f),  new Vector3(1.2f, 0.6f, 1.4f), new Color(0.4f, 0.75f, 1f));

            var engineGO = AddShipPart(model.transform, PrimitiveType.Cylinder, "Engine",
                new Vector3(0f, 0f, -2.8f),    new Vector3(0.6f, 0.8f, 0.6f), new Color(0.15f, 0.15f, 0.18f));
            engineGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // ── Caméra 3e personne ───────────────────────────
            var camGO = new GameObject("ShipCamera");
            camGO.transform.SetParent(shipGO.transform, false);

            var shipCam             = camGO.AddComponent<Camera>();
            shipCam.nearClipPlane   = 0.5f;
            shipCam.farClipPlane    = 150_000f;  // idem caméra joueur
            shipCam.fieldOfView     = 65f;
            shipCam.clearFlags      = CameraClearFlags.Skybox;

            var urp = camGO.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (urp == null)
                urp = camGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;

            camGO.AddComponent<AudioListener>();

            var camScript  = camGO.AddComponent<SpaceShipCamera>();
            camScript.target = shipGO.transform;

            // Caméra vaisseau inactive au départ (joueur utilise sa propre caméra)
            camGO.SetActive(false);

            // ── Point de sortie ──────────────────────────────
            var exit = new GameObject("ExitPoint");
            exit.transform.SetParent(shipGO.transform, false);
            exit.transform.localPosition = new Vector3(2.5f, 1.5f, 0f);   // côté droit

            // ── Contrôleur ───────────────────────────────────
            var ctrl        = shipGO.AddComponent<SpaceShipController>();
            ctrl.exitPoint  = exit.transform;
            ctrl.shipCamera = shipCam;
            ctrl.SetPlayerReferences(playerBody, playerCam);
            ctrl.SetPlanetWorld(world);
        }

        private static GameObject AddShipPart(
            Transform parent,
            PrimitiveType type,
            string partName,
            Vector3 localPos,
            Vector3 localScale,
            Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = partName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = localScale;

            // Pas de collider sur les pièces visuelles
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);

            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                var mat = CreateDefaultMaterial(color);
                rend.sharedMaterial = mat;
            }

            return go;
        }

        private static Material CreateDefaultMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }

        // ── Système d'astéroïdes ──────────────────────────────

        private static void BuildAsteroidSystem(
            Transform  player,
            Material[] blockMaterials,
            SunOrbit sunOrbit)
        {
            var go = new GameObject("AsteroidSystem");
            var mgr = go.AddComponent<AstroVoxel.Space.AsteroidSystemManager>();

            // Position du soleil : direction × rayon orbital
            Vector3 sunPos = sunOrbit != null
                ? sunOrbit.SunDirection * 800f
                : Vector3.right * 800f;

            mgr.Init(player, blockMaterials, sunPos);
        }

        // ── Système de planètes infinies ──────────────────────

        private static void BuildInfinitePlanets(Transform player, Material[] blockMaterials)
        {
            var go  = new GameObject("InfinitePlanetSystem");
            var sys = go.AddComponent<InfinitePlanetSystem>();
            sys.Init(player, blockMaterials);
        }
    }
}

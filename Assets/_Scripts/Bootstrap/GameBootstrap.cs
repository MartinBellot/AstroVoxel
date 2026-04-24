// ============================================================
//  GameBootstrap.cs
//  Crée toute la scène par code : planète, joueur, caméra.
//  Attache ce script à un GameObject vide "Bootstrap" dans la scène.
//  C'est le seul MonoBehaviour à placer manuellement.
// ============================================================

using System;
using UnityEngine;
using UnityEngine.UI;
using AstroVoxel.VoxelEngine;
using AstroVoxel.Physics;
using AstroVoxel.Player;

namespace AstroVoxel.Bootstrap
{
    /// <summary>
    /// Association d'un type de bloc à son matériau Unity.
    /// Renseigner dans l'Inspector du Bootstrap.
    /// </summary>
    [Serializable]
    public struct BlockMaterialEntry
    {
        public BlockType blockType;
        public Material  material;
    }

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

        [Header("Matériaux par bloc")]
        [Tooltip("Un entrée par type de bloc. Les types non renseignés reçoivent un matériau gris par défaut.")]
        [SerializeField] private BlockMaterialEntry[] blockMaterials;

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            BuildPlanet(out PlanetWorld world, out GravityAttractor attractor);
            BuildPlayer(world, attractor, out Transform playerBody, out Camera playerCam);

            world.SetViewer(playerBody);

            // Force le chargement synchrone des chunks AVANT que la physique ne simule
            // le premier FixedUpdate, sinon le joueur traverse la planète.
            world.UpdateChunks();
            UnityEngine.Physics.SyncTransforms();
        }

        // ── Construction de la planète ────────────────────────

        private void BuildPlanet(out PlanetWorld world, out GravityAttractor attractor)
        {
            var planetGO = new GameObject("Planet");
            planetGO.transform.position = Vector3.zero;

            // GravityAttractor
            attractor = planetGO.AddComponent<GravityAttractor>();

            // PlanetWorld
            world = planetGO.AddComponent<PlanetWorld>();
            world.planetRadius    = planetRadius;
            world.blockMaterials  = BuildBlockMaterialArray();
        }

        /// <summary>
        /// Assemble un tableau Material[] indexé par (byte)BlockType.
        /// Les types non renseignés reçoivent un matériau gris par défaut.
        /// </summary>
        private Material[] BuildBlockMaterialArray()
        {
            int count = Enum.GetValues(typeof(BlockType)).Length;
            var mats  = new Material[count];

            // Remplit tout avec un matériau gris par défaut
            for (int i = 0; i < count; i++)
                mats[i] = CreateDefaultMaterial(new Color(0.5f, 0.45f, 0.4f));

            // Surcharge avec les matériaux assignés dans l'Inspector
            if (blockMaterials != null)
                foreach (var entry in blockMaterials)
                    if (entry.material != null)
                        mats[(int)entry.blockType] = entry.material;

            return mats;
        }

        // ── Construction du joueur ────────────────────────────

        private void BuildPlayer(
            PlanetWorld world,
            GravityAttractor attractor,
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
            playerGO.transform.position = Vector3.up * spawnDist;
            playerGO.transform.up       = Vector3.up;

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
            rb.linearDamping  = 0.5f;
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
            playerCam.farClipPlane  = 1000f;
            playerCam.fieldOfView   = 70f;

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
            BuildHUD(blockInteract, playerGO.transform);
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

        private static void BuildHUD(BlockInteraction blockInteract, Transform playerBody)
        {
            // Canvas Screen Space Overlay
            var canvasGO = new GameObject("HUD");
            var canvas   = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // Crosshair
            CreateCrosshairBar(canvasGO.transform, new Vector2(20f, 2f));
            CreateCrosshairBar(canvasGO.transform, new Vector2(2f, 20f));

            // Hotbar (slots + label)
            blockInteract.InitHotbar(canvas);

            // Overlay FPS / XYZ / Bloc
            var hudGO = new GameObject("HudOverlay");
            hudGO.transform.SetParent(canvasGO.transform, false);
            var overlay = hudGO.AddComponent<HudOverlay>();
            overlay.Init(playerBody, blockInteract, canvas);
        }

        private static Material CreateDefaultMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }
    }
}

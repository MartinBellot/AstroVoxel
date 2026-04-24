// ============================================================
//  GameBootstrap.cs
//  Crée toute la scène par code : planète, joueur, caméra.
//  Attache ce script à un GameObject vide "Bootstrap" dans la scène.
//  C'est le seul MonoBehaviour à placer manuellement.
// ============================================================

using UnityEngine;
using AstroVoxel.VoxelEngine;
using AstroVoxel.Physics;
using AstroVoxel.Player;

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
        [SerializeField] private float planetRadius = 80f;

        [Header("Joueur")]
        [SerializeField] private float playerHeight  = 1.8f;
        [SerializeField] private float playerRadius  = 0.4f;
        [SerializeField] private float spawnAltitude = 10f;   // blocs au-dessus de la surface

        [Header("Matériau des chunks (optionnel)")]
        [SerializeField] private Material chunkMaterial;

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
            world.planetRadius         = planetRadius;
            world.chunkMaterial        = chunkMaterial != null
                ? chunkMaterial
                : CreateDefaultMaterial();

            // Sphère visuelle de référence (wireframe en edit mode)
#if UNITY_EDITOR
            var debugSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            debugSphere.name = "PlanetDebugSphere";
            debugSphere.transform.SetParent(planetGO.transform);
            debugSphere.transform.localScale = Vector3.one * planetRadius * 2f;
            Destroy(debugSphere.GetComponent<Collider>());
            var mr = debugSphere.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var c = Color.blue; c.a = 0.1f;
            mat.color = c;
            // Transparent mode
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.renderQueue = 3000;
            mr.sharedMaterial = mat;
#endif
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
        }

        // ── Utilitaires ───────────────────────────────────────

        private static Material CreateDefaultMaterial()
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            // Couleur grise neutre en attendant l'atlas de textures
            mat.color = new Color(0.5f, 0.45f, 0.4f);
            return mat;
        }
    }
}

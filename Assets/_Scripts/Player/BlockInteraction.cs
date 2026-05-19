// ============================================================
//  BlockInteraction.cs
//  Clic gauche = casser un bloc, Clic droit = placer un bloc.
//  Utilise un Raycast depuis la caméra vers le monde.
// ============================================================

using UnityEngine;
using AstroVoxel.VoxelEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AstroVoxel.Player
{
    /// <summary>
    /// Gère l'interaction joueur ↔ voxels :
    /// - Clic gauche  : détruit le bloc visé
    /// - Clic droit   : place un bloc sur la face visée
    /// Dépend de <see cref="PlanetWorld"/> pour modifier les chunks.
    /// </summary>
    public sealed class BlockInteraction : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────
        [Header("Portée d'action")]
        [SerializeField] private float reach = 6f;

        [Header("Bloc à placer")]
        [SerializeField] private BlockType blockToPlace = BlockType.Stone;

        [Header("Références")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private PlanetWorld world;

        // ── Visualisation (preview du bloc sélectionné) ───────
        [Header("Highlight (optionnel)")]
        [SerializeField] private Transform blockHighlight;   // cube semi-transparent

        // (hotbar gérée par HudBuilder)

        // ── Cycle de vie ──────────────────────────────────────

        private void Update()
        {
            // Bloque toute interaction quand l'inventaire ou la console est ouvert(e)
            if (CreativeInventory.IsOpen || GameConsole.IsOpen) return;

            if (GetMouseDown(0))   // Clic gauche
                TryBreakBlock();

            if (GetMouseDown(1))   // Clic droit
                TryPlaceBlock();

            // Scroll ou touches pour changer le bloc actif
            HandleBlockSelection();
        }

        // Le highlight est mis à jour en LateUpdate, APRÈS que le
        // character controller planétaire a appliqué sa rotation
        // gravitationnelle. Sans ça, la rotation du joueur/caméra
        // (parent du highlight) écrase la world-rotation qu'on vient
        // de poser dans Update, causant un décalage total dès qu'on bouge.
        private void LateUpdate() => UpdateHighlight();

        // ── Actions ───────────────────────────────────────────

        private void TryBreakBlock()
        {
            if (!Raycast(out RaycastHit hit)) return;
            Vector3 pos = hit.point - hit.normal * 0.5f;

            // Astéroïde
            var ast = hit.collider?.GetComponentInParent<AstroVoxel.Space.AsteroidWorld>();
            if (ast != null) { ast.BreakBlock(pos); return; }

            // Planète infinie (PlanetWorld créé dynamiquement par InfinitePlanetSystem)
            var pw = hit.collider?.GetComponentInParent<PlanetWorld>();
            if (pw != null) { pw.BreakBlock(pos); return; }

            // Planète de base
            world?.BreakBlock(pos);
        }

        private void TryPlaceBlock()
        {
            if (!Raycast(out RaycastHit hit)) return;
            Vector3 pos = hit.point + hit.normal * 0.5f;

            // Astéroïde
            var ast = hit.collider?.GetComponentInParent<AstroVoxel.Space.AsteroidWorld>();
            if (ast != null) { ast.PlaceBlock(pos, blockToPlace); return; }

            // Planète infinie (PlanetWorld créé dynamiquement par InfinitePlanetSystem)
            var pw = hit.collider?.GetComponentInParent<PlanetWorld>();
            if (pw != null) { pw.PlaceBlock(pos, blockToPlace); return; }

            // Planète de base
            world?.PlaceBlock(pos, blockToPlace);
        }

        // ── Raycast ───────────────────────────────────────────

        private bool Raycast(out RaycastHit hit)
        {
            if (playerCamera == null)
            {
                hit = default;
                return false;
            }

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            return UnityEngine.Physics.Raycast(ray, out hit, reach,
                ~LayerMask.GetMask("Player"), QueryTriggerInteraction.Ignore);
        }

        // ── Highlight ───────────────────────────────────────────

        private Vector3? _targetBlockPos;

        /// <summary>Coin (0,0,0) du bloc actuellement visé, ou null si rien.</summary>
        public Vector3? TargetBlockPos => _targetBlockPos;

        private void UpdateHighlight()
        {
            if (Raycast(out RaycastHit hit))
            {
                Vector3 blockCenter = hit.point - hit.normal * 0.5f;

                // Priorité : chunk directement touché par le raycast (évite
                // l'ambiguïté GetFace aux coutures kSeamMargin).
                // Fallback : GetChunkAt si le collider n'est pas un chunk
                // (autre objet de la scène, terrain, etc.).
                ChunkRenderer cr = hit.collider != null
                    ? hit.collider.GetComponent<ChunkRenderer>()
                    : null;
                if (cr == null && world != null)
                    cr = world.GetChunkAt(hit.point - hit.normal * 0.5f);

                if (cr != null)
                {
                    // Convertit le point hit en espace LOCAL du chunk (gère
                    // rotation + échelle + hiérarchie de transforms).
                    Vector3 local = cr.transform.InverseTransformPoint(blockCenter);
                    int lx = Mathf.Clamp(Mathf.FloorToInt(local.x), 0, VoxelData.ChunkWidth  - 1);
                    int ly = Mathf.Clamp(Mathf.FloorToInt(local.y), 0, VoxelData.ChunkHeight - 1);
                    int lz = Mathf.Clamp(Mathf.FloorToInt(local.z), 0, VoxelData.ChunkDepth  - 1);

                    // Coin du bloc en world-space (le pivot du highlight est au coin).
                    Vector3 corner = cr.transform.TransformPoint(lx, ly, lz);
                    _targetBlockPos = corner;
                    if (blockHighlight != null)
                    {
                        blockHighlight.position = corner;
                        blockHighlight.rotation = cr.transform.rotation;
                        blockHighlight.gameObject.SetActive(true);
                    }
                }
                else
                {
                    // Repli si le collider n'est pas un chunk (ne devrait pas arriver)
                    _targetBlockPos = new Vector3(
                        Mathf.FloorToInt(blockCenter.x),
                        Mathf.FloorToInt(blockCenter.y),
                        Mathf.FloorToInt(blockCenter.z));
                    if (blockHighlight != null)
                    {
                        blockHighlight.position = _targetBlockPos.Value;
                        blockHighlight.gameObject.SetActive(true);
                    }
                }
            }
            else
            {
                _targetBlockPos = null;
                if (blockHighlight != null)
                    blockHighlight.gameObject.SetActive(false);
            }
        }

        // ── Sélection du bloc ─────────────────────────────────

        private static readonly BlockType[] _defaultHotbar = new BlockType[9]
        {
            BlockType.Stone, BlockType.Dirt, BlockType.Grass,
            BlockType.Sand, BlockType.Wood, BlockType.Leaves,
            BlockType.Cobblestone, BlockType.StoneBricks, BlockType.Bricks,
        };
        private BlockType[] _hotbar = (BlockType[])_defaultHotbar.Clone();
        private int _hotbarIndex;

        private void HandleBlockSelection()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.y.ReadValue();
                if (scroll > 0f)  _hotbarIndex = (_hotbarIndex + 1) % _hotbar.Length;
                if (scroll < 0f)  _hotbarIndex = (_hotbarIndex - 1 + _hotbar.Length) % _hotbar.Length;
            }
            var kb = Keyboard.current;
            if (kb != null)
                for (int i = 0; i < _hotbar.Length; i++)
                    if (kb[(Key)(Key.Digit1 + i)].wasPressedThisFrame)
                        _hotbarIndex = i;
#else
            float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (scroll > 0f)  _hotbarIndex = (_hotbarIndex + 1) % _hotbar.Length;
            if (scroll < 0f)  _hotbarIndex = (_hotbarIndex - 1 + _hotbar.Length) % _hotbar.Length;

            for (int i = 0; i < _hotbar.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    _hotbarIndex = i;
#endif
            blockToPlace = _hotbar[_hotbarIndex];
        }

        private static bool GetMouseDown(int button)
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null) return false;
            return button == 0 ? mouse.leftButton.wasPressedThisFrame
                 : button == 1 ? mouse.rightButton.wasPressedThisFrame
                 : false;
#else
            return Input.GetMouseButtonDown(button);
#endif
        }

        // ── Accesseurs ────────────────────────────────────────

        public BlockType   ActiveBlock  => blockToPlace;
        public int         HotbarIndex  => _hotbarIndex;
        public BlockType[] Hotbar       => _hotbar;

        /// <summary>
        /// Permet à l'inventaire créatif de remplacer un bloc dans la hotbar.
        /// </summary>
        public void SetHotbarSlot(int slot, BlockType t)
        {
            if (slot >= 0 && slot < _hotbar.Length)
                _hotbar[slot] = t;
        }

        /// <summary>
        /// Vide tous les slots de la hotbar (Air).
        /// Appelé par la console avec la commande /clear.
        /// </summary>
        public void ClearInventory()
        {
            for (int i = 0; i < _hotbar.Length; i++)
                _hotbar[i] = BlockType.Air;
            _hotbarIndex = 0;
            blockToPlace = BlockType.Air;
        }

        /// <summary>Assigne les références depuis GameBootstrap.</summary>
        public void Init(Camera cam, PlanetWorld w)
        {
            playerCamera = cam;
            world        = w;
        }

        /// <summary>Assigne le cube de sélection 3D créé par GameBootstrap.</summary>
        public void InitHighlight(Transform highlight)
        {
            blockHighlight = highlight;
            if (blockHighlight != null)
                blockHighlight.gameObject.SetActive(false);
        }


    }
}

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
            UpdateHighlight();

            if (GetMouseDown(0))   // Clic gauche
                TryBreakBlock();

            if (GetMouseDown(1))   // Clic droit
                TryPlaceBlock();

            // Scroll ou touches pour changer le bloc actif
            HandleBlockSelection();


        }

        // ── Actions ───────────────────────────────────────────

        private void TryBreakBlock()
        {
            if (!Raycast(out RaycastHit hit)) return;
            world.BreakBlock(hit.point - hit.normal * 0.5f);
        }

        private void TryPlaceBlock()
        {
            if (!Raycast(out RaycastHit hit)) return;
            // Place légèrement au-dessus de la face touchée
            world.PlaceBlock(hit.point + hit.normal * 0.5f, blockToPlace);
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
            else
            {
                _targetBlockPos = null;
                if (blockHighlight != null)
                    blockHighlight.gameObject.SetActive(false);
            }
        }

        // ── Sélection du bloc ─────────────────────────────────

        private static readonly BlockType[] _palette = new BlockType[]
        {
            BlockType.Stone, BlockType.Dirt, BlockType.Grass,
            BlockType.Sand,  BlockType.Wood, BlockType.Leaves,
        };
        private int _paletteIndex;

        private void HandleBlockSelection()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.y.ReadValue();
                if (scroll > 0f)  _paletteIndex = (_paletteIndex + 1) % _palette.Length;
                if (scroll < 0f)  _paletteIndex = (_paletteIndex - 1 + _palette.Length) % _palette.Length;
            }
            var kb = Keyboard.current;
            if (kb != null)
                for (int i = 0; i < _palette.Length; i++)
                    if (kb[(Key)(Key.Digit1 + i)].wasPressedThisFrame)
                        _paletteIndex = i;
#else
            float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (scroll > 0f)  _paletteIndex = (_paletteIndex + 1) % _palette.Length;
            if (scroll < 0f)  _paletteIndex = (_paletteIndex - 1 + _palette.Length) % _palette.Length;

            for (int i = 0; i < _palette.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    _paletteIndex = i;
#endif
            blockToPlace = _palette[_paletteIndex];
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

        public BlockType ActiveBlock   => blockToPlace;
        public int       PaletteIndex    => _paletteIndex;
        public static BlockType[] Palette => _palette;

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

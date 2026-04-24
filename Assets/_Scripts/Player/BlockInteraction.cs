// ============================================================
//  BlockInteraction.cs
//  Clic gauche = casser un bloc, Clic droit = placer un bloc.
//  Utilise un Raycast depuis la caméra vers le monde.
// ============================================================

using UnityEngine;
using AstroVoxel.VoxelEngine;

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

        // ── Cycle de vie ──────────────────────────────────────

        private void Update()
        {
            UpdateHighlight();

            if (Input.GetMouseButtonDown(0))   // Clic gauche
                TryBreakBlock();

            if (Input.GetMouseButtonDown(1))   // Clic droit
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

        // ── Highlight ─────────────────────────────────────────

        private void UpdateHighlight()
        {
            if (blockHighlight == null) return;

            if (Raycast(out RaycastHit hit))
            {
                // Centre sur le bloc visé (arrondi à la grille)
                Vector3 blockCenter = hit.point - hit.normal * 0.5f;
                blockHighlight.position = new Vector3(
                    Mathf.FloorToInt(blockCenter.x) + 0.5f,
                    Mathf.FloorToInt(blockCenter.y) + 0.5f,
                    Mathf.FloorToInt(blockCenter.z) + 0.5f);
                blockHighlight.gameObject.SetActive(true);
            }
            else
            {
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
            float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (scroll > 0f)  _paletteIndex = (_paletteIndex + 1) % _palette.Length;
            if (scroll < 0f)  _paletteIndex = (_paletteIndex - 1 + _palette.Length) % _palette.Length;
            blockToPlace = _palette[_paletteIndex];

            // Touches numériques 1-6
            for (int i = 0; i < _palette.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    _paletteIndex = i;
                    blockToPlace  = _palette[i];
                }
        }

        // ── Accesseurs ────────────────────────────────────────

        public BlockType ActiveBlock => blockToPlace;

        /// <summary>Assigne les références depuis GameBootstrap.</summary>
        public void Init(Camera cam, PlanetWorld w)
        {
            playerCamera = cam;
            world        = w;
        }
    }
}

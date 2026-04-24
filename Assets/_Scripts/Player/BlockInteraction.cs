// ============================================================
//  BlockInteraction.cs
//  Clic gauche = casser un bloc, Clic droit = placer un bloc.
//  Utilise un Raycast depuis la caméra vers le monde.
// ============================================================

using UnityEngine;
using UnityEngine.UI;
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

        // ── HUD Hotbar ────────────────────────────────────────
        private RawImage[] _hotbarIcons;
        private Image[]    _hotbarBorders;
        private Text       _blockNameText;
        private int        _lastPaletteIndex = -1;
        private Material[] _blockMaterials;

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

            // Mise à jour hotbar
            if (_paletteIndex != _lastPaletteIndex)
            {
                _lastPaletteIndex = _paletteIndex;
                UpdateHotbarVisuals();
            }
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

        public BlockType ActiveBlock => blockToPlace;

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

        // ── Couleurs de la palette ────────────────────────────
        // Remplacé par ApplyMaterialToIcon + GetFallbackColor.

        /// <summary>
        /// Crée la hotbar (6 slots) dans le canvas fourni par GameBootstrap.
        /// </summary>
        public void InitHotbar(Canvas canvas, Material[] blockMaterials = null)
        {
            _blockMaterials = blockMaterials;

            int   count    = _palette.Length;
            float slotSize = 52f;
            float gap      = 4f;
            float totalW   = count * slotSize + (count - 1) * gap;

            // Racine de la hotbar (ancrée en bas centre)
            var rootGO = new GameObject("Hotbar");
            rootGO.transform.SetParent(canvas.transform, false);
            var rootRT = rootGO.AddComponent<RectTransform>();
            rootRT.anchorMin        = new Vector2(0.5f, 0f);
            rootRT.anchorMax        = new Vector2(0.5f, 0f);
            rootRT.pivot            = new Vector2(0.5f, 0f);
            rootRT.anchoredPosition = new Vector2(0f, 12f);
            rootRT.sizeDelta        = new Vector2(totalW, slotSize);

            _hotbarIcons   = new RawImage[count];
            _hotbarBorders = new Image[count];

            for (int i = 0; i < count; i++)
            {
                float xPos = i * (slotSize + gap);

                // Bordure
                var borderGO = CreateUIRect("Slot_Border_" + i, rootGO.transform,
                    new Vector2(xPos, 0f), new Vector2(slotSize, slotSize),
                    new Color(1f, 1f, 1f, 0f));
                _hotbarBorders[i] = borderGO.GetComponent<Image>();

                // Fond du slot
                CreateUIRect("Slot_BG_" + i, borderGO.transform,
                    new Vector2(0f, 0f), new Vector2(slotSize - 4f, slotSize - 4f),
                    new Color(0.1f, 0.1f, 0.1f, 0.75f));

                // Icône : RawImage affichant la texture ou la couleur du matériau
                var iconGO = new GameObject("Slot_Icon_" + i);
                iconGO.transform.SetParent(borderGO.transform, false);
                var iconRT = iconGO.AddComponent<RectTransform>();
                iconRT.anchorMin        = new Vector2(0.5f, 0.5f);
                iconRT.anchorMax        = new Vector2(0.5f, 0.5f);
                iconRT.pivot            = new Vector2(0.5f, 0.5f);
                iconRT.sizeDelta        = new Vector2(slotSize - 10f, slotSize - 10f);
                iconRT.anchoredPosition = Vector2.zero;

                var rawImg = iconGO.AddComponent<RawImage>();
                ApplyMaterialToIcon(rawImg, _palette[i]);
                _hotbarIcons[i] = rawImg;
            }

            // Label du bloc actif
            var labelGO = new GameObject("BlockNameLabel");
            labelGO.transform.SetParent(canvas.transform, false);
            var labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin        = new Vector2(0.5f, 0f);
            labelRT.anchorMax        = new Vector2(0.5f, 0f);
            labelRT.pivot            = new Vector2(0.5f, 0f);
            labelRT.anchoredPosition = new Vector2(0f, 12f + slotSize + 6f);
            labelRT.sizeDelta        = new Vector2(200f, 24f);

            _blockNameText = labelGO.AddComponent<Text>();
            _blockNameText.alignment = TextAnchor.MiddleCenter;
            _blockNameText.fontSize  = 14;
            _blockNameText.color     = Color.white;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 14);
            _blockNameText.font = font;

            UpdateHotbarVisuals();
        }

        private void ApplyMaterialToIcon(RawImage icon, BlockType blockType)
        {
            int idx = (int)blockType;
            Material mat = (_blockMaterials != null && idx >= 0 && idx < _blockMaterials.Length)
                ? _blockMaterials[idx] : null;

            if (mat != null && mat.mainTexture is Texture2D texAny)
            {
                icon.texture = texAny;
                icon.color   = Color.white;
                return;
            }

            // Couleur solid : depuis le matériau ou fallback
            Color col = mat != null ? mat.color : GetFallbackColor(blockType);
            var solidTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            solidTex.SetPixel(0, 0, col);
            solidTex.Apply();
            icon.texture = solidTex;
            icon.color   = Color.white;
        }

        private static Color GetFallbackColor(BlockType t)
        {
            switch (t)
            {
                case BlockType.Stone:  return new Color(0.55f, 0.55f, 0.55f);
                case BlockType.Dirt:   return new Color(0.55f, 0.35f, 0.15f);
                case BlockType.Grass:  return new Color(0.30f, 0.65f, 0.20f);
                case BlockType.Sand:   return new Color(0.90f, 0.85f, 0.45f);
                case BlockType.Wood:   return new Color(0.60f, 0.40f, 0.20f);
                case BlockType.Leaves: return new Color(0.18f, 0.48f, 0.12f);
                default: return Color.gray;
            }
        }

        private void UpdateHotbarVisuals()
        {
            if (_hotbarBorders == null) return;
            for (int i = 0; i < _hotbarBorders.Length; i++)
            {
                bool selected = (i == _paletteIndex);
                _hotbarBorders[i].color = selected
                    ? new Color(1f, 1f, 1f, 1f)
                    : new Color(0.3f, 0.3f, 0.3f, 0.85f);

                if (_hotbarIcons != null && i < _hotbarIcons.Length)
                {
                    var img = _hotbarIcons[i];
                    img.color = selected
                        ? Color.white
                        : new Color(0.65f, 0.65f, 0.65f, 1f);
                }
            }

            if (_blockNameText != null)
                _blockNameText.text = _palette[_paletteIndex].ToString();
        }

        /// <summary>Crée un RectTransform avec Image centré sur son parent.</summary>
        private static GameObject CreateUIRect(
            string name, Transform parent, Vector2 anchoredPos, Vector2 size, Color color)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var rt  = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = size;
            rt.anchoredPosition = anchoredPos;
            return go;
        }
    }
}

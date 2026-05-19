// ============================================================
//  CreativeInventory.cs
//  Inventaire créatif style Minecraft, accessible via la touche E.
//
//  Layout :
//  ┌──────────────────────────────────────────────────────────┐
//  │  Overlay plein-écran semi-transparent                    │
//  │  ┌────────────────────────────────────────────────┐      │
//  │  │  INVENTAIRE CRÉATIF                            │      │
//  │  │  [Rechercher un bloc…                  ]       │      │
//  │  │  ┌──────────────────────────────────────────┐  │      │
//  │  │  │  ScrollView (9 col × N lignes)           │  │      │
//  │  │  │  ← Clic ou glisser vers la hotbar        │  │      │
//  │  │  └──────────────────────────────────────────┘  │      │
//  │  │  <nom du bloc survolé>                          │      │
//  │  └────────────────────────────────────────────────┘      │
//  └──────────────────────────────────────────────────────────┘
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using AstroVoxel.VoxelEngine;
using AstroVoxel.Vehicle;

namespace AstroVoxel.Player
{
    public sealed class CreativeInventory : MonoBehaviour
    {
        // ── Palette Apple Dark ────────────────────────────────
        private static readonly Color _overlayBg   = new Color(0.03f, 0.03f, 0.04f, 0.88f);
        private static readonly Color _panelBg     = new Color(0.07f, 0.07f, 0.09f, 0.97f);
        private static readonly Color _slotBg      = new Color(0.12f, 0.12f, 0.14f, 1.00f);
        private static readonly Color _slotHover   = new Color(0.20f, 0.20f, 0.24f, 1.00f);
        private static readonly Color _textPrimary = new Color(1f, 1f, 1f, 0.90f);
        private static readonly Color _textSub     = new Color(1f, 1f, 1f, 0.45f);
        private static readonly Color _inputBg     = new Color(0.09f, 0.09f, 0.11f, 1.00f);
        private static readonly Color _scrollBg    = new Color(0.05f, 0.05f, 0.07f, 1.00f);
        private static readonly Color _thumbColor  = new Color(0.30f, 0.30f, 0.35f, 1.00f);

        // ── Config layout ─────────────────────────────────────
        private const int   Columns        = 9;
        private const float SlotSize       = 52f;
        private const float SlotGap        = 4f;
        private const float PaddingH       = 20f;
        private const float PaddingV       = 16f;
        private const float TitleH         = 38f;
        private const float SearchH        = 34f;
        private const float TooltipH       = 26f;
        private const float ScrollBarW     = 8f;
        private const float MaxHeightRatio = 0.82f;

        // ── State ─────────────────────────────────────────────
        public static bool IsOpen { get; private set; }

        private BlockInteraction _blockInteract;
        private Material[]       _materials;
        private RectTransform[]  _hotbarSlotRects;

        private GameObject    _overlay;
        private RectTransform _overlayRT;
        private Text          _tooltip;
        private InputField    _searchField;

        // Drag & drop
        private RawImage      _dragGhost;
        private RectTransform _dragGhostRT;
        private BlockType     _draggingBlock;

        // Slots filtrables
        private readonly List<(GameObject go, byte blockId, string displayName)> _allSlots = new();

        // ── Init ──────────────────────────────────────────────

        public void Init(
            Canvas           canvas,
            BlockInteraction blockInteract,
            Material[]       materials,
            RectTransform[]  hotbarSlotRects = null)
        {
            _blockInteract   = blockInteract;
            _materials       = materials;
            _hotbarSlotRects = hotbarSlotRects;

            BuildUI(canvas);
            _overlay.SetActive(false);
        }

        private void Awake()
        {
            IsOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        // ── Input ─────────────────────────────────────────────

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb        = UnityEngine.InputSystem.Keyboard.current;
            bool toggleE  = kb != null && kb.eKey.wasPressedThisFrame;
            bool pressEsc = kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            bool toggleE  = Input.GetKeyDown(KeyCode.E);
            bool pressEsc = Input.GetKeyDown(KeyCode.Escape);
#endif
            // Ignore le E quand la barre de recherche a le focus,
            // sinon l'utilisateur ne peut jamais taper la lettre 'e'.
            if (toggleE && IsSearchFocused()) toggleE = false;

            // Ignore le E quand le joueur pilote un vaisseau (E = roulis droite)
            if (toggleE && SpaceShipController.IsAnyShipPiloted) toggleE = false;

            if (toggleE)
            {
                if (IsOpen) Close();
                else        Open();
            }
            else if (pressEsc && IsOpen)
            {
                Close();
            }
        }

        private bool IsSearchFocused()
        {
            return _searchField != null && _searchField.isFocused;
        }

        private void Open()
        {
            IsOpen = true;
            _overlay.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        private void Close()
        {
            IsOpen = false;
            _overlay.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            if (_searchField != null) _searchField.text = "";
            foreach (var s in _allSlots) s.go.SetActive(true);
            if (_dragGhost != null) _dragGhost.gameObject.SetActive(false);
        }

        // ── Build UI ──────────────────────────────────────────

        private void BuildUI(Canvas canvas)
        {
            // Overlay plein-ecran
            var overlayGO        = new GameObject("CreativeInventory_Overlay");
            overlayGO.transform.SetParent(canvas.transform, false);
            _overlayRT           = overlayGO.AddComponent<RectTransform>();
            _overlayRT.anchorMin = Vector2.zero;
            _overlayRT.anchorMax = Vector2.one;
            _overlayRT.offsetMin = Vector2.zero;
            _overlayRT.offsetMax = Vector2.zero;
            var overlayImg       = overlayGO.AddComponent<Image>();
            overlayImg.color     = _overlayBg;
            overlayImg.raycastTarget = true;
            _overlay             = overlayGO;

            // Dimensions du panel
            float gridW  = Columns * SlotSize + (Columns - 1) * SlotGap;
            float panelW = gridW + PaddingH * 2f + ScrollBarW + 6f;
            float screenH = Mathf.Max(Screen.height, 600f);
            float maxH    = screenH * MaxHeightRatio;
            float chromeH = TitleH + SearchH + TooltipH + PaddingV * 2f + SlotGap * 4f;
            float allRows = Mathf.CeilToInt((float)BlockFaceData.AllBlockIds.Length / Columns);
            float idealScrollH = allRows * SlotSize + (allRows - 1) * SlotGap;
            float scrollH = Mathf.Clamp(idealScrollH, SlotSize * 2f, maxH - chromeH);
            float panelH  = chromeH + scrollH;

            // Panel central
            var panelGO  = new GameObject("Panel");
            panelGO.transform.SetParent(overlayGO.transform, false);
            var panelRT              = panelGO.AddComponent<RectTransform>();
            panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
            panelRT.pivot            = new Vector2(0.5f, 0.5f);
            panelRT.anchoredPosition = Vector2.zero;
            panelRT.sizeDelta        = new Vector2(panelW, panelH);
            panelGO.AddComponent<Image>().color = _panelBg;

            // Titre
            MakeLabel(panelGO.transform, "Title",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, TitleH),
                "INVENTAIRE CRÉATIF", 13, _textPrimary, TextAnchor.MiddleCenter);

            // Barre de recherche
            BuildSearchBar(panelGO.transform);

            // ScrollRect avec grille
            BuildScrollView(panelGO.transform, scrollH);

            // Tooltip bas de panel
            var ttGO = new GameObject("Tooltip");
            ttGO.transform.SetParent(panelGO.transform, false);
            var ttRT              = ttGO.AddComponent<RectTransform>();
            ttRT.anchorMin        = new Vector2(0f, 0f);
            ttRT.anchorMax        = new Vector2(1f, 0f);
            ttRT.pivot            = new Vector2(0.5f, 0f);
            ttRT.anchoredPosition = new Vector2(0f, PaddingV * 0.5f);
            ttRT.sizeDelta        = new Vector2(0f, TooltipH);
            _tooltip              = ttGO.AddComponent<Text>();
            _tooltip.font         = GetFont(11);
            _tooltip.fontSize     = 11;
            _tooltip.color        = _textSub;
            _tooltip.alignment    = TextAnchor.MiddleCenter;

            // Ghost de drag
            var ghostGO = new GameObject("DragGhost");
            ghostGO.transform.SetParent(overlayGO.transform, false);
            _dragGhostRT            = ghostGO.AddComponent<RectTransform>();
            _dragGhostRT.anchorMin  = Vector2.zero;
            _dragGhostRT.anchorMax  = Vector2.zero;
            _dragGhostRT.pivot      = new Vector2(0.5f, 0.5f);
            _dragGhostRT.sizeDelta  = new Vector2(SlotSize * 0.85f, SlotSize * 0.85f);
            _dragGhost              = ghostGO.AddComponent<RawImage>();
            _dragGhost.color        = new Color(1f, 1f, 1f, 0.85f);
            _dragGhost.raycastTarget = false;
            ghostGO.SetActive(false);
        }

        // ── Build Search Bar ──────────────────────────────────

        private void BuildSearchBar(Transform panel)
        {
            float yOff = -(TitleH + SlotGap);
            var bg = new GameObject("SearchBar");
            bg.transform.SetParent(panel, false);
            var rt              = bg.AddComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, yOff);
            rt.sizeDelta        = new Vector2(-(PaddingH * 2f), SearchH);
            bg.AddComponent<Image>().color = _inputBg;

            // Texte saisi
            var txtGO = new GameObject("InputText");
            txtGO.transform.SetParent(bg.transform, false);
            var txtRT              = txtGO.AddComponent<RectTransform>();
            txtRT.anchorMin        = Vector2.zero;
            txtRT.anchorMax        = Vector2.one;
            txtRT.offsetMin        = new Vector2(10f, 2f);
            txtRT.offsetMax        = new Vector2(-10f, -2f);
            var inputTxt           = txtGO.AddComponent<Text>();
            inputTxt.font          = GetFont(12);
            inputTxt.fontSize      = 12;
            inputTxt.color         = _textPrimary;
            inputTxt.alignment     = TextAnchor.MiddleLeft;
            inputTxt.supportRichText = false;

            // Placeholder
            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(bg.transform, false);
            var phRT           = phGO.AddComponent<RectTransform>();
            phRT.anchorMin     = Vector2.zero;
            phRT.anchorMax     = Vector2.one;
            phRT.offsetMin     = new Vector2(10f, 2f);
            phRT.offsetMax     = new Vector2(-10f, -2f);
            var phTxt          = phGO.AddComponent<Text>();
            phTxt.text         = "Rechercher un bloc\u2026";
            phTxt.font         = GetFont(12);
            phTxt.fontSize     = 12;
            phTxt.color        = _textSub;
            phTxt.alignment    = TextAnchor.MiddleLeft;
            phTxt.fontStyle    = FontStyle.Italic;

            _searchField = bg.AddComponent<InputField>();
            _searchField.textComponent = inputTxt;
            _searchField.placeholder   = phTxt;
            _searchField.onValueChanged.AddListener(OnSearchChanged);
        }

        // ── Build ScrollView ──────────────────────────────────

        private void BuildScrollView(Transform panel, float scrollH)
        {
            float yOff = -(TitleH + SearchH + SlotGap * 2f);

            var scrollGO = new GameObject("ScrollRect");
            scrollGO.transform.SetParent(panel, false);
            var scrollRT              = scrollGO.AddComponent<RectTransform>();
            scrollRT.anchorMin        = new Vector2(0f, 1f);
            scrollRT.anchorMax        = new Vector2(1f, 1f);
            scrollRT.pivot            = new Vector2(0.5f, 1f);
            scrollRT.anchoredPosition = new Vector2(0f, yOff);
            scrollRT.sizeDelta        = new Vector2(-(PaddingH * 2f), scrollH);

            var scroll              = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal       = false;
            scroll.vertical         = true;
            scroll.scrollSensitivity = 40f;
            scroll.movementType     = ScrollRect.MovementType.Elastic;
            scroll.elasticity       = 0.08f;
            scroll.inertia          = true;
            scroll.decelerationRate = 0.13f;

            // Viewport
            // NB : on utilise RectMask2D plutôt que Mask+Image(Color.clear) :
            // une Image avec alpha 0 n'est pas rendue, donc aucune valeur stencil
            // n'est écrite et les enfants masqués deviennent invisibles.
            var vpGO = new GameObject("Viewport");
            vpGO.transform.SetParent(scrollGO.transform, false);
            var vpRT           = vpGO.AddComponent<RectTransform>();
            vpRT.anchorMin     = Vector2.zero;
            vpRT.anchorMax     = Vector2.one;
            vpRT.offsetMin     = Vector2.zero;
            vpRT.offsetMax     = new Vector2(-(ScrollBarW + 4f), 0f);
            vpGO.AddComponent<RectMask2D>();
            scroll.viewport    = vpRT;

            // Content
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(vpGO.transform, false);
            var contentRT          = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin    = new Vector2(0f, 1f);
            contentRT.anchorMax    = new Vector2(1f, 1f);
            contentRT.pivot        = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta    = Vector2.zero;
            scroll.content         = contentRT;

            var grid                = contentGO.AddComponent<GridLayoutGroup>();
            grid.constraint         = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount    = Columns;
            grid.cellSize           = new Vector2(SlotSize, SlotSize);
            grid.spacing            = new Vector2(SlotGap, SlotGap);
            grid.padding            = new RectOffset(0, 0, 0, (int)SlotGap);
            grid.childAlignment     = TextAnchor.UpperLeft;

            var csf               = contentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit       = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var blockId in BlockFaceData.AllBlockIds)
                BuildSlot(contentGO.transform, blockId);

            // Scrollbar verticale fine
            var sbGO = new GameObject("Scrollbar_V");
            sbGO.transform.SetParent(scrollGO.transform, false);
            var sbRT              = sbGO.AddComponent<RectTransform>();
            sbRT.anchorMin        = new Vector2(1f, 0f);
            sbRT.anchorMax        = new Vector2(1f, 1f);
            sbRT.pivot            = new Vector2(1f, 0.5f);
            sbRT.anchoredPosition = Vector2.zero;
            sbRT.sizeDelta        = new Vector2(ScrollBarW, 0f);
            sbGO.AddComponent<Image>().color = _scrollBg;

            var slideGO = new GameObject("SlidingArea");
            slideGO.transform.SetParent(sbGO.transform, false);
            var slideRT       = slideGO.AddComponent<RectTransform>();
            slideRT.anchorMin = Vector2.zero;
            slideRT.anchorMax = Vector2.one;
            slideRT.offsetMin = new Vector2(2f, 2f);
            slideRT.offsetMax = new Vector2(-2f, -2f);

            var handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(slideGO.transform, false);
            var handleRT      = handleGO.AddComponent<RectTransform>();
            handleRT.sizeDelta = Vector2.zero;
            var handleImg     = handleGO.AddComponent<Image>();
            handleImg.color   = _thumbColor;

            var sb                   = sbGO.AddComponent<Scrollbar>();
            sb.direction             = Scrollbar.Direction.BottomToTop;
            sb.handleRect            = handleRT;
            sb.targetGraphic         = handleImg;
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing    = 2f;
        }

        // ── Build Slot ────────────────────────────────────────

        private void BuildSlot(Transform parent, byte blockId)
        {
            var slotGO = new GameObject($"Slot_{blockId}");
            slotGO.transform.SetParent(parent, false);
            slotGO.AddComponent<RectTransform>();

            var slotImg           = slotGO.AddComponent<Image>();
            slotImg.color         = _slotBg;
            slotImg.raycastTarget = true;

            byte     iconRid = BlockFaceData.GetIconRenderingId(blockId);
            Material mat     = (_materials != null && iconRid < _materials.Length)
                               ? _materials[iconRid] : null;

            var iconGO       = new GameObject("Icon");
            iconGO.transform.SetParent(slotGO.transform, false);
            var iconRT       = iconGO.AddComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.1f, 0.1f);
            iconRT.anchorMax = new Vector2(0.9f, 0.9f);
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;
            var rawImg       = iconGO.AddComponent<RawImage>();
            rawImg.raycastTarget = false;
            ApplySlotIcon(rawImg, mat, blockId);

            var trigger    = slotGO.AddComponent<EventTrigger>();
            var capturedId = blockId;
            var capturedImg = slotImg;
            string dName   = BlockFaceData.GetDisplayName(blockId);

            AddTrigger(trigger, EventTriggerType.PointerEnter, _ =>
            {
                capturedImg.color = _slotHover;
                if (_tooltip != null) _tooltip.text = dName;
            });

            AddTrigger(trigger, EventTriggerType.PointerExit, _ =>
            {
                capturedImg.color = _slotBg;
                if (_tooltip != null) _tooltip.text = "";
            });

            AddTrigger(trigger, EventTriggerType.PointerClick, _ =>
            {
                if (_blockInteract != null)
                    _blockInteract.SetHotbarSlot(_blockInteract.HotbarIndex, (BlockType)capturedId);
                Close();
            });

            // Drag & drop
            AddTrigger(trigger, EventTriggerType.BeginDrag, data =>
            {
                var ped        = (PointerEventData)data;
                _draggingBlock = (BlockType)capturedId;
                if (_dragGhost == null) return;
                _dragGhost.gameObject.SetActive(true);
                if (mat != null && mat.mainTexture is Texture2D tex)
                {
                    _dragGhost.texture = tex;
                    _dragGhost.color   = new Color(mat.color.r, mat.color.g, mat.color.b, 0.85f);
                }
                else
                {
                    _dragGhost.texture = null;
                    Color fc           = BlockFaceData.GetFallbackColor((BlockType)capturedId);
                    _dragGhost.color   = new Color(fc.r, fc.g, fc.b, 0.85f);
                }
                MoveGhost(ped.position);
            });

            AddTrigger(trigger, EventTriggerType.Drag, data =>
            {
                if (_dragGhost != null && _dragGhost.gameObject.activeSelf)
                    MoveGhost(((PointerEventData)data).position);
            });

            AddTrigger(trigger, EventTriggerType.EndDrag, data =>
            {
                if (_dragGhost != null) _dragGhost.gameObject.SetActive(false);
                if (_blockInteract == null) return;
                var ped  = (PointerEventData)data;
                int slot = HotbarSlotAtScreen(ped.position);
                _blockInteract.SetHotbarSlot(
                    slot >= 0 ? slot : _blockInteract.HotbarIndex,
                    _draggingBlock);
            });

            _allSlots.Add((slotGO, blockId, dName));
        }

        // ── Search filter ─────────────────────────────────────

        private void OnSearchChanged(string query)
        {
            string q = query.Trim().ToLowerInvariant();
            bool empty = string.IsNullOrEmpty(q);
            foreach (var (go, _, displayName) in _allSlots)
                go.SetActive(empty || displayName.ToLowerInvariant().Contains(q));
        }

        // ── Helpers ───────────────────────────────────────────

        private static void ApplySlotIcon(RawImage raw, Material mat, byte blockId)
        {
            if (mat != null && mat.mainTexture is Texture2D tex)
            {
                raw.texture = tex;
                raw.color   = mat.color;   // Teinte biome (_BaseColor)
                return;
            }
            Color col    = BlockFaceData.GetFallbackColor((BlockType)blockId);
            var fallback = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            fallback.SetPixel(0, 0, col);
            fallback.Apply();
            raw.texture = fallback;
            raw.color   = Color.white;
        }

        private void MoveGhost(Vector2 screenPos)
        {
            if (_dragGhostRT == null || _overlayRT == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _overlayRT, screenPos, null, out var local);
            _dragGhostRT.anchoredPosition = local;
        }

        private int HotbarSlotAtScreen(Vector2 screenPos)
        {
            if (_hotbarSlotRects == null) return -1;
            for (int i = 0; i < _hotbarSlotRects.Length; i++)
            {
                if (_hotbarSlotRects[i] != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(
                        _hotbarSlotRects[i], screenPos, null))
                    return i;
            }
            return -1;
        }

        private static void MakeLabel(
            Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta,
            string text, int fontSize, Color color, TextAnchor alignment)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt              = go.AddComponent<RectTransform>();
            rt.anchorMin        = anchorMin;
            rt.anchorMax        = anchorMax;
            rt.pivot            = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = sizeDelta;
            var txt             = go.AddComponent<Text>();
            txt.text            = text;
            txt.font            = GetFont(fontSize);
            txt.fontSize        = fontSize;
            txt.color           = color;
            txt.alignment       = alignment;
        }

        private static void AddTrigger(
            EventTrigger trigger, EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(action);
            trigger.triggers.Add(entry);
        }

        private static Font GetFont(int size)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("SF Pro Display", size);
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Helvetica Neue", size);
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", size);
            return font;
        }
    }
}

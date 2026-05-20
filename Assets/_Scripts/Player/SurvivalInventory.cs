// ============================================================
//  SurvivalInventory.cs
//  Inventaire de survie style Minecraft.
//
//  Fonctionnalités :
//   • Touche E en mode Survie pour ouvrir/fermer
//   • Liste des items ramassés avec icône + compteur
//   • Recettes disponibles listées à droite (auto-détectées)
//   • Bouton "Fabriquer" qui craft et rafraîchit l'UI
//   • Mode Établi : recettes 3×3 débloquées si dans l'inventaire
//
//  Layout (Apple Dark) :
//  ┌──────────────────────────────────────────────────────────┐
//  │  Overlay plein-écran semi-transparent                    │
//  │  ┌─────────────────────┐  ┌──────────────────────────┐  │
//  │  │  INVENTAIRE SURVIE  │  │  CRAFT                   │  │
//  │  │  [items + counts ]  │  │  [recette 1] [Fabriquer] │  │
//  │  │  [item grid  …   ]  │  │  [recette 2] [Fabriquer] │  │
//  │  │                     │  │  (établi requis grisé)   │  │
//  │  └─────────────────────┘  └──────────────────────────┘  │
//  └──────────────────────────────────────────────────────────┘
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using AstroVoxel.VoxelEngine;

namespace AstroVoxel.Player
{
    public sealed class SurvivalInventory : MonoBehaviour
    {
        // ── Palette Apple Dark ────────────────────────────────
        private static readonly Color _overlayBg   = new Color(0.03f, 0.03f, 0.04f, 0.88f);
        private static readonly Color _panelBg     = new Color(0.07f, 0.07f, 0.09f, 0.97f);
        private static readonly Color _panelBg2    = new Color(0.08f, 0.08f, 0.11f, 0.97f);
        private static readonly Color _slotBg      = new Color(0.12f, 0.12f, 0.14f, 1.00f);
        private static readonly Color _slotHover   = new Color(0.20f, 0.20f, 0.24f, 1.00f);
        private static readonly Color _textPrimary = new Color(1f, 1f, 1f, 0.90f);
        private static readonly Color _textSub     = new Color(1f, 1f, 1f, 0.45f);
        private static readonly Color _textCount   = new Color(1f, 0.85f, 0.30f, 1.00f);
        private static readonly Color _btnOk       = new Color(0.15f, 0.50f, 0.25f, 1.00f);
        private static readonly Color _btnDisabled = new Color(0.20f, 0.20f, 0.22f, 1.00f);
        private static readonly Color _scrollBg    = new Color(0.05f, 0.05f, 0.07f, 1.00f);
        private static readonly Color _thumbColor  = new Color(0.30f, 0.30f, 0.35f, 1.00f);

        // ── Config layout ─────────────────────────────────────
        private const float SlotSize    = 52f;
        private const float SlotGap     = 4f;
        private const float PaddingH    = 18f;
        private const float PaddingV    = 14f;
        private const float TitleH      = 36f;
        private const float ScrollBarW  = 8f;
        private const int   Columns     = 5;  // colonnes dans la grille d'items

        // ── State ─────────────────────────────────────────────
        public  static bool IsOpen        { get; private set; }
        private static bool _craftingTableMode;

        private BlockInteraction _blockInteract;
        private Material[]       _materials;
        private Material[]       _itemMaterials;

        private GameObject    _overlay;
        private Transform     _itemGridContent;
        private Transform     _recipeListContent;
        private Text          _statusText;
        // ── Drag & Drop ────────────────────────────────────────
        private Canvas          _canvas;
        private RectTransform[] _hotbarSlotRects;
        private DragSlot        _activeDrag;
        private GameObject      _dragVisual;
        private bool            _dropHandled;
        // ── Init ──────────────────────────────────────────────

        public void Init(
            Canvas           canvas,
            BlockInteraction blockInteract,
            Material[]       materials,
            RectTransform[]  hotbarSlotRects = null,
            Material[]       itemMaterials   = null)
        {
            _blockInteract = blockInteract;
            _materials     = materials;
            _itemMaterials = itemMaterials;            _canvas          = canvas;
            _hotbarSlotRects = hotbarSlotRects;            BuildUI(canvas);
            _overlay.SetActive(false);

            // S'abonner aux changements d'inventaire pour rafraîchir en temps réel
            SurvivalInventoryData.Instance.OnChanged += RefreshAll;
        }

        private void Awake()
        {
            IsOpen             = false;
            _craftingTableMode = false;
        }

        private void OnDestroy()
        {
            SurvivalInventoryData.Instance.OnChanged -= RefreshAll;
        }

        // ── Input ─────────────────────────────────────────────

        private void Update()
        {
            if (!GameModeManager.IsSurvival) return;
            if (CreativeInventory.IsOpen)    return;  // mutuel exclusif

#if ENABLE_INPUT_SYSTEM
            var kb        = UnityEngine.InputSystem.Keyboard.current;
            bool toggleE  = kb != null && kb.eKey.wasPressedThisFrame;
            bool pressEsc = kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            bool toggleE  = Input.GetKeyDown(KeyCode.E);
            bool pressEsc = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (toggleE && AstroVoxel.Vehicle.SpaceShipController.IsAnyShipPiloted) toggleE = false;
            if (toggleE && GameConsole.IsOpen) toggleE = false;

            if (toggleE)
            {
                if (IsOpen) Close();
                else        OpenNormal();
            }
            else if (pressEsc && IsOpen)
            {
                Close();
            }
        }

        // ── Ouverture / fermeture ─────────────────────────────

        public void OpenNormal()
        {
            _craftingTableMode = false;
            OpenInternal();
        }

        /// <summary>Ouvert depuis un clic-droit sur un établi — recettes 3×3 débloquées.</summary>
        public void OpenWithCraftingTable()
        {
            _craftingTableMode = true;
            OpenInternal();
        }

        private void OpenInternal()
        {
            IsOpen = true;
            _overlay.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            RefreshAll();
            RegisterHotbarDragSlots();
        }

        public void Close()
        {
            CancelDrag();
            UnregisterHotbarDragSlots();
            IsOpen             = false;
            _craftingTableMode = false;
            _overlay.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        // ── Build UI ──────────────────────────────────────────

        private void BuildUI(Canvas canvas)
        {
            // Overlay plein-ecran
            var ovGO         = new GameObject("SurvivalInventory_Overlay");
            ovGO.transform.SetParent(canvas.transform, false);
            var ovRT         = ovGO.AddComponent<RectTransform>();
            ovRT.anchorMin   = Vector2.zero;
            ovRT.anchorMax   = Vector2.one;
            ovRT.offsetMin   = Vector2.zero;
            ovRT.offsetMax   = Vector2.zero;
            var ovImg        = ovGO.AddComponent<Image>();
            ovImg.color      = _overlayBg;
            ovImg.raycastTarget = true;
            _overlay         = ovGO;

            float panelH = 420f;

            // ── Panneau gauche : inventaire ───────────────────
            float leftW   = Columns * (SlotSize + SlotGap) + PaddingH * 2f + ScrollBarW + 6f;
            var leftPanelGO = MakePanel(ovGO.transform, "InventoryPanel",
                new Vector2(-leftW * 0.5f - 10f, 0f),
                new Vector2(leftW, panelH));

            MakeLabel(leftPanelGO.transform, "TitleLeft",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, TitleH),
                "INVENTAIRE SURVIE", 12, _textPrimary, TextAnchor.MiddleCenter);

            float scrollH    = panelH - TitleH - PaddingV * 2f;
            _itemGridContent = BuildScrollPanel(leftPanelGO.transform, scrollH, "Items");

            // ── Panneau droit : craft ─────────────────────────
            float rightW     = 300f;
            var rightPanelGO = MakePanel(ovGO.transform, "CraftPanel",
                new Vector2(rightW * 0.5f + 10f, 0f),
                new Vector2(rightW, panelH));

            MakeLabel(rightPanelGO.transform, "TitleRight",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, TitleH),
                "CRAFT", 12, _textPrimary, TextAnchor.MiddleCenter);

            float rScrollH      = panelH - TitleH - PaddingV * 2f - 30f;
            _recipeListContent  = BuildScrollPanel(rightPanelGO.transform, rScrollH, "Recipes");

            // Statut craft (établi requis, etc.)
            var stGO            = new GameObject("CraftStatus");
            stGO.transform.SetParent(rightPanelGO.transform, false);
            var stRT            = stGO.AddComponent<RectTransform>();
            stRT.anchorMin      = new Vector2(0f, 0f);
            stRT.anchorMax      = new Vector2(1f, 0f);
            stRT.pivot          = new Vector2(0.5f, 0f);
            stRT.anchoredPosition = new Vector2(0f, PaddingV);
            stRT.sizeDelta      = new Vector2(0f, 24f);
            _statusText         = stGO.AddComponent<Text>();
            _statusText.font    = GetFont(11);
            _statusText.fontSize = 11;
            _statusText.color   = _textSub;
            _statusText.alignment = TextAnchor.MiddleCenter;
        }

        // ── Rafraîchissement ──────────────────────────────────

        private void RefreshAll()
        {
            if (!IsOpen) return;
            RefreshItemGrid();
            RefreshRecipeList();
        }

        private void RefreshItemGrid()
        {
            ClearChildren(_itemGridContent);
            var stacks = SurvivalInventoryData.Instance.GetAllStacks();
            foreach (var stack in stacks)
                BuildItemSlot(_itemGridContent, stack);
        }

        private void BuildItemSlot(Transform parent, ItemStack stack)
        {
            var slotGO        = new GameObject($"ISlot_{stack.itemType}");
            slotGO.transform.SetParent(parent, false);
            var slotRT        = slotGO.AddComponent<RectTransform>();
            slotRT.sizeDelta  = new Vector2(SlotSize, SlotSize);
            var slotImg       = slotGO.AddComponent<Image>();
            slotImg.color     = _slotBg;

            // Icône
            var iconGO       = new GameObject("Icon");
            iconGO.transform.SetParent(slotGO.transform, false);
            var iconRT       = iconGO.AddComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.08f, 0.20f);
            iconRT.anchorMax = new Vector2(0.92f, 0.92f);
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;
            var rawImg       = iconGO.AddComponent<RawImage>();
            rawImg.raycastTarget = false;

            if (stack.IsBlock())
            {
                byte iconId  = BlockFaceData.GetIconRenderingId((byte)(int)stack.itemType);
                Material mat = (_materials != null && iconId < _materials.Length) ? _materials[iconId] : null;
                ApplyIcon(rawImg, mat, (int)stack.itemType);
            }
            else
            {
                ApplyItemIcon(rawImg, stack.itemType);
            }

            // Compteur
            var cntGO        = new GameObject("Count");
            cntGO.transform.SetParent(slotGO.transform, false);
            var cntRT        = cntGO.AddComponent<RectTransform>();
            cntRT.anchorMin  = Vector2.zero;
            cntRT.anchorMax  = Vector2.one;
            cntRT.offsetMin  = new Vector2(2f, 2f);
            cntRT.offsetMax  = new Vector2(-2f, -2f);
            var cntTxt       = cntGO.AddComponent<Text>();
            cntTxt.text      = stack.count.ToString();
            cntTxt.font      = GetFont(10);
            cntTxt.fontSize  = 10;
            cntTxt.color     = _textCount;
            cntTxt.alignment = TextAnchor.UpperRight;
            cntTxt.raycastTarget = false;

            // Hover
            var trigger       = slotGO.AddComponent<EventTrigger>();
            var capturedSlot  = slotImg;
            var capturedType  = stack.itemType;
            AddTrigger(trigger, EventTriggerType.PointerEnter, _ => capturedSlot.color = _slotHover);
            AddTrigger(trigger, EventTriggerType.PointerExit,  _ => capturedSlot.color = _slotBg);

            // Drag & Drop
            var ds         = slotGO.AddComponent<DragSlot>();
            ds.Owner       = this;
            ds.ItemType    = stack.itemType;
            ds.HotbarIndex = -1;
        }

        private void RefreshRecipeList()
        {
            ClearChildren(_recipeListContent);

            bool hasCraftingTable = SurvivalInventoryData.Instance.Has(ItemType.CraftingTable, 1)
                                    || _craftingTableMode;

            var allRecipes = CraftingSystem.GetAllCraftableRecipes(hasCraftingTable);
            // Recettes disponibles en premier
            allRecipes.Sort((a, b) =>
            {
                bool ca = a.CanCraft(SurvivalInventoryData.Instance);
                bool cb = b.CanCraft(SurvivalInventoryData.Instance);
                return cb.CompareTo(ca);
            });

            foreach (var recipe in allRecipes)
            {
                bool canCraft = recipe.CanCraft(SurvivalInventoryData.Instance);
                BuildRecipeRow(_recipeListContent, recipe, canCraft);
            }

            if (_statusText != null)
            {
                if (!hasCraftingTable && _craftingTableMode == false)
                    _statusText.text = "Fabrique un établi pour débloquer + de recettes";
                else
                    _statusText.text = "";
            }
        }

        private void BuildRecipeRow(Transform parent, CraftingRecipe recipe, bool canCraft)
        {
            float rowH   = 46f;
            float rowW   = 280f;

            var rowGO    = new GameObject($"Recipe_{recipe.ResultItem}");
            rowGO.transform.SetParent(parent, false);
            var rowRT    = rowGO.AddComponent<RectTransform>();
            rowRT.sizeDelta = new Vector2(rowW, rowH);
            rowGO.AddComponent<Image>().color = canCraft
                ? new Color(0.10f, 0.14f, 0.10f, 1f)
                : new Color(0.12f, 0.12f, 0.13f, 0.8f);

            // Icône du résultat
            var iconGO       = new GameObject("Icon");
            iconGO.transform.SetParent(rowGO.transform, false);
            var iconRT       = iconGO.AddComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0f, 0f);
            iconRT.anchorMax = new Vector2(0f, 0f);
            iconRT.pivot     = new Vector2(0f, 0.5f);
            iconRT.anchoredPosition = new Vector2(8f, rowH * 0.5f);
            iconRT.sizeDelta = new Vector2(36f, 36f);
            var rawImg       = iconGO.AddComponent<RawImage>();
            rawImg.raycastTarget = false;

            if (((ItemStack)new ItemStack(recipe.ResultItem, 1)).IsBlock())
            {
                byte iconId  = BlockFaceData.GetIconRenderingId((byte)(int)recipe.ResultItem);
                Material mat = (_materials != null && iconId < _materials.Length) ? _materials[iconId] : null;
                ApplyIcon(rawImg, mat, (int)recipe.ResultItem);
            }
            else
            {
                ApplyItemIcon(rawImg, recipe.ResultItem);
            }

            // Nom + ingrédients
            var txtGO       = new GameObject("Txt");
            txtGO.transform.SetParent(rowGO.transform, false);
            var txtRT       = txtGO.AddComponent<RectTransform>();
            txtRT.anchorMin = new Vector2(0f, 0f);
            txtRT.anchorMax = new Vector2(1f, 1f);
            txtRT.offsetMin = new Vector2(52f, 2f);
            txtRT.offsetMax = new Vector2(-84f, -2f);
            var txt         = txtGO.AddComponent<Text>();
            txt.font        = GetFont(11);
            txt.fontSize    = 11;
            txt.color       = canCraft ? _textPrimary : _textSub;
            txt.raycastTarget = false;

            // Construire la description des ingrédients
            string ingLine = "";
            foreach (var kv in recipe.Ingredients)
            {
                int have = SurvivalInventoryData.Instance.GetCount(kv.Key);
                string color = have >= kv.Value ? "#AAFFAA" : "#FF8888";
                ingLine += $"<color={color}>{kv.Value}×{ItemTypeHelper.GetDisplayName(kv.Key)}</color> ";
            }
            txt.text = $"<b>{recipe.DisplayName}</b> ×{recipe.ResultCount}\n{ingLine.TrimEnd()}";
            txt.supportRichText = true;

            // Bouton Fabriquer
            var btnGO       = new GameObject("CraftBtn");
            btnGO.transform.SetParent(rowGO.transform, false);
            var btnRT       = btnGO.AddComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(1f, 0.5f);
            btnRT.anchorMax = new Vector2(1f, 0.5f);
            btnRT.pivot     = new Vector2(1f, 0.5f);
            btnRT.anchoredPosition = new Vector2(-6f, 0f);
            btnRT.sizeDelta = new Vector2(74f, 30f);
            var btnImg      = btnGO.AddComponent<Image>();
            btnImg.color    = canCraft ? _btnOk : _btnDisabled;
            btnImg.raycastTarget = true;

            var btnTxtGO    = new GameObject("BtnTxt");
            btnTxtGO.transform.SetParent(btnGO.transform, false);
            var bRT         = btnTxtGO.AddComponent<RectTransform>();
            bRT.anchorMin   = Vector2.zero;
            bRT.anchorMax   = Vector2.one;
            bRT.offsetMin   = Vector2.zero;
            bRT.offsetMax   = Vector2.zero;
            var bTxt        = btnTxtGO.AddComponent<Text>();
            bTxt.text       = "Fabriquer";
            bTxt.font       = GetFont(11);
            bTxt.fontSize   = 11;
            bTxt.color      = canCraft ? Color.white : _textSub;
            bTxt.alignment  = TextAnchor.MiddleCenter;
            bTxt.raycastTarget = false;

            if (canCraft)
            {
                var btnTrigger  = btnGO.AddComponent<EventTrigger>();
                var capturedRecipe = recipe;
                AddTrigger(btnTrigger, EventTriggerType.PointerClick, _ =>
                {
                    capturedRecipe.TryCraft(SurvivalInventoryData.Instance);
                    // RefreshAll sera appelé via OnChanged
                });
                AddTrigger(btnTrigger, EventTriggerType.PointerEnter, _ => btnImg.color = new Color(0.20f, 0.65f, 0.35f, 1f));
                AddTrigger(btnTrigger, EventTriggerType.PointerExit,  _ => btnImg.color = _btnOk);
            }
        }

        // ── Helpers UI ────────────────────────────────────────

        private GameObject MakePanel(Transform parent, string name, Vector2 center, Vector2 size)
        {
            var go   = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt           = go.AddComponent<RectTransform>();
            rt.anchorMin     = new Vector2(0.5f, 0.5f);
            rt.anchorMax     = new Vector2(0.5f, 0.5f);
            rt.pivot         = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = center;
            rt.sizeDelta     = size;
            go.AddComponent<Image>().color = _panelBg;
            return go;
        }

        private Transform BuildScrollPanel(Transform panel, float scrollH, string name)
        {
            float yOff   = -(TitleH + SlotGap);

            var scrollGO = new GameObject($"Scroll_{name}");
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

            var vpGO       = new GameObject("Viewport");
            vpGO.transform.SetParent(scrollGO.transform, false);
            var vpRT       = vpGO.AddComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero;
            vpRT.offsetMax = new Vector2(-(ScrollBarW + 4f), 0f);
            vpGO.AddComponent<RectMask2D>();
            scroll.viewport = vpRT;

            var contentGO  = new GameObject("Content");
            contentGO.transform.SetParent(vpGO.transform, false);
            var contentRT  = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin    = new Vector2(0f, 1f);
            contentRT.anchorMax    = new Vector2(1f, 1f);
            contentRT.pivot        = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta    = Vector2.zero;
            scroll.content         = contentRT;

            var grid                = contentGO.AddComponent<GridLayoutGroup>();
            grid.constraint         = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount    = (name == "Recipes") ? 1 : Columns;
            grid.cellSize           = (name == "Recipes")
                ? new Vector2(280f, 46f)
                : new Vector2(SlotSize, SlotSize);
            grid.spacing            = new Vector2(SlotGap, SlotGap);
            grid.padding            = new RectOffset(4, 4, 4, 4);
            grid.childAlignment     = TextAnchor.UpperLeft;

            var csf             = contentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit     = ContentSizeFitter.FitMode.PreferredSize;

            // Scrollbar
            var sbGO       = new GameObject("Scrollbar_V");
            sbGO.transform.SetParent(scrollGO.transform, false);
            var sbRT       = sbGO.AddComponent<RectTransform>();
            sbRT.anchorMin = new Vector2(1f, 0f);
            sbRT.anchorMax = new Vector2(1f, 1f);
            sbRT.pivot     = new Vector2(1f, 0.5f);
            sbRT.anchoredPosition = Vector2.zero;
            sbRT.sizeDelta = new Vector2(ScrollBarW, 0f);
            sbGO.AddComponent<Image>().color = _scrollBg;

            var slideGO    = new GameObject("SlidingArea");
            slideGO.transform.SetParent(sbGO.transform, false);
            var slideRT    = slideGO.AddComponent<RectTransform>();
            slideRT.anchorMin = Vector2.zero;
            slideRT.anchorMax = Vector2.one;
            slideRT.offsetMin = new Vector2(2f, 2f);
            slideRT.offsetMax = new Vector2(-2f, -2f);

            var handleGO   = new GameObject("Handle");
            handleGO.transform.SetParent(slideGO.transform, false);
            handleGO.AddComponent<RectTransform>().sizeDelta = Vector2.zero;
            var handleImg  = handleGO.AddComponent<Image>();
            handleImg.color = _thumbColor;

            var sb                   = sbGO.AddComponent<Scrollbar>();
            sb.direction             = Scrollbar.Direction.BottomToTop;
            sb.handleRect            = handleGO.GetComponent<RectTransform>();
            sb.targetGraphic         = handleImg;
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing    = 2f;

            return contentRT;
        }

        private static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Destroy(t.GetChild(i).gameObject);
        }

        // ── Helpers textures ──────────────────────────────────

        private static void ApplyIcon(RawImage raw, Material mat, int id)
        {
            if (mat != null && mat.mainTexture is Texture2D tex)
            {
                raw.texture = tex;
                raw.color   = mat.color;
                return;
            }
            Color col    = id >= 1 && id <= 255
                ? BlockFaceData.GetFallbackColor((BlockType)id)
                : Color.gray;
            raw.texture = MakeSolidTex(col);
            raw.color   = Color.white;
        }

        private static Texture2D MakeSolidTex(Color col)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, col);
            t.Apply();
            return t;
        }

        private void ApplyItemIcon(RawImage raw, ItemType itype)
        {
            int idx = (int)itype - 300;
            if (_itemMaterials != null && idx >= 0 && idx < _itemMaterials.Length && _itemMaterials[idx] != null)
            {
                var mat = _itemMaterials[idx];
                if (mat.mainTexture is Texture2D tex)
                {
                    raw.texture = tex;
                    raw.color   = Color.white;
                    return;
                }
            }
            // Repli : couleur unie
            raw.color   = GetToolColor(itype);
            raw.texture = MakeSolidTex(raw.color);
        }

        private static Color GetToolColor(ItemType t)
        {
            switch (t)
            {
                case ItemType.Stick:         return new Color(0.70f, 0.55f, 0.30f);
                case ItemType.WoodenPickaxe: return new Color(0.65f, 0.50f, 0.28f);
                case ItemType.WoodenAxe:     return new Color(0.65f, 0.50f, 0.28f);
                case ItemType.WoodenShovel:  return new Color(0.65f, 0.50f, 0.28f);
                case ItemType.StonePickaxe:  return new Color(0.60f, 0.60f, 0.60f);
                case ItemType.StoneAxe:      return new Color(0.60f, 0.60f, 0.60f);
                case ItemType.StoneShovel:   return new Color(0.60f, 0.60f, 0.60f);
                case ItemType.IronPickaxe:   return new Color(0.85f, 0.85f, 0.90f);
                default:                     return Color.gray;
            }
        }

        // ── Helpers UI ────────────────────────────────────────

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

        // ── Drag & Drop : gestion ─────────────────────────────

        private void RegisterHotbarDragSlots()
        {
            if (_hotbarSlotRects == null) return;
            for (int i = 0; i < _hotbarSlotRects.Length; i++)
            {
                if (_hotbarSlotRects[i] == null) continue;
                var slotGO = _hotbarSlotRects[i].gameObject;
                var img = slotGO.GetComponent<Image>();
                if (img != null) img.raycastTarget = true;
                var ds = slotGO.GetComponent<DragSlot>();
                if (ds == null) ds = slotGO.AddComponent<DragSlot>();
                ds.Owner       = this;
                ds.HotbarIndex = i;
                ds.ItemType    = ItemType.None; // lu dynamiquement au début du drag
            }
        }

        private void UnregisterHotbarDragSlots()
        {
            if (_hotbarSlotRects == null) return;
            foreach (var rt in _hotbarSlotRects)
            {
                if (rt == null) continue;
                var ds = rt.gameObject.GetComponent<DragSlot>();
                if (ds != null) Destroy(ds);
            }
        }

        private void CancelDrag()
        {
            if (_dragVisual != null) { Destroy(_dragVisual); _dragVisual = null; }
            _activeDrag = null;
        }

        internal void BeginDrag(DragSlot source, PointerEventData e)
        {
            _activeDrag = source;
            _dragVisual = BuildDragVisual(source.ItemType, e);
        }

        internal void MoveDragVisual(PointerEventData e)
        {
            if (_dragVisual == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_canvas.transform, e.position, e.pressEventCamera, out var lp);
            ((RectTransform)_dragVisual.transform).anchoredPosition = lp;
        }

        internal void EndDrag(DragSlot source, PointerEventData e)
        {
            if (_dragVisual != null) { Destroy(_dragVisual); _dragVisual = null; }

            // Fallback : l'overlay plein-écran bloque les OnDrop vers la hotbar.
            // On détecte manuellement si le curseur est au-dessus d'un slot hotbar.
            if (!_dropHandled && _activeDrag != null && _hotbarSlotRects != null)
            {
                for (int i = 0; i < _hotbarSlotRects.Length; i++)
                {
                    if (_hotbarSlotRects[i] == null) continue;
                    if (RectTransformUtility.RectangleContainsScreenPoint(
                            _hotbarSlotRects[i], e.position, e.pressEventCamera))
                    {
                        var ds = _hotbarSlotRects[i].GetComponent<DragSlot>();
                        if (ds != null) { Drop(ds); break; }
                    }
                }
            }

            _dropHandled = false;
            _activeDrag  = null;
        }

        internal void Drop(DragSlot target)
        {
            if (_activeDrag == null || _activeDrag == target) return;
            var src = _activeDrag;
            var inv = SurvivalInventoryData.Instance;

            if (src.HotbarIndex >= 0 && target.HotbarIndex >= 0)
            {
                // hotbar ↔ hotbar : échange de position
                inv.SwapHotbarSlots(src.HotbarIndex, target.HotbarIndex);
            }
            else if (src.HotbarIndex < 0 && target.HotbarIndex >= 0)
            {
                // inventaire → hotbar
                inv.MoveItemToHotbarSlot(src.ItemType, target.HotbarIndex);
            }
            else if (src.HotbarIndex >= 0 && target.HotbarIndex < 0)
            {
                // hotbar → inventaire : libère le slot
                inv.ClearHotbarSlot(src.HotbarIndex);
            }
            else
            {
                // inventaire ↔ inventaire : échange leurs positions hotbar
                int si = FindHotbarSlot(src.ItemType);
                int ti = FindHotbarSlot(target.ItemType);
                if (si >= 0 && ti >= 0)      inv.SwapHotbarSlots(si, ti);
                else if (si >= 0 && ti < 0)  inv.MoveItemToHotbarSlot(target.ItemType, si);
                else if (si < 0  && ti >= 0) inv.MoveItemToHotbarSlot(src.ItemType,    ti);
            }
            _dropHandled = true;
        }

        private static int FindHotbarSlot(ItemType t)
        {
            var h = SurvivalInventoryData.Instance.Hotbar;
            for (int i = 0; i < h.Length; i++)
                if (h[i].itemType == t) return i;
            return -1;
        }

        private GameObject BuildDragVisual(ItemType itemType, PointerEventData e)
        {
            var go = new GameObject("DragVisual");
            go.transform.SetParent(_canvas.transform, false);
            var rt        = go.AddComponent<RectTransform>();
            rt.sizeDelta  = new Vector2(SlotSize, SlotSize);
            rt.anchorMin  = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot      = new Vector2(0.5f, 0.5f);

            // Semi-transparent + ne bloque pas les raycasts
            var cg            = go.AddComponent<CanvasGroup>();
            cg.alpha          = 0.80f;
            cg.blocksRaycasts = false;
            cg.interactable   = false;

            var bg           = go.AddComponent<Image>();
            bg.color         = new Color(0.20f, 0.20f, 0.25f, 0.85f);
            bg.raycastTarget = false;

            var iconGO       = new GameObject("Icon");
            iconGO.transform.SetParent(go.transform, false);
            var iconRT       = iconGO.AddComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.1f, 0.1f);
            iconRT.anchorMax = new Vector2(0.9f, 0.9f);
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;
            var rawImg       = iconGO.AddComponent<RawImage>();
            rawImg.raycastTarget = false;

            var tempStack = new ItemStack(itemType, 1);
            if (tempStack.IsBlock())
            {
                byte iconId = BlockFaceData.GetIconRenderingId((byte)(int)itemType);
                Material mat = (_materials != null && iconId < _materials.Length) ? _materials[iconId] : null;
                ApplyIcon(rawImg, mat, (int)itemType);
            }
            else
            {
                ApplyItemIcon(rawImg, itemType);
            }

            // Position initiale sous le curseur
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_canvas.transform, e.position, e.pressEventCamera, out var lp);
            rt.anchoredPosition = lp;

            return go;
        }

        // ── DragSlot : composant attaché sur chaque slot glissable ──

        internal sealed class DragSlot : MonoBehaviour,
            IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
        {
            public SurvivalInventory Owner;
            public ItemType          ItemType;
            public int               HotbarIndex = -1; // -1 = slot inventaire

            public void OnBeginDrag(PointerEventData e)
            {
                // Hotbar : lire l'item au moment du drag (peut avoir changé)
                if (HotbarIndex >= 0)
                {
                    var hotbar = SurvivalInventoryData.Instance.Hotbar;
                    ItemType = HotbarIndex < hotbar.Length ? hotbar[HotbarIndex].itemType : ItemType.None;
                }
                if (ItemType == ItemType.None) { e.pointerDrag = null; return; }
                Owner?.BeginDrag(this, e);
            }

            public void OnDrag(PointerEventData e)    => Owner?.MoveDragVisual(e);
            public void OnEndDrag(PointerEventData e) => Owner?.EndDrag(this, e);
            public void OnDrop(PointerEventData e)    => Owner?.Drop(this);
        }
    }
}

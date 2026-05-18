// ============================================================
//  CreativeInventory.cs
//  Inventaire créatif style Minecraft, accessible via la touche E.
//
//  Architecture :
//  ┌──────────────────────────────────────────────────────────┐
//  │  Overlay plein-écran semi-transparent                    │
//  │  ┌────────────────────────────────────────────────┐      │
//  │  │  Grille 9×N — tous les blocs (BlockFaceData)   │      │
//  │  │  Clic → SetHotbarSlot(HotbarIndex, blockType)  │      │
//  │  └────────────────────────────────────────────────┘      │
//  └──────────────────────────────────────────────────────────┘
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using AstroVoxel.VoxelEngine;

namespace AstroVoxel.Player
{
    /// <summary>
    /// Inventaire créatif en plein écran.
    /// Appeler <see cref="Init"/> depuis GameBootstrap.
    /// </summary>
    public sealed class CreativeInventory : MonoBehaviour
    {
        // ── Palette Apple Dark ────────────────────────────────
        private static readonly Color _overlayBg  = new Color(0.03f, 0.03f, 0.04f, 0.88f);
        private static readonly Color _panelBg    = new Color(0.07f, 0.07f, 0.09f, 0.95f);
        private static readonly Color _slotBg     = new Color(0.12f, 0.12f, 0.14f, 1.00f);
        private static readonly Color _slotHover  = new Color(0.20f, 0.20f, 0.24f, 1.00f);
        private static readonly Color _accent      = new Color(0.00f, 0.48f, 1.00f, 1.00f);
        private static readonly Color _textPrimary = new Color(1f, 1f, 1f, 0.90f);
        private static readonly Color _textSub     = new Color(1f, 1f, 1f, 0.45f);

        // ── Config layout ─────────────────────────────────────
        private const int   Columns   = 9;
        private const float SlotSize  = 52f;
        private const float SlotGap   = 4f;
        private const float Padding   = 20f;
        private const float TitleH    = 36f;
        private const float Radius    = 16f;

        // ── State ─────────────────────────────────────────────
        public static bool IsOpen { get; private set; }

        private BlockInteraction _blockInteract;
        private Material[]       _materials;

        private GameObject _overlay;
        private Text       _tooltip;

        // ── Init (appelé par GameBootstrap) ──────────────────

        public void Init(Canvas canvas, BlockInteraction blockInteract, Material[] materials)
        {
            _blockInteract = blockInteract;
            _materials     = materials;

            BuildUI(canvas);
            _overlay.SetActive(false);
        }

        // Réinitialise le flag static à chaque démarrage de scène.
        // Sans ça, si la variable reste true entre deux sessions Play dans l'éditeur
        // (domaine non rechargé), la caméra et le joueur restent bloqués.
        private void Awake()
        {
            IsOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        // ── Input loop ────────────────────────────────────────

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool toggleE   = kb != null && kb.eKey.wasPressedThisFrame;
            bool pressEsc  = kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            bool toggleE   = Input.GetKeyDown(KeyCode.E);
            bool pressEsc  = Input.GetKeyDown(KeyCode.Escape);
#endif
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

        // ── Open / Close ──────────────────────────────────────

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
        }

        // ── Build UI ──────────────────────────────────────────

        private void BuildUI(Canvas canvas)
        {
            // Overlay plein écran (intercepte les clics)
            var overlayGO = new GameObject("CreativeInventory_Overlay");
            overlayGO.transform.SetParent(canvas.transform, false);
            var overlayRT = overlayGO.AddComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            var overlayImg = overlayGO.AddComponent<Image>();
            overlayImg.color = _overlayBg;
            overlayImg.raycastTarget = true;
            _overlay = overlayGO;

            // Panel central
            var allBlocks = BlockFaceData.AllBlockIds;
            int rows  = Mathf.CeilToInt((float)allBlocks.Length / Columns);
            float gridW = Columns * SlotSize + (Columns - 1) * SlotGap;
            float gridH = rows   * SlotSize + (rows   - 1) * SlotGap;
            float panelW = gridW + Padding * 2f;
            float panelH = gridH + Padding * 2f + TitleH + 28f; // +28 = tooltip zone

            var panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(overlayGO.transform, false);
            var panelRT = panelGO.AddComponent<RectTransform>();
            panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
            panelRT.pivot            = new Vector2(0.5f, 0.5f);
            panelRT.anchoredPosition = Vector2.zero;
            panelRT.sizeDelta        = new Vector2(panelW, panelH);
            var panelImg = panelGO.AddComponent<Image>();
            panelImg.color = _panelBg;
            SetRounded(panelImg, Radius);

            // Titre
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(panelGO.transform, false);
            var titleRT = titleGO.AddComponent<RectTransform>();
            titleRT.anchorMin        = new Vector2(0f, 1f);
            titleRT.anchorMax        = new Vector2(1f, 1f);
            titleRT.pivot            = new Vector2(0.5f, 1f);
            titleRT.anchoredPosition = Vector2.zero;
            titleRT.sizeDelta        = new Vector2(0f, TitleH);
            var titleTxt = titleGO.AddComponent<Text>();
            titleTxt.text      = "INVENTAIRE CRÉATIF";
            titleTxt.font      = GetFont(13);
            titleTxt.fontSize  = 13;
            titleTxt.color     = _textPrimary;
            titleTxt.alignment = TextAnchor.MiddleCenter;

            // Zone tooltip en bas du panel
            var ttGO = new GameObject("Tooltip");
            ttGO.transform.SetParent(panelGO.transform, false);
            var ttRT = ttGO.AddComponent<RectTransform>();
            ttRT.anchorMin        = new Vector2(0f, 0f);
            ttRT.anchorMax        = new Vector2(1f, 0f);
            ttRT.pivot            = new Vector2(0.5f, 0f);
            ttRT.anchoredPosition = new Vector2(0f, 6f);
            ttRT.sizeDelta        = new Vector2(0f, 22f);
            _tooltip = ttGO.AddComponent<Text>();
            _tooltip.font      = GetFont(11);
            _tooltip.fontSize  = 11;
            _tooltip.color     = _textSub;
            _tooltip.alignment = TextAnchor.MiddleCenter;

            // Grille de blocs
            // Origine en bas-gauche de la zone de grille
            float gridOriginX = -gridW * 0.5f;
            float gridOriginY = -(TitleH) + (panelH * 0.5f) - Padding - gridH;

            for (int idx = 0; idx < allBlocks.Length; idx++)
            {
                byte bt     = allBlocks[idx];
                int  col    = idx % Columns;
                int  row    = idx / Columns;

                float x = gridOriginX + col * (SlotSize + SlotGap) + SlotSize * 0.5f;
                float y = gridOriginY + (rows - 1 - row) * (SlotSize + SlotGap) + SlotSize * 0.5f;

                BuildSlot(panelGO.transform, bt, new Vector2(x, y));
            }
        }

        private void BuildSlot(Transform parent, byte blockId, Vector2 pos)
        {
            var slotGO = new GameObject($"Slot_{blockId}");
            slotGO.transform.SetParent(parent, false);
            var slotRT = slotGO.AddComponent<RectTransform>();
            slotRT.anchorMin        = new Vector2(0.5f, 0f);
            slotRT.anchorMax        = new Vector2(0.5f, 0f);
            slotRT.pivot            = new Vector2(0.5f, 0f);
            slotRT.anchoredPosition = pos;
            slotRT.sizeDelta        = new Vector2(SlotSize, SlotSize);

            var slotImg = slotGO.AddComponent<Image>();
            slotImg.color        = _slotBg;
            slotImg.raycastTarget = true;
            SetRounded(slotImg, 8f);

            // Icône du bloc
            byte iconRid = BlockFaceData.GetIconRenderingId(blockId);
            Material mat = (_materials != null && iconRid < _materials.Length) ? _materials[iconRid] : null;

            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(slotGO.transform, false);
            var iconRT = iconGO.AddComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0.1f, 0.1f);
            iconRT.anchorMax = new Vector2(0.9f, 0.9f);
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;
            var rawImg = iconGO.AddComponent<RawImage>();

            if (mat != null && mat.mainTexture is Texture2D tex)
            {
                rawImg.texture = tex;
                rawImg.color   = Color.white;
            }
            else
            {
                Color col = BlockFaceData.GetFallbackColor((BlockType)blockId);
                var solidTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                solidTex.SetPixel(0, 0, col);
                solidTex.Apply();
                rawImg.texture = solidTex;
                rawImg.color   = Color.white;
            }
            rawImg.raycastTarget = false;

            // Evénements souris
            var trigger = slotGO.AddComponent<EventTrigger>();

            // Hover → afficher nom
            var onEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            var capturedId = blockId;
            var capturedImg = slotImg;
            onEnter.callback.AddListener(_ =>
            {
                capturedImg.color = _slotHover;
                if (_tooltip != null)
                    _tooltip.text = BlockFaceData.GetDisplayName(capturedId);
            });
            trigger.triggers.Add(onEnter);

            // Exit → cacher nom
            var onExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            onExit.callback.AddListener(_ =>
            {
                capturedImg.color = _slotBg;
                if (_tooltip != null) _tooltip.text = "";
            });
            trigger.triggers.Add(onExit);

            // Clic → assigner à la hotbar
            var onClick = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            onClick.callback.AddListener(_ =>
            {
                if (_blockInteract != null)
                    _blockInteract.SetHotbarSlot(_blockInteract.HotbarIndex, (BlockType)capturedId);
                Close();
            });
            trigger.triggers.Add(onClick);
        }

        // ── Helpers ───────────────────────────────────────────

        private static void SetRounded(Image img, float radius)
        {
            img.sprite = null;
            img.type   = Image.Type.Sliced;
            // Génère un sprite arrondi procédural via un pixel sprite
            // (Unity ne supporte pas les coin radius nativement sans sprite)
            // → on laisse le sprite null ; résultat = rectangle avec les bords gérés par UI
            // Pour un vrai arrondi, il faudrait utiliser UI Toolkit ou une sprite atlas.
            // Ici, on accepte un coin carré pour simplifier (cohérent avec le style).
            img.type = Image.Type.Simple;
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

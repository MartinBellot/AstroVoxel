// ============================================================
//  HudBuilder.cs
//  Construit le HUD complet en Dark Theme ultra-moderne façon Apple.
//
//  Architecture :
//  ┌─────────────────────────────────────────────────────────┐
//  │  Canvas (Screen Space Overlay, sort=10)                 │
//  │  ├── Crosshair   – centre écran                         │
//  │  ├── Hotbar      – bas centre, glassmorphism            │
//  │  ├── BlockLabel  – au-dessus de la hotbar, pill shape   │
//  │  └── InfoPanel   – haut gauche, carte frosted glass     │
//  └─────────────────────────────────────────────────────────┘
// ============================================================

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using AstroVoxel.VoxelEngine;

namespace AstroVoxel.Player
{
    /// <summary>
    /// Construit et anime le HUD Apple Dark Theme.
    /// Appelé uniquement depuis GameBootstrap.
    /// </summary>
    public sealed class HudBuilder : MonoBehaviour
    {
        // ── Palette Apple Dark ────────────────────────────────
        private static readonly Color _bg            = new Color(0.07f, 0.07f, 0.08f, 0.78f);
        private static readonly Color _bgDeep        = new Color(0.04f, 0.04f, 0.05f, 0.90f);
        private static readonly Color _border        = new Color(1f,    1f,    1f,    0.10f);
        private static readonly Color _accent        = new Color(0.00f, 0.48f, 1.00f, 1.00f);   // Apple Blue
        private static readonly Color _accentGlow    = new Color(0.00f, 0.48f, 1.00f, 0.25f);
        private static readonly Color _textPrimary   = new Color(1f,    1f,    1f,    0.92f);
        private static readonly Color _textSecondary = new Color(1f,    1f,    1f,    0.45f);
        private static readonly Color _white80       = new Color(1f,    1f,    1f,    0.80f);
        private static readonly Color _crosshair     = new Color(1f,    1f,    1f,    0.75f);

        // ── État runtime ──────────────────────────────────────
        private BlockInteraction _blockInteract;
        private Transform        _playerBody;

        private Image[]    _slotBg;
        private Image[]    _slotRim;
        private RawImage[] _slotIcon;
        private Color[]    _slotIconTint;   // teinte biome (vert herbe, feuillages…)
        private Text[]     _slotCount;      // compteur survie (haut-droite de chaque slot)
        private int        _visibleIndex = -1;
        private Material[]      _materials;
        private Material[]      _itemMaterials;
        private BlockType[]     _hotbarCache      = new BlockType[9];
        private int[]           _hotbarCountCache = new int[9];
        private ItemType[]      _hotbarItemTypeCache = new ItemType[9];
        private RectTransform[] _hotbarSlotRTs;

        private Text   _blockLabel;
        private Image  _blockLabelBg;
        private float  _labelFade;
        private float  _labelTimer;

        private Text   _fpsText;
        private Text   _posText;
        private Text   _blockText;
        private float  _fpsAccum;
        private int    _fpsFrames;
        private float  _fpsCurrent;
        private float  _fpsTimer;
        private const float FpsInterval = 0.25f;

        // Animation selection pulse
        private Coroutine _pulseCoroutine;

        // ── Survie ────────────────────────────────────────────────
        private GameObject _healthBarRoot;
        private Image[]    _heartIcons;       // 10 hearts (full/half/empty via color)
        private GameObject _miningBarRoot;
        private Image      _miningBarFill;

        // ── Public API ───────────────────────────────────────

        /// <summary>RectTransforms des slots hotbar pour la détection de drop drag&amp;drop.</summary>
        public RectTransform[] HotbarSlotRects => _hotbarSlotRTs;

        /// <summary>Fournit les matériaux des items (outils) pour l'affichage dans la hotbar.</summary>
        public void SetItemMaterials(Material[] mats) => _itemMaterials = mats;

        public void Init(
            Canvas canvas,
            BlockInteraction blockInteract,
            Transform playerBody,
            Material[] blockMaterials)
        {
            _blockInteract = blockInteract;
            _playerBody    = playerBody;
            _materials      = blockMaterials;

            BuildCrosshair(canvas);
            BuildHotbar(canvas, blockMaterials);
            BuildBlockLabel(canvas);
            BuildInfoPanel(canvas);
            BuildHealthBar(canvas);
            BuildMiningBar(canvas);

            // Abonnements Mode Survie
            GameModeManager.OnGameModeChanged += OnGameModeChanged;
            if (PlayerHealth.Instance != null)
                PlayerHealth.Instance.OnHealthChanged += OnHealthChanged;
        }

        // ── Update loop ───────────────────────────────────────

        private void Update()
        {
            UpdateFps();
            UpdateInfoTexts();
            UpdateHotbar();
            UpdateLabelFade();
            UpdateSurvivalHud();
        }

        // ─────────────────────────────────────────────────────
        //  FPS
        // ─────────────────────────────────────────────────────

        private void UpdateFps()
        {
            _fpsAccum  += Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f;
            _fpsFrames++;
            _fpsTimer  += Time.unscaledDeltaTime;

            if (_fpsTimer >= FpsInterval)
            {
                _fpsCurrent = _fpsAccum / _fpsFrames;
                _fpsAccum   = 0f;
                _fpsFrames  = 0;
                _fpsTimer   = 0f;
            }
        }

        private void UpdateInfoTexts()
        {
            if (_fpsText != null)
            {
                _fpsText.text = $"{_fpsCurrent:F0}";
                // Couleur dynamique: vert > 50, orange > 30, rouge < 30
                _fpsText.color = _fpsCurrent >= 50f
                    ? new Color(0.20f, 0.90f, 0.45f, 1f)
                    : _fpsCurrent >= 30f
                        ? new Color(1.00f, 0.70f, 0.10f, 1f)
                        : new Color(1.00f, 0.30f, 0.25f, 1f);
            }

            if (_posText != null && _playerBody != null)
            {
                Vector3 p = _playerBody.position;
                _posText.text = $"{p.x:F1}  {p.y:F1}  {p.z:F1}";
            }

            if (_blockText != null && _blockInteract != null)
            {
                Vector3? b = _blockInteract.TargetBlockPos;
                _blockText.text = b.HasValue
                    ? $"{(int)b.Value.x}  {(int)b.Value.y}  {(int)b.Value.z}"
                    : "—";
            }
        }

        // ─────────────────────────────────────────────────────
        //  Hotbar
        // ─────────────────────────────────────────────────────

        private void UpdateHotbar()
        {
            if (_blockInteract == null || _slotBg == null) return;

            if (GameModeManager.IsSurvival)
            {
                var survHotbar = SurvivalInventoryData.Instance.Hotbar;

                // Survie : détecte les changements par ItemType (blocs ET outils)
                for (int i = 0; i < 9 && i < _slotIcon.Length; i++)
                {
                    ItemType curType = survHotbar[i].IsEmpty ? ItemType.None : survHotbar[i].itemType;
                    if (curType == _hotbarItemTypeCache[i]) continue;
                    _hotbarItemTypeCache[i] = curType;

                    if (!survHotbar[i].IsEmpty && survHotbar[i].IsTool())
                    {
                        ApplyItemIcon(_slotIcon[i], survHotbar[i].itemType);
                        _hotbarCache[i] = BlockType.Air;
                    }
                    else
                    {
                        BlockType bt = survHotbar[i].IsEmpty ? BlockType.Air : survHotbar[i].ToBlockType();
                        _hotbarCache[i] = bt;
                        ApplyBlockColor(_slotIcon[i], bt, _materials);
                    }

                    if (_slotIconTint != null && i < _slotIconTint.Length)
                        _slotIconTint[i] = _slotIcon[i].color;
                    bool sel = (i == _visibleIndex);
                    var tint = _slotIconTint != null && i < _slotIconTint.Length
                        ? _slotIconTint[i] : Color.white;
                    _slotIcon[i].color = new Color(tint.r, tint.g, tint.b, sel ? 1f : 0.55f);
                }
            }
            else
            {
                // Créatif : blocs ET items non-blocs (Propulseur…)
                var hotbar = _blockInteract.Hotbar;
                for (int i = 0; i < hotbar.Length && i < _slotIcon.Length; i++)
                {
                    ItemType creativeItem = _blockInteract.GetCreativeHotbarItem(i);
                    bool changed = hotbar[i] != _hotbarCache[i]
                                || creativeItem != _hotbarItemTypeCache[i];
                    if (!changed) continue;

                    _hotbarCache[i]         = hotbar[i];
                    _hotbarItemTypeCache[i] = creativeItem;

                    if (creativeItem != ItemType.None)
                        ApplyItemIcon(_slotIcon[i], creativeItem);
                    else
                        ApplyBlockColor(_slotIcon[i], hotbar[i], _materials);

                    if (_slotIconTint != null && i < _slotIconTint.Length)
                        _slotIconTint[i] = _slotIcon[i].color;
                    bool selected = (i == _visibleIndex);
                    var t = _slotIconTint != null && i < _slotIconTint.Length
                        ? _slotIconTint[i] : Color.white;
                    _slotIcon[i].color = new Color(t.r, t.g, t.b, selected ? 1f : 0.55f);
                }
            }

            // Mettre à jour les compteurs survie
            if (GameModeManager.IsSurvival && _slotCount != null)
            {
                var survHotbar = SurvivalInventoryData.Instance.Hotbar;
                for (int i = 0; i < 9 && i < _slotCount.Length; i++)
                {
                    if (_slotCount[i] == null) continue;
                    int cnt = (!survHotbar[i].IsEmpty) ? survHotbar[i].count : 0;
                    if (cnt != _hotbarCountCache[i])
                    {
                        _hotbarCountCache[i] = cnt;
                        _slotCount[i].text   = cnt > 1 ? cnt.ToString() : "";
                    }
                }
            }

            int idx = _blockInteract.HotbarIndex;
            if (idx == _visibleIndex) return;

            // Désélectionne l'ancien
            if (_visibleIndex >= 0 && _visibleIndex < _slotBg.Length)
                AnimateSlot(_visibleIndex, false);

            _visibleIndex = idx;
            AnimateSlot(_visibleIndex, true);

            // Label du bloc/item sélectionné
            if (GameModeManager.IsSurvival)
            {
                var survHotbar = SurvivalInventoryData.Instance.Hotbar;
                ItemStack activeStack = (idx >= 0 && idx < survHotbar.Length) ? survHotbar[idx] : ItemStack.Empty;
                string labelName = activeStack.IsEmpty
                    ? ""
                    : activeStack.IsTool()
                        ? ItemTypeHelper.GetDisplayName(activeStack.itemType)
                        : BlockFaceData.GetDisplayName((byte)(int)activeStack.itemType);
                ShowBlockLabel(labelName);
            }
            else
            {
                ItemType creativeItem = _blockInteract.GetCreativeHotbarItem(idx);
                if (creativeItem != ItemType.None)
                    ShowBlockLabel(ItemTypeHelper.GetDisplayName(creativeItem));
                else
                    ShowBlockLabel(BlockFaceData.GetDisplayName((byte)_blockInteract.ActiveBlock));
            }
        }

        private void AnimateSlot(int i, bool selected)
        {
            if (_slotBg == null || i < 0 || i >= _slotBg.Length) return;

            _slotBg[i].color  = selected ? new Color(0.10f, 0.10f, 0.12f, 0.95f) : _bg;
            _slotRim[i].color = selected ? _accent : _border;

            if (_slotIcon != null && i < _slotIcon.Length)
            {
                var t = _slotIconTint != null && i < _slotIconTint.Length
                    ? _slotIconTint[i] : Color.white;
                _slotIcon[i].color = new Color(t.r, t.g, t.b, selected ? 1f : 0.55f);
            }
        }

        // ─────────────────────────────────────────────────────
        //  Block Label fade
        // ─────────────────────────────────────────────────────

        private void ShowBlockLabel(string name)
        {
            if (_blockLabel == null) return;
            _blockLabel.text  = name.ToUpperInvariant();
            _labelTimer       = 2.0f;   // visible 2 secondes
        }

        private void UpdateLabelFade()
        {
            if (_blockLabelBg == null) return;

            _labelTimer -= Time.unscaledDeltaTime;
            float alpha  = Mathf.Clamp01(_labelTimer / 0.4f);   // fondu sur 0.4 s

            Color bg = _blockLabelBg.color;
            bg.a = alpha * 0.82f;
            _blockLabelBg.color = bg;

            if (_blockLabel != null)
            {
                Color tc = _blockLabel.color;
                tc.a = alpha;
                _blockLabel.color = tc;
            }
        }

        // ─────────────────────────────────────────────────────
        //  BUILD: Health Bar (10 cœurs, visible en mode Survie)
        // ─────────────────────────────────────────────────────

        private void BuildHealthBar(Canvas canvas)
        {
            const float heartW   = 18f;
            const float heartH   = 18f;
            const float gap      = 3f;
            const float totalW   = 10 * heartW + 9 * gap;
            const float yOffset  = 16f + (58f + 20f) + 36f;  // au-dessus du BlockLabel

            var root = MakeRootRT("HealthBar", canvas.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, yOffset),
                new Vector2(totalW, heartH));

            _heartIcons = new Image[10];
            float startX = -(totalW * 0.5f) + heartW * 0.5f;

            for (int i = 0; i < 10; i++)
            {
                float xPos  = startX + i * (heartW + gap);
                var heartGO = new GameObject($"Heart_{i}");
                heartGO.transform.SetParent(root, false);
                var heartRT         = heartGO.AddComponent<RectTransform>();
                heartRT.anchorMin   = new Vector2(0.5f, 0.5f);
                heartRT.anchorMax   = new Vector2(0.5f, 0.5f);
                heartRT.pivot       = new Vector2(0.5f, 0.5f);
                heartRT.sizeDelta   = new Vector2(heartW, heartH);
                heartRT.anchoredPosition = new Vector2(xPos, 0f);
                var heartImg        = heartGO.AddComponent<Image>();
                heartImg.color      = HeartColor(i, PlayerHealth.MaxHealth);
                _heartIcons[i]      = heartImg;
            }

            _healthBarRoot = root.gameObject;
            _healthBarRoot.SetActive(GameModeManager.IsSurvival);
        }

        private static Color HeartColor(int heartIndex, int currentHp)
        {
            // chaque cœur = 2 HP
            int heartHp = (heartIndex + 1) * 2;
            if (currentHp >= heartHp)          return new Color(0.90f, 0.15f, 0.15f, 1f);  // plein
            if (currentHp >= heartHp - 1)      return new Color(0.90f, 0.50f, 0.10f, 1f);  // demi
            return                                     new Color(0.30f, 0.30f, 0.32f, 1f);  // vide
        }

        private void OnHealthChanged(int current, int max)
        {
            if (_heartIcons == null) return;
            for (int i = 0; i < _heartIcons.Length; i++)
                if (_heartIcons[i] != null)
                    _heartIcons[i].color = HeartColor(i, current);
        }

        private void OnGameModeChanged(GameMode mode)
        {
            if (_healthBarRoot != null)
                _healthBarRoot.SetActive(mode == GameMode.Survival);
            if (_miningBarRoot != null)
                _miningBarRoot.SetActive(false); // cache la barre immédiatement
            // Effacer les compteurs de la hotbar si on revient en créatif
            if (mode != GameMode.Survival && _slotCount != null)
            {
                for (int i = 0; i < _slotCount.Length; i++)
                {
                    if (_slotCount[i] != null) _slotCount[i].text = "";
                    _hotbarCountCache[i] = 0;
                }
            }
        }

        // ─────────────────────────────────────────────────────
        //  BUILD: Mining Progress Bar (sous le crosshair)
        // ─────────────────────────────────────────────────────

        private void BuildMiningBar(Canvas canvas)
        {
            const float barW   = 100f;
            const float barH   = 6f;
            const float yBelowCrosshair = -28f;

            var root = MakeRootRT("MiningBarRoot", canvas.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, yBelowCrosshair),
                new Vector2(barW, barH));

            // Fond
            var bgImg   = root.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.10f, 0.10f, 0.12f, 0.85f);
            MakeRounded(bgImg, 3f);

            // Remplissage
            var fillGO   = new GameObject("MiningFill");
            fillGO.transform.SetParent(root, false);
            var fillRT   = fillGO.AddComponent<RectTransform>();
            fillRT.anchorMin = new Vector2(0f, 0f);
            fillRT.anchorMax = new Vector2(0f, 1f);  // width via sizeDelta.x
            fillRT.pivot     = new Vector2(0f, 0.5f);
            fillRT.anchoredPosition = new Vector2(0f, 0f);
            fillRT.sizeDelta        = new Vector2(0f, 0f);
            _miningBarFill  = fillGO.AddComponent<Image>();
            _miningBarFill.color = new Color(0.95f, 0.75f, 0.15f, 1f);

            _miningBarRoot = root.gameObject;
            _miningBarRoot.SetActive(false);
        }

        private void UpdateSurvivalHud()
        {
            if (!GameModeManager.IsSurvival) return;

            // Barre de minage
            float progress = _blockInteract != null ? _blockInteract.MineProgress : 0f;
            if (_miningBarRoot != null)
                _miningBarRoot.SetActive(progress > 0.001f);

            if (_miningBarFill != null && progress > 0.001f)
            {
                var rt = _miningBarFill.GetComponent<RectTransform>();
                if (rt != null)
                {
                    float parentW = _miningBarRoot.GetComponent<RectTransform>().sizeDelta.x;
                    rt.sizeDelta  = new Vector2(parentW * progress, 0f);
                }
            }
        }

        // ─────────────────────────────────────────────────────
        //  BUILD: Crosshair
        // ─────────────────────────────────────────────────────

        private void BuildCrosshair(Canvas canvas)
        {
            var root = MakeRootRT("Crosshair", canvas.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));

            // Dot central
            var dot = CreateRect("Dot", root, Vector2.zero, new Vector2(4f, 4f), _crosshair);
            MakeRounded(dot, 2f);

            // Bras horizontaux (gap au centre)
            CreateRect("H_Left",  root, new Vector2(-9f,  0f), new Vector2(7f, 1.5f), _crosshair);
            CreateRect("H_Right", root, new Vector2( 9f,  0f), new Vector2(7f, 1.5f), _crosshair);
            // Bras verticaux
            CreateRect("V_Top",    root, new Vector2(0f,  9f), new Vector2(1.5f, 7f), _crosshair);
            CreateRect("V_Bottom", root, new Vector2(0f, -9f), new Vector2(1.5f, 7f), _crosshair);
        }

        // ─────────────────────────────────────────────────────
        //  BUILD: Hotbar
        // ─────────────────────────────────────────────────────

        private void BuildHotbar(Canvas canvas, Material[] blockMaterials)
        {
            var hotbar = _blockInteract != null ? _blockInteract.Hotbar : new BlockType[9];
            int count   = hotbar.Length;

            const float slotSize = 58f;
            const float gap      = 6f;
            const float padding  = 10f;
            const float radius   = 18f;

            float totalW = padding * 2f + count * slotSize + (count - 1) * gap;
            float totalH = slotSize + padding * 2f;

            // Fond principal glassmorphism
            var hotbarRoot = MakeRootRT("Hotbar", canvas.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 16f),
                new Vector2(totalW, totalH));

            var hotbarBg = hotbarRoot.GetComponent<Image>();
            if (hotbarBg == null) hotbarBg = hotbarRoot.gameObject.AddComponent<Image>();
            hotbarBg.color = _bgDeep;
            MakeRounded(hotbarBg, radius);

            _slotBg        = new Image[count];
            _slotRim       = new Image[count];
            _slotIcon      = new RawImage[count];
            _slotIconTint  = new Color[count];
            _slotCount     = new Text[count];
            _hotbarSlotRTs = new RectTransform[count];

            float startX = -(totalW * 0.5f) + padding + slotSize * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float xPos = startX + i * (slotSize + gap);

                // Fond du slot (arrondi)
                var slotBgGO = CreateRect($"Slot_BG_{i}", hotbarRoot,
                    new Vector2(xPos, 0f), new Vector2(slotSize, slotSize), _bg);
                _slotBg[i]       = slotBgGO.GetComponent<Image>();
                _hotbarSlotRTs[i] = slotBgGO.GetComponent<RectTransform>();
                MakeRounded(_slotBg[i], 12f);

                // Rim du slot (sélection = accent bleu)
                var slotRimGO = CreateRect($"Slot_Rim_{i}", hotbarRoot,
                    new Vector2(xPos, 0f), new Vector2(slotSize, slotSize), _border);
                _slotRim[i] = slotRimGO.GetComponent<Image>();
                MakeRounded(_slotRim[i], 12f);
                _slotRim[i].fillCenter = false;

                // Icône du bloc
                var iconGO = new GameObject($"Slot_Icon_{i}");
                iconGO.transform.SetParent(hotbarRoot, false);
                var iconRT = iconGO.AddComponent<RectTransform>();
                iconRT.anchorMin        = new Vector2(0.5f, 0.5f);
                iconRT.anchorMax        = new Vector2(0.5f, 0.5f);
                iconRT.pivot            = new Vector2(0.5f, 0.5f);
                iconRT.sizeDelta        = new Vector2(slotSize - 18f, slotSize - 18f);
                iconRT.anchoredPosition = new Vector2(xPos, 0f);

                var rawImg = iconGO.AddComponent<RawImage>();
                ApplyBlockColor(rawImg, hotbar[i], blockMaterials);
                _slotIcon[i]     = rawImg;
                _slotIconTint[i] = rawImg.color;
                if (i < _hotbarCache.Length) _hotbarCache[i] = hotbar[i];

                // Numéro du slot (petit label en bas)
                var numGO  = new GameObject($"Slot_Num_{i}");
                numGO.transform.SetParent(hotbarRoot, false);
                var numRT = numGO.AddComponent<RectTransform>();
                numRT.anchorMin        = new Vector2(0.5f, 0.5f);
                numRT.anchorMax        = new Vector2(0.5f, 0.5f);
                numRT.pivot            = new Vector2(0.5f, 0.5f);
                numRT.sizeDelta        = new Vector2(slotSize, 14f);
                numRT.anchoredPosition = new Vector2(xPos, -(slotSize * 0.5f - 9f));
                var numTxt = numGO.AddComponent<Text>();
                numTxt.text      = (i + 1).ToString();
                numTxt.fontSize  = 10;
                numTxt.color     = _textSecondary;
                numTxt.alignment = TextAnchor.MiddleCenter;
                numTxt.font      = GetFont(10);

                // Compteur d'items (haut-droite, visible en survie uniquement)
                var cntGO = new GameObject($"Slot_Count_{i}");
                cntGO.transform.SetParent(hotbarRoot, false);
                var cntRT = cntGO.AddComponent<RectTransform>();
                cntRT.anchorMin        = new Vector2(0.5f, 0.5f);
                cntRT.anchorMax        = new Vector2(0.5f, 0.5f);
                cntRT.pivot            = new Vector2(1f, 1f);
                cntRT.sizeDelta        = new Vector2(slotSize - 10f, 14f);
                cntRT.anchoredPosition = new Vector2(xPos + slotSize * 0.5f - 3f, slotSize * 0.5f - 3f);
                var cntTxt = cntGO.AddComponent<Text>();
                cntTxt.text          = "";
                cntTxt.fontSize      = 10;
                cntTxt.color         = new Color(1f, 0.85f, 0.30f, 1f);
                cntTxt.alignment     = TextAnchor.UpperRight;
                cntTxt.font          = GetFont(10);
                cntTxt.raycastTarget = false;
                _slotCount[i]        = cntTxt;
            }

            // Initialise la sélection visuelle immédiatement
            _visibleIndex = 0;
            AnimateSlot(0, true);
        }

        // ─────────────────────────────────────────────────────
        //  BUILD: BlockLabel (pill au-dessus hotbar)
        // ─────────────────────────────────────────────────────

        private void BuildBlockLabel(Canvas canvas)
        {
            const float w = 160f;
            const float h = 28f;

            // Position juste au-dessus de la hotbar
            var labelRoot = MakeRootRT("BlockLabel", canvas.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 16f + (58f + 20f) + 8f),
                new Vector2(w, h));

            _blockLabelBg = labelRoot.GetComponent<Image>();
            if (_blockLabelBg == null) _blockLabelBg = labelRoot.gameObject.AddComponent<Image>();
            _blockLabelBg.color = new Color(0.04f, 0.04f, 0.05f, 0f);
            MakeRounded(_blockLabelBg, h * 0.5f);

            var textGO = new GameObject("LabelText");
            textGO.transform.SetParent(labelRoot, false);
            var textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;
            _blockLabel = textGO.AddComponent<Text>();
            _blockLabel.font      = GetFont(12);
            _blockLabel.fontSize  = 12;
            _blockLabel.color     = new Color(1f, 1f, 1f, 0f);
            _blockLabel.alignment = TextAnchor.MiddleCenter;

            // Force affichage initial
            _labelTimer = 0f;
        }

        // ─────────────────────────────────────────────────────
        //  BUILD: Info Panel (haut gauche)
        // ─────────────────────────────────────────────────────

        private void BuildInfoPanel(Canvas canvas)
        {
            const float w       = 210f;
            const float h       = 94f;
            const float radius  = 14f;
            const float margin  = 14f;

            var panel = MakeRootRT("InfoPanel", canvas.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(margin, -margin),
                new Vector2(w, h));

            // Fond glassmorphism
            var panelBg = panel.GetComponent<Image>();
            if (panelBg == null) panelBg = panel.gameObject.AddComponent<Image>();
            panelBg.color = _bgDeep;
            MakeRounded(panelBg, radius);


            const float rowH    = 24f;
            const float padX    = 14f;
            const float padY    = 10f;

            // Ligne 1 : FPS
            BuildInfoRow(panel, 0, padX, padY, rowH, w, "FPS", out _fpsText);
            // Ligne 2 : XYZ
            BuildInfoRow(panel, 1, padX, padY, rowH, w, "XYZ", out _posText);
            // Ligne 3 : BLOC
            BuildInfoRow(panel, 2, padX, padY, rowH, w, "BLK", out _blockText);
        }

        private void BuildInfoRow(
            Transform parent, int row,
            float padX, float padY, float rowH, float panelW,
            string label, out Text valueText)
        {
            float y = -(padY + row * rowH + rowH * 0.5f);

            // Label gris (clé)
            var lblGO = new GameObject($"Row_{label}_Key");
            lblGO.transform.SetParent(parent, false);
            var lblRT = lblGO.AddComponent<RectTransform>();
            lblRT.anchorMin        = new Vector2(0f, 1f);
            lblRT.anchorMax        = new Vector2(0f, 1f);
            lblRT.pivot            = new Vector2(0f, 0.5f);
            lblRT.anchoredPosition = new Vector2(padX, y);
            lblRT.sizeDelta        = new Vector2(36f, rowH);
            var lblTxt = lblGO.AddComponent<Text>();
            lblTxt.text      = label;
            lblTxt.font      = GetFont(11);
            lblTxt.fontSize  = 11;
            lblTxt.color     = _textSecondary;
            lblTxt.alignment = TextAnchor.MiddleLeft;

            // Valeur blanche (valeur dynamique)
            var valGO = new GameObject($"Row_{label}_Val");
            valGO.transform.SetParent(parent, false);
            var valRT = valGO.AddComponent<RectTransform>();
            valRT.anchorMin        = new Vector2(0f, 1f);
            valRT.anchorMax        = new Vector2(1f, 1f);
            valRT.pivot            = new Vector2(0f, 0.5f);
            valRT.anchoredPosition = new Vector2(padX + 40f, y);
            valRT.sizeDelta        = new Vector2(-(padX * 2f + 40f), rowH);
            valueText = valGO.AddComponent<Text>();
            valueText.text      = "—";
            valueText.font      = GetFont(11);
            valueText.fontSize  = 11;
            valueText.color     = _textPrimary;
            valueText.alignment = TextAnchor.MiddleLeft;
        }

        // ─────────────────────────────────────────────────────
        //  Helpers UI
        // ─────────────────────────────────────────────────────

        /// <summary>Crée un RectTransform ancré et retourne sa référence.</summary>
        private static Transform MakeRootRT(
            string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin        = anchorMin;
            rt.anchorMax        = anchorMax;
            rt.pivot            = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = sizeDelta;
            return go.transform;
        }

        /// <summary>Crée un rect enfant centré sur un anchoredPosition.</summary>
        private static Image CreateRect(
            string name, Transform parent, Vector2 anchoredPos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var rt   = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0.5f, 0.5f);
            rt.anchorMax        = new Vector2(0.5f, 0.5f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = size;
            rt.anchoredPosition = anchoredPos;
            return img;
        }

        /// <summary>Arrondit les coins d'une Image via Sprite 9-slice généré procéduralement.</summary>
        private static void MakeRounded(Image img, float radius)
        {
            img.sprite = CreateRoundedSprite(Mathf.RoundToInt(radius));
            img.type   = Image.Type.Sliced;
        }

        /// <summary>Génère un Sprite Texture2D avec coins arrondis.</summary>
        private static Sprite CreateRoundedSprite(int radius)
        {
            const int texSize = 64;
            int r = Mathf.Clamp(radius, 1, texSize / 2);

            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;

            var pixels = new Color32[texSize * texSize];

            for (int py = 0; py < texSize; py++)
            {
                for (int px = 0; px < texSize; px++)
                {
                    // Distance au coin le plus proche
                    float cx = px < r ? r : (px >= texSize - r ? texSize - r - 1 : px);
                    float cy = py < r ? r : (py >= texSize - r ? texSize - r - 1 : py);

                    float dx = px - cx;
                    float dy = py - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // Anti-aliasing sur 1 pixel
                    float alpha = Mathf.Clamp01(r - dist + 0.5f);
                    pixels[py * texSize + px] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            // Border = radius pour un 9-slice correct
            var sprite = Sprite.Create(tex,
                new Rect(0, 0, texSize, texSize),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(r, r, r, r));

            return sprite;
        }

        private static void ApplyBlockColor(RawImage icon, BlockType blockType, Material[] materials)
        {
            // Utilise l'ID de rendu "icône" (top/log-top pour les blocs multi-faces)
            byte iconRid = BlockFaceData.GetIconRenderingId((byte)blockType);
            Material mat = (materials != null && iconRid < materials.Length) ? materials[iconRid] : null;

            if (mat != null && mat.mainTexture is Texture2D tex)
            {
                icon.texture = tex;
                icon.color   = mat.color;   // Applique la teinte biome (_BaseColor du matériau)
                return;
            }

            Color col = BlockFaceData.GetFallbackColor(blockType);
            // Texture 1x1 solid
            var solidTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            solidTex.SetPixel(0, 0, col);
            solidTex.Apply();
            icon.texture = solidTex;
            icon.color   = Color.white;
        }

        private static Font GetFont(int size)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("SF Pro Display", size);
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Helvetica Neue", size);
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", size);
            return font;
        }

        private void ApplyItemIcon(RawImage icon, ItemType itemType)
        {
            int idx = (int)itemType - 300;
            if (_itemMaterials != null && idx >= 0 && idx < _itemMaterials.Length && _itemMaterials[idx] != null)
            {
                var mat = _itemMaterials[idx];
                if (mat.mainTexture is Texture2D tex)
                {
                    icon.texture = tex;
                    icon.color   = Color.white;
                    return;
                }
            }
            // Fallback : couleur unie selon le type d'outil
            Color col = GetToolColor(itemType);
            var solidTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            solidTex.SetPixel(0, 0, col);
            solidTex.Apply();
            icon.texture = solidTex;
            icon.color   = Color.white;
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
                case ItemType.Propulseur:    return new Color(1.00f, 0.50f, 0.05f);   // orange thruster
                default:                     return Color.gray;
            }
        }
    }
}

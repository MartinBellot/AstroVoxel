// ============================================================
//  MainMenu.cs
//  Menu principal d'AstroVoxel.
//  Construit entièrement par code (cohérent avec GameBootstrap).
//  Affiché avant la construction du monde si skipMenu = false.
//
//  Flow :
//    GameBootstrap.Awake() → MainMenu.Show(bootstrap, callback)
//    → Joueur choisit → callback(MenuResult) → StartWorld()
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using AstroVoxel.Save;
using AstroVoxel.Player;
using AstroVoxel.Network;

namespace AstroVoxel.Bootstrap
{
    /// <summary>
    /// Menu principal : sélection de save, création de monde, multijoueur.
    /// MonoBehaviour créé par GameBootstrap avant la construction du monde.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        // ── Résultat du menu ──────────────────────────────────

        public struct MenuResult
        {
            /// <summary>true = charger une save existante.</summary>
            public bool     IsLoad;
            /// <summary>Nom de la save (IsLoad=true).</summary>
            public string   SaveName;
            /// <summary>Seed personnalisée ; null = aléatoire (IsLoad=false).</summary>
            public int?     CustomSeed;
            /// <summary>Mode de jeu pour un nouveau monde.</summary>
            public GameMode GameMode;
            /// <summary>true = héberger un serveur après démarrage.</summary>
            public bool     IsHost;
            /// <summary>Code de connexion pour rejoindre (null = pas de join).</summary>
            public string   JoinCode;
        }

        // ── Palette Apple Dark (identique à GameConsole) ──────
        private static readonly Color _bg      = new Color(0.04f, 0.04f, 0.06f, 1.00f);
        private static readonly Color _card    = new Color(0.09f, 0.09f, 0.12f, 0.97f);
        private static readonly Color _cardAlt = new Color(0.05f, 0.05f, 0.08f, 0.97f);
        private static readonly Color _input   = new Color(0.08f, 0.08f, 0.11f, 1.00f);
        private static readonly Color _primary = new Color(0.90f, 0.90f, 0.92f, 1.00f);
        private static readonly Color _sub     = new Color(0.52f, 0.52f, 0.57f, 1.00f);
        private static readonly Color _blue    = new Color(0.18f, 0.53f, 1.00f, 1.00f);
        private static readonly Color _orange  = new Color(1.00f, 0.58f, 0.12f, 1.00f);
        private static readonly Color _red     = new Color(1.00f, 0.23f, 0.19f, 1.00f);
        private static readonly Color _green   = new Color(0.20f, 0.78f, 0.35f, 1.00f);

        // ── Animation ─────────────────────────────────────────
        private const float ANIM = 0.18f;

        // ── État ──────────────────────────────────────────────
        private Action<MenuResult> _callback;
        private RectTransform      _rootRT;

        // Panels
        private GameObject _mainPanel;
        private GameObject _playPanel;
        private GameObject _createPanel;
        private GameObject _multiPanel;
        private GameObject _deleteConfirmPanel;

        // État play panel
        private string     _selectedSave;
        private GameObject _saveCardsContainer;
        private GameObject _saveActionBar;
        private Text       _saveActionLabel;

        // État create panel
        private InputField _createNameInput;
        private InputField _createSeedInput;
        private bool       _createIsSurvival;
        private bool       _createIsMulti;
        private Text       _toggleModeLabel;
        private Text       _toggleMultiLabel;
        private Image      _toggleModeImg;
        private Image      _toggleMultiImg;

        // État multi panel
        private InputField _joinCodeInput;
        private string     _multiSaveName;           // save sélectionnée (ou null si nouveau monde)
        private bool       _multiFromCreate;
        private MenuResult _pendingNewWorldResult;   // résultat partiel du create panel
        private Text       _multiContextLabel;

        // Delete confirm
        private Action _deleteConfirmAction;
        private Text   _deleteConfirmMsg;

        // Étoiles (twinkle)
        private readonly List<Image> _stars = new List<Image>();

        // ── Entrée publique ───────────────────────────────────

        /// <summary>Crée et affiche le menu. Le callback est invoqué à la validation.</summary>
        public static void Show(MonoBehaviour owner, Action<MenuResult> callback)
        {
            EnsureEventSystem();
            var go   = new GameObject("MainMenu");
            var menu = go.AddComponent<MainMenu>();
            menu.Init(callback);
        }

        // ── Initialisation ────────────────────────────────────

        private void Init(Action<MenuResult> callback)
        {
            _callback = callback;
            BuildCanvas();
            BuildBackground();
            BuildMainPanel();
            BuildPlayPanel();
            BuildCreatePanel();
            BuildMultiPanel();
            BuildDeleteConfirmPanel();
            ShowPanel(_mainPanel);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        // ── Canvas ────────────────────────────────────────────

        private void BuildCanvas()
        {
            // Caméra de fond : évite le message "no camera rendering"
            var camGO = new GameObject("MenuCamera");
            camGO.transform.SetParent(transform, false);
            var cam             = camGO.AddComponent<Camera>();
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask     = 0;
            cam.depth           = -1;

            var canvasGO = new GameObject("MenuCanvas");
            canvasGO.transform.SetParent(transform, false);

            var canvas         = canvasGO.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler                    = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode            = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution    = new Vector2(1920f, 1080f);
            scaler.screenMatchMode        = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight     = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            // Root (plein écran) pour animer en fondu global
            var rootGO = new GameObject("Root");
            rootGO.transform.SetParent(canvasGO.transform, false);
            rootGO.AddComponent<Image>().color = Color.clear;   // RT sans fond visible
            _rootRT = rootGO.GetComponent<RectTransform>();
            SetStretch(_rootRT);
        }

        // ── Background + étoiles ──────────────────────────────

        private void BuildBackground()
        {
            // Fond sombre
            var bgImg = MakeImage("Background", _rootRT);
            bgImg.color = _bg;
            SetStretch(bgImg.rectTransform);

            // Gradient sur la moitié haute
            var gradImg = MakeImage("Gradient", bgImg.rectTransform);
            gradImg.color = new Color(0.02f, 0.02f, 0.04f, 0.55f);
            var grt = gradImg.rectTransform;
            grt.anchorMin = new Vector2(0f, 0.45f);
            grt.anchorMax = Vector2.one;
            grt.offsetMin = Vector2.zero;
            grt.offsetMax = Vector2.zero;

            // Étoiles procédurales
            var rng = new System.Random(42);
            for (int i = 0; i < 90; i++)
            {
                var starImg = MakeImage($"Star_{i}", bgImg.rectTransform);
                var srt     = starImg.rectTransform;
                float ax    = (float)rng.NextDouble();
                float ay    = (float)rng.NextDouble();
                srt.anchorMin    = new Vector2(ax, ay);
                srt.anchorMax    = new Vector2(ax, ay);
                srt.pivot        = new Vector2(0.5f, 0.5f);
                float sz         = (float)rng.NextDouble() * 2.5f + 0.5f;
                srt.sizeDelta    = new Vector2(sz, sz);
                float bright     = (float)rng.NextDouble() * 0.6f + 0.3f;
                starImg.color    = new Color(bright, bright, bright + 0.1f, 1f);
                _stars.Add(starImg);
            }
        }

        // ── Update (twinkle des étoiles) ──────────────────────

        private void Update()
        {
            float t = Time.unscaledTime;
            for (int i = 0; i < _stars.Count; i++)
            {
                if (_stars[i] == null) continue;
                float a  = 0.25f + 0.45f * Mathf.Abs(Mathf.Sin(t * 0.4f + i * 1.37f));
                Color c  = _stars[i].color;
                c.a      = a;
                _stars[i].color = c;
            }
        }

        // ─────────────────────────────────────────────────────
        // MAIN PANEL
        // ─────────────────────────────────────────────────────

        private void BuildMainPanel()
        {
            _mainPanel = MakePanel("MainPanel");
            _mainPanel.AddComponent<CanvasGroup>();

            // Colonne centrale fixe
            var col    = MakeContainer("CenterCol", _mainPanel.transform);
            var colRT  = col.GetComponent<RectTransform>();
            colRT.anchorMin        = new Vector2(0.5f, 0.5f);
            colRT.anchorMax        = new Vector2(0.5f, 0.5f);
            colRT.pivot            = new Vector2(0.5f, 0.5f);
            colRT.sizeDelta        = new Vector2(380f, 490f);
            var colVLG             = col.GetComponent<VerticalLayoutGroup>();
            colVLG.childAlignment  = TextAnchor.UpperCenter;
            colVLG.spacing         = 0f;

            // Titre
            MakeText("Title", col.transform, "ASTROVOXEL", Color.white, 54, FontStyle.Bold, TextAnchor.MiddleCenter, 0f, 80f);
            // Sous-titre
            MakeText("Sub", col.transform, "Explorer l'infini, un voxel à la fois", _sub, 14, FontStyle.Normal, TextAnchor.MiddleCenter, 0f, 28f);
            MakeSpacer(col.transform, 36f);

            // Boutons
            MakePrimaryButton("BtnPlay",   col.transform, "Jouer",          _blue,  () => TransitionTo(_mainPanel, _playPanel));
            MakeSpacer(col.transform, 12f);
            MakePrimaryButton("BtnCreate", col.transform, "Créer un monde", _card,  () => { _multiSaveName = null; TransitionTo(_mainPanel, _createPanel); }, outline: true);
            MakeSpacer(col.transform, 12f);
            MakePrimaryButton("BtnQuit",   col.transform, "Quitter",        _red,   OnQuit);

            // Version (coin bas gauche)
            var verLblGO = new GameObject("Version");
            verLblGO.transform.SetParent(_rootRT, false);
            var verT     = verLblGO.AddComponent<Text>();
            var vrt      = verT.rectTransform;
            vrt.anchorMin  = new Vector2(0.01f, 0.01f);
            vrt.anchorMax  = new Vector2(0.25f, 0.04f);
            vrt.offsetMin  = Vector2.zero;
            vrt.offsetMax  = Vector2.zero;
            verT.text      = "AstroVoxel · v0.1";
            verT.font      = GetFont();
            verT.fontSize  = 11;
            verT.color     = _sub;
            verT.alignment = TextAnchor.LowerLeft;
        }

        // ─────────────────────────────────────────────────────
        // PLAY PANEL (sélection de saves)
        // ─────────────────────────────────────────────────────

        private void BuildPlayPanel()
        {
            _playPanel = MakePanel("PlayPanel");
            _playPanel.AddComponent<CanvasGroup>();

            var card   = MakeCard("Card", _playPanel.transform, new Vector2(700f, 560f));
            var cardRT = card.GetComponent<RectTransform>();

            // ── Header ──
            var hdr    = MakeHRow("Header", cardRT, new RectOffset(24, 24, 14, 0), 50f, anchorTop: true);
            MakeText("Title", hdr.transform, "Mes Mondes", _primary, 20, FontStyle.Bold, TextAnchor.MiddleLeft, 200f, 40f);
            MakeFill(hdr.transform);
            MakeSmallBtn("BtnNew",  hdr.transform, "+ Nouveau",  new Vector2(118f, 36f), _blue,  () => { _multiSaveName = null; TransitionTo(_playPanel, _createPanel); });
            MakeSmallBtn("BtnBack", hdr.transform, "← Retour",  new Vector2(100f, 36f), _card,  () => TransitionTo(_playPanel, _mainPanel), outline: true);

            // ── ScrollView ──
            var scrollGO  = new GameObject("ScrollView");
            scrollGO.transform.SetParent(cardRT, false);
            var scrollImg = scrollGO.AddComponent<Image>(); scrollImg.color = Color.clear;
            var scrollRT  = scrollImg.rectTransform;
            scrollRT.anchorMin = new Vector2(0f, 0f);
            scrollRT.anchorMax = new Vector2(1f, 1f);
            scrollRT.offsetMin = new Vector2(16f, 78f);
            scrollRT.offsetMax = new Vector2(-16f, -62f);
            var scroll         = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal  = false;
            scroll.vertical    = true;
            scroll.scrollSensitivity = 20f;
            scroll.movementType      = ScrollRect.MovementType.Clamped;

            // Viewport
            var vpGO  = new GameObject("Viewport");
            vpGO.transform.SetParent(scrollGO.transform, false);
            var vpImg = vpGO.AddComponent<Image>(); vpImg.color = Color.clear;
            var vpRT  = vpImg.rectTransform;
            SetStretch(vpRT);
            vpGO.AddComponent<RectMask2D>();
            scroll.viewport = vpRT;

            // Content
            var contentGO  = new GameObject("Content");
            contentGO.transform.SetParent(vpGO.transform, false);
            var contentImg = contentGO.AddComponent<Image>(); contentImg.color = Color.clear;
            var contentRT  = contentImg.rectTransform;
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot     = new Vector2(0.5f, 1f);
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;
            var vlg  = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.padding = new RectOffset(0, 0, 4, 4);
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRT;
            _saveCardsContainer = contentGO;

            // ── Barre d'actions (apparaît quand une save est sélectionnée) ──
            _saveActionBar = new GameObject("ActionBar");
            _saveActionBar.transform.SetParent(cardRT, false);
            var abImg = _saveActionBar.AddComponent<Image>(); abImg.color = new Color(0.06f, 0.06f, 0.09f, 0.98f);
            var abRT  = abImg.rectTransform;
            abRT.anchorMin = new Vector2(0f, 0f);
            abRT.anchorMax = new Vector2(1f, 0f);
            abRT.pivot     = new Vector2(0.5f, 0f);
            abRT.offsetMin = new Vector2(0f, 0f);
            abRT.offsetMax = new Vector2(0f, 70f);
            var abHLG = _saveActionBar.AddComponent<HorizontalLayoutGroup>();
            abHLG.spacing = 10f;
            abHLG.padding = new RectOffset(16, 16, 10, 10);
            abHLG.childAlignment     = TextAnchor.MiddleLeft;
            abHLG.childForceExpandWidth  = false;
            abHLG.childForceExpandHeight = true;

            var lblGO      = new GameObject("Label");
            lblGO.transform.SetParent(_saveActionBar.transform, false);
            _saveActionLabel           = lblGO.AddComponent<Text>();
            _saveActionLabel.font      = GetFont();
            _saveActionLabel.fontSize  = 12;
            _saveActionLabel.color     = _sub;
            _saveActionLabel.alignment = TextAnchor.MiddleLeft;
            var lblLE = lblGO.AddComponent<LayoutElement>(); lblLE.preferredWidth = 220f;

            MakeFill(_saveActionBar.transform);
            MakeSmallBtn("BtnSolo",  _saveActionBar.transform, "Solo",        new Vector2(90f, 44f),  _blue,   OnPlaySolo);
            MakeSmallBtn("BtnMulti", _saveActionBar.transform, "Multijoueur", new Vector2(120f, 44f), _orange, OnPlayMulti);
            MakeSmallBtn("BtnDel",   _saveActionBar.transform, "Supprimer",   new Vector2(100f, 44f), _red,    OnDeleteSave);
            _saveActionBar.SetActive(false);
        }

        private void RefreshSaveList()
        {
            if (_saveCardsContainer == null || _saveActionBar == null) return;
            foreach (Transform child in _saveCardsContainer.transform)
                Destroy(child.gameObject);
            _selectedSave = null;
            _saveActionBar.SetActive(false);

            string[] names = SaveSystem.GetAllSaveNames();
            Debug.Log($"[MainMenu] RefreshSaveList : {names.Length} save(s) trouvée(s).");
            if (names.Length == 0)
            {
                MakeText("Empty", _saveCardsContainer.transform,
                    "Aucun monde sauvegardé.\nCliquez sur « + Nouveau » pour en créer un.",
                    _sub, 13, FontStyle.Normal, TextAnchor.MiddleCenter, 0f, 90f);
                return;
            }
            foreach (var name in names)
            {
                try   { BuildSaveCard(name, SaveSystem.ReadSaveMetadata(name)); }
                catch (System.Exception e) { Debug.LogError($"[MainMenu] BuildSaveCard('{name}') : {e}"); }
            }
        }

        private void BuildSaveCard(string saveName, WorldSaveData meta)
        {
            int    seed     = meta?.seed ?? 0;
            string date     = meta?.saveDate ?? "";
            int    gameMode = meta?.gameMode ?? 0;

            var cardGO  = new GameObject($"Card_{saveName}");
            cardGO.transform.SetParent(_saveCardsContainer.transform, false);
            var cardImg = cardGO.AddComponent<Image>(); cardImg.color = _card;
            var cardLE  = cardGO.AddComponent<LayoutElement>(); cardLE.preferredHeight = 72f;
            var cardRT  = cardImg.rectTransform;

            // Accent gauche (bleu=Créatif, orange=Survie)
            var accGO  = new GameObject("Accent");
            accGO.transform.SetParent(cardRT, false);
            var accImg = accGO.AddComponent<Image>(); accImg.color = gameMode == 1 ? _orange : _blue;
            var accRT  = accImg.rectTransform;
            accRT.anchorMin = new Vector2(0f, 0f);
            accRT.anchorMax = new Vector2(0f, 1f);
            accRT.offsetMin = new Vector2(0f, 0f);
            accRT.offsetMax = new Vector2(4f, 0f);

            // Rangée contenu
            var rowGO  = new GameObject("Row");
            rowGO.transform.SetParent(cardRT, false);
            var rowImg = rowGO.AddComponent<Image>(); rowImg.color = Color.clear;
            var rowRT  = rowImg.rectTransform;
            rowRT.anchorMin = Vector2.zero;
            rowRT.anchorMax = Vector2.one;
            rowRT.offsetMin = new Vector2(10f, 0f);
            rowRT.offsetMax = Vector2.zero;
            var rowHLG = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowHLG.spacing = 12f;
            rowHLG.padding = new RectOffset(4, 12, 8, 8);
            rowHLG.childAlignment     = TextAnchor.MiddleLeft;
            rowHLG.childForceExpandWidth  = false;
            rowHLG.childForceExpandHeight = true;

            // Icône colorée
            var iconImg = MakeImage("Icon", rowGO.transform);
            iconImg.color = gameMode == 1 ? _orange : _blue;
            var iconRT  = iconImg.rectTransform;
            iconRT.sizeDelta = new Vector2(34f, 34f);
            iconImg.gameObject.AddComponent<LayoutElement>().preferredWidth = 34f;

            // Colonne infos
            var infoGO = new GameObject("Info");
            infoGO.transform.SetParent(rowGO.transform, false);
            infoGO.AddComponent<Image>().color = Color.clear;
            var infoVLG = infoGO.AddComponent<VerticalLayoutGroup>();
            infoVLG.childForceExpandWidth  = true;
            infoVLG.childForceExpandHeight = false;
            infoVLG.spacing = 2f;
            infoGO.AddComponent<LayoutElement>().flexibleWidth = 1f;

            MakeText("Name", infoGO.transform, saveName, _primary, 15, FontStyle.Bold, TextAnchor.MiddleLeft, 0f, 24f);
            string subLine = $"Seed : {seed}   ·   {(gameMode == 1 ? "Survie" : "Créatif")}";
            if (!string.IsNullOrEmpty(date)) subLine += $"\n{FormatDate(date)}";
            MakeText("Sub", infoGO.transform, subLine, _sub, 11, FontStyle.Normal, TextAnchor.UpperLeft, 0f, 34f);

            // Bouton hover/clic
            var btn = cardGO.AddComponent<Button>();
            btn.targetGraphic = cardImg;
            var cb = btn.colors;
            cb.normalColor      = _card;
            cb.highlightedColor = new Color(0.13f, 0.13f, 0.17f, 0.97f);
            cb.pressedColor     = new Color(0.07f, 0.07f, 0.10f, 0.97f);
            btn.colors = cb;
            string captured = saveName;
            btn.onClick.AddListener(() => SelectSave(captured));
        }

        private void SelectSave(string name)
        {
            _selectedSave = name;
            _saveActionBar.SetActive(true);
            if (_saveActionLabel != null) _saveActionLabel.text = $"Sélectionné : {name}";
        }

        private void OnPlaySolo()
        {
            if (_selectedSave == null) return;
            FinishMenu(new MenuResult { IsLoad = true, SaveName = _selectedSave });
        }

        private void OnPlayMulti()
        {
            if (_selectedSave == null) return;
            _multiSaveName   = _selectedSave;
            _multiFromCreate = false;
            if (_multiContextLabel != null) _multiContextLabel.text = $"Monde : {_multiSaveName}";
            TransitionTo(_playPanel, _multiPanel);
        }

        private void OnDeleteSave()
        {
            if (_selectedSave == null) return;
            string target = _selectedSave;
            ShowDeleteConfirm($"Supprimer le monde\n<b>{target}</b> ?", () =>
            {
                SaveSystem.DeleteSaveFile(target);
                RefreshSaveList();
            });
        }

        // ─────────────────────────────────────────────────────
        // CREATE WORLD PANEL
        // ─────────────────────────────────────────────────────

        private void BuildCreatePanel()
        {
            _createPanel = MakePanel("CreatePanel");
            _createPanel.AddComponent<CanvasGroup>();

            var card   = MakeCard("Card", _createPanel.transform, new Vector2(480f, 530f));
            var cardRT = card.GetComponent<RectTransform>();

            // Header
            var hdr = MakeHRow("Header", cardRT, new RectOffset(24, 24, 14, 0), 50f, anchorTop: true);
            MakeText("Title", hdr.transform, "Nouveau Monde", _primary, 20, FontStyle.Bold, TextAnchor.MiddleLeft, 200f, 40f);
            MakeFill(hdr.transform);
            MakeSmallBtn("BtnBack", hdr.transform, "← Retour", new Vector2(100f, 36f), _card, () =>
            {
                if (_multiSaveName != null) TransitionTo(_createPanel, _playPanel);
                else                        TransitionTo(_createPanel, _mainPanel);
            }, outline: true);

            // Formulaire
            var formGO  = new GameObject("Form");
            formGO.transform.SetParent(cardRT, false);
            formGO.AddComponent<Image>().color = Color.clear;
            var formRT  = formGO.GetComponent<RectTransform>();
            formRT.anchorMin = new Vector2(0f, 0f);
            formRT.anchorMax = new Vector2(1f, 1f);
            formRT.offsetMin = new Vector2(28f, 68f);
            formRT.offsetMax = new Vector2(-28f, -68f);
            var formVLG = formGO.AddComponent<VerticalLayoutGroup>();
            formVLG.spacing            = 14f;
            formVLG.childAlignment     = TextAnchor.UpperLeft;
            formVLG.childForceExpandWidth  = true;
            formVLG.childForceExpandHeight = false;

            // Nom du monde
            MakeText("LblName", formGO.transform, "Nom du monde", _sub, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 0f, 20f);
            _createNameInput = MakeInputField("InputName", formGO.transform, "Mon Monde", 44f);

            // Seed
            MakeText("LblSeed", formGO.transform, "Seed  (vide = aléatoire)", _sub, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 0f, 20f);
            _createSeedInput = MakeInputField("InputSeed", formGO.transform, "Aléatoire", 44f, numeric: true);

            // Mode de jeu
            var modeRow = MakeHRowInline("ModeRow", formGO.transform, 44f);
            MakeText("LblMode", modeRow, "Mode de jeu", _sub, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 150f, 44f);
            MakeFill(modeRow);
            _createIsSurvival = false;
            var modeBtn = MakeToggleBtn("ToggleMode", modeRow, "Créatif", new Vector2(124f, 36f), _blue,
                out _toggleModeLabel, out _toggleModeImg);
            modeBtn.onClick.AddListener(() =>
            {
                _createIsSurvival   = !_createIsSurvival;
                _toggleModeLabel.text = _createIsSurvival ? "Survie" : "Créatif";
                if (_toggleModeImg != null) _toggleModeImg.color = _createIsSurvival ? _orange : _blue;
            });

            // Multijoueur
            var multiRow = MakeHRowInline("MultiRow", formGO.transform, 44f);
            MakeText("LblMulti", multiRow, "Multijoueur", _sub, 12, FontStyle.Normal, TextAnchor.MiddleLeft, 150f, 44f);
            MakeFill(multiRow);
            _createIsMulti = false;
            var multiBtn = MakeToggleBtn("ToggleMulti", multiRow, "Non", new Vector2(124f, 36f), _cardAlt,
                out _toggleMultiLabel, out _toggleMultiImg);
            multiBtn.onClick.AddListener(() =>
            {
                _createIsMulti        = !_createIsMulti;
                _toggleMultiLabel.text = _createIsMulti ? "Oui" : "Non";
                if (_toggleMultiImg != null) _toggleMultiImg.color = _createIsMulti ? _green : _cardAlt;
            });

            // Bouton Créer
            MakeSpacer(formGO.transform, 6f);
            MakePrimaryButton("BtnCreate", formGO.transform, "Créer le monde", _blue, OnCreateWorld);
        }

        private void OnCreateWorld()
        {
            string worldName = _createNameInput != null ? _createNameInput.text.Trim() : "";
            if (string.IsNullOrEmpty(worldName)) worldName = "MonMonde";

            int? seed = null;
            if (_createSeedInput != null && !string.IsNullOrEmpty(_createSeedInput.text.Trim()))
            {
                string rawSeed = _createSeedInput.text.Trim();
                if (int.TryParse(rawSeed, out int parsedSeed))
                    seed = parsedSeed;
                else
                    seed = rawSeed.GetHashCode();   // seed textuelle → hash int
            }

            var result = new MenuResult
            {
                IsLoad     = false,
                CustomSeed = seed,
                GameMode   = _createIsSurvival ? GameMode.Survival : GameMode.Creative,
                IsHost     = false,
                JoinCode   = null,
            };

            if (_createIsMulti)
            {
                _multiSaveName        = null;
                _multiFromCreate      = true;
                _pendingNewWorldResult = result;
                if (_multiContextLabel != null) _multiContextLabel.text = "Nouveau monde";
                TransitionTo(_createPanel, _multiPanel);
            }
            else
            {
                FinishMenu(result);
            }
        }

        // ─────────────────────────────────────────────────────
        // MULTI PANEL
        // ─────────────────────────────────────────────────────

        private void BuildMultiPanel()
        {
            _multiPanel = MakePanel("MultiPanel");
            _multiPanel.AddComponent<CanvasGroup>();

            var card   = MakeCard("Card", _multiPanel.transform, new Vector2(540f, 500f));
            var cardRT = card.GetComponent<RectTransform>();

            // Header
            var hdr = MakeHRow("Header", cardRT, new RectOffset(24, 24, 14, 0), 50f, anchorTop: true);
            MakeText("Title", hdr.transform, "Multijoueur", _primary, 20, FontStyle.Bold, TextAnchor.MiddleLeft, 200f, 40f);
            MakeFill(hdr.transform);
            MakeSmallBtn("BtnBack", hdr.transform, "← Retour", new Vector2(100f, 36f), _card, () =>
            {
                if (_multiFromCreate) TransitionTo(_multiPanel, _createPanel);
                else                  TransitionTo(_multiPanel, _playPanel);
            }, outline: true);

            // Contexte (nom du monde sélectionné)
            var ctxGO  = new GameObject("Context");
            ctxGO.transform.SetParent(cardRT, false);
            ctxGO.AddComponent<Image>().color = Color.clear;
            var ctxRT  = ctxGO.GetComponent<RectTransform>();
            ctxRT.anchorMin = new Vector2(0f, 1f);
            ctxRT.anchorMax = new Vector2(1f, 1f);
            ctxRT.pivot     = new Vector2(0.5f, 1f);
            ctxRT.offsetMin = new Vector2(24f, -90f);
            ctxRT.offsetMax = new Vector2(-24f, -64f);
            var ctxLblGO = new GameObject("ContextLabel");
            ctxLblGO.transform.SetParent(ctxGO.transform, false);
            _multiContextLabel           = ctxLblGO.AddComponent<Text>();
            SetStretch(_multiContextLabel.rectTransform);
            _multiContextLabel.font      = GetFont();
            _multiContextLabel.fontSize  = 13;
            _multiContextLabel.color     = _sub;
            _multiContextLabel.alignment = TextAnchor.MiddleCenter;

            // Zone contenu
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(cardRT, false);
            contentGO.AddComponent<Image>().color = Color.clear;
            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 0f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.offsetMin = new Vector2(24f, 20f);
            contentRT.offsetMax = new Vector2(-24f, -90f);
            var cVLG = contentGO.AddComponent<VerticalLayoutGroup>();
            cVLG.spacing = 18f;
            cVLG.childAlignment     = TextAnchor.UpperCenter;
            cVLG.childForceExpandWidth  = true;
            cVLG.childForceExpandHeight = false;

            // Carte Héberger
            var hostCard = MakeInlineCard("HostCard", contentGO.transform, 144f);
            MakeText("HTitle", hostCard.transform, "Héberger une partie",
                _primary, 16, FontStyle.Bold, TextAnchor.MiddleCenter, 0f, 30f);
            MakeText("HSub", hostCard.transform,
                "Partagez le code affiché dans la console\navec vos amis pour jouer ensemble.",
                _sub, 12, FontStyle.Normal, TextAnchor.MiddleCenter, 0f, 38f);
            MakeSpacer(hostCard.transform, 4f);
            MakePrimaryButton("BtnHost", hostCard.transform, "Héberger", _blue, OnHostGame);

            // Carte Rejoindre
            var joinCard = MakeInlineCard("JoinCard", contentGO.transform, 164f);
            MakeText("JTitle", joinCard.transform, "Rejoindre une partie",
                _primary, 16, FontStyle.Bold, TextAnchor.MiddleCenter, 0f, 30f);
            MakeText("JSub", joinCard.transform, "Entrez le code partagé par l'hôte.",
                _sub, 12, FontStyle.Normal, TextAnchor.MiddleCenter, 0f, 24f);
            MakeSpacer(joinCard.transform, 4f);
            _joinCodeInput = MakeInputField("InputCode", joinCard.transform, "Code de connexion  (ex: A1B2C3D4E5)", 44f);
            MakeSpacer(joinCard.transform, 4f);
            MakePrimaryButton("BtnJoin", joinCard.transform, "Rejoindre", _orange, OnJoinGame);
        }

        private void OnHostGame()
        {
            if (_multiFromCreate)
            {
                var r = _pendingNewWorldResult;
                r.IsHost = true;
                FinishMenu(r);
            }
            else
            {
                FinishMenu(new MenuResult
                {
                    IsLoad   = _multiSaveName != null,
                    SaveName = _multiSaveName,
                    IsHost   = true,
                    GameMode = GameMode.Creative,
                });
            }
        }

        private void OnJoinGame()
        {
            string code = _joinCodeInput != null ? _joinCodeInput.text.Trim() : "";
            if (string.IsNullOrEmpty(code)) return;

            if (_multiFromCreate)
            {
                var r = _pendingNewWorldResult;
                r.JoinCode = code;
                FinishMenu(r);
            }
            else
            {
                FinishMenu(new MenuResult
                {
                    IsLoad   = false,
                    JoinCode = code,
                    GameMode = GameMode.Creative,
                });
            }
        }

        // ─────────────────────────────────────────────────────
        // DELETE CONFIRM MODAL
        // ─────────────────────────────────────────────────────

        private void BuildDeleteConfirmPanel()
        {
            _deleteConfirmPanel = MakePanel("DeleteConfirm");

            // Overlay sombre
            var overlayImg = MakeImage("Overlay", _deleteConfirmPanel.transform);
            overlayImg.color = new Color(0f, 0f, 0f, 0.72f);
            SetStretch(overlayImg.rectTransform);

            // Modale
            var modalGO  = new GameObject("Modal");
            modalGO.transform.SetParent(_deleteConfirmPanel.transform, false);
            var modalImg = modalGO.AddComponent<Image>(); modalImg.color = _card;
            var modalRT  = modalGO.GetComponent<RectTransform>();
            modalRT.anchorMin = new Vector2(0.5f, 0.5f);
            modalRT.anchorMax = new Vector2(0.5f, 0.5f);
            modalRT.pivot     = new Vector2(0.5f, 0.5f);
            modalRT.sizeDelta = new Vector2(420f, 200f);
            var mVLG = modalGO.AddComponent<VerticalLayoutGroup>();
            mVLG.padding = new RectOffset(28, 28, 24, 20);
            mVLG.spacing = 18f;
            mVLG.childForceExpandWidth  = true;
            mVLG.childForceExpandHeight = false;
            mVLG.childAlignment = TextAnchor.UpperCenter;

            var msgGO      = new GameObject("Msg");
            msgGO.transform.SetParent(modalGO.transform, false);
            _deleteConfirmMsg           = msgGO.AddComponent<Text>();
            _deleteConfirmMsg.font      = GetFont();
            _deleteConfirmMsg.fontSize  = 15;
            _deleteConfirmMsg.color     = _primary;
            _deleteConfirmMsg.alignment = TextAnchor.MiddleCenter;
            _deleteConfirmMsg.supportRichText = true;
            msgGO.AddComponent<LayoutElement>().preferredHeight = 52f;

            var btnRow = new GameObject("BtnRow");
            btnRow.transform.SetParent(modalGO.transform, false);
            var brHLG = btnRow.AddComponent<HorizontalLayoutGroup>();
            brHLG.spacing = 14f;
            brHLG.childForceExpandWidth  = false;
            brHLG.childForceExpandHeight = false;
            brHLG.childAlignment = TextAnchor.MiddleCenter;
            btnRow.AddComponent<LayoutElement>().preferredHeight = 44f;

            MakeSmallBtn("BtnCancel",  btnRow.transform, "Annuler",   new Vector2(144f, 40f), _card,  () => _deleteConfirmPanel.SetActive(false), outline: true);
            MakeSmallBtn("BtnConfirm", btnRow.transform, "Supprimer", new Vector2(144f, 40f), _red,   () => { _deleteConfirmPanel.SetActive(false); _deleteConfirmAction?.Invoke(); });

            _deleteConfirmPanel.SetActive(false);
        }

        private void ShowDeleteConfirm(string message, Action onConfirm)
        {
            _deleteConfirmAction  = onConfirm;
            if (_deleteConfirmMsg != null) _deleteConfirmMsg.text = message;
            _deleteConfirmPanel.SetActive(true);
            _deleteConfirmPanel.transform.SetAsLastSibling();
        }

        // ─────────────────────────────────────────────────────
        // TRANSITIONS
        // ─────────────────────────────────────────────────────

        private void ShowPanel(GameObject panel)
        {
            _mainPanel?.SetActive(false);
            _playPanel?.SetActive(false);
            _createPanel?.SetActive(false);
            _multiPanel?.SetActive(false);
            panel.SetActive(true);
            if (panel == _playPanel) RefreshSaveList();
            var cg = panel.GetComponent<CanvasGroup>();
            if (cg != null) StartCoroutine(CoFadeIn(cg));
        }

        private void TransitionTo(GameObject from, GameObject to)
        {
            var cg = from.GetComponent<CanvasGroup>();
            if (cg != null)
                StartCoroutine(CoFadeOutThen(cg, from, () => ShowPanel(to)));
            else
            {
                from.SetActive(false);
                ShowPanel(to);
            }
        }

        private IEnumerator CoFadeIn(CanvasGroup cg)
        {
            cg.alpha = 0f;
            float t = 0f;
            while (t < ANIM)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Clamp01(t / ANIM);
                yield return null;
            }
            cg.alpha = 1f;
        }

        private IEnumerator CoFadeOutThen(CanvasGroup cg, GameObject go, Action then)
        {
            float t = 0f;
            while (t < ANIM)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = 1f - Mathf.Clamp01(t / ANIM);
                yield return null;
            }
            cg.alpha = 0f;
            go.SetActive(false);
            then?.Invoke();
        }

        // ─────────────────────────────────────────────────────
        // FIN DU MENU
        // ─────────────────────────────────────────────────────

        private void FinishMenu(MenuResult result)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
            _callback?.Invoke(result);
            Destroy(gameObject);
        }

        private void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ─────────────────────────────────────────────────────
        // HELPERS : CONSTRUCTION UI
        // ─────────────────────────────────────────────────────

        private static Font GetFont()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return f != null ? f : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        /// <summary>Crée un Panel plein écran enfant de _rootRT.</summary>
        private GameObject MakePanel(string name)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(_rootRT, false);
            go.AddComponent<Image>().color = Color.clear;
            SetStretch(go.GetComponent<RectTransform>());
            return go;
        }

        /// <summary>Crée une carte centrée avec fond sombre.</summary>
        private static GameObject MakeCard(string name, Transform parent, Vector2 size)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = new Color(0.07f, 0.07f, 0.10f, 0.97f);
            var rt  = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            return go;
        }

        /// <summary>Crée une petite carte dans un layout (pour multi panel).</summary>
        private static GameObject MakeInlineCard(string name, Transform parent, float height)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.97f);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(18, 18, 12, 12);
            vlg.spacing = 6f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;
            go.AddComponent<LayoutElement>().preferredHeight = height;
            return go;
        }

        /// <summary>Conteneur VLG (fond transparent).</summary>
        private static GameObject MakeContainer(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = Color.clear;
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            return go;
        }

        /// <summary>HLG ancré en haut d'un card (offsetMin/Max absolus).</summary>
        private static RectTransform MakeHRow(string name, Transform parent, RectOffset padding, float height, bool anchorTop = false)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = Color.clear;
            var rt  = go.GetComponent<RectTransform>();
            if (anchorTop)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot     = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(padding.left, -padding.top - height);
                rt.offsetMax = new Vector2(-padding.right, -padding.top);
            }
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            return rt;
        }

        /// <summary>HLG en ligne dans un VLG (LayoutElement height).</summary>
        private static Transform MakeHRowInline(string name, Transform parent, float height)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = Color.clear;
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            go.AddComponent<LayoutElement>().preferredHeight = height;
            return go.transform;
        }

        private static Image MakeImage(string name, Transform parent)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<Image>();
        }

        private static Text MakeText(string name, Transform parent, string text,
            Color color, int fontSize, FontStyle style, TextAnchor anchor,
            float preferredWidth, float preferredHeight)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t            = go.AddComponent<Text>();
            t.text           = text;
            t.font           = GetFont();
            t.fontSize       = fontSize;
            t.fontStyle      = style;
            t.color          = color;
            t.alignment      = anchor;
            t.supportRichText = true;
            var le = go.AddComponent<LayoutElement>();
            if (preferredWidth  > 0f) le.preferredWidth  = preferredWidth;
            if (preferredHeight > 0f) le.preferredHeight = preferredHeight;
            return t;
        }

        private static void MakeSpacer(Transform parent, float height)
        {
            var go = new GameObject("Spacer");
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = height;
        }

        private static void MakeFill(Transform parent)
        {
            var go = new GameObject("Fill");
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        /// <summary>Bouton pleine largeur (dans un VLG).</summary>
        private static void MakePrimaryButton(string name, Transform parent, string label,
            Color bgColor, Action onClick, bool outline = false)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = bgColor;
            go.AddComponent<LayoutElement>().preferredHeight = 50f;
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            var cb  = btn.colors;
            cb.normalColor      = bgColor;
            cb.highlightedColor = bgColor * 1.25f;
            cb.pressedColor     = bgColor * 0.75f;
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var lGO  = new GameObject("Label");
            lGO.transform.SetParent(go.transform, false);
            var lRT  = lGO.AddComponent<RectTransform>();
            SetStretch(lRT);
            var lTxt = lGO.AddComponent<Text>();
            lTxt.text      = label;
            lTxt.font      = GetFont();
            lTxt.fontSize  = 16;
            lTxt.fontStyle = FontStyle.Bold;
            lTxt.color     = Color.white;
            lTxt.alignment = TextAnchor.MiddleCenter;
        }

        /// <summary>Bouton de taille fixe (dans un HLG).</summary>
        private static void MakeSmallBtn(string name, Transform parent, string label,
            Vector2 size, Color bgColor, Action onClick, bool outline = false)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = bgColor;
            var le  = go.AddComponent<LayoutElement>();
            le.preferredWidth  = size.x;
            le.preferredHeight = size.y;
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            var cb  = btn.colors;
            cb.normalColor      = bgColor;
            cb.highlightedColor = new Color(
                Mathf.Min(bgColor.r * 1.25f, 1f),
                Mathf.Min(bgColor.g * 1.25f, 1f),
                Mathf.Min(bgColor.b * 1.25f, 1f),
                bgColor.a);
            cb.pressedColor = bgColor * 0.75f;
            btn.colors = cb;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var lGO  = new GameObject("Label");
            lGO.transform.SetParent(go.transform, false);
            var lRT  = lGO.AddComponent<RectTransform>();
            SetStretch(lRT);
            var lTxt = lGO.AddComponent<Text>();
            lTxt.text      = label;
            lTxt.font      = GetFont();
            lTxt.fontSize  = 13;
            lTxt.fontStyle = FontStyle.Bold;
            lTxt.color     = Color.white;
            lTxt.alignment = TextAnchor.MiddleCenter;
        }

        /// <summary>Bouton à état (mode, multi…).</summary>
        private static Button MakeToggleBtn(string name, Transform parent, string label,
            Vector2 size, Color bgColor, out Text labelText, out Image bgImage)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = bgColor;
            bgImage = img;
            go.AddComponent<LayoutElement>().preferredWidth = size.x;
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            var cb  = btn.colors;
            cb.normalColor = bgColor;
            btn.colors = cb;

            var lGO  = new GameObject("Label");
            lGO.transform.SetParent(go.transform, false);
            var lRT  = lGO.AddComponent<RectTransform>();
            SetStretch(lRT);
            labelText           = lGO.AddComponent<Text>();
            labelText.text      = label;
            labelText.font      = GetFont();
            labelText.fontSize  = 13;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color     = Color.white;
            labelText.alignment = TextAnchor.MiddleCenter;
            return btn;
        }

        private InputField MakeInputField(string name, Transform parent,
            string placeholder, float height, bool numeric = false)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent, false);
            var bg  = go.AddComponent<Image>(); bg.color = _input;
            go.AddComponent<LayoutElement>().preferredHeight = height;

            // Placeholder
            var phGO  = new GameObject("Placeholder");
            phGO.transform.SetParent(go.transform, false);
            var phTxt = phGO.AddComponent<Text>();
            phTxt.text      = placeholder;
            phTxt.font      = GetFont();
            phTxt.fontSize  = 14;
            phTxt.color     = _sub;
            phTxt.alignment = TextAnchor.MiddleLeft;
            phTxt.fontStyle = FontStyle.Italic;
            var phRT = phGO.GetComponent<RectTransform>();
            SetStretch(phRT);
            phRT.offsetMin = new Vector2(10f, 0f);
            phRT.offsetMax = new Vector2(-8f, 0f);

            // Text
            var txtGO  = new GameObject("Text");
            txtGO.transform.SetParent(go.transform, false);
            var txtTxt = txtGO.AddComponent<Text>();
            txtTxt.font            = GetFont();
            txtTxt.fontSize        = 14;
            txtTxt.color           = _primary;
            txtTxt.alignment       = TextAnchor.MiddleLeft;
            txtTxt.supportRichText = false;
            var txtRT = txtGO.GetComponent<RectTransform>();
            SetStretch(txtRT);
            txtRT.offsetMin = new Vector2(10f, 0f);
            txtRT.offsetMax = new Vector2(-8f, 0f);

            var field             = go.AddComponent<InputField>();
            field.textComponent   = txtTxt;
            field.placeholder     = phTxt;
            field.caretColor      = _blue;
            field.selectionColor  = new Color(_blue.r, _blue.g, _blue.b, 0.3f);
            if (numeric) field.contentType = InputField.ContentType.IntegerNumber;
            return field;
        }

        private static void SetStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // ─────────────────────────────────────────────────────
        // DIVERS
        // ─────────────────────────────────────────────────────

        private static string FormatDate(string iso)
        {
            if (System.DateTime.TryParse(iso, out System.DateTime dt))
                return dt.ToString("d MMM yyyy 'à' HH:mm",
                    System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
            return iso;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            esGO.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}

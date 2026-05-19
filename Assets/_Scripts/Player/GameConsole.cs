// ============================================================
//  GameConsole.cs
//  Console de commandes en jeu.
//  T ou "/" → ouvre   |   ESC → ferme   |   ↑↓ → historique
//
//  Layout :
//  ┌─────────────────────────────────────────────────────────┐  ← haut écran
//  │  CONSOLE                            ESC — fermer        │  ← title bar 30 px
//  ├─────────────────────────────────────────────────────────┤
//  │                                                         │
//  │  AstroVoxel Console — tapez help pour les commandes     │  ← output scroll
//  │  › /clear                                               │
//  │  ✓  Hotbar vidée.                                       │
//  │                                                         │
//  ├─────────────────────────────────────────────────────────┤
//  │  ›  Entrez une commande…                                │  ← input 40 px
//  └─────────────────────────────────────────────────────────┘
// ============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using AstroVoxel.Space;
using AstroVoxel.Save;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AstroVoxel.Player
{
    /// <summary>
    /// Console de commandes style terminal.
    /// S'ajoute au canvas HUD via <see cref="Init"/>.
    /// </summary>
    public sealed class GameConsole : MonoBehaviour
    {
        // ── Palette Apple Dark ────────────────────────────────
        private static readonly Color _bg       = new Color(0.05f, 0.05f, 0.07f, 0.96f);
        private static readonly Color _titleBg  = new Color(0.07f, 0.07f, 0.09f, 1.00f);
        private static readonly Color _inputBg  = new Color(0.08f, 0.08f, 0.11f, 1.00f);
        private static readonly Color _border   = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color _primary  = new Color(0.90f, 0.90f, 0.92f, 1.00f);
        private static readonly Color _sub      = new Color(0.52f, 0.52f, 0.57f, 1.00f);
        private static readonly Color _green    = new Color(0.22f, 0.82f, 0.44f, 1.00f);
        private static readonly Color _red      = new Color(1.00f, 0.35f, 0.35f, 1.00f);
        private static readonly Color _blue     = new Color(0.20f, 0.55f, 1.00f, 1.00f);

        // ── Layout ────────────────────────────────────────────
        private const float H_TOTAL  = 290f;
        private const float H_TITLE  = 30f;
        private const float H_INPUT  = 42f;
        private const float ANIM_DUR = 0.14f;
        private const float SLIDE_PX = 10f;   // small downward slide on open

        // ── Public state ──────────────────────────────────────
        public static bool IsOpen { get; private set; }

        // ── References ────────────────────────────────────────
        private BlockInteraction _bi;

        private GameObject    _root;
        private RectTransform _rootRT;
        private CanvasGroup   _cg;
        private InputField    _inputField;
        private Text          _outputText;
        private RectTransform _contentRT;
        private ScrollRect    _scroll;

        private readonly List<string> _log     = new();
        private readonly List<string> _history = new();
        private int                   _hIdx    = -1;

        private Coroutine _anim;
        private bool      _openedWithSlash;

        private const int MAX_LINES = 200;

        // ── Init ──────────────────────────────────────────────

        public void Init(Canvas canvas, BlockInteraction bi)
        {
            _bi = bi;
            BuildUI(canvas);
            _root.SetActive(false);
        }

        private void Awake() => IsOpen = false;

        // ── Input handling ────────────────────────────────────

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;
            bool kT   = kb.tKey.wasPressedThisFrame;
            bool kSl  = kb.slashKey.wasPressedThisFrame;
            bool kEsc = kb.escapeKey.wasPressedThisFrame;
            bool kUp  = kb.upArrowKey.wasPressedThisFrame;
            bool kDn  = kb.downArrowKey.wasPressedThisFrame;
#else
            bool kT   = Input.GetKeyDown(KeyCode.T);
            bool kSl  = Input.GetKeyDown(KeyCode.Slash);
            bool kEsc = Input.GetKeyDown(KeyCode.Escape);
            bool kUp  = Input.GetKeyDown(KeyCode.UpArrow);
            bool kDn  = Input.GetKeyDown(KeyCode.DownArrow);
#endif
            if (!IsOpen)
            {
                // Don't open if another overlay is already open
                if (CreativeInventory.IsOpen) return;
                if (kT || kSl)
                {
                    _openedWithSlash = kSl;
                    Open();
                }
                return;
            }

            // Console open
            if (kEsc) { Close(); return; }
            if (kUp)  { HistNav(-1); return; }
            if (kDn)  { HistNav(+1); return; }
        }

        // ── Open / Close ──────────────────────────────────────

        private void Open()
        {
            IsOpen = true;
            _root.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;

            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(CoIn());
        }

        private void Close()
        {
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(CoOut());
        }

        private IEnumerator CoIn()
        {
            // Slide from slightly above + fade in
            float t = 0f;
            while (t < ANIM_DUR)
            {
                t += Time.unscaledDeltaTime;
                float p = EaseOut(Mathf.Clamp01(t / ANIM_DUR));
                _cg.alpha = p;
                _rootRT.anchoredPosition = new Vector2(0f, Mathf.Lerp(SLIDE_PX, 0f, p));
                yield return null;
            }
            _cg.alpha = 1f;
            _rootRT.anchoredPosition = Vector2.zero;

            // Focus input after 1 frame (avoids T/slash being typed in)
            yield return null;
            _inputField?.Select();
            _inputField?.ActivateInputField();

            if (_openedWithSlash && _inputField != null)
            {
                _inputField.text = "/";
                _inputField.MoveTextEnd(false);
            }
        }

        private IEnumerator CoOut()
        {
            float t = 0f;
            float startAlpha = _cg.alpha;
            float startY     = _rootRT.anchoredPosition.y;

            while (t < ANIM_DUR)
            {
                t += Time.unscaledDeltaTime;
                float p = EaseIn(Mathf.Clamp01(t / ANIM_DUR));
                _cg.alpha = Mathf.Lerp(startAlpha, 0f, p);
                _rootRT.anchoredPosition = new Vector2(0f, Mathf.Lerp(startY, SLIDE_PX, p));
                yield return null;
            }
            _cg.alpha = 0f;

            IsOpen = false;
            _root.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            if (_inputField != null) _inputField.text = "";
            _hIdx = -1;
        }

        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);
        private static float EaseIn(float t)  => t * t;

        // ── History ───────────────────────────────────────────

        private void HistNav(int dir)
        {
            if (_history.Count == 0) return;
            _hIdx = Mathf.Clamp(_hIdx + dir, -1, _history.Count - 1);
            if (_inputField == null) return;
            _inputField.text = _hIdx >= 0 ? _history[_hIdx] : "";
            _inputField.MoveTextEnd(false);
        }

        // ── Command flow ──────────────────────────────────────

        private void OnSubmit(string raw)
        {
            string cmd = raw.Trim();
            if (_inputField != null) _inputField.text = "";

            if (string.IsNullOrEmpty(cmd)) { Refocus(); return; }

            // Add to history (index 0 = most recent)
            _history.Insert(0, cmd);
            if (_history.Count > 64) _history.RemoveAt(_history.Count - 1);
            _hIdx = -1;

            // Echo
            Push($"<color=#{H(_green)}>›</color>  <color=#{H(_primary)}>{Esc(cmd)}</color>");
            Run(cmd);
            Refocus();
        }

        private void Run(string raw)
        {
            string[] parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            string verb = parts[0].TrimStart('/').ToLowerInvariant();
            switch (verb)
            {
                case "help":    CmdHelp();              break;
                case "clear":   CmdClear();             break;
                case "seed":    CmdSeed();              break;
                case "restart": CmdRestart();           break;
                case "save":    CmdSave(parts);         break;
                case "load":    CmdLoad(parts);         break;
                case "saves":   CmdSaves(parts);         break;
                default:
                    PushErr($"Commande inconnue : <b>{Esc(parts[0])}</b>  —  tapez <b>help</b>");
                    break;
            }
        }

        // ── Commands ──────────────────────────────────────────

        private void CmdHelp()
        {
            string sep = $"<color=#{H(_sub)}>{'─'.ToString().PadRight(50, '─')}</color>";
            Push(sep);
            Push($"<b><color=#{H(_primary)}>AstroVoxel Console</color></b>");
            Push($"  <color=#{H(_blue)}>/clear</color>    <color=#{H(_sub)}>Vide tous les blocs de la hotbar</color>");
            Push($"  <color=#{H(_blue)}>/seed</color>     <color=#{H(_sub)}>Affiche la seed du monde actuel</color>");
            Push($"  <color=#{H(_blue)}>/restart</color>  <color=#{H(_sub)}>Redémarre avec une nouvelle seed aléatoire</color>");
            Push($"  <color=#{H(_blue)}>/save NOM</color>          <color=#{H(_sub)}>Sauvegarde le monde sous le nom NOM</color>");
            Push($"  <color=#{H(_blue)}>/load NOM</color>          <color=#{H(_sub)}>Charge la sauvegarde NOM</color>");
            Push($"  <color=#{H(_blue)}>/saves</color>             <color=#{H(_sub)}>Liste toutes les sauvegardes disponibles</color>");
            Push($"  <color=#{H(_blue)}>/saves delete NOM</color>  <color=#{H(_sub)}>Supprime la sauvegarde NOM</color>");
            Push($"  <color=#{H(_blue)}>/saves folder</color>      <color=#{H(_sub)}>Ouvre le dossier des sauvegardes</color>");
            Push(sep);
        }

        private void CmdClear()
        {
            if (_bi == null) { PushErr("Référence introuvable."); return; }
            _bi.ClearInventory();
            PushOk("Hotbar vidée.");
        }

        private void CmdSeed()
        {
            int seed = WorldSeedManager.Seed;
            Push($"<color=#{H(_blue)}>SEED</color>  <color=#{H(_primary)}><b>{seed}</b></color>");
            Push($"  <color=#{H(_sub)}>Notez cette valeur pour retrouver ce monde.</color>");
        }

        private void CmdRestart()
        {
            PushOk("Génération d'une nouvelle seed…");
            StartCoroutine(CoRestart());
        }

        private static IEnumerator CoRestart()
        {
            // Attend 1 frame pour que le message s'affiche
            yield return null;
            WorldSeedManager.GenerateNewSeed();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void CmdSave(string[] parts)
        {
            if (parts.Length < 2) { PushErr("Usage : /save NOM"); return; }
            if (SaveSystem.Instance == null) { PushErr("SaveSystem introuvable."); return; }
            string name = SanitizeSaveName(parts[1]);
            if (string.IsNullOrEmpty(name)) { PushErr("Nom de sauvegarde invalide."); return; }
            try
            {
                SaveSystem.Instance.SaveWorld(name);
                PushOk($"Monde sauvegardé : <b>{Esc(name)}</b>");
            }
            catch (Exception e)
            {
                PushErr($"Erreur lors de la sauvegarde : {Esc(e.Message)}");
            }
        }

        private void CmdLoad(string[] parts)
        {
            if (parts.Length < 2) { PushErr("Usage : /load NOM"); return; }
            if (SaveSystem.Instance == null) { PushErr("SaveSystem introuvable."); return; }
            string name = SanitizeSaveName(parts[1]);
            if (string.IsNullOrEmpty(name)) { PushErr("Nom de sauvegarde invalide."); return; }
            if (!SaveSystem.Instance.SaveExists(name)) { PushErr($"Sauvegarde introuvable : <b>{Esc(name)}</b>"); return; }
            PushOk($"Chargement de <b>{Esc(name)}</b>…");
            StartCoroutine(CoLoad(name));
        }

        private static IEnumerator CoLoad(string name)
        {
            yield return null;  // laisse le message s'afficher
            SaveSystem.Instance.LoadWorld(name);
        }

        private void CmdSaves(string[] parts)
        {
            if (parts.Length >= 2)
            {
                switch (parts[1].ToLowerInvariant())
                {
                    case "delete": CmdDeleteSave(parts); return;
                    case "folder": CmdOpenSavesFolder();  return;
                }
            }
            CmdListSaves();
        }

        private void CmdDeleteSave(string[] parts)
        {
            if (parts.Length < 3) { PushErr("Usage : /saves delete NOM"); return; }
            if (SaveSystem.Instance == null) { PushErr("SaveSystem introuvable."); return; }
            string name = SanitizeSaveName(parts[2]);
            if (string.IsNullOrEmpty(name)) { PushErr("Nom de sauvegarde invalide."); return; }
            try
            {
                SaveSystem.Instance.DeleteSave(name);
                PushOk($"Sauvegarde supprim\u00e9e : <b>{Esc(name)}</b>");
            }
            catch (Exception e)
            {
                PushErr($"Erreur : {Esc(e.Message)}");
            }
        }

        private void CmdOpenSavesFolder()
        {
            if (SaveSystem.Instance == null) { PushErr("SaveSystem introuvable."); return; }
            SaveSystem.Instance.OpenSavesFolder();
            PushOk("Dossier des sauvegardes ouvert.");
        }

        private void CmdListSaves()
        {
            if (SaveSystem.Instance == null) { PushErr("SaveSystem introuvable."); return; }
            string[] names = SaveSystem.Instance.GetSaveNames();
            if (names.Length == 0)
            {
                Push($"<color=#{H(_sub)}>Aucune sauvegarde trouvée.</color>");
                return;
            }
            Push($"<color=#{H(_blue)}>Sauvegardes ({names.Length}) :</color>");
            foreach (var n in names)
                Push($"  <color=#{H(_primary)}>{Esc(n)}</color>");
        }

        private static string SanitizeSaveName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                if (Array.IndexOf(invalid, c) < 0) sb.Append(c);
            return sb.ToString().Trim();
        }

        // ── Log helpers ───────────────────────────────────────

        private void Push(string richLine)
        {
            _log.Add(richLine);
            if (_log.Count > MAX_LINES) _log.RemoveAt(0);
            Refresh();
        }

        private void PushOk(string msg)  => Push($"<color=#{H(_green)}>✓</color>  <color=#{H(_primary)}>{Esc(msg)}</color>");
        private void PushErr(string msg) => Push($"<color=#{H(_red)}>✗</color>  <color=#{H(_sub)}>{msg}</color>");

        private void Refresh()
        {
            if (_outputText == null) return;
            _outputText.text = string.Join("\n", _log);

            // Pass 1 – let the canvas compute rect widths so preferredHeight is valid.
            Canvas.ForceUpdateCanvases();

            // Manually drive content height (ContentSizeFitter is unreliable at init time).
            if (_contentRT != null)
            {
                float h = Mathf.Max(_outputText.preferredHeight + 16f, 10f);
                _contentRT.sizeDelta = new Vector2(_contentRT.sizeDelta.x, h);
            }

            // Pass 2 – let the scroll rect react to the new content height.
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
        }

        private void Refocus() => StartCoroutine(CoRefocus());

        private IEnumerator CoRefocus()
        {
            yield return null;
            _inputField?.Select();
            _inputField?.ActivateInputField();
        }

        // ── Utils ─────────────────────────────────────────────

        private static string Esc(string s) => s.Replace("<", "\u003c").Replace(">", "\u003e");
        private static string H(Color c)    => ColorUtility.ToHtmlStringRGB(c);

        // ── Build UI ──────────────────────────────────────────

        private void BuildUI(Canvas canvas)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // ── Root panel ────────────────────────────────────
            _root = new GameObject("ConsoleRoot");
            _root.transform.SetParent(canvas.transform, false);
            _root.transform.SetAsLastSibling();

            _cg    = _root.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;

            _rootRT = _root.AddComponent<RectTransform>();
            _rootRT.anchorMin        = new Vector2(0f, 1f);
            _rootRT.anchorMax        = new Vector2(1f, 1f);
            _rootRT.pivot            = new Vector2(0.5f, 1f);
            _rootRT.sizeDelta        = new Vector2(0f, H_TOTAL);
            _rootRT.anchoredPosition = new Vector2(0f, SLIDE_PX);

            _root.AddComponent<Image>().color = _bg;
            HLine(_root.transform, 0f);  // bottom edge separator

            // ── Title bar ─────────────────────────────────────
            var titleRT = MakeRectGO("TitleBar", _root.transform);
            titleRT.anchorMin        = new Vector2(0f, 1f);
            titleRT.anchorMax        = new Vector2(1f, 1f);
            titleRT.pivot            = new Vector2(0.5f, 1f);
            titleRT.sizeDelta        = new Vector2(0f, H_TITLE);
            titleRT.anchoredPosition = Vector2.zero;
            titleRT.gameObject.AddComponent<Image>().color = _titleBg;
            HLine(titleRT, 0f);

            // "CONSOLE" label — left side
            var consoleLabel = MakeLabelGO("LabelConsole", titleRT, font);
            consoleLabel.text      = "CONSOLE";
            consoleLabel.fontSize  = 10;
            consoleLabel.fontStyle = FontStyle.Bold;
            consoleLabel.color     = _blue;
            consoleLabel.alignment = TextAnchor.MiddleLeft;
            {
                var rt = consoleLabel.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot     = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(80f, 0f);
                rt.anchoredPosition = new Vector2(14f, 0f);
            }

            // "ESC — fermer" label — right side
            var escLabel = MakeLabelGO("LabelEsc", titleRT, font);
            escLabel.text      = "ESC  —  fermer";
            escLabel.fontSize  = 10;
            escLabel.fontStyle = FontStyle.Normal;
            escLabel.color     = _sub;
            escLabel.alignment = TextAnchor.MiddleRight;
            {
                var rt = escLabel.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot     = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(120f, 0f);
                rt.anchoredPosition = new Vector2(-14f, 0f);
            }

            // ── Input row (bottom) ────────────────────────────
            var inputRowRT = MakeRectGO("InputRow", _root.transform);
            inputRowRT.anchorMin        = new Vector2(0f, 0f);
            inputRowRT.anchorMax        = new Vector2(1f, 0f);
            inputRowRT.pivot            = new Vector2(0.5f, 0f);
            inputRowRT.sizeDelta        = new Vector2(0f, H_INPUT);
            inputRowRT.anchoredPosition = Vector2.zero;
            inputRowRT.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.06f, 0.08f, 1f);
            HLine(inputRowRT, 1f);  // top edge separator

            // "›" prompt
            var promptLabel = MakeLabelGO("Prompt", inputRowRT, font);
            promptLabel.text      = "›";
            promptLabel.fontSize  = 16;
            promptLabel.color     = _green;
            promptLabel.alignment = TextAnchor.MiddleLeft;
            {
                var rt = promptLabel.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot     = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(22f, 0f);
                rt.anchoredPosition = new Vector2(10f, 0f);
            }

            // InputField background
            var ifBgRT = MakeRectGO("InputFieldBg", inputRowRT);
            ifBgRT.anchorMin = Vector2.zero;
            ifBgRT.anchorMax = Vector2.one;
            ifBgRT.offsetMin = new Vector2(36f, 6f);
            ifBgRT.offsetMax = new Vector2(-12f, -6f);
            ifBgRT.gameObject.AddComponent<Image>().color = _inputBg;

            // InputField inner text
            var ifTextGO = new GameObject("Text");
            ifTextGO.transform.SetParent(ifBgRT, false);
            var ifText = ifTextGO.AddComponent<Text>();
            ifText.font            = font;
            ifText.fontSize        = 13;
            ifText.color           = _primary;
            ifText.supportRichText = false;
            var ifTextRT = ifTextGO.GetComponent<RectTransform>();
            ifTextRT.anchorMin = Vector2.zero;
            ifTextRT.anchorMax = Vector2.one;
            ifTextRT.offsetMin = new Vector2(8f, 0f);
            ifTextRT.offsetMax = new Vector2(-8f, 0f);

            // Placeholder
            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(ifBgRT, false);
            var phText = phGO.AddComponent<Text>();
            phText.text      = "Entrez une commande…";
            phText.font      = font;
            phText.fontSize  = 12;
            phText.fontStyle = FontStyle.Italic;
            phText.color     = new Color(1f, 1f, 1f, 0.20f);
            var phRT = phGO.GetComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero;
            phRT.anchorMax = Vector2.one;
            phRT.offsetMin = new Vector2(8f, 0f);
            phRT.offsetMax = new Vector2(-8f, 0f);

            _inputField = ifBgRT.gameObject.AddComponent<InputField>();
            _inputField.textComponent   = ifText;
            _inputField.placeholder      = phText;
            _inputField.caretColor       = _green;
            _inputField.customCaretColor = true;
            _inputField.caretWidth       = 2;
            _inputField.onSubmit.AddListener(OnSubmit);

            // ── Scroll output area (between title & input) ────
            var scrollGO = new GameObject("ScrollView");
            scrollGO.transform.SetParent(_root.transform, false);
            var scrollRT = scrollGO.AddComponent<RectTransform>();
            scrollRT.anchorMin = Vector2.zero;
            scrollRT.anchorMax = Vector2.one;
            scrollRT.offsetMin = new Vector2(0f, H_INPUT);
            scrollRT.offsetMax = new Vector2(0f, -H_TITLE);
            scrollGO.AddComponent<Image>().color = Color.clear;

            _scroll = scrollGO.AddComponent<ScrollRect>();
            _scroll.horizontal        = false;
            _scroll.scrollSensitivity = 40f;
            _scroll.movementType      = ScrollRect.MovementType.Clamped;

            // Viewport with mask — RectMask2D ne nécessite pas d'Image(Color.clear)
            // qui bloquerait le rendu de tout le contenu via le stencil buffer.
            var vpGO = new GameObject("Viewport");
            vpGO.transform.SetParent(scrollGO.transform, false);
            vpGO.AddComponent<RectMask2D>();
            var vpRT = vpGO.GetComponent<RectTransform>();
            vpRT.anchorMin        = Vector2.zero;
            vpRT.anchorMax        = Vector2.one;
            vpRT.sizeDelta        = Vector2.zero;
            vpRT.anchoredPosition = Vector2.zero;

            // Output text IS the scroll content — ContentSizeFitter grows it downward.
            // No VerticalLayoutGroup: adding one before Start() causes zero-height content.
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(vpGO.transform, false);

            _outputText = contentGO.AddComponent<Text>();
            _outputText.font               = font;
            _outputText.fontSize           = 12;
            _outputText.color              = _primary;
            _outputText.supportRichText    = true;
            _outputText.alignment          = TextAnchor.UpperLeft;
            _outputText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _outputText.verticalOverflow   = VerticalWrapMode.Overflow;
            _outputText.lineSpacing        = 1.35f;

            // Stretch full width with horizontal padding, grow downward from top.
            // ContentSizeFitter is intentionally NOT used here: it reads preferredHeight
            // before the canvas has computed rect widths (same Awake frame), giving 0.
            // Instead we set height manually in Refresh() after ForceUpdateCanvases().
            _contentRT = contentGO.GetComponent<RectTransform>();
            _contentRT.anchorMin        = new Vector2(0f, 1f);
            _contentRT.anchorMax        = new Vector2(1f, 1f);
            _contentRT.pivot            = new Vector2(0.5f, 1f);
            _contentRT.sizeDelta        = new Vector2(-24f, 10f);
            _contentRT.anchoredPosition = new Vector2(0f, -8f);

            _scroll.viewport = vpRT;
            _scroll.content  = _contentRT;

            // Welcome message
            string sep = $"<color=#{H(_sub)}>{'─'.ToString().PadRight(50, '─')}</color>";
            Push(sep);
            Push($"<b><color=#{H(_primary)}>AstroVoxel Console</color></b>  " +
                 $"<color=#{H(_sub)}>tapez <b>help</b> pour les commandes disponibles</color>");
            Push(sep);
        }

        // ── Layout helpers ────────────────────────────────────

        private static RectTransform MakeRectGO(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        private static Text MakeLabelGO(string name, RectTransform parent, Font font)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>(); // Text auto-adds RectTransform
            t.font = font;
            return t;
        }

        private static void HLine(Transform parent, float yAnchor)
        {
            var go = new GameObject("HLine");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.07f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0f, yAnchor);
            rt.anchorMax        = new Vector2(1f, yAnchor);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
        }
    }
}

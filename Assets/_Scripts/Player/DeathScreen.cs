// ============================================================
//  DeathScreen.cs
//  Écran de mort réutilisable — s'affiche à chaque mort du joueur.
//
//  API publique :
//    DeathScreen.Show(onRespawn, cause)
//      • onRespawn  — callback appelé après la fin du fondu de sortie
//      • cause      — message de mort personnalisé (null = message aléatoire)
//
//  Architecture :
//    • Singleton paresseux créé automatiquement au premier appel
//    • Canvas Screen Space Overlay, sortingOrder 999 (au-dessus de tout)
//    • Fond noir-rouge semi-transparent
//    • Titre "VOUS ÊTES MORT" en grand, texte aléatoire en dessous
//    • Fondu entrée 0.6 s → pause 3 s → fondu sortie 0.5 s → respawn
// ============================================================

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace AstroVoxel.Player
{
    public sealed class DeathScreen : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────
        public static DeathScreen Instance { get; private set; }

        // ── Messages rigolos ──────────────────────────────────
        private static readonly string[] FunnyMessages = new[]
        {
            "Votre assurance ne couvre pas les explosions de vaisseau.",
            "L'univers : 1  —  Vous : 0.",
            "Prochaine fois, ralentissez AVANT le mur.",
            "Darwin aurait pris des notes.",
            "Votre maman vous avait pourtant prévenu.",
            "Erreur 404 : joueur introuvable.",
            "C'est pas prévu dans le plan de vol.",
            "Vous avez découvert une toute nouvelle façon de ne pas survivre.",
            "Conseil du jour : ne pas foncer dans les astéroïdes.",
            "Le soleil vous envoie ses chaleureuses salutations.",
            "Bonne nouvelle : c'est respawnable !",
            "En mode Survie, les blocs font mal. Très mal.",
            "GAME OVER. Comme dans les jeux vidéo.",
            "Votre épitaphe : « Il allait trop vite. »",
            "Même les astronautes portent une ceinture de sécurité.",
            "Vous avez volontairement ignoré les lois de la physique.",
            "Mission échouée. Brillamment.",
            "C'est pas un bug, c'est une feature d'élimination.",
            "Prochain objectif : survivre plus de 10 secondes.",
            "L'espace ne pardonne pas. Ni les blocs d'astéroïdes.",
        };

        // ── Références UI ─────────────────────────────────────
        private Canvas      _canvas;
        private CanvasGroup _canvasGroup;
        private Text        _subtitleText;

        // ── État ──────────────────────────────────────────────
        private bool   _showing;
        private Action _onRespawn;

        // ── API publique ──────────────────────────────────────

        /// <summary>
        /// Affiche l'écran de mort.
        /// </summary>
        /// <param name="onRespawn">Callback exécuté après le fondu de sortie.</param>
        /// <param name="cause">Message personnalisé sous le titre. Null = message aléatoire.</param>
        public static void Show(Action onRespawn = null, string cause = null)
        {
            EnsureInstance();
            Instance?.ShowInternal(onRespawn, cause);
        }

        // ── Création paresseuse ───────────────────────────────

        private static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("DeathScreen");
            go.AddComponent<DeathScreen>();
            // Instance est assignée dans Awake
        }

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            BuildUI();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Logique d'affichage ───────────────────────────────

        private void ShowInternal(Action onRespawn, string cause)
        {
            if (_showing) return;
            _showing   = true;
            _onRespawn = onRespawn;

            // Choisit le message
            _subtitleText.text = !string.IsNullOrEmpty(cause)
                ? cause
                : FunnyMessages[UnityEngine.Random.Range(0, FunnyMessages.Length)];

            _canvasGroup.alpha = 0f;
            _canvas.gameObject.SetActive(true);

            StartCoroutine(AnimateShow());
        }

        private IEnumerator AnimateShow()
        {
            // ── Fondu entrant ─────────────────────────────────
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * (1f / 0.6f);
                _canvasGroup.alpha = Mathf.Clamp01(t);
                yield return null;
            }

            // ── Pause ─────────────────────────────────────────
            yield return new WaitForSecondsRealtime(3f);

            // ── Fondu sortant ─────────────────────────────────
            t = 1f;
            while (t > 0f)
            {
                t -= Time.unscaledDeltaTime * (1f / 0.5f);
                _canvasGroup.alpha = Mathf.Clamp01(t);
                yield return null;
            }

            _canvas.gameObject.SetActive(false);
            _showing = false;

            Action cb = _onRespawn;
            _onRespawn = null;
            cb?.Invoke();
        }

        // ── Construction de l'UI ──────────────────────────────

        private void BuildUI()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                     ?? Font.CreateDynamicFontFromOSFont("Arial", 72);

            // ── Canvas principal ──────────────────────────────
            var canvasGO = new GameObject("DeathScreenCanvas");
            canvasGO.transform.SetParent(transform, false);

            _canvas             = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 999;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            _canvasGroup       = canvasGO.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;

            canvasGO.SetActive(false);

            // ── Fond sombre rouge-sang ────────────────────────
            var bg   = MakeRect("BG", canvasGO.transform);
            bg.anchorMin = Vector2.zero;
            bg.anchorMax = Vector2.one;
            bg.offsetMin = Vector2.zero;
            bg.offsetMax = Vector2.zero;
            var bgImg   = bg.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.04f, 0.00f, 0.01f, 0.93f);

            // ── Titre : VOUS ÊTES MORT ────────────────────────
            var titleRT  = MakeRect("Title", canvasGO.transform);
            titleRT.anchorMin        = new Vector2(0f, 0.5f);
            titleRT.anchorMax        = new Vector2(1f, 0.5f);
            titleRT.pivot            = new Vector2(0.5f, 0.5f);
            titleRT.anchoredPosition = new Vector2(0f, 80f);
            titleRT.sizeDelta        = new Vector2(0f, 130f);

            var titleTxt       = titleRT.gameObject.AddComponent<Text>();
            titleTxt.text      = "VOUS ÊTES MORT";
            titleTxt.font      = font;
            titleTxt.fontSize  = 90;
            titleTxt.color     = new Color(0.88f, 0.04f, 0.04f, 1f);
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.fontStyle = FontStyle.Bold;

            var titleOutline           = titleRT.gameObject.AddComponent<Outline>();
            titleOutline.effectColor   = new Color(0f, 0f, 0f, 0.85f);
            titleOutline.effectDistance = new Vector2(3f, -3f);

            var titleShadow            = titleRT.gameObject.AddComponent<Shadow>();
            titleShadow.effectColor    = new Color(0.5f, 0f, 0f, 0.6f);
            titleShadow.effectDistance = new Vector2(0f, -6f);

            // ── Sous-titre (message aléatoire) ────────────────
            var subRT            = MakeRect("Subtitle", canvasGO.transform);
            subRT.anchorMin        = new Vector2(0.05f, 0.5f);
            subRT.anchorMax        = new Vector2(0.95f, 0.5f);
            subRT.pivot            = new Vector2(0.5f, 0.5f);
            subRT.anchoredPosition = new Vector2(0f, -20f);
            subRT.sizeDelta        = new Vector2(0f, 70f);

            _subtitleText           = subRT.gameObject.AddComponent<Text>();
            _subtitleText.text      = "";
            _subtitleText.font      = font;
            _subtitleText.fontSize  = 30;
            _subtitleText.color     = new Color(0.85f, 0.62f, 0.62f, 1f);
            _subtitleText.alignment = TextAnchor.MiddleCenter;
            _subtitleText.fontStyle = FontStyle.Italic;

            var subShadow            = subRT.gameObject.AddComponent<Shadow>();
            subShadow.effectColor    = new Color(0f, 0f, 0f, 0.7f);
            subShadow.effectDistance = new Vector2(1f, -2f);

            // ── Indication respawn ────────────────────────────
            var hintRT            = MakeRect("Hint", canvasGO.transform);
            hintRT.anchorMin        = new Vector2(0f, 0.5f);
            hintRT.anchorMax        = new Vector2(1f, 0.5f);
            hintRT.pivot            = new Vector2(0.5f, 0.5f);
            hintRT.anchoredPosition = new Vector2(0f, -90f);
            hintRT.sizeDelta        = new Vector2(0f, 40f);

            var hintTxt           = hintRT.gameObject.AddComponent<Text>();
            hintTxt.text          = "Respawn dans quelques secondes…";
            hintTxt.font          = font;
            hintTxt.fontSize      = 20;
            hintTxt.color         = new Color(0.55f, 0.55f, 0.55f, 0.85f);
            hintTxt.alignment     = TextAnchor.MiddleCenter;

            // ── Ligne de séparation décorative ────────────────
            var lineRT            = MakeRect("Separator", canvasGO.transform);
            lineRT.anchorMin        = new Vector2(0.3f, 0.5f);
            lineRT.anchorMax        = new Vector2(0.7f, 0.5f);
            lineRT.pivot            = new Vector2(0.5f, 0.5f);
            lineRT.anchoredPosition = new Vector2(0f, 30f);
            lineRT.sizeDelta        = new Vector2(0f, 2f);
            var lineImg             = lineRT.gameObject.AddComponent<Image>();
            lineImg.color           = new Color(0.6f, 0.04f, 0.04f, 0.7f);
        }

        private static RectTransform MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }
    }
}

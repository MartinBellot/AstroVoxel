// ============================================================
//  PauseMenu.cs
//  Menu pause (ÉCHAP) : Reprendre / Revenir au menu / Quitter.
//  Créé procéduralement (UGUI), même palette que MainMenu.
//
//  Priorité ÉCHAP :
//    1. Console / inventaire ouvert  → fermeture de ces overlays (géré par eux)
//    2. Pause ouverte                → reprendre
//    3. Rien d'ouvert, curseur libre → n'ouvre PAS (sécurité)
//    3. Rien d'ouvert, en jeu        → ouvre la pause
// ============================================================

using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Netcode;
using AstroVoxel.Player;

namespace AstroVoxel.Bootstrap
{
    public sealed class PauseMenu : MonoBehaviour
    {
        // ── Palette (identique à MainMenu) ──────────────────────
        private static readonly Color _card  = new Color(0.090f, 0.090f, 0.125f, 1f);   // #171720
        private static readonly Color _blue  = new Color(0.176f, 0.529f, 1.000f, 1f);   // #2D87FF
        private static readonly Color _red   = new Color(1.000f, 0.231f, 0.188f, 1f);   // #FF3B30
        private static readonly Color _green = new Color(0.200f, 0.780f, 0.353f, 1f);   // #33C75A

        // ── State ─────────────────────────────────────────────────
        public static bool IsPaused { get; private set; }

        // ── UI ────────────────────────────────────────────────────
        private GameObject _overlay;

        // Garde mémoire pour éviter qu'un ÉCHAP qui ferme un overlay
        // n'ouvre immédiatement la pause le même frame.
        private bool _prevOverlayOpen;

        // ── Init ──────────────────────────────────────────────────
        public void Init(Canvas canvas)
        {
            BuildUI(canvas);
            _overlay.SetActive(false);
            IsPaused = false;
        }

        // ── Lifecycle ─────────────────────────────────────────────
        private void Awake() => IsPaused = false;

        private void Update()
        {
            bool overlayOpen = GameConsole.IsOpen
                            || CreativeInventory.IsOpen
                            || SurvivalInventory.IsOpen;

#if ENABLE_INPUT_SYSTEM
            bool esc = UnityEngine.InputSystem.Keyboard.current != null
                    && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            bool esc = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (esc)
            {
                if (IsPaused)
                {
                    Resume();
                }
                else if (!overlayOpen && !_prevOverlayOpen)
                {
                    // Ouvrir uniquement si le curseur était verrouillé (joueur en jeu)
                    if (Cursor.lockState == CursorLockMode.Locked)
                        Open();
                }
            }

            _prevOverlayOpen = overlayOpen;
        }

        // ── Open / Resume ─────────────────────────────────────────

        private void Open()
        {
            IsPaused = true;
            _overlay.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            // Ne pas figer le temps en multijoueur (désynchronisation réseau)
            if (!IsNetworkActive())
                Time.timeScale = 0f;
        }

        public void Resume()
        {
            IsPaused         = false;
            _overlay.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
            Time.timeScale   = 1f;
        }

        // ── Actions ───────────────────────────────────────────────

        private void ReturnToMenu()
        {
            Time.timeScale = 1f;
            IsPaused       = false;

            // Couper le réseau proprement si actif
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            // Effacer les états en attente → GameBootstrap.Awake() affichera le menu
            AstroVoxel.Network.ServerManager.PendingJoinCode = null;
            GameBootstrap._pendingHost          = false;
            GameBootstrap._pendingGameModeValue = -1;

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static bool IsNetworkActive()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && (nm.IsServer || nm.IsClient);
        }

        // ── Build UI ──────────────────────────────────────────────

        private void BuildUI(Canvas canvas)
        {
            // ── Fond semi-transparent plein écran ───────────────
            var overlayGO  = new GameObject("PauseOverlay");
            overlayGO.transform.SetParent(canvas.transform, false);
            _overlay = overlayGO;

            var bgImg  = overlayGO.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.65f);
            var bgRT    = bgImg.rectTransform;
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            // ── Carte centrale ──────────────────────────────────
            var cardGO  = new GameObject("Card");
            cardGO.transform.SetParent(overlayGO.transform, false);
            var cardImg = cardGO.AddComponent<Image>();
            cardImg.color = _card;
            var cardRT  = cardImg.rectTransform;
            cardRT.anchorMin        = new Vector2(0.5f, 0.5f);
            cardRT.anchorMax        = new Vector2(0.5f, 0.5f);
            cardRT.pivot            = new Vector2(0.5f, 0.5f);
            cardRT.sizeDelta        = new Vector2(360f, 310f);
            cardRT.anchoredPosition = Vector2.zero;

            // ── Titre ───────────────────────────────────────────
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(cardGO.transform, false);
            var titleT  = titleGO.AddComponent<Text>();
            titleT.text      = "PAUSE";
            titleT.font      = GetFont();
            titleT.fontSize  = 28;
            titleT.fontStyle = FontStyle.Bold;
            titleT.color     = Color.white;
            titleT.alignment = TextAnchor.MiddleCenter;
            var titleRT = titleT.rectTransform;
            titleRT.anchorMin       = new Vector2(0f, 1f);
            titleRT.anchorMax       = new Vector2(1f, 1f);
            titleRT.pivot           = new Vector2(0.5f, 1f);
            titleRT.offsetMin       = new Vector2(0f, -76f);
            titleRT.offsetMax       = new Vector2(0f, -24f);

            // ── Séparateur ──────────────────────────────────────
            var sepGO  = new GameObject("Separator");
            sepGO.transform.SetParent(cardGO.transform, false);
            var sepImg = sepGO.AddComponent<Image>();
            sepImg.color = new Color(1f, 1f, 1f, 0.08f);
            var sepRT  = sepImg.rectTransform;
            sepRT.anchorMin       = new Vector2(0f, 1f);
            sepRT.anchorMax       = new Vector2(1f, 1f);
            sepRT.pivot           = new Vector2(0.5f, 1f);
            sepRT.offsetMin       = new Vector2(24f, -79f);
            sepRT.offsetMax       = new Vector2(-24f, -77f);

            // ── Boutons ─────────────────────────────────────────
            float btnY    = -110f;
            float btnStep =  64f;
            MakeButton(cardGO.transform, "Reprendre",       _green, btnY,             Resume);
            MakeButton(cardGO.transform, "Revenir au menu", _blue,  btnY - btnStep,    ReturnToMenu);
            MakeButton(cardGO.transform, "Quitter",         _red,   btnY - btnStep*2f, QuitGame);
        }

        private void MakeButton(Transform parent, string label, Color color, float anchoredY, Action callback)
        {
            var go  = new GameObject(label);
            go.transform.SetParent(parent, false);
            var img  = go.AddComponent<Image>();
            img.color = color;
            var rt   = img.rectTransform;
            rt.anchorMin        = new Vector2(0.5f, 1f);
            rt.anchorMax        = new Vector2(0.5f, 1f);
            rt.pivot            = new Vector2(0.5f, 0.5f);
            rt.sizeDelta        = new Vector2(280f, 46f);
            rt.anchoredPosition = new Vector2(0f, anchoredY);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var cs = btn.colors;
            cs.normalColor      = color;
            cs.highlightedColor = color * 1.18f;
            cs.pressedColor     = color * 0.78f;
            cs.selectedColor    = color;
            cs.fadeDuration     = 0.08f;
            btn.colors          = cs;
            btn.onClick.AddListener(() => callback());

            var lblGO = new GameObject("Label");
            lblGO.transform.SetParent(go.transform, false);
            var lblT  = lblGO.AddComponent<Text>();
            lblT.text      = label;
            lblT.font      = GetFont();
            lblT.fontSize  = 15;
            lblT.fontStyle = FontStyle.Bold;
            lblT.color     = Color.white;
            lblT.alignment = TextAnchor.MiddleCenter;
            var lblRT = lblT.rectTransform;
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero;
            lblRT.offsetMax = Vector2.zero;
        }

        private static Font GetFont()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }
    }
}

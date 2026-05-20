// ============================================================
//  Scoreboard.cs
//  Tableau des scores affiché en appuyant sur TAB.
//  Visible uniquement quand le réseau est actif.
//
//  Colonnes : Rôle | Nom joueur | Latence (ping)
//  Design   : Dark Apple theme, cohérent avec HudBuilder.
// ============================================================

using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AstroVoxel.Network
{
    /// <summary>
    /// Scoreboard multijoueur activé/désactivé par la touche TAB.
    /// Créé par <see cref="AstroVoxel.Player.HudBuilder.Init"/>.
    /// </summary>
    public sealed class Scoreboard : MonoBehaviour
    {
        // ── Palette Dark Theme ────────────────────────────────
        private static readonly Color _bgPanel    = new Color(0.04f, 0.04f, 0.05f, 0.92f);
        private static readonly Color _border     = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color _title      = new Color(1f, 1f, 1f, 0.92f);
        private static readonly Color _rowBg      = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color _rowBgAlt   = new Color(1f, 1f, 1f, 0.02f);
        private static readonly Color _textPrim   = new Color(1f, 1f, 1f, 0.88f);
        private static readonly Color _textSec    = new Color(1f, 1f, 1f, 0.45f);
        private static readonly Color _hostBadge  = new Color(1.00f, 0.78f, 0.00f, 1f); // or
        private static readonly Color _pingGreen  = new Color(0.30f, 0.90f, 0.40f, 1f);
        private static readonly Color _pingYellow = new Color(1.00f, 0.80f, 0.10f, 1f);
        private static readonly Color _pingRed    = new Color(1.00f, 0.33f, 0.30f, 1f);

        private Canvas         _canvas;
        private GameObject     _panel;
        private bool           _visible;

        private float          _refreshTimer;
        private const float    RefreshInterval = 1f;  // rafraîchit les pings toutes les secondes

        // ── Build ─────────────────────────────────────────────

        public static Scoreboard Create(Canvas canvas)
        {
            var go = new GameObject("Scoreboard");
            go.transform.SetParent(canvas.transform, false);
            var s = go.AddComponent<Scoreboard>();
            s._canvas = canvas;
            s.Build();
            return s;
        }

        private void Build()
        {
            _panel = new GameObject("Panel");
            _panel.transform.SetParent(transform, false);

            var panelRT = _panel.AddComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot     = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta = new Vector2(480, 300);

            var bg = _panel.AddComponent<Image>();
            bg.color = _bgPanel;

            // Bord
            AddBorder(panelRT);

            // Titre
            AddTitle();

            _panel.SetActive(false);
        }

        private void AddBorder(RectTransform parent)
        {
            void MakeBorder(string n, Vector2 amin, Vector2 amax, Vector2 size)
            {
                var g = new GameObject(n);
                g.transform.SetParent(parent, false);
                var rt = g.AddComponent<RectTransform>();
                rt.anchorMin = amin; rt.anchorMax = amax;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                if (size != Vector2.zero) rt.sizeDelta = size;
                var img = g.AddComponent<Image>();
                img.color = _border;
            }
            // 1px border simulation (top/bottom/left/right)
            MakeBorder("BorderTop",    new Vector2(0,0), new Vector2(1,1), new Vector2(0, 1));
            MakeBorder("BorderBottom", new Vector2(0,0), new Vector2(1,0), new Vector2(0, 1));
        }

        private void AddTitle()
        {
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(_panel.transform, false);
            var rt = titleGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -36f);
            rt.offsetMax = Vector2.zero;

            var t = titleGO.AddComponent<Text>();
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize  = 14;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color     = _title;
            t.text      = "▌ JOUEURS CONNECTÉS ▐";
        }

        // ── Toggle ────────────────────────────────────────────

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
                TryToggle();
#else
            if (Input.GetKeyDown(KeyCode.Tab))
                TryToggle();
#endif
            // Rafraîchit le contenu si visible
            if (_visible && ServerManager.IsNetworkActive)
            {
                _refreshTimer += Time.deltaTime;
                if (_refreshTimer >= RefreshInterval)
                {
                    _refreshTimer = 0f;
                    RefreshRows();
                }
            }
            else if (_visible && !ServerManager.IsNetworkActive)
            {
                Hide();
            }
        }

        private void TryToggle()
        {
            if (!ServerManager.IsNetworkActive) return;
            if (_visible) Hide(); else Show();
        }

        private void Show()
        {
            _visible = true;
            _panel.SetActive(true);
            _refreshTimer = RefreshInterval; // force refresh immédiat
        }

        private void Hide()
        {
            _visible = false;
            _panel.SetActive(false);
        }

        // ── Rows ──────────────────────────────────────────────

        private void RefreshRows()
        {
            // Supprimer les anciennes lignes
            for (int i = _panel.transform.childCount - 1; i >= 0; i--)
            {
                var child = _panel.transform.GetChild(i);
                if (child.name.StartsWith("Row_")) Destroy(child.gameObject);
            }

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;

            var transport = nm.GetComponent<UnityTransport>();
            int row = 0;

            foreach (var clientId in nm.ConnectedClientsIds)
            {
                bool isLocalPlayer = clientId == nm.LocalClientId;
                bool isHostClient  = clientId == 0;

                ulong ping = 0;
                if (nm.IsServer && transport != null)
                    ping = transport.GetCurrentRtt(clientId);

                string roleStr = isHostClient ? "HOST" : "Client";
                string nameStr = isLocalPlayer ? "Vous" : $"Joueur {clientId}";
                string pingStr = nm.IsServer ? $"{ping} ms" : "— ms";

                AddRow(row, nameStr, roleStr, pingStr, isLocalPlayer, isHostClient, ping);
                row++;
            }

            // Redimensionner le panel selon le nombre de joueurs
            float panelH = 40f + Mathf.Max(row, 1) * 36f + 12f;
            var panelRT  = _panel.GetComponent<RectTransform>();
            panelRT.sizeDelta = new Vector2(480, panelH);
        }

        private void AddRow(int index, string name, string role, string ping,
                            bool isLocal, bool isHost, ulong pingMs)
        {
            var rowGO = new GameObject($"Row_{index}");
            rowGO.transform.SetParent(_panel.transform, false);

            var rt = rowGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            float yOffset = -(40f + index * 36f);
            rt.offsetMin = new Vector2(0f, yOffset - 34f);
            rt.offsetMax = new Vector2(0f, yOffset);

            // Fond alterné
            var rowBg = rowGO.AddComponent<Image>();
            rowBg.color = index % 2 == 0 ? _rowBg : _rowBgAlt;

            // ── Colonne Rôle (gauche) ─────────────────────────
            AddRowText(rowGO, "Role", role,
                new Vector2(0f, 0f), new Vector2(0.22f, 1f),
                isHost ? _hostBadge : _textSec, 11, FontStyle.Bold);

            // ── Colonne Nom (centre) ──────────────────────────
            string displayName = isLocal ? $"★ {name}" : name;
            AddRowText(rowGO, "Name", displayName,
                new Vector2(0.22f, 0f), new Vector2(0.75f, 1f),
                _textPrim, 13, isLocal ? FontStyle.Bold : FontStyle.Normal);

            // ── Colonne Ping (droite) ─────────────────────────
            Color pingColor = pingMs < 50 ? _pingGreen : (pingMs < 120 ? _pingYellow : _pingRed);
            if (ping == "— ms") pingColor = _textSec;
            AddRowText(rowGO, "Ping", ping,
                new Vector2(0.75f, 0f), new Vector2(1f, 1f),
                pingColor, 12, FontStyle.Normal, TextAnchor.MiddleRight);
        }

        private static void AddRowText(GameObject parent, string name, string content,
            Vector2 anchorMin, Vector2 anchorMax,
            Color color, int fontSize, FontStyle style,
            TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(8f, 0f);
            rt.offsetMax = new Vector2(-8f, 0f);
            var t = go.AddComponent<Text>();
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize  = fontSize;
            t.fontStyle = style;
            t.alignment = align;
            t.color     = color;
            t.text      = content;
        }
    }
}

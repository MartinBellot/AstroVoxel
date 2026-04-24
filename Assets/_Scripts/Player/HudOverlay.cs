// ============================================================
//  HudOverlay.cs
//  Affiche en haut-gauche : FPS, coordonnées joueur,
//  coordonnées du bloc sélectionné.
// ============================================================

using UnityEngine;
using UnityEngine.UI;

namespace AstroVoxel.Player
{
    public sealed class HudOverlay : MonoBehaviour
    {
        // ── Références ────────────────────────────────────────
        private Transform         _playerBody;
        private BlockInteraction  _blockInteraction;

        // ── UI ────────────────────────────────────────────────
        private Text _fpsText;
        private Text _posText;
        private Text _blockText;

        // ── FPS lissés ────────────────────────────────────────
        private float _fpsAccum;
        private int   _fpsFrames;
        private float _fpsCurrent;
        private const float FpsInterval = 0.25f;
        private float _fpsTimer;

        // ── Init ──────────────────────────────────────────────

        public void Init(Transform playerBody, BlockInteraction blockInteraction, Canvas canvas)
        {
            _playerBody       = playerBody;
            _blockInteraction = blockInteraction;

            BuildUI(canvas);
        }

        // ── Cycle de vie ──────────────────────────────────────

        private void Update()
        {
            UpdateFps();
            UpdateTexts();
        }

        // ── FPS ───────────────────────────────────────────────

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

        // ── Textes ────────────────────────────────────────────

        private void UpdateTexts()
        {
            if (_fpsText != null)
                _fpsText.text = $"FPS: {_fpsCurrent:F0}";

            if (_posText != null && _playerBody != null)
            {
                Vector3 p = _playerBody.position;
                _posText.text = $"XYZ: {p.x:F1}  {p.y:F1}  {p.z:F1}";
            }

            if (_blockText != null)
            {
                Vector3? b = _blockInteraction != null
                    ? _blockInteraction.TargetBlockPos
                    : null;
                _blockText.text = b.HasValue
                    ? $"Bloc: {(int)b.Value.x}  {(int)b.Value.y}  {(int)b.Value.z}"
                    : "Bloc: —";
            }
        }

        // ── Construction UI ───────────────────────────────────

        private void BuildUI(Canvas canvas)
        {
            // Racine ancrée en haut-gauche
            var rootGO = new GameObject("HudOverlayRoot");
            rootGO.transform.SetParent(canvas.transform, false);
            var rootRT = rootGO.AddComponent<RectTransform>();
            rootRT.anchorMin        = new Vector2(0f, 1f);
            rootRT.anchorMax        = new Vector2(0f, 1f);
            rootRT.pivot            = new Vector2(0f, 1f);
            rootRT.anchoredPosition = new Vector2(10f, -10f);
            rootRT.sizeDelta        = new Vector2(300f, 72f);

            // Fond semi-transparent
            var bg    = rootGO.AddComponent<Image>();
            bg.color  = new Color(0f, 0f, 0f, 0.45f);

            // Trois lignes
            _fpsText   = CreateLine(rootGO.transform, 0, "FPS: …");
            _posText   = CreateLine(rootGO.transform, 1, "XYZ: …");
            _blockText = CreateLine(rootGO.transform, 2, "Bloc: —");
        }

        private static Text CreateLine(Transform parent, int lineIndex, string initial)
        {
            const float lineH   = 22f;
            const float padding =  4f;

            var go = new GameObject($"HudLine_{lineIndex}");
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(padding, -(padding + lineIndex * lineH));
            rt.sizeDelta        = new Vector2(-padding * 2f, lineH);

            var txt = go.AddComponent<Text>();
            txt.text      = initial;
            txt.fontSize  = 13;
            txt.color     = Color.white;
            txt.alignment = TextAnchor.MiddleLeft;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 13);
            txt.font = font;

            // Ombre portée pour lisibilité sur fond clair
            var shadow  = go.AddComponent<Shadow>();
            shadow.effectColor    = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(1f, -1f);

            return txt;
        }
    }
}

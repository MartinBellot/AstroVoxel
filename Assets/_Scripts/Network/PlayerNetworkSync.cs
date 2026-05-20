// ============================================================
//  PlayerNetworkSync.cs
//  Proxy de position pour chaque joueur connecté.
//  MonoBehaviour simple géré par ServerManager — aucun NGO PlayerPrefab.
//
//  - isLocal = true  : joueur local, envoie sa position via CustomMessaging.
//  - isLocal = false : joueur distant, reçoit les positions et anime un
//    mannequin capsule + nametag représentant le joueur.
// ============================================================

using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace AstroVoxel.Network
{
    public sealed class PlayerNetworkSync : MonoBehaviour
    {
        // ── Registre statique : lookup par clientId ────────────
        private static readonly Dictionary<ulong, PlayerNetworkSync> _registry = new();

        public static PlayerNetworkSync GetById(ulong clientId) =>
            _registry.TryGetValue(clientId, out var s) ? s : null;

        // ── Identité ──────────────────────────────────────────
        private ulong _clientId;
        private bool  _isLocal;

        // ── Position distante (non-local) ─────────────────────
        private Vector3    _remoteTargetPos;
        private Quaternion _remoteTargetRot = Quaternion.identity;
        private bool       _hasRemotePosition;

        // ── Timer d'envoi de position (local) ─────────────────
        private float      _syncTimer;
        private const float SyncInterval = 0.1f; // 10 Hz

        // ── État embarqué ─────────────────────────────────────
        private bool _inShip;

        // ── Références ────────────────────────────────────────
        private Transform  _localPlayerTransform;
        private GameObject _remoteCapsule;
        private Text       _nameText;
        private float      _nameFaceCamTimer;
        private const float NameFaceInterval = 0.1f;

        // Palette Dark Theme cohérente
        private static readonly Color _bodyColor = new Color(0.30f, 0.55f, 1.00f);
        private static readonly Color _headColor = new Color(0.38f, 0.62f, 1.00f);

        // ── Initialisation explicite (appelée par ServerManager) ──

        public void Init(ulong clientId, bool isLocal)
        {
            _clientId = clientId;
            _isLocal  = isLocal;
            _registry[clientId] = this;

            if (isLocal)
            {
                var pc = FindAnyObjectByType<AstroVoxel.Player.PlayerController>();
                if (pc != null) _localPlayerTransform = pc.transform;
                gameObject.name = "PlayerNet_Local";
            }
            else
            {
                gameObject.name = $"PlayerNet_{clientId}";
                BuildRemoteCapsule();
            }
        }

        /// <summary>Supprime le sync et le mannequin associé.</summary>
        public void Cleanup()
        {
            _registry.Remove(_clientId);
            if (_remoteCapsule != null) Destroy(_remoteCapsule);
            Destroy(gameObject);
        }

        /// <summary>Supprime tous les syncs (appelé lors d'une déconnexion).</summary>
        public static void DestroyAll()
        {
            var copy = new System.Collections.Generic.List<PlayerNetworkSync>(_registry.Values);
            _registry.Clear();
            foreach (var s in copy)
            {
                if (s == null) continue;
                if (s._remoteCapsule != null) Destroy(s._remoteCapsule);
                Destroy(s.gameObject);
            }
        }

        private void OnDestroy()
        {
            // Filet de sécurité : nettoyer le registre si le GO est détruit
            // (ex. rechargement de scène) sans passer par Cleanup().
            if (_registry.TryGetValue(_clientId, out var s) && ReferenceEquals(s, this))
                _registry.Remove(_clientId);
        }

        // ── API publique : appelée par ServerManager ──────────

        public void SetRemotePosition(Vector3 pos, Quaternion rot, bool inShip)
        {
            if (_isLocal) return;
            _remoteTargetPos   = pos;
            _remoteTargetRot   = rot;
            _hasRemotePosition = true;

            // Cacher / montrer la capsule selon l'état embarqué
            if (_remoteCapsule != null)
            {
                bool shouldShow = !inShip;
                if (_remoteCapsule.activeSelf != shouldShow)
                    _remoteCapsule.SetActive(shouldShow);
            }
        }

        // ── Envoi de position (CustomMessaging) ───────────────

        private void SendPositionUpdate()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            using var w = new FastBufferWriter(41, Allocator.Temp);
            w.WriteValueSafe(_clientId);
            w.WriteValueSafe(_localPlayerTransform.position);
            w.WriteValueSafe(_localPlayerTransform.rotation);
            w.WriteValueSafe((byte)(_inShip ? 1 : 0));

            if (nm.IsServer)
            {
                // HOST : envoie directement sa position à chaque client connecté
                foreach (var cid in nm.ConnectedClientsIds)
                {
                    if (cid == nm.LocalClientId) continue;
                    nm.CustomMessagingManager.SendNamedMessage(
                        ServerManager.MsgPlayerPos, cid, w,
                        NetworkDelivery.UnreliableSequenced);
                }
            }
            else
            {
                // CLIENT : envoie au serveur qui relaiera aux autres
                nm.CustomMessagingManager.SendNamedMessage(
                    ServerManager.MsgPlayerPos, NetworkManager.ServerClientId, w,
                    NetworkDelivery.UnreliableSequenced);
            }
        }

        // ── Update ────────────────────────────────────────────

        private void Update()
        {
            if (_isLocal)
            {
                if (_localPlayerTransform == null)
                {
                    var pc = FindAnyObjectByType<AstroVoxel.Player.PlayerController>();
                    if (pc != null) _localPlayerTransform = pc.transform;
                    return;
                }

                // Détecter montée / descente du vaisseau via l'état actif du GO joueur
                bool nowInShip = !_localPlayerTransform.gameObject.activeSelf;
                if (nowInShip != _inShip)
                {
                    _inShip    = nowInShip;
                    _syncTimer = SyncInterval; // forcer envoi immédiat
                }

                // En vaisseau : inutile d'envoyer des mises à jour de position
                if (_inShip) return;

                // Envoi de position à 10 Hz via CustomMessaging
                _syncTimer += Time.deltaTime;
                if (_syncTimer < SyncInterval) return;
                _syncTimer = 0f;
                SendPositionUpdate();
            }
            else if (_remoteCapsule != null && _hasRemotePosition)
            {
                // Téléporte si > 5 unités, sinon lerp fluide
                float sqrDist = (_remoteCapsule.transform.position - _remoteTargetPos).sqrMagnitude;
                if (sqrDist > 25f)
                {
                    _remoteCapsule.transform.position = _remoteTargetPos;
                    _remoteCapsule.transform.rotation = _remoteTargetRot;
                }
                else
                {
                    _remoteCapsule.transform.position = Vector3.Lerp(
                        _remoteCapsule.transform.position, _remoteTargetPos, Time.deltaTime * 15f);
                    _remoteCapsule.transform.rotation = Quaternion.Slerp(
                        _remoteCapsule.transform.rotation, _remoteTargetRot, Time.deltaTime * 15f);
                }

                // Orienter le nametag vers la caméra (à 10 Hz)
                _nameFaceCamTimer += Time.deltaTime;
                if (_nameFaceCamTimer >= NameFaceInterval)
                {
                    _nameFaceCamTimer = 0f;
                    FaceNameTagToCamera();
                }
            }
        }

        // ── Mannequin distant ─────────────────────────────────

        private void BuildRemoteCapsule()
        {
            _remoteCapsule = new GameObject($"RemotePlayer_{_clientId}");

            // ── Corps (capsule) ───────────────────────────────
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(_remoteCapsule.transform, false);
            body.transform.localPosition = Vector3.up * 0.9f;
            body.transform.localScale    = new Vector3(0.8f, 0.9f, 0.8f);
            Object.Destroy(body.GetComponent<Collider>());
            ApplyColor(body, _bodyColor);

            // ── Tête (sphère) ─────────────────────────────────
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(_remoteCapsule.transform, false);
            head.transform.localPosition = Vector3.up * 1.85f;
            head.transform.localScale    = Vector3.one * 0.55f;
            Object.Destroy(head.GetComponent<Collider>());
            ApplyColor(head, _headColor);

            // ── Nametag world-space ───────────────────────────
            BuildNameTag();
        }

        private void BuildNameTag()
        {
            var tagGO = new GameObject("NameTag");
            tagGO.transform.SetParent(_remoteCapsule.transform, false);
            tagGO.transform.localPosition = Vector3.up * 2.35f;

            var canvas = tagGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.WorldSpace;
            canvas.sortingOrder = 5;

            var rt = tagGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(3f, 0.5f);
            tagGO.transform.localScale = Vector3.one * 0.01f;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(tagGO.transform, false);

            _nameText = textGO.AddComponent<Text>();
            _nameText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _nameText.fontSize  = 36;
            _nameText.alignment = TextAnchor.MiddleCenter;
            _nameText.color     = Color.white;
            _nameText.text      = $"Joueur {_clientId}";

            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = textRT.offsetMax = Vector2.zero;

            // Fond semi-transparent
            var bg = new GameObject("Bg");
            bg.transform.SetParent(tagGO.transform, false);
            bg.transform.SetAsFirstSibling();
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0, 0, 0, 0.55f);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = new Vector2(-4f, -2f);
            bgRT.offsetMax = new Vector2( 4f,  2f);
        }

        // ── Orientation du nametag ────────────────────────────

        private void FaceNameTagToCamera()
        {
            var cam = Camera.main;
            if (cam == null || _remoteCapsule == null) return;

            var nameTag = _remoteCapsule.transform.Find("NameTag");
            if (nameTag == null) return;

            Vector3 dir = nameTag.position - cam.transform.position;
            if (dir.sqrMagnitude < 0.001f) return;
            nameTag.rotation = Quaternion.LookRotation(dir);
        }

        private static void ApplyColor(GameObject go, Color color)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Standard");
            if (shader == null) return;
            var mat = new Material(shader);
            // Compatibilité URP (_BaseColor) et Standard (_Color)
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     color);
            rend.material = mat; // material d'instance (pas partagé)
        }
    }
}

// ============================================================
//  PlayerNetworkSync.cs
//  NetworkBehaviour servant de "proxy réseau" pour chaque joueur.
//  Utilisé comme PlayerPrefab de NGO (spawn automatique par client).
//
//  - IsOwner = true  : envoie la position locale via ServerRpc → ClientRpc
//                      (plus fiable que NetworkVariable owner-write en NGO 2.x).
//  - IsOwner = false : reçoit les positions via ClientRpc et anime un mannequin
//    capsule + nametag représentant le joueur distant.
// ============================================================

using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace AstroVoxel.Network
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerNetworkSync : NetworkBehaviour
    {
        // ── Position distante reçue par RPC (non-owner) ───────
        private Vector3    _remoteTargetPos;
        private Quaternion _remoteTargetRot = Quaternion.identity;
        private bool       _hasRemotePosition;

        // ── Timer d'envoi de position (owner) ─────────────────
        private float      _syncTimer;
        private const float SyncInterval = 0.1f; // 10 Hz

        // ── Références ────────────────────────────────────────
        private Transform  _localPlayerTransform;
        private GameObject _remoteCapsule;
        private Text       _nameText;
        private float      _nameFaceCamTimer;
        private const float NameFaceInterval = 0.1f;

        // Palette Dark Theme cohérente
        private static readonly Color _bodyColor = new Color(0.30f, 0.55f, 1.00f);
        private static readonly Color _headColor = new Color(0.38f, 0.62f, 1.00f);

        // ── NGO Lifecycle ─────────────────────────────────────

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                var pc = FindAnyObjectByType<AstroVoxel.Player.PlayerController>();
                if (pc != null) _localPlayerTransform = pc.transform;
                gameObject.name = "PlayerNet_Local";
            }
            else
            {
                gameObject.name = $"PlayerNet_{OwnerClientId}";
                BuildRemoteCapsule();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_remoteCapsule != null) Destroy(_remoteCapsule);
        }

        // ── RPCs position ─────────────────────────────────────

        // Owner → Server : envoie la position locale
        [ServerRpc(RequireOwnership = true)]
        private void UpdatePositionServerRpc(Vector3 pos, Quaternion rot)
        {
            // Le serveur relaie à tous les clients
            UpdatePositionClientRpc(pos, rot);
        }

        // Server → tous les clients : mise à jour de la cible
        [ClientRpc]
        private void UpdatePositionClientRpc(Vector3 pos, Quaternion rot)
        {
            if (IsOwner) return; // l'owner n'a pas besoin de se mettre à jour lui-même
            _remoteTargetPos   = pos;
            _remoteTargetRot   = rot;
            _hasRemotePosition = true;
        }

        // ── Update ────────────────────────────────────────────

        private void Update()
        {
            if (IsOwner)
            {
                if (_localPlayerTransform == null)
                {
                    var pc = FindAnyObjectByType<AstroVoxel.Player.PlayerController>();
                    if (pc != null) _localPlayerTransform = pc.transform;
                    return;
                }
                // Envoi de position à 10 Hz via ServerRpc
                _syncTimer += Time.deltaTime;
                if (_syncTimer >= SyncInterval)
                {
                    _syncTimer = 0f;
                    UpdatePositionServerRpc(
                        _localPlayerTransform.position,
                        _localPlayerTransform.rotation);
                }
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
            _remoteCapsule = new GameObject($"RemotePlayer_{OwnerClientId}");

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
            _nameText.text      = $"Joueur {OwnerClientId}";

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

// ============================================================
//  PlayerNetworkSync.cs
//  Proxy de position pour chaque joueur connecté.
//  MonoBehaviour simple géré par ServerManager — aucun NGO PlayerPrefab.
//
//  - isLocal = true  : joueur local, envoie sa position + pitch caméra + vitesse.
//  - isLocal = false : joueur distant, reçoit les données et anime un
//    mannequin Minecraft-like (corps cubique, tête qui suit le regard,
//    corps avec délai de rotation, animation de marche/course) + nametag.
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

        /// <summary>ClientId du joueur associé à ce sync.</summary>
        public ulong ClientId => _clientId;

        /// <summary>Transform du mannequin distant (null si local ou mannequin non créé).</summary>
        public Transform RemoteTransform => _remoteCapsule != null ? _remoteCapsule.transform : null;

        /// <summary>Itère sur tous les syncs distants (joueurs clients).</summary>
        public static void ForEachRemote(System.Action<PlayerNetworkSync> action)
        {
            foreach (var s in _registry.Values)
                if (s != null && !s._isLocal) action(s);
        }

        // ── Identité ──────────────────────────────────────────
        private ulong _clientId;
        private bool  _isLocal;

        // ── Position distante (non-local) ─────────────────────
        private Vector3    _remoteTargetPos;
        private Quaternion _remoteTargetRot = Quaternion.identity;
        private bool       _hasRemotePosition;

        // Données d'animation distantes
        private float      _remoteHeadPitch;           // pitch caméra (degrés)
        private float      _remoteSpeed;               // vitesse horizontale (m/s)
        private Quaternion _remoteBodyRot = Quaternion.identity; // yaw corps (laggy)

        // ── Timer d'envoi de position (local) ─────────────────
        private float      _syncTimer;
        private const float SyncInterval = 0.1f; // 10 Hz

        // ── État embarqué ─────────────────────────────────────
        private bool _inShip;

        // ── Références locales ────────────────────────────────
        private Transform  _localPlayerTransform;
        private Transform  _localCameraTransform;
        private Rigidbody  _localRigidbody;

        // ── Références mannequin ──────────────────────────────
        private GameObject _remoteCapsule;   // racine (position seule)
        private Transform  _bodyPivot;       // pivot corps (yaw avec délai)
        private Transform  _headPivot;       // pivot tête (yaw+pitch instantané)
        private Transform  _armLPivot;       // pivot épaule gauche
        private Transform  _armRPivot;       // pivot épaule droite
        private Transform  _legLPivot;       // pivot hanche gauche
        private Transform  _legRPivot;       // pivot hanche droite

        private Text       _nameText;
        private float      _walkCycle;       // accumulateur animation de marche

        // Nametag orientation
        private float      _nameFaceCamTimer;
        private const float NameFaceInterval = 0.1f;

        // ── Initialisation explicite (appelée par ServerManager) ──

        public void Init(ulong clientId, bool isLocal)
        {
            _clientId = clientId;
            _isLocal  = isLocal;
            _registry[clientId] = this;

            if (isLocal)
            {
                CacheLocalRefs();
                gameObject.name = "PlayerNet_Local";
            }
            else
            {
                gameObject.name = $"PlayerNet_{clientId}";
                BuildRemoteCapsule();
            }
        }

        private void CacheLocalRefs()
        {
            var pc = FindAnyObjectByType<AstroVoxel.Player.PlayerController>();
            if (pc == null) return;
            _localPlayerTransform = pc.transform;
            _localRigidbody       = pc.GetComponent<Rigidbody>();
            var camComp = pc.GetComponentInChildren<AstroVoxel.Player.PlayerCamera>();
            _localCameraTransform = camComp != null ? camComp.transform : Camera.main?.transform;
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

        public void SetRemotePosition(Vector3 pos, Quaternion rot, float headPitch, float speed, bool inShip)
        {
            if (_isLocal) return;
            _remoteTargetPos = pos;
            _remoteTargetRot = rot;
            _remoteHeadPitch = headPitch;
            _remoteSpeed     = speed;

            // Premier paquet : snap immédiat, pas de lag initial
            if (!_hasRemotePosition)
            {
                _remoteBodyRot = rot;
                if (_remoteCapsule != null)
                {
                    _remoteCapsule.transform.position = pos;
                    _remoteCapsule.transform.rotation = rot;
                }
            }
            _hasRemotePosition = true;

            // Cacher / montrer le mannequin selon l'état embarqué
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

            // Pitch de la caméra locale (converti en [-180, 180])
            float headPitch = 0f;
            if (_localCameraTransform != null)
            {
                headPitch = _localCameraTransform.localEulerAngles.x;
                if (headPitch > 180f) headPitch -= 360f;
            }

            // Vitesse horizontale projetée sur le plan tangent à la planète
            float speed = 0f;
            if (_localRigidbody != null)
            {
                Vector3 up = _localPlayerTransform.up;
                speed = Vector3.ProjectOnPlane(_localRigidbody.linearVelocity, up).magnitude;
            }

            // Buffer : clientId(8) + pos(12) + rot(16) + headPitch(4) + speed(4) + inShip(1) = 45 B
            using var w = new FastBufferWriter(48, Allocator.Temp);
            w.WriteValueSafe(_clientId);
            w.WriteValueSafe(_localPlayerTransform.position);
            w.WriteValueSafe(_localPlayerTransform.rotation);
            w.WriteValueSafe(headPitch);
            w.WriteValueSafe(speed);
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
                    CacheLocalRefs();
                    return;
                }

                // Détecter montée / descente du vaisseau via l'état actif du GO joueur
                bool nowInShip = !_localPlayerTransform.gameObject.activeSelf;
                if (nowInShip != _inShip)
                {
                    _inShip    = nowInShip;
                    _syncTimer = 0f;
                    SendPositionUpdate(); // notifier immédiatement les autres du changement d'état
                    return;
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
                UpdateRemotePosition();
                UpdateRemoteAnimation();

                // Orienter le nametag vers la caméra (à 10 Hz)
                _nameFaceCamTimer += Time.deltaTime;
                if (_nameFaceCamTimer >= NameFaceInterval)
                {
                    _nameFaceCamTimer = 0f;
                    FaceNameTagToCamera();
                }
            }
        }

        // ── Interpolation position + rotations séparées ───────

        private void UpdateRemotePosition()
        {
            // Position : téléporte si > 5 u, sinon lerp fluide
            float sqrDist = (_remoteCapsule.transform.position - _remoteTargetPos).sqrMagnitude;
            if (sqrDist > 25f)
            {
                _remoteCapsule.transform.position = _remoteTargetPos;
                _remoteBodyRot = _remoteTargetRot;
            }
            else
            {
                _remoteCapsule.transform.position = Vector3.Lerp(
                    _remoteCapsule.transform.position, _remoteTargetPos, Time.deltaTime * 15f);
            }

            // Rotation racine : s'aligne rapidement sur la planète (pour que les
            // offsets locaux des enfants soient dans le bon repère de gravité)
            _remoteCapsule.transform.rotation = Quaternion.Slerp(
                _remoteCapsule.transform.rotation, _remoteTargetRot, Time.deltaTime * 15f);

            // Corps : suit le yaw cible avec un délai (plus lent à l'arrêt → effet Minecraft)
            float bodySpeed = _remoteSpeed > 0.5f ? 10f : 2.5f;
            _remoteBodyRot = Quaternion.Slerp(_remoteBodyRot, _remoteTargetRot, Time.deltaTime * bodySpeed);
            if (_bodyPivot != null)
                _bodyPivot.rotation = _remoteBodyRot; // rotation monde

            // Tête : snap instantané sur le yaw + pitch de la caméra en espace local
            if (_headPivot != null)
                _headPivot.localRotation = Quaternion.Euler(_remoteHeadPitch, 0f, 0f);
        }

        // ── Animation marche / course (balancement membres) ───

        private void UpdateRemoteAnimation()
        {
            if (_armLPivot == null) return;

            if (_remoteSpeed < 0.3f)
            {
                // Idle : membres reviennent doucement en position neutre
                float t   = Time.deltaTime * 8f;
                var   idle = Quaternion.identity;
                _armLPivot.localRotation = Quaternion.Lerp(_armLPivot.localRotation, idle, t);
                _armRPivot.localRotation = Quaternion.Lerp(_armRPivot.localRotation, idle, t);
                _legLPivot.localRotation = Quaternion.Lerp(_legLPivot.localRotation, idle, t);
                _legRPivot.localRotation = Quaternion.Lerp(_legRPivot.localRotation, idle, t);
            }
            else
            {
                bool  running = _remoteSpeed > 7f;
                float freq    = running ? 6.5f : 4.0f;  // Hz du cycle
                float amp     = running ? 45f  : 30f;   // amplitude en degrés

                _walkCycle += Time.deltaTime * freq;
                float swing = Mathf.Sin(_walkCycle) * amp;

                // Bras et jambes en opposition (style Minecraft)
                _armLPivot.localRotation = Quaternion.Euler( swing, 0f, 0f);
                _armRPivot.localRotation = Quaternion.Euler(-swing, 0f, 0f);
                _legLPivot.localRotation = Quaternion.Euler(-swing, 0f, 0f);
                _legRPivot.localRotation = Quaternion.Euler( swing, 0f, 0f);
            }
        }

        // ── Construction du mannequin Minecraft-like ──────────
        //
        //  Proportions (en unités Unity, 1 u ≈ 1 bloc Minecraft) :
        //    Pieds  y=0.00  ──  Hanches  y=0.75  ──  Épaules  y=1.50
        //    Haut tête y=2.00  (taille totale ≈ 2 u, Steve ≈ 1.95 blocs)
        //
        //  Structure :
        //    RemotePlayer_X   (racine, position seule)
        //    ├── BodyPivot    (y=0, rotation monde = yaw laggy)
        //    │   ├── Torso    (cube 0.50×0.75×0.25, centre y=1.125)
        //    │   ├── ArmLPivot (pivot épaule y=1.5, x=-0.375)
        //    │   │   └── ArmL  (cube 0.25×0.75×0.25, pendant à y=-0.375)
        //    │   ├── ArmRPivot (pivot épaule y=1.5, x=+0.375)
        //    │   │   └── ArmR
        //    │   ├── LegLPivot (pivot hanche y=0.75, x=-0.125)
        //    │   │   └── LegL  (cube 0.25×0.75×0.25, pendant à y=-0.375)
        //    │   └── LegRPivot (pivot hanche y=0.75, x=+0.125)
        //    │       └── LegR
        //    ├── HeadPivot    (y=1.5 dans espace racine, localRot = pitch seul)
        //    │   ├── Head     (cube 0.50×0.50×0.50, centre y=0.25)
        //    │   ├── EyeL     (cube noir, face avant)
        //    │   └── EyeR
        //    └── NameTag      (canvas world-space, y=2.25)

        private void BuildRemoteCapsule()
        {
            EnsureSkinLoaded();
            bool hasSkin = _skinMaterial != null;

            _remoteCapsule = new GameObject($"RemotePlayer_{_clientId}");

            // Couleurs de fallback (utilisées si skin.png non trouvé)
            float hue        = ((_clientId * 137UL) % 360UL) / 360f;
            Color shirtColor = Color.HSVToRGB(hue, 0.65f, 0.90f);
            Color pantsColor = Color.HSVToRGB((hue + 0.55f) % 1f, 0.70f, 0.55f);
            Color skinColor  = new Color(0.78f, 0.55f, 0.38f);
            Color eyeColor   = new Color(0.06f, 0.06f, 0.06f);

            // ── BodyPivot ──────────────────────────────────────────
            var bodyGO = new GameObject("BodyPivot");
            bodyGO.transform.SetParent(_remoteCapsule.transform, false);
            bodyGO.transform.localPosition = Vector3.zero;
            bodyGO.transform.localRotation = Quaternion.identity;
            _bodyPivot = bodyGO.transform;

            // Torse  (0.50 × 0.75 × 0.25 u)  centre y = 1.125
            if (hasSkin)
                MakeSkinBox("Torso", bodyGO.transform,
                    new Vector3(0f, 1.125f, 0f), new Vector3(0.50f, 0.75f, 0.25f),
                    SkinUV(20,20,8,12), SkinUV(32,20,8,12),
                    SkinUV(28,20,4,12), SkinUV(16,20,4,12),
                    SkinUV(20,16,8, 4), SkinUV(28,16,8, 4));
            else
                MakeBlock("Torso", bodyGO.transform,
                    new Vector3(0f, 1.125f, 0f), new Vector3(0.50f, 0.75f, 0.25f), shirtColor);

            // Bras gauche — pivot à l'épaule, bras pend vers -Y local
            var armLGO = new GameObject("ArmLPivot");
            armLGO.transform.SetParent(bodyGO.transform, false);
            armLGO.transform.localPosition = new Vector3(-0.375f, 1.5f, 0f);
            _armLPivot = armLGO.transform;
            if (hasSkin)
                // Ancien format 64×32 : pas de région bras gauche — miroir du bras droit
                // uvL/uvR inversés vs ArmR + mirrorU=true pour rendu symétrique correct
                MakeSkinBox("ArmL", armLGO.transform,
                    new Vector3(0f, -0.375f, 0f), new Vector3(0.25f, 0.75f, 0.25f),
                    SkinUV(44,20,4,12), SkinUV(52,20,4,12),
                    SkinUV(40,20,4,12), SkinUV(48,20,4,12),
                    SkinUV(44,16,4, 4), SkinUV(48,16,4, 4), true);
            else
                MakeBlock("ArmL", armLGO.transform,
                    new Vector3(0f, -0.375f, 0f), new Vector3(0.25f, 0.75f, 0.25f), shirtColor);

            // Bras droit
            var armRGO = new GameObject("ArmRPivot");
            armRGO.transform.SetParent(bodyGO.transform, false);
            armRGO.transform.localPosition = new Vector3(0.375f, 1.5f, 0f);
            _armRPivot = armRGO.transform;
            if (hasSkin)
                MakeSkinBox("ArmR", armRGO.transform,
                    new Vector3(0f, -0.375f, 0f), new Vector3(0.25f, 0.75f, 0.25f),
                    SkinUV(44,20,4,12), SkinUV(52,20,4,12),
                    SkinUV(48,20,4,12), SkinUV(40,20,4,12),
                    SkinUV(44,16,4, 4), SkinUV(48,16,4, 4));
            else
                MakeBlock("ArmR", armRGO.transform,
                    new Vector3(0f, -0.375f, 0f), new Vector3(0.25f, 0.75f, 0.25f), shirtColor);

            // Jambe gauche — pivot à la hanche, jambe pend vers -Y local
            var legLGO = new GameObject("LegLPivot");
            legLGO.transform.SetParent(bodyGO.transform, false);
            legLGO.transform.localPosition = new Vector3(-0.125f, 0.75f, 0f);
            _legLPivot = legLGO.transform;
            if (hasSkin)
                // Ancien format 64×32 : pas de région jambe gauche — miroir de la jambe droite
                MakeSkinBox("LegL", legLGO.transform,
                    new Vector3(0f, -0.375f, 0f), new Vector3(0.25f, 0.75f, 0.25f),
                    SkinUV( 4,20,4,12), SkinUV(12,20,4,12),
                    SkinUV( 0,20,4,12), SkinUV( 8,20,4,12),
                    SkinUV( 4,16,4, 4), SkinUV( 8,16,4, 4), true);
            else
                MakeBlock("LegL", legLGO.transform,
                    new Vector3(0f, -0.375f, 0f), new Vector3(0.25f, 0.75f, 0.25f), pantsColor);

            // Jambe droite
            var legRGO = new GameObject("LegRPivot");
            legRGO.transform.SetParent(bodyGO.transform, false);
            legRGO.transform.localPosition = new Vector3(0.125f, 0.75f, 0f);
            _legRPivot = legRGO.transform;
            if (hasSkin)
                MakeSkinBox("LegR", legRGO.transform,
                    new Vector3(0f, -0.375f, 0f), new Vector3(0.25f, 0.75f, 0.25f),
                    SkinUV( 4,20,4,12), SkinUV(12,20,4,12),
                    SkinUV( 8,20,4,12), SkinUV( 0,20,4,12),
                    SkinUV( 4,16,4, 4), SkinUV( 8,16,4, 4));
            else
                MakeBlock("LegR", legRGO.transform,
                    new Vector3(0f, -0.375f, 0f), new Vector3(0.25f, 0.75f, 0.25f), pantsColor);

            // ── HeadPivot ──────────────────────────────────────────
            var headGO = new GameObject("HeadPivot");
            headGO.transform.SetParent(_remoteCapsule.transform, false);
            headGO.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            headGO.transform.localRotation = Quaternion.identity;
            _headPivot = headGO.transform;

            if (hasSkin)
            {
                // La texture skin contient les yeux — pas de cubes séparés
                MakeSkinBox("Head", headGO.transform,
                    new Vector3(0f, 0.25f, 0f), new Vector3(0.50f, 0.50f, 0.50f),
                    SkinUV( 8, 8,8,8), SkinUV(24, 8,8,8),
                    SkinUV(16, 8,8,8), SkinUV( 0, 8,8,8),
                    SkinUV( 8, 0,8,8), SkinUV(16, 0,8,8));
            }
            else
            {
                MakeBlock("Head", headGO.transform,
                    new Vector3(0f, 0.25f, 0f), new Vector3(0.50f, 0.50f, 0.50f), skinColor);
                MakeBlock("EyeL", headGO.transform,
                    new Vector3(-0.10f, 0.30f, 0.251f), new Vector3(0.10f, 0.08f, 0.01f), eyeColor);
                MakeBlock("EyeR", headGO.transform,
                    new Vector3( 0.10f, 0.30f, 0.251f), new Vector3(0.10f, 0.08f, 0.01f), eyeColor);
            }

            // ── Nametag world-space ────────────────────────────────
            BuildNameTag();
        }

        private static GameObject MakeBlock(string name, Transform parent, Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = localScale;
            Object.Destroy(go.GetComponent<Collider>());
            ApplyColor(go, color);
            return go;
        }

        private void BuildNameTag()
        {
            var tagGO = new GameObject("NameTag");
            tagGO.transform.SetParent(_remoteCapsule.transform, false);
            tagGO.transform.localPosition = new Vector3(0f, 2.25f, 0f);

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

        // ── Orientation du nametag vers la caméra ─────────────

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
            // Priorité au shader du projet (garanti présent + compilé en URP)
            var shader = Shader.Find("AstroVoxel/BlockUnlit")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Standard");
            if (shader == null) return;
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     color);
            rend.material = mat;
        }

        // Réinitialise les statics au démarrage du mode Play (évite l'état périmé
        // quand Domain Reload est désactivé dans les Project Settings).
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSkinStatics()
        {
            _skinTexture = null;
            _skinMaterial = null;
            _skinLoaded   = false;
        }

        // ── Skin Minecraft (Assets/textures/skin.png) ─────────────

        private static Texture2D _skinTexture;
        private static Material  _skinMaterial;
        private static bool      _skinLoaded;

        private static void EnsureSkinLoaded()
        {
            if (_skinLoaded) return;
            _skinLoaded = true;

            // En build : le fichier doit être dans Assets/Resources/skin.png
            _skinTexture = Resources.Load<Texture2D>("skin");

#if UNITY_EDITOR
            // En éditeur, fallback sur Assets/textures/skin.png
            if (_skinTexture == null)
                _skinTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/textures/skin.png");
#endif
            if (_skinTexture == null) return;

            _skinTexture.filterMode = FilterMode.Point; // pixel-perfect, sans lissage

            // Même shader que les blocs voxel du projet (garanti présent + compilé)
            var shader = Shader.Find("AstroVoxel/BlockUnlit")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Standard");
            if (shader == null) return;

            _skinMaterial = new Material(shader);
            _skinMaterial.SetTexture("_BaseMap", _skinTexture);
            if (_skinMaterial.HasProperty("_MainTex")) _skinMaterial.SetTexture("_MainTex", _skinTexture);
        }

        /// <summary>
        /// Convertit un rectangle en pixels (origine haut-gauche, système Minecraft)
        /// en Rect UV Unity (origine bas-gauche, Y inversé).
        /// Prend en compte les dimensions réelles de la texture (64×32 ancien format,
        /// 64×64 nouveau format).
        /// </summary>
        private static Rect SkinUV(float px, float py, float pw, float ph)
        {
            float sw = _skinTexture != null ? _skinTexture.width  : 64f;
            float sh = _skinTexture != null ? _skinTexture.height : 32f;
            return new Rect(px / sw, 1f - (py + ph) / sh, pw / sw, ph / sh);
        }

        /// <summary>
        /// Crée un mesh de boîte avec UV Minecraft par face.
        /// Ordre des faces : Front(+Z), Back(-Z), Left(-X), Right(+X), Top(+Y), Bottom(-Y).
        /// mirrorU=true : inverse U sur toutes les faces (utilisé pour les membres gauches
        /// dans les skins ancien format 64×32 qui n’ont pas de région gauche propre).
        /// </summary>
        private static Mesh MakeSkinBoxMesh(Vector3 size,
            Rect uvF, Rect uvBk, Rect uvL, Rect uvR, Rect uvT, Rect uvBot,
            bool mirrorU = false)
        {
            float hx = size.x * 0.5f, hy = size.y * 0.5f, hz = size.z * 0.5f;

            var verts = new Vector3[24];
            // Front (+Z)
            verts[0]  = new(-hx,-hy, hz); verts[1]  = new( hx,-hy, hz);
            verts[2]  = new( hx, hy, hz); verts[3]  = new(-hx, hy, hz);
            // Back (-Z)
            verts[4]  = new( hx,-hy,-hz); verts[5]  = new(-hx,-hy,-hz);
            verts[6]  = new(-hx, hy,-hz); verts[7]  = new( hx, hy,-hz);
            // Left (-X)
            verts[8]  = new(-hx,-hy,-hz); verts[9]  = new(-hx,-hy, hz);
            verts[10] = new(-hx, hy, hz); verts[11] = new(-hx, hy,-hz);
            // Right (+X)
            verts[12] = new( hx,-hy, hz); verts[13] = new( hx,-hy,-hz);
            verts[14] = new( hx, hy,-hz); verts[15] = new( hx, hy, hz);
            // Top (+Y)
            verts[16] = new(-hx, hy, hz); verts[17] = new( hx, hy, hz);
            verts[18] = new( hx, hy,-hz); verts[19] = new(-hx, hy,-hz);
            // Bottom (-Y)
            verts[20] = new(-hx,-hy,-hz); verts[21] = new( hx,-hy,-hz);
            verts[22] = new( hx,-hy, hz); verts[23] = new(-hx,-hy, hz);

            Rect[] rects = { uvF, uvBk, uvL, uvR, uvT, uvBot };
            var uvs = new Vector2[24];
            for (int f = 0; f < 6; f++)
            {
                Rect rc = rects[f]; int b = f * 4;
                // mirrorU inverse le sens U pour créer le miroir horizontal
                float uA = mirrorU ? rc.xMax : rc.xMin;
                float uB = mirrorU ? rc.xMin : rc.xMax;
                uvs[b]   = new(uA, rc.yMin);
                uvs[b+1] = new(uB, rc.yMin);
                uvs[b+2] = new(uB, rc.yMax);
                uvs[b+3] = new(uA, rc.yMax);
            }

            var tris = new int[36];
            for (int f = 0; f < 6; f++)
            {
                int b = f * 4, i = f * 6;
                tris[i]   = b;   tris[i+1] = b+2; tris[i+2] = b+1;
                tris[i+3] = b;   tris[i+4] = b+3; tris[i+5] = b+2;
            }

            var mesh = new Mesh { name = "SkinBox" };
            mesh.vertices  = verts;
            mesh.uv        = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static GameObject MakeSkinBox(string name, Transform parent,
            Vector3 localPos, Vector3 size,
            Rect uvF, Rect uvBk, Rect uvL, Rect uvR, Rect uvT, Rect uvBot,
            bool mirrorU = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.mesh     = MakeSkinBoxMesh(size, uvF, uvBk, uvL, uvR, uvT, uvBot, mirrorU);
            mr.material = _skinMaterial;
            return go;
        }
    }
}

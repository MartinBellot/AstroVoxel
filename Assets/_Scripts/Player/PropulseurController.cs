// ============================================================
//  PropulseurController.cs
//  Item "Propulseur" : clic droit sur un point → le joueur est
//  propulsé en direction de ce point.
//  Utilisable en mode Survie (item dans la hotbar) et en mode
//  Créatif (activé via l'inventaire créatif).
// ============================================================

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AstroVoxel.Player
{
    public sealed class PropulseurController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────
        [Header("Propulsion")]
        [Tooltip("Vitesse de lancement (m/s) vers la cible.")]
        [SerializeField] private float launchSpeed = 42f;
        [Tooltip("Portée maximale du raycast (blocs Unity).")]
        [SerializeField] private float maxRange    = 400f;
        [Tooltip("Délai entre deux utilisations (secondes).")]
        [SerializeField] private float cooldown    = 2.0f;

        [Header("Références")]
        [SerializeField] private Camera          playerCamera;
        [SerializeField] private Rigidbody       playerRigidbody;
        [SerializeField] private BlockInteraction blockInteract;

        // ── Singleton léger ───────────────────────────────────
        public static PropulseurController Instance { get; private set; }

        // ── État ──────────────────────────────────────────────
        private float _cooldownTimer;

        // ── API ───────────────────────────────────────────────

        /// <summary>
        /// True quand le joueur tient le Propulseur en main :
        /// - mode Survie  : slot hotbar actif = ItemType.Propulseur
        /// - mode Créatif : flag activé depuis l'inventaire créatif
        /// </summary>
        public bool IsActive
        {
            get
            {
                if (blockInteract == null) return false;
                if (GameModeManager.IsSurvival)
                {
                    var hotbar = SurvivalInventoryData.Instance.Hotbar;
                    int idx    = blockInteract.SurvivalHotbarIndex;
                    return idx >= 0 && idx < hotbar.Length
                        && hotbar[idx].itemType == ItemType.Propulseur;
                }
                return blockInteract.CreativePropulseurActive;
            }
        }

        /// <summary>Cooldown restant (0 = prêt).</summary>
        public float CooldownRemaining => Mathf.Max(0f, _cooldownTimer);

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>Appelé depuis GameBootstrap après création du joueur.</summary>
        public void Init(Camera cam, Rigidbody rb, BlockInteraction bi)
        {
            playerCamera    = cam;
            playerRigidbody = rb;
            blockInteract   = bi;
        }

        private void Update()
        {
            if (_cooldownTimer > 0f)
                _cooldownTimer -= Time.deltaTime;

            if (!IsActive) return;

            // Ignore pendant que les UIs sont ouvertes
            if (CreativeInventory.IsOpen || SurvivalInventory.IsOpen || GameConsole.IsOpen)
                return;

            if (_cooldownTimer > 0f) return;

            if (GetMouseDown(1))
                TryPropulse();
        }

        // ── Propulsion ────────────────────────────────────────

        private void TryPropulse()
        {
            if (playerCamera == null || playerRigidbody == null) return;

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            if (!UnityEngine.Physics.Raycast(
                    ray, out RaycastHit hit, maxRange,
                    ~LayerMask.GetMask("Player"),
                    QueryTriggerInteraction.Ignore))
                return;

            // Direction vers la cible (depuis le centre du joueur)
            Vector3 origin    = playerRigidbody.position;
            Vector3 direction = (hit.point - origin).normalized;

            // Annule la vélocité courante puis lance vers la cible
            playerRigidbody.linearVelocity = direction * launchSpeed;

            _cooldownTimer = cooldown;

            SpawnTargetMarker(hit.point);
        }

        // ── Marqueur visuel temporaire ─────────────────────────

        private static void SpawnTargetMarker(Vector3 pos)
        {
            // Petite sphère orange qui disparaît en 1,2 s
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.position   = pos;
            go.transform.localScale = Vector3.one * 0.25f;
            go.name = "PropulseurTarget";

            // Retire le collider pour ne pas interférer avec le gameplay
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Matériau orange vif (Sprites/Default est toujours dispo)
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var mat = new Material(Shader.Find("Sprites/Default"));
                if (mat != null)
                    mat.color = new Color(1f, 0.50f, 0f, 0.92f);
                mr.material = mat;
            }

            Destroy(go, 1.2f);
        }

        // ── Input ─────────────────────────────────────────────

        private static bool GetMouseDown(int btn)
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            return btn == 0 ? m.leftButton.wasPressedThisFrame
                 : btn == 1 ? m.rightButton.wasPressedThisFrame
                 : false;
#else
            return Input.GetMouseButtonDown(btn);
#endif
        }
    }
}

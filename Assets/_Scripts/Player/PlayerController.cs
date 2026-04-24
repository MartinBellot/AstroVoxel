// ============================================================
//  PlayerController.cs
//  Déplacement ZQSD + WASD en surface planétaire.
//  Compatible Old Input Manager ET New Input System.
// ============================================================

using UnityEngine;
using AstroVoxel.Physics;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AstroVoxel.Player
{
    /// <summary>
    /// Contrôle le mouvement horizontal du joueur (ZQSD / WASD)
    /// dans le référentiel local aligné sur la surface planétaire.
    /// Le saut et la gravité sont délégués à <see cref="GravityBody"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(GravityBody))]
    public sealed class PlayerController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────
        [Header("Mouvement")]
        [SerializeField] private float moveSpeed   = 6f;
        [SerializeField] private float jumpForce   = 5f;
        [SerializeField] private float groundCheckRadius   = 0.35f;
        [SerializeField] private float groundCheckDistance = 0.25f;

        [Header("Références")]
        [Tooltip("Transform de la caméra (pour orienter le mouvement).")]
        [SerializeField] private Transform cameraTransform;

        // ── Composants ────────────────────────────────────────
        private Rigidbody _rb;

        // ── État ──────────────────────────────────────────────
        private bool _isGrounded;
        private bool _jumpQueued;           // saut mis en attente
        private float _coyoteTimer;         // tolérance de saut après bord
        private const float CoyoteTime = 0.12f;

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            CheckGround();

            // Coyote time : fenêtre de saut après avoir quitté un bord
            if (_isGrounded)
                _coyoteTimer = CoyoteTime;
            else
                _coyoteTimer -= Time.deltaTime;

            if (GetJumpDown() && _coyoteTimer > 0f)
                _jumpQueued = true;
        }

        private void FixedUpdate()
        {
            Move();

            if (_jumpQueued)
            {
                _jumpQueued  = false;
                _coyoteTimer = 0f;   // consomme la fenêtre coyote
                Jump();
            }
        }

        // ── Déplacement ───────────────────────────────────────

        private void Move()
        {
            float h = GetHorizontal();
            float v = GetVertical();

            if (h == 0f && v == 0f) return;

            // Référentiel de la caméra projeté sur la surface planétaire
            Vector3 planetUp = transform.up;  // GravityBody maintient transform.up

            Vector3 camForward = cameraTransform != null
                ? cameraTransform.forward
                : transform.forward;

            // Projette les directions caméra sur le plan tangentiel
            Vector3 forward = Vector3.ProjectOnPlane(camForward, planetUp).normalized;
            // Cross(planetUp, forward) = vecteur droit (vérifié : Cross(up,fwd)=right en Unity)
            Vector3 right   = Vector3.Cross(planetUp, forward).normalized;

            Vector3 moveDir = (forward * v + right * h).normalized;
            Vector3 targetVelocity = moveDir * moveSpeed;

            // Préserve la composante radiale (gravité)
            Vector3 radialVelocity = Vector3.Project(_rb.linearVelocity, planetUp);
            Vector3 desiredVelocity = targetVelocity + radialVelocity;

            // Applique la vitesse par impulsion (compatible avec ForceMode.Acceleration de la gravité)
            Vector3 velocityChange = desiredVelocity - _rb.linearVelocity;
            // Clamp pour ne pas contrecarrer brutalement la gravité radiale
            velocityChange = Vector3.ClampMagnitude(velocityChange, moveSpeed);

            _rb.AddForce(velocityChange, ForceMode.VelocityChange);
        }

        private void Jump()
        {
            // Annule la vitesse radiale descendante avant de sauter
            Vector3 planetUp = transform.up;
            Vector3 radialVel = Vector3.Project(_rb.linearVelocity, planetUp);
            if (Vector3.Dot(radialVel, planetUp) < 0)
                _rb.linearVelocity -= radialVel;

            _rb.AddForce(planetUp * jumpForce, ForceMode.VelocityChange);
        }

        // ── Détection du sol ──────────────────────────────────

        private void CheckGround()
        {
            // Origine au bas de la capsule (pivot + 0.5 unité vers le haut)
            Vector3 planetUp = transform.up;
            Vector3 origin   = transform.position + planetUp * groundCheckRadius;
            Vector3 down     = -planetUp;

            _isGrounded = UnityEngine.Physics.SphereCast(
                origin, groundCheckRadius, down,
                out _, groundCheckRadius + groundCheckDistance,
                ~LayerMask.GetMask("Player"),
                QueryTriggerInteraction.Ignore);
        }

        // ── Accesseurs ────────────────────────────────────────

        public bool IsGrounded => _isGrounded;

        /// <summary>Assigne la caméra après l'initialisation de la scène.</summary>
        public void SetCamera(Transform cam) => cameraTransform = cam;

        // ── Abstraction Input ─────────────────────────────────

        private static float GetHorizontal()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return 0f;
            float r = kb.dKey.isPressed ? 1f : 0f;
            float l = (kb.qKey.isPressed || kb.aKey.isPressed) ? 1f : 0f;
            return r - l;
#else
            return Input.GetAxisRaw("Horizontal");
#endif
        }

        private static float GetVertical()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return 0f;
            float fwd = (kb.zKey.isPressed || kb.wKey.isPressed) ? 1f : 0f;
            float bwd = kb.sKey.isPressed ? 1f : 0f;
            return fwd - bwd;
#else
            return Input.GetAxisRaw("Vertical");
#endif
        }

        private static bool GetJumpDown()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.spaceKey.wasPressedThisFrame;
#else
            return Input.GetButtonDown("Jump");
#endif
        }
    }
}

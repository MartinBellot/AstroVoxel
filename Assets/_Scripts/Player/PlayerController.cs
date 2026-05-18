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
        [SerializeField] private float moveSpeed    = 6f;
        [SerializeField] private float sprintSpeed  = 12f;
        [SerializeField] private float jumpForce    = 5f;
        [SerializeField] private float groundCheckRadius   = 0.35f;
        [SerializeField] private float groundCheckDistance = 0.25f;

        [Header("Auto-saut (step-up)")]
        [Tooltip("Active le saut automatique sur les blocs d'une hauteur.")]
        [SerializeField] private bool  autoJump          = true;
        [Tooltip("Distance avant de détection d'une marche (doit dépasser le rayon de la capsule).")]
        [SerializeField] private float stepDetectDist    = 0.65f;
        [Tooltip("Hauteur max considérée comme 'une marche' (1 bloc Unity = 1 unité).")]
        [SerializeField] private float stepHeight        = 1.05f;

        [Header("Références")]
        [Tooltip("Transform de la caméra (pour orienter le mouvement).")]
        [SerializeField] private Transform cameraTransform;

        // ── Composants ────────────────────────────────────────
        private Rigidbody _rb;

        // ── État ──────────────────────────────────────────────
        private bool  _isGrounded;
        private bool  _jumpQueued;           // saut mis en attente
        private bool  _isSprinting;
        private float _coyoteTimer;          // tolérance de saut après bord
        private const float CoyoteTime = 0.12f;

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            if (CreativeInventory.IsOpen) return;

            CheckGround();
            _isSprinting = GetSprint();

            // Coyote time : fenêtre de saut après avoir quitté un bord
            if (_isGrounded)
                _coyoteTimer = CoyoteTime;
            else
                _coyoteTimer -= Time.deltaTime;

            if (GetJumpDown() && _coyoteTimer > 0f)
                _jumpQueued = true;

            // Auto-saut : si on avance vers une marche d'un bloc, sauter automatiquement
            if (!_jumpQueued && autoJump)
                CheckAutoJump();
        }

        private void FixedUpdate()
        {
            if (CreativeInventory.IsOpen) return;

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
            float   speed   = _isSprinting ? sprintSpeed : moveSpeed;
            Vector3 targetVelocity = moveDir * speed;

            // Préserve la composante radiale (gravité)
            Vector3 radialVelocity = Vector3.Project(_rb.linearVelocity, planetUp);
            Vector3 desiredVelocity = targetVelocity + radialVelocity;

            // Applique la vitesse par impulsion (compatible avec ForceMode.Acceleration de la gravité)
            Vector3 velocityChange = desiredVelocity - _rb.linearVelocity;
            // Clamp pour ne pas contrecarrer brutalement la gravité radiale
            velocityChange = Vector3.ClampMagnitude(velocityChange, speed);

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

        // ── Auto-saut ─────────────────────────────────────────

        /// <summary>
        /// Déclenche un saut automatique lorsque le joueur avance
        /// droit sur un obstacle d'exactement 1 bloc de hauteur.
        /// Logique : obstacle présent au niveau des pieds (blockedLow)
        /// mais espace libre à <see cref="stepHeight"/> (clearHigh).
        /// </summary>
        private void CheckAutoJump()
        {
            if (!_isGrounded) return;

            float h = GetHorizontal();
            float v = GetVertical();
            if (h == 0f && v == 0f) return;

            Vector3 planetUp  = transform.up;
            Vector3 camFwd    = cameraTransform != null ? cameraTransform.forward : transform.forward;
            Vector3 forward   = Vector3.ProjectOnPlane(camFwd, planetUp).normalized;
            Vector3 right     = Vector3.Cross(planetUp, forward).normalized;
            Vector3 moveDir   = (forward * v + right * h).normalized;

            int mask = ~LayerMask.GetMask("Player");

            // Raycast bas : obstacle juste devant les pieds ?
            Vector3 lowOrigin = transform.position + planetUp * 0.1f;
            bool blockedLow   = UnityEngine.Physics.Raycast(
                lowOrigin, moveDir, stepDetectDist, mask,
                QueryTriggerInteraction.Ignore);

            if (!blockedLow) return;

            // Raycast haut : passage libre au-dessus de la marche ?
            Vector3 highOrigin = transform.position + planetUp * stepHeight;
            bool blockedHigh   = UnityEngine.Physics.Raycast(
                highOrigin, moveDir, stepDetectDist, mask,
                QueryTriggerInteraction.Ignore);

            if (!blockedHigh)
                _jumpQueued = true;
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

        private static bool GetSprint()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.leftShiftKey.isPressed;
#else
            return Input.GetKey(KeyCode.LeftShift);
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

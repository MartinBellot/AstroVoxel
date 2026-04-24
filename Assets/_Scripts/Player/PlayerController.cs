// ============================================================
//  PlayerController.cs
//  Déplacement ZQSD en surface d'une planète sphérique.
//  Travaille en tandem avec GravityBody (physique) et
//  PlayerCamera (orientation caméra).
// ============================================================

using UnityEngine;
using AstroVoxel.Physics;

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
        [SerializeField] private float groundCheckDistance = 0.15f;

        [Header("Références")]
        [Tooltip("Transform de la caméra (pour orienter le mouvement).")]
        [SerializeField] private Transform cameraTransform;

        // ── Composants ────────────────────────────────────────
        private Rigidbody _rb;

        // ── État ──────────────────────────────────────────────
        private bool _isGrounded;

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            CheckGround();

            if (_isGrounded && Input.GetButtonDown("Jump"))
                Jump();
        }

        private void FixedUpdate()
        {
            Move();
        }

        // ── Déplacement ───────────────────────────────────────

        private void Move()
        {
            // Axes d'entrée (ZQSD configuré dans Input Manager, ou WASD par défaut)
            float h = Input.GetAxis("Horizontal");  // Q/D ou A/D
            float v = Input.GetAxis("Vertical");    // Z/S ou W/S

            if (h == 0f && v == 0f) return;

            // Référentiel de la caméra projeté sur la surface planétaire
            Vector3 planetUp = transform.up;  // GravityBody maintient transform.up

            Vector3 camForward = cameraTransform != null
                ? cameraTransform.forward
                : transform.forward;

            // Projette les directions caméra sur le plan tangentiel
            Vector3 forward = Vector3.ProjectOnPlane(camForward, planetUp).normalized;
            Vector3 right   = Vector3.Cross(planetUp, forward).normalized * -1f;

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
            // Spherecast vers le bas planétaire depuis le centre du joueur
            Vector3 origin = transform.position;
            Vector3 down   = -transform.up;

            // Capsule height / 2 approximé à 1 unité
            _isGrounded = UnityEngine.Physics.SphereCast(
                origin, 0.4f, down,
                out _, 1f + groundCheckDistance,
                ~LayerMask.GetMask("Player"),
                QueryTriggerInteraction.Ignore);
        }

        // ── Accesseurs ────────────────────────────────────────

        public bool IsGrounded => _isGrounded;

        /// <summary>Assigne la caméra après l'initialisation de la scène.</summary>
        public void SetCamera(Transform cam) => cameraTransform = cam;
    }
}

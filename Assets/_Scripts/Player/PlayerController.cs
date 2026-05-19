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
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(GravityBody))]
    public sealed class PlayerController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────
        [Header("Mouvement")]
        [SerializeField] private float moveSpeed    = 5.5f;
        [SerializeField] private float sprintSpeed  = 9f;
        [SerializeField] private float crouchSpeed  = 2.2f;
        [Tooltip("Accélération au sol (m/s²). Plus c'est élevé, plus le contrôle est nerveux.")]
        [SerializeField] private float groundAccel  = 60f;
        [Tooltip("Accélération en l'air (m/s²). Plus faible pour limiter le contrôle aérien.")]
        [SerializeField] private float airAccel     = 12f;
        [Tooltip("Friction appliquée au sol quand aucune touche n'est pressée (s⁻¹).")]
        [SerializeField] private float groundFriction = 14f;

        [Header("Saut")]
        // Valeurs calées sur Minecraft : hauteur max 1.252 bloc, apex à ~0.43 s.
        // v0 = 2h/t ≈ 5.82 m/s, avec g = v0/t ≈ 13.54 m/s² (GravityAttractor).
        [SerializeField] private float jumpForce    = 5.82f;
        [SerializeField] private float groundCheckRadius   = 0.3f;
        [SerializeField] private float groundCheckDistance = 0.15f;

        [Header("Auto-saut (step-up)")]
        [Tooltip("Active le saut automatique sur les blocs d'une hauteur.")]
        [SerializeField] private bool  autoJump          = true;
        [Tooltip("Hauteur max considérée comme 'une marche' (1 bloc Unity = 1 unité).")]
        [SerializeField] private float stepHeight        = 1.05f;
        [Tooltip("Cooldown minimal entre deux auto-sauts (s).")]
        [SerializeField] private float autoJumpCooldown  = 0.35f;
        [Tooltip("Impulsion verticale de l'auto-saut. " +
                 "5.33 = √(2 · 13.54 · 1.05), juste de quoi franchir 1 bloc.")]
        [SerializeField] private float autoStepImpulse   = 5.33f;

        [Header("Accroupi (touche A)")]
        [Tooltip("Hauteur de la capsule en position accroupie (multiplicateur).")]
        [SerializeField, Range(0.4f, 0.95f)] private float crouchHeightFactor = 0.6f;
        [Tooltip("Vitesse de transition debout↔accroupi (1/s).")]
        [SerializeField] private float crouchLerpSpeed = 12f;

        [Header("Références")]
        [Tooltip("Transform de la caméra (pour orienter le mouvement).")]
        [SerializeField] private Transform cameraTransform;

        // ── Composants ────────────────────────────────────────
        private Rigidbody       _rb;
        private CapsuleCollider _capsule;

        // ── État ──────────────────────────────────────────────
        private bool  _isGrounded;
        private bool  _jumpQueued;
        private bool  _isSprinting;
        private bool  _isCrouching;
        private float _coyoteTimer;
        private float _autoJumpTimer;
        private float _baseCapsuleHeight;
        private Vector3 _baseCapsuleCenter;
        private Vector3 _baseCameraLocalPos;
        private bool    _hasBaseCameraPos;
        private const float CoyoteTime = 0.12f;

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            _rb      = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();
            _baseCapsuleHeight = _capsule.height;
            _baseCapsuleCenter = _capsule.center;
        }

        private void Update()
        {
            if (CreativeInventory.IsOpen || GameConsole.IsOpen) return;

            // Inputs événementiels : à lire dans Update.
            if (GetJumpDown() && _coyoteTimer > 0f && !_isCrouching)
                _jumpQueued = true;
        }

        private void FixedUpdate()
        {
            if (CreativeInventory.IsOpen || GameConsole.IsOpen)
            {
                // Stoppe le joueur si l'inventaire ou la console s'ouvre, conserve la gravité radiale.
                StopHorizontal();
                return;
            }

            float dt = Time.fixedDeltaTime;

            CheckGround();
            _isSprinting = GetSprint() && !_isCrouching;
            _isCrouching = GetCrouch() && _isGrounded;

            // Coyote time
            if (_isGrounded) _coyoteTimer = CoyoteTime;
            else             _coyoteTimer -= dt;

            if (_autoJumpTimer > 0f) _autoJumpTimer -= dt;

            UpdateCapsuleHeight(dt);
            Move(dt);

            if (autoJump && !_jumpQueued && !_isCrouching && _autoJumpTimer <= 0f)
                CheckAutoJump();

            if (_jumpQueued)
            {
                _jumpQueued  = false;
                _coyoteTimer = 0f;
                Jump();
            }
        }

        // ── Déplacement ───────────────────────────────────────

        private void Move(float dt)
        {
            float h = GetHorizontal();
            float v = GetVertical();

            Vector3 planetUp   = transform.up;
            Vector3 linearVel  = _rb.linearVelocity;
            Vector3 radialVel  = Vector3.Project(linearVel, planetUp);
            Vector3 tangentVel = linearVel - radialVel;

            Vector3 camForward = cameraTransform != null ? cameraTransform.forward : transform.forward;
            Vector3 forward    = Vector3.ProjectOnPlane(camForward, planetUp).normalized;
            Vector3 right      = Vector3.Cross(planetUp, forward).normalized;

            bool hasInput = (h != 0f || v != 0f);

            if (hasInput)
            {
                Vector3 moveDir = (forward * v + right * h).normalized;
                float   speed   = _isCrouching ? crouchSpeed
                                : _isSprinting ? sprintSpeed
                                : moveSpeed;
                Vector3 targetTangent = moveDir * speed;

                // Edge guard : en mode accroupi, empêche le joueur de tomber d'un bord.
                if (_isCrouching && _isGrounded)
                    targetTangent = ApplyEdgeGuard(targetTangent, planetUp);

                float accel = _isGrounded ? groundAccel : airAccel;
                Vector3 delta = targetTangent - tangentVel;
                float   maxDv = accel * dt;
                if (delta.sqrMagnitude > maxDv * maxDv)
                    delta = delta.normalized * maxDv;

                _rb.AddForce(delta, ForceMode.VelocityChange);
            }
            else if (_isGrounded)
            {
                // Pas d'input → friction exponentielle pour stopper net (anti-dérive).
                float factor = Mathf.Clamp01(groundFriction * dt);
                _rb.AddForce(-tangentVel * factor, ForceMode.VelocityChange);
            }
        }

        private void StopHorizontal()
        {
            Vector3 planetUp  = transform.up;
            Vector3 radialVel = Vector3.Project(_rb.linearVelocity, planetUp);
            _rb.linearVelocity = radialVel;
        }

        private void Jump()
        {
            Vector3 planetUp = transform.up;
            Vector3 radialVel = Vector3.Project(_rb.linearVelocity, planetUp);
            // Annule la vitesse radiale (ascendante ou descendante) avant de sauter
            // pour obtenir une hauteur de saut reproductible.
            _rb.linearVelocity -= radialVel;
            _rb.AddForce(planetUp * jumpForce, ForceMode.VelocityChange);
        }

        // ── Edge guard (anti-chute en accroupi) ───────────────

        /// <summary>
        /// Décompose la vélocité désirée en composantes forward/right et annule
        /// celles qui mèneraient le joueur au-dessus du vide.
        /// </summary>
        private Vector3 ApplyEdgeGuard(Vector3 desiredTangent, Vector3 planetUp)
        {
            if (desiredTangent.sqrMagnitude < 1e-6f) return desiredTangent;

            // Test à une distance correspondant au rayon de la capsule + marge.
            float probeDist  = _capsule.radius + 0.2f;
            // Profondeur faible : on n'accepte que le sol au MÊME niveau que les pieds
            // (toute chute > 0.4 unité = vide → on bloque le déplacement).
            const float probeDepth = 0.4f;
            int   mask       = ~LayerMask.GetMask("Player");

            // L'origine doit partir d'AU-DESSUS du sol pour que le raycast ne parte
            // pas déjà à l'intérieur du bloc sur lequel on se tient.
            Vector3 footOrigin = transform.position + planetUp * 0.1f;

            // Décompose la direction désirée en deux axes orthogonaux.
            Vector3 dir = desiredTangent.normalized;
            // axis1 = direction désirée, axis2 = perpendiculaire dans le plan tangent.
            Vector3 axis2 = Vector3.Cross(planetUp, dir).normalized;

            float a1 = Vector3.Dot(desiredTangent, dir);
            float a2 = Vector3.Dot(desiredTangent, axis2);

            // Probe le long de la direction principale.
            if (a1 > 0f)
            {
                Vector3 probe = footOrigin + dir * probeDist;
                if (!UnityEngine.Physics.Raycast(probe, -planetUp, probeDepth, mask,
                        QueryTriggerInteraction.Ignore))
                    a1 = 0f;
            }
            // Probe latéral (utile en mouvement diagonal).
            if (Mathf.Abs(a2) > 0.01f)
            {
                Vector3 lateralDir = a2 > 0f ? axis2 : -axis2;
                Vector3 probe      = footOrigin + lateralDir * probeDist;
                if (!UnityEngine.Physics.Raycast(probe, -planetUp, probeDepth, mask,
                        QueryTriggerInteraction.Ignore))
                    a2 = 0f;
            }

            return dir * a1 + axis2 * a2;
        }

        // ── Capsule (hauteur dynamique pour l'accroupi) ───────

        private void UpdateCapsuleHeight(float dt)
        {
            float targetHeight = _isCrouching
                ? _baseCapsuleHeight * crouchHeightFactor
                : _baseCapsuleHeight;
            Vector3 targetCenter = _baseCapsuleCenter * (targetHeight / _baseCapsuleHeight);

            float t = Mathf.Clamp01(crouchLerpSpeed * dt);
            _capsule.height = Mathf.Lerp(_capsule.height, targetHeight, t);
            _capsule.center = Vector3.Lerp(_capsule.center, targetCenter, t);

            // Caméra : suit la hauteur de la capsule (descend en accroupi).
            if (cameraTransform != null)
            {
                if (!_hasBaseCameraPos)
                {
                    _baseCameraLocalPos = cameraTransform.localPosition;
                    _hasBaseCameraPos   = true;
                }

                Vector3 targetCamPos = _baseCameraLocalPos;
                targetCamPos.y       = _baseCameraLocalPos.y * (targetHeight / _baseCapsuleHeight);
                cameraTransform.localPosition = Vector3.Lerp(
                    cameraTransform.localPosition, targetCamPos, t);
            }
        }

        // ── Détection du sol ──────────────────────────────────

        private void CheckGround()
        {
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
        /// Détecte un obstacle d'exactement 1 bloc devant le joueur et applique
        /// une impulsion verticale calibrée (pas de "bonds énormes").
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

            // Si le joueur est en pleine chute, pas d'auto-saut.
            Vector3 radialVel = Vector3.Project(_rb.linearVelocity, planetUp);
            if (Vector3.Dot(radialVel, planetUp) < -0.5f) return;

            int   mask      = ~LayerMask.GetMask("Player");
            float probeDist = _capsule.radius + 0.35f;

            // 1) Obstacle juste devant les pieds ? SphereCast plus tolérant qu'un Raycast
            //    (fonctionne même quand le joueur est plaqué contre le mur, vitesse nulle).
            Vector3 footOrigin = transform.position + planetUp * 0.15f;
            if (!UnityEngine.Physics.SphereCast(footOrigin, 0.12f, moveDir,
                    out _, probeDist, mask, QueryTriggerInteraction.Ignore))
                return;

            // 2) Passage libre à la hauteur stepHeight (sinon : marche trop haute).
            Vector3 highOrigin = transform.position + planetUp * (stepHeight + 0.15f);
            if (UnityEngine.Physics.SphereCast(highOrigin, 0.12f, moveDir,
                    out _, probeDist, mask, QueryTriggerInteraction.Ignore))
                return;

            // 3) Mesure la hauteur exacte du sommet de la marche, devant le joueur.
            //    Le ray part bien au-dessus et descend en se concentrant DEVANT les pieds.
            Vector3 topProbe = transform.position
                             + planetUp * (stepHeight + 0.5f)
                             + moveDir  * probeDist;
            if (!UnityEngine.Physics.Raycast(topProbe, -planetUp, out var topHit,
                    stepHeight + 0.5f, mask, QueryTriggerInteraction.Ignore))
                return;

            // Hauteur du sommet de la marche par rapport aux pieds (signée le long de planetUp).
            float stepRise = Vector3.Dot(topHit.point - transform.position, planetUp);

            // Critère strict : MONTÉE seulement.
            //  - stepRise > 0.15 : il faut une vraie marche (sinon = sol plat).
            //  - stepRise < stepHeight + 0.05 : on ne saute pas sur un mur trop haut.
            //  - stepRise > 0 garantit qu'on ignore les descentes (sommet sous les pieds).
            if (stepRise < 0.15f || stepRise > stepHeight + 0.05f) return;

            // Impulsion calibrée pour franchir 1 bloc.
            _rb.linearVelocity -= radialVel;
            _rb.AddForce(planetUp * autoStepImpulse, ForceMode.VelocityChange);

            _autoJumpTimer = autoJumpCooldown;
            _coyoteTimer   = 0f;
        }

        // ── Accesseurs ────────────────────────────────────────

        public bool IsGrounded => _isGrounded;
        public bool IsCrouching => _isCrouching;

        /// <summary>Assigne la caméra après l'initialisation de la scène.</summary>
        public void SetCamera(Transform cam) => cameraTransform = cam;

        // ── Abstraction Input ─────────────────────────────────

        private static float GetHorizontal()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return 0f;
            float r = kb.dKey.isPressed ? 1f : 0f;
            // 'Q' est réservé à l'accroupissement → on utilise 'A' pour la gauche.
            float l = kb.aKey.isPressed ? 1f : 0f;
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

        private static bool GetCrouch()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && (kb.qKey.isPressed || kb.leftCtrlKey.isPressed);
#else
            return Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl);
#endif
        }
    }
}

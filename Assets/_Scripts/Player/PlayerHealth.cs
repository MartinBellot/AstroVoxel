// ============================================================
//  PlayerHealth.cs
//  Système de santé du joueur en mode Survie.
//  10 cœurs = 20 HP (comme Minecraft).
//  • Régénération naturelle : +1 HP/4 s
//  • Dégâts de chute     : au-dessus de 3 blocs de hauteur
//  • Mort                : reset à 20 HP + message console
// ============================================================

using System;
using UnityEngine;

namespace AstroVoxel.Player
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PlayerHealth : MonoBehaviour
    {
        // ── Constantes ────────────────────────────────────────
        public const int MaxHealth = 20;          // 10 cœurs

        private const float RegenInterval    = 4f;   // secondes entre chaque +1 HP
        private const float FallDamageBase   = 3f;   // hauteur min pour dégâts (blocs)
        private const float FallDamageScale  = 1f;   // 1 HP par bloc en plus de 3

        // ── État ──────────────────────────────────────────────
        public int CurrentHealth { get; private set; } = MaxHealth;

        // ── Événements ────────────────────────────────────────
        public event Action<int, int> OnHealthChanged;  // (current, max)
        public event Action           OnDeath;

        // ── Singleton léger ───────────────────────────────────
        public static PlayerHealth Instance { get; private set; }

        // ── Internes ──────────────────────────────────────────
        private Rigidbody _rb;
        private float     _regenTimer;
        private float     _maxFallSpeed;      // pic de vitesse descendante cette chute
        private bool      _wasGroundedLast;   // pour détecter l'atterrissage
        private float     _groundCheckTimer;

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            Instance = this;
            _rb      = GetComponent<Rigidbody>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!GameModeManager.IsSurvival) return;

            // Régénération lente
            _regenTimer += Time.deltaTime;
            if (_regenTimer >= RegenInterval && CurrentHealth < MaxHealth)
            {
                _regenTimer = 0f;
                Heal(1);
            }
            else if (_regenTimer >= RegenInterval)
            {
                _regenTimer = 0f;
            }

            TrackFall();
        }

        // ── API publique ──────────────────────────────────────

        public void TakeDamage(int amount)
        {
            if (!GameModeManager.IsSurvival || amount <= 0) return;
            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
            if (CurrentHealth <= 0) Die();
        }

        public void Heal(int amount)
        {
            if (amount <= 0) return;
            int before = CurrentHealth;
            CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
            if (CurrentHealth != before)
                OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }

        public void ResetHealth()
        {
            CurrentHealth = MaxHealth;
            _maxFallSpeed = 0f;
            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }

        // ── Dégâts de chute ───────────────────────────────────

        private void TrackFall()
        {
            if (_rb == null) return;

            Vector3 up     = transform.up;
            float   radVel = Vector3.Dot(_rb.linearVelocity, -up); // positif = descente

            // Track la vitesse max en descente
            if (radVel > _maxFallSpeed) _maxFallSpeed = radVel;

            // Détection d'atterrissage : vitesse descendante qui redevient ~0
            bool isGrounded = IsGrounded();
            if (isGrounded && !_wasGroundedLast && _maxFallSpeed > 0.5f)
            {
                ApplyFallDamage(_maxFallSpeed);
                _maxFallSpeed = 0f;
            }
            if (isGrounded) _maxFallSpeed = 0f;
            _wasGroundedLast = isGrounded;
        }

        private bool IsGrounded()
        {
            // Sphère cast court vers le bas depuis les pieds
            var capsule = GetComponent<CapsuleCollider>();
            if (capsule == null) return false;

            Vector3 origin = transform.position + transform.up * 0.15f;
            float   radius = capsule.radius * 0.9f;
            return UnityEngine.Physics.SphereCast(
                origin, radius, -transform.up, out _, 0.3f,
                ~LayerMask.GetMask("Player"), QueryTriggerInteraction.Ignore);
        }

        private void ApplyFallDamage(float fallSpeed)
        {
            // Vitesse de chute en m/s → hauteur équivalente H = v²/(2g), g ≈ 13.5
            float height = (fallSpeed * fallSpeed) / (2f * 13.5f);
            float excess = height - FallDamageBase;
            if (excess <= 0f) return;
            int dmg = Mathf.CeilToInt(excess * FallDamageScale);
            TakeDamage(dmg);
        }

        // ── Mort ──────────────────────────────────────────────

        private void Die()
        {
            OnDeath?.Invoke();
            // Reset automatique après 0.5 s (respawn simple)
            Invoke(nameof(Respawn), 0.5f);
        }

        private void Respawn()
        {
            ResetHealth();
            // Vide l'inventaire survie (perte à la mort)
            SurvivalInventoryData.Instance?.Reset();
        }
    }
}

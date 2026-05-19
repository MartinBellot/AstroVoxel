// ============================================================
//  SpaceShipCamera.cs
//  Caméra 3e personne qui suit le vaisseau depuis l'arrière.
//  Fonctionne en tout référentiel (surface planétaire et espace)
//  car elle utilise les axes locaux du vaisseau comme référence.
//
//  Paramètres Inspector :
//    followDistance  — recul depuis le vaisseau
//    followHeight    — décalage vertical local
//    followSmooth    — inertie de la position (0 = instantané)
//    lookSmooth      — inertie de l'orientation
// ============================================================

using UnityEngine;

namespace AstroVoxel.Vehicle
{
    /// <summary>
    /// Caméra Spring-Arm 3e personne pour le vaisseau.
    /// Se positionne derrière le vaisseau sur son axe -forward
    /// et regarde vers lui (légèrement devant son pivot).
    /// </summary>
    public sealed class SpaceShipCamera : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────

        [Header("Cible")]
        [Tooltip("Transform du vaisseau à suivre.")]
        [SerializeField] public Transform target;

        [Header("Bras ressort")]
        [Tooltip("Recul depuis le vaisseau (axe -forward local).")]
        [SerializeField] private float followDistance = 25f;

        [Tooltip("Décalage vertical local (axe +up).")]
        [SerializeField] private float followHeight   = 7f;

        [Header("Lissage")]
        [Tooltip("Vitesse de suivi de la position (0 = collé, ∞ = instantané).")]
        [SerializeField] private float followSmooth   = 8f;

        [Tooltip("Vitesse d'orientation vers le vaisseau.")]
        [SerializeField] private float lookSmooth     = 12f;

        [Header("Cible visuelle")]
        [Tooltip("Offset avant pour que la caméra regarde légèrement devant le vaisseau.")]
        [SerializeField] private float lookAheadOffset = 8f;

        [Header("Effet de vitesse (Boost)")]
        [Tooltip("FOV normal hors boost.")]
        [SerializeField] private float normalFOV           = 60f;

        [Tooltip("FOV cible lors du boost — sensation d'accélération franche.")]
        [SerializeField] private float boostFOV            = 85f;

        [Tooltip("Vitesse de transition FOV (Lerp).")]
        [SerializeField] private float fovSmoothSpeed      = 5f;

        [Tooltip("Multiplicateur de recul en boost (1 = normal, 1.5 = 50% plus loin).")]
        [SerializeField] private float boostDistanceMult   = 1.5f;

        [Tooltip("Hauteur supplémentaire de la caméra en boost (unités locales).")]
        [SerializeField] private float boostHeightAdd      = 2.5f;

        [Tooltip("Amplitude des vibrations caméra en boost (0 = aucune).")]
        [SerializeField] private float boostShakeIntensity = 0.08f;

        [Tooltip("Fréquence des vibrations caméra.")]
        [SerializeField] private float boostShakeFrequency = 25f;

        // ── Privé ─────────────────────────────────────────────
        private Camera              _cam;
        private SpaceShipController _shipController;
        private float               _shakeTime;

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam != null) _cam.fieldOfView = normalFOV;
        }

        private void Start()
        {
            if (target != null)
                _shipController = target.GetComponent<SpaceShipController>();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            bool isBoosting = _shipController != null && _shipController.IsBoostActive;

            // ── FOV : s'élargit pendant le boost ───────────────────────
            if (_cam != null)
            {
                float targetFOV  = isBoosting ? boostFOV : normalFOV;
                _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, targetFOV,
                                              Time.deltaTime * fovSmoothSpeed);
            }

            // ── Position : recul dramatique et montée en boost ────────
            float curDist   = isBoosting ? followDistance * boostDistanceMult : followDistance;
            float curHeight = isBoosting ? followHeight   + boostHeightAdd    : followHeight;

            Vector3 desiredPos = target.position
                                 - target.forward * curDist
                                 + target.up      * curHeight;

            // ── Vibration caméra en boost (turbulence / g-force) ───
            if (isBoosting && boostShakeIntensity > 0f)
            {
                _shakeTime += Time.deltaTime * boostShakeFrequency;
                float shakeX = Mathf.Sin(_shakeTime * 1.7f) * boostShakeIntensity;
                float shakeY = Mathf.Sin(_shakeTime * 2.3f) * boostShakeIntensity;
                desiredPos  += target.right * shakeX + target.up * shakeY;
            }
            else
            {
                _shakeTime = 0f;
            }

            transform.position = Vector3.Lerp(
                transform.position,
                desiredPos,
                Time.deltaTime * followSmooth
            );

            // ── Orientation : regarde vers le vaisseau (légèrement devant) ──
            Vector3 lookTarget = target.position + target.forward * lookAheadOffset;
            Vector3 lookDir    = lookTarget - transform.position;

            if (lookDir.sqrMagnitude > 0.001f)
            {
                Quaternion desiredRot = Quaternion.LookRotation(lookDir, target.up);
                transform.rotation   = Quaternion.Slerp(
                    transform.rotation,
                    desiredRot,
                    Time.deltaTime * lookSmooth
                );
            }
        }
    }
}

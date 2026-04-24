// ============================================================
//  PlayerCamera.cs
//  Mouse-look FPS aligné sur la verticale planétaire.
//  La caméra est un enfant du joueur ; cet axe X (pitch) est
//  géré ici, l'axe Y (yaw) est appliqué au corps du joueur.
// ============================================================

using UnityEngine;

namespace AstroVoxel.Player
{
    /// <summary>
    /// Contrôle la caméra FPS du joueur en tenant compte de la
    /// verticale planétaire : le yaw fait tourner le corps du joueur,
    /// le pitch incline uniquement la caméra.
    /// </summary>
    public sealed class PlayerCamera : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────
        [Header("Sensibilité")]
        [SerializeField] private float sensitivityX = 2f;
        [SerializeField] private float sensitivityY = 2f;

        [Header("Limites verticales (degrés)")]
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch =  80f;

        [Header("Références")]
        [Tooltip("Transform du corps du joueur (reçoit le yaw).")]
        [SerializeField] private Transform playerBody;

        // ── État ──────────────────────────────────────────────
        private float _pitch;   // angle vertical cumulé

        // ── Cycle de vie ──────────────────────────────────────

        private void Awake()
        {
            LockCursor(true);
        }

        private void Update()
        {
            // Dans l'éditeur Unity, le curseur ne se verrouille qu'après que
            // la Game View est focalisée. Un clic gauche force le re-verrouillage.
            if (!IsCursorLocked() && Input.GetMouseButtonDown(0))
                LockCursor(true);

            HandleMouseLook();

            // Toggle curseur avec Escape
            if (Input.GetKeyDown(KeyCode.Escape))
                LockCursor(false);
        }

        // ── Mouse look ────────────────────────────────────────

        private void HandleMouseLook()
        {
            if (!IsCursorLocked()) return;

            float mouseX = Input.GetAxisRaw("Mouse X") * sensitivityX;
            float mouseY = Input.GetAxisRaw("Mouse Y") * sensitivityY;

            // Pitch (axe X local de la caméra)
            _pitch -= mouseY;
            _pitch  = Mathf.Clamp(_pitch, minPitch, maxPitch);
            transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            // Yaw : fait pivoter le corps du joueur autour de son axe "up" planétaire
            if (playerBody != null)
                playerBody.Rotate(playerBody.up, mouseX, Space.World);
        }

        // ── Curseur ───────────────────────────────────────────

        private static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible   = !locked;
        }

        private static bool IsCursorLocked() => Cursor.lockState == CursorLockMode.Locked;

        // ── Accesseurs ────────────────────────────────────────

        /// <summary>Assigne le corps du joueur (appelé par GameBootstrap).</summary>
        public void SetPlayerBody(Transform body) => playerBody = body;
    }
}

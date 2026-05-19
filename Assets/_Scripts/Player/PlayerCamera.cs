// ============================================================
//  PlayerCamera.cs
//  Mouse-look FPS aligné sur la verticale planétaire.
//  Compatible Old Input Manager ET New Input System.
// ============================================================

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AstroVoxel.Player
{
    public sealed class PlayerCamera : MonoBehaviour
    {
        [Header("Sensibilité")]
        [SerializeField] private float sensitivityX = 0.15f;
        [SerializeField] private float sensitivityY = 0.15f;

        [Header("Limites verticales (degrés)")]
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch =  80f;

        [Header("Références")]
        [SerializeField] private Transform playerBody;

        private float _pitch;

        private void Awake()
        {
            LockCursor(true);
        }

        private void Update()
        {
            if (CreativeInventory.IsOpen || SurvivalInventory.IsOpen || GameConsole.IsOpen) return;

            if (!IsCursorLocked() && GetAnyMouseButtonDown())
                LockCursor(true);

            if (GetKeyDown_Escape())
                LockCursor(false);

            if (IsCursorLocked())
                HandleMouseLook();
        }

        private void HandleMouseLook()
        {
            float mouseX = GetMouseDeltaX() * sensitivityX;
            float mouseY = GetMouseDeltaY() * sensitivityY;

            _pitch -= mouseY;
            _pitch  = Mathf.Clamp(_pitch, minPitch, maxPitch);
            transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            if (playerBody != null && mouseX != 0f)
                playerBody.Rotate(playerBody.up, mouseX, UnityEngine.Space.World);
        }

        // ── Abstraction Input ─────────────────────────────────

        private static float GetMouseDeltaX()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            return m != null ? m.delta.x.ReadValue() : 0f;
#else
            return Input.GetAxisRaw("Mouse X");
#endif
        }

        private static float GetMouseDeltaY()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            return m != null ? m.delta.y.ReadValue() : 0f;
#else
            return Input.GetAxisRaw("Mouse Y");
#endif
        }

        private static bool GetAnyMouseButtonDown()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            return m != null && m.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private static bool GetKeyDown_Escape()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        // ── Curseur ───────────────────────────────────────────

        private static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible   = !locked;
        }

        private static bool IsCursorLocked() => Cursor.lockState == CursorLockMode.Locked;

        public void SetPlayerBody(Transform body) => playerBody = body;
    }
}

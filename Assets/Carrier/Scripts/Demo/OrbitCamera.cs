using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>Orbit camera: right mouse button drags to orbit, the wheel zooms, it follows the target.
    /// On a gamepad the right stick orbits and the triggers zoom.</summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0f, 40f, 0f);
        public float distance = 1100f;
        public float minDistance = 60f;
        public float maxDistance = 4000f;
        public float yaw = -140f;
        public float pitch = 18f;
        public float sensitivity = 0.2f;
        public float autoRotate = 0f;
        public float padOrbitSpeed = 110f;
        [Tooltip("LT/RT zoom the camera (off when the triggers fly the ship).")]
        public bool padTriggerZoom = true;

        Transform cachedTarget;
        CarrierController ship;

        void LateUpdate()
        {
            // no orbiting or zooming under the pause menu (it still follows its target)
            var mouse = GameMenu.IsOpen ? null : Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    yaw += d.x * sensitivity;
                    pitch = Mathf.Clamp(pitch - d.y * sensitivity, -85f, 85f);
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    distance = Mathf.Clamp(distance * (scroll > 0 ? 0.9f : 1.1f), minDistance, maxDistance);
            }
            var pad = GameMenu.IsOpen ? null : Gamepad.current;
            if (pad != null)
            {
                // right stick orbits (degrees per second), RT zooms in, LT zooms out
                float dt = Time.unscaledDeltaTime;
                Vector2 rs = pad.rightStick.ReadValue();
                yaw += rs.x * padOrbitSpeed * dt;
                pitch = Mathf.Clamp(pitch - rs.y * padOrbitSpeed * 0.6f * dt, -85f, 85f);
                float zoom = padTriggerZoom ? pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue() : 0f;
                if (Mathf.Abs(zoom) > 0.05f)
                    distance = Mathf.Clamp(distance * Mathf.Pow(0.3f, zoom * dt), minDistance, maxDistance);
            }
            yaw += autoRotate * Time.deltaTime;
            if (target == null) return;
            // (round the ship's own up: out from the planet it is by, so the horizon stays level)
            if (target != cachedTarget) { cachedTarget = target; ship = target.GetComponent<CarrierController>(); }
            Quaternion frame = ship != null ? ship.upFrame : Quaternion.identity;
            var rot = frame * Quaternion.Euler(pitch, yaw, 0f);
            Vector3 focus = target.position + frame * offset;
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, Quaternion.LookRotation(rot * Vector3.forward, frame * Vector3.up));
        }
    }
}

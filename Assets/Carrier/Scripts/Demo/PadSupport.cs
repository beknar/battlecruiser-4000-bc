using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Small helpers for gamepads (an Xbox 360 pad shows up as an XInput gamepad, or in a web
    /// browser as a standard gamepad; the page's xbox360.js sees to that): which device was used
    /// last (so the help box can show the right controls), short rumble pulses, and
    /// connect/disconnect messages.
    /// </summary>
    public static class PadSupport
    {
        /// <summary>True when the gamepad was used more recently than the keyboard or mouse.</summary>
        public static bool UsingPad;

        static float rumbleUntil;
        static bool rumbling, hooked;

        public static void Init()
        {
            if (hooked) return;
            hooked = true;
            InputSystem.onDeviceChange += OnDeviceChange;
            UsingPad = Gamepad.current != null;
            if (Gamepad.current != null && !CarrierDemoInput.ReelMode) CarrierDemoInput.Toast("Gamepad ready: press Back for its controls");
        }

        public static void Shutdown()
        {
            StopRumble();
            if (!hooked) return;
            hooked = false;
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is Gamepad)) return;
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
            {
                UsingPad = true;
                CarrierDemoInput.Toast("Gamepad connected");
            }
            else if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
            {
                StopRumble();
                if (Gamepad.all.Count == 0) UsingPad = false;
                CarrierDemoInput.Toast("Gamepad disconnected");
            }
        }

        /// <summary>Notice which device the player is using; call once per frame.</summary>
        public static void Track(Gamepad pad, Keyboard k, Mouse m)
        {
            if (pad != null && PadActive(pad)) UsingPad = true;
            else if ((k != null && k.anyKey.wasPressedThisFrame) ||
                     (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame)))
                UsingPad = false;

            if (rumbling && Time.unscaledTime >= rumbleUntil) StopRumble();
        }

        static bool PadActive(Gamepad p)
        {
            foreach (var c in p.allControls)
                if (c is UnityEngine.InputSystem.Controls.ButtonControl b && b.wasPressedThisFrame) return true;
            return p.leftStick.ReadValue().sqrMagnitude > 0.25f || p.rightStick.ReadValue().sqrMagnitude > 0.25f;
        }

        /// <summary>A short rumble on the current pad (low = heavy motor, high = light motor).</summary>
        public static void Rumble(float low, float high, float seconds)
        {
            var pad = Gamepad.current;
            if (pad == null) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            // the web player can't drive the motors itself: the page does it
            CarrierPadRumble(Mathf.Clamp01(low), Mathf.Clamp01(high), Mathf.RoundToInt(seconds * 1000f));
#else
            pad.SetMotorSpeeds(Mathf.Clamp01(low), Mathf.Clamp01(high));
#endif
            rumbling = true;
            rumbleUntil = Mathf.Max(rumbleUntil, Time.unscaledTime + seconds);
        }

        public static void StopRumble()
        {
            if (!rumbling) return;
            rumbling = false;
            rumbleUntil = 0f;
#if UNITY_WEBGL && !UNITY_EDITOR
            CarrierPadRumble(0f, 0f, 0);
#else
            InputSystem.ResetHaptics();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void CarrierPadRumble(float low, float high, int ms);   // Plugins/WebGL/CarrierPage.jslib
#endif
    }
}

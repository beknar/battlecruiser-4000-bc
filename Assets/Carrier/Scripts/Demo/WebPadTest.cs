using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Gamepad check for the web build: open the page with ?padtest. It reports, to the browser
    /// console, which controller the game sees and how (a gamepad, or only a generic joystick),
    /// then every button, trigger and stick movement as the game reads it, and whether R3 opens
    /// the system map and Start the pause menu. A test harness presses the buttons (a stand-in
    /// controller in the page) and compares the log with what it pressed.
    /// </summary>
    public class WebPadTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!CarrierDemoInput.OnWeb || !SystemTest.HasQueryKey(Application.absoluteURL, "padtest")) return;
            if (FindAnyObjectByType<WebPadTest>() != null) return;
            var go = new GameObject("WebPadTest");
            DontDestroyOnLoad(go);
            go.AddComponent<WebPadTest>();
        }

        static void Log(string s) => Debug.Log("[PadTest] " + s);

        IEnumerator Start()
        {
            yield return new WaitForSeconds(1f);
            foreach (var d in InputSystem.devices)
                if (d is Joystick && !(d is Gamepad)) Log($"joystick only (not a gamepad): {d.layout} '{d.description.product}'");
            Log("ready");
            float until = Time.realtimeSinceStartup + 30f;
            while (Gamepad.current == null && Time.realtimeSinceStartup < until) yield return null;
            var pad = Gamepad.current;
            if (pad == null)
            {
                foreach (var d in InputSystem.devices) Log($"device: {d.layout} '{d.description.product}' ({d.GetType().Name})");
                Log("FAIL: no gamepad");
                yield break;
            }
            Log($"gamepad: layout {pad.layout}, product '{pad.description.product}', {pad.GetType().Name}");
            // a rumble the stand-in controller can report
            PadSupport.Rumble(0.6f, 0.3f, 0.25f);
            Log("rumble asked: 0.60 0.30 0.25 s");
        }

        static readonly string[] Buttons =
        {
            "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder",
            "select", "start", "leftStickPress", "rightStickPress", "dpad/up", "dpad/down", "dpad/left", "dpad/right",
        };
        bool lt, rt;
        string ls = "", rs = "";
        bool mapWas, menuWas;

        void Update()
        {
            var pad = Gamepad.current;
            if (pad == null) return;
            foreach (var name in Buttons)
                if (pad[name] is ButtonControl b && b.wasPressedThisFrame)
                    Log("pressed " + name);
            float l = pad.leftTrigger.ReadValue(), r = pad.rightTrigger.ReadValue();
            if (!lt && l > 0.5f) Log($"left trigger {l:F2}");
            if (!rt && r > 0.5f) Log($"right trigger {r:F2}");
            if (lt && l < 0.1f) Log("left trigger released");
            if (rt && r < 0.1f) Log("right trigger released");
            lt = l > 0.5f ? true : l < 0.1f ? false : lt;
            rt = r > 0.5f ? true : r < 0.1f ? false : rt;
            ls = Stick("left stick", pad.leftStick.ReadValue(), ls);
            rs = Stick("right stick", pad.rightStick.ReadValue(), rs);
            // what the game did with them
            if (SystemMap.IsOpen != mapWas) Log(SystemMap.IsOpen ? "system map opened" : "system map closed");
            if (GameMenu.IsOpen != menuWas) Log(GameMenu.IsOpen ? "pause menu opened" : "pause menu closed");
            mapWas = SystemMap.IsOpen;
            menuWas = GameMenu.IsOpen;
        }

        static string Stick(string name, Vector2 v, string was)
        {
            string now = v.magnitude < 0.5f ? "" : Mathf.Abs(v.x) > Mathf.Abs(v.y) ? (v.x > 0 ? "right" : "left") : (v.y > 0 ? "up" : "down");
            if (now != was && now != "") Log($"{name} {now}");
            return now;
        }
    }
}

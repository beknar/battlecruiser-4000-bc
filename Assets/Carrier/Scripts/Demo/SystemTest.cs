using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Self-test for the star system and its mini-warp ("-systemtest &lt;folder&gt;"). Drives the
    /// system map with a virtual keyboard, mouse and gamepad, makes warps of different lengths and
    /// times them, takes screenshots of the map and the warp effects, and checks that the drive
    /// refuses to warp in combat. Writes systemtest.txt and the screenshots, then quits.
    /// </summary>
    public class SystemTest : MonoBehaviour
    {
        string dir, log = "";
        SystemMap map;
        WarpDrive drive;
        CarrierController cc;
        CarrierDemoInput demo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-systemtest");
            // in a web build: open the page with ?systemtest, and the results go to the browser console
            bool web = CarrierDemoInput.OnWeb && (Application.absoluteURL ?? "").Contains("systemtest");
            if (i < 0 && !web) return;
            var t = new GameObject("SystemTest").AddComponent<SystemTest>();
            t.web = web;
            t.dir = i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Application.persistentDataPath;
        }

        bool web;

        void Log(string s)
        {
            log += $"{Time.time,6:F1}s  {s}\n";
            if (web) { Debug.Log($"[SystemTest] {Time.time,6:F1}s  {s}"); return; }
            try { File.WriteAllText(Path.Combine(dir, "systemtest.txt"), log); } catch { }
        }

        void Shot(string name)
        {
            if (web) StartCoroutine(WebShot(name));
            else ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        }

        /// <summary>A web page can't save files: the screenshot goes to the browser console as a
        /// base64 JPEG, one line, for a test harness to pick up. WebGPU only reads the GPU back
        /// asynchronously, so it goes through a render texture and AsyncGPUReadback.</summary>
        IEnumerator WebShot(string name)
        {
            yield return new WaitForEndOfFrame();
            int w = Screen.width, h = Screen.height;
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
            var req = UnityEngine.Rendering.AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
            while (!req.done) yield return null;
            if (!req.hasError)
            {
                var px = req.GetData<byte>().ToArray();
                // the capture comes back bottom-up on some graphics APIs: the harness can flip it
                var jpg = ImageConversion.EncodeArrayToJPG(px, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h, 0, 80);
                // in pieces: browser logs cut long lines short
                string b64 = Convert.ToBase64String(jpg);
                const int piece = 4000;
                int count = (b64.Length + piece - 1) / piece;
                for (int i = 0; i < count; i++)
                    Debug.Log($"[SystemTestShot] {name} {i + 1}/{count} {b64.Substring(i * piece, Math.Min(piece, b64.Length - i * piece))}");
            }
            rt.Release();
            Destroy(rt);
        }

        WarpPoint Point(string name) => WarpPoint.All.FirstOrDefault(w => w.displayName == name);

        IEnumerator Start()
        {
            if (!web) Directory.CreateDirectory(dir);
            yield return null;
            demo = FindAnyObjectByType<CarrierDemoInput>();
            map = FindAnyObjectByType<SystemMap>();
            cc = demo != null ? demo.carrier : null;
            drive = cc != null ? cc.GetComponent<WarpDrive>() : null;
            if (map == null || drive == null) { Log("FAIL: no system map or warp drive in this scene"); Application.Quit(); yield break; }
            Log($"screen {Screen.width}x{Screen.height}, {WarpPoint.All.Count} warp points: " +
                string.Join(", ", WarpPoint.All.Select(w => $"{w.displayName} ({w.kind})")));
            Log($"kinds: {WarpPoint.All.Count(w => w.kind == WarpPoint.Kind.Star)} star, {WarpPoint.All.Count(w => w.kind == WarpPoint.Kind.Planet)} planets, " +
                $"{WarpPoint.All.Count(w => w.kind == WarpPoint.Kind.Moon)} moons, {WarpPoint.All.Count(w => w.kind == WarpPoint.Kind.AsteroidField)} asteroid fields");
            yield return new WaitForSeconds(2f);
            Shot("w0_start");
            Log($"start: near {WarpPoint.Nearest(cc.transform.position)?.displayName}, warp ready {drive.CanWarp(out string why0)} {why0}");

            // ---------------------------------------------------------------- keyboard
            var kb = InputSystem.AddDevice<Keyboard>("SystemTestKeyboard");
            yield return Key(kb, UnityEngine.InputSystem.Key.M);
            Log($"keyboard M: map open {SystemMap.IsOpen}, selected {map.Selected?.displayName}");
            yield return new WaitForSeconds(0.3f);
            Shot("w1_map_keyboard");
            var before = map.Selected;
            yield return Key(kb, UnityEngine.InputSystem.Key.RightArrow);
            Log($"keyboard right arrow: {before?.displayName} -> {map.Selected?.displayName}");
            yield return Key(kb, UnityEngine.InputSystem.Key.Tab);
            Log($"keyboard Tab: -> {map.Selected?.displayName}");
            yield return Key(kb, UnityEngine.InputSystem.Key.Escape);
            Log($"keyboard Esc: map open {SystemMap.IsOpen} (game still running: {Application.isPlaying})");
            yield return Key(kb, UnityEngine.InputSystem.Key.M);
            // choose the moon with the keyboard: Tab until it is selected
            for (int i = 0; i < 20 && map.Selected?.displayName != "Selene"; i++) yield return Key(kb, UnityEngine.InputSystem.Key.Tab);
            Log($"keyboard Tab to Selene: selected {map.Selected?.displayName}");
            yield return Warp(() => Key(kb, UnityEngine.InputSystem.Key.Enter), "keyboard Enter", "w2", true);

            // the long trip: to the star
            yield return Key(kb, UnityEngine.InputSystem.Key.M);
            for (int i = 0; i < 20 && map.Selected?.displayName != "Kestrel"; i++) yield return Key(kb, UnityEngine.InputSystem.Key.Tab);
            yield return Warp(() => Key(kb, UnityEngine.InputSystem.Key.Enter), "keyboard Enter", "w3", false);

            // ---------------------------------------------------------------- mouse
            var mouse = InputSystem.AddDevice<Mouse>("SystemTestMouse");
            yield return Key(kb, UnityEngine.InputSystem.Key.M);
            yield return null;
            var ochre = Point("Ochre");
            Vector2 gui = map.ScreenPosOf(ochre);
            yield return Click(mouse, gui);
            Log($"mouse click on Ochre at {gui:F0}: selected {map.Selected?.displayName}");
            Shot("w4_map_mouse");
            yield return new WaitForSeconds(0.2f);
            Vector2 button = map.WarpButtonRect.center;
            yield return Warp(() => Click(mouse, button), "mouse WARP button", "w5", false);

            // ---------------------------------------------------------------- gamepad
            var pad = InputSystem.AddDevice<Gamepad>("SystemTestPad");
            yield return Pad(pad, GamepadButton.RightStick);
            Log($"pad R3: map open {SystemMap.IsOpen}, selected {map.Selected?.displayName}");
            var s0 = map.Selected;
            yield return Pad(pad, GamepadButton.DpadRight);
            var s1 = map.Selected;
            yield return Pad(pad, GamepadButton.DpadUp);
            Log($"pad D-pad right/up: {s0?.displayName} -> {s1?.displayName} -> {map.Selected?.displayName}");
            yield return Pad(pad, GamepadButton.RightShoulder);
            Log($"pad RB: -> {map.Selected?.displayName}");
            yield return new WaitForSeconds(0.3f);
            Shot("w6_map_pad");
            for (int i = 0; i < 20 && map.Selected?.displayName != "Jovara Trojans"; i++) yield return Pad(pad, GamepadButton.RightShoulder);
            Log($"pad RB to the Trojans: selected {map.Selected?.displayName}");
            yield return Warp(() => Pad(pad, GamepadButton.South), "pad A", "w7", false);

            // ---------------------------------------------------------------- combat lock
            var group = GameObject.Find("Raiders")?.GetComponent<EncounterGroup>();
            var field = Point("Jovara Trojans");
            cc.throttle = 1f;
            float until = Time.time + 70f;
            while (Time.time < until && (group == null || !group.Active))
            {
                var v = ShipAutopilot.BowOn(cc, field.Position);
                demo.autoRudder = v.x;
                demo.autoPitch = v.y;
                yield return null;
            }
            Log($"flew into the Trojans: raiders attacking {group != null && group.Active} after {70f - (until - Time.time):F0} s");
            yield return new WaitForSeconds(4f);
            bool combat = drive.InCombat(out string cw);
            Log($"in combat: {combat} ({cw})");
            yield return Pad(pad, GamepadButton.RightStick);
            for (int i = 0; i < 20 && map.Selected?.displayName != "Thalassa"; i++) yield return Pad(pad, GamepadButton.RightShoulder);
            yield return new WaitForSeconds(0.2f);
            Shot("w8_combat_locked_map");
            yield return Pad(pad, GamepadButton.South);
            yield return null;
            Log($"pad A in combat: drive {drive.Current} (want Ready), message '{drive.LastMessage}', map still open {SystemMap.IsOpen}");
            Log(drive.Current == WarpDrive.State.Ready && combat ? "PASS: warp refused in combat" : "FAIL: warp was not refused in combat");
            yield return Pad(pad, GamepadButton.East);
            Shot("w9_combat");
            yield return new WaitForSeconds(0.5f);
            Log("done");
            Application.Quit();
        }

        /// <summary>Press a warp control, then follow the jump through, timing it and taking pictures.</summary>
        IEnumerator Warp(Func<IEnumerator> press, string how, string shot, bool pictures)
        {
            var target = map.Selected;
            float expectTrip = drive.TravelTimeTo(target), expect = drive.chargeTime + expectTrip;
            float km = drive.DistanceTo(target) / 1000f;
            float t0 = Time.time;
            yield return press();
            if (drive.Current != WarpDrive.State.Charging) { Log($"FAIL: {how} did not start a warp to {target?.displayName} ({drive.LastMessage})"); yield break; }
            Log($"{how}: warping to {target.displayName}, {km:F0} km, expected {expect:F1} s (trip {expectTrip:F1} s)");
            if (pictures) { yield return new WaitForSeconds(drive.chargeTime * 0.6f); Shot(shot + "_charging"); }
            while (drive.Current == WarpDrive.State.Charging) yield return null;
            Shot(shot + "_jump");
            if (pictures) { yield return new WaitForSeconds(Mathf.Min(1.2f, drive.TravelTime * 0.4f)); Shot(shot + "_tunnel"); }
            while (drive.Current == WarpDrive.State.Warping) yield return null;
            float took = Time.time - t0;
            yield return null;
            Shot(shot + "_arrive");
            while (drive.Current == WarpDrive.State.Arriving) yield return null;
            yield return new WaitForSeconds(0.5f);
            Shot(shot + "_there");
            float dist = Vector3.Distance(cc.transform.position, target.Position) / 1000f;
            Log($"  arrived after {took:F1} s (expected {expect:F1}), {dist:F1} km from {target.displayName}, nearest point {WarpPoint.Nearest(cc.transform.position)?.displayName}, " +
                $"speed {cc.Speed:F0} m/s, ship hull {cc.GetComponent<Health>().Current:F0}");
        }

        static IEnumerator Key(Keyboard kb, UnityEngine.InputSystem.Key key)
        {
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            yield return null;
        }

        static IEnumerator Pad(Gamepad pad, GamepadButton b)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(b));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
            yield return null;
        }

        /// <summary>Click the left mouse button at a GUI position (y down).</summary>
        static IEnumerator Click(Mouse mouse, Vector2 gui)
        {
            var pos = new Vector2(gui.x, Screen.height - gui.y);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pos });
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pos }.WithButton(MouseButton.Left));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pos });
            yield return null;
            yield return null;
        }
    }
}

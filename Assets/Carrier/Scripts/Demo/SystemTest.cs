using System;
using System.Collections;
using System.Collections.Generic;
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
    /// "-minetest" (or ?minetest) runs only the mining test: scan, mine an asteroid until the
    /// hold is full, jettison, mine a comet and a vein on a planet.
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
            bool web = CarrierDemoInput.OnWeb && HasQueryKey(Application.absoluteURL, "systemtest");
            if (i < 0 && !web) return;
            var t = new GameObject("SystemTest").AddComponent<SystemTest>();
            t.web = web;
            t.dir = i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Application.persistentDataPath;
            // -landtest (or ?landtest): only the landings, on every kind of world
            t.landOnly = Array.IndexOf(args, "-landtest") >= 0 || (web && HasQueryKey(Application.absoluteURL, "landtest"));
            // -landworld <name> (or ?landworld=<name>, spaces as _): after Thalassa, only that world
            int lw = Array.IndexOf(args, "-landworld");
            if (lw >= 0 && lw + 1 < args.Length) t.landWorld = args[lw + 1].Replace('_', ' ');
            if (web) t.landWorld = QueryValue(Application.absoluteURL, "landworld")?.Replace('_', ' ') ?? t.landWorld;
            // -minetest (or ?minetest): only the mining
            t.mineOnly = Array.IndexOf(args, "-minetest") >= 0 || (web && HasQueryKey(Application.absoluteURL, "minetest"));
            // -minesurface: only the vein on a planet (quick)
            t.mineSurfaceOnly = Array.IndexOf(args, "-minesurface") >= 0;
            if (t.mineSurfaceOnly) t.mineOnly = true;
        }

        bool web, landOnly, mineOnly, mineSurfaceOnly;
        string landWorld;

        static string QueryValue(string url, string key)
        {
            if (string.IsNullOrEmpty(url)) return null;
            int q = url.IndexOf('?');
            if (q < 0) return null;
            foreach (var part in url.Substring(q + 1).Split('&', '#'))
            {
                var kv = part.Split('=');
                if (kv[0] == key && kv.Length > 1) return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }

        /// <summary>Is `key` one of the URL's query options (?key, ?key=..., &key...)?</summary>
        public static bool HasQueryKey(string url, string key)
        {
            if (string.IsNullOrEmpty(url)) return false;
            int q = url.IndexOf('?');
            if (q < 0) return false;
            int hash = url.IndexOf('#', q);
            string query = hash < 0 ? url.Substring(q + 1) : url.Substring(q + 1, hash - q - 1);
            foreach (var part in query.Split('&'))
                if (part.Split('=')[0] == key) return true;
            return false;
        }

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
        /// base64 JPEG, in numbered pieces, for a test harness to pick up. WebGPU only reads the GPU
        /// back asynchronously (AsyncGPUReadback); elsewhere the frame is read straight back.</summary>
        IEnumerator WebShot(string name)
        {
            yield return new WaitForEndOfFrame();
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) { Debug.LogWarning($"[SystemTestShot] {name} failed: no capture"); yield break; }
                LogShot(name, tex.EncodeToJPG(80));
                Destroy(tex);
                yield break;
            }
            int w = Screen.width, h = Screen.height;
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
            var req = UnityEngine.Rendering.AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
            while (!req.done) yield return null;
            if (req.hasError) Debug.LogWarning($"[SystemTestShot] {name} failed: GPU readback error");
            else
            {
                // the capture comes back bottom-up on some graphics APIs (WebGPU): the harness flips it
                var px = req.GetData<byte>().ToArray();
                LogShot(name, ImageConversion.EncodeArrayToJPG(px, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h, 0, 80));
            }
            rt.Release();
            Destroy(rt);
        }

        static void LogShot(string name, byte[] jpg)
        {
            // in pieces: browser logs cut long lines short
            string b64 = Convert.ToBase64String(jpg);
            const int piece = 4000;
            int count = (b64.Length + piece - 1) / piece;
            for (int i = 0; i < count; i++)
                Debug.Log($"[SystemTestShot] {name} {i + 1}/{count} {b64.Substring(i * piece, Math.Min(piece, b64.Length - i * piece))}");
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
                $"{WarpPoint.All.Count(w => w.kind == WarpPoint.Kind.Moon)} moons, {WarpPoint.All.Count(w => w.kind == WarpPoint.Kind.AsteroidField)} asteroid fields, " +
                $"{WarpPoint.All.Count(w => w.kind == WarpPoint.Kind.Comet)} comets");
            yield return new WaitForSeconds(2f);
            Shot("w0_start");
            Log($"start: near {WarpPoint.Nearest(cc.transform.position)?.displayName}, warp ready {drive.CanWarp(out string why0)} {why0}");

            // ---------------------------------------------------------------- keyboard
            var kb = InputSystem.AddDevice<Keyboard>("SystemTestKeyboard");
            if (landOnly)
            {
                yield return LandingTest(kb, true);
                Log("done");
                if (!web) Application.Quit();
                yield break;
            }
            if (mineOnly)
            {
                yield return MiningTest(kb);
                Log("done");
                if (!web) Application.Quit();
                yield break;
            }
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

            // ---------------------------------------------------------------- pause menu
            yield return Key(kb, UnityEngine.InputSystem.Key.Escape);
            var menu = FindAnyObjectByType<GameMenu>();
            Log($"keyboard Esc: menu open {GameMenu.IsOpen}, time scale {Time.timeScale}");
            float paused0 = Time.time;
            yield return new WaitForSecondsRealtime(0.5f);
            Shot("m1_menu");
            bool frozen = GameMenu.IsOpen && Time.time - paused0 < 0.01f;
            Log($"  game time moved {Time.time - paused0:F2} s in 0.5 s of real time (paused: {frozen})");
            bool muted0 = SoundFX.Muted;
            yield return Key(kb, UnityEngine.InputSystem.Key.DownArrow);
            yield return Key(kb, UnityEngine.InputSystem.Key.DownArrow);
            string onSound = menu != null ? menu.SelectedLabel : "?";
            yield return Key(kb, UnityEngine.InputSystem.Key.Enter);
            bool toggled = SoundFX.Muted != muted0;
            yield return Key(kb, UnityEngine.InputSystem.Key.Enter);
            Log($"  keyboard down twice: '{onSound}'; Enter toggled the sound {toggled}; Enter again: muted {SoundFX.Muted} (was {muted0})");
            Vector2 resumeAt = menu != null ? menu.ItemScreenPos(0) : Vector2.zero;
            yield return Click(mouse, new Vector2(resumeAt.x, Screen.height - resumeAt.y));
            bool mouseResumed = !GameMenu.IsOpen && Time.timeScale > 0.5f;
            Log($"  mouse click on Resume: menu open {GameMenu.IsOpen}, time scale {Time.timeScale}");
            yield return Pad(pad, GamepadButton.Start);
            bool padOpened = GameMenu.IsOpen;
            yield return Pad(pad, GamepadButton.East);
            Log($"  pad Start: menu open {padOpened}; pad B: menu open {GameMenu.IsOpen}");
            bool menuOk = frozen && toggled && SoundFX.Muted == muted0 && mouseResumed && padOpened && !GameMenu.IsOpen;
            Log(menuOk ? "PASS: pause menu (pause, sound, resume by mouse and pad)" : "FAIL: pause menu");

            // restart from the menu with the pad: Start, down to Restart, A, then A again to confirm
            DontDestroyOnLoad(gameObject);
            var oldDemo = demo;
            yield return Pad(pad, GamepadButton.Start);
            yield return Pad(pad, GamepadButton.DpadDown);
            yield return Pad(pad, GamepadButton.South);
            Log($"  pad A on Restart: '{(menu != null ? menu.SelectedLabel : "?")}' (asks to confirm)");
            yield return Pad(pad, GamepadButton.South);
            float waitUntil = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < waitUntil)
            {
                demo = FindAnyObjectByType<CarrierDemoInput>();
                if (demo != null && demo != oldDemo) break;
                yield return null;
            }
            yield return new WaitForSeconds(1.5f);
            map = FindAnyObjectByType<SystemMap>();
            cc = demo != null ? demo.carrier : null;
            drive = cc != null ? cc.GetComponent<WarpDrive>() : null;
            if (cc == null || drive == null || map == null || demo == oldDemo) { Log("FAIL: the level did not come back after Restart"); Application.Quit(); yield break; }
            var raiders = GameObject.Find("Raiders")?.GetComponent<EncounterGroup>();
            string nearNow = WarpPoint.Nearest(cc.transform.position)?.displayName;
            bool restarted = Time.timeScale > 0.5f && !GameMenu.IsOpen && nearNow == "Thalassa" && raiders != null && !raiders.Active;
            Log($"restart: new level, near {nearNow}, hull {cc.GetComponent<Health>().Current:F0}, " +
                $"raiders attacking {raiders != null && raiders.Active}, warp ready {drive.CanWarp(out string rw)} {rw}");
            Log(restarted ? "PASS: restart" : "FAIL: restart");
            Shot("m2_restarted");

            // ---------------------------------------------------------------- landing (Thalassa)
            yield return LandingTest(kb, false);

            // ---------------------------------------------------------------- mining
            yield return MiningTest(kb);

            // ---------------------------------------------------------------- capital ships
            var dread = GameObject.Find("Pirate Dreadnought");
            var fleet = GameObject.Find("Dreadnought battle group")?.GetComponent<EncounterGroup>();
            var patrol = GameObject.Find("Corsair patrol")?.GetComponent<EncounterGroup>();
            var dreadTurrets = dread != null ? dread.GetComponentsInChildren<CarrierTurret>() : new CarrierTurret[0];
            var dreadHealth = dread != null ? dread.GetComponent<Health>() : null;
            Log($"capital ships: dreadnought {(dread != null ? "present" : "MISSING")} ({dreadTurrets.Length} turrets, hull {(dreadHealth != null ? dreadHealth.Current : 0f):F0}), " +
                $"battle group {(fleet != null ? fleet.pilots.Length : 0)} ships, corsair patrol {(patrol != null ? patrol.pilots.Length : 0)} ships, " +
                $"dreadnought visible before the fight {dread != null && !CombatPilot.IsHidden(dread.transform)}");
            yield return Key(kb, UnityEngine.InputSystem.Key.M);
            for (int i = 0; i < 20 && map.Selected?.displayName != "Glacis"; i++) yield return Key(kb, UnityEngine.InputSystem.Key.Tab);
            yield return Warp(() => Key(kb, UnityEngine.InputSystem.Key.Enter), "keyboard Enter", "c0", false);
            if (dread != null)
                Log($"  the dreadnought is {Vector3.Distance(cc.transform.position, dread.transform.position) / 1000f:F1} km away, " +
                    $"{Vector3.Angle(cc.transform.forward, dread.transform.position - cc.transform.position):F0} deg off the bow");
            float fightUntil = Time.time + 15f;
            while (Time.time < fightUntil && (fleet == null || !fleet.Active)) yield return null;
            Log($"  battle group attacking: {fleet != null && fleet.Active} after {15f - (fightUntil - Time.time):F1} s");
            // turn the bow on the dreadnought and hold there while the fleet closes, firing the bow gun
            if (dread != null) demo.SelectTarget(dread.transform);
            cc.throttle = 0f;
            var health = cc.GetComponent<Health>();
            var shield = cc.GetComponentInChildren<Shield>();
            float hull0 = health.Current, shield0 = shield != null ? shield.Strength : 0f, shieldLow = shield0;
            int charged = 0;
            fightUntil = Time.time + 40f;
            while (Time.time < fightUntil)
            {
                if (dread != null)
                {
                    var v = ShipAutopilot.BowOn(cc, dread.transform.position);
                    demo.autoRudder = v.x;
                    demo.autoPitch = v.y;
                }
                if (shield != null) shieldLow = Mathf.Min(shieldLow, shield.Strength);
                if (charged < 3 && dread != null && Vector3.Distance(cc.transform.position, dread.transform.position) < 6000f &&
                    Vector3.Angle(cc.transform.forward, dread.transform.position - cc.transform.position) < 6f && demo.MainGun != null &&
                    demo.MainGun.Current == MainBeam.State.Idle)
                {
                    yield return Key(kb, UnityEngine.InputSystem.Key.B);
                    charged++;
                }
                yield return null;
            }
            demo.autoRudder = null;
            demo.autoPitch = null;
            Shot("c1_battle");
            int turretShots = dreadTurrets.Sum(t => t.ShotsFired);
            var escorts = fleet != null ? fleet.pilots.Where(pl => pl != null && pl.gameObject != dread).ToArray() : new CombatPilot[0];
            int escortShots = escorts.Sum(e => e.GetComponentsInChildren<CarrierTurret>().Sum(t => t.ShotsFired));
            float dreadTook = dreadHealth != null ? dreadHealth.TotalDamage : 0f;
            Log($"  after 40 s: dreadnought turrets fired {turretShots}, escort turrets fired {escortShots}; frigate shield {shield0:F0} -> lowest {shieldLow:F0}, " +
                $"hull {hull0:F0} -> {health.Current:F0}; bow gun charged {charged}x; dreadnought took {dreadTook:F0} of {(dreadHealth != null ? dreadHealth.maxHealth : 0f):F0}");
            bool fought = fleet != null && fleet.Active && turretShots > 0 && escortShots > 0 && (shieldLow < shield0 || health.Current < hull0) && dreadTook > 0f;
            Log(fought ? "PASS: capital ship battle (both sides hit)" : "FAIL: capital ship battle");
            Log($"  warp during the fight: {(drive.CanWarp(out string fw) ? "allowed" : "locked: " + fw)}");
            if (web)
            {
                // Quit from the menu: the page shuts the game down (the harness looks for "[Page] quit")
                Log("quitting from the menu: Esc, up to Quit, Enter, Enter to confirm");
                yield return Key(kb, UnityEngine.InputSystem.Key.Escape);
                yield return Key(kb, UnityEngine.InputSystem.Key.UpArrow);
                yield return Key(kb, UnityEngine.InputSystem.Key.Enter);
                Log("done");
                yield return Key(kb, UnityEngine.InputSystem.Key.Enter);
                yield break;
            }
            Log("done");
            Application.Quit();
        }

        // ------------------------------------------------------------------ mining

        /// <summary>Open the map and warp to a place by name (Tab to it, Enter).</summary>
        IEnumerator WarpTo(Keyboard kb, string name, string shot)
        {
            yield return Key(kb, UnityEngine.InputSystem.Key.M);
            for (int i = 0; i < 30 && map.Selected?.displayName != name; i++) yield return Key(kb, UnityEngine.InputSystem.Key.Tab);
            if (map.Selected?.displayName != name) { Log($"FAIL: couldn't choose {name} on the map"); yield return Key(kb, UnityEngine.InputSystem.Key.Escape); yield break; }
            yield return Warp(() => Key(kb, UnityEngine.InputSystem.Key.Enter), "keyboard Enter", shot, false);
        }

        /// <summary>Fly toward a deposit until the laser's reach to it is under `reach` (in space),
        /// or until over a vein (on a surface: level, at the height the ship is at).</summary>
        IEnumerator Approach(MiningLaser laser, IMineralSource src, float reach, float seconds)
        {
            float until = Time.time + seconds, logAt = Time.time + 5f;
            bool vein = src is SurfaceVein;
            var landing = PlanetLanding.Instance;
            while (Time.time < until && src.Valid)
            {
                Vector3 p = src.Position;
                float r = laser.Reach(src);
                Vector3 flat = p - cc.transform.position;
                float across = new Vector2(flat.x, flat.z).magnitude;
                if (vein ? across < 300f : r < reach) break;
                // over a surface: up clear of the dunes and ridges first, then level
                bool low = vein && landing != null && landing.Altitude < 250f;
                demo.autoElevator = low ? 1f : vein ? 0f : (float?)null;
                var v = ShipAutopilot.BowOn(cc, vein ? p + Vector3.up * (cc.transform.position.y - p.y) : p);
                demo.autoRudder = v.x;
                demo.autoPitch = vein ? 0f : v.y;
                float to = vein ? across : r;
                cc.throttle = low ? 0.2f : to > 3500f ? 1f : to > 1600f ? 0.6f : 0.3f;
                if (Time.time >= logAt)
                {
                    logAt = Time.time + 5f;
                    Log($"    approach: {(vein ? across : r):F0} m to go, speed {cc.Speed:F0} m/s, heading {cc.Heading:F0} (bearing {Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg:F0})" +
                        (landing != null && PlanetLanding.OnSurface ? $", altitude {landing.Altitude:F0} m, landed {landing.Landed}" : ""));
                }
                yield return null;
            }
            cc.throttle = 0f;
            float stop = Time.time + 6f;
            while (cc.Speed > 3f && Time.time < stop) yield return null;
            demo.autoRudder = null; demo.autoPitch = null; demo.autoElevator = null;
        }

        /// <summary>A picture of the beam from the side: the camera swung round square to it, and
        /// back far enough to take in the ship and where the beam lands.</summary>
        IEnumerator BeamView(MiningLaser laser, string shot)
        {
            var oc = demo.orbitCamera;
            if (oc == null || !laser.Cutting) { Shot(shot); yield break; }
            float d0 = oc.distance, p0 = oc.pitch, y0 = oc.yaw;
            Vector3 beam = laser.HitPoint - cc.transform.position;
            float heading = Mathf.Atan2(beam.x, beam.z) * Mathf.Rad2Deg;
            oc.yaw = heading + 90f;
            oc.pitch = 12f;
            oc.distance = Mathf.Clamp(beam.magnitude * 1.3f, oc.minDistance, oc.maxDistance);
            yield return new WaitForSeconds(0.8f);
            Shot(shot);
            yield return new WaitForSeconds(0.2f);
            oc.distance = d0; oc.pitch = p0; oc.yaw = y0;
        }

        static string Mix(IMineralSource s) => s == null ? "(none)" : $"{s.Label}: {Minerals.Summary(s.Mix, 4, false)}, {s.Remaining:F0} of {s.Initial:F0} t";

        string Manifest(CargoHold hold) =>
            string.Join(", ", hold.Manifest().Take(8).Select(e => $"{Minerals.Name(e.mineral)} {e.tonnes:F1} t")) + $" (worth {hold.Value:N0} cr)";

        /// <summary>Scan (V) and wait for the sweep; returns the nearest find (not mined out).</summary>
        IEnumerator ScanAndLog(Keyboard kb, MineralSensor sensor, string where, Action<IMineralSource> nearest)
        {
            yield return Key(kb, UnityEngine.InputSystem.Key.V);
            bool started = sensor.Sweeping;
            float until = Time.time + sensor.sweepTime + 3f;
            while (sensor.Sweeping && Time.time < until) yield return null;
            yield return new WaitForSeconds(0.3f);
            Log($"  scan (V) at {where}: started {started}, {sensor.Finds.Count} found within {sensor.ScanRange / 1000f:F0} km (traces before: {sensor.Traces})");
            foreach (var f in sensor.Finds.Take(4))
            {
                Vector3 off = f.source.Position - cc.transform.position;
                Log($"    {f.distance / 1000f:F2} km ({off.x:F0}, {off.y:F0}, {off.z:F0}): {Mix(f.source)}");
            }
            nearest(sensor.Finds.Select(f => f.source).FirstOrDefault(s => s.Valid && !s.Depleted));
        }

        IEnumerator MiningTest(Keyboard kb)
        {
            var laser = demo.Laser;
            var sensor = demo.Sensor;
            var hold = demo.Cargo;
            if (laser == null || sensor == null || hold == null) { Log("FAIL: mining (no mining gear on the frigate)"); yield break; }
            Log($"mining: laser {laser.range / 1000f:F1} km, {laser.rate:F0} t/s; hold {hold.capacity:F0} t; sensor {sensor.spaceRange / 1000f:F0} km in space, " +
                $"{sensor.surfaceRange / 1000f:F0} km on a surface; {MineralDeposit.All.Count} bodies with minerals");
            Log("  kinds: " + string.Join(", ", MineralDeposit.All.GroupBy(d => d.Label).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} {g.Key}")));
            int kinds = MineralDeposit.All.SelectMany(d => d.Mix).Select(s => s.mineral).Distinct().Count();
            Log($"  {kinds} different minerals in the bodies in space, of {Minerals.Count} there are");

            bool scanOk = true, mineOk = true, fullOk = true, jetOk = true, listed = true, cometOk = true;
            IMineralSource src = null;
            if (mineSurfaceOnly) goto Surface;

            // ---------------------------------------------------- an asteroid field
            yield return WarpTo(kb, "Inner Belt", "n0");
            yield return ScanAndLog(kb, sensor, "the Inner Belt", s => src = s);
            Shot("n1_scan");
            scanOk = sensor.Finds.Count > 0 && sensor.Scans == 1;
            if (src == null) { Log("FAIL: mining (the scan found nothing)"); yield break; }
            yield return Approach(laser, src, 800f, 60f);
            Log($"  closed to {laser.Reach(src):F0} m of {src.Label}");
            float used0 = hold.Used;
            yield return Key(kb, UnityEngine.InputSystem.Key.Z);
            Log($"  Z: laser on {laser.On}, {laser.Status}");
            yield return new WaitForSeconds(3f);
            Shot("n2_mining");
            yield return BeamView(laser, "n2_mining_side");
            yield return new WaitForSeconds(2.4f);
            float got = hold.Used - used0;
            Log($"  after 6 s: {got:F1} t aboard, cutting {laser.Cutting} ({Mix(laser.Source)}); hold: {Manifest(hold)}");
            mineOk = got > laser.rate * 4f;

            // keep cutting till the hold is full, moving on to the next rock when one is mined out
            float until = Time.time + 150f;
            while (!hold.Full && Time.time < until)
            {
                if (!laser.On) yield return Key(kb, UnityEngine.InputSystem.Key.Z);
                if (laser.On && !laser.Cutting && laser.Source == null)
                {
                    // nothing left in reach: on to the nearest rock the scan found
                    var next = sensor.Finds.Select(f => f.source).Where(s => s.Valid && !s.Depleted)
                                     .OrderBy(s => laser.Reach(s)).FirstOrDefault();
                    if (next == null) { yield return ScanAndLog(kb, sensor, "the Inner Belt (again)", s => next = s); }
                    if (next != null) yield return Approach(laser, next, 800f, 40f);
                }
                yield return null;
            }
            yield return null;
            Log($"  hold full {hold.Full} ({hold.Used:F1} of {hold.capacity:F0} t), laser off {!laser.On}, rocks mined out {laser.DepositsMinedOut}; {Manifest(hold)}");
            fullOk = hold.Full && !laser.On;
            Shot("n3_full");

            // jettison the cheapest; the cargo list
            var cheap = hold.Cheapest();
            float before = hold.Used;
            yield return Key(kb, UnityEngine.InputSystem.Key.J);
            jetOk = cheap != null && hold[cheap.Value] < 0.01f && hold.Used < before;
            Log($"  J: jettisoned {(cheap != null ? Minerals.Name(cheap.Value) : "nothing")}, {before - hold.Used:F1} t; {hold.Used:F1} t aboard");
            yield return Key(kb, UnityEngine.InputSystem.Key.I);
            yield return new WaitForSeconds(0.3f);
            Shot("n4_cargo");
            listed = CarrierDemoInput.ManifestOpen;
            yield return Key(kb, UnityEngine.InputSystem.Key.I);
            // room for what comes next
            while (hold.Free < 200f && hold.Cheapest() is Mineral m) hold.Jettison(m);

            // ---------------------------------------------------- a comet
            yield return WarpTo(kb, "Comet Iskra", "n5");
            var comet = FindObjectsByType<Comet>(FindObjectsSortMode.None).FirstOrDefault(c => c.name == "Comet Iskra");
            bool tailOk = false;
            if (comet != null)
            {
                tailOk = comet.TailError < 1f && comet.ionTail != null && comet.ionTail.particleCount > 100 && comet.dustTail.particleCount > 100;
                Log($"  {comet.name}: tails {comet.TailError:F2} deg off straight away from the star; particles: coma {comet.coma.particleCount}, " +
                    $"ion tail {comet.ionTail.particleCount}, dust tail {comet.dustTail.particleCount}; {comet.GetComponentsInChildren<MineralDeposit>().Length - 1} lumps of debris");
            }
            else Log("  FAIL: no comet called Comet Iskra");
            Shot("n5_comet");
            var oc = demo.orbitCamera;
            if (oc != null && comet != null)
            {
                // look back past the ship along the tails
                Vector3 side = Vector3.Cross(comet.tails.forward, Vector3.up).normalized;
                Vector3 look = comet.transform.position + comet.tails.forward * 15000f - (cc.transform.position + side * 2000f);
                oc.yaw = Mathf.Atan2(-look.x, -look.z) * Mathf.Rad2Deg;
                oc.pitch = 8f;
                oc.distance = oc.maxDistance;
                yield return new WaitForSeconds(0.8f);
                Shot("n5_comet_tails");
                oc.distance = demo.ShipViewDistance;
                oc.pitch = 16f;
            }
            float water0 = hold[Mineral.Water];
            src = null;
            yield return ScanAndLog(kb, sensor, "Comet Iskra", s => src = s);
            if (src != null)
            {
                yield return Approach(laser, src, 800f, 60f);
                if (!laser.On) yield return Key(kb, UnityEngine.InputSystem.Key.Z);
                yield return new WaitForSeconds(5f);
                Shot("n6_comet_mining");
                yield return BeamView(laser, "n6_comet_mining_side");
                Log($"  cutting {laser.Cutting} ({Mix(laser.Source)}); water +{hold[Mineral.Water] - water0:F1} t");
            }
            cometOk = tailOk && hold[Mineral.Water] - water0 > 5f;
            if (laser.On) yield return Key(kb, UnityEngine.InputSystem.Key.Z);

            // ---------------------------------------------------- a vein in a planet
            Surface:
            var landing = FindAnyObjectByType<PlanetLanding>();
            bool surfOk = false;
            if (landing != null)
            {
                landing.LandOn(Point("Ochre"));
                yield return WaitLanding(landing, PlanetLanding.State.OnSurface, 15f);
                yield return new WaitForSeconds(2f);
                float used1 = hold.Used;
                src = null;
                yield return ScanAndLog(kb, sensor, "Ochre's surface", s => src = s);
                Shot("n7_surface_scan");
                var surf = landing.Surface;
                foreach (var f in sensor.Finds.Take(4))
                {
                    Vector3 vp = f.source.Position;
                    Log($"    vein ground: built {surf.Ready(vp)} (any detail {surf.Ready(vp, false)}), floor {surf.FloorAt(vp) - surf.Center.y:F0} m, " +
                        $"ground under the ship {surf.FloorAt(cc.transform.position) - surf.Center.y:F0} m, ship {cc.transform.position.y - surf.Center.y:F0} m, altitude {landing.Altitude:F0} m");
                }
                if (src != null)
                {
                    yield return Approach(laser, src, 0f, 60f);
                    Log($"  over the vein: {laser.Reach(src):F0} m from it, altitude {landing.Altitude:F0} m");
                    yield return Key(kb, UnityEngine.InputSystem.Key.Z);
                    Log($"  Z: laser on {laser.On}, {laser.Status}");
                    yield return new WaitForSeconds(5f);
                    Shot("n8_surface_mining");
                    yield return BeamView(laser, "n8_surface_mining_side");
                    Log($"  cutting {laser.Cutting} ({Mix(laser.Source)}); +{hold.Used - used1:F1} t; {Manifest(hold)}");
                    surfOk = hold.Used - used1 > laser.rate * 3f;
                    if (laser.On) yield return Key(kb, UnityEngine.InputSystem.Key.Z);
                }
                landing.TakeOff();
                yield return WaitLanding(landing, PlanetLanding.State.InSpace, 12f);
            }
            Log($"  totals: mined {hold.TotalMined:F0} t, jettisoned {hold.TotalJettisoned:F0} t, aboard {hold.Used:F0} t worth {hold.Value:N0} cr");
            bool ok = scanOk && mineOk && fullOk && jetOk && listed && cometOk && surfOk;
            Log(ok ? "PASS: mining (scan, mine an asteroid till the hold is full, jettison, a comet's tails and ice, a vein on Ochre)"
                   : $"FAIL: mining (scan {scanOk}, mine {mineOk}, hold full {fullOk}, jettison {jetOk}, cargo list {listed}, comet {cometOk}, surface {surfOk})");
        }

        static string Plain(string s) => s == null ? "(none)" : System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");

        IEnumerator WaitLanding(PlanetLanding landing, PlanetLanding.State want, float seconds)
        {
            float until = Time.time + seconds;
            while (landing.Current != want && Time.time < until) yield return null;
        }

        /// <summary>Land at Thalassa with the K key, fly to the pad and set down on it, test the
        /// edge of the area, take off with K. With `all`, then land on every other kind of world
        /// (from here, straight down) for pictures and checks, sinking to the ground on a moon and
        /// climbing out through the ceiling on Ochre.</summary>
        IEnumerator LandingTest(Keyboard kb, bool all)
        {
            var landing = FindAnyObjectByType<PlanetLanding>();
            if (landing == null) { Log("FAIL: landing (no landing controller in this level)"); yield break; }
            var health = cc.GetComponent<Health>();
            var shield = cc.GetComponentInChildren<Shield>();
            var spaceSky = RenderSettings.skybox;
            Vector3 orbit = cc.transform.position;
            Log($"landing: near {WarpPoint.Nearest(orbit)?.displayName}; HUD '{Plain(landing.HudText())}'");
            yield return Key(kb, UnityEngine.InputSystem.Key.K);
            yield return WaitLanding(landing, PlanetLanding.State.OnSurface, 15f);
            var site = landing.Site;
            if (site == null) { Log("FAIL: landing (K did not land)"); yield break; }
            yield return new WaitForSeconds(1.5f);
            Report(landing, "keyboard K");
            Shot("l1_thalassa");
            bool siteOk = site.kind == SurfaceKind.Ocean && site.terrain != null && Mathf.Approximately(site.terrain.terrainData.size.x, SurfaceGenerator.Size) &&
                          landing.Altitude > 50f;

            // fly to the pad and set down on it
            float hull0 = health.Current, shield0 = shield != null ? shield.Strength : 0f;
            float until = Time.time + 70f;
            bool descending = false;
            while (Time.time < until && !landing.Landed)
            {
                Vector3 to = site.pad - cc.transform.position;
                to.y = 0f;
                float d = to.magnitude;
                var v = ShipAutopilot.BowOn(cc, site.pad + Vector3.up * (cc.transform.position.y - site.pad.y));
                demo.autoRudder = d > 60f ? v.x : 0f;
                demo.autoPitch = 0f;
                cc.throttle = d > 220f ? 0.45f : d > 70f ? 0.12f : 0f;
                if (!descending && d < 110f && cc.Speed < 12f) descending = true;
                demo.autoElevator = descending ? -1f : 0f;
                yield return null;
            }
            demo.autoRudder = null; demo.autoPitch = null; demo.autoElevator = null;
            yield return new WaitForSeconds(1f);
            float lost = (hull0 - health.Current) + (shield0 - (shield != null ? shield.Strength : 0f));
            Log($"  touchdown: landed {landing.Landed}, on the pad {landing.OnPad}, altitude {landing.Altitude:F1} m, took {Mathf.Max(0f, lost):F0} damage, touchdowns {landing.Touchdowns}");
            Shot("l2_on_the_pad");
            yield return null;
            bool downOk = landing.Landed && (landing.OnPad || !site.hasOutpost) && lost < 1f;

            // the planet goes on: areas ahead are built, the ones left behind thrown away, and all
            // the way round (east, or over both poles) it is the same ground again
            bool edgeOk = false;
            yield return StreamingTest(landing, ok => edgeOk = ok);

            yield return Key(kb, UnityEngine.InputSystem.Key.K);
            yield return WaitLanding(landing, PlanetLanding.State.InSpace, 10f);
            yield return new WaitForSeconds(0.5f);
            bool backOk = landing.Current == PlanetLanding.State.InSpace && landing.Site == null && RenderSettings.skybox == spaceSky &&
                          !RenderSettings.fog && Vector3.Distance(cc.transform.position, orbit) < 3000f;
            Log($"  take off (K): back in space {landing.Current == PlanetLanding.State.InSpace}, {Vector3.Distance(cc.transform.position, orbit):F0} m from where it left, " +
                $"space sky back {RenderSettings.skybox == spaceSky}, fog {RenderSettings.fog}, warp {(drive.CanWarp(out string lw) ? "ready" : lw)}");
            Log(siteOk && downOk && edgeOk && backOk ? "PASS: landing (land, touch down on the pad, fly round the planet, take off)"
                : $"FAIL: landing (site {siteOk}, touchdown {downOk}, streaming {edgeOk}, back {backOk})");
            if (!all) yield break;

            int ok = 0, total = 0;
            foreach (var name in new[] { "Ochre", "Cinder", "Jovara", "Glacis", "Selene", "Steropes", "Inner Belt", "Jovara Trojans", "Thalassa" })
            {
                if (!string.IsNullOrEmpty(landWorld) && name != landWorld) continue;
                var wp = Point(name);
                if (wp == null) { Log($"FAIL: no place called {name}"); continue; }
                total++;
                landing.LandOn(wp);
                yield return WaitLanding(landing, PlanetLanding.State.OnSurface, 15f);
                site = landing.Site;
                if (site == null) { Log($"FAIL: landing on {name}"); continue; }
                yield return new WaitForSeconds(2f);
                Report(landing, "straight down");
                string file = name.Replace(' ', '_').ToLower();
                Shot($"l_{file}");
                var oc = demo.orbitCamera;
                float dist0 = oc != null ? oc.distance : 0f, pitch0 = oc != null ? oc.pitch : 0f;
                float yaw0 = oc != null ? oc.yaw : 0f;
                if (oc != null) { oc.distance = Mathf.Min(oc.maxDistance, 4500f); oc.pitch = 24f; }
                yield return new WaitForSeconds(0.6f);
                Shot($"l_{file}_wide");
                yield return new WaitForSeconds(0.3f);
                var planet = site.root.transform.Find("Parent planet");
                if (planet != null && oc != null)
                {
                    // look up past the ship at the planet this moon circles
                    Vector3 up = planet.position - cc.transform.position;
                    oc.yaw = Mathf.Atan2(up.x, up.z) * Mathf.Rad2Deg;
                    oc.pitch = -Mathf.Atan2(up.y, new Vector2(up.x, up.z).magnitude) * Mathf.Rad2Deg * 0.8f;
                    oc.distance = dist0;
                    yield return new WaitForSeconds(0.6f);
                    Log($"  the parent planet is {up.magnitude / 1000f:F0} km away, {Mathf.Atan2(up.y, new Vector2(up.x, up.z).magnitude) * Mathf.Rad2Deg:F0} deg up");
                    Shot($"l_{file}_sky");
                    yield return new WaitForSeconds(0.3f);
                }
                if (oc != null) { oc.distance = dist0; oc.pitch = pitch0; oc.yaw = yaw0; }
                bool good = site.terrain != null && landing.Altitude > 20f;
                if (wp.hostile)
                {
                    // a hostile world: its ground forces, and whether they spot the ship
                    float until2 = Time.time + 12f;
                    int alerted = 0;
                    while (Time.time < until2)
                    {
                        alerted = 0;
                        foreach (var g in landing.Surface.Garrisons) if (g.Alert) alerted++;
                        if (alerted > 0) break;
                        yield return null;
                    }
                    int units = 0, garrisons = 0;
                    var kinds = new Dictionary<string, int>();
                    foreach (var g in landing.Surface.Garrisons)
                    {
                        garrisons++;
                        foreach (var u in g.units)
                        {
                            if (u == null) continue;
                            units++;
                            kinds.TryGetValue(u.name, out int c);
                            kinds[u.name] = c + 1;
                        }
                    }
                    Log($"  ground forces: {units} in {garrisons} garrisons ({string.Join(", ", kinds.Select(k => $"{k.Value} {k.Key}"))}); on alert: {alerted}");
                    good &= units > 0;
                    // up close to the nearest battery (or whatever there is), and see it shoot
                    GroundUnit near = null;
                    float best = float.MaxValue;
                    foreach (var g in landing.Surface.Garrisons)
                        foreach (var u in g.units)
                        {
                            if (u == null || !u.Alive || u.GetComponent<CombatPilot>() != null) continue;
                            float d = (u.transform.position - cc.transform.position).sqrMagnitude * (u.name == "Ground Battery" ? 0.25f : 1f);
                            if (d < best) { best = d; near = u; }
                        }
                    if (near != null)
                    {
                        var turret = near.GetComponentInChildren<CarrierTurret>();
                        var weapons = near.GetComponent<EnemyWeapons>();
                        int shots0 = turret != null ? turret.ShotsFired : 0;
                        float hull1 = health.Current, shield1 = shield != null ? shield.Strength : 0f;
                        Vector3 at = near.transform.position + new Vector3(-650f, 0f, -450f);
                        at.y = Mathf.Max(landing.Surface.FloorAt(at), near.transform.position.y) + 220f;
                        cc.transform.position = at;
                        Vector3 look = near.transform.position - at;
                        cc.SetAttitude(Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, 0f);
                        cc.SetMotion(0f);
                        cc.throttle = 0f;
                        var oc2 = demo.orbitCamera;
                        if (oc2 != null) { oc2.yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg - 25f; oc2.pitch = 12f; }
                        near.garrison.Raise();
                        yield return new WaitForSeconds(7f);
                        Shot($"l_{name.Replace(' ', '_').ToLower()}_forces");
                        float took = (hull1 - health.Current) + (shield1 - (shield != null ? shield.Strength : 0f));
                        Log($"  close to a {near.name} ({Vector3.Distance(near.transform.position, cc.transform.position):F0} m): it fired {(turret != null ? turret.ShotsFired - shots0 : -1)} shots" +
                            $"{(weapons != null && weapons.missilePrefab != null ? " (missiles)" : "")}, the frigate took {Mathf.Max(0f, took):F0} damage");
                        good &= took > 0f;
                    }

                    // near the north pole, an area can be built twice (once each side of the pole):
                    // its forces must only be in one of them
                    var surf = landing.Surface;
                    var pw = landing.World;
                    surf.PlanetOf(cc.transform.position, out double pu, out _);
                    double uq = (pw.N / 4 + 0.5) * SurfaceWorld.T;   // where an area and its twin are side by side
                    uq += System.Math.Round((pu - uq) / pw.C) * pw.C;
                    double vq = pw.V - 1500.0;
                    Vector3 off = surf.WorldOf(uq, vq, 0f) - surf.Center;
                    surf.ShiftBy(new Vector3(-Mathf.Round(off.x / SurfaceWorld.T) * SurfaceWorld.T, 0f, -Mathf.Round(off.z / SurfaceWorld.T) * SurfaceWorld.T));
                    Vector3 pp = surf.WorldOf(uq, vq, 0f);
                    pp.y = surf.FloorAt(pp) + 900f;
                    cc.transform.position = pp;
                    cc.SetMotion(0f);
                    cc.throttle = 0f;
                    float until3 = Time.time + 25f;
                    while (Time.time < until3 && surf.TwinAreasBuilt == 0) yield return null;
                    yield return new WaitForSeconds(2f);
                    int copies = surf.GarrisonCopies;
                    Log($"  near the north pole: {surf.TwinAreasBuilt} areas built twice (each side of the pole), {copies} with their forces in both");
                    good &= copies == 0;
                }
                if (name == "Selene")
                {
                    // sink straight down with the thrusters, away from the pad
                    cc.throttle = 0f;
                    demo.autoElevator = -1f;
                    until = Time.time + 40f;
                    while (Time.time < until && !landing.Landed) yield return null;
                    demo.autoElevator = null;
                    yield return new WaitForSeconds(1f);
                    float under = cc.transform.position.y - site.GroundAt(cc.transform.position);
                    Log($"  sinking with the thrusters: landed {landing.Landed}, pivot {under:F0} m above the ground");
                    Shot("l_selene_down");
                    good &= landing.Landed;
                }
                if (name == "Ochre")
                {
                    // climb out through the ceiling
                    cc.throttle = 0f;
                    demo.autoElevator = 1f;
                    until = Time.time + 90f;
                    while (Time.time < until && landing.Current == PlanetLanding.State.OnSurface) yield return null;
                    demo.autoElevator = null;
                    Log($"  climbing: left the surface by itself {landing.Current != PlanetLanding.State.OnSurface}");
                    good &= landing.Current != PlanetLanding.State.OnSurface;
                    yield return WaitLanding(landing, PlanetLanding.State.InSpace, 10f);
                }
                else
                {
                    landing.TakeOff();
                    yield return WaitLanding(landing, PlanetLanding.State.InSpace, 10f);
                }
                good &= landing.Current == PlanetLanding.State.InSpace && RenderSettings.skybox == spaceSky;
                if (good) ok++;
                Log(good ? $"  {name}: ok" : $"  {name}: FAILED");
                yield return new WaitForSeconds(0.5f);
            }
            Log(ok == total ? $"PASS: landings on every kind of world ({ok} of {total})" : $"FAIL: landings ({ok} of {total} ok)");
        }

        /// <summary>Fly on over the planet (in jumps, so it doesn't take minutes), then all the
        /// way round it east, then over both poles, checking the ground comes back the same.</summary>
        IEnumerator StreamingTest(PlanetLanding landing, Action<bool> done)
        {
            var surf = landing.Surface;
            var w = landing.World;
            if (surf == null || w == null) { Log("  streaming: no surface"); done(false); yield break; }
            Log($"  the planet: {w.N} x {w.M} areas of 5 km ({w.AreaKm2:F0} km2), {w.C / 1000.0:F0} km round; {surf.TilesLoaded} areas built");
            Vector3 probe = cc.transform.position + new Vector3(170f, 0f, 230f);
            float g0 = surf.GroundAt(probe);
            surf.PlanetOf(probe, out double u0, out double v0);
            int a0 = surf.AreaIndex(probe);
            int built0 = surf.TilesBuilt, shifts0 = surf.Shifts;

            IEnumerator GoTo(double u, double v, string what)
            {
                // a jump of up to a whole lap: shift the world down here first (the ship goes
                // with it, staying where it is on the planet), so the place is near the middle
                Vector3 p = surf.WorldOf(u, v, 0f);
                Vector3 off = p - surf.Center;
                surf.ShiftBy(new Vector3(-Mathf.Round(off.x / SurfaceWorld.T) * SurfaceWorld.T, 0f, -Mathf.Round(off.z / SurfaceWorld.T) * SurfaceWorld.T));
                p = surf.WorldOf(u, v, 0f);
                p.y = surf.FloorAt(p) + 700f;
                cc.transform.position = p;
                cc.SetMotion(0f);
                cc.throttle = 0f;
                yield return null;
                yield return null;   // the world shifts back round the ship
                float until = Time.time + 25f;
                Vector3 here = surf.WorldOf(u, v, 0f);
                while (Time.time < until && !surf.Ready(here)) { here = surf.WorldOf(u, v, 0f); yield return null; }
                yield return new WaitForSeconds(0.5f);
                cc.transform.position = new Vector3(cc.transform.position.x, surf.FloorAt(cc.transform.position) + 500f, cc.transform.position.z);
                yield return null;
                Log($"  {what}: built {surf.Ready(surf.WorldOf(u, v, 0f))}, {surf.TilesLoaded} areas in memory, {surf.TilesBuilt} built so far, " +
                    $"{surf.TilesUnloaded} thrown away, world shifted {surf.Shifts} times, last area worked out in {surf.LastJobSeconds * 1000f:F0} ms, " +
                    $"put in place in {surf.LastApplyMs:F0} ms");
            }

            // on across the planet, 4 km at a time
            float worst = 0f;
            for (int i = 1; i <= 6; i++)
            {
                yield return GoTo(u0 + i * 4000.0, v0, $"{i * 4} km east");
                worst = Mathf.Max(worst, surf.LastApplyMs);
            }
            bool gone = surf.TilesUnloaded > 0;
            Shot("l3_streaming");

            // all the way round, east
            yield return GoTo(u0 + w.C, v0, "all the way round east");
            Vector3 q = surf.WorldOf(u0 + w.C, v0, 0f);
            float g1 = surf.GroundAt(q);
            int a1 = surf.AreaIndex(q);
            bool round = Mathf.Abs(g1 - g0) < 0.5f && a1 == a0;
            Log($"  after {w.C / 1000.0:F0} km east: ground {g1 - surf.Center.y:F1} m here, {g0 - surf.Center.y:F1} m at the start; area {a1} (was {a0})");
            Shot("l4_round_east");

            // over the north pole, down the far side, over the south pole and back up
            yield return GoTo(u0 + w.C, v0 + 4.0 * w.V, "over both poles");
            Vector3 q2 = surf.WorldOf(u0 + w.C, v0 + 4.0 * w.V, 0f);
            float g2 = surf.GroundAt(q2);
            int a2 = surf.AreaIndex(q2);
            bool poles = Mathf.Abs(g2 - g0) < 0.5f && a2 == a0;
            Log($"  after {4.0 * w.V / 1000.0:F0} km north (over both poles): ground {g2 - surf.Center.y:F1} m, area {a2}");
            // half way: the far side of the north pole is the area at longitude + 180, turned round
            Vector3 q3 = surf.WorldOf(u0 + w.C, v0 + 2.0 * w.V, 0f);
            surf.LatLon(q3, out float lat3, out float lon3);
            surf.LatLon(probe, out float lat0, out float lon0);
            Log($"  over the north pole the start point's twin is at {PlanetLanding.LatLonText(lat3, lon3)} (start {PlanetLanding.LatLonText(lat0, lon0)})");

            bool ok = gone && round && poles && surf.Shifts > shifts0 && surf.TilesBuilt > built0;
            Log($"  streaming: areas thrown away {gone}, same ground after a lap east {round}, after both poles {poles}, " +
                $"explored {surf.Explored.Count} of {w.AreaCount} areas, slowest area put in place {worst:F0} ms");
            done(ok);
        }

        void Report(PlanetLanding landing, string how)
        {
            var site = landing.Site;
            var td = site.terrain != null ? site.terrain.terrainData : null;
            string terrain = td != null ? $"{td.size.x:F0} x {td.size.z:F0} m, {td.size.y:F0} m high" : "none";
            string sea = float.IsNegativeInfinity(site.seaLevel) ? "none" : $"{site.seaLevel - site.center.y:F0} m";
            var w = landing.World;
            string planet = w != null ? $"; planet {w.N} x {w.M} areas, {w.C / 1000.0:F0} km round, {landing.Surface.TilesLoaded} areas built" : "";
            planet += $"; memory {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576} MB reserved, managed heap {GC.GetTotalMemory(false) / 1048576} MB";
            Log($"  {how}: on {landing.Body?.displayName} ({site.kind}{(w != null && w.hostile ? ", hostile" : "")}), seed {landing.Seed}, built in {landing.LastBuildSeconds * 1000f:F0} ms{planet}; " +
                $"terrain {terrain}, outpost {site.hasOutpost}, sea {sea}; ship altitude {landing.Altitude:F0} m; " +
                $"HUD '{Plain(landing.HudText())}'; {site.description}");
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

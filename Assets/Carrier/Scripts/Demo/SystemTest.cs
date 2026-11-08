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
    /// "-stationtest" (or ?stationtest) runs only the installations: every station, base and
    /// installation visited, docking (repair, sale) where there is a berth, the hostile ones'
    /// guns fought and left behind, both surface bases landed at, and the garbage collected on
    /// the way down, on the way up and round a restart.
    /// "-orbittest" (or ?orbittest) runs only the orbits: ten minutes of them at 8x, every
    /// motion checked against Kepler's laws and the Lagrange points, the map before and after,
    /// the launchers launching, a warp to a moving planet, and a restart.
    /// </summary>
    public partial class SystemTest : MonoBehaviour
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
            // -stationtest (or ?stationtest): only the stations, bases and installations
            t.stationOnly = Array.IndexOf(args, "-stationtest") >= 0 || (web && HasQueryKey(Application.absoluteURL, "stationtest"));
            // -orbittest (or ?orbittest): only the orbits and the launchers
            t.orbitOnly = Array.IndexOf(args, "-orbittest") >= 0 || (web && HasQueryKey(Application.absoluteURL, "orbittest"));
            // -hudtest (or ?hudtest): only the ship's console and the captain
            t.hudOnly = Array.IndexOf(args, "-hudtest") >= 0 || (web && HasQueryKey(Application.absoluteURL, "hudtest"));
        }

        bool web, landOnly, mineOnly, mineSurfaceOnly, stationOnly, orbitOnly;
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
            if (orbitOnly)
            {
                yield return OrbitTest(kb);
                Log("done");
                if (!web) Application.Quit();
                yield break;
            }
            if (hudOnly)
            {
                yield return HudTest(kb, InputSystem.AddDevice<Mouse>("SystemTestHudMouse"));
                Log("done");
                if (!web) Application.Quit();
                yield break;
            }
            if (stationOnly)
            {
                yield return StationTest(kb);
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
            for (int i = 0; i < 60 && map.Selected?.displayName != "Selene"; i++) yield return Key(kb, UnityEngine.InputSystem.Key.Tab);
            Log($"keyboard Tab to Selene: selected {map.Selected?.displayName}");
            yield return Warp(() => Key(kb, UnityEngine.InputSystem.Key.Enter), "keyboard Enter", "w2", true);

            // the long trip: to the star
            yield return Key(kb, UnityEngine.InputSystem.Key.M);
            for (int i = 0; i < 60 && map.Selected?.displayName != "Kestrel"; i++) yield return Key(kb, UnityEngine.InputSystem.Key.Tab);
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
            for (int i = 0; i < 60 && map.Selected?.displayName != "Jovara Trojans"; i++) yield return Pad(pad, GamepadButton.RightShoulder);
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
            for (int i = 0; i < 60 && map.Selected?.displayName != "Thalassa"; i++) yield return Pad(pad, GamepadButton.RightShoulder);
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
            // back to Thalassa: the capital ships wait on the way in from there
            yield return WarpTo(kb, "Thalassa", "n9");

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
            for (int i = 0; i < 60 && map.Selected?.displayName != "Glacis"; i++) yield return Key(kb, UnityEngine.InputSystem.Key.Tab);
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


        // ------------------------------------------------------------------ orbits

        static float AngleAround(Vector3 a, Vector3 b) => Vector3.Angle(a, b);

        IEnumerator OrbitTest(Keyboard kb)
        {
            var sys = OrbitalSystem.Instance;
            if (sys == null) { Log("FAIL: orbits (no OrbitalSystem in this level)"); yield break; }
            var orbits = sys.Orbits.Where(o => o != null).ToList();
            Orbit O(string n) => orbits.FirstOrDefault(o => o.name.StartsWith(n + " ("));
            var planets = orbits.Where(o => o.kind == Orbit.Kind.Kepler && o.parent == null && o.massRatio > 0f).ToList();
            var results = new List<(string, bool)>();
            Log($"orbits: {orbits.Count} ({planets.Count} planets); star GM {OrbitalSystem.StarGM:E3}");
            // Kepler's third law: T^2 / a^3 the same for every planet
            double k0 = 0.0, worstK = 0.0;
            foreach (var p in planets)
            {
                double k = p.Period * p.Period / (p.semiMajor * p.semiMajor * p.semiMajor);
                if (k0 == 0.0) k0 = k;
                worstK = System.Math.Max(worstK, System.Math.Abs(k / k0 - 1.0));
                Log($"  {p.name,-22} a {p.semiMajor / 1000.0,6:F1} km  period {p.Period / 60.0,6:F1} min  T^2/a^3 {k:E4}");
            }
            foreach (var o in orbits.Where(o => !planets.Contains(o)))
                Log($"  {o.name,-38} period {o.Period / 60.0,7:F1} min");
            results.Add(("Kepler's third law", worstK < 1e-6));

            // the start: where everything is
            var thalassa = O("Thalassa");
            var star = Point("Kestrel");
            var highport = Installation.All.FirstOrDefault(i => i.displayName == "Thalassa Highport");
            var thalassaBody = Point("Thalassa").transform;
            var l1 = O("Thalassa L1 Observatory");
            var l4 = O("Jovara Trojans");
            var l5 = O("Jovara L5 Depot");
            var jovara = O("Jovara");
            var keep = l1 != null ? l1.GetComponent<StationKeeping>() : null;
            var swarm = l4 != null ? l4.GetComponent<TrojanSwarm>() : null;
            var iskra = FindObjectsByType<Comet>(FindObjectsSortMode.None).FirstOrDefault(c => c.name.Contains("Iskra"));
            var launchers = FindObjectsByType<LaunchSystem>(FindObjectsSortMode.None);
            var climbers = FindAnyObjectByType<ElevatorClimbers>();
            var homes = FindObjectsByType<OrbitHome>(FindObjectsSortMode.None);
            Vector3 S() => star.Position;
            var off0 = orbits.ToDictionary(o => o, o => o.transform.position - (o.parent != null ? o.parent.transform.position : S()));
            double t0 = sys.Time;
            Vector3 ship0 = cc.transform.position - thalassa.transform.position;
            Vector3 hpLocal0 = thalassaBody.InverseTransformPoint(highport.transform.position);
            var homeDist0 = homes.ToDictionary(h => h, h => h.transform.position - h.home.position);
            var rock = swarm != null && swarm.rocks != null && swarm.rocks.childCount > 0 ? swarm.rocks.GetChild(0) : null;
            Vector3 rock0 = rock != null ? rock.localPosition : Vector3.zero;
            float iskraR0 = iskra != null ? Vector3.Distance(iskra.transform.position, S()) : 0f;
            yield return Key(kb, UnityEngine.InputSystem.Key.H);
            yield return Key(kb, UnityEngine.InputSystem.Key.M);
            yield return new WaitForSecondsRealtime(0.5f);
            var px0 = planets.ToDictionary(p => p, p => map.ScreenPosOf(p.GetComponentInChildren<WarpPoint>()));
            Shot("o0_map_start");
            yield return Key(kb, UnityEngine.InputSystem.Key.Escape);

            // ten minutes of orbits, eight times as fast
            Log("ten minutes of orbits at 8x speed...");
            cc.throttle = 0f;
            Time.timeScale = 8f;
            float frames = 0f, realStart = Time.realtimeSinceStartup;
            while (sys.Time - t0 < 600.0) { frames++; yield return null; }
            Time.timeScale = 1f;
            double dt = sys.Time - t0;
            Log($"  {dt:F0} s of orbits in {Time.realtimeSinceStartup - realStart:F0} s, {frames / Mathf.Max(1f, Time.realtimeSinceStartup - realStart):F0} frames a second; " +
                $"anchor {sys.Anchor?.name}, {sys.AnchorChanges} anchor changes");

            // each orbit moved by what its period says
            bool anglesOk = true;
            foreach (var o in orbits.Where(o => o.kind == Orbit.Kind.Kepler && o.eccentricity < 0.01))
            {
                Vector3 now = o.transform.position - (o.parent != null ? o.parent.transform.position : S());
                float moved = AngleAround(off0[o], now), want = (float)(360.0 * dt / o.Period);
                want %= 360f;
                want = want > 180f ? 360f - want : want;
                float dr = Mathf.Abs(now.magnitude - off0[o].magnitude);
                bool ok = Mathf.Abs(moved - want) < 0.5f && dr < 2f;
                anglesOk &= ok;
                if (!ok || planets.Contains(o) || o.parent != null && planets.Contains(o.parent))
                    Log($"  {o.name,-30} moved {moved,6:F1} deg (Kepler says {want,6:F1}), distance changed {dr:F2} m {(ok ? "" : "  WRONG")}");
            }
            results.Add(("planets, moons and stations move as their periods say", anglesOk));

            // geostationary: the Highport stays over the same point of Thalassa, and Thalassa's day
            // is the period of an orbit at the Highport's height (so it is a real orbit, not a tower)
            float hpDrift = Vector3.Distance(thalassaBody.InverseTransformPoint(highport.transform.position), hpLocal0) * thalassaBody.lossyScale.x;
            float spinRate = thalassaBody.GetComponent<Drift>().spin.y;
            float spun = (float)(dt * spinRate) % 360f;
            var thOrbit = OrbitalSystem.OrbitOf(thalassaBody);
            double hpR = Vector3.Distance(highport.transform.position, thalassaBody.position);
            double kepler = thOrbit != null ? 2.0 * System.Math.PI * System.Math.Sqrt(hpR * hpR * hpR / thOrbit.BodyGM) : 0.0;
            float day = 360f / Mathf.Max(1e-6f, Mathf.Abs(spinRate));
            Log($"  Highport: {hpDrift:F2} m from where it was over Thalassa (which turned {spun:F0} deg); Thalassa's day {day:F0} s, an orbit at {hpR / 1000.0:F1} km takes {kepler:F0} s");
            results.Add(("the Highport is geostationary", hpDrift < 1f && kepler > 0.0 && System.Math.Abs(kepler - day) < day * 0.01));

            // the Lagrange points
            Vector3 sT = thalassa.transform.position - S(), sL1 = l1.transform.position - S();
            float colinear = AngleAround(sT, sL1);
            float hill = (float)Orbit.HillFraction(thalassa.massRatio) * sT.magnitude;
            float l1Off = Vector3.Distance(l1.transform.position, thalassa.transform.position);
            Log($"  L1: {colinear:F3} deg off the star-Thalassa line, {l1Off / 1000f:F2} km from Thalassa (Hill radius {hill / 1000f:F2} km); " +
                $"station-keeping burns {keep?.Burns}, largest drift {keep?.LargestDrift:F0} m, e-folding time {1f / Mathf.Max(1e-6f, keep != null ? keep.Lambda : 0f) / 60f:F1} min");
            bool l1Ok = colinear < 0.2f && Mathf.Abs(l1Off - hill) < 200f && keep != null && keep.Burns >= 1 && keep.LargestDrift <= keep.tolerance + 5f;
            Vector3 sJ = jovara.transform.position - S();
            float a4 = AngleAround(sJ, l4.transform.position - S()), a5 = AngleAround(sJ, l5.transform.position - S());
            float r4 = (l4.transform.position - S()).magnitude / sJ.magnitude, r5 = (l5.transform.position - S()).magnitude / sJ.magnitude;
            float rockMoved = rock != null ? Vector3.Distance(rock.localPosition, rock0) : 0f;
            Log($"  L4 (Trojans): {a4:F2} deg from Jovara, radius ratio {r4:F4}; L5 (depot): {a5:F2} deg, ratio {r5:F4}; a Trojan rock librated {rockMoved:F0} m " +
                $"(one swing takes {(swarm != null ? swarm.LibrationPeriod / 3600.0 : 0.0):F1} h)");
            bool l45Ok = Mathf.Abs(a4 - 60f) < 0.1f && Mathf.Abs(a5 - 60f) < 0.4f && Mathf.Abs(r4 - 1f) < 0.01f && Mathf.Abs(r5 - 1f) < 0.01f && rockMoved > 1f;
            results.Add(("Lagrange points (L1 on the line, unstable, kept by burns; L4 and L5 at 60 degrees, librating)", l1Ok && l45Ok));

            // the comet on its ellipse, its tails still away from the star
            if (iskra != null)
            {
                float r1 = Vector3.Distance(iskra.transform.position, S());
                Log($"  Comet Iskra: {iskraR0 / 1000f:F1} -> {r1 / 1000f:F1} km from the star; tails {iskra.TailError:F1} deg off straight away from it");
                results.Add(("the comet follows its ellipse", r1 > iskraR0 + 1000f && iskra.TailError < 3f));
            }

            // the frigate's neighbourhood held still; enemies elsewhere carried with their homes
            float shipMoved = Vector3.Distance(cc.transform.position - thalassa.transform.position, ship0);
            float worstHome = 0f; string worstName = "";
            foreach (var h in homes)
            {
                if (h == null || h.home == null) continue;
                float d = Vector3.Distance(h.transform.position - h.home.position, homeDist0[h]);
                if (d > worstHome) { worstHome = d; worstName = h.name; }
            }
            Log($"  the frigate moved {shipMoved:F1} m relative to Thalassa; {homes.Length} posted enemies stayed with their posts (worst {worstName}, {worstHome:F0} m)");
            results.Add(("ships carried with what they are in", shipMoved < 10f && worstHome < 300f));

            // the map has changed
            yield return Key(kb, UnityEngine.InputSystem.Key.M);
            yield return new WaitForSecondsRealtime(0.5f);
            Shot("o1_map_after_10_min");
            float leastPx = float.MaxValue; string moves = "";
            foreach (var p in planets)
            {
                float d = Vector2.Distance(px0[p], map.ScreenPosOf(p.GetComponentInChildren<WarpPoint>()));
                leastPx = Mathf.Min(leastPx, d);
                moves += $"{p.name.Replace(" (orbit)", "")} {d:F0} px  ";
            }
            Log($"  on the map: {moves}");
            yield return Key(kb, UnityEngine.InputSystem.Key.Escape);
            results.Add(("the map shows the planets moving", leastPx > 3f));

            // the launchers have been launching
            Log($"  launchers: {string.Join(", ", launchers.Select(l => $"{l.name} {l.Launched}"))}; elevator climbers travelled {(climbers != null ? climbers.Travelled / 1000f : 0f):F0} km");
            results.Add(("every launcher launched", launchers.Length >= 5 && launchers.All(l => l.Launched > 0) && climbers != null && climbers.Travelled > 1000f));

            // each launcher, at a launch
            foreach (var (name, shot) in new[] { ("Thalassa Space Elevator", "o3_elevator"), ("Thalassa Launch Loop", "o4_loop"), ("Ochre StarTram", "o5_startram"),
                                                ("Ochre Spin Launcher", "o6_spin"), ("Selene Mass Driver", "o7_massdriver"), ("Steropes Slingatron", "o8_slingatron") })
            {
                yield return WarpTo(kb, name, shot);
                var wp = Point(name);
                if (wp == null) continue;
                var ls = wp.GetComponentInParent<LaunchSystem>();
                if (ls != null)
                {
                    int n0 = ls.Launched;
                    float until = Time.time + ls.interval + ls.trackTime + 2f;
                    while (Time.time < until && ls.Launched == n0) yield return null;
                    yield return new WaitForSeconds(0.4f);
                    Log($"  {name}: launched {ls.Launched - n0} while watching (track {ls.TrackLength / 1000f:F2} km, {ls.exitSpeed:F0} m/s off the end)");
                }
                yield return LookAt(wp.Position, shot + "_view", 4f, 30f);
            }

            // a warp to the fastest-moving planet arrives where it has got to (last: it is hostile)
            yield return WarpTo(kb, "Cinder", "o9_cinder");
            float arrive = Vector3.Distance(cc.transform.position, Point("Cinder").Position);
            Log($"  arrived {arrive / 1000f:F1} km from Cinder (arrival distance {Point("Cinder").arrivalDistance / 1000f:F1} km)");
            results.Add(("a warp to a moving planet arrives by it", Mathf.Abs(arrive - Point("Cinder").arrivalDistance) < 2500f));

            // a restart starts the orbits again
            DontDestroyOnLoad(gameObject);
            var oldDemo = demo;
            var menu = FindAnyObjectByType<GameMenu>();
            if (menu != null) menu.Restart();
            float wait = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < wait)
            {
                demo = FindAnyObjectByType<CarrierDemoInput>();
                if (demo != null && demo != oldDemo) break;
                yield return null;
            }
            yield return new WaitForSeconds(1f);
            var sys2 = OrbitalSystem.Instance;
            var th2 = sys2 != null ? sys2.Orbits.FirstOrDefault(o => o != null && o.name.StartsWith("Thalassa (")) : null;
            float back = th2 != null ? AngleAround(th2.transform.position - sys2.StarPosition, off0[thalassa]) : 180f;
            Log($"  after a restart: orbit time {sys2?.Time:F1} s, Thalassa {back:F2} deg from where it started");
            results.Add(("a restart starts the orbits again", back < 0.5f));

            foreach (var (what, ok) in results) Log($"  {(ok ? "ok" : "FAILED")}: {what}");
            int passed = results.Count(r => r.Item2);
            Log(passed == results.Count ? $"PASS: orbits ({passed} of {results.Count})"
                                        : $"FAIL: orbits ({passed} of {results.Count}: failed {string.Join("; ", results.Where(r => !r.Item2).Select(r => r.Item1))})");
        }

        // ------------------------------------------------------------------ installations

        /// <summary>Turn the camera from behind the frigate onto a point, take a picture, put it back.</summary>
        IEnumerator LookAt(Vector3 p, string shot, float distMul = 2.6f, float down = 6f)
        {
            var oc = demo.orbitCamera;
            if (oc == null) { Shot(shot); yield break; }
            float d0 = oc.distance, p0 = oc.pitch, y0 = oc.yaw;
            Vector3 to = p - cc.transform.position;
            oc.yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            oc.pitch = down - Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            oc.distance = demo.ShipViewDistance * distMul;
            yield return new WaitForSeconds(0.7f);
            Shot(shot);
            yield return null;
            oc.distance = d0; oc.pitch = p0; oc.yaw = y0;
        }

        /// <summary>Fly the frigate toward a point until within `within` of it, `stop` says so, or out of time.</summary>
        IEnumerator FlyTo(Vector3 p, float within, float seconds, float throttle = 0.7f, Func<bool> stop = null)
        {
            float until = Time.time + seconds;
            cc.throttle = throttle;
            while (Time.time < until && Vector3.Distance(cc.transform.position, p) > within && (stop == null || !stop()))
            {
                var v = ShipAutopilot.BowOn(cc, p);
                demo.autoRudder = v.x;
                demo.autoPitch = v.y;
                yield return null;
            }
            demo.autoRudder = null;
            demo.autoPitch = null;
        }

        /// <summary>Fly straight away from a point until `done` (or out of time).</summary>
        IEnumerator FlyAway(Vector3 from, Func<bool> done, float seconds)
        {
            float until = Time.time + seconds;
            cc.throttle = 1f;
            while (Time.time < until && !done())
            {
                Vector3 away = cc.transform.position + (cc.transform.position - from).normalized * 5000f;
                var v = ShipAutopilot.BowOn(cc, away);
                demo.autoRudder = v.x;
                demo.autoPitch = v.y;
                yield return null;
            }
            demo.autoRudder = null;
            demo.autoPitch = null;
            cc.throttle = 0f;
        }

        /// <summary>Dock (K) at a station, check the services, undock (K).</summary>
        IEnumerator DockTest(Keyboard kb, string name, string shot, Action<bool> done)
        {
            var dock = Docking.Instance;
            var st = Installation.All.FirstOrDefault(i => i.displayName == name);
            if (dock == null || st == null) { Log($"  FAIL: no docking controller, or no {name}"); done(false); yield break; }
            yield return FlyTo(st.BerthPosition, st.dockRange * 0.85f, 60f);
            cc.throttle = 0f;
            var hull = cc.GetComponent<Health>();
            var hold = cc.GetComponent<CargoHold>();
            hull.Damage(700f);
            if (hold != null) { hold.Add(Mineral.Iron, 90f); hold.Add(Mineral.Platinum, 4f); }
            // (a hit counts as being under fire for a few seconds: no docking till it has passed)
            yield return new WaitForSeconds(drive.recentHitSeconds + 0.5f);
            float hull0 = hull.Current, credits0 = hold != null ? hold.Credits : 0f, worth = hold != null ? hold.Value : 0f, load = hold != null ? hold.Used : 0f;
            Log($"  {name}: {Vector3.Distance(cc.transform.position, st.BerthPosition) / 1000f:F1} km from the berth; HUD '{Strip(dock.HudText())}'");
            yield return Key(kb, UnityEngine.InputSystem.Key.K);
            float until = Time.time + 8f;
            while (Time.time < until && dock.Current != Docking.State.Docked) yield return null;
            yield return new WaitForSeconds(1.4f);
            bool docked = dock.Current == Docking.State.Docked && dock.At == st;
            bool atBerth = Vector3.Distance(cc.transform.position, st.BerthPosition) < 5f;
            bool repaired = hull.Current >= hull.maxHealth - 0.5f;
            bool sold = !st.market || (hold != null && hold.Used < 0.5f && hold.Credits - credits0 > worth * 0.99f);
            bool warpLocked = !drive.CanWarp(out string lockWhy);
            bool landLocked = PlanetLanding.Instance == null || !PlanetLanding.Instance.CanLand(out _, out _);
            Log($"  docked {docked} at the berth {atBerth}; hull {hull0:F0} -> {hull.Current:F0} of {hull.maxHealth:F0}; " +
                (st.market ? $"sold {load:F0} t worth {worth:N0} cr: credits {credits0:N0} -> {(hold != null ? hold.Credits : 0f):N0}; " : "no market; ") +
                $"warp locked {warpLocked} ({lockWhy}), landing locked {landLocked}");
            yield return LookAt(st.transform.position, shot + "_docked", 3.2f, 10f);
            yield return Key(kb, UnityEngine.InputSystem.Key.K);
            until = Time.time + 6f;
            while (Time.time < until && dock.Current != Docking.State.Free) yield return null;
            yield return new WaitForSeconds(1f);
            bool undocked = dock.Current == Docking.State.Free && drive.CanWarp(out _) && cc.Speed > 5f;
            Log($"  undocked {undocked}: speed {cc.Speed:F0} m/s, {Vector3.Distance(cc.transform.position, st.BerthPosition):F0} m from the berth, warp ready {drive.CanWarp(out string w2)} {w2}");
            cc.throttle = 0f;
            done(docked && atBerth && repaired && sold && warpLocked && landLocked && undocked);
        }

        static string Strip(string s) => s == null ? "(none)" : System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "");

        /// <summary>Fly at a hostile installation until its guns open up, trade fire, then get
        /// out of range.</summary>
        IEnumerator GridTest(Keyboard kb, string name, string targetName, string shot, float fight, Action<bool> done)
        {
            var grid = DefenseGrid.All.FirstOrDefault(g => g.gridName == name);
            if (grid == null) { Log($"  FAIL: no defence grid for {name}"); done(false); yield break; }
            var centre = grid.transform.position;
            var guns = grid.weapons.Where(w => w != null).ToArray();
            if (guns.Length == 0) { Log($"  FAIL: {name}'s defence grid has no guns"); done(false); yield break; }
            var turrets = guns.SelectMany(w => w.GetComponentsInChildren<CarrierTurret>()).ToArray();
            int shots0 = turrets.Sum(t => t.ShotsFired);
            var health = cc.GetComponent<Health>();
            var shield = cc.GetComponentInChildren<Shield>();
            float near0 = grid.NearestGun(cc.transform.position, out _);
            Log($"  {name}: {guns.Length} guns, {turrets.Length} turrets, nearest {near0 / 1000f:F1} km; engaged before closing {grid.Engaged}");
            // in toward it until they open fire (well clear of the rock or the platforms)
            yield return FlyTo(centre, 1500f, 120f, 0.5f, () => grid.Engaged);
            float engagedAt = grid.NearestGun(cc.transform.position, out _);
            Log($"  engaged {grid.Engaged} at {engagedAt / 1000f:F1} km from the nearest gun");
            if (!grid.Engaged) { done(false); yield break; }
            var target = guns.Select(w => w.transform).FirstOrDefault(t => t.name == targetName) ?? guns[0].transform;
            // the missiles and beams reach farther than the turrets: close in to turret range
            yield return FlyTo(target.position, 2500f, 40f, 0.5f);
            cc.throttle = 0.15f;
            demo.SelectTarget(target);
            var targetHealth = target.GetComponent<Health>();
            float shield0 = shield != null ? shield.Strength : 0f, shieldLow = shield0, hull0 = health.Current, hullLow = hull0;
            bool combat = false, warpLocked = false;
            string why = "";
            int charged = 0;
            float until = Time.time + fight;
            bool pictured = false;
            while (Time.time < until)
            {
                var v = ShipAutopilot.BowOn(cc, target.position);
                demo.autoRudder = v.x; demo.autoPitch = v.y;
                if (shield != null) shieldLow = Mathf.Min(shieldLow, shield.Strength);
                hullLow = Mathf.Min(hullLow, health.Current);
                if (!combat) combat = drive.InCombat(out why);
                if (!warpLocked) warpLocked = !drive.CanWarp(out _);
                if (charged < 3 && demo.MainGun != null && demo.MainGun.Current == MainBeam.State.Idle &&
                    Vector3.Angle(cc.transform.forward, target.position - cc.transform.position) < 8f)
                {
                    yield return Key(kb, UnityEngine.InputSystem.Key.B);
                    charged++;
                }
                if (!pictured && Time.time > until - fight * 0.5f) { pictured = true; yield return LookAt(centre, shot + "_fight", 3.4f, 4f); }
                yield return null;
            }
            demo.autoRudder = null; demo.autoPitch = null;
            int shots = turrets.Sum(t => t.ShotsFired) - shots0;
            float took = targetHealth != null ? targetHealth.TotalDamage : 0f;
            Log($"  after {fight:F0} s: their turrets fired {shots}; frigate shield {shield0:F0} -> lowest {shieldLow:F0}, hull {hull0:F0} -> lowest {hullLow:F0}; " +
                $"{target.name} took {took:F0} of {(targetHealth != null ? targetHealth.maxHealth : 0f):F0} (destroyed {targetHealth != null && targetHealth.TimesDestroyed > 0}); " +
                $"bow gun charged {charged}x; in combat {combat} ({why}); warp locked {warpLocked}");
            // out of range: they stop, and the warp drive is free again
            yield return FlyAway(centre, () => !grid.Engaged, 120f);
            float far = grid.NearestGun(cc.transform.position, out _);
            yield return new WaitForSeconds(drive.recentHitSeconds + 0.5f);
            // the guns have stopped: no grid is holding the drive any more
            bool free = !grid.Engaged && !DefenseGrid.AnyEngagedNear(cc.transform.position, drive.combatRange, out _);
            // (other hostiles may have joined in, like the corsair patrol at Cinder, and chased the
            // frigate: keep going till they give up, so the next warp can go)
            yield return FlyAway(centre, () => drive.CanWarp(out _), 300f);
            Log($"  left: {far / 1000f:F1} km from the nearest gun, grid engaged {grid.Engaged}, warp ready {drive.CanWarp(out string fw2)} {fw2}");
            done((shieldLow < shield0 || hullLow < hull0) && took > 0f && combat && warpLocked && free);
        }

        /// <summary>Land on a surface base (by K from over it, or straight down for a hostile one),
        /// check its buildings, take off; the garbage is collected each way.</summary>
        IEnumerator BaseTest(Keyboard kb, string name, bool byKey, string shot, Action<bool> done)
        {
            var landing = PlanetLanding.Instance;
            var wp = Point(name);
            if (landing == null || wp == null) { Log($"  FAIL: no landing controller, or no {name}"); done(false); yield break; }
            int gc0 = SceneMemory.Collections;
            float alt = Vector3.Distance(cc.transform.position, wp.Position);
            Vector3 up = (wp.Position - wp.landsOn.Position).normalized;
            float overhead = Vector3.Angle(up, cc.transform.position - wp.Position);
            Log($"  over {name}: {alt / 1000f:F1} km from it, {overhead:F0} deg off straight overhead; landing HUD '{Strip(landing.HudText())}'");
            yield return LookAt(wp.Position, shot + "_from_orbit", 2.4f, 40f);
            if (byKey) yield return Key(kb, UnityEngine.InputSystem.Key.K);
            else landing.LandOn(wp);
            yield return WaitLanding(landing, PlanetLanding.State.OnSurface, 90f);
            if (landing.Current != PlanetLanding.State.OnSurface) { Log($"  FAIL: didn't land at {name} ({landing.Current})"); done(false); yield break; }
            int gcDown = SceneMemory.Collections - gc0;
            yield return new WaitForSeconds(2.5f);
            var group = landing.Surface.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Base: " + name);
            int buildings = group != null ? group.childCount : 0;
            Vector3 middle = cc.transform.position;
            if (buildings > 0) { middle = Vector3.zero; foreach (Transform c in group) middle += c.position; middle /= buildings; }
            float away = Vector3.Distance(new Vector3(middle.x, 0f, middle.z), new Vector3(cc.transform.position.x, 0f, cc.transform.position.z));
            var garrisons = landing.Surface.GetComponentsInChildren<GroundGarrison>(true);
            int units = garrisons.Sum(g => g.units.Count);
            Log($"  landed on {landing.Body?.displayName} ({(byKey ? "K" : "straight down")}): '{landing.Site?.description}', pad {landing.Site != null && landing.Site.hasOutpost}; " +
                $"'{group?.name}' has {buildings} buildings ({string.Join(", ", group != null ? group.Cast<Transform>().Select(t => t.name).Distinct() : new string[0])}), " +
                $"{away:F0} m from the frigate; ground forces {units} in {garrisons.Length} garrisons; garbage collected {gcDown}x on the way down");
            yield return LookAt(middle, shot + "_surface", 3f, 8f);
            landing.TakeOff();
            yield return WaitLanding(landing, PlanetLanding.State.InSpace, 30f);
            yield return new WaitForSeconds(0.5f);
            int gcUp = SceneMemory.Collections - gc0 - gcDown;
            Log($"  back in space: {landing.Current}; garbage collected {gcUp}x after leaving the surface ('{SceneMemory.LastReason}', " +
                $"heap {SceneMemory.LastBeforeMB:F0} -> {SceneMemory.LastAfterMB:F0} MB)");
            done(landing.Current == PlanetLanding.State.InSpace && buildings >= 12 && gcDown >= 1 && gcUp >= 1 && landing.Body == wp.landsOn
                 && (byKey || units >= 10));
        }

        IEnumerator StationTest(Keyboard kb)
        {
            var built = WarpPoint.All.Where(w => w != null && w.Built).ToList();
            Log($"installations: {built.Count}: " + string.Join(", ", built.Select(w => $"{w.displayName} ({SystemMap.KindText(w)}{(w.hostile ? ", hostile" : "")})")));
            var kinds = new[] { WarpPoint.Kind.Station, WarpPoint.Kind.Installation, WarpPoint.Kind.Weapons, WarpPoint.Kind.AsteroidBase, WarpPoint.Kind.SurfaceBase, WarpPoint.Kind.Lagrange };
            bool allKinds = kinds.All(k => built.Any(w => w.kind == k));
            int dockable = Installation.All.Count(i => i.canDock);
            Log($"  every kind present {allKinds}; {dockable} places to dock; {DefenseGrid.All.Count} defence grids");
            yield return Key(kb, UnityEngine.InputSystem.Key.H);   // the controls list off the pictures
            yield return Key(kb, UnityEngine.InputSystem.Key.M);
            yield return new WaitForSeconds(0.4f);
            Shot("s0_map");
            yield return Key(kb, UnityEngine.InputSystem.Key.Escape);
            var results = new List<(string, bool)>();
            bool ok = false;

            // the friendly places: dock where there is a berth, look at the rest
            foreach (var (name, shot, dockHere) in new[]
            {
                ("Thalassa Highport", "s1_highport", true), ("Thalassa L1 Observatory", "s2_observatory", false),
                ("Deepcut Station", "s3_deepcut", true), ("Ochre Relay", "s4_relay", false),
                ("Brontes Shipyard", "s5_shipyard", true), ("Jovara L5 Depot", "s6_depot", true),
            })
            {
                yield return WarpTo(kb, name, shot);
                var wp = Point(name);
                bool here = wp != null && WarpPoint.Nearest(cc.transform.position) == wp;
                yield return LookAt(wp != null ? wp.Position : cc.transform.position, shot + "_view");
                ok = here;
                if (dockHere && here) yield return DockTest(kb, name, shot, r => ok = r);
                Log($"{(ok ? "PASS" : "FAIL")}: {name} (arrived{(dockHere ? ", docked, repaired, sold, undocked" : "")})");
                results.Add((name, ok));
            }

            // a base on a moon: land by it with K, and take off again
            yield return WarpTo(kb, "Selene Base", "s7_selene");
            yield return BaseTest(kb, "Selene Base", true, "s7_selene", r => ok = r);
            Log($"{(ok ? "PASS" : "FAIL")}: Selene Base (from orbit over it, land beside it, colony buildings, take off, garbage collected)");
            results.Add(("Selene Base", ok));

            // the hostile ones: the frigate is kept alive for the rest of the test
            var hull = cc.GetComponent<Health>();
            hull.cannotDie = true;
            Log("(the frigate can't be destroyed from here on: the test goes on whatever happens in the fights)");
            yield return WarpTo(kb, "Corsair Haven", "s8_haven");
            yield return GridTest(kb, "Corsair Haven", "Haven Gun A", "s8_haven", 25f, r => ok = r);
            Log($"{(ok ? "PASS" : "FAIL")}: Corsair Haven (its guns open up, both sides hit, out of range they stop)");
            results.Add(("Corsair Haven", ok));
            yield return WarpTo(kb, "Cinder Orbital Battery", "s9_battery");
            yield return GridTest(kb, "Cinder Orbital Battery", "Battery Platform A", "s9_battery", 35f, r => ok = r);
            Log($"{(ok ? "PASS" : "FAIL")}: Cinder Orbital Battery (the platforms and lance open up, both sides hit, out of range they stop)");
            results.Add(("Cinder Orbital Battery", ok));
            yield return WarpTo(kb, "Cinder Fortress", "s10_fortress");
            yield return BaseTest(kb, "Cinder Fortress", false, "s10_fortress", r => ok = r);
            Log($"{(ok ? "PASS" : "FAIL")}: Cinder Fortress (from orbit over it, down beside it, fortress buildings and ground forces, take off, garbage collected)");
            results.Add(("Cinder Fortress", ok));
            hull.cannotDie = false;

            // restart: the garbage is collected before the level opens again, and once it is closed
            int gcBefore = SceneMemory.Collections;
            DontDestroyOnLoad(gameObject);
            var oldDemo = demo;
            var menu = FindAnyObjectByType<GameMenu>();
            if (menu != null) menu.Restart();
            float waitUntil = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < waitUntil)
            {
                demo = FindAnyObjectByType<CarrierDemoInput>();
                if (demo != null && demo != oldDemo) break;
                yield return null;
            }
            yield return new WaitForSeconds(1f);
            int gcRestart = SceneMemory.Collections - gcBefore;
            ok = demo != null && demo != oldDemo && gcRestart >= 2;
            Log($"restart: new level {demo != null && demo != oldDemo}, garbage collected {gcRestart}x (last: '{SceneMemory.LastReason}')");
            Log($"{(ok ? "PASS" : "FAIL")}: garbage collected when the level is closed and before it is opened again");
            results.Add(("restart", ok));

            int passed = results.Count(r => r.Item2);
            Log(passed == results.Count && allKinds
                ? $"PASS: installations ({passed} of {results.Count}: stations, installations, orbital weapons, asteroid bases, planetary bases, Lagrange points)"
                : $"FAIL: installations ({passed} of {results.Count} passed: failed {string.Join(", ", results.Where(r => !r.Item2).Select(r => r.Item1))}; every kind present {allKinds})");
        }

        // ------------------------------------------------------------------ mining

        /// <summary>Open the map and warp to a place by name (Tab to it, Enter).</summary>
        IEnumerator WarpTo(Keyboard kb, string name, string shot)
        {
            if (!SystemMap.IsOpen) yield return Key(kb, UnityEngine.InputSystem.Key.M);
            for (int i = 0; i < 60 && map.Selected?.displayName != name; i++) yield return Key(kb, UnityEngine.InputSystem.Key.Tab);
            if (map.Selected?.displayName != name)
            {
                Log($"FAIL: couldn't choose {name} on the map (map open {SystemMap.IsOpen}, selected {map.Selected?.displayName}, menu open {GameMenu.IsOpen}, " +
                    $"destroyed {cc.Destroyed}, landing {PlanetLanding.Instance?.Current}, docking {Docking.Instance?.Current}, warp {drive.Current})");
                if (SystemMap.IsOpen) yield return Key(kb, UnityEngine.InputSystem.Key.Escape);
                yield break;
            }
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

        /// <summary>Scan (V) and wait for the sweep; returns the nearest find (not mined out).
        /// With `sweepShot`, a picture of the pulse halfway out, from above and looking away
        /// from the star (its glare would hide the rings).</summary>
        IEnumerator ScanAndLog(Keyboard kb, MineralSensor sensor, string where, Action<IMineralSource> nearest, string sweepShot = null)
        {
            yield return Key(kb, UnityEngine.InputSystem.Key.V);
            bool started = sensor.Sweeping;
            var oc = demo.orbitCamera;
            if (sweepShot != null && oc != null && started)
            {
                float d0 = oc.distance, p0 = oc.pitch, y0 = oc.yaw;
                var star = Point("Kestrel");
                Vector3 away = star != null ? cc.transform.position - star.Position : cc.transform.forward;
                oc.yaw = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;
                oc.pitch = 32f;
                oc.distance = demo.ShipViewDistance * 6f;
                while (sensor.Sweeping && sensor.SweepProgress < 0.45f) yield return null;
                Shot(sweepShot);
                Log($"  {sweepShot}: the pulse {sensor.SweepProgress * sensor.ScanRange / 1000f:F1} km out, rings drawn {sensor.ringFlat != null && sensor.ringFlat.enabled}");
                yield return null;
                oc.distance = d0; oc.pitch = p0; oc.yaw = y0;
            }
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
            yield return ScanAndLog(kb, sensor, "the Inner Belt", s => src = s, "n1_sweep");
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
                // from high above the comet, looking down on its whole length: the coma, the
                // straight ion tail and the dust tail curving back along the orbit
                Vector3 look = -(comet.tails.up * 60000f - comet.tails.right * 8000f);
                var target0 = oc.target;
                Vector3 offset0 = oc.offset;
                oc.target = comet.transform;
                oc.offset = comet.tails.forward * 20000f;
                oc.yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                oc.pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
                oc.distance = look.magnitude;
                yield return new WaitForSeconds(0.8f);
                Shot("n5_comet_tails");
                yield return null;
                oc.target = target0;
                oc.offset = offset0;
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
                yield return ScanAndLog(kb, sensor, "Ochre's surface", s => src = s, "n7_sweep");
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
            Log($"landing: near {WarpPoint.Nearest(cc.transform.position)?.displayName}; HUD '{Plain(landing.HudText())}'");
            yield return Key(kb, UnityEngine.InputSystem.Key.K);
            yield return WaitLanding(landing, PlanetLanding.State.OnSurface, 15f);
            var site = landing.Site;
            if (site == null) { Log("FAIL: landing (K did not land)"); yield break; }
            // where it should come back to on take-off: the same place over the body it landed on
            var orbitBody = landing.Body;
            Vector3 orbit = landing.OrbitOffset;
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
                          !RenderSettings.fog && orbitBody != null && Vector3.Distance(cc.transform.position - orbitBody.Position, orbit) < 3000f;
            Log($"  take off (K): back in space {landing.Current == PlanetLanding.State.InSpace}, " +
                $"{(orbitBody != null ? Vector3.Distance(cc.transform.position - orbitBody.Position, orbit) : -1f):F0} m from where it left over {orbitBody?.displayName}, " +
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
                    // up close to the nearest battery (or whatever there is) that can see the frigate
                    // from somewhere 800 m off (hills and buildings are cover), and see it shoot
                    var candidates = new List<(GroundUnit u, float d)>();
                    foreach (var g in landing.Surface.Garrisons)
                        foreach (var u in g.units)
                        {
                            if (u == null || !u.Alive || u.GetComponent<CombatPilot>() != null) continue;
                            candidates.Add((u, (u.transform.position - cc.transform.position).sqrMagnitude * (u.name == "Ground Battery" ? 0.25f : 1f)));
                        }
                    GroundUnit near = null;
                    Vector3 at = Vector3.zero;
                    int tried = 0;
                    foreach (var (u, _) in candidates.OrderBy(c => c.d))
                    {
                        if (tried++ >= 6) break;
                        var tu = u.GetComponentInChildren<CarrierTurret>();
                        Vector3 eye = (tu != null ? tu.transform.position : u.transform.position) + Vector3.up * 6f;
                        foreach (float up in new[] { 220f, 120f, 400f })
                            for (int k = 0; k < 12 && near == null; k++)
                            {
                                float ang = (215f + k * 30f) * Mathf.Deg2Rad;
                                Vector3 p = u.transform.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 790f;
                                p.y = Mathf.Max(landing.Surface.FloorAt(p), u.transform.position.y) + up;
                                if (tu != null ? tu.CanBear(p) : Cover.Visible(eye, p)) { near = u; at = p; }
                            }
                        if (near != null) break;
                    }
                    if (near == null && candidates.Count > 0)
                    {
                        near = candidates.OrderBy(c => c.d).First().u;
                        at = near.transform.position + new Vector3(-650f, 0f, -450f);
                        at.y = Mathf.Max(landing.Surface.FloorAt(at), near.transform.position.y) + 220f;
                        Log($"  (no unit of the nearest {tried} had a clear view from 800 m)");
                    }
                    if (near != null)
                    {
                        var turret = near.GetComponentInChildren<CarrierTurret>();
                        var weapons = near.GetComponent<EnemyWeapons>();
                        int shots0 = turret != null ? turret.ShotsFired : 0;
                        float hull1 = health.Current, shield1 = shield != null ? shield.Strength : 0f;

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
                        Log($"  close to a {near.name} ({Vector3.Distance(near.transform.position, cc.transform.position):F0} m, in sight {Cover.Visible(near.transform.position + Vector3.up * 8f, cc.transform.position)}, " +
                            $"gun can bear {(turret != null && turret.CanBear(cc.transform.position))}, " +
                            $"garrison alert {near.garrison != null && near.garrison.Alert}, turret target {(turret != null && turret.target != null ? turret.target.name : "none")}): it fired {(turret != null ? turret.ShotsFired - shots0 : -1)} shots" +
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
            if (drive.Current != WarpDrive.State.Charging)
            {
                Log($"FAIL: {how} did not start a warp to {target?.displayName} ({drive.LastMessage})");
                if (SystemMap.IsOpen) map.Close();
                yield break;
            }
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

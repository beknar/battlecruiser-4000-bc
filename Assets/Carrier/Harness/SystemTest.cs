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
            // in a web build: open the page with ?systemtest, and the results go to the browser console
            bool web = LaunchArgs.PageHas("systemtest");
            if (!LaunchArgs.Has("-systemtest") && !web) return;
            var t = new GameObject("SystemTest").AddComponent<SystemTest>();
            t.web = web;
            t.dir = LaunchArgs.FolderAfter("-systemtest", Application.persistentDataPath);
            // -landtest (or ?landtest): only the landings, on every kind of world
            t.landOnly = LaunchArgs.Switch("landtest");
            // -landworld <name> (or ?landworld=<name>, spaces as _): after Thalassa, only that world
            t.landWorld = (LaunchArgs.PageValue("landworld") ?? LaunchArgs.ValueOf("-landworld"))?.Replace('_', ' ') ?? t.landWorld;
            // -minetest (or ?minetest): only the mining
            t.mineOnly = LaunchArgs.Switch("minetest");
            // -minesurface: only the vein on a planet (quick)
            t.mineSurfaceOnly = LaunchArgs.Has("-minesurface");
            if (t.mineSurfaceOnly) t.mineOnly = true;
            // -stationtest (or ?stationtest): only the stations, bases and installations
            t.stationOnly = LaunchArgs.Switch("stationtest");
            // -orbittest (or ?orbittest): only the orbits and the launchers
            t.orbitOnly = LaunchArgs.Switch("orbittest");
            // -hudtest (or ?hudtest): only the ship's console and the captain
            t.hudOnly = LaunchArgs.Switch("hudtest");
            // -shiptest (or ?shiptest), with -frigate <id> (?frigate=<id>): only the frigate
            t.shipOnly = LaunchArgs.Switch("shiptest");
            // -watertest (or ?watertest): only the sea (Pelagia: afloat, under, out, the sea forces)
            t.waterOnly = LaunchArgs.Switch("watertest");
            // -industrytest (or ?industrytest): only the refineries and factories
            t.industryOnly = LaunchArgs.Switch("industrytest");
            // -crewtest (or ?crewtest): only the crew and the settlements
            t.crewOnly = LaunchArgs.Switch("crewtest");
            // -streamtest (or ?streamtest): only the streaming and the world's memory
            t.streamOnly = LaunchArgs.Switch("streamtest");
            // -approachtest (or ?approachtest): only the real-size worlds and flying down to them
            t.approachOnly = LaunchArgs.Switch("approachtest");
            // -striketest (or ?striketest): only the strikes from orbit
            t.strikeOnly = LaunchArgs.Switch("striketest");
            // -cooptest (or ?cooptest), with -connect address:port (?server=...&connect): co-op with
            // the scripted second player (Tools/CoopTest/bot.py); -coopquick (?coopquick) skips the
            // part at Corsair Haven
            t.coopQuick = LaunchArgs.Switch("coopquick");
            t.coopOnly = t.coopQuick || LaunchArgs.Switch("cooptest");
        }

        bool coopQuick;

        bool web, landOnly, mineOnly, mineSurfaceOnly, stationOnly, orbitOnly;
        string landWorld;

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
            // the test's own keyboard and pad go on working when the window loses focus (a console
            // or a notification popping up mid-test used to swallow a key press)
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var kb = InputSystem.AddDevice<Keyboard>("SystemTestKeyboard");
            if (coopOnly)
            {
                yield return CoopTest(kb, coopQuick);
                Log("done");
                if (!web) Application.Quit();
                yield break;
            }
            if (strikeOnly)
            {
                yield return StrikeTest(kb);
                Log("done");
                if (!web) Application.Quit();
                yield break;
            }
            if (approachOnly)
            {
                yield return ApproachTest(kb);
                Log("done");
                if (!web) Application.Quit();
                yield break;
            }
            if (streamOnly)
            {
                yield return StreamTest(kb);
                Log("done");
                if (!web) Application.Quit();
                yield break;
            }
            if (crewOnly)
            {
                yield return CrewTest(kb);
                Log("done");
                if (!web) Application.Quit();
                yield break;
            }
            if (industryOnly)
            {
                yield return IndustryTest(kb);
                Log("done");
                if (!web) Application.Quit();
                yield break;
            }
            if (waterOnly)
            {
                yield return WaterTest(kb);
                Log("done");
                if (!web) Application.Quit();
                yield break;
            }
            if (shipOnly)
            {
                yield return ShipTest(kb);
                Log("done");
                if (!web) Application.Quit();
                yield break;
            }
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
            // ten days on: the planets have moved, the way in from Thalassa has turned, and the
            // battle group must have moved onto it to be met there
            var orbitClock = OrbitalSystem.Instance;
            if (orbitClock != null) orbitClock.ContinueFrom(orbitClock.Time + 10.0 * 86400.0);
            yield return new WaitForSeconds(0.5f);
            var post = fleet != null ? fleet.GetComponent<PostedOnApproach>() : null;
            int moves0 = post != null ? post.Moves : 0;
            yield return Key(kb, UnityEngine.InputSystem.Key.M);
            for (int i = 0; i < 60 && map.Selected?.displayName != "Glacis"; i++) yield return Key(kb, UnityEngine.InputSystem.Key.Tab);
            yield return Warp(() => Key(kb, UnityEngine.InputSystem.Key.Enter), "keyboard Enter", "c0", false);
            float dreadKm = dread != null ? Vector3.Distance(cc.transform.position, dread.transform.position) / 1000f : -1f;
            if (dread != null)
                Log($"  the dreadnought is {dreadKm:F1} km away, " +
                    $"{Vector3.Angle(cc.transform.forward, dread.transform.position - cc.transform.position):F0} deg off the bow");
            bool onTheWayIn = post != null && post.Moves > moves0 && dreadKm > 0f && dreadKm < 15f;
            Log(onTheWayIn ? $"PASS: capital ships on the way in, ten days on ({dreadKm:F1} km ahead)"
                           : $"FAIL: capital ships on the way in, ten days on (the dreadnought {dreadKm:F1} km off; moved {(post != null ? post.Moves - moves0 : 0)}x)");
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
            foreach (var e in fleet != null ? fleet.pilots.Where(pl => pl != null).ToArray() : new CombatPilot[0])
            {
                Log($"  {e.name}: {Vector3.Distance(e.transform.position, cc.transform.position) / 1000f:F1} km off, {e.Current}, alive {e.Alive}");
                var guns = e.GetComponentsInChildren<CarrierTurret>();
                Log($"    turrets bearing on the frigate: {guns.Count(t => t.CanBear(cc.transform.position))} of {guns.Length}");
            }
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

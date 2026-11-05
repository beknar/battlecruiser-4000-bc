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
            bool web = CarrierDemoInput.OnWeb && HasQueryKey(Application.absoluteURL, "systemtest");
            if (i < 0 && !web) return;
            var t = new GameObject("SystemTest").AddComponent<SystemTest>();
            t.web = web;
            t.dir = i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Application.persistentDataPath;
            // -landtest (or ?landtest): only the landings, on every kind of world
            t.landOnly = Array.IndexOf(args, "-landtest") >= 0 || (web && HasQueryKey(Application.absoluteURL, "landtest"));
        }

        bool web, landOnly;

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
                $"{WarpPoint.All.Count(w => w.kind == WarpPoint.Kind.Moon)} moons, {WarpPoint.All.Count(w => w.kind == WarpPoint.Kind.AsteroidField)} asteroid fields");
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

            // the edge of the area
            cc.transform.position = site.center + new Vector3(2700f, site.maxHeight - site.center.y + 200f, 0f);
            yield return null;
            yield return null;
            float edgeX = cc.transform.position.x - site.center.x;
            bool edgeOk = edgeX <= landing.bounds + 1f;
            Log($"  edge: put 2700 m out, held at {edgeX:F0} m (limit {landing.bounds:F0})");

            yield return Key(kb, UnityEngine.InputSystem.Key.K);
            yield return WaitLanding(landing, PlanetLanding.State.InSpace, 10f);
            yield return new WaitForSeconds(0.5f);
            bool backOk = landing.Current == PlanetLanding.State.InSpace && landing.Site == null && RenderSettings.skybox == spaceSky &&
                          !RenderSettings.fog && Vector3.Distance(cc.transform.position, orbit) < 3000f;
            Log($"  take off (K): back in space {landing.Current == PlanetLanding.State.InSpace}, {Vector3.Distance(cc.transform.position, orbit):F0} m from where it left, " +
                $"space sky back {RenderSettings.skybox == spaceSky}, fog {RenderSettings.fog}, warp {(drive.CanWarp(out string lw) ? "ready" : lw)}");
            Log(siteOk && downOk && edgeOk && backOk ? "PASS: landing (land, touch down on the pad, edge, take off)"
                : $"FAIL: landing (site {siteOk}, touchdown {downOk}, edge {edgeOk}, back {backOk})");
            if (!all) yield break;

            int ok = 0, total = 0;
            foreach (var name in new[] { "Ochre", "Cinder", "Jovara", "Glacis", "Selene", "Steropes", "Inner Belt", "Jovara Trojans", "Thalassa" })
            {
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

        void Report(PlanetLanding landing, string how)
        {
            var site = landing.Site;
            var td = site.terrain != null ? site.terrain.terrainData : null;
            string terrain = td != null ? $"{td.size.x:F0} x {td.size.z:F0} m, {td.size.y:F0} m high" : "none";
            string sea = float.IsNegativeInfinity(site.seaLevel) ? "none" : $"{site.seaLevel - site.center.y:F0} m";
            Log($"  {how}: on {landing.Body?.displayName} ({site.kind}), seed {landing.Seed}, built in {landing.LastBuildSeconds * 1000f:F0} ms; " +
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

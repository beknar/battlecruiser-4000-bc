using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Records a one-minute combat reel from inside the game. Start the build with
    /// "-reel &lt;folder&gt; -screen-fullscreen 0 -screen-width 1920 -screen-height 1080".
    /// Game time is locked to 30 frames a second (Time.captureFramerate), so the result is smooth
    /// however long each frame takes to save. Every frame is written as a JPEG, and the game's own
    /// sound mix is captured in step with the frames (AudioRenderer) and saved as reel.wav.
    /// A short shot list moves the camera and gives the orders. Join the frames and the sound
    /// with ffmpeg afterwards. Nothing is captured from outside the game.
    /// </summary>
    public class DemoReel : MonoBehaviour
    {
        const int Fps = 30;
        float seconds = 60f;               // the asteroid ambush runs longer (see Start)
        const int Warmup = 45;            // frames to let shaders and the scene settle (not recorded)

        string dir;
        CarrierDemoInput demo;
        CarrierController cc;
        OrbitCamera cam;
        int frame, recorded, channels;
        readonly List<float> audio = new List<float>(48000 * 2 * 62);
        bool audioOk;
        string log = "";

        int Total => Mathf.RoundToInt(seconds * Fps);
        float T => (frame - Warmup) / (float)Fps;     // seconds into the reel

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Flag()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-reel") >= 0) CarrierDemoInput.ReelMode = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-reel");
            if (i < 0) return;
            var r = new GameObject("DemoReel").AddComponent<DemoReel>();
            r.dir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Application.persistentDataPath;
            Time.captureFramerate = Fps;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            demo = FindAnyObjectByType<CarrierDemoInput>();
            cc = demo != null ? demo.carrier : null;
            cam = demo != null ? demo.orbitCamera : null;
            if (cc == null || cam == null) { Log("no demo controller in scene"); Application.Quit(); yield break; }
            if (AmbushDirector.Instance != null) seconds = 76f;
            sysMap = FindAnyObjectByType<SystemMap>();
            if (sysMap != null) seconds = 220f;     // ends a few seconds after the cruiser dies (see SystemShots)
            Log($"screen {Screen.width}x{Screen.height}, gfx {SystemInfo.graphicsDeviceType}, audio {AudioSettings.outputSampleRate} Hz {AudioSettings.speakerMode}, {seconds:F0} s");

            var eof = new WaitForEndOfFrame();
            while (recorded < Total)
            {
                yield return eof;
                if (frame == Warmup - 1) StartAudio();
                if (frame < Warmup) continue;
                SaveFrame();
                CaptureAudio();
                recorded++;
                if (recorded % 300 == 0) Log($"{recorded} frames");
            }
            if (audioOk) AudioRenderer.Stop();
            WriteWav(Path.Combine(dir, "reel.wav"));
            Log($"done: {recorded} frames, audio {(audioOk ? audio.Count / channels / (float)AudioSettings.outputSampleRate : 0f):F1} s");
            Application.Quit();
        }

        // ------------------------------------------------------------------ shot list

        int shot = -1;
        Transform follow;

        void Update()
        {
            if (cc == null) return;
            frame++;
            if (frame < Warmup) return;
            float t = T;
            LogKills();
            if (sysMap != null) { SystemShots(t); return; }
            if (AmbushDirector.Instance != null) { AsteroidShots(t, AmbushDirector.Instance); return; }
            if (!cc.HasHangar) { FrigateShots(t); return; }

            if (Cue(0f))
            {
                cc.throttle = 0.35f;
                Shot(0, cc.transform, new Vector3(0f, 40f, 0f), 1500f, -150f, 12f, 7f);
            }
            if (Cue(1.0f)) demo.LaunchFighters();
            if (Cue(2.5f)) demo.LaunchDrones();
            if (shot == 0) cam.distance = Mathf.Lerp(1500f, 1000f, Smooth(t / 6.5f));

            // chase a fighter as the wing goes in
            if (Cue(6.5f))
            {
                demo.WingAttack();
                follow = PickCraft(Craft.Kind.Fighter);
                Shot(1, follow != null ? follow : cc.transform, Vector3.zero, 75f, 0f, 9f, 0f);
                if (follow != null) cam.yaw = follow.eulerAngles.y + 25f;
            }
            if (Cue(7f)) demo.FireMissiles();
            if (shot == 1) Chase();

            // the cruiser under fire, through its first kill
            if (Cue(9.5f)) Shot(2, demo.targets[0], Vector3.zero, 800f, 60f, 10f, 9f);
            if (shot == 2) cam.distance = Mathf.Lerp(800f, 650f, Smooth((t - 9.5f) / 6.5f));

            // main guns close up, swinging onto a drone
            if (Cue(16f))
            {
                Pick(1);
                var turret = cc.mainTurrets != null && cc.mainTurrets.Length > 0 ? cc.mainTurrets[0].transform : cc.transform;
                Shot(3, turret, new Vector3(0f, 10f, 0f), 170f, 200f, 18f, 12f);
            }
            if (Cue(17.5f) || Cue(20f) || Cue(22.5f)) demo.FireGuns();

            // drones hunted down by the wing
            if (Cue(25f))
            {
                demo.WingAttack();
                demo.FireMissiles();
                Shot(4, demo.targets[1], Vector3.zero, 150f, 0f, 8f, 16f);
            }
            if (Cue(26f)) demo.LaunchDropships();
            if (Cue(31f))
            {
                Pick(2);
                demo.WingAttack();
                demo.FireMissiles();
                Shot(5, demo.targets[2], Vector3.zero, 180f, 120f, -6f, -14f);
            }

            // back to the cruiser: everything at once, first from behind a fighter...
            if (Cue(37f))
            {
                Pick(0);
                demo.WingAttack();
                demo.FireMissiles();
                follow = PickCraft(Craft.Kind.Fighter);
                Shot(6, follow != null ? follow : cc.transform, Vector3.zero, 90f, 0f, 7f, 0f);
                if (follow != null) cam.yaw = follow.eulerAngles.y - 20f;
            }
            if (Cue(40f)) demo.FireMissiles();
            if (shot == 6) Chase();
            // ...then on the cruiser for the kill
            if (Cue(41f)) Shot(7, demo.targets[0], Vector3.zero, 700f, -40f, 6f, -10f);

            // the carrier's guns working over the respawned cruiser
            if (Cue(48f))
            {
                var turret = cc.mainTurrets != null && cc.mainTurrets.Length > 1 ? cc.mainTurrets[cc.mainTurrets.Length - 1].transform : cc.transform;
                Shot(8, turret, new Vector3(0f, 10f, 0f), 150f, 20f, 12f, -12f);
            }
            if (Cue(49.5f) || Cue(52f)) demo.FireGuns();

            // pull back for the ending
            if (Cue(54f)) Shot(9, cc.transform, new Vector3(0f, 40f, 0f), 1100f, 30f, 16f, 6f);
            if (shot == 9) cam.distance = Mathf.Lerp(1100f, 2800f, Smooth((t - 54f) / 6f));
        }

        // ------------------------------------------------------------------ frigate shot list

        int side = 1;
        bool bowOn;
        CarrierTurret ciwsCam;
        EnemyWeapons cruiserGuns;

        static float Bearing(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        /// <summary>The frigate's weapons and shield: cannon shells and missiles on the shield,
        /// a CIWS mount shooting a drone, the cruiser's beam on the shield, the bow gun charging,
        /// firing and killing the cruiser, then everything at once on a drone. The side guns hold
        /// fire until the finale so the cruiser is whole when the bow gun fires.</summary>
        void FrigateShots(float t)
        {
            float d = demo.ShipViewDistance;
            var cruiser = demo.targets[0];
            if (cruiserGuns == null && cruiser) cruiserGuns = cruiser.GetComponent<EnemyWeapons>();
            Vector3 me = cc.transform.position;

            // steering: bow onto the target for the bow gun, otherwise keep a broadside
            if (bowOn && cc.target)
            {
                var v = ShipAutopilot.BowOn(cc, cc.target.position);
                demo.autoRudder = v.x;
                demo.autoPitch = v.y;
            }
            else if (cc.target) demo.autoRudder = ShipAutopilot.Broadside(cc, cc.target.position, side, 1300f);

            // 0-6: the frigate under way, its shield shimmering
            if (Cue(0f))
            {
                cc.throttle = 0.35f;
                SideGuns(false);
                Shot(0, cc.transform, demo.ShipViewOffset, d * 1.7f, 150f, 14f, -8f);
            }
            if (shot == 0) cam.distance = Mathf.Lerp(d * 1.7f, d * 1.1f, Smooth(t / 6f));

            // 6-14: the cruiser's cannon shells and missiles on the shield, seen from its side
            if (Cue(6f)) Shot(1, cc.transform, demo.ShipViewOffset, d * 1.25f, Bearing(cruiser.position, me) + 40f, 10f, 0f);
            if (shot == 1) cam.yaw = Mathf.LerpAngle(cam.yaw, Bearing(cruiser.position, me) + 40f, Time.deltaTime);
            if (Cue(5.5f) || Cue(9f)) cruiserGuns?.FireBurstNow();
            if (Cue(5f)) cruiserGuns?.FireSalvoNow();

            // 14-22: the CIWS cluster on the side facing the nearest drone, seen from outboard and
            // ahead of it with the conning tower behind, the mounts swinging out to shoot
            if (Cue(14f))
            {
                ciwsSide = NearestDroneSide();
                ciwsCam = ClusterMount(ciwsSide);
                if (ciwsCam) Shot(2, ciwsCam.transform, new Vector3(0f, 3f, 0f), 30f, cc.Heading + 180f + ciwsSide * 50f, 22f, 0f);
                Log($"  CIWS shot on the {(ciwsSide > 0 ? "starboard" : "port")} cluster ({(ciwsCam ? ciwsCam.Label : "none")})");
            }
            if (shot == 2)
            {
                cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 180f + ciwsSide * 50f, Time.deltaTime * 2f);
                // if this cluster sits idle while the other one is shooting, cut across to it
                if (!switchedCluster && !ClusterBusy(ciwsSide) && ClusterBusy(-ciwsSide))
                {
                    switchedCluster = true;
                    ciwsSide = -ciwsSide;
                    ciwsCam = ClusterMount(ciwsSide);
                    if (ciwsCam) Shot(2, ciwsCam.transform, new Vector3(0f, 3f, 0f), 30f, cc.Heading + 180f + ciwsSide * 50f, 22f, 0f);
                    Log($"  CIWS shot cut to the {(ciwsSide > 0 ? "starboard" : "port")} cluster");
                }
            }
            if (Cue(16f) || Cue(18f) || Cue(20f) || Cue(21.9f)) Log($"  CIWS engaging: {CiwsStatus()}");

            // 22-30: the cruiser's beam on the shield, while the frigate swings its bow round
            if (Cue(22f))
            {
                bowOn = true;
                Pick(0);
                bool charging = cruiserGuns != null && cruiserGuns.ChargeBeamNow();
                Log($"  cruiser beam charging: {charging}");
                Shot(3, cc.transform, demo.ShipViewOffset, d * 1.15f, Bearing(me, cruiser.position) + 35f, 9f, 0f);
            }
            if (shot == 3) cam.yaw = Mathf.LerpAngle(cam.yaw, Bearing(me, cruiser.position) + 35f, Time.deltaTime);

            // 30-46: the bow gun: charging (close on the bow), firing (chase cam), hitting (on the cruiser)
            if (Cue(30f))
            {
                demo.ChargeMainGun();
                var gun = demo.MainGun;
                if (gun) Shot(4, gun.transform, Vector3.zero, 75f, cc.Heading + 150f, 8f, 0f);
            }
            if (shot == 4) cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 150f, Time.deltaTime * 2f);
            if (Cue(36f)) Shot(5, cc.transform, demo.ShipViewOffset, d * 0.85f, cc.Heading, 9f, 0f);
            if (shot == 5) FollowHeading(0f, 9f);
            if (Cue(40.6f)) Shot(6, cruiser, Vector3.zero, 700f, Bearing(me, cruiser.position) + 70f, 6f, 0f);

            // 46-54: everything on a drone: side guns, CIWS, and its blasters on the shield
            if (Cue(46f))
            {
                bowOn = false;
                demo.autoPitch = null;
                demo.LevelOff();
                Pick(1);
                SideGuns(true);
                side = 1;
                cc.throttle = 0.6f;
                Shot(7, cc.transform, demo.ShipViewOffset, d * 1.2f, cc.Heading - 120f, 18f, 9f);
            }

            // 54-60: pull back
            if (Cue(54f)) Shot(8, cc.transform, demo.ShipViewOffset, d * 1.2f, cc.Heading + 60f, 16f, 6f);
            if (shot == 8) cam.distance = Mathf.Lerp(d * 1.2f, d * 3.5f, Smooth((t - 54f) / 6f));

            if (Cue(59.5f)) ReportSystems();
        }

        // ------------------------------------------------------------------ star system shot list

        SystemMap sysMap;
        WarpDrive sysDrive;
        EncounterGroup pirates;
        Transform pirateCruiser;
        int sysPhase;
        float phaseAt, nextChargeAt = -1f, nextCut;
        bool arrivedLogged;

        void Phase(int p, float t)
        {
            sysPhase = p;
            phaseAt = t;
            visitStarted = leaving = false;
            landedAt = -1f;
        }

        PlanetLanding landing;
        bool visitStarted, leaving;
        float visitAt, landedAt = -1f;

        /// <summary>A landing: wait out the descent, fly the site (to the pad and down onto it, or
        /// just across it), take off, and on to `next` once back in orbit.</summary>
        void SurfaceVisit(float t, float d, int b, bool setDown, float stay, int next)
        {
            if (landing == null) { Phase(next, t); return; }
            if (leaving)
            {
                if (landing.Current == PlanetLanding.State.InSpace) Phase(next, t);
                return;
            }
            if (landing.Current != PlanetLanding.State.OnSurface) return;   // still coming down
            var site = landing.Site;
            Vector3 me = cc.transform.position;
            Vector3 to = site.pad - me;
            to.y = 0f;
            float dist = to.magnitude;
            if (!visitStarted)
            {
                visitStarted = true;
                visitAt = t;
                Log($"{T,5:F1}s   on the surface of {landing.Body?.displayName}: {site.description} (built in {landing.LastBuildSeconds * 1000f:F0} ms), {dist:F0} m from the pad");
                Shot(b, cc.transform, demo.ShipViewOffset, d * 1.5f, cc.Heading + 150f, 11f, 2f);
            }
            float s = t - visitAt;
            var v = ShipAutopilot.BowOn(cc, site.pad + Vector3.up * (me.y - site.pad.y));
            demo.autoRudder = dist > 60f ? v.x : 0f;
            demo.autoPitch = 0f;
            if (setDown)
            {
                // come in over the pad, dropping to about 90 m above it, stop over it, then set down
                cc.throttle = dist > 380f ? 0.6f : dist > 160f ? 0.25f : dist > 70f ? 0.1f : 0f;
                bool over = dist < 110f && cc.Speed < 14f;
                float above = me.y - site.pad.y;
                demo.autoElevator = over ? -1f : above > 130f ? -0.7f : 0f;
                // in close: low and to the side as it comes down onto the pad
                if (shot == b && dist < 430f) Shot(b + 1, cc.transform, demo.ShipViewOffset, d * 1.2f, cc.Heading + 75f, 6f, 1.5f);
                if (landing.Landed && landedAt < 0f)
                {
                    landedAt = t;
                    Log($"{T,5:F1}s   touchdown {(landing.OnPad ? "on the pad" : "")} after {s:F1} s on the surface");
                    Shot(b + 2, cc.transform, demo.ShipViewOffset, d * 1.9f, cc.Heading - 140f, 18f, 5f);
                }
            }
            else
            {
                cc.throttle = 0.32f;
                demo.autoElevator = 0f;
                // the platform as the frigate passes over it
                var outpost = site.root.transform.Find("Outpost");
                if (shot == b && s > 4f && outpost != null) Shot(b + 1, outpost, Vector3.zero, 700f, Bearing(outpost.position, me) + 40f, 12f, 3f);
            }
            bool done = setDown ? landedAt > 0f && t - landedAt > 4.5f : s > stay;
            if (done || s > 45f)
            {
                demo.autoRudder = null; demo.autoPitch = null; demo.autoElevator = null;
                cc.throttle = 0.15f;
                Log($"{T,5:F1}s   taking off from {landing.Body?.displayName}");
                landing.TakeOff();
                leaving = true;
            }
        }

        WarpPoint WP(string name) => WarpPoint.All.FirstOrDefault(w => w.displayName == name);

        /// <summary>The frigate in orbit over Thalassa lands: it flies in over the island and sets
        /// down on the landing pad, then takes off. It opens the system map, browses it and warps
        /// to the gas giant Jovara, drops to the cloud tops, sets down on the floating mining
        /// platform there and climbs back out; opens the map again and warps across the system to
        /// the Outer Drift, where a pirate cruiser and its drones ambush it from the rocks; it fights
        /// the drones off with its CIWS and side guns and turns its bow gun on the cruiser until it
        /// is destroyed.</summary>
        void SystemShots(float t)
        {
            float d = demo.ShipViewDistance;
            if (sysDrive == null) sysDrive = cc.GetComponent<WarpDrive>();
            if (landing == null) landing = PlanetLanding.Instance;
            if (pirates == null)
            {
                pirates = GameObject.Find("Pirates")?.GetComponent<EncounterGroup>();
                pirateCruiser = pirates != null ? pirates.pilots.FirstOrDefault(pl => pl != null && pl.style == CombatPilot.Style.Capital)?.transform : null;
            }
            float u = t - phaseAt;   // time in the current phase

            switch (sysPhase)
            {
                case 0:   // in orbit over Thalassa
                    if (Cue(0f))
                    {
                        cc.throttle = 0.15f;
                        SideGuns(false);
                        Shot(0, cc.transform, demo.ShipViewOffset, d * 1.7f, cc.Heading + 150f, 6f, 3f);
                    }
                    if (Cue(3.5f))
                    {
                        bool down = landing != null && landing.Land();
                        Log($"{T,5:F1}s   landing on Thalassa: {(down ? "descending" : "refused")}");
                        Phase(down ? 7 : 8, t);
                    }
                    break;

                case 7:   // Thalassa: in over the island and down onto the pad, then back up
                    SurfaceVisit(t, d, 70, true, 0f, 8);
                    break;

                case 8:   // back in orbit: the map, and on to Jovara
                    if (Cue(phaseAt + 0.05f)) Shot(80, cc.transform, demo.ShipViewOffset, d * 1.6f, cc.Heading + 150f, 6f, 3f);
                    if (Cue(phaseAt + 1.6f)) { sysMap.Open(); Log($"{T,5:F1}s   map opened (selected {sysMap.Selected?.displayName})"); }
                    if (Cue(phaseAt + 2.7f)) sysMap.Select(WP("Steropes"));
                    if (Cue(phaseAt + 3.7f)) sysMap.Select(WP("Brontes"));
                    if (Cue(phaseAt + 4.7f)) sysMap.Select(WP("Jovara"));
                    if (Cue(phaseAt + 6.3f)) Warp(t, 1);
                    break;

                case 9:   // Jovara: down to the cloud tops and onto the floating mining platform, back up
                    SurfaceVisit(t, d, 90, true, 0f, 10);
                    break;

                case 10:  // back over Jovara: the map again
                    if (Cue(phaseAt + 0.05f)) Shot(100, cc.transform, demo.ShipViewOffset, d * 1.6f, cc.Heading + 25f, 8f, 2f);
                    if (Cue(phaseAt + 2f)) { sysMap.Open(); Phase(3, t); }
                    break;

                case 1:   // the first jump: charging, the jump, the tunnel
                case 4:
                    WarpShots(t, u, d);
                    if (sysDrive.Current == WarpDrive.State.Ready && u > 2f) Arrived(t, sysPhase == 1 ? 2 : 5);
                    break;

                case 2:   // at Jovara: a look at the gas giant and its moons
                    if (Cue(phaseAt + 0.1f)) Shot(10, cc.transform, demo.ShipViewOffset, d * 1.6f, cc.Heading + 25f, 8f, 0f);
                    if (sysPhase == 2) FollowHeading(25f, 8f);
                    if (Cue(phaseAt + 4.5f))
                    {
                        bool down = landing != null && landing.Land();
                        Log($"{T,5:F1}s   landing on Jovara: {(down ? "descending" : "refused")}");
                        if (down) Phase(9, t);
                        else { sysMap.Open(); Phase(3, t); }
                    }
                    break;

                case 3:   // the map again: on to the Outer Drift
                    if (Cue(phaseAt + 1.0f)) sysMap.Select(WP("Arges"));
                    if (Cue(phaseAt + 2.0f)) sysMap.Select(WP("Kestrel"));
                    if (Cue(phaseAt + 3.0f)) sysMap.Select(WP("Outer Drift"));
                    if (Cue(phaseAt + 4.6f)) Warp(t, 4);
                    break;

                case 5:   // dropped out at the Outer Drift: the ambush
                    if (Cue(phaseAt + 0.1f)) Shot(20, cc.transform, demo.ShipViewOffset, d * 1.8f, cc.Heading + 140f, 10f, 4f);
                    if (Cue(phaseAt + 2.5f))
                    {
                        cc.throttle = 0.5f;
                        // for the reel the cruiser stands and fights rather than retreating behind a rock
                        var cp = pirateCruiser ? pirateCruiser.GetComponent<CombatPilot>() : null;
                        if (cp) cp.hideBelow = 0.15f;
                        if (pirates != null && !pirates.Active)
                        {
                            Log($"{T,5:F1}s   pirates not in sight yet: they break cover");
                            pirates.Spring();
                        }
                        Log($"{T,5:F1}s   AMBUSH at the Outer Drift; warp drive now: {(sysDrive.InCombat(out string why) ? "locked, " + why : "ready")}");
                    }
                    if (Cue(phaseAt + 3.5f) && pirateCruiser) Shot(21, pirateCruiser, Vector3.zero, 1100f, Bearing(pirateCruiser.position, cc.transform.position) + 35f, 9f, 3f);
                    if (Cue(phaseAt + 9.5f))
                    {
                        // CIWS and side guns on the drones
                        var drone = CombatPilot.Nearest(pirates.pilots.Where(pl => pl != null && pl.style == CombatPilot.Style.Fighter), cc.transform.position);
                        if (drone) demo.SelectTarget(drone.transform);
                        SideGuns(true);
                        ciwsSide = NearestDroneSide();
                        ciwsCam = ClusterMount(ciwsSide);
                        if (ciwsCam) Shot(22, ciwsCam.transform, new Vector3(0f, 3f, 0f), 30f, cc.Heading + 180f + ciwsSide * 50f, 22f, 0f);
                    }
                    if (shot == 22) cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 180f + ciwsSide * 50f, Time.deltaTime * 2f);
                    if (Cue(phaseAt + 15.5f)) Shot(23, cc.transform, demo.ShipViewOffset, d * 1.5f, cc.Heading - 120f, 14f, 5f);
                    // turn the bow onto the cruiser and close in
                    if (u > 15.5f && pirateCruiser)
                    {
                        if (nextChargeAt < float.MaxValue) cc.throttle = 1f;     // close in fast
                        // while closing, cut between the cruiser and the frigate
                        if (nextChargeAt < float.MaxValue && u > 21.5f && t >= nextCut)
                        {
                            nextCut = t + 7f;
                            bool onCruiser = shot != 28;
                            if (onCruiser) Shot(28, pirateCruiser, Vector3.zero, 1000f, Bearing(pirateCruiser.position, cc.transform.position) - 30f, 8f, 4f);
                            else Shot(23, cc.transform, demo.ShipViewOffset, d * 1.3f, cc.Heading + 150f, 10f, 4f);
                        }
                        if (cc.target != pirateCruiser) demo.SelectTarget(pirateCruiser);
                        var v = ShipAutopilot.BowOn(cc, pirateCruiser.position);
                        demo.autoRudder = v.x;
                        demo.autoPitch = v.y;
                        if (Cue(phaseAt + 16f)) { Log($"{T,5:F1}s   closing on the cruiser ({Vector3.Distance(cc.transform.position, pirateCruiser.position) / 1000f:F1} km)"); nextChargeAt = t + 3f; }
                    }
                    // the bow gun: charge, fire, again if it's still flying
                    var gun = demo.MainGun;
                    var ch = pirateCruiser ? pirateCruiser.GetComponent<Health>() : null;
                    if (gun && ch && !ch.Dead && nextChargeAt > 0f && t >= nextChargeAt && gun.Current == MainBeam.State.Idle)
                    {
                        float km = Vector3.Distance(cc.transform.position, pirateCruiser.position) / 1000f;
                        if (km < 5.8f && Cover.Visible(cc.transform.position, pirateCruiser.position))
                        {
                            demo.ChargeMainGun();
                            nextChargeAt = float.MaxValue;
                            Log($"{T,5:F1}s   bow gun charging on the cruiser: {km:F1} km, {ShipAutopilot.BowError(cc, pirateCruiser.position):F0} deg off the bow, cruiser hull {ch.Current:F0}");
                            Shot(24, gun.transform, Vector3.zero, 75f, cc.Heading + 150f, 8f, 0f);
                        }
                        else nextChargeAt = t + 0.5f;
                    }
                    if (shot == 24) cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 150f, Time.deltaTime * 2f);
                    if (gun && gun.Current == MainBeam.State.Charging && gun.ChargeTimeLeft < 3.5f && shot == 24)
                        Shot(25, cc.transform, demo.ShipViewOffset, d * 0.85f, cc.Heading, 9f, 0f);
                    if (shot == 25) FollowHeading(0f, 9f);
                    if (gun && gun.Current == MainBeam.State.Firing && shot == 25 && pirateCruiser)
                        Shot(26, pirateCruiser, Vector3.zero, 800f, Bearing(cc.transform.position, pirateCruiser.position) + 70f, 6f, 0f);
                    if (gun && gun.Current == MainBeam.State.Idle && nextChargeAt == float.MaxValue && ch && !ch.Dead)
                    {
                        Log($"{T,5:F1}s   the cruiser survived the beam (hull {ch.Current:F0}): charging again");
                        nextChargeAt = t + 1.5f;
                        Shot(23, cc.transform, demo.ShipViewOffset, d * 1.5f, cc.Heading - 120f, 14f, 5f);
                    }
                    if (ch && ch.Dead)
                    {
                        Log($"{T,5:F1}s   the pirate cruiser is destroyed");
                        demo.autoPitch = null;
                        Phase(6, t);
                    }
                    break;

                case 6:   // the end: pull back over the wreckage
                    if (Cue(phaseAt + 1.5f)) Shot(27, cc.transform, demo.ShipViewOffset, d * 1.4f, cc.Heading + 60f, 16f, 6f);
                    if (shot == 27) cam.distance = Mathf.Lerp(d * 1.4f, d * 3.5f, Smooth((t - phaseAt - 1.5f) / 5f));
                    if (Cue(phaseAt + 6.5f))
                    {
                        ReportSystems();
                        Log($"  warp trips: {sysDrive.Jumps}, last {sysDrive.LastTravelSeconds:F1} s over {sysDrive.LastDistance / 1000f:F0} km");
                        seconds = Mathf.Min(seconds, t + 0.5f);
                    }
                    break;
            }
        }

        void Warp(float t, int nextPhase)
        {
            var to = sysMap.Selected;
            float km = sysDrive.DistanceTo(to) / 1000f, eta = sysDrive.chargeTime + sysDrive.TravelTimeTo(to);
            bool ok = sysMap.WarpToSelected();
            Log($"{T,5:F1}s   warp to {to?.displayName}: {km:F0} km, {eta:F1} s ({(ok ? "engaged" : "refused: " + sysDrive.LastMessage)})");
            Phase(nextPhase, t);
            Shot(nextPhase * 10 + 1, cc.transform, demo.ShipViewOffset, d0 * 1.0f, cc.Heading + 110f, 8f, 0f);
            arrivedLogged = false;
        }

        float d0 => demo.ShipViewDistance;

        /// <summary>Charging from the side, the tunnel from behind, then from off the quarter.</summary>
        void WarpShots(float t, float u, float d)
        {
            int b = sysPhase * 10;
            if (sysDrive.Current == WarpDrive.State.Charging) cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 110f, Time.deltaTime * 2f);
            if (sysDrive.Current == WarpDrive.State.Warping && shot == b + 1) Shot(b + 2, cc.transform, demo.ShipViewOffset, d * 0.9f, cc.Heading, 6f, 0f);
            if (shot == b + 2)
            {
                FollowHeading(0f, 6f);
                if (sysDrive.StateTime > Mathf.Min(4f, sysDrive.TravelTime * 0.5f)) Shot(b + 3, cc.transform, demo.ShipViewOffset, d * 1.1f, cc.Heading + 60f, 10f, 0f);
            }
            if (shot == b + 3) FollowHeading(60f, 10f);
            if (sysDrive.Current == WarpDrive.State.Arriving && !arrivedLogged)
            {
                arrivedLogged = true;
                Log($"{T,5:F1}s   dropped out of warp at {sysDrive.Destination?.displayName} after {sysDrive.LastTravelSeconds:F1} s in the tunnel");
            }
        }

        void Arrived(float t, int next) => Phase(next, t);

        // ------------------------------------------------------------------ asteroid ambush shot list

        int wp = 1;
        float ambushT = -1f, minClearance = float.MaxValue;
        bool rounded;

        /// <summary>Steer along the route over the big asteroid and round its end.</summary>
        void FollowRoute(AmbushDirector amb)
        {
            var r = amb.route;
            if (r == null || r.Length < 2) return;
            Vector3 me = cc.transform.position;
            while (wp < r.Length - 1 && (Vector3.Distance(me, r[wp]) < 320f || Vector3.Dot(r[wp] - me, r[wp] - r[wp - 1]) < 0f)) wp++;
            var v = ShipAutopilot.BowOn(cc, r[wp]);
            demo.autoRudder = v.x;
            demo.autoPitch = v.y;
        }

        /// <summary>The frigate crosses the big asteroid with the travelling music, comes round its
        /// far end and is ambushed: fighters from under the rock (the music switches to the battle
        /// track), then the heavy cruiser and its drones from behind a boulder. The frigate fights
        /// back with its CIWS and side guns, then turns its bow gun on the cruiser.</summary>
        void AsteroidShots(float t, AmbushDirector amb)
        {
            float d = demo.ShipViewDistance;
            Vector3 me = cc.transform.position;
            var cruiser = demo.targets != null && demo.targets.Length > 0 ? demo.targets[0] : null;
            if (cruiserGuns == null && cruiser) cruiserGuns = cruiser.GetComponent<EnemyWeapons>();
            if (frame % 15 == 0 && !amb.Sprung) minClearance = Mathf.Min(minClearance, cc.ObstacleClearance(800f));

            if (bowOn && cc.target)
            {
                var v = ShipAutopilot.BowOn(cc, cc.target.position);
                demo.autoRudder = v.x;
                demo.autoPitch = v.y;
            }
            else FollowRoute(amb);

            // 0-7: under way over the asteroid, the gas giant ahead
            if (Cue(0f))
            {
                cc.throttle = 1f;
                SideGuns(false);
                Shot(0, cc.transform, demo.ShipViewOffset, d * 1.6f, cc.Heading + 25f, 7f, 0f);
            }
            if (shot == 0) { FollowHeading(25f, 7f); cam.distance = Mathf.Lerp(d * 1.6f, d * 1.15f, Smooth(t / 7f)); }

            // 7-13: low chase, the surface rushing by underneath
            if (Cue(7f)) Shot(1, cc.transform, demo.ShipViewOffset, d * 0.8f, cc.Heading, 3f, 0f);
            if (shot == 1) FollowHeading(0f, 3f);

            // 13 on: wide from the side as the end of the asteroid comes up
            if (Cue(13f)) Shot(2, cc.transform, demo.ShipViewOffset, d * 2.8f, cc.Heading - 70f, 18f, 0f);
            if (shot == 2) FollowHeading(-70f, 18f);

            // diving round the end, seen from below
            if (!rounded && wp >= amb.roundingIndex - 1 && !amb.Sprung)
            {
                rounded = true;
                Log($"{T,5:F1}s   rounding the end of the asteroid; closest approach to rock on the way across {minClearance:F0} m, collisions {cc.Collisions}");
                Shot(3, cc.transform, demo.ShipViewOffset, d * 1.5f, cc.Heading + 120f, -14f, 0f);
            }
            if (shot == 3) FollowHeading(120f, -14f);
            if (!amb.Sprung && t >= 34f) amb.Spring("the reel's fallback cue");

            // the ambush: the alarm on the frigate, then on one of the fighters coming out from under the rock
            if (amb.Sprung && ambushT < 0f)
            {
                ambushT = t;
                Log($"{T,5:F1}s   AMBUSH sprung ({amb.SprungBy})");
                cc.throttle = 0.45f;
                SideGuns(true);
            }
            if (ambushT < 0f) return;
            if (Cue(ambushT + 1.8f))
            {
                follow = NearestPilot(amb.firstWave);
                if (follow != null) Shot(4, follow, Vector3.zero, 90f, follow.eulerAngles.y + 20f, 8f, 0f);
                else Shot(4, cc.transform, demo.ShipViewOffset, d * 1.3f, cc.Heading + 140f, 10f, 0f);
            }
            float a = ambushT;
            if (shot == 4 && follow != null)
            {
                cam.yaw = Mathf.LerpAngle(cam.yaw, follow.eulerAngles.y + 18f, Time.deltaTime * 1.5f);
                cam.pitch = Mathf.Lerp(cam.pitch, 8f, Time.deltaTime);
            }

            // the CIWS cluster facing the fighters opens up
            if (Cue(a + 6f))
            {
                ciwsSide = NearestDroneSide();
                ciwsCam = ClusterMount(ciwsSide);
                if (ciwsCam) Shot(5, ciwsCam.transform, new Vector3(0f, 3f, 0f), 30f, cc.Heading + 180f + ciwsSide * 50f, 22f, 0f);
                Log($"  CIWS engaging: {CiwsStatus()}");
            }
            if (shot == 5) cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 180f + ciwsSide * 50f, Time.deltaTime * 2f);

            // the second wave: the heavy cruiser rounding its boulder
            if (Cue(a + 10.5f) && cruiser) Shot(6, cruiser, Vector3.zero, 950f, Bearing(cruiser.position, me) + 35f, 9f, 3f);
            if (Cue(a + 13f)) Log($"  second wave in: {amb.SecondWaveIn}; cruiser in sight of the frigate: {(cruiser ? Cover.Visible(cruiser.position, me) : false)}");

            // in the thick of it
            if (Cue(a + 16.5f)) Shot(7, cc.transform, demo.ShipViewOffset, d * 1.8f, cc.Heading + 130f, 14f, 5f);

            // the bow gun on the cruiser: charging, firing, hitting
            if (Cue(a + 22f) && cruiser)
            {
                bowOn = true;
                Pick(0);
                demo.ChargeMainGun();
                var gun = demo.MainGun;
                if (gun) Shot(8, gun.transform, Vector3.zero, 75f, cc.Heading + 150f, 8f, 0f);
                Log($"  bow gun charging on the cruiser ({ShipAutopilot.BowError(cc, cruiser.position):F0} deg off the bow, in sight: {Cover.Visible(me, cruiser.position)})");
            }
            if (shot == 8) cam.yaw = Mathf.LerpAngle(cam.yaw, cc.Heading + 150f, Time.deltaTime * 2f);
            if (Cue(a + 28f)) Shot(9, cc.transform, demo.ShipViewOffset, d * 0.85f, cc.Heading, 9f, 0f);
            if (shot == 9) FollowHeading(0f, 9f);
            if (Cue(a + 32.6f) && cruiser) Shot(10, cruiser, Vector3.zero, 700f, Bearing(me, cruiser.position) + 70f, 6f, 0f);

            // pull back over the battle
            if (Cue(a + 38f))
            {
                bowOn = false;
                demo.autoPitch = null;
                demo.LevelOff();
                var f = NearestPilot(amb.firstWave);
                if (f) demo.SelectTarget(f);
                Shot(11, cc.transform, demo.ShipViewOffset, d * 1.4f, cc.Heading + 60f, 16f, 6f);
            }
            if (shot == 11) cam.distance = Mathf.Lerp(d * 1.4f, d * 4f, Smooth((t - (a + 38f)) / 8f));

            if (Cue(seconds - 0.5f)) ReportAmbush(amb);
        }

        Transform NearestPilot(CombatPilot[] pilots) => CombatPilot.Nearest(pilots, cc.transform.position)?.transform;

        void ReportAmbush(AmbushDirector amb)
        {
            ReportSystems();
            var hull = cc.GetComponent<Health>();
            if (hull) Log($"  frigate hull {hull.Current:F0}/{hull.maxHealth:F0}, destroyed {hull.TimesDestroyed}x, collisions with rock {cc.Collisions}");
            foreach (var pl in amb.firstWave.Concat(amb.secondWave))
            {
                if (pl == null) continue;
                var h = pl.GetComponent<Health>();
                var w = pl.GetComponent<EnemyWeapons>();
                Log($"  {pl.name}: {pl.Current}, destroyed {(h ? h.TimesDestroyed : 0)}x, hid {pl.TimesHidden}x, shots held for cover {(w ? w.HeldForCover : 0)}");
            }
            Log($"  music at the end: {MusicDirector.NowPlaying} (combat mix {MusicDirector.CombatMix:F2})");
        }

        void SideGuns(bool on)
        {
            foreach (var g in cc.mainTurrets) if (g) g.autoFire = on;
        }

        int ciwsSide = 1;
        bool switchedCluster;

        bool ClusterBusy(int s)
        {
            var dir = cc.GetComponent<CiwsDirector>();
            if (dir == null) return false;
            foreach (var m in dir.mounts)
                if (m && m.target && cc.transform.InverseTransformPoint(m.transform.position).x * s > 0f) return true;
            return false;
        }

        /// <summary>+1 if the nearest drone or fighter within CIWS range (alive, or about to
        /// respawn) is to starboard, -1 if to port.</summary>
        int NearestDroneSide()
        {
            float best = float.MaxValue;
            int s = 1;
            foreach (var info in TargetInfo.All)
            {
                if (info == null || !info.IsSmallCraft) continue;
                var h = info.GetComponent<Health>();
                if (h != null && h.Dead && h.RespawnAt - Time.time > 4f) continue;
                float dist = Vector3.Distance(info.transform.position, cc.transform.position);
                if (dist > 2300f) continue;
                if (dist >= best) continue;
                best = dist;
                s = cc.transform.InverseTransformPoint(info.transform.position).x >= 0f ? 1 : -1;
            }
            return s;
        }

        /// <summary>The forward mount of the CIWS cluster on one side.</summary>
        CarrierTurret ClusterMount(int s)
        {
            var dir = cc.GetComponent<CiwsDirector>();
            if (dir == null || dir.mounts == null) return null;
            CarrierTurret best = null;
            foreach (var m in dir.mounts)
            {
                if (!m) continue;
                Vector3 p = cc.transform.InverseTransformPoint(m.transform.position);
                if (p.x * s <= 0f) continue;
                if (best == null || p.z > cc.transform.InverseTransformPoint(best.transform.position).z) best = m;
            }
            return best;
        }

        string CiwsStatus()
        {
            var dir = cc.GetComponent<CiwsDirector>();
            if (dir == null) return "none";
            var parts = new List<string>();
            foreach (var m in dir.mounts) if (m) parts.Add($"{m.Label}: {(m.target ? m.target.name : "-")} ({m.ShotsFired} shots)");
            return string.Join(", ", parts);
        }

        void ReportSystems()
        {
            var sh = cc.GetComponentInChildren<Shield>(true);
            if (sh) Log($"  shield {sh.Fraction:P0}, collapsed {sh.Collapses}x, hits: projectiles {sh.HitsByKind[0]}, cannon {sh.HitsByKind[1]}, missiles {sh.HitsByKind[2]}, beam {sh.HitsByKind[3]}");
            var gun = demo.MainGun;
            if (gun) Log($"  main gun shots {gun.Shots}, damage {gun.DamageDealt:F0}, charge {gun.LastChargeSeconds:F1} s");
            var dir = cc.GetComponent<CiwsDirector>();
            int n = 0;
            if (dir) foreach (var m in dir.mounts) if (m) n += m.ShotsFired;
            Log($"  CIWS shots {n}");
            Log("  sound sources: " + SoundFX.SourceReport.Replace("\n", "; "));
        }
        /// <summary>Keep the camera at a fixed angle to the ship's heading.</summary>
        void FollowHeading(float offset, float pitch)
        {
            cam.yaw = Mathf.LerpAngle(cam.yaw, cc.transform.eulerAngles.y + offset, Time.deltaTime * 1.5f);
            cam.pitch = Mathf.Lerp(cam.pitch, pitch, Time.deltaTime);
        }

        /// <summary>Close on one side gun from outboard and ahead of it.</summary>
        void GunShot(string label, int index)
        {
            CarrierTurret gun = null;
            foreach (var g in cc.mainTurrets) if (g && g.Label == label) gun = g;
            if (gun == null) return;
            float outYaw = Quaternion.LookRotation(gun.transform.forward).eulerAngles.y;
            // camera ahead of and outboard of the pod, looking back at it and out to sea
            float sign = label == "Port" ? -1f : 1f;
            Shot(index == 0 ? 2 : 5, gun.transform, new Vector3(0f, 6f, 0f), 85f, outYaw + 180f - sign * 40f, 14f, -sign * 4f);
        }

        readonly Dictionary<Transform, bool> wasDead = new Dictionary<Transform, bool>();

        void LogKills()
        {
            if (demo.targets == null) return;
            foreach (var tr in demo.targets)
            {
                var h = tr != null ? tr.GetComponent<Health>() : null;
                if (h == null) continue;
                wasDead.TryGetValue(tr, out bool before);
                if (h.Dead && !before) Log($"{T,5:F1}s   {tr.name} destroyed (camera on shot {shot})");
                wasDead[tr] = h.Dead;
            }
        }

        /// <summary>True on the one frame where the reel clock passes the cue time.</summary>
        bool Cue(float at)
        {
            int f = Mathf.RoundToInt(at * Fps) + Warmup;
            return frame == f;
        }

        void Shot(int index, Transform target, Vector3 offset, float distance, float yaw, float pitch, float spin)
        {
            shot = index;
            cam.target = target;
            cam.offset = offset;
            cam.distance = distance;
            cam.yaw = yaw;
            cam.pitch = pitch;
            cam.autoRotate = spin;
            Log($"{T,5:F1}s shot {index}: {(target ? target.name : "none")}   (music: {(MusicDirector.Playing ? $"{MusicDirector.NowPlaying}, combat mix {MusicDirector.CombatMix:F2}" : "off")})");
        }

        /// <summary>Sit behind the followed craft, easing round to its heading.</summary>
        void Chase()
        {
            if (follow == null)
            {
                follow = PickCraft(Craft.Kind.Fighter);
                if (follow == null) return;
                cam.target = follow;
            }
            cam.yaw = Mathf.LerpAngle(cam.yaw, follow.eulerAngles.y + 18f, Time.deltaTime * 1.5f);
            cam.pitch = Mathf.Lerp(cam.pitch, 9f, Time.deltaTime);
        }

        Transform PickCraft(Craft.Kind kind)
        {
            foreach (var c in cc.Wing)
                if (c != null && c.kind == kind && c.order != Craft.Order.Return) return c.transform;
            foreach (var c in cc.Wing) if (c != null) return c.transform;
            return null;
        }

        void Pick(int index)
        {
            if (demo.targets != null && index < demo.targets.Length) demo.SelectTarget(demo.targets[index]);
        }

        static float Smooth(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

        // ------------------------------------------------------------------ capture

        void SaveFrame()
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(Path.Combine(dir, $"f{recorded:D5}.jpg"), tex.EncodeToJPG(92)); }
            catch (Exception e) { Log("frame " + recorded + ": " + e.Message); }
            Destroy(tex);
        }

        void StartAudio()
        {
            channels = AudioSettings.speakerMode switch
            {
                AudioSpeakerMode.Mono => 1, AudioSpeakerMode.Quad => 4, AudioSpeakerMode.Surround => 5,
                AudioSpeakerMode.Mode5point1 => 6, AudioSpeakerMode.Mode7point1 => 8, _ => 2,
            };
            audioOk = AudioRenderer.Start();
            if (!audioOk) Log("audio capture not available: the reel will be silent");
            else
            {
                // throw away anything rendered before the first recorded frame
                int n = AudioRenderer.GetSampleCountForCaptureFrame();
                if (n > 0) { var junk = new NativeArray<float>(n * channels, Allocator.Temp); AudioRenderer.Render(junk); junk.Dispose(); }
            }
        }

        void CaptureAudio()
        {
            if (!audioOk) return;
            int n = AudioRenderer.GetSampleCountForCaptureFrame();
            if (n <= 0) return;
            var buf = new NativeArray<float>(n * channels, Allocator.Temp);
            AudioRenderer.Render(buf);
            for (int i = 0; i < buf.Length; i++) audio.Add(buf[i]);
            buf.Dispose();
        }

        void WriteWav(string path)
        {
            if (!audioOk || audio.Count == 0) return;
            int rate = AudioSettings.outputSampleRate;
            using var w = new BinaryWriter(File.Create(path));
            int bytes = audio.Count * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
            w.Write((short)1); w.Write((short)channels); w.Write(rate);
            w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
            foreach (float s in audio) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(s, -1f, 1f) * 32767f));
        }

        void Log(string line)
        {
            log += line + "\n";
            try { File.WriteAllText(Path.Combine(dir, "reel.txt"), log); } catch { }
        }
    }
}

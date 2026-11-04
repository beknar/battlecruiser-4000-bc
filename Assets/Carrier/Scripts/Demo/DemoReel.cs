using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        const float Seconds = 60f;
        const int Warmup = 45;            // frames to let shaders and the scene settle (not recorded)

        string dir;
        CarrierDemoInput demo;
        CarrierController cc;
        OrbitCamera cam;
        int frame, recorded, channels;
        readonly List<float> audio = new List<float>(48000 * 2 * 62);
        bool audioOk;
        string log = "";

        static int Total => Mathf.RoundToInt(Seconds * Fps);
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
            Log($"screen {Screen.width}x{Screen.height}, gfx {SystemInfo.graphicsDeviceType}, audio {AudioSettings.outputSampleRate} Hz {AudioSettings.speakerMode}");

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
            Log($"{T,5:F1}s shot {index}: {(target ? target.name : "none")}");
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

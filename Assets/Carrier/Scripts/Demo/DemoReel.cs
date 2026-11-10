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
    public partial class DemoReel : MonoBehaviour
    {
        const int Fps = 30;
        float seconds = 60f;               // the asteroid ambush runs longer (see Start)
        const int Warmup = 45;            // frames to let shaders and the scene settle (not recorded)

        string dir;
        CarrierDemoInput demo;
        CarrierController cc;
        OrbitCamera cam;
        int frame, recorded, channels;
        BinaryWriter wav;                 // the sound goes straight to reel.wav as it is captured
        long wavSamples;
        float gain = 1f;                  // sound level on the last captured frame (fades with the picture)
        bool audioOk;
        string log = "";

        int Total => Mathf.RoundToInt(seconds * Fps);
        float T => (frame - Warmup) / (float)Fps;     // seconds into the reel

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Flag()
        {
            if (LaunchArgs.Has("-reel")) CarrierDemoInput.ReelMode = true;
            // -features: the Star System feature reel (mining, combat, landing, mini-warps);
            // -orbits: the orbits and stations reel (it runs on the feature reel's machinery)
            // -all: everything in one reel
            Orbits = CarrierDemoInput.ReelMode && LaunchArgs.Has("-orbits");
            All = CarrierDemoInput.ReelMode && LaunchArgs.Has("-all");
            Features = CarrierDemoInput.ReelMode && (Orbits || All || LaunchArgs.Has("-features"));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!LaunchArgs.Has("-reel")) return;
            var r = new GameObject("DemoReel").AddComponent<DemoReel>();
            r.dir = LaunchArgs.FolderAfter("-reel", Application.persistentDataPath);
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
            if (sysMap != null) seconds = 260f;     // ends a few seconds after the cruiser dies (see SystemShots)
            if (Features && sysMap != null)
            {
                seconds = 900f;                     // a cap: the shot list ends it
                StartCoroutine(All ? AllReel() : Orbits ? OrbitReel() : FeatureReel());
            }
            Log($"screen {Screen.width}x{Screen.height}, gfx {SystemInfo.graphicsDeviceType}, audio {AudioSettings.outputSampleRate} Hz {AudioSettings.speakerMode}, {seconds:F0} s");

            var eof = new WaitForEndOfFrame();
            while (recorded < Total && !finished)
            {
                yield return eof;
                if (frame == Warmup - 1) StartAudio();
                if (frame < Warmup) continue;
                if (!recording) { CaptureAudio(false); continue; }   // between scenes: nothing kept
                SaveFrame();
                CaptureAudio(true);
                recorded++;
                if (recorded % 300 == 0) Log($"{recorded} frames");
            }
            if (audioOk) AudioRenderer.Stop();
            FinishWav();
            Log($"done: {recorded} frames, audio {(audioOk ? wavSamples / channels / (float)AudioSettings.outputSampleRate : 0f):F1} s");
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
            if (Features) { perFrame?.Invoke(); return; }
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

        void Log(string line)
        {
            log += line + "\n";
            try { File.WriteAllText(Path.Combine(dir, "reel.txt"), log); } catch { }
        }
    }
}

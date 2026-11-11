using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CarrierGame.Demo
{
    /// <summary>
    /// A self-driving test run, so the demo can be checked without injecting keystrokes or grabbing
    /// the screen from outside. Start the build with "-autotest &lt;folder&gt;" (add
    /// "-screen-fullscreen 0" to keep it in a window). It measures how the ship accelerates and
    /// turns, steers a broadside onto the target and checks the guns hit, launches craft and fires
    /// missiles if the ship has them, mutes and unmutes, clears and re-picks the target, and drives
    /// a virtual gamepad, saving a screenshot and a status line at each step, then quits. The log
    /// is rewritten after every step, so a partial run still leaves a record.
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        string dir;
        readonly StringBuilder log = new StringBuilder();
        CarrierDemoInput demo;
        CarrierController cc;
        int broadsideSide;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!LaunchArgs.Has("-autotest")) return;
            var t = new GameObject("AutoTest").AddComponent<AutoTest>();
            t.dir = LaunchArgs.FolderAfter("-autotest", Application.persistentDataPath);
        }

        int overlapFrames, checkedFrames;

        bool bowOn, breakupSeen;

        /// <summary>The first time the cruiser comes apart: look at it, screenshot it breaking and
        /// the wreckage after the blast, and log how it went.</summary>
        IEnumerator WatchBreakup(ShipBreakup br)
        {
            var cam = demo.orbitCamera;
            var oldTarget = cam.target; var oldDist = cam.distance; var oldOffset = cam.offset;
            var follow = new GameObject("BreakupCam").transform;
            follow.position = br.transform.position;
            cam.target = follow;
            cam.offset = Vector3.zero;
            cam.distance = 700f;
            var mover = br.GetComponent<IVelocity>();
            Vector3 v = mover != null ? mover.Velocity : Vector3.zero;
            float t0 = Time.time;
            bool shot1 = false, shot2 = false;
            while (Time.time - t0 < 2.6f)
            {
                follow.position += v * Time.deltaTime;
                if (!shot1 && Time.time - t0 > br.breakTime * 0.7f) { shot1 = true; ScreenCapture.CaptureScreenshot(Path.Combine(dir, "b1_breaking.png")); }
                if (!shot2 && br.State == ShipBreakup.Phase.Blown && Time.time - br.BlewAt > 1.2f) { shot2 = true; ScreenCapture.CaptureScreenshot(Path.Combine(dir, "b2_wreckage.png")); }
                yield return null;
            }
            float spread = 0f;
            foreach (var c in br.chunks) if (c) spread = Mathf.Max(spread, Vector3.Distance(c.position, follow.position));
            log.AppendLine($"  breakup: {br.name}: {br.chunks.Length} parts, {br.ReleasedBeforeBlast} broke off before the explosion, " +
                           $"explosion {br.BlewAt - br.BrokeAt:F2} s after the first part, wreckage spread {spread:F0} m after {Time.time - br.BlewAt:F1} s");
            Flush();
            cam.target = oldTarget; cam.distance = oldDist; cam.offset = oldOffset;
            Destroy(follow.gameObject);
        }

        void Update()
        {
            if (!breakupSeen && demo != null && Targets.All.Count > 0 && Targets.All[0] != null)
            {
                var br = Targets.All[0].GetComponent<ShipBreakup>();
                if (br != null && br.State == ShipBreakup.Phase.Breaking) { breakupSeen = true; StartCoroutine(WatchBreakup(br)); }
            }
            if (broadsideSide != 0 && cc != null && cc.target != null)
                demo.autoRudder = ShipAutopilot.Broadside(cc, cc.target.position, broadsideSide, 1500f);
            if (bowOn && cc != null && cc.target != null)
            {
                var v = ShipAutopilot.BowOn(cc, cc.target.position);
                demo.autoRudder = v.x;
                demo.autoPitch = v.y;
            }
            // every frame: never more than half the guns able to engage the one target
            if (cc != null && cc.target != null && cc.mainTurrets != null && cc.mainTurrets.Length > 1 && !cc.HasHangar)
            {
                int n = 0;
                foreach (var g in cc.mainTurrets) if (g && g.CanBear(cc.target.position)) n++;
                checkedFrames++;
                if (n * 2 > cc.mainTurrets.Length) overlapFrames++;
            }
        }

        /// <summary>Turn a full circle with the target held still in range, logging for each
        /// 45-degree sector round the ship which guns could bear and which actually fired.</summary>
        IEnumerator Sweep()
        {
            var guns = cc.mainTurrets;
            var bearing = new HashSet<string>[8];
            var shots = new int[8, guns.Length];
            for (int i = 0; i < 8; i++) bearing[i] = new HashSet<string>();
            var last = new int[guns.Length];
            for (int g = 0; g < guns.Length; g++) last[g] = guns[g] ? guns[g].ShotsFired : 0;

            broadsideSide = 0;
            cc.throttle = 0f;
            demo.autoRudder = 1f;
            float turned = 0f, prev = cc.transform.eulerAngles.y, t0 = Time.time;
            while (turned < 365f && Time.time - t0 < 30f)
            {
                yield return null;
                float h = cc.transform.eulerAngles.y;
                turned += Mathf.Abs(Mathf.DeltaAngle(prev, h));
                prev = h;
                if (cc.target == null) continue;
                Vector3 to = cc.target.position - cc.transform.position;
                float rel = Mathf.DeltaAngle(h, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);   // - port, + starboard
                int s = Mathf.Clamp(Mathf.FloorToInt((rel + 180f) / 45f), 0, 7);
                for (int g = 0; g < guns.Length; g++)
                {
                    if (!guns[g]) continue;
                    if (guns[g].CanBear(cc.target.position)) bearing[s].Add(guns[g].Label);
                    shots[s, g] += guns[g].ShotsFired - last[g];
                    last[g] = guns[g].ShotsFired;
                }
            }
            demo.autoRudder = null;
            log.AppendLine($"  sweep: turned {turned:F0} deg in {Time.time - t0:F0}s; target bearing relative to the bow (- port, + starboard):");
            for (int s = 0; s < 8; s++)
            {
                var fired = new List<string>();
                for (int g = 0; g < guns.Length; g++) if (shots[s, g] > 0) fired.Add($"{guns[g].Label} {shots[s, g]}");
                log.AppendLine($"    {s * 45 - 180,4}..{s * 45 - 135,4} deg: can bear {(bearing[s].Count == 0 ? "none" : string.Join(", ", bearing[s]))}; " +
                               $"shots {(fired.Count == 0 ? "none" : string.Join(", ", fired))}");
            }
            Flush();
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            demo = FindAnyObjectByType<CarrierDemoInput>();
            cc = demo != null ? demo.carrier : null;
            if (cc == null)
            {
                Note("no demo controller in scene");
                Application.Quit();
                yield break;
            }
            yield return new WaitForSeconds(3f);
            log.AppendLine($"ship {cc.shipName} ({cc.shipClass}): max speed {cc.maxSpeed} m/s, acceleration {cc.acceleration} m/s/s, " +
                           $"turn {cc.turnRate} deg/s, guns {cc.mainTurrets.Length}, hangar {cc.HasHangar}, missiles {cc.HasMissiles}");
            Note("start");
            yield return Shot("a1_start");

            // handling: full throttle and full rudder for 4 s
            float h0 = cc.transform.eulerAngles.y, t0 = Time.time;
            cc.throttle = 1f;
            demo.autoRudder = 1f;
            yield return new WaitForSeconds(4f);
            float turned = Mathf.DeltaAngle(h0, cc.transform.eulerAngles.y);
            log.AppendLine($"  handling: after {Time.time - t0:F1}s at full throttle and rudder: speed {cc.Speed:F0} m/s, turned {turned:F0} deg " +
                           $"({turned / (Time.time - t0):F1} deg/s)");
            Note("full throttle + rudder");
            demo.autoRudder = 0f;

            // attitude: nose up, level off, then the elevator thrusters up and down
            float y0 = cc.transform.position.y;
            demo.autoPitch = 1f; yield return new WaitForSeconds(3f); demo.autoPitch = null;
            log.AppendLine($"  attitude: nose up 3s -> pitch {cc.Pitch:F0} deg, climbing {cc.ClimbRate:F0} m/s");
            yield return new WaitForSeconds(1f);
            log.AppendLine($"  attitude: held 1s -> pitch {cc.Pitch:F0} deg, gained {cc.transform.position.y - y0:F0} m");
            demo.LevelOff();
            yield return new WaitForSeconds(4f);
            log.AppendLine($"  attitude: level off -> pitch {cc.Pitch:F1} deg");
            cc.throttle = 0.3f;
            y0 = cc.transform.position.y;
            demo.autoElevator = 1f; yield return new WaitForSeconds(1.5f);
            log.AppendLine($"  elevators: up 1.5s -> flares {Flares()}");
            yield return Shot("a1c_elevators_up");
            yield return new WaitForSeconds(1.5f);
            log.AppendLine($"  elevators: up 3s -> lift {cc.LiftSpeed:F0} m/s, rose {cc.transform.position.y - y0:F0} m (pitch {cc.Pitch:F0}), flares {Flares()}");
            y0 = cc.transform.position.y;
            demo.autoElevator = -1f; yield return new WaitForSeconds(3f);
            log.AppendLine($"  elevators: down 3s -> lift {cc.LiftSpeed:F0} m/s, moved {cc.transform.position.y - y0:F0} m, flares {Flares()}");
            demo.autoElevator = null;
            yield return new WaitForSeconds(0.5f);
            log.AppendLine($"  elevators: released 0.5s (braking) -> lift {cc.LiftSpeed:F0} m/s, flares {Flares()}");
            yield return new WaitForSeconds(2.5f);
            log.AppendLine($"  elevators: released 3s -> lift {cc.LiftSpeed:F0} m/s, flares {Flares()}");
            Note("attitude + elevators");
            yield return Shot("a1b_attitude");

            // broadside: steer to keep the target to starboard and let the guns work
            broadsideSide = 1;
            cc.throttle = 0.6f;
            float hull0 = Hull(), hits0 = Hits();
            yield return new WaitForSeconds(10f);
            Note("starboard broadside 10s");
            log.AppendLine($"  guns: hits {Hits() - hits0}, damage {Hull() - hull0:F0} (bearing now: {Bearing()})");
            yield return Shot("a2_attack");

            if (!cc.HasHangar) { yield return Sweep(); Note("after full-circle sweep"); }
            if (demo.MainGun != null) yield return MainGunSteps();
            yield return EnemyBeamStep();
            yield return FrigateDestructionStep();

            if (cc.HasHangar) { demo.LaunchFighters(); yield return new WaitForSeconds(1f); demo.WingAttack(); }
            demo.FireMissiles();      // a frigate just says it has none
            demo.FireGuns();
            yield return new WaitForSeconds(1f);
            Note("missiles/guns key");

            CarrierDemoInput.ToggleMute();
            yield return new WaitForSeconds(0.5f);
            Note("muted");
            yield return Shot("a3_muted");
            CarrierDemoInput.ToggleMute();
            yield return new WaitForSeconds(0.5f);
            Note("unmuted");

            demo.ClearTarget();
            yield return new WaitForSeconds(1.5f);
            Note("target cleared");
            yield return Shot("a4_cleared");

            demo.NextTarget();
            demo.FireGuns();          // straight away, while the guns are still swinging round
            broadsideSide = -1;       // swing round to port on the new target
            yield return new WaitForSeconds(8f);
            Note("target picked again, port broadside 8s");
            yield return Shot("a5_retarget");
            broadsideSide = 0;
            demo.autoRudder = null;

            yield return GamepadSteps();

            if (checkedFrames > 0)
                log.AppendLine($"arc check: frames where both guns could engage the target together: {overlapFrames} of {checkedFrames}");
            int shotsTotal = 0, offTarget = 0, throughHull = 0;
            foreach (var g in cc.mainTurrets) if (g) { shotsTotal += g.ShotsFired; offTarget += g.OffTargetShots; throughHull += g.HullShots; }
            log.AppendLine($"shot check: {shotsTotal} shots, {offTarget} fired away from the target, {throughHull} through the frigate's own hull");
            SystemsReport();
            log.AppendLine("done");
            Flush();
            yield return null;
            Application.Quit();
        }

        /// <summary>Point the bow at the cruiser, charge the main gun, and check it fires on time and hits.</summary>
        IEnumerator MainGunSteps()
        {
            var gun = demo.MainGun;
            demo.SelectTarget(Targets.All[0]);
            var h = cc.target.GetComponent<Health>();
            for (float w = 0f; h != null && h.Dead && w < 8f; w += 0.25f) yield return new WaitForSeconds(0.25f);
            broadsideSide = 0;
            bowOn = true;
            cc.throttle = 0.2f;
            float hull0 = h ? h.TotalDamage : 0f;
            int kills0 = h ? h.TimesDestroyed : 0;
            float t0 = Time.time;
            demo.ChargeMainGun();
            log.AppendLine($"  main gun: charging ({gun.Current}), bow {ShipAutopilot.BowError(cc, cc.target.position):F0} deg off the target");
            yield return new WaitForSeconds(5f);
            log.AppendLine($"  main gun: 5s in -> {gun.Current}, {gun.ChargeTimeLeft:F1} s left, bow {ShipAutopilot.BowError(cc, cc.target.position):F1} deg off");
            yield return Shot("a7_charging");
            while (gun.Current == MainBeam.State.Charging && Time.time - t0 < 15f) yield return null;
            log.AppendLine($"  main gun: fired after {gun.LastChargeSeconds:F2} s of charging ({gun.Current})");
            float fpsMin = 999f;
            bool shot = false;
            while (gun.Current == MainBeam.State.Firing)
            {
                fpsMin = Mathf.Min(fpsMin, 1f / Mathf.Max(1e-4f, Time.unscaledDeltaTime));
                if (!shot && Time.time - t0 > gun.LastChargeSeconds + 0.8f) { shot = true; yield return Shot("a8_beam"); }
                yield return null;
            }
            bowOn = false;
            demo.autoRudder = null;
            demo.autoPitch = null;
            log.AppendLine($"  main gun: beam done: dealt {gun.DamageDealt:F0} damage, target took {(h ? h.TotalDamage - hull0 : 0f):F0}, " +
                           $"destroyed {(h ? h.TimesDestroyed - kills0 : 0)}x, lowest fps while firing {fpsMin:F0}");
            Note("after main gun");
            demo.LevelOff();
        }

        /// <summary>Hold our fire and let the cruiser fire its beam at us, to see the shield take a beam.</summary>
        IEnumerator EnemyBeamStep()
        {
            var shield = cc.GetComponentInChildren<Shield>(true);
            var cruiser = Targets.All[0];
            var beam = cruiser ? cruiser.GetComponentInChildren<MainBeam>() : null;
            if (shield == null || beam == null) yield break;
            foreach (var g in cc.mainTurrets) if (g) g.autoFire = false;
            demo.ClearTarget();
            var h = cruiser.GetComponent<Health>();
            for (float w = 0f; h != null && h.Dead && w < 8f; w += 0.25f) yield return new WaitForSeconds(0.25f);
            for (float w = 0f; !shield.Up && w < 8f; w += 0.25f) yield return new WaitForSeconds(0.25f);
            int before = shield.HitsByKind[(int)HitKind.Beam];
            float s0 = shield.Strength;
            beam.Cancel();
            beam.StartCharge();
            float t0 = Time.time;
            while (beam.Current != MainBeam.State.Firing && Time.time - t0 < 8f) yield return null;
            yield return new WaitForSeconds(0.7f);
            yield return Shot("a9_enemy_beam");
            while (beam.Current == MainBeam.State.Firing) yield return null;
            log.AppendLine($"  enemy beam: fired after {beam.LastChargeSeconds:F1} s charge from {Vector3.Distance(cruiser.position, cc.transform.position):F0} m, " +
                           $"last hit {(beam.LastHit ? beam.LastHit.name : "none")}, shield beam hits +{shield.HitsByKind[(int)HitKind.Beam] - before}, " +
                           $"shield {s0:F0} -> {shield.Strength:F0}");
            foreach (var g in cc.mainTurrets) if (g) g.autoFire = true;
            demo.SelectTarget(cruiser);
        }

        /// <summary>Destroy the frigate and check it comes apart, goes quiet, and comes back whole.</summary>
        IEnumerator FrigateDestructionStep()
        {
            var h = cc.GetComponent<Health>();
            var br = cc.GetComponent<ShipBreakup>();
            if (h == null || br == null || h.cannotDie) { log.AppendLine("  frigate destruction: not destroyable"); yield break; }
            var cam = demo.orbitCamera;
            float oldDist = cam.distance;
            cam.distance = 650f;
            var shield = cc.GetComponentInChildren<Shield>(true);
            int projectiles0 = shield ? shield.HitsByKind[0] + shield.HitsByKind[1] : 0;

            h.Damage(h.Current + 1f);   // straight to the hull
            yield return null;
            log.AppendLine($"  frigate destroyed: dead {h.Dead}, breakup {br.State}, {br.chunks.Length} parts, respawn in {cc.RespawnIn:F1} s");
            yield return new WaitForSeconds(br.breakTime * 0.7f);
            yield return Shot("c1_frigate_breaking");
            while (br.State != ShipBreakup.Phase.Blown) yield return null;
            yield return new WaitForSeconds(1.3f);
            yield return Shot("c2_frigate_wreck");

            int turretsOn = 0;
            foreach (var tu in br.chunks) if (tu && tu.GetComponent<CarrierTurret>() is CarrierTurret t && t.enabled) turretsOn++;
            var gun = demo.MainGun;
            demo.FireGuns();
            demo.ChargeMainGun();
            yield return new WaitForSeconds(0.5f);
            float spread = 0f;
            foreach (var c in br.chunks) if (c) spread = Mathf.Max(spread, Vector3.Distance(c.position, cc.transform.position));
            log.AppendLine($"  while destroyed: {br.ReleasedBeforeBlast} parts broke off before the blast ({br.BlewAt - br.BrokeAt:F2} s), wreckage spread {spread:F0} m, " +
                           $"turrets running {turretsOn}, shield up {(shield ? shield.Up : false)}, main gun {(gun ? gun.Current.ToString() : "-")} after pressing fire, " +
                           $"speed {cc.Speed:F0} m/s");
            int projectiles1 = shield ? shield.HitsByKind[0] + shield.HitsByKind[1] : 0;

            float t0 = Time.time;
            while (h.Dead && Time.time - t0 < 15f) yield return null;
            yield return new WaitForSeconds(0.5f);
            int restored = 0;
            foreach (var c in br.chunks) if (c && c.IsChildOf(cc.transform) && c.gameObject.activeSelf) restored++;
            turretsOn = 0;
            foreach (var tu in cc.GetComponentsInChildren<CarrierTurret>()) if (tu.enabled) turretsOn++;
            log.AppendLine($"  respawned {Time.time - t0:F1} s later: hull {h.Current:F0}/{h.maxHealth:F0}, shield {(shield ? shield.Fraction : 0f):P0} up {(shield ? shield.Up : false)}, " +
                           $"{restored}/{br.chunks.Length} parts back in place, turrets running {turretsOn}, at {cc.transform.position:F0}, " +
                           $"enemy hits on the shield while it was down: {projectiles1 - projectiles0}");
            yield return Shot("c3_respawned");
            cam.distance = oldDist;
        }

        void SystemsReport()
        {
            log.AppendLine("sound sources: " + SoundFX.SourceReport.Replace("\n", "; "));
            log.AppendLine($"music: playing {MusicDirector.Playing}, combat mix {MusicDirector.CombatMix:F2}");
            var shield = cc.GetComponentInChildren<Shield>(true);
            var hull = cc.GetComponent<Health>();
            if (shield != null)
                log.AppendLine($"shield: {shield.Fraction:P0} now, up {shield.Up}, collapsed {shield.Collapses}x; hits taken: " +
                               $"projectiles {shield.HitsByKind[(int)HitKind.Projectile]}, cannon {shield.HitsByKind[(int)HitKind.Cannon]}, " +
                               $"missiles {shield.HitsByKind[(int)HitKind.Missile]}, beam {shield.HitsByKind[(int)HitKind.Beam]}");
            if (hull != null) log.AppendLine($"frigate hull: {hull.Current:F0} / {hull.maxHealth:F0} ({hull.TotalDamage:F0} got through the shield)");
            var ciws = cc.GetComponent<CiwsDirector>();
            if (ciws != null && ciws.mounts != null)
            {
                int shots = 0;
                var at = new HashSet<string>();
                foreach (var m in ciws.mounts) if (m) { shots += m.ShotsFired; at.UnionWith(m.FiredAt); }
                var kinds = new List<string>();
                foreach (var n in at)
                {
                    var go = GameObject.Find(n);
                    var ti = go ? go.GetComponent<TargetInfo>() : null;
                    kinds.Add($"{n} ({(ti ? ti.kind.ToString() : "?")})");
                }
                log.AppendLine($"CIWS: {ciws.mounts.Length} mounts, {shots} shots, fired at: {(kinds.Count == 0 ? "nothing" : string.Join(", ", kinds))}");
                foreach (var t in Targets.All)
                {
                    var th = t ? t.GetComponent<Health>() : null;
                    if (th) log.AppendLine($"  {t.name}: destroyed {th.TimesDestroyed}x, hits taken {th.Hits}");
                }
            }
        }

        string Flares()
        {
            float up = 0f, down = 0f;
            if (cc.liftThrustersUp != null) foreach (var f in cc.liftThrustersUp) if (f) up = Mathf.Max(up, f.thrust);
            if (cc.liftThrustersDown != null) foreach (var f in cc.liftThrustersDown) if (f) down = Mathf.Max(down, f.thrust);
            return $"ventral {up:F2} / dorsal {down:F2}";
        }

        float Hull() { var h = cc.target ? cc.target.GetComponent<Health>() : null; return h ? h.TotalDamage : 0f; }
        int Hits() { var h = cc.target ? cc.target.GetComponent<Health>() : null; return h ? h.Hits : 0; }
        string Bearing() { string b = cc.GunsBearing(); return b.Length == 0 ? "none" : b; }

        // ------------------------------------------------------------ gamepad, through a virtual pad

        Gamepad vpad;

        IEnumerator GamepadSteps()
        {
            // the test window may not have focus; keep feeding the virtual pad anyway
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            vpad = InputSystem.AddDevice<Gamepad>("AutoTestPad");
            vpad.MakeCurrent();
            yield return Frames(3);
            PadNote("pad added");

            yield return Press(GamepadButton.East);       // B: stop targeting
            PadNote("pad B (stop targeting)");
            yield return Press(GamepadButton.North);      // Y: next target
            PadNote("pad Y (next target)");
            if (cc.HasHangar)
            {
                yield return Press(GamepadButton.DpadUp);     // launch fighters
                yield return new WaitForSeconds(1f);
                PadNote("pad D-pad up (fighters)");
            }
            else
            {
                yield return Hold(new GamepadState().WithButton(GamepadButton.DpadUp), 1f);
                PadNote($"pad D-pad up 1s (nose up: pitch {cc.Pitch:F0} deg)");
                yield return Press(GamepadButton.South);      // A: level off
                yield return new WaitForSeconds(2f);
                PadNote($"pad A (level off: pitch {cc.Pitch:F1} deg)");
                float y0 = cc.transform.position.y;
                yield return Hold(new GamepadState { rightTrigger = 1f }, 2f);
                PadNote($"pad RT 2s (elevators up: lift {cc.LiftSpeed:F0} m/s, rose {cc.transform.position.y - y0:F0} m)");
                yield return Hold(new GamepadState { leftTrigger = 1f }, 2f);
                PadNote($"pad LT 2s (elevators down: lift {cc.LiftSpeed:F0} m/s)");
                yield return Hold(new GamepadState().WithButton(GamepadButton.DpadRight), 1f);
                PadNote("pad D-pad right 1s (zoom in)");
            }

            cc.throttle = 0f;
            yield return Hold(new GamepadState { leftStick = new Vector2(0f, 1f) }, 1f);
            PadNote("pad left stick up 1s (throttle)");
            float h0 = cc.transform.eulerAngles.y;
            yield return Hold(new GamepadState { leftStick = new Vector2(-1f, 0f) }, 1.5f);
            PadNote($"pad left stick left 1.5s (turned {Mathf.DeltaAngle(h0, cc.transform.eulerAngles.y):F0} deg)");
            if (cc.HasHangar)
            {
                yield return Hold(new GamepadState { rightTrigger = 1f }, 1f);
                PadNote("pad RT 1s (zoom in)");
            }
            yield return Hold(new GamepadState { rightStick = new Vector2(1f, 0f) }, 1f);
            PadNote("pad right stick right 1s (orbit)");

            yield return Press(GamepadButton.Start);      // pause menu
            PadNote($"pad Start (pause menu open {GameMenu.IsOpen}, time scale {Time.timeScale})");
            yield return Press(GamepadButton.Start);      // and closed again
            PadNote($"pad Start again (pause menu open {GameMenu.IsOpen}, time scale {Time.timeScale})");
            yield return new WaitForSeconds(0.5f);
            yield return Shot("a6_pad");

            InputSystem.RemoveDevice(vpad);
            yield return Frames(2);
            PadNote("pad removed");
        }

        IEnumerator Press(GamepadButton b)
        {
            InputSystem.QueueStateEvent(vpad, new GamepadState().WithButton(b));
            yield return Frames(3);
            InputSystem.QueueStateEvent(vpad, new GamepadState());
            yield return Frames(3);
        }

        IEnumerator Hold(GamepadState state, float seconds)
        {
            InputSystem.QueueStateEvent(vpad, state);
            yield return new WaitForSeconds(seconds);
            InputSystem.QueueStateEvent(vpad, new GamepadState());
            yield return Frames(3);
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        void PadNote(string step)
        {
            var cam = demo.orbitCamera;
            log.AppendLine(
                $"{Time.time,6:F1}s {step}: gamepad {(Gamepad.current != null ? Gamepad.current.name : "none")}, " +
                $"pad help shown {PadSupport.UsingPad}, target {(cc.target ? cc.target.name : "none")}, " +
                $"throttle {cc.throttle:P0}, speed {cc.Speed:F0}, heading {cc.transform.eulerAngles.y:F0}, wing {cc.Wing.Count}, " +
                $"camera distance {(cam ? cam.distance : 0f):F0}, yaw {(cam ? cam.yaw : 0f):F0}, muted {SoundFX.Muted}");
            Flush();
        }

        void Note(string step)
        {
            var t = cc != null ? cc.target : null;
            var h = t != null ? t.GetComponent<Health>() : null;
            int attackers = 0;
            if (cc != null) foreach (var c in cc.Wing) if (c != null && c.order == Craft.Order.Attack) attackers++;
            log.AppendLine(
                $"{Time.time,6:F1}s {step}: fps {1f / Mathf.Max(1e-4f, Time.smoothDeltaTime):F0}, " +
                $"gfx {SystemInfo.graphicsDeviceType}, {Screen.width}x{Screen.height} {Screen.fullScreenMode}, " +
                $"speed {(cc ? cc.Speed : 0f):F0} m/s, heading {(cc ? cc.transform.eulerAngles.y : 0f):F0}, " +
                $"target {(t ? t.name : "none")}, hull {(h ? h.Current.ToString("F0") : "-")}, hits {(h ? h.Hits : 0)}, killed {(h ? h.TimesDestroyed : 0)}x, " +
                $"guns bearing {(cc ? Bearing() : "-")}, wing {(cc ? cc.Wing.Count : 0)} ({attackers} attacking), missiles in flight {Missile.Active.Count}, " +
                $"muted {SoundFX.Muted}, listener volume {AudioListener.volume:F1}, voices playing {SoundFX.PlayingVoices}");
            Flush();
        }

        void Flush()
        {
            try { File.WriteAllText(Path.Combine(dir, "autotest.txt"), log.ToString()); }
            catch (Exception e) { Debug.LogWarning("AutoTest log: " + e.Message); }
        }

        IEnumerator Shot(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return null;
            yield return null;
        }
    }
}

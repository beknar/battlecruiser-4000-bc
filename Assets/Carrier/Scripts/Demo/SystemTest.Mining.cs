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
    /// <summary>The self-test's mining section: the sensor, the laser, the cargo hold, a comet and a vein.</summary>
    public partial class SystemTest
    {
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
    }
}

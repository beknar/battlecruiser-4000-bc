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
    /// <summary>The self-test's orbits section: Kepler's laws, the moons, the Lagrange points.</summary>
    public partial class SystemTest
    {
        // ------------------------------------------------------------------ orbits

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
                float moved = Vector3.Angle(off0[o], now), want = (float)(360.0 * dt / o.Period);
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
            float colinear = Vector3.Angle(sT, sL1);
            float hill = (float)Orbit.HillFraction(thalassa.massRatio) * sT.magnitude;
            float l1Off = Vector3.Distance(l1.transform.position, thalassa.transform.position);
            Log($"  L1: {colinear:F3} deg off the star-Thalassa line, {l1Off / 1000f:F2} km from Thalassa (Hill radius {hill / 1000f:F2} km); " +
                $"station-keeping burns {keep?.Burns}, largest drift {keep?.LargestDrift:F0} m, e-folding time {1f / Mathf.Max(1e-6f, keep != null ? keep.Lambda : 0f) / 60f:F1} min");
            bool l1Ok = colinear < 0.2f && Mathf.Abs(l1Off - hill) < 200f && keep != null && keep.Burns >= 1 && keep.LargestDrift <= keep.tolerance + 5f;
            Vector3 sJ = jovara.transform.position - S();
            float a4 = Vector3.Angle(sJ, l4.transform.position - S()), a5 = Vector3.Angle(sJ, l5.transform.position - S());
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
            float back = th2 != null ? Vector3.Angle(th2.transform.position - sys2.StarPosition, off0[thalassa]) : 180f;
            Log($"  after a restart: orbit time {sys2?.Time:F1} s, Thalassa {back:F2} deg from where it started");
            results.Add(("a restart starts the orbits again", back < 0.5f));

            foreach (var (what, ok) in results) Log($"  {(ok ? "ok" : "FAILED")}: {what}");
            int passed = results.Count(r => r.Item2);
            Log(passed == results.Count ? $"PASS: orbits ({passed} of {results.Count})"
                                        : $"FAIL: orbits ({passed} of {results.Count}: failed {string.Join("; ", results.Where(r => !r.Item2).Select(r => r.Item1))})");
        }
    }
}

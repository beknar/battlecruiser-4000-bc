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
    /// <summary>The self-test's landing section: landing on each world, flying low, and the surface streaming.</summary>
    public partial class SystemTest
    {
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
    }
}

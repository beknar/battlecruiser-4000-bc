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
    /// <summary>The self-test's installations section: docking, the defence grids, the bases and the stations.</summary>
    public partial class SystemTest
    {
        // ------------------------------------------------------------------ installations

        /// <summary>Turn the camera from behind the frigate onto a point, take a picture, put it back.</summary>
        IEnumerator LookAt(Vector3 p, string shot, float distMul = 2.6f, float down = 6f)
        {
            var oc = demo.orbitCamera;
            if (oc == null) { Shot(shot); yield break; }
            float d0 = oc.distance, p0 = oc.pitch, y0 = oc.yaw;
            Vector3 to = Quaternion.Inverse(cc.upFrame) * (p - cc.transform.position);   // (in the ship's up frame, as the camera turns)
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
            // (the named gun, if the frigate can see it from where it has come in: otherwise the
            // nearest one it can see; the frigate arrives from wherever its last place was)
            var named = guns.Select(w => w.transform).FirstOrDefault(t => t.name == targetName);
            bool Seen(Transform t) => t != null && Cover.Visible(cc.transform.position, t.position + Vector3.up * 8f);
            var target = Seen(named) ? named
                : guns.Select(w => w.transform).Where(Seen).OrderBy(t => Vector3.Distance(t.position, cc.transform.position)).FirstOrDefault()
                  ?? named ?? guns[0].transform;
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
            // (from before the warp here: the ground below starts being built as soon as the
            // frigate is low over a planet, and the garbage is collected then)
            int gc0 = Mathf.Min(SceneMemory.Collections, collectionsAtWarp);
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
            // (nor its crew thinned: a short-handed frigate is slower to get out of range, and the
            // crew are -crewtest's to test)
            var people = cc.GetComponent<Crew>();
            if (people != null) people.Changed += () => { if (people.Missing > 0) people.SetAboard(people.complement); };
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
    }
}

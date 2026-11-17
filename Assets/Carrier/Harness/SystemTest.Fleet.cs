using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using K = UnityEngine.InputSystem.Key;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The fleet (-fleettest): two ships berthed at the Kestrel Deep Yard; at the yard one joins
    /// the fleet crewed and the other is towed (half speed, warps twice as long); the fleet mode's
    /// menu lists them; the crewed one follows, and attacks the leader's target; the fleet warps
    /// with the leader; a fleet ship destroyed is lost; one left at a yard is berthed there.
    /// </summary>
    public partial class SystemTest
    {
        bool fleetOnly;

        IEnumerator FleetTest(Keyboard kb)
        {
            int checks = 0, passed = 0;
            void Check(bool ok, string what)
            {
                checks++;
                if (ok) passed++;
                Log($"{(ok ? "ok" : "FAIL")}: {what}");
            }
            var hull = cc.GetComponent<Health>();
            if (hull != null) hull.cannotDie = true;
            var fleet = FleetDirector.Instance;
            var panel = IndustryPanel.Instance;
            var hold = demo.Cargo;
            if (fleet == null || panel == null || hold == null) { Log("FAIL: fleet (no fleet director, services screen or hold)"); yield break; }
            const string yard = "Kestrel Deep Yard";
            IndustryLedger.Launch("Halberd", yard);
            IndustryLedger.Launch("Rapier", yard);
            hold.Earn(500000f);

            // ---- at the yard: one joins crewed, one is towed
            yield return WarpTo(kb, yard, "fl_yard");
            var inst = Installation.All.FirstOrDefault(i => i.displayName == yard);
            yield return FlyTo(inst.BerthPosition, inst.dockRange * 0.85f, 60f);
            cc.throttle = 0f;
            yield return new WaitForSeconds(drive.recentHitSeconds + 0.5f);
            yield return Key(kb, K.K);
            float until = Time.time + 10f;
            while (Time.time < until && !Docking.Docked) yield return null;
            yield return new WaitForSeconds(1f);
            if (IndustryPanel.IsOpen) panel.Show(IndustryPanel.Page.Shipyard); else panel.Open(IndustryPanel.Page.Shipyard);
            yield return null;
            Check(Docking.Docked && panel.Current == IndustryPanel.Page.Shipyard && panel.RowIds.Contains("*berth:Halberd") && panel.RowIds.Contains("*berth:Rapier"),
                  $"docked at the yard, the shipyard page lists the two berthed ({string.Join(", ", panel.RowIds.Where(r => r.StartsWith("*")))})");
            // (the yard has fewer recruits waiting than a destroyer needs: some of her crew
            // signed on before, the rest from the yard and the frigate)
            IndustryLedger.SetCrew("Halberd", Mathf.Max(0, FleetDirector.CrewToSail("Halberd") - 40));
            panel.Act("*berth:Halberd", 16);
            yield return null;
            var halberd = fleet.Ships.FirstOrDefault(s => s.entry.id == "Halberd");
            int crew = Mathf.RoundToInt(IndustryLedger.CrewOf("Halberd") ?? 0f);
            Check(halberd != null && IndustryLedger.Fleet.Count == 1 && crew >= FleetDirector.CrewToSail("Halberd"),
                  $"the Halberd joins the fleet, crewed ({crew} aboard, {FleetDirector.CrewToSail("Halberd")} needed to sail): {Strip(Notices.Last)}");
            panel.Act("*berth:Rapier", 17);
            yield return null;
            var rapier = fleet.Ships.FirstOrDefault(s => s.entry.id == "Rapier");
            Check(rapier != null && rapier.Towed && cc.towScale == 0.5f && drive.slowScale == 2f && Mathf.Approximately(drive.ChargeTime, drive.chargeTime * 2f),
                  $"the Rapier is towed: the leader at half speed (x{cc.towScale}), warps twice as long (charge {drive.ChargeTime:F0} s)");
            Shot("fl_shipyard");
            if (halberd == null || rapier == null) { Log("FAIL: fleet (the two didn't both join, so the rest can't be tried)"); panel.Close(); yield break; }
            panel.Close();
            yield return null;
            yield return null;
            // the fleet's panels, on the right: shown, and hidden when set so
            bool shown = FleetDirector.PanelsRect.height > 0f;
            HudSettings.FleetPanels = false;
            yield return null;
            yield return null;
            bool hidden = FleetDirector.PanelsRect.height <= 0f;
            HudSettings.FleetPanels = true;
            // (the alert varies: a ship launched at the yard is sometimes nudged out of its structure)
            Log($"  the Halberd's panel: {FleetDirector.Alert(halberd, out _)}");
            Check(shown && hidden, $"the fleet's panels: shown {shown}, hidden when the HUD is set so {hidden} ({FleetDirector.Guns(halberd)})");
            yield return Key(kb, K.K);
            until = Time.time + 10f;
            while (Time.time < until && Docking.Busy) yield return null;
            cc.throttle = 0f;

            // ---- fleet mode: the menu, and following
            yield return Key(kb, K.Digit5);
            yield return Key(kb, K.Tab);
            var menu = ModeMenu.Instance;
            int listed = menu != null ? menu.Rows.Count(r => r.tag is FleetPilot) : 0;
            Check(ModeMenu.IsOpen && listed == 2, $"5, Tab: the fleet's menu lists its {listed} ships");
            Shot("fl_menu");
            yield return Key(kb, K.Tab);
            yield return Key(kb, K.F);
            Check(halberd.Current == FleetPilot.Order.Follow, $"F: following ('{Notices.Last}')");
            // the map shows the fleet's ships (no warp to them: they fly with you)
            if (map != null)
            {
                yield return Key(kb, K.M);
                yield return null;
                var marked = map.Allies.Select(a => a.name).ToList();
                map.SelectAlly(halberd.transform);
                bool refused = !map.WarpToSelected();
                Shot("fl_map");
                Check(SystemMap.IsOpen && marked.Contains(halberd.displayName) && marked.Contains(rapier.displayName) && refused,
                      $"the map shows the fleet: {string.Join(", ", marked)}; no warp to one ('{Notices.Last}')");
                if (SystemMap.IsOpen) yield return Key(kb, K.M);
                yield return Key(kb, K.Digit5);
            }
            cc.throttle = 1f;
            yield return new WaitForSeconds(25f);
            cc.throttle = 0.4f;
            yield return new WaitForSeconds(10f);
            float off = Vector3.Distance(halberd.transform.position, halberd.Station(false));
            float tow = Vector3.Distance(rapier.transform.position, cc.transform.position);
            Check(off < 1500f && tow < 1200f, $"after a run, the Halberd is {off:F0} m off its station ({halberd.Doing}); the Rapier on its tow, {tow:F0} m astern");
            Shot("fl_following");

            // ---- the leader turns hard across its escort: the Halberd goes round her, never through her
            {
                float Gap()
                {
                    ShipAvoidance.CapsuleOf(cc.transform, out var c1, out var a1, out float h1, out float r1);
                    ShipAvoidance.CapsuleOf(halberd.transform, out var c2, out var a2, out float h2, out float r2);
                    ShipAvoidance.ClosestPoints(c1 - a1 * h1, c1 + a1 * h1, c2 - a2 * h2, c2 + a2 * h2, out var p1, out var p2);
                    return Vector3.Distance(p1, p2) - r1 - r2;
                }
                float minGap = float.MaxValue;
                int bumps0 = CarrierController.ShipBumps;
                cc.throttle = 0.5f;
                float tu = Time.time + 30f, turnUntil = Time.time + 12f;
                while (Time.time < tu)
                {
                    demo.autoRudder = Time.time < turnUntil ? 1f : (float?)null;
                    minGap = Mathf.Min(minGap, Gap());
                    yield return null;
                }
                demo.autoRudder = null;
                cc.throttle = 0.4f;
                yield return new WaitForSeconds(10f);
                float off2 = Vector3.Distance(halberd.transform.position, halberd.Station(false));
                Shot("fl_turn_across");
                int bumps = CarrierController.ShipBumps - bumps0;
                Check(minGap > 0f && minGap < 2000f && bumps == 0 && off2 < 2500f,
                      $"the leader turning hard across the Halberd: it steers round her ({minGap:F0} m between the hulls at the closest; pushed apart in {bumps} frames), and it is back on station ({off2:F0} m off)");
            }

            // ---- attack the leader's target (V, combat mode)
            var dummy = new GameObject("Fleet practice target");
            dummy.SetActive(false);
            var dh = dummy.AddComponent<Health>();
            dh.maxHealth = 1e9f;
            dh.cannotDie = true;
            dummy.AddComponent<SphereCollider>().radius = 40f;
            var info = dummy.AddComponent<TargetInfo>();
            info.displayName = "Fleet practice target";
            // (kept where it is put from the leader each frame: a thing left loose in space here
            // can be carried off with a different frame from the ships, by kilometres a second)
            cc.throttle = 0f;
            Vector3 dummyOff = cc.transform.forward * 3000f;
            dummy.transform.position = cc.transform.position + dummyOff;
            dummy.SetActive(true);
            Targets.Add(dummy.transform);
            cc.SetTarget(dummy.transform);
            yield return Key(kb, K.Digit1);
            yield return Key(kb, K.V);
            int shots0 = halberd.ship.mainTurrets.Where(t => t != null).Sum(t => t.ShotsFired);
            until = Time.time + 40f;
            while (Time.time < until && halberd.ship.mainTurrets.Where(t => t != null).Sum(t => t.ShotsFired) == shots0)
            {
                dummy.transform.position = cc.transform.position + dummyOff;
                yield return null;
            }
            int shots = halberd.ship.mainTurrets.Where(t => t != null).Sum(t => t.ShotsFired) - shots0;
            if (shots == 0)
            {
                var guns = halberd.ship.mainTurrets.Where(t => t != null).ToList();
                Log($"    the Halberd {Vector3.Distance(halberd.transform.position, dummy.transform.position):F0} m from the target; guns {guns.Count}, " +
                    $"bearing {guns.Count(g => g.CanBear(dummy.transform.position))}, auto-fire {guns.Count(g => g.autoFire)}, aimed at it {guns.Count(g => g.target == dummy.transform)}, " +
                    $"range {(guns.Count > 0 ? guns.Max(g => g.range) : 0f):F0} m, speed {halberd.ship.Speed:F0} m/s, ship target {(halberd.ship.target != null ? halberd.ship.target.name : "none")}");
            }
            Check(halberd.Current == FleetPilot.Order.Attack && halberd.Fighting == dummy.transform && shots > 0,
                  $"V: the Halberd attacks the target ({halberd.Doing}), {shots} shots");
            Shot("fl_attack");
            Targets.Remove(dummy.transform);
            Destroy(dummy);
            cc.SetTarget(null);
            cc.throttle = 0f;

            // ---- the fleet warps with the leader
            yield return Key(kb, K.Digit5);
            yield return Key(kb, K.R);
            yield return WarpTo(kb, "Thalassa", "fl_thalassa");
            yield return new WaitForSeconds(2f);
            float h1 = Vector3.Distance(halberd.transform.position, cc.transform.position), r1 = Vector3.Distance(rapier.transform.position, cc.transform.position);
            Check(h1 < 5000f && r1 < 1200f, $"the fleet warps with the leader: the Halberd {h1:F0} m off, the Rapier on its tow {r1:F0} m astern");
            Shot("fl_warped");

            // ---- a fleet ship destroyed is lost; one left at a yard is berthed there
            var hh = halberd.GetComponent<Health>();
            hh.cannotDie = false;
            hh.Damage(hh.Current + 1e6f);
            yield return new WaitForSeconds(1f);
            Check(fleet.Lost == 1 && IndustryLedger.Fleet.All(f => f.id != "Halberd") && !IndustryLedger.Owns("Halberd"),
                  $"the Halberd destroyed: lost from the fleet ('{Notices.Last}')");
            fleet.LeaveAt(rapier, yard);
            yield return null;
            Check(IndustryLedger.Fleet.Count == 0 && IndustryLedger.Berthed.Any(b => b.id == "Rapier" && b.site == yard) && cc.towScale == 1f && drive.slowScale == 1f,
                  "the Rapier left at the yard: berthed there, and the leader's speed and warps back to normal");

            if (hull != null) hull.cannotDie = false;
            Log($"{(passed == checks ? "PASS" : "FAIL")}: the fleet ({passed} of {checks})");
        }
    }
}

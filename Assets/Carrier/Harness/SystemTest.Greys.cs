using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -greystest: the Greys (Data/greys.json), their cell in the Outer Drift.
    /// - Their four ships in the cell, each with its numbers and its gear: quiet on the sensors,
    ///   no armour; the Scout cloaked, with a targeting laser and a drone bay at its bays; the
    ///   Shroud's jammer and EMP burst; the Lance's salvo; the Reaper's tractor emitters.
    /// - The frigate warps there: the Scout cloaked is unseen from beyond what the frigate's
    ///   sensors and ECCM can find.
    /// - Sprung, they fight as the Greys do: the Scout paints the frigate from the edge of its
    ///   sensors; the Shroud jams it and its EMP burst knocks a component out; the Lances go for
    ///   it from a flank; and, its engines out, the Reaper closes and locks its tractor on.
    /// </summary>
    public partial class SystemTest
    {
        bool greysOnly;

        IEnumerator GreysTest(Keyboard kb)
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

            // ---- the cell and its four kinds of ship
            var group = GameObject.Find("Greys cell")?.GetComponent<EncounterGroup>();
            var ships = group != null && group.pilots != null
                ? group.pilots.Where(p => p != null).Select(p => p.GetComponent<GreysShip>()).Where(g => g != null).ToList()
                : new List<GreysShip>();
            Check(group != null && ships.Count == group.pilots.Length && ships.Count >= 4,
                  $"the Greys cell: {ships.Count} ships ({string.Join(", ", ships.Select(g => $"{g.name} ({g.role})"))})");
            if (group == null || ships.Count == 0) { Log("FAIL: the Greys (no cell)"); yield break; }
            var byRole = new Dictionary<GreysShip.Role, GreysShip>();
            foreach (var g in ships) if (!byRole.ContainsKey(g.role)) byRole[g.role] = g;
            foreach (GreysShip.Role r in System.Enum.GetValues(typeof(GreysShip.Role)))
            {
                if (!byRole.TryGetValue(r, out var g)) { Check(false, $"{r}: in the cell"); continue; }
                var info = g.GetComponent<TargetInfo>();
                var sig = g.GetComponent<Signature>();
                var shield = g.GetComponentInChildren<Shield>(true);
                var guns = g.GetComponentsInChildren<CarrierTurret>(true).Where(t => t.projectilePrefab != null).ToArray();
                bool gear;
                string say;
                switch (r)
                {
                    case GreysShip.Role.Scout:
                        var cl = g.GetComponent<Cloak>();
                        var bay = g.GetComponent<DroneBay>();
                        gear = cl != null && g.GetComponent<Designator>() != null && bay != null && bay.bays.Length == 2 && g.GetComponent<ScoutTactics>() != null;
                        say = $"cloak to {cl?.hiddenScale:0.##}, targeting laser, drone bay {bay?.stock} drones at {bay?.bays.Length} bays";
                        break;
                    case GreysShip.Role.Shroud:
                        var ecm = g.GetComponent<EcmEmitter>();
                        var emp = g.GetComponent<EmpBurst>();
                        gear = ecm != null && ecm.radius >= 5000f && ecm.ghosts > 0 && emp != null && guns.Any(t => t.projectilePrefab.kind == HitKind.Emp) && g.GetComponent<ShroudTactics>() != null;
                        say = $"jammer {ecm?.radius / 1000f:0.#} km {ecm?.ghosts} ghosts, EMP burst {emp?.radius / 1000f:0.#} km, EMP projector";
                        break;
                    case GreysShip.Role.Lance:
                        gear = guns.Length > 0 && guns.Sum(t => t.muzzles.Length) >= 5 && g.GetComponent<LanceTactics>() != null;
                        say = $"strike salvo from {guns.Sum(t => t.muzzles.Length)} muzzles";
                        break;
                    default:
                        var tb = g.GetComponent<TractorBeam>();
                        gear = tb != null && g.PointsOf("tractor").Length >= 2 && g.GetComponent<ReaperTactics>() != null;
                        say = $"tractor emitters ({g.PointsOf("tractor").Length}), {tb?.range / 1000f:0.#} km";
                        break;
                }
                Check(info != null && info.Hostile && info.group == GreysShip.Faction && sig != null && sig.value < 1f && g.GetComponent<Armor>() == null
                      && shield != null && shield.capacity > 0f && gear,
                      $"{r}: {info?.Name}, {info?.shipClass}, signature {sig?.value:0.##}, shield {shield?.capacity:F0}, no armour; {say}");
            }

            // ---- the frigate there: the Scout, cloaked, unseen from beyond what it can find
            yield return WarpTo(kb, "Outer Drift", "g1_arrive");
            var scout = byRole.TryGetValue(GreysShip.Role.Scout, out var sc) ? sc : null;
            var sysF = cc.GetComponent<ShipSystems>();
            if (scout != null)
            {
                float eccm = FrigateClass.EccmOf(cc);
                float sees = Sensors.PlayerRange * 0.6f * 0.1f * (1f + eccm);
                float d = Vector3.Distance(scout.transform.position, cc.transform.position);
                Sensors.Forget();
                var cloak = scout.GetComponent<Cloak>();
                yield return null;
                Check(d > sees && cloak != null && cloak.Up && !Sensors.Detected(scout.transform),
                      $"the Scout, cloaked, {d / 1000f:0.#} km off: unseen (the frigate finds it only inside {sees / 1000f:0.#} km)");
            }

            // ---- sprung: close in on the cell's middle
            var shieldF = cc.GetComponentInChildren<Shield>(true);
            if (shieldF != null) { shieldF.SetOffline(false); shieldF.SetCapacity(1e7f); }
            group.Spring();
            Vector3 middle = group.transform.position;
            PlaceShip(middle + (cc.transform.position - middle).normalized * 5000f, middle - cc.transform.position);
            float until = Time.time + 25f;
            while (Time.time < until && (scout == null || Designation.PainterOf(cc.transform) != scout.transform)) yield return null;
            Check(scout != null && Designation.PainterOf(cc.transform) == scout.transform,
                  $"the Scout paints the frigate ({scout?.GetComponent<ScoutTactics>()?.Doing}); the HUD: '{Designation.HudText(cc.transform).Replace("\n", " / ")}'");
            Shot("g2_painted");

            // the Scout calls in the nearest other band of the Greys (the watcher at the Trojans,
            // far off): it drops out of warp near the frigate, sprung
            var watcherG = GameObject.Find("Greys watcher")?.GetComponent<EncounterGroup>();
            var st = scout != null ? scout.GetComponent<ScoutTactics>() : null;
            until = Time.time + 10f;
            while (Time.time < until && (st == null || st.Calls == 0)) yield return null;
            var called = watcherG != null && watcherG.pilots != null ? watcherG.pilots.FirstOrDefault(p => p != null) : null;
            float calledOff = called != null ? Vector3.Distance(called.transform.position, cc.transform.position) : -1f;
            Check(st != null && st.Calls > 0 && watcherG != null && watcherG.Active && calledOff >= 0f && calledOff < 15000f,
                  $"the Scout calls in another band: the watcher from the Trojans, sprung and dropped out of warp {calledOff / 1000f:0.#} km from the frigate (calls {st?.Calls}, sprung {watcherG?.Active}, held off {watcherG?.holdOff}, its Scout {called?.name})");

            // the Shroud: jamming, and its EMP burst on the frigate close in
            var shroud = byRole.TryGetValue(GreysShip.Role.Shroud, out var sh) ? sh : null;
            if (shroud != null)
            {
                PlaceShip(shroud.transform.position + (cc.transform.position - shroud.transform.position).normalized * 3000f, shroud.transform.position - cc.transform.position);
                until = Time.time + 15f;
                while (Time.time < until && Jamming.On(cc.transform) <= 0f) yield return null;
                float jam = Jamming.On(cc.transform);
                Check(jam > 0f, $"the Shroud jams the frigate: {jam:P0} after its ECCM ({shroud.GetComponent<ShroudTactics>()?.Doing})");
                int knocks0 = sysF != null ? sysF.Knocks : 0;
                PlaceShip(shroud.transform.position + (cc.transform.position - shroud.transform.position).normalized * 1500f, shroud.transform.position - cc.transform.position);
                until = Time.time + 20f;
                while (Time.time < until && sysF != null && sysF.Knocks == knocks0) yield return null;
                Check(sysF != null && sysF.Knocks > knocks0, $"its EMP (burst or projector) knocks a component of the frigate's out ({(sysF != null ? sysF.Knocks - knocks0 : 0)})");
                Shot("g3_jammed");
            }

            // the Lances: from a flank
            var lances = ships.Where(g => g.role == GreysShip.Role.Lance).Select(g => g.GetComponent<LanceTactics>()).Where(t => t != null).ToList();
            until = Time.time + 20f;
            while (Time.time < until && !lances.Any(l => l.Current != LanceTactics.Phase.None)) yield return null;
            var lance = lances.FirstOrDefault(l => l.Current != LanceTactics.Phase.None);
            Check(lance != null, $"the Lances go for the frigate from a flank ({lance?.Doing})");

            // the Reaper: the frigate's engines out, it closes and locks on
            var reaper = byRole.TryGetValue(GreysShip.Role.Reaper, out var rp) ? rp : null;
            if (reaper != null && sysF != null)
            {
                sysF.Knock(ShipSystem.Engines, 120f);
                PlaceShip(reaper.transform.position + (cc.transform.position - reaper.transform.position).normalized * 2500f, reaper.transform.position - cc.transform.position);
                var rt = reaper.GetComponent<ReaperTactics>();
                until = Time.time + 45f;
                while (Time.time < until && TractorBeam.BeamsOn(cc.transform) == 0) yield return null;
                bool held = TractorBeam.BeamsOn(cc.transform) > 0;
                bool noWarp = !drive.CanWarp(out string why);
                Check(held && noWarp, $"the frigate disabled, the Reaper locks its tractor on ({rt?.Doing}); no warp ({why})");
                Shot("g4_held");

                // the abduction: crew taken alive a few at a time, the Greys' prisoners; then a
                // sample, the prize; the band vanishes, the Reaper towing the frigate off
                var ab = reaper.GetComponent<Abductor>();
                var crewF = cc.GetComponent<Crew>();
                var fleetG = group.GetComponent<GreysFleet>();
                if (ab != null)
                {
                    ab.interval = 0.6f;
                    int prisoners0 = WorldState.PrisonersOf(GreysShip.Faction);
                    float aboard0 = crewF != null ? crewF.Current : 0f;
                    until = Time.time + 30f;
                    while (Time.time < until && !ab.Done) yield return null;
                    int prisoners = WorldState.PrisonersOf(GreysShip.Faction) - prisoners0;
                    Check(ab.Done && ab.CrewTaken > 0 && prisoners > 0 && crewF != null && crewF.Current < aboard0 && ab.Samples == 1,
                          $"the Reaper abducts: {ab.CrewTaken} of the crew taken ({aboard0:0} -> {crewF?.Current:0} aboard), the Greys' prisoners now {prisoners}, a sample taken");
                    Shot("g5_abducted");
                    until = Time.time + 5f;
                    while (Time.time < until && (fleetG == null || fleetG.Current != GreysFleet.Phase.Vanish)) yield return null;
                    Vector3 held0 = cc.transform.position, reaper0 = reaper.transform.position;
                    // (the beam pulls once the gap is past its hold distance: a while after the Reaper sets off)
                    until = Time.time + 40f;
                    while (Time.time < until && Vector3.Distance(cc.transform.position, held0) < 100f && TractorBeam.BeamsOn(cc.transform) > 0) yield return null;
                    float towed = Vector3.Distance(cc.transform.position, held0);
                    Log($"  towing: the Reaper moved {Vector3.Distance(reaper.transform.position, reaper0):F0} m, held {TractorBeam.BeamsOn(cc.transform)}, " +
                        $"its pilot {reaper.GetComponent<CombatPilot>()?.Current}, the frigate's speed {cc.Velocity.magnitude:F1}, gap {Vector3.Distance(reaper.transform.position, cc.transform.position):F0} m");
                    Check(fleetG != null && fleetG.Current == GreysFleet.Phase.Vanish && group.holdOff && rt != null && rt.towing && towed >= 100f,
                          $"the prize taken, the band vanishes ({fleetG?.Why}; phases {string.Join(" > ", fleetG?.History ?? new List<GreysFleet.Phase>())}), the Reaper towing the frigate off ({towed:F0} m)");
                }
            }
            if (hull != null) hull.cannotDie = false;
            Log(passed == checks ? $"PASS: the Greys ({passed} of {checks})" : $"FAIL: the Greys ({passed} of {checks} passed)");
        }
    }
}

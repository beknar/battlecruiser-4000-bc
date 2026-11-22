using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -empiretest: the Empire (Data/empire.json), their task force holding Brontes.
    /// - Their four ships in it, each with its gear: the Assault frigate's ablative armour and
    ///   plates, CIWS, missiles and drone bay; the Patrol frigate's CIWS, marines and warp
    ///   inhibitor; the Raider's guns as point defence; the Recon's targeting laser and inhibitor.
    /// - Sprung, they fight as a line: the Recon paints the frigate; the Assault frigates' drones
    ///   come out; a Raider goes for the lightest target; their CIWS shoots a missile of the
    ///   frigate's down; a Patrol frigate puts a drone back in an Assault frigate's bay; the frigate
    ///   can't warp in an inhibitor's field; its engines out, the marines' pods launch at it; and
    ///   under half the band left, it falls back in order.
    /// </summary>
    public partial class SystemTest
    {
        bool empireOnly;

        IEnumerator EmpireTest(Keyboard kb)
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
            var group = GameObject.Find("Imperial task force")?.GetComponent<EncounterGroup>();
            var ships = group != null && group.pilots != null
                ? group.pilots.Where(p => p != null).Select(p => p.GetComponent<EmpireShip>()).Where(e => e != null).ToList()
                : new List<EmpireShip>();
            Check(group != null && ships.Count == group.pilots.Length && ships.Count >= 4,
                  $"the Imperial task force: {ships.Count} ships ({string.Join(", ", ships.Select(e => $"{e.name} ({e.role})"))})");
            if (group == null || ships.Count == 0) { Log("FAIL: the Empire (no task force)"); yield break; }
            EmpireShip First(EmpireShip.Role r) => ships.FirstOrDefault(e => e.role == r);
            foreach (EmpireShip.Role r in System.Enum.GetValues(typeof(EmpireShip.Role)))
            {
                var e = First(r);
                if (e == null) { Check(false, $"{r}: in the task force"); continue; }
                var info = e.GetComponent<TargetInfo>();
                var armor = e.GetComponent<Armor>();
                var plates = e.GetComponent<AblativePlates>();
                var pd = e.GetComponent<PointDefence>();
                bool gear;
                string say;
                switch (r)
                {
                    case EmpireShip.Role.Assault:
                        var bay = e.GetComponent<DroneBay>();
                        var w = e.GetComponent<EnemyWeapons>();
                        gear = armor != null && armor.rating >= 0.4f && plates != null && plates.Count >= 12 && pd != null && pd.mounts.Length >= 4
                               && bay != null && bay.bays.Length == 2 && w != null && w.missilePrefab != null && e.GetComponent<AssaultTactics>() != null;
                        say = $"armour {armor?.rating:0.##} ({plates?.Count} plates), CIWS {pd?.mounts.Length}, drones {bay?.stock} at {bay?.bays.Length} bays, missiles";
                        break;
                    case EmpireShip.Role.Patrol:
                        var pods = e.GetComponent<PodLauncher>();
                        gear = pd != null && pd.mounts.Length >= 2 && pods != null && pods.hatches.Length >= 2 && e.GetComponent<Interdictor>() != null && e.GetComponent<PatrolTactics>() != null;
                        say = $"CIWS {pd?.mounts.Length}, marines {pods?.perWave} a wave from {pods?.hatches.Length} hatches, a warp inhibitor";
                        break;
                    case EmpireShip.Role.Raider:
                        gear = pd != null && pd.mounts.Length >= 1 && e.GetComponent<RaiderTactics>() != null && e.GetComponent<CombatPilot>().style == CombatPilot.Style.Fighter;
                        say = $"its guns point defence ({pd?.mounts.Length}), a fighter's runs";
                        break;
                    default:
                        gear = e.GetComponent<Designator>() != null && e.GetComponent<Interdictor>() != null && e.sensorRange >= 30000f && e.GetComponent<ReconTactics>() != null;
                        say = $"a targeting laser, a warp inhibitor, sensors {e.sensorRange / 1000f:0} km";
                        break;
                }
                Check(info != null && info.Hostile && info.group == EmpireShip.Faction && gear, $"{r}: {info?.Name}, {info?.shipClass}; {say}");
            }

            // ---- there, sprung, the frigate 8 km off the line
            yield return WarpTo(kb, "Brontes", "e1_arrive");
            var shieldF = cc.GetComponentInChildren<Shield>(true);
            if (shieldF != null) { shieldF.SetOffline(false); shieldF.SetCapacity(1e7f); }
            var sysF = cc.GetComponent<ShipSystems>();
            if (sysF != null) sysF.toughness = 1e6f;
            group.Spring();
            Vector3 middle = group.transform.position;
            PlaceShip(middle + (cc.transform.position - middle).normalized * 8000f, middle - cc.transform.position);
            var recon = First(EmpireShip.Role.Recon);
            float until = Time.time + 25f;
            while (Time.time < until && (recon == null || Designation.PainterOf(cc.transform) != recon.transform)) yield return null;
            Check(recon != null && Designation.PainterOf(cc.transform) == recon.transform, $"the Recon paints the frigate ({recon?.GetComponent<ReconTactics>()?.Doing})");
            var assaults = ships.Where(e => e.role == EmpireShip.Role.Assault).Select(e => e.GetComponent<DroneBay>()).Where(b => b != null).ToList();
            // (the frigate 6 km off an Assault frigate: inside its bay's launch range)
            var firstBay = assaults.FirstOrDefault();
            if (firstBay != null) PlaceShip(firstBay.transform.position + (cc.transform.position - firstBay.transform.position).normalized * 6000f, firstBay.transform.position - cc.transform.position);
            until = Time.time + 25f;
            while (Time.time < until && assaults.Sum(b => b.Launched) == 0) yield return null;
            Check(assaults.Sum(b => b.Launched) > 0, $"the Assault frigates' drones come out ({assaults.Sum(b => b.Launched)} launched; " +
                  $"{string.Join(", ", assaults.Select(b => $"{b.name}: {Vector3.Distance(b.transform.position, cc.transform.position) / 1000f:0.#} km, stock {b.Stock}, pilot {b.GetComponent<CombatPilot>()?.Current}"))})");
            var raider = First(EmpireShip.Role.Raider)?.GetComponent<RaiderTactics>();
            until = Time.time + 15f;
            while (Time.time < until && (raider == null || raider.Prey == null)) yield return null;
            Check(raider != null && raider.Prey == cc.transform, $"a Raider goes for the lightest target ({raider?.Why}: {raider?.Prey?.name})");
            Shot("e2_line");

            // ---- their CIWS shoots a missile of the frigate's down: salvos of the frigate's own
            // missiles at an Assault frigate from 4 km off it (as the Vrask test does)
            var assault = First(EmpireShip.Role.Assault);
            var apd = assault != null ? assault.GetComponent<PointDefence>() : null;
            var mslPrefab = cc.missiles != null && cc.missiles.missilePrefab != null ? cc.missiles.missilePrefab : assault?.GetComponent<EnemyWeapons>()?.missilePrefab;
            if (assault != null && apd != null && mslPrefab != null)
            {
                int down0 = apd.ShotDown, fired = 0;
                for (int salvo = 0; salvo < 4 && apd.ShotDown == down0; salvo++)
                {
                    Vector3 toA = (assault.transform.position - cc.transform.position).normalized;
                    Vector3 launch = assault.transform.position - toA * 4000f;
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 up = (toA + cc.transform.up * 0.3f + cc.transform.right * (i - 1) * 0.3f).normalized;
                        var m = Instantiate(mslPrefab, launch + up * 60f, Quaternion.LookRotation(up));
                        m.owner = cc.transform;
                        m.target = assault.transform;
                        fired++;
                    }
                    until = Time.time + 12f;
                    while (Time.time < until && apd.ShotDown == down0) yield return null;
                }
                Check(apd.ShotDown > down0, $"the Assault frigate's CIWS shoots the frigate's missiles down ({apd.ShotDown - down0} of {fired}; engagements {apd.Engagements}, " +
                      $"{apd.mounts.Sum(t => t != null ? t.ShotsFired : 0)} shots)");
            }
            else Check(false, "the Assault frigate's CIWS (no frigate missile to fire at it)");

            // ---- a Patrol frigate's logistics: a drone back in the bay
            var patrols = ships.Where(e => e.role == EmpireShip.Role.Patrol).Select(e => e.GetComponent<PatrolTactics>()).Where(t => t != null).ToList();
            until = Time.time + 45f;
            while (Time.time < until && patrols.Sum(t => t.Supplied) == 0) yield return null;
            Check(patrols.Sum(t => t.Supplied) > 0, $"a Patrol frigate puts drones back in an Assault frigate's bay ({patrols.Sum(t => t.Supplied)})");

            // ---- an inhibitor's field over the frigate
            var field = Interdictor.At(cc.transform.position);
            Check(field != null, $"the frigate is in an Imperial warp inhibitor's field ({field?.Name}): no warp out");

            // ---- the frigate's engines out: the marines
            if (sysF != null)
            {
                sysF.Knock(ShipSystem.Engines, 120f);
                var podsAll = ships.Select(e => e.GetComponent<PodLauncher>()).Where(p => p != null).ToList();
                var patrolShip = First(EmpireShip.Role.Patrol);
                if (patrolShip != null)
                    PlaceShip(patrolShip.transform.position + (cc.transform.position - patrolShip.transform.position).normalized * 2200f, patrolShip.transform.position - cc.transform.position);
                until = Time.time + 40f;
                while (Time.time < until && podsAll.Sum(p => p.Launched) == 0) yield return null;
                Check(podsAll.Sum(p => p.Launched) > 0, $"the frigate disabled, the marines' pods launch at it ({podsAll.Sum(p => p.Launched)})");
                Shot("e3_marines");
            }

            // ---- under half the band left: it falls back in order
            var fleet = group.GetComponent<EmpireFleet>();
            var kill = ships.Where(e => e.role != EmpireShip.Role.Assault).Take(ships.Count / 2 + 1).ToList();
            foreach (var e in kill) { var h = e.GetComponent<Health>(); if (h != null && !h.Dead) h.Damage(1e9f, e.transform.position, HitKind.Cannon); }
            until = Time.time + 10f;
            while (Time.time < until && (fleet == null || fleet.Current != EmpireFleet.Phase.Retreat)) yield return null;
            var at = assault != null ? assault.GetComponent<AssaultTactics>() : null;
            Check(fleet != null && fleet.Current == EmpireFleet.Phase.Retreat && group.holdOff && at != null && at.retreating,
                  $"under half left, the band falls back in order ({fleet?.Why}; phases {string.Join(" > ", fleet?.History ?? new List<EmpireFleet.Phase>())})");
            if (hull != null) hull.cannotDie = false;
            Log(passed == checks ? $"PASS: the Empire ({passed} of {checks})" : $"FAIL: the Empire ({passed} of {checks} passed)");
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -vrasktest's fleet engagement (from VraskTest, after the siege, before each is destroyed):
    /// the whole raiding group, its fleet coordination (VraskFleet) and the Skinks' escort tactics
    /// (SkinkTactics, PointDefence) on, against the frigate (that can't be destroyed, with a crew
    /// too big to be taken). The group starts 11 km off, the Maw behind it. The phases come in the
    /// order the Vrask fight in: the Skinks run ahead on picket and make contact; the Maw's
    /// artillery opens first, the Snares and the Fang holding back; a missile the frigate fires
    /// at the Maw is run down and shot down by a Skink; the Snares go in and disable the frigate,
    /// a tractor holds it, and then the Fang boards it. Last the Maw and both Snares are destroyed:
    /// the rest withdraw into hiding and the band doesn't spring again till they are back.
    /// </summary>
    public partial class SystemTest
    {
        IEnumerator FleetEngagement(EncounterGroup group, List<VraskShip> ships, List<Behaviour> escorts, System.Action<bool, string> Check, Shield shieldF, float cap0)
        {
            var fleet = group != null ? group.GetComponent<VraskFleet>() : null;
            var maw = ships.FirstOrDefault(v => v != null && v.role == VraskShip.Role.Maw);
            var fang = ships.FirstOrDefault(v => v != null && v.role == VraskShip.Role.Fang);
            var snares = ships.Where(v => v != null && v.role == VraskShip.Role.Snare).ToList();
            var skinks = ships.Where(v => v != null && v.role == VraskShip.Role.Skink).ToList();
            var pds = skinks.Select(v => v.GetComponent<PointDefence>()).Where(x => x != null).ToList();
            var skinkT = skinks.Select(v => v.GetComponent<SkinkTactics>()).Where(x => x != null).ToList();
            Check(fleet != null && fleet.ships.Length == ships.Count && fleet.group == group && skinks.Count > 0 &&
                  pds.Count == skinks.Count && pds.All(x => x.mounts.Length >= 8 && x.missileScale > 1f) && skinkT.Count == skinks.Count &&
                  skinks.All(v => v.sensorRange > fleet.sensorRange),
                  $"the raiders' fleet coordination: {fleet?.ships.Length ?? 0} ships, hold line {fleet?.holdLine / 1000f:0.#} km; the Skinks' point defence on " +
                  $"{string.Join("/", pds.Select(x => x.mounts.Length))} guns, their tactics, sensors {skinks.FirstOrDefault()?.sensorRange / 1000f:0.#} km");
            if (fleet == null || maw == null || fang == null || snares.Count == 0 || skinks.Count == 0 || shieldF == null) yield break;

            var frigate = cc.transform;
            var hull = cc.GetComponent<Health>();
            var sysF = cc.GetComponent<ShipSystems>();
            var crew = cc.GetComponent<Crew>();
            var gun = maw.GetComponentInChildren<ArtilleryGun>(true);
            var fangT = fang.GetComponent<FangTactics>();
            var fangPods = fang.GetComponent<PodLauncher>();
            CarrierTurret[] Guns(VraskShip v) => v.GetComponentsInChildren<CarrierTurret>(true);

            // ---- all of them back, whole, out of hiding; the frigate whole, still, its own shield
            // up, and a crew too big for their boarders to take
            foreach (var v in ships) { var h = v.GetComponent<Health>(); if (h != null && h.Dead) h.RespawnNow(); }
            yield return null;
            foreach (var v in ships)
            {
                v.GetComponent<Health>()?.Repair();
                v.GetComponent<ShipSystems>()?.RepairAll();
                var sh = v.GetComponentInChildren<Shield>(true);
                if (sh != null) sh.SetOffline(false);
                var beam = v.GetComponent<TractorBeam>();
                if (beam != null && beam.Locked) beam.Release("the test");
            }
            var fb0 = Boarders.On(frigate);
            if (fb0 != null) fb0.Clear();
            hull.cannotDie = true;
            hull.Repair();
            if (sysF != null) sysF.RepairAll();
            shieldF.SetCapacity(cap0);
            shieldF.SetOffline(false);
            int complement0 = crew != null ? crew.complement : 0;
            if (crew != null) { crew.complement = Mathf.Max(complement0, 3000); crew.SetAboard(crew.complement); }
            cc.throttle = 0f;
            cc.SetTarget(null);    // (the frigate's own guns quiet: nothing of theirs is shot at but by the test)

            // the group 11 km off on the side away from the comet, the Maw 1.5 km behind it, the
            // Skinks among them, everyone bow on
            var place = WarpPoint.Nearest(frigate.position);
            Vector3 away = place != null ? frigate.position - place.Position : -frigate.forward;
            away = away.sqrMagnitude > 1f ? away.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(away, Vector3.up);
            side = side.sqrMagnitude > 0.01f ? side.normalized : Vector3.right;
            Vector3 c = frigate.position + away * 11000f;
            void Put(VraskShip v, Vector3 at) => v.transform.SetPositionAndRotation(at, Quaternion.LookRotation(frigate.position - at));
            Put(maw, c + away * 1500f + Vector3.up * 200f);
            for (int i = 0; i < snares.Count; i++) Put(snares[i], c + side * (i % 2 == 0 ? 700f : -700f) * (1 + i / 2));
            Put(fang, c + away * 400f + Vector3.up * 300f);
            for (int i = 0; i < skinks.Count; i++) Put(skinks[i], c + away * 300f + side * (i % 2 == 0 ? 1400f : -1400f) * (1 + i / 2) - Vector3.up * 200f);
            foreach (var v in ships)
            {
                var pl = v.GetComponent<CombatPilot>();
                if (pl != null) pl.enabled = true;
                foreach (var b in new Behaviour[] { v.GetComponent<SnareTactics>(), v.GetComponent<FangTactics>(), v.GetComponent<MawTactics>(), v.GetComponent<PodLauncher>() })
                    if (b != null) b.enabled = true;
                var w = v.GetComponent<EnemyWeapons>();
                if (w != null) { w.weaponsFree = true; w.SetTarget(frigate); }
            }
            foreach (var b in escorts) if (b != null) b.enabled = true;
            fleet.enabled = true;
            foreach (var v in ships)
            {
                var pl = v.GetComponent<CombatPilot>();
                if (pl == null) continue;
                pl.target = frigate;
                if (pl.Current == CombatPilot.State.Lurk) pl.Engage();
            }
            int phases0 = fleet.Phases.Count, history0 = fleet.History.Count;
            int art0 = gun != null ? gun.Fired : 0;
            var shots0 = ships.ToDictionary(v => v, v => Guns(v).Sum(t => t.ShotsFired));
            int clamps0 = fangT != null ? fangT.Clamps : 0, podWaves0 = fangPods != null ? fangPods.Waves : 0;
            float t0 = Time.time, firstArt = -1f, firstOther = -1f, heldMin = float.MaxValue, fangMin = float.MaxValue;
            float disabledAt = -1f, tractorAt = -1f, fangAt = -1f;
            string firstWho = "";
            VraskFleet.Phase artPhase = VraskFleet.Phase.Idle;

            // watched every frame: the first shots, how near the Snares and the Fang came while held
            void Watch()
            {
                float now = Time.time - t0;
                var ph = fleet.Current;
                if (firstArt < 0f && gun != null && gun.Fired > art0) { firstArt = now; artPhase = ph; }
                if (firstOther < 0f)
                    foreach (var v in ships)
                        if (Guns(v).Sum(t => t.ShotsFired) > shots0[v]) { firstOther = now; firstWho = v.name; break; }
                if (ph == VraskFleet.Phase.Picket || ph == VraskFleet.Phase.Bombard)
                    foreach (var v in snares.Append(fang))
                        if (!v.GetComponent<Health>().Dead) heldMin = Mathf.Min(heldMin, Vector3.Distance(v.transform.position, frigate.position));
                if (ph != VraskFleet.Phase.Board && fangAt < 0f) fangMin = Mathf.Min(fangMin, Vector3.Distance(fang.transform.position, frigate.position));
                if (disabledAt < 0f && sysF != null && (sysF.EnginesDown || sysF.IsDown(ShipSystem.Shields))) disabledAt = now;
                if (tractorAt < 0f && TractorBeam.BeamsOn(frigate) > 0) tractorAt = now;
                if (fangAt < 0f && fangT != null && (fangT.Clamps > clamps0 || (fangPods != null && fangPods.Waves > podWaves0))) fangAt = now;
            }
            IEnumerator Until(System.Func<bool> done, float seconds)
            {
                float end = Time.time + seconds;
                while (Time.time < end && !done()) { Watch(); yield return null; }
                Watch();
            }
            List<VraskFleet.Phase> Seen() => fleet.Phases.Skip(phases0).ToList();
            void LogHistory() { foreach (var line in fleet.History.Skip(history0)) Log($"  fleet: {line}"); history0 = fleet.History.Count; }

            // ---- approach and picket: the Skinks ahead make contact
            yield return Until(() => fleet.Current != VraskFleet.Phase.Idle && fleet.Current != VraskFleet.Phase.Picket, 75f);
            LogHistory();
            var seen = Seen();
            var spotter = fleet.ContactMadeBy;
            Vector3 centre = fleet.Centre(false);
            Check(seen.Count >= 2 && seen[0] == VraskFleet.Phase.Picket && skinkT.Any(x => x.Pickets > 0) && spotter != null && spotter.role == VraskShip.Role.Skink &&
                  fleet.ContactMadeAt > fleet.sensorRange,
                  $"approach and picket: the Skinks ran ahead ({string.Join(", ", skinkT.Select(x => $"{x.name} {x.Pickets}"))}); contact by {spotter?.name} at " +
                  $"{fleet.ContactMadeAt / 1000f:0.0} km (beyond the {fleet.sensorRange / 1000f:0} km the others see), the rest {Vector3.Distance(centre, frigate.position) / 1000f:0.0} km off");

            // ---- bombard: the Maw's artillery first, the Snares and the Fang held back
            yield return Until(() => firstArt >= 0f, gun != null ? gun.reload * 2f + 30f : 1f);
            LogHistory();
            // (at contact: the Maw may think before the fleet does, a quarter-second either way)
            Check(firstArt >= 0f && (firstOther < 0f || firstArt <= firstOther) && seen.Count >= 2 && seen[1] == VraskFleet.Phase.Bombard &&
                  (artPhase == VraskFleet.Phase.Bombard || artPhase == VraskFleet.Phase.Picket),
                  $"the artillery opens first: the Maw's shell at {firstArt:0.0} s in the {VraskFleet.Name(artPhase)} phase, " +
                  $"before any other Vrask gun ({(firstOther < 0f ? "none yet" : $"{firstWho} at {firstOther:0.0} s")})");
            yield return Until(() => fleet.Current != VraskFleet.Phase.Bombard, fleet.bombardTime + 10f);
            LogHistory();
            Check(heldMin >= fleet.holdLine * 0.85f, $"while it shelled, the Snares and the Fang held the line: the nearest came {heldMin / 1000f:0.0} km " +
                                                    $"(the line {fleet.holdLine / 1000f:0.0} km)");

            // ---- screen and disable: a missile from the frigate at the Maw, run down by a Skink;
            // the Snares disable the frigate, a tractor holds it, then the Fang boards
            var prefab = cc.missiles != null && cc.missiles.missilePrefab != null ? cc.missiles.missilePrefab : fang.GetComponent<EnemyWeapons>()?.missilePrefab;
            int downedBySkink = 0, salvos = 0, fired = 0;
            string downedBy = "";
            void OnDown(Missile m, Transform by)
            {
                if (m == null || by == null || m.owner != frigate) return;
                var v = by.GetComponentInParent<VraskShip>();
                if (v != null && v.role == VraskShip.Role.Skink) { downedBySkink++; downedBy = v.name; }
            }
            Missile.Downed += OnDown;
            float nextSalvo = Time.time, end = Time.time + 420f;
            int intercepts0 = skinkT.Sum(x => x.Intercepts);
            try
            {
                while (Time.time < end && (fangAt < 0f || (prefab != null && downedBySkink == 0 && salvos < 4) || Time.time < nextSalvo - 6f))
                {
                    bool screening = fleet.Current == VraskFleet.Phase.Disable || fleet.Current == VraskFleet.Phase.Board;
                    if (prefab != null && downedBySkink == 0 && salvos < 4 && Time.time >= nextSalvo && screening && !maw.GetComponent<Health>().Dead)
                    {
                        salvos++;
                        nextSalvo = Time.time + 15f;
                        // (launched from 4 km off the Maw, as if from a ship that had closed: from where
                        // the frigate is held, 12 km back, a missile burns out before it gets there)
                        Vector3 toMaw = (maw.transform.position - frigate.position).normalized;
                        Vector3 launch = maw.transform.position - toMaw * 4000f;
                        for (int i = 0; i < 3; i++)
                        {
                            Vector3 up = (toMaw + frigate.up * 0.3f + frigate.right * (i - 1) * 0.3f).normalized;
                            var m = Object.Instantiate(prefab, launch + up * 60f, Quaternion.LookRotation(up));
                            m.owner = frigate;
                            m.target = maw.transform;
                            fired++;
                        }
                        Log($"  3 of the frigate's missiles at the Maw ({salvos}), from 4 km off it (the frigate {Vector3.Distance(frigate.position, maw.transform.position) / 1000f:0.0} km); the fleet: {VraskFleet.Name(fleet.Current)}");
                    }
                    Watch();
                    yield return null;
                }
            }
            finally { Missile.Downed -= OnDown; }
            LogHistory();
            foreach (var pdc in pds)
            {
                var w = pdc.GetComponent<EnemyWeapons>();
                var ms = pdc.mounts ?? new CarrierTurret[0];
                Log($"  {pdc.name}'s point defence: on {pdc.isActiveAndEnabled}, engagements {pdc.Engagements}, weapons free {(w != null ? w.weaponsFree.ToString() : "-")}, " +
                    $"{ms.Length} mounts, {ms.Sum(t => t != null ? t.ShotsFired : 0)} shots, aimed at {string.Join(",", ms.Select(t => t != null && t.target != null ? t.target.name : "-").Distinct())}");
            }
            Check(downedBySkink > 0 && skinkT.Sum(x => x.Intercepts) > intercepts0 && pds.Sum(x => x.Engagements) > 0,
                  $"point defence: of {fired} missiles at the Maw, {downedBySkink} shot down by a Skink ({downedBy}); intercepts " +
                  $"{string.Join(", ", skinkT.Select(x => $"{x.name} {x.Intercepts} ({x.LastIntercept})"))}");
            seen = Seen();
            int iDisable = seen.IndexOf(VraskFleet.Phase.Disable), iBoard = seen.IndexOf(VraskFleet.Phase.Board);
            Check(disabledAt >= 0f && tractorAt >= 0f && fangAt >= 0f && disabledAt <= tractorAt + 0.5f && tractorAt <= fangAt && iDisable >= 0 && iBoard > iDisable,
                  $"screen and disable, then board: the Snares knock out the frigate's {(sysF != null && sysF.EnginesDown ? "engines" : "shield generator")} at {disabledAt:0} s, " +
                  $"a tractor holds it at {tractorAt:0} s, the Fang boards at {fangAt:0} s ({(fangT != null ? fangT.Clamps - clamps0 : 0)} clamp(s), " +
                  $"{(fangPods != null ? fangPods.Waves - podWaves0 : 0)} pod wave(s)); phases {string.Join(" > ", seen.Select(VraskFleet.Name))}");
            Check(fangMin >= fleet.holdLine * 0.6f, $"the Fang held off till the boarding: it came no nearer than {fangMin / 1000f:0.0} km before it");
            Check(!hull.Captured && !hull.Dead, $"the frigate's big crew holds ({crew?.Current:0} aboard): not captured");

            // ---- the Maw and both Snares destroyed: the rest withdraw
            foreach (var v in snares.Append(maw))
            {
                var h = v.GetComponent<Health>();
                for (int i = 0; i < 4 && h != null && !h.Dead; i++) h.Damage(h.maxHealth * 20f, v.transform.position, HitKind.Penetrator);
            }
            yield return Until(() => fleet.Current == VraskFleet.Phase.Withdraw, 3f);
            yield return new WaitForSeconds(1f);
            LogHistory();
            var rest = ships.Where(v => v.role == VraskShip.Role.Fang || v.role == VraskShip.Role.Skink).Select(v => v.GetComponent<CombatPilot>()).Where(p => p != null).ToList();
            Check(fleet.Current == VraskFleet.Phase.Withdraw && group.holdOff && rest.All(p => p.Current == CombatPilot.State.Lurk) &&
                  (fangT == null || !fangT.IsClamped) && TractorBeam.BeamsOn(frigate) == 0,
                  $"the Maw and both Snares lost: the fleet withdraws ({VraskFleet.Name(fleet.Current)}: {fleet.Why}); the rest back into hiding " +
                  $"({string.Join(", ", rest.Select(p => $"{p.name} {p.Current}"))}), the Fang cast off, no beam on the frigate, the band won't spring {group.holdOff}");

            // ---- back as it was (the frigate still can't be destroyed: VraskTest's)
            var fb = Boarders.On(frigate);
            if (fb != null) fb.Clear();
            if (crew != null) { crew.complement = complement0; crew.SetAboard(complement0); }
            if (sysF != null) sysF.RepairAll();
            shieldF.SetCapacity(1e7f);
            shieldF.SetOffline(false);
            cc.throttle = 0f;
        }
    }
}

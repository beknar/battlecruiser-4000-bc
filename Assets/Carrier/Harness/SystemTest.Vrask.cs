using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using K = UnityEngine.InputSystem.Key;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -vrasktest (?vrasktest): the Vrask, the alien faction. Their raiding group lies in wait
    /// round Comet Mirren: all four of their ships are there (Snare, Fang, Maw, Skink), each
    /// hostile and of the Vrask, armoured, shielded, with its systems and its own weapons (ion
    /// bolts; heavy short-range guns and missiles; an armour-piercing spinal gun; rapid light
    /// guns), the Maw the slowest; they are a location the SpawnDirector spawns. The frigate (that
    /// can't be destroyed, its shield made too strong to fall so every hit is counted by its kind)
    /// warps there and closes on the comet until they spring; while they fight the warp drive is
    /// locked, and each of the four fires on it, ion bolts and armour-piercing slugs among the hits.
    /// The Snares then capture it: ion knocks out its shield generator and engines, a tractor beam
    /// holds it and pulls it in, the warp drive is refused, and the beam lets go when its Snare
    /// dies (SystemTest.Tractor).
    /// Then each is destroyed, coming apart with its shield down, and once the place's quiet is
    /// over they are back, whole, shields up. No screenshots.
    /// </summary>
    public partial class SystemTest
    {
        bool vraskOnly;

        IEnumerator VraskTest(Keyboard kb)
        {
            int checks = 0, passed = 0;
            void Check(bool ok, string what)
            {
                checks++;
                if (ok) passed++;
                Log($"{(ok ? "ok" : "FAIL")}: {what}");
            }
            yield return Key(kb, K.H);
            var hull = cc.GetComponent<Health>();
            if (hull != null) hull.cannotDie = true;

            // ---- the group and its four kinds of ship
            var group = GameObject.Find("Vrask raiders")?.GetComponent<EncounterGroup>();
            var ships = group != null && group.pilots != null
                ? group.pilots.Where(p => p != null).Select(p => p.GetComponent<VraskShip>()).Where(v => v != null).ToList()
                : new List<VraskShip>();
            Check(group != null && ships.Count == group.pilots.Length && ships.Count >= 4,
                  $"the Vrask raiders: {ships.Count} ships ({string.Join(", ", ships.Select(v => $"{v.name} ({v.role})"))})");
            if (group == null || ships.Count == 0) { Log("FAIL: the Vrask (no raiding group)"); yield break; }
            var byRole = new Dictionary<VraskShip.Role, VraskShip>();
            foreach (var v in ships) if (!byRole.ContainsKey(v.role)) byRole[v.role] = v;
            var roles = (VraskShip.Role[])System.Enum.GetValues(typeof(VraskShip.Role));

            CarrierTurret[] Guns(VraskShip v) => v.GetComponentsInChildren<CarrierTurret>(true);
            bool Fires(VraskShip v, HitKind kind) => Guns(v).Any(t => t.projectilePrefab != null && t.projectilePrefab.kind == kind);
            string Arms(VraskShip v)
            {
                var w = v.GetComponent<EnemyWeapons>();
                var guns = Guns(v).Where(t => t.projectilePrefab != null).GroupBy(t => t.projectilePrefab.kind)
                    .Select(g => $"{g.Count()} {g.Key} ({g.Min(t => t.fireInterval):0.##} s, {g.Max(t => t.range) / 1000f:0.#} km)");
                return string.Join(", ", guns) + (w != null && w.missilePrefab != null ? $", missiles {w.salvoSize} every {w.salvoInterval:0} s" : "");
            }
            foreach (var r in roles)
            {
                if (!byRole.TryGetValue(r, out var v)) { Check(false, $"{r}: in the group"); continue; }
                var info = v.GetComponent<TargetInfo>();
                var armor = v.GetComponent<Armor>();
                var shield = v.GetComponentInChildren<Shield>(true);
                var health = v.GetComponent<Health>();
                // PR5b: the ship's systems (by name, so this compiles before ShipSystems is in)
                bool systems = v.GetComponent("ShipSystems") != null;
                Check(info != null && info.Hostile && info.group == VraskShip.Faction && info.Name.StartsWith("Vrask ") &&
                      armor != null && armor.rating >= 0.25f && armor.rating <= 0.45f && shield != null && shield.capacity > 0f && shield.Up &&
                      health != null && health.maxHealth > 0f && systems && v.GetComponent<ShipBreakup>() != null,
                      $"{r}: {info?.Name}, {info?.shipClass} ({info?.FactionLabel}), hull {health?.maxHealth:F0}, shield {shield?.capacity:F0}, " +
                      $"armour {armor?.rating:F2} ({armor?.plating:F0}), systems {systems}, breaks up {v.GetComponent<ShipBreakup>() != null}");
                var w = v.GetComponent<EnemyWeapons>();
                var guns = Guns(v);
                bool armed;
                switch (r)
                {
                    case VraskShip.Role.Snare: armed = Fires(v, HitKind.Ion) && v.PointOf("tractor") != null; break;
                    case VraskShip.Role.Fang:
                        armed = guns.Any(t => t.projectilePrefab != null && t.projectilePrefab.kind == HitKind.Cannon && t.range <= 2000f) &&
                                w != null && w.missilePrefab != null && v.PointOf("breach") != null; break;
                    case VraskShip.Role.Maw:
                        armed = guns.Any(t => t.projectilePrefab != null && t.projectilePrefab.kind == HitKind.Penetrator && t.range >= 6000f) &&
                                guns.Count(t => t.projectilePrefab != null && t.projectilePrefab.kind != HitKind.Penetrator) >= 3 &&
                                v.PointsOf("nuke_cell").Length > 0; break;
                    default:
                        armed = guns.Count(t => t.projectilePrefab != null && t.fireInterval <= 0.6f) >= 8; break;
                }
                Check(w != null && w.turrets != null && w.turrets.Length == guns.Length && armed, $"{r}'s weapons: {Arms(v)}");
            }
            // the Maw the slowest of them
            var pilots = ships.Select(v => v.GetComponent<CombatPilot>()).Where(p => p != null).ToList();
            if (byRole.TryGetValue(VraskShip.Role.Maw, out var mawShip))
            {
                var maw = mawShip.GetComponent<CombatPilot>();
                var others = pilots.Where(p => p.GetComponent<VraskShip>().role != VraskShip.Role.Maw).ToList();
                Check(maw != null && others.Count > 0 && others.All(p => maw.attackSpeed < p.attackSpeed && maw.cruiseSpeed < p.cruiseSpeed && maw.acceleration < p.acceleration),
                      $"the Maw is the slowest: {maw?.cruiseSpeed:F0}/{maw?.attackSpeed:F0} m/s, {maw?.acceleration:F0} m/s2 " +
                      $"(the others {string.Join(", ", others.Select(p => $"{p.GetComponent<VraskShip>().role} {p.cruiseSpeed:F0}/{p.attackSpeed:F0}, {p.acceleration:F0}"))})");
            }
            // a location the spawns come back to; pilots the warp drive and the music know of
            var sd = SpawnDirector.Instance;
            var site = sd != null ? sd.Sites.FirstOrDefault(x => x.name == group.groupName) : null;
            Check(site != null && site.enemies.Count == ships.Count, $"the SpawnDirector has them as a location: {site?.name} ({site?.enemies.Count ?? 0})");
            Check(pilots.Count == ships.Count && pilots.All(p => CombatPilot.Active.Contains(p)) && ships.All(v => v.GetComponent<TargetInfo>().Hostile),
                  $"their pilots are among the active enemy pilots ({pilots.Count(p => CombatPilot.Active.Contains(p))} of {ships.Count}: the warp lock, the music) " +
                  "and they are hostile (co-op registers them)");

            // ---- to them: a warp to the place, then in toward it till they spring
            var place = WarpPoint.Nearest(ships[0].transform.position);
            if (place == null) { Check(false, "a place they wait at"); yield break; }
            Log($"  they wait round {place.displayName}, {ships.Min(v => Vector3.Distance(v.transform.position, place.Position)) / 1000f:F1} to " +
                $"{ships.Max(v => Vector3.Distance(v.transform.position, place.Position)) / 1000f:F1} km from it; spotting at {group.spotRange / 1000f:F1} km");
            if (!drive.Engage(place, out string why)) { Check(false, $"a warp to {place.displayName}: refused ({why})"); yield break; }
            yield return null;
            float until = Time.time + 150f;
            while (drive.Current != WarpDrive.State.Ready && Time.time < until) yield return null;
            yield return new WaitForSeconds(1f);
            float nearKm = ships.Min(v => Vector3.Distance(v.transform.position, cc.transform.position)) / 1000f;
            Log($"  arrived at {place.displayName}: the nearest of them {nearKm:F1} km off, attacking {group.Active}");
            Check(drive.Current == WarpDrive.State.Ready && !group.Active,
                  $"arrived at {place.displayName} without being set on at once (they lie in wait: the nearest {nearKm:F1} km off)");

            var health0 = cc.GetComponent<Health>();
            var shieldF = cc.GetComponentInChildren<Shield>(true);
            float cap0 = shieldF != null ? shieldF.capacity : 0f;
            // (the frigate's shield made too strong to fall: every hit lands on it, counted by its kind)
            if (shieldF != null) { shieldF.SetOffline(false); shieldF.SetCapacity(1e7f); }
            // (and its components too tough to wear for now, so the shield stays up for the count:
            // the Snares' ion goes for its generator; the capture, after, lets them)
            var sysF = cc.GetComponent<ShipSystems>();
            float tough0 = sysF != null ? sysF.toughness : 0.12f;
            if (sysF != null) sysF.toughness = 1e6f;
            cc.throttle = 1f;
            until = Time.time + 120f;
            while (Time.time < until && !group.Active)
            {
                Vector3 to = place.Position - cc.transform.position;
                if (to.magnitude < 3500f) cc.throttle = 0f;
                var v = ShipAutopilot.BowOn(cc, place.Position);
                demo.autoRudder = v.x;
                demo.autoPitch = v.y;
                yield return null;
            }
            bool sprang = group.Active;
            Check(sprang, $"closing on {place.displayName}, they spring: attacking {sprang} after {120f - (until - Time.time):F0} s, " +
                          $"{Vector3.Distance(cc.transform.position, place.Position) / 1000f:F1} km from it");
            if (!sprang) group.Spring();
            cc.throttle = 0f;
            demo.autoRudder = null;
            demo.autoPitch = null;

            // ---- the fight: each of the four fires on the frigate
            var shots0 = ships.ToDictionary(v => v, v => Guns(v).Sum(t => t.ShotsFired));
            int Hits(HitKind k) => shieldF != null && (int)k < shieldF.HitsByKind.Length ? shieldF.HitsByKind[(int)k] : 0;
            int ion0 = Hits(HitKind.Ion), pen0 = Hits(HitKind.Penetrator), cannon0 = Hits(HitKind.Cannon);
            bool locked = false, engagedNear = false;
            string lockWhy = "";
            until = Time.time + 90f;
            while (Time.time < until)
            {
                if (!locked && drive.InCombat(out lockWhy)) locked = true;
                if (!engagedNear && CombatPilot.AnyEngagedNear(cc.transform.position, 15000f)) engagedNear = true;
                // (done early once every kind has fired and the slugs and ion bolts have landed)
                if (Time.time > until - 60f && roles.All(r => byRole.ContainsKey(r) && Guns(byRole[r]).Sum(t => t.ShotsFired) > shots0[byRole[r]]) &&
                    Hits(HitKind.Ion) > ion0 && Hits(HitKind.Penetrator) > pen0) break;
                yield return null;
            }
            Check(locked && engagedNear, $"in the fight the warp drive is locked ({lockWhy}), an enemy engaged near {engagedNear}");
            foreach (var r in roles)
            {
                if (!byRole.TryGetValue(r, out var v)) continue;
                var guns = Guns(v);
                int fired = guns.Sum(t => t.ShotsFired) - shots0[v];
                bool atMe = guns.Any(t => t.FiredAt.Contains(cc.name));
                var pl = v.GetComponent<CombatPilot>();
                Check(fired > 0 && atMe, $"the {r} fires on the frigate: {fired} shots, from {guns.Count(t => t.ShotsFired > 0)} of {guns.Length} guns, " +
                                         $"{Vector3.Distance(v.transform.position, cc.transform.position) / 1000f:F1} km off ({pl?.Current})");
            }
            Check(Hits(HitKind.Ion) > ion0 && Hits(HitKind.Penetrator) > pen0,
                  $"their hits on the frigate's shield: {Hits(HitKind.Ion) - ion0} ion, {Hits(HitKind.Penetrator) - pen0} armour-piercing, {Hits(HitKind.Cannon) - cannon0} cannon " +
                  $"(hull {health0?.Current:F0})");

            // ---- the Snares' capture: ion, the tractor, no warp, let go (SystemTest.Tractor)
            if (sysF != null) sysF.toughness = tough0;
            yield return SnareCapture(ships, Check);
            if (sysF != null) sysF.RepairAll();

            // ---- each can be destroyed: it comes apart, its shield down
            foreach (var r in roles)
            {
                if (!byRole.TryGetValue(r, out var v)) continue;
                var h = v.GetComponent<Health>();
                var sh = v.GetComponentInChildren<Shield>(true);
                for (int i = 0; i < 4 && h != null && !h.Dead; i++) h.Damage(h.maxHealth * 20f, v.transform.position, HitKind.Penetrator);
                yield return new WaitForSeconds(0.5f);
                Check(h != null && h.Dead && sh != null && !sh.Up, $"the {r} destroyed: dead {h?.Dead}, its shield down {sh != null && !sh.Up}");
            }
            // the rest of the group too, so the place is cleared
            foreach (var v in ships)
            {
                var h = v.GetComponent<Health>();
                for (int i = 0; i < 4 && h != null && !h.Dead; i++) h.Damage(h.maxHealth * 20f, v.transform.position, HitKind.Penetrator);
            }
            yield return new WaitForSeconds(2f);
            Check(site != null && site.Cleared, $"all {ships.Count} destroyed: the place is cleared ({site?.clearances ?? 0} clearances)");

            // ---- and back, once the quiet is over (not before their wrecks have settled)
            yield return new WaitForSeconds(Mathf.Max(1f, (sd != null ? sd.minDead : 20f) + 1f));
            if (sd != null) sd.EndQuietNow();
            until = Time.time + 6f;
            while (Time.time < until && ships.Any(v => v.GetComponent<Health>().Dead)) yield return null;
            yield return new WaitForSeconds(0.5f);
            bool back = ships.All(v => !v.GetComponent<Health>().Dead);
            bool whole = ships.All(v => v.GetComponent<Health>().Current >= v.GetComponent<Health>().maxHealth - 0.5f &&
                                        v.GetComponentInChildren<Shield>(true).Up && v.GetComponent<Armor>().Integrity > 0.999f);
            Check(back && whole && site != null && site.spawns > 0,
                  $"they come back with the spawns: {ships.Count(v => !v.GetComponent<Health>().Dead)} of {ships.Count} back, whole and shielded {whole} " +
                  $"({site?.spawns ?? 0} spawns)");

            if (shieldF != null) shieldF.SetCapacity(cap0);
            if (hull != null) hull.cannotDie = false;
            Log($"{(passed == checks ? "PASS" : "FAIL")}: the Vrask ({passed} of {checks})");
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -vrasktest's artillery and siege (from VraskTest, after the boarding): the Maw has its
    /// spinal artillery (10 to 14 km, a slow reload, slow shells bursting wide), its two nuclear
    /// missiles and its tactics. With the rest of the Vrask held still far off and the Maw 10 km
    /// behind the frigate (that can't be destroyed), it holds its fire while no other Vrask ship
    /// can see the frigate; once a Skink comes within 3 km of it, the Maw opens fire from beyond
    /// 8 km, a shield nullifier at the frigate's shield; the shell takes its time, lands on the
    /// frigate and its blast hurts a second ship 300 m off too, less. A nuclear missile at the
    /// frigate, whole and able to die, breaks its shield but doesn't destroy it; the silos hold
    /// two, no more. Then the siege: the Vrask siege group is on station over Port Ochre (a city on
    /// a world with air), on the game's clock and saved; a raid with a nuclear strike ruins every
    /// building in the city (WorldState), for far longer than a kinetic strike's hour, and kills
    /// many of its people.
    /// </summary>
    public partial class SystemTest
    {
        IEnumerator MawArtillery(List<VraskShip> ships, System.Action<bool, string> Check, Shield shieldF, float cap0)
        {
            var maw = ships.FirstOrDefault(v => v != null && v.role == VraskShip.Role.Maw);
            var gun = maw != null ? maw.GetComponentInChildren<ArtilleryGun>(true) : null;
            var tactics = maw != null ? maw.GetComponent<MawTactics>() : null;
            var silos = maw != null ? maw.GetComponent<NukeLauncher>() : null;
            var shell = gun != null ? gun.shellPrefab : null;
            Check(gun != null && gun.range >= 10000f && gun.range <= 14000f && gun.reload >= 12f && gun.reload <= 20f && shell != null &&
                  shell.speed >= 400f && shell.speed <= 700f && shell.blastRadius >= 400f && shell.blastRadius <= 600f && tactics != null &&
                  silos != null && silos.capacity == 2 && silos.nukePrefab != null && silos.cells != null && silos.cells.Length > 0,
                  $"the Maw's spinal artillery: {gun?.minRange / 1000f:0.#} to {gun?.range / 1000f:0.#} km every {gun?.reload:0} s, shells at {shell?.speed:0} m/s " +
                  $"bursting {shell?.blastRadius:0} m wide; {silos?.capacity ?? 0} nuclear missiles in {silos?.cells?.Length ?? 0} silos; tactics {tactics != null}");
            if (gun == null || tactics == null || silos == null || shell == null || shieldF == null) yield break;

            var frigate = cc.transform;
            var hull = cc.GetComponent<Health>();
            var ciws = cc.GetComponent<CiwsDirector>();
            var mawPilot = maw.GetComponent<CombatPilot>();
            var mawArms = maw.GetComponent<EnemyWeapons>();
            int nukeAfter0 = tactics.shellsBeforeNuke, bunched0 = tactics.bunched;
            tactics.shellsBeforeNuke = 1000;          // (no nuclear missile of its own choosing while this is counted)
            tactics.bunched = 1000;

            // the rest of the Vrask still and silent, well off; the frigate still, its shield too strong to fall
            var others = ships.Where(v => v != null && v != maw).ToList();
            var wasOn = new Dictionary<Behaviour, bool>();
            void Hold(Behaviour b) { if (b != null) { wasOn[b] = b.enabled; b.enabled = false; } }
            for (int i = 0; i < others.Count; i++)
            {
                var v = others[i];
                Hold(v.GetComponent<CombatPilot>());
                Hold(v.GetComponent<SnareTactics>());
                Hold(v.GetComponent<FangTactics>());
                Hold(v.GetComponent<PodLauncher>());
                var beam = v.GetComponent<TractorBeam>();
                if (beam != null && beam.Locked) beam.Release("the test");
                var w = v.GetComponent<EnemyWeapons>();
                if (w != null) w.weaponsFree = false;
                v.transform.position = frigate.position + Quaternion.AngleAxis(40f * i, frigate.up) * frigate.forward * (25000f + 500f * i);
            }
            var fb = Boarders.On(frigate);
            if (fb != null) fb.Clear();
            cc.throttle = 0f;
            hull.cannotDie = true;
            shieldF.SetCapacity(1e7f);
            shieldF.SetOffline(false);
            // the Maw 10 km behind the frigate, bow on, in the fight
            maw.transform.position = frigate.position - frigate.forward * 10000f + frigate.up * 300f;
            maw.transform.rotation = Quaternion.LookRotation(frigate.position - maw.transform.position);
            if (mawPilot != null)
            {
                if (mawPilot.Current != CombatPilot.State.Engage) mawPilot.Engage();
                mawPilot.target = frigate;
            }
            if (mawArms != null) { mawArms.weaponsFree = true; mawArms.SetTarget(frigate); }
            // (settled first: a shell already on its way, from before the others were moved off, lands)
            yield return new WaitForSeconds(gun.reload * 0.5f + 1f);
            int fired0 = gun.Fired, held0 = tactics.HeldUnspotted;
            yield return new WaitForSeconds(gun.reload + 2f);

            // ---- no other ship can see the frigate: it holds its fire
            Check(gun.Fired == fired0 && !tactics.Spotted && tactics.Target == frigate && tactics.HeldUnspotted > held0,
                  $"with no other Vrask ship near the frigate, the Maw {Vector3.Distance(maw.transform.position, frigate.position) / 1000f:0.#} km off holds its fire " +
                  $"(fired {gun.Fired - fired0}, spotted {tactics.Spotted}, held {tactics.HeldUnspotted - held0} times)");

            // ---- a Skink comes within 3 km of it: the Maw opens fire from beyond 8 km
            var spotter = others.FirstOrDefault(v => v.role == VraskShip.Role.Skink) ?? others.FirstOrDefault();
            if (spotter == null) { Check(false, "a Vrask ship to spot for the Maw"); yield break; }
            spotter.transform.position = frigate.position + frigate.right * 3000f;
            int blasts0 = ArtilleryShell.Blasts;
            int nullHits0 = shieldF.HitsByKind[(int)HitKind.Nullifier];
            float shield0 = shieldF.Strength;
            float until = Time.time + gun.reload * 2f + 6f;
            while (Time.time < until && gun.Fired == fired0) yield return null;
            float firedFrom = Vector3.Distance(maw.transform.position, frigate.position);
            Check(gun.Fired > fired0 && tactics.Spotted && tactics.Spotter == spotter && firedFrom > 8000f && gun.LastRange > 8000f,
                  $"once {spotter.name} is 3 km from the frigate, the Maw opens fire from {firedFrom / 1000f:0.#} km (its aim {gun.LastRange / 1000f:0.#} km off, " +
                  $"spotted by {tactics.Spotter?.name}; salvos {tactics.Salvos}, shared {tactics.SharedSalvos})");
            Check(gun.LastRound == ArtilleryGun.Round.Nullifier && tactics.AimedAt == ShipSystem.Shields,
                  $"at a shield that is up: a {gun.LastRound} round, aimed at the shield generator ({tactics.AimedAt})");

            // ---- the shell takes its time and bursts wide: the frigate and a ship 300 m off are both hit
            var dummy = new GameObject("Artillery test hulk");
            dummy.transform.position = frigate.position + frigate.right * 300f;
            var dh = dummy.AddComponent<Health>();
            dh.maxHealth = 5000f;
            dh.cannotDie = true;
            dummy.AddComponent<BoxCollider>().size = Vector3.one * 40f;
            var flying = gun.LastShell;
            float flight = gun.LastFlight, t0 = Time.time;
            until = Time.time + flight + 8f;
            while (Time.time < until && flying != null && !flying.Burst) yield return null;
            yield return null;
            var blast = ArtilleryShell.LastBlast;
            Check(flight >= 10f && ArtilleryShell.Blasts > blasts0 && blast.Hit(hull) && shieldF.HitsByKind[(int)HitKind.Nullifier] > nullHits0,
                  $"the shell lands after {Time.time - t0:0.#} s (its flight {flight:0.#} s) on the frigate: {blast.Count} ships in its {blast.radius:0} m blast, " +
                  $"the frigate taking {blast.ShareOf(hull):P0} of {blast.damage:0} as {blast.kind} (its shield {shield0:0} -> {shieldF.Strength:0})");
            Check(blast.Hit(dh) && dh.Current < dh.maxHealth && blast.ShareOf(dh) > 0f,
                  $"area damage: a hulk 300 m off is hit too, {blast.ShareOf(dh):P0} of it ({dh.maxHealth - dh.Current:0} damage)");
            Object.Destroy(dummy);

            // ---- a nuclear missile at the frigate (whole, and able to die): hurt, not destroyed
            // (the Maw's guns and shells held meanwhile: the nuke's blast alone is measured)
            if (mawArms != null) mawArms.weaponsFree = false;
            if (ciws != null) ciws.active = false;
            shieldF.SetCapacity(cap0);
            shieldF.SetOffline(false);
            hull.Repair();
            hull.cannotDie = false;
            silos.Restock();
            // (from inside the silos' reach: from 10 km the missile would run out of flight on the way)
            maw.transform.position = frigate.position - frigate.forward * 7000f + frigate.up * 300f;
            spotter.transform.position = frigate.position + frigate.right * 25000f;
            maw.transform.rotation = Quaternion.LookRotation(frigate.position - maw.transform.position);
            yield return null;
            float sh0 = shieldF.Strength, hu0 = hull.Current;
            int stock0 = silos.Stock, det0 = NukeMissile.Detonations;
            var nuke = silos.LaunchNow(frigate);
            Check(nuke != null && silos.Stock == stock0 - 1, $"a nuclear missile launched at the frigate: {stock0} -> {silos.Stock} left");
            until = Time.time + (nuke != null ? nuke.lifetime + 5f : 0f);
            while (Time.time < until && NukeMissile.Detonations == det0 && nuke != null) yield return null;
            yield return null;
            var nb = NukeMissile.LastBlast;
            bool hurt = shieldF.Strength < sh0 - 1f || hull.Current < hu0 - 1f;
            Check(NukeMissile.Detonations > det0 && nb.Hit(hull) && hurt && !hull.Dead && hull.Current > 0f,
                  $"in space it goes off ({nb.radius / 1000f:0.#} km blast, {nb.damage:0} at the centre, the frigate taking {nb.ShareOf(hull):P0}): " +
                  $"shield {sh0:0} -> {shieldF.Strength:0}, hull {hu0:0} -> {hull.Current:0}; the frigate is not destroyed");
            Log($"  the nuke went off {Vector3.Distance(nb.at, frigate.position):0} m from the frigate, {(nuke != null ? Vector3.Distance(nb.at, maw.transform.position) : -1f):0} m from the Maw");
            hull.cannotDie = true;
            if (hull.Dead) { until = Time.time + hull.respawnDelay + 4f; while (Time.time < until && hull.Dead) yield return null; }
            // the silos hold two, no more, till the Maw is back from being destroyed
            var second = silos.LaunchNow(spotter.transform);
            var third = silos.LaunchNow(spotter.transform);
            Check(second != null && third == null && silos.Stock == 0, $"the silos hold two: a second goes, a third doesn't ({silos.Launched} launched, {silos.Stock} left)");
            if (second != null) Object.Destroy(second.gameObject);
            silos.Restock();

            // ---- back as it was
            tactics.shellsBeforeNuke = nukeAfter0;
            tactics.bunched = bunched0;
            foreach (var kv in wasOn) if (kv.Key != null) kv.Key.enabled = kv.Value;
            foreach (var v in others) { var w = v.GetComponent<EnemyWeapons>(); if (w != null) w.weaponsFree = true; }
            if (mawArms != null) mawArms.weaponsFree = true;
            shieldF.SetCapacity(1e7f);
            shieldF.SetOffline(false);
            if (ciws != null) ciws.active = true;
        }

        IEnumerator Bombardment(System.Action<bool, string> Check)
        {
            var siege = VraskBombardment.All.FirstOrDefault();
            WarpPoint place = null, body = null;
            TileFeatures site = null;
            bool there = siege != null && siege.Site(out place, out body, out site);
            Check(there && VraskBombardment.HasAir(body), $"the Vrask siege group: {siege?.name} over {siege?.target} on {body?.displayName} (air {VraskBombardment.HasAir(body)})");
            if (!there) yield break;
            var grid = siege.GetComponentInParent<DefenseGrid>();
            var mawT = siege.GetComponent<MawTactics>();
            var siegeGun = siege.GetComponentInChildren<ArtilleryGun>(true);
            Vector3 city = OrbitalStrikes.GroundPoint(body, site.su, site.sv);
            float over = Vector3.Angle(siege.transform.position - body.Position, city - body.Position);
            float up = Vector3.Distance(siege.transform.position, body.Position) - body.bodyRadius;
            Check(grid != null && grid.weapons != null && grid.weapons.Length >= 2 && mawT != null && siegeGun != null && siege.battery != null && over < 15f,
                  $"on station over {siege.target}: the Maw and {(grid?.weapons?.Length ?? 1) - 1} escorts, {up / 1000f:0} km up, {over:0.#} deg off the city's zenith; " +
                  $"its artillery {siegeGun != null}, its tactics {mawT != null}");
            bool clocked = WorldState.TryTimer(siege.Timer, out double next) && WorldState.Snapshot().timers.Any(t => t.name == siege.Timer);
            Check(clocked && siege.NextAt > WorldState.Clock,
                  $"its raids on the game's clock and saved: the next in {(next - WorldState.Clock) / 60.0:0} min, every {siege.intervalMinutes:0} min");

            // ---- a raid with a nuclear strike: the whole city ruined, far longer than a kinetic strike's
            OrbitalStrikes.Buildings(place, out int total, out int ruined0, out _);
            float pool0 = IndustryLedger.Pool(siege.target);
            int strikes0 = WorldState.Strikes.Count, raids0 = siege.Raids, clouds0 = MushroomCloud.Raised;
            var eye = PlayerShips.Primary;
            bool inSight = eye != null && !PlanetLanding.Busy && Vector3.Distance(eye.position, siege.transform.position) < siege.seenWithin;
            siege.RaidNow(nuke: true, force: true);
            float until = Time.time + 45f;
            while (Time.time < until && WorldState.Strikes.Count == strikes0) yield return null;
            yield return null;
            OrbitalStrikes.Buildings(place, out int total1, out int ruined, out double soonest);
            var st = WorldState.Strikes.Count > strikes0 ? WorldState.Strikes[WorldState.Strikes.Count - 1] : default;
            Check(siege.Raids == raids0 + 1 && st.weapon != null && st.weapon.Contains("nuclear") && st.place == siege.target && total1 > 0 && ruined * 4 >= total1 * 3,
                  $"a scripted raid's nuclear strike on {siege.target}: {ruined} of {total1} buildings ruined (were {ruined0}), {st.buildings} by it, {st.casualties} killed; {siege.LastRaid}");
            // (seen from the ship: the burst's flash and its cloud, kilometres high, over the city)
            var cloud = MushroomCloud.Last;
            if (inSight)
                Check(MushroomCloud.Raised == clouds0 + 1 && cloud != null && cloud.Height >= 14000f,
                      $"its burst seen from the ship: a mushroom cloud rising over {siege.target}, to {(cloud != null ? cloud.Height / 1000f : 0f):0} km");
            else
                Check(MushroomCloud.Raised == clouds0, "its burst out of the ship's sight: no cloud drawn");
            double minutes = (soonest - WorldState.Clock) / 60.0;
            Check(!double.IsNaN(soonest) && minutes > OrbitalStrikes.BuildingRebuildMinutes * 2.0,
                  $"its ruins stand {minutes:0} min before any is rebuilt (a kinetic strike's: {OrbitalStrikes.BuildingRebuildMinutes:0} min)");
            float pool1 = IndustryLedger.Pool(siege.target);
            Check(pool0 <= 0f || pool1 < pool0 * 0.75f, $"and its people are killed: recruits there {pool0:0} -> {pool1:0}");
            Check(!siege.NukeReady && siege.NextAt > WorldState.Clock,
                  $"its silos are empty till they are filled again ({siege.nukeRestockMinutes:0} min); the next raid in {(siege.NextAt - WorldState.Clock) / 60.0:0} min");
        }
    }
}

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -sensortest: what the players' sensors see (Sensors, Signature), with a fixture next to the
    /// ship: a drone made hard to see (a twentieth of a ship's signature: seen inside 1.5 km of
    /// the 30, named inside 750 m), its guns off. Far out it is off the screen, the target list
    /// and the console's count, and it locks no warp; closer it is an unknown contact; closer
    /// still it is named; firing gives it away at any of those ranges. The plain pirates and the
    /// Vrask are seen as they always were (the other self-tests).
    /// The later PRs of improvements 7 add their fixtures here (the cloak, the jammer, the EMP).
    /// </summary>
    public partial class SystemTest
    {
        bool sensorOnly;

        IEnumerator SensorTest(Keyboard kb)
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
            bool warp0 = drive.CanWarp(out string why0);
            Log($"  before: warp ready {warp0} {why0}");

            var prefab = CoopSpawnables.Find("EnemyDrone");
            if (prefab == null) { Log("FAIL: sensors (no drone prefab to make a fixture of)"); yield break; }
            var me = cc.transform;
            Vector3 Ahead(float m) => me.position + me.forward * m + Vector3.up * 40f;
            var drone = Instantiate(prefab, Ahead(4000f), Quaternion.LookRotation(-me.forward));
            drone.name = "Sensor test drone";
            // (its guns off: a hit on the ship would light it up, and lock the warp for being under fire)
            foreach (var w in drone.GetComponentsInChildren<EnemyWeapons>()) w.enabled = false;
            foreach (var t in drone.GetComponentsInChildren<CarrierTurret>()) t.autoFire = false;
            // (its pilot on and out of hiding, engaged: what the warp lock counts; it can't fire)
            // (the drone as built circles on its own: that off, and a pilot for it, as a band's has)
            foreach (var m in drone.GetComponentsInChildren<OrbitMover>()) m.enabled = false;
            var pilot = drone.GetComponent<CombatPilot>();
            if (pilot == null) pilot = drone.AddComponent<CombatPilot>();
            // (not lying in wait from its start, and not flying itself: the test puts it where it wants)
            if (pilot != null) { pilot.concealed = false; pilot.startLurking = false; pilot.externalMotion = true; pilot.NetLurk(false); }
            var body = drone.GetComponent<Rigidbody>();
            // (the frigate's CIWS off: once it sees the drone it would shoot it, lighting it up)
            var ciws = cc.GetComponentsInChildren<CiwsDirector>();
            foreach (var c in ciws) c.enabled = false;
            var sig = drone.AddComponent<Signature>();
            sig.value = 0.05f;
            sig.lightUp = 3f;
            var dt = drone.transform;
            yield return null;

            // (where the test puts it, the physics too: a Rigidbody would put it back)
            void Put(float m)
            {
                dt.position = Ahead(m);
                if (body != null) { body.position = dt.position; body.linearVelocity = Vector3.zero; }
                if (pilot != null) pilot.NetLurk(false);
                Sensors.Forget();
            }
            string Why() => $"seen {Sensors.Detected(dt)}, named {Sensors.Identified(dt)}, in range {TargetDisplay.InRange(dt, me.position)}, " +
                             $"hidden {Sensors.Hidden(dt)} (ambush {CombatPilot.IsHidden(dt)}), {Vector3.Distance(dt.position, me.position):F0} m, signature {sig.Current:0.###}";
            bool Listed() => TargetDisplay.InRange(dt, me.position) && !Sensors.Hidden(dt);
            bool Counted() => !Sensors.Hidden(dt);

            // far out: not seen
            Put(4000f);
            yield return null;
            bool engaged = pilot != null && pilot.Current != CombatPilot.State.Lurk;
            bool warp = drive.CanWarp(out string why);
            Check(!Sensors.Detected(dt) && !Listed() && !Counted(),
                  $"4 km out, a twentieth of a ship's signature: not seen, not listed, not counted (seen {Sensors.Detected(dt)})");
            Check(engaged && (warp || why != "hostiles nearby"),
                  $"an engaged enemy the sensors can't see locks no warp (engaged {engaged}; warp {warp} {why})");

            // closer: an unknown contact
            Put(1200f);
            yield return null;
            Check(Sensors.Detected(dt) && Listed() && !Sensors.Identified(dt) && Sensors.NameOf(dt, "Hostile Drone") == Sensors.UnknownContact,
                  $"1.2 km: seen, listed, but an unknown contact ('{Sensors.NameOf(dt, "Hostile Drone")}'; {Why()})");
            warp = drive.CanWarp(out why);
            Check(!warp && why == "hostiles nearby", $"seen, it locks the warp as any enemy does ({warp} {why})");
            demo.SelectTarget(dt);
            yield return null;
            Shot("s1_unknown");
            // (untargeted again: the frigate's guns would fire on it, lighting it up)
            cc.SetTarget(null);

            // closer still: named
            Put(500f);
            yield return null;
            Check(Sensors.Identified(dt) && Sensors.NameOf(dt, "Hostile Drone") == "Hostile Drone", "500 m: named");

            // firing gives it away, from out where it couldn't be seen
            Put(4000f);
            yield return null;
            bool hidden = Sensors.Hidden(dt);
            sig.Fired();
            Sensors.Forget();
            Check(hidden && Sensors.Detected(dt) && Sensors.Identified(dt) && Listed(),
                  $"4 km out, it fires: seen and named at once (was hidden {hidden})");
            yield return new WaitForSeconds(sig.lightUp + 0.5f);
            Put(4000f);
            Sensors.Forget();
            Check(!Sensors.Detected(dt), $"{sig.lightUp:0} s quiet: gone from the sensors again ({Why()})");

            // a ship without a signature is seen as before
            Destroy(sig);
            yield return null;
            Sensors.Forget();
            Check(Sensors.Detected(dt) && Sensors.Identified(dt) && Listed(), "without a signature: seen and named at 4 km, as any ship");

            // ---- a cloak (PR 5): a ship's signature, a tenth of it cloaked; this frigate's ECCM
            // sees it from further (a Sentinel's 0.7: 30 x 0.1 x 1.7 = 5.1 km)
            sig = drone.AddComponent<Signature>();
            sig.lightUp = 2f;
            var cloak = drone.AddComponent<Cloak>();
            cloak.recloakTime = 1f;
            yield return null;
            float eccm = FrigateClass.EccmOf(cc);
            float cloakedRange = Sensors.PlayerRange * cloak.hiddenScale * (1f + eccm);
            Put(cloakedRange + 1500f);
            yield return null;
            Check(cloak.Up && !Sensors.Detected(dt), $"cloaked, {Vector3.Distance(dt.position, me.position) / 1000f:0.0} km out: unseen (ECCM {eccm:0.0}: seen inside {cloakedRange / 1000f:0.0} km)");
            Put(cloakedRange - 800f);
            yield return null;
            Check(Sensors.Detected(dt), $"cloaked, {Vector3.Distance(dt.position, me.position) / 1000f:0.0} km out: inside what this frigate's ECCM sees");
            Shot("s2_cloak");
            Put(cloakedRange + 1500f);
            sig.Fired();
            yield return null;
            Sensors.Forget();
            Check(!cloak.Up && Sensors.Detected(dt), "it fires: the cloak drops, and it is seen");
            yield return new WaitForSeconds(sig.lightUp + cloak.recloakTime + 0.5f);
            Put(cloakedRange + 1500f);
            Check(cloak.Up && !Sensors.Detected(dt), "quiet again: the cloak back, unseen");
            Destroy(cloak);
            Destroy(sig);
            yield return null;

            // ---- a targeting laser on this frigate: the HUD says who and from where
            Put(8000f);
            var laser = drone.AddComponent<Designator>();
            laser.range = 15000f;
            laser.target = me;
            yield return null;
            yield return null;
            string paint = Designation.HudText(me);
            Check(Designation.PainterOf(me) == dt && paint.StartsWith("Targeting laser: "), $"painted by a targeting laser: the HUD says '{paint.Replace("\n", " / ")}'");
            Shot("s3_painted");
            Destroy(laser);
            yield return new WaitForSeconds(Designation.Hold + 0.3f);
            Check(Designation.HudText(me).Length == 0, "the laser off: the warning goes");

            // ---- a warp inhibitor's field (the drone out of the fight, lying in wait: so only its
            // field can hold the warp, not "hostiles nearby")
            if (pilot != null) pilot.NetLurk(true);
            var field = drone.AddComponent<Interdictor>();
            field.radius = 12000f;
            yield return null;
            warp = drive.CanWarp(out why);
            Check(!warp && why.StartsWith("warp inhibited by"), $"inside an inhibitor's field: no warp ({warp} {why})");
            Destroy(field);
            yield return null;
            warp = drive.CanWarp(out why);
            Check(warp, $"the field gone: the warp free ({warp} {why})");

            // ---- a jammer (PR 6): the frigate's sensors cut, ghosts on them, missiles scrambled close in
            var far = new GameObject("Sensor test far contact").transform;
            far.position = me.position - me.forward * 25000f;
            bool farBefore = TargetDisplay.InRange(far, me.position);
            var jammer = drone.AddComponent<EcmEmitter>();
            jammer.radius = 6000f;
            jammer.strength = 0.8f;
            jammer.ghosts = 3;
            jammer.ghostRange = new Vector2(4000f, 5500f);
            Put(2000f);
            yield return null;
            float jam = Jamming.On(me);
            Sensors.Forget();
            bool farAfter = TargetDisplay.InRange(far, me.position);
            Check(jam > 0.2f && farBefore && !farAfter,
                  $"2 km from a jammer (80 %): jammed {jam:P0} after this frigate's ECCM; a contact 25 km off seen before, not now");
            var ghostList = new System.Collections.Generic.List<Sensors.Ghost>();
            Sensors.Ghosts(ghostList);
            Check(ghostList.Count > 0, $"ghost contacts on the jammed sensors: {ghostList.Count}");
            Shot("s4_jammed");
            int through0 = Sensors.GhostsSeenThrough;
            jammer.ghostRange = new Vector2(100f, 400f);
            Put(500f);
            yield return null;
            Sensors.Ghosts(ghostList);
            Check(ghostList.Count == 0 && Sensors.GhostsSeenThrough > through0, $"looked at closely, the ghosts are seen through ({Sensors.GhostsSeenThrough - through0}) and gone");
            // a missile of this frigate's, inside the jammer's 1.5 km: scrambled
            Put(1200f);
            var msl = new GameObject("Sensor test missile").AddComponent<Missile>();
            msl.transform.position = me.position + me.forward * 200f;
            msl.owner = me;
            msl.target = dt;
            int scrambled0 = jammer.Scrambled;
            float until = Time.time + 6f;
            while (jammer.Scrambled == scrambled0 && msl != null && Time.time < until) yield return null;
            Check(jammer.Scrambled > scrambled0, $"this frigate's missile into the jammer's 1.5 km is scrambled ({jammer.Scrambled - scrambled0})");
            if (msl != null) Destroy(msl.gameObject);
            Destroy(jammer);
            Destroy(far.gameObject);
            yield return null;
            Check(Jamming.On(me) == 0f, "the jammer gone: no jamming");

            // ---- an EMP burst (PR 7): it charges with the frigate in reach, the HUD warning; the
            // pulse knocks one of the frigate's components out, for less with its ECCM; back after
            Put(2000f);
            var sysF = cc.GetComponent<ShipSystems>();
            var burst = drone.AddComponent<EmpBurst>();
            burst.radius = 2500f;
            burst.chargeTime = 2f;
            burst.strength = 400f;
            burst.cooldown = 600f;
            yield return null;
            yield return null;
            string empWarn = ShipHud.EmpText(cc);
            Check(burst.Current == EmpBurst.State.Charging && empWarn.StartsWith("EMP charging"), $"an EMP burst charging with the frigate in reach: '{empWarn}'");
            int knocks0 = sysF != null ? sysF.Knocks : 0;
            float until2 = Time.time + 4f;
            while (burst.Bursts == 0 && Time.time < until2) yield return null;
            yield return null;
            float outFor = 0f;
            ShipSystem hitSys = ShipSystem.Hull;
            if (sysF != null) foreach (var c in ShipSystems.Components) if (sysF.KnockedFor(c) > outFor) { outFor = sysF.KnockedFor(c); hitSys = c; }
            string empNow = ShipHud.EmpText(cc);
            Check(sysF != null && sysF.Knocks > knocks0 && outFor > 0f && sysF.IsDown(hitSys) && empNow.Contains("back in"),
                  $"the pulse knocks the {ShipSystems.Label(hitSys)} out for {outFor:0.0} s (ECCM {FrigateClass.EccmOf(cc):0.0}); the HUD: '{empNow}'");
            Shot("s5_emp");
            Destroy(burst);
            yield return new WaitForSeconds(outFor + 0.5f);
            Check(sysF != null && !sysF.IsDown(hitSys), $"the {ShipSystems.Label(hitSys)} back after the EMP's time");

            // ---- a drone bay (PR 8): in the fight it launches drones at the frigate; the frigate's
            // CIWS (on again) goes for them; called home, those left are stock again
            Put(6000f);
            if (pilot != null) pilot.NetLurk(false);
            foreach (var c in ciws) c.enabled = true;
            var bay = drone.AddComponent<DroneBay>();
            bay.stock = 4;
            bay.maxOut = 2;
            bay.launchInterval = 1f;
            yield return null;
            yield return null;
            Check(bay.Out.Count == 2 && bay.Stock == 2, $"a drone bay 6 km off launches at the frigate: {bay.Out.Count} out, {bay.Stock} left in stock");
            float until3 = Time.time + 40f;
            bool engaged2 = false;
            float closest = float.MaxValue;
            while (Time.time < until3 && !engaged2 && bay.Lost == 0)
            {
                foreach (var c in ciws) foreach (var d in bay.Out) if (d != null && c.EngagedTarget == d.transform) engaged2 = true;
                foreach (var d in bay.Out) if (d != null) closest = Mathf.Min(closest, Vector3.Distance(d.transform.position, me.position));
                yield return null;
            }
            string ciwsSays = string.Join(", ", System.Linq.Enumerable.Select(ciws, c => $"{(c.enabled ? "on" : "off")}/{(c.active ? "active" : "inactive")}/{c.mounts?.Length ?? 0} mounts/target {(c.EngagedTarget != null ? c.EngagedTarget.name : "none")}"));
            Check(engaged2 || bay.Lost > 0, $"the frigate's CIWS goes for the drones (engaged {engaged2}, {bay.Lost} shot down; the drones came within {closest:F0} m; CIWS {ciwsSays})");
            Shot("s6_drones");
            int out2 = bay.Out.Count, recovered0 = bay.Recovered;
            bay.recall = true;
            until3 = Time.time + 60f;
            while (bay.Out.Count > 0 && Time.time < until3) yield return null;
            Check(bay.Out.Count == 0 && bay.Recovered - recovered0 == out2 && bay.Stock == 4 - bay.Lost,
                  $"called home: {bay.Recovered - recovered0} back aboard, the stock {bay.Stock} (lost {bay.Lost})");
            Destroy(bay);
            foreach (var c in ciws) c.enabled = false;

            cc.SetTarget(null);
            foreach (var c in ciws) c.enabled = true;
            Destroy(drone);
            if (hull != null) hull.cannotDie = false;
            Log(passed == checks ? $"PASS: sensors ({passed} of {checks})" : $"FAIL: sensors ({passed} of {checks} passed)");
        }
    }
}

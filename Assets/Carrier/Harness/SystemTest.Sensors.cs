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

            cc.SetTarget(null);
            foreach (var c in ciws) c.enabled = true;
            Destroy(drone);
            if (hull != null) hull.cannotDie = false;
            Log(passed == checks ? $"PASS: sensors ({passed} of {checks})" : $"FAIL: sensors ({passed} of {checks} passed)");
        }
    }
}

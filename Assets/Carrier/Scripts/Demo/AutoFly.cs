using System;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Flying the ship to something by itself, in real time: a target (combat mode's U), a wreck
    /// (combat mode's Z on one out of reach), anything that can say where it is. It steers for
    /// it, in cruise while it is far (cruise slows by itself near a station, under fire, lower
    /// down), the throttle easing off as it closes, until it is within the distance asked for;
    /// then it hands back the helm (and does what was asked on arrival: line up the guns, start
    /// the salvage beam). Taking the helm or the throttle, changing mode, a warp, a landing or a
    /// panel coming up stops it; so does the thing going (destroyed, stripped).
    /// </summary>
    public class AutoFly : MonoBehaviour
    {
        public CarrierDemoInput demo;

        public static AutoFly Instance { get; private set; }

        /// <summary>Flying to something now.</summary>
        public bool Active { get; private set; }
        /// <summary>What it is flying to.</summary>
        public string Label { get; private set; } = "";
        /// <summary>How far there is still to go (m).</summary>
        public float Left { get; private set; }
        /// <summary>Trips that got there (for the self-test).</summary>
        public int Arrivals { get; private set; }

        Func<Vector3> where;
        Func<bool> stillThere;
        Action onArrival;
        float within;
        ModeOfPlay mode;
        float throttleSet = -1f;

        CarrierController Ship => demo != null ? demo.carrier : null;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Fly to where `at` says, until within `stopWithin` metres of it; `arrived` is
        /// done on arrival. `valid` (if given) going false ends the trip.</summary>
        public bool Begin(string label, Func<Vector3> at, float stopWithin, Action arrived = null, Func<bool> valid = null)
        {
            var ship = Ship;
            if (ship == null || ship.Destroyed || at == null) return false;
            if (demo.ShipHeld || PlanetLanding.Busy)
            {
                SoundFX.Ui(Sfx.UiError);
                Notices.Post($"Can't fly to {label} now");
                return false;
            }
            // (one autopilot at a time)
            demo.Combat?.StopLineUp();
            demo.Mining?.StopApproach();
            demo.Navigation?.Stop();
            Label = label;
            where = at;
            within = Mathf.Max(100f, stopWithin);
            onArrival = arrived;
            stillThere = valid;
            mode = Modes.Current;
            throttleSet = -1f;
            Active = true;
            Left = Vector3.Distance(ship.transform.position, at());
            SoundFX.Ui(Sfx.UiLock);
            Notices.Post($"Flying to {label}: {CombatMode.Distance(Left)}. Steer or touch the throttle to take over");
            return true;
        }

        public void Stop(string why = null)
        {
            if (!Active) return;
            Active = false;
            throttleSet = -1f;
            var pf = PlanetFlight.Instance;
            if (pf != null) pf.autoCruise = false;
            if (demo != null) { demo.autoRudder = null; demo.autoPitch = null; }
            if (why != null) Notices.Post(why);
        }

        void Update()
        {
            if (!Active) return;
            var ship = Ship;
            if (ship == null || ship.Destroyed) { Stop(); return; }
            if (stillThere != null && !stillThere()) { Stop($"{Label}: gone"); return; }
            if (demo.PilotFlying) { Stop("Auto-flight off: you have the helm"); return; }
            if (throttleSet >= 0f && Mathf.Abs(ship.throttle - throttleSet) > 0.01f) { Stop("Auto-flight off: you have the throttle"); return; }
            if (!Modes.Is(mode) || demo.ShipHeld || PlanetLanding.Busy) { Stop(); return; }
            Vector3 to = where();
            Left = Vector3.Distance(ship.transform.position, to);
            var pf = PlanetFlight.Instance;
            if (Left <= within)
            {
                // there: the helm back, the engines eased right down
                Stop(null);
                ship.throttle = Mathf.Min(ship.throttle, 0.1f);
                Arrivals++;
                Notices.Post($"At {Label}");
                onArrival?.Invoke();
                return;
            }
            var v = ShipAutopilot.BowOn(ship, to);
            demo.autoRudder = v.x;
            demo.autoPitch = v.y;
            // in cruise while far (it slows by itself near a station, in a fight, lower down);
            // no faster than would cover what is left in a few seconds, so it closes smoothly
            bool far = Left > within + 8000f;
            if (pf != null) pf.autoCruise = far;
            float top = pf != null && pf.TopSpeed > 0f ? pf.TopSpeed : ship.maxSpeed;
            float want = Mathf.Max((Left - within) / 5f, ship.maxSpeed * 0.25f);
            // (and not hard at it while still turning onto the way there)
            float aligned = Mathf.Clamp01(1f - ShipAutopilot.BowError(ship, to) / 120f);
            ship.throttle = Mathf.Clamp(want / Mathf.Max(1f, top), 0.05f, 1f) * (0.3f + 0.7f * aligned);
            throttleSet = ship.throttle;
        }
    }
}

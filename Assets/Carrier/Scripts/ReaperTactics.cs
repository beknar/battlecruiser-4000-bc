using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The Grey Reaper's way of fighting: capture by abduction.
    /// - It holds back (`holdBack`) while the others work, its light guns its only defence.
    /// - Once the target is disabled (its engines out, or its shield down and its hull under
    ///   half: GreysTactic.Disabled), it closes and locks its tractor emitters on (TractorBeam),
    ///   which holds the ship and stops its warp; the abduction itself is Abductor's.
    /// - Held, it can tow the ship away with it (`towing`: the group's withdrawal), the beam
    ///   dragging it along; the hold breaks if its own hull drops under `breakBelow`, or the
    ///   ship gets its engines back and is no longer disabled.
    /// </summary>
    public class ReaperTactics : GreysTactic
    {
        public float holdBack = 6000f;
        public float lockRange = 1800f;
        public float breakBelow = 0.35f;
        [Tooltip("Set by the fleet as it withdraws: fly off with what it holds.")]
        public bool towing;
        public Vector3 towTo;

        public enum Phase { None, HoldBack, Close, Holding }
        public Phase Current { get; private set; }
        TractorBeam beam;
        /// <summary>Ships it has locked on to (the tests).</summary>
        public int Locks { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            beam = GetComponent<TractorBeam>();
        }

        protected override void Stood()
        {
            Current = Phase.None;
            towing = false;
            if (beam != null && beam.Locked) beam.Release("out of the fight");
        }

        protected override void Think(bool seeing)
        {
            if (Target == null) { Stood(); Doing = "searching"; return; }
            float d = Vector3.Distance(transform.position, Target.position);
            bool disabled = Disabled(Target);
            if (beam != null && beam.Locked)
            {
                Current = Phase.Holding;
                if (HullOf(transform) < breakBelow) beam.Release("its hull failing");
                else if (!disabled && !towing) beam.Release("the ship has its engines back");
            }
            else if (disabled)
            {
                Current = Phase.Close;
                if (beam != null && d < Mathf.Min(lockRange, beam.range) && beam.Lock(Target, out _)) Locks++;
            }
            else Current = Phase.HoldBack;
            Doing = Current == Phase.Holding ? (towing ? "towing its catch away" : "holding its catch") : Current == Phase.Close ? "closing on a disabled ship" : "holding back";
        }

        public override void Steer(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            if (Target == null) return;
            Vector3 tp = Target.position;
            Vector3 to = tp - pos;
            switch (Current)
            {
                case Phase.Holding:
                    if (towing)
                    {
                        Vector3 go = towTo - pos;
                        desired = go.sqrMagnitude > 1f ? go.normalized : desired;
                        wantSpeed = pilot.cruiseSpeed * 0.6f;
                    }
                    else { desired = to.normalized; wantSpeed = 0f; }
                    break;
                case Phase.Close:
                    desired = to.normalized;
                    wantSpeed = to.magnitude > lockRange * 0.8f ? pilot.attackSpeed : 0f;
                    break;
                default:
                    // hold back: no nearer than its line
                    Vector3 spot = tp - to.normalized * holdBack;
                    Vector3 g = spot - pos;
                    desired = g.sqrMagnitude > 1f ? g.normalized : to.normalized;
                    wantSpeed = Mathf.Clamp(g.magnitude * 0.15f, 0f, pilot.cruiseSpeed);
                    break;
            }
        }
    }
}

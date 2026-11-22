using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The Imperial Assault frigate's way of fighting: the line of battle. It closes bow-on to its
    /// guns' range (`lineRange`) and holds there, broadside to nothing, its armour taking what
    /// comes; its drones out (DroneBay, on its own) and its missiles at a painted target from
    /// further out (EnemyWeapons, Designation). It falls back in order with the fleet (EmpireFleet:
    /// `retreating`).
    /// </summary>
    public class AssaultTactics : SensorTactic
    {
        public float lineRange = 3200f;
        [Tooltip("Set by the fleet as it falls back: away from the target, guns still on it.")]
        public bool retreating;

        protected override void Think(bool seeing)
        {
            Doing = Target == null ? "searching" : retreating ? "falling back in order" : "in the line";
        }

        public override void Steer(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            if (Target == null) return;
            Vector3 to = TargetPos - pos;
            float d = to.magnitude;
            if (retreating) { desired = -to.normalized; wantSpeed = pilot.cruiseSpeed; return; }
            desired = to.normalized;
            // bow on, to its range, and hold there
            wantSpeed = d > lineRange * 1.1f ? pilot.attackSpeed : d < lineRange * 0.8f ? 0f : pilot.cruiseSpeed * 0.3f;
        }
    }
}

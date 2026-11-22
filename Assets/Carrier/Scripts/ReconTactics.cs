using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The Imperial Recon frigate's way of fighting: scout and designator. It holds well out
    /// (`holdRange`), seeing furthest of the fleet, its targeting laser (Designator) on the target
    /// so the fleet's guns fire tighter and its missiles from further out (Designation), its warp
    /// inhibitor (Interdictor) keeping the target from running; light guns its only defence. It
    /// keeps its distance from anything that closes (`keepClear`).
    /// </summary>
    public class ReconTactics : SensorTactic
    {
        public float holdRange = 9000f;
        public float keepClear = 4000f;

        Designator laser;

        protected override void Awake()
        {
            base.Awake();
            laser = GetComponent<Designator>();
            if (laser != null) laser.driven = true;
        }

        protected override void Stood()
        {
            if (laser != null) laser.target = null;
        }

        protected override void Think(bool seeing)
        {
            if (laser != null) laser.target = seeing ? Target : null;
            Doing = Target == null ? "searching" : seeing ? $"painting {Target.name}" : "searching where it was seen";
        }

        public override void Steer(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            if (Target == null) return;
            Vector3 to = TargetPos - pos;
            float d = to.magnitude;
            if (d < keepClear) { desired = -to.normalized; wantSpeed = pilot.attackSpeed; return; }
            Vector3 side = Vector3.Cross(Vector3.up, to.normalized);
            Vector3 spot = TargetPos - to.normalized * holdRange + side * holdRange * 0.25f;
            Vector3 go = spot - pos;
            desired = go.sqrMagnitude > 1f ? go.normalized : to.normalized;
            wantSpeed = Mathf.Clamp(go.magnitude * 0.2f, 20f, pilot.attackSpeed);
        }
    }
}

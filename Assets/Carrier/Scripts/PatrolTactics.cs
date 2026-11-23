using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The Imperial Patrol frigate's way of fighting: escort and screen.
    /// - It keeps by the nearest Assault frigate, between it and the target (`screenDistance`), its
    ///   CIWS (PointDefence) over both against missiles, drones and fighters.
    /// - Its warp inhibitor (Interdictor) holds the target from running.
    /// - Its marines: once the target is disabled and within its pods' reach, a wave of boarding
    ///   pods (PodLauncher): the Empire takes ships.
    /// - Light logistics: alongside an Assault frigate (`supplyRange`), it puts a drone back in its
    ///   bay every `supplyInterval` seconds.
    /// </summary>
    public class PatrolTactics : SensorTactic
    {
        public float screenDistance = 1500f;
        public float standoff = 3000f;
        public float supplyRange = 2500f;
        public float supplyInterval = 20f;
        [Tooltip("Set by the fleet as it falls back: it covers the line's retreat.")]
        public bool retreating;

        PodLauncher pods;
        float nextSupply;
        /// <summary>The Assault frigate it screens, and drones it has put back (the tests).</summary>
        public EmpireShip Ward { get; private set; }
        public int Supplied { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            pods = GetComponent<PodLauncher>();
        }

        void LateUpdate()
        {
            // its logistics run whether or not it is in a fight (not from a wreck)
            if (Time.time < nextSupply || (pilot != null && !pilot.Alive)) return;
            nextSupply = Time.time + supplyInterval;
            var a = EmpireShip.Nearest(EmpireShip.Role.Assault, transform.position, supplyRange);
            var bay = a != null ? a.GetComponent<DroneBay>() : null;
            if (bay != null && bay.Restock(1)) Supplied++;
        }

        protected override void Think(bool seeing)
        {
            Ward = EmpireShip.Nearest(EmpireShip.Role.Assault, transform.position, 20000f);
            if (Target == null) { Doing = "patrolling"; return; }
            float d = Vector3.Distance(transform.position, Target.position);
            if (pods != null && Disabled(Target) && Rivalry.IsPlayer(Target) && d < pods.range && pods.Ready) pods.LaunchWave(Target);
            Doing = $"{(retreating ? "covering the retreat" : Ward != null ? $"screening {Ward.name}" : "standing off")}{(Disabled(Target) ? ", marines ready" : "")}";
        }

        public override void Steer(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            if (Target == null) return;
            Vector3 tp = TargetPos;
            Vector3 spot;
            if (Ward != null)
            {
                Vector3 w = Ward.transform.position;
                Vector3 toT = tp - w;
                spot = w + (toT.sqrMagnitude > 1f ? toT.normalized : Vector3.forward) * screenDistance;
            }
            else
            {
                Vector3 away = pos - tp;
                spot = tp + (away.sqrMagnitude > 1f ? away.normalized : Vector3.back) * standoff;
            }
            Vector3 go = spot - pos;
            if (go.sqrMagnitude < 200f * 200f) { desired = (tp - pos).normalized; wantSpeed = 0f; return; }
            desired = go.normalized;
            wantSpeed = Mathf.Clamp(go.magnitude * 0.2f, 40f, pilot.attackSpeed);
        }
    }
}

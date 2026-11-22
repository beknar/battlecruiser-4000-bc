using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The Grey Shroud's way of fighting: electronic warfare and escort.
    /// - It keeps by the Reaper, its mothership, between it and the target, its jammer's field
    ///   (EcmEmitter) over both; with no Reaper it stands off the target.
    /// - It jams once a player's ship is within its field and a half (ghosts, masking, the
    ///   rest), quiet otherwise.
    /// - Its EMP burst (EmpBurst) it fires when the target is well inside its reach; its EMP
    ///   projector (its guns: an EMP bolt) fires on its own.
    /// </summary>
    public class ShroudTactics : GreysTactic
    {
        public float escortDistance = 1200f;
        public float standoff = 4000f;

        EcmEmitter ecm;
        EmpBurst burst;
        /// <summary>The ship it escorts (the nearest Reaper), if any.</summary>
        public GreysShip Ward { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            ecm = GetComponent<EcmEmitter>();
            burst = GetComponent<EmpBurst>();
            if (burst != null) burst.autoFire = false;
        }

        protected override void Stood()
        {
            if (ecm != null) ecm.active = false;
        }

        protected override void Think(bool seeing)
        {
            Ward = GreysShip.Nearest(GreysShip.Role.Reaper, transform.position);
            if (Ward != null && (Ward.transform.position - transform.position).sqrMagnitude > 20000f * 20000f) Ward = null;
            if (Target == null) { Stood(); Doing = "searching"; return; }
            float d = Vector3.Distance(transform.position, TargetPos);
            if (ecm != null) ecm.active = d < ecm.radius * 1.5f;
            if (burst != null && seeing && d < burst.radius * 0.85f) burst.Fire();
            Doing = $"{(ecm != null && ecm.active ? "jamming" : "quiet")}{(Ward != null ? $", escorting {Ward.name}" : "")}{(burst != null && burst.Current == EmpBurst.State.Charging ? ", EMP charging" : "")}";
        }

        public override void Steer(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            if (Target == null) return;
            Vector3 tp = TargetPos;
            Vector3 spot;
            if (Ward != null)
            {
                // between its ward and the target, close to its ward
                Vector3 w = Ward.transform.position;
                Vector3 toT = tp - w;
                spot = w + (toT.sqrMagnitude > 1f ? toT.normalized : Vector3.forward) * escortDistance;
            }
            else
            {
                Vector3 away = pos - tp;
                spot = tp + (away.sqrMagnitude > 1f ? away.normalized : Vector3.back) * standoff;
            }
            Vector3 go = spot - pos;
            if (go.sqrMagnitude < 150f * 150f) { desired = (tp - pos).normalized; wantSpeed = 0f; return; }
            desired = go.normalized;
            wantSpeed = Mathf.Clamp(go.magnitude * 0.15f, 30f, pilot.attackSpeed);
        }
    }
}

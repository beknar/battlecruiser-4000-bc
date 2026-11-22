using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The Grey Scout's way of fighting: deep reconnaissance.
    /// - It shadows a player's ship it sees from `shadowRange` out, cloaked (Cloak), quiet: its
    ///   guns cold, so its cloak holds.
    /// - It paints the ship for the rest of the Greys (Designator) and calls them in: the nearest
    ///   other Greys band within `callRange` that lies in wait is sprung and sent to it (Called).
    /// - When something closes on it (`evadeRange`) it slips away, cloaked.
    /// - Hit-and-run: when the ship's shield is down, it runs in with its precision beams and its
    ///   two drones (DroneBay), for `runTime` seconds, then breaks off and cloaks again.
    /// </summary>
    public class ScoutTactics : GreysTactic
    {
        public float shadowRange = 7000f;
        public float evadeRange = 3000f;
        public float callRange = 40000f;
        public float runTime = 8f;
        public float runEvery = 45f;

        public enum Phase { None, Shadow, Evade, Run }
        public Phase Current { get; private set; }
        /// <summary>Bands it has called in, and when it last ran in.</summary>
        public int Calls { get; private set; }
        float runUntil = -1f, lastRun = -1000f;
        bool called;
        Cloak cloak;
        Designator laser;
        DroneBay bay;

        /// <summary>A band of the Greys called in by a scout (the tests).</summary>
        public static event System.Action<EncounterGroup, Transform> Called;

        protected override void Awake()
        {
            base.Awake();
            cloak = GetComponent<Cloak>();
            laser = GetComponent<Designator>();
            bay = GetComponent<DroneBay>();
        }

        protected override void Stood()
        {
            Current = Phase.None;
            called = false;
            if (laser != null) laser.target = null;
            if (cloak != null) cloak.Engage(true);
            if (bay != null) bay.recall = true;
        }

        protected override void Think(bool seeing)
        {
            if (Target == null) { Stood(); Doing = "searching"; return; }
            float d = Vector3.Distance(transform.position, TargetPos);
            // paint what it sees, for the rest
            if (laser != null) laser.target = seeing ? Target : null;
            if (seeing && !called) { called = true; CallIn(); }
            // something closing on it: slip away
            bool closing = NearestPlayerWithin(evadeRange);
            var sh = Target.GetComponentInChildren<Shield>();
            bool open = sh == null || !sh.Up;
            if (Current == Phase.Run && Time.time > runUntil) { Current = Phase.Shadow; lastRun = Time.time; }
            if (Current != Phase.Run && seeing && open && Time.time - lastRun > runEvery && d < shadowRange * 1.5f)
            {
                Current = Phase.Run;
                runUntil = Time.time + runTime;
            }
            else if (Current != Phase.Run) Current = closing ? Phase.Evade : Phase.Shadow;
            bool run = Current == Phase.Run;
            if (cloak != null) cloak.Engage(!run);
            if (weapons != null) weapons.weaponsFree = run;
            if (bay != null) bay.recall = !run;
            Doing = run ? "hit-and-run" : Current == Phase.Evade ? "slipping away" : seeing ? "shadowing, painting" : "searching where it was seen";
        }

        bool NearestPlayerWithin(float r)
        {
            foreach (var p in PlayerShips.All)
                if (p != null && p.gameObject.activeInHierarchy && PlayerShips.Present(p) && (p.position - transform.position).sqrMagnitude < r * r) return true;
            return false;
        }

        /// <summary>The nearest other band of the Greys within reach, lying in wait: sprung, its
        /// pilots sent after what this one paints.</summary>
        void CallIn()
        {
            EncounterGroup best = null;
            float bd = callRange * callRange;
            foreach (var g in FindObjectsByType<EncounterGroup>(FindObjectsSortMode.None))
            {
                if (g == null || g.pilots == null || g.Active || g.holdOff) continue;
                bool greys = false, mine = false;
                foreach (var p in g.pilots)
                {
                    if (p == null) continue;
                    if (p == pilot) mine = true;
                    if (p.GetComponent<GreysShip>() != null) greys = true;
                }
                if (!greys || mine) continue;
                float d = (g.transform.position - transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = g; }
            }
            if (best == null) return;
            best.Spring();
            foreach (var p in best.pilots) if (p != null) { p.target = Target; p.Engage(); }
            Calls++;
            Called?.Invoke(best, Target);
        }

        public override void Steer(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            if (Target == null) return;
            Vector3 to = TargetPos - pos;
            float d = to.magnitude;
            switch (Current)
            {
                case Phase.Evade:
                    // away from the nearest, fast
                    desired = (-to).normalized;
                    wantSpeed = pilot.attackSpeed;
                    break;
                case Phase.Run:
                    // in close, the pilot's own attack run does the rest
                    break;
                default:
                    // shadow: hold at its range, off to one side, matching its course
                    Vector3 side = Vector3.Cross(Vector3.up, to.normalized);
                    Vector3 spot = TargetPos - to.normalized * shadowRange + side * shadowRange * 0.3f;
                    Vector3 go = spot - pos;
                    desired = go.sqrMagnitude > 1f ? go.normalized : to.normalized;
                    wantSpeed = Mathf.Clamp(go.magnitude * 0.2f, 20f, pilot.attackSpeed);
                    break;
            }
        }
    }
}

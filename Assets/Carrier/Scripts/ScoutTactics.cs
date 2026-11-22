using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The Grey Scout's way of fighting: deep reconnaissance.
    /// - It shadows a player's ship it sees from `shadowRange` out, cloaked (Cloak), quiet: its
    ///   guns cold, so its cloak holds.
    /// - It paints the ship for the rest of the Greys (Designator) and calls them in: the nearest
    ///   other Greys band within `callRange` of it that lies in wait is sprung and sent to it,
    ///   dropping out of warp near it if it is far off (Called).
    /// - When something closes on it (`evadeRange`) it slips away, cloaked.
    /// - Hit-and-run: when the ship's shield is down, it runs straight in to its precision beams'
    ///   range with its two drones (DroneBay), fires for `hitTime` seconds, then breaks off and
    ///   cloaks again, its drones out a while longer.
    /// </summary>
    public class ScoutTactics : GreysTactic
    {
        public float shadowRange = 7000f;
        public float evadeRange = 3000f;
        [Tooltip("How far (m) from what it paints a band it calls in can be (the system is real-size: by default, anywhere in it); it drops out of warp near it.")]
        public float callRange = 1e13f;
        [Tooltip("How far (m) from what it paints the band it calls in drops out of warp.")]
        public float arriveRange = 7000f;
        [Tooltip("A hit-and-run lasts this long (s): long enough to close from its shadowing range to its guns.")]
        public float runTime = 25f;
        [Tooltip("Its drones stay out this long (s) after a run.")]
        public float dronesLinger = 20f;
        [Tooltip("A run ends once its guns have been in range this long (s): hit, then run.")]
        public float hitTime = 8f;
        public float runEvery = 45f;

        public enum Phase { None, Shadow, Evade, Run }
        public Phase Current { get; private set; }
        /// <summary>Bands it has called in, and when it last ran in.</summary>
        public int Calls { get; private set; }
        float runUntil = -1f, lastRun = -1000f, inRangeSince = -1f;
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
            if (laser != null) laser.driven = true;
            bay = GetComponent<DroneBay>();
        }

        protected override void Stood()
        {
            Current = Phase.None;
            // (quiet: its guns cold, so its cloak holds)
            if (weapons != null) weapons.weaponsFree = false;
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
            // (in its guns' range: a few seconds of fire, then away)
            float gunRange = 3500f;
            if (Current == Phase.Run && d < gunRange) { if (inRangeSince < 0f) inRangeSince = Time.time; }
            if (Current == Phase.Run && (Time.time > runUntil || (inRangeSince >= 0f && Time.time - inRangeSince > hitTime)))
            {
                Current = Phase.Shadow;
                lastRun = Time.time;
                inRangeSince = -1f;
            }
            if (Current != Phase.Run && seeing && open && Time.time - lastRun > runEvery && d < shadowRange * 1.5f)
            {
                Current = Phase.Run;
                runUntil = Time.time + runTime;
            }
            else if (Current != Phase.Run) Current = closing ? Phase.Evade : Phase.Shadow;
            bool run = Current == Phase.Run;
            if (cloak != null) cloak.Engage(!run);
            if (weapons != null) weapons.weaponsFree = run;
            // (its drones out for the run and a while after it)
            if (bay != null) bay.recall = !run && Time.time - lastRun > dronesLinger;
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
            Vector3 from = Target != null ? Target.position : transform.position;
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
                float d = (g.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = g; }
            }
            if (best == null) return;
            // (far off: they come out of warp near what it paints, on the far side, together)
            Vector3 tp = Target.position;
            Vector3 away = transform.position - tp;
            away = away.sqrMagnitude > 1f ? -away.normalized : Vector3.forward;
            Vector3 centre = tp + away * arriveRange;
            if ((best.transform.position - centre).sqrMagnitude > 15000f * 15000f)
            {
                Vector3 c0 = Vector3.zero;
                int n = 0;
                foreach (var p in best.pilots) if (p != null) { c0 += p.transform.position; n++; }
                c0 = n > 0 ? c0 / n : best.transform.position;
                // (into this one's place in the system, carried along with it as this one is: a band
                // from round another world is in another moving frame)
                var myHome = GetComponentInParent<OrbitHome>();
                foreach (var p in best.pilots)
                {
                    if (p == null || !p.Alive) continue;
                    var to = p.transform.position - c0;
                    var oh = p.GetComponent<OrbitHome>();
                    if (myHome != null && myHome.home != null) { if (oh == null) oh = p.gameObject.AddComponent<OrbitHome>(); oh.home = myHome.home; }
                    else if (oh != null) oh.home = null;
                    p.transform.position = centre + Vector3.ClampMagnitude(to, 3000f);
                    p.transform.rotation = Quaternion.LookRotation(tp - p.transform.position);
                    // (posted here now: its leash and its way home from here, not from the far side of the system)
                    p.Repost();
                    // (and no longer kept far off where it was: the orbits would put it back there)
                    if (OrbitalSystem.Instance != null) OrbitalSystem.Instance.Forget(p.transform);
                }
                best.transform.position = centre;
                Notices.Post("Unidentified ships drop out of warp nearby");
            }
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
                    // straight in, fast, to its guns' range
                    desired = to.normalized;
                    wantSpeed = d > 2500f ? pilot.attackSpeed : pilot.cruiseSpeed * 0.5f;
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

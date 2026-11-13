using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// An AI pilot for the player's ship: a co-op player's wingman (CoopBot), flying by itself on
    /// orders. It drives the ship through the same controls a player has (CarrierDemoInput's
    /// autopilot inputs, the throttle, the target, the guns, the main gun, missiles, the warp
    /// drive and docking), so the ship behaves exactly as anyone's would.
    ///
    ///   Follow  - keep station on a player's ship (off its starboard quarter), warping after it
    ///             when it goes far (to the place nearest it)
    ///   Hold    - stop where it is
    ///   Attack  - fight a target (named, or the nearest enemy): broadside at gun range, the bow
    ///             beam when lined up, missiles now and then
    ///   Goto    - warp to a place, then hold there
    ///   Dock    - dock at the station it is at
    /// When an enemy comes close while it is following or holding, it fights, and goes back to
    /// its order when the fight is over.
    /// </summary>
    public class Wingman : MonoBehaviour
    {
        public enum Order { Hold, Follow, Attack, Goto, Dock }

        public CarrierDemoInput demo;
        [Tooltip("Where it keeps station off its leader: right, up and behind, in the leader's frame, in ship sizes.")]
        public Vector3 slot = new Vector3(1.7f, 0.2f, -2.2f);
        public float gunRange = 1600f;
        [Tooltip("An enemy this close (m) is fought even while following or holding.")]
        public float defendRange = 6000f;
        [Tooltip("How far the leader may get before it warps after them (m).")]
        public float warpAfter = 60000f;

        public Order Current { get; private set; } = Order.Hold;
        public int LeaderId { get; private set; }
        public string LeaderName => LeaderId != 0 && CoopClient.Instance != null ? CoopClient.Instance.NameOf(LeaderId) : "";
        public Transform Target { get; private set; }
        public WarpPoint Destination { get; private set; }
        /// <summary>What it is doing now, in a few words (for the status and the comms).</summary>
        public string Doing { get; private set; } = "holding position";

        CarrierController Ship => demo != null ? demo.carrier : null;
        WarpDrive drive;
        bool defending;
        float nextMissiles, nextWarp, nextLook;
        // the ship's size (its bounds' half-diagonal), worked out once a ship
        CarrierController sized;
        float size = 150f;

        void OnDestroy() => Release();

        // ------------------------------------------------------------ orders

        public void Hold() => Set(Order.Hold);
        public void Follow(int playerId) { LeaderId = playerId; Set(Order.Follow); }
        public void Attack(Transform t) { Target = t; Set(Order.Attack); }
        public void Goto(WarpPoint p) { Destination = p; Set(Order.Goto); }
        public void Dock() => Set(Order.Dock);

        void Set(Order o)
        {
            Current = o;
            defending = false;
            nextWarp = 0f;
            if (o != Order.Attack) Target = null;
        }

        /// <summary>The leader's ship in this game (null if it isn't here: in another level, or
        /// not seen: landed, docked or in warp).</summary>
        public RemotePlayer Leader()
        {
            var w = CoopWorld.Instance;
            var r = w != null && LeaderId != 0 ? w.RemoteOf(LeaderId) : null;
            return r != null && !r.Hidden ? r : null;
        }

        /// <summary>The nearest hostile ship or installation within `range` of a point (alive, in the level).</summary>
        public static Transform NearestEnemy(Vector3 from, float range)
        {
            Transform best = null;
            float bd = range * range;
            foreach (var t in Targets.All)
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                if (!t.TryGetComponent(out TargetInfo info) || !info.Hostile) continue;
                if (t.TryGetComponent(out Health h) && h.Dead) continue;
                float d = (t.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = t; }
            }
            return best;
        }

        /// <summary>A target by name (part of its name, any case), the nearest that matches.</summary>
        public static Transform FindTarget(string name, Vector3 from)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim().ToLowerInvariant();
            return Targets.All.Where(t => t != null && t.gameObject.activeInHierarchy && NameOf(t).ToLowerInvariant().Contains(name))
                .OrderBy(t => (t.position - from).sqrMagnitude).FirstOrDefault();
        }

        /// <summary>A target's name as the player sees it.</summary>
        public static string NameOf(Transform t) => t == null ? "" : t.TryGetComponent(out TargetInfo info) ? info.Name : t.name;

        static bool Gone(Transform t) => t == null || !t.gameObject.activeInHierarchy || (t.TryGetComponent(out Health h) && h.Dead);

        // ------------------------------------------------------------ flying

        void Release()
        {
            if (demo == null) return;
            demo.autoRudder = demo.autoPitch = demo.autoElevator = demo.autoRoll = null;
        }

        void Update()
        {
            var cc = Ship;
            if (cc == null) return;
            if (cc.Destroyed) { Doing = "destroyed, waiting to come back"; return; }
            if (sized != cc)
            {
                sized = cc;
                var col = cc.GetComponentInChildren<Collider>();
                size = Mathf.Max(150f, col != null ? col.bounds.extents.magnitude : 150f);
            }
            if (drive == null || drive.gameObject != cc.gameObject) drive = cc.GetComponent<WarpDrive>();
            // (the warp drive, or the landing, flies the ship: hands off)
            if (drive != null && drive.Current != WarpDrive.State.Ready) { Steer(0f, 0f); Doing = "in warp"; return; }
            if (PlanetLanding.Busy) { Steer(0f, 0f); Doing = "down on a planet"; return; }

            // an enemy close while following or holding: fight it, then carry on
            if (Time.time >= nextLook && Current != Order.Attack && Current != Order.Dock)
            {
                nextLook = Time.time + 1f;
                var near = NearestEnemy(cc.transform.position, defendRange);
                if (near != null) { defending = true; if (Gone(Target)) Target = near; }
                else if (defending) { defending = false; Target = null; cc.SetTarget(null); }
            }

            if (Current == Order.Attack || defending)
            {
                if (Gone(Target))
                {
                    Target = NearestEnemy(cc.transform.position, Current == Order.Attack ? 30000f : defendRange);
                    if (Target == null)
                    {
                        if (Current == Order.Attack) { Current = LeaderId != 0 ? Order.Follow : Order.Hold; Doing = "target destroyed"; }
                        defending = false;
                        cc.SetTarget(null);
                        return;
                    }
                }
                Fight(cc, Target);
                return;
            }

            switch (Current)
            {
                case Order.Hold:
                    Steer(0f, 0f);
                    cc.throttle = 0f;
                    Doing = Docking.Docked ? "docked" : "holding position";
                    break;
                case Order.Follow:
                    FollowLeader(cc);
                    break;
                case Order.Goto:
                    if (Destination == null) { Current = Order.Hold; break; }
                    if (Vector3.Distance(cc.transform.position, Destination.Position) < Mathf.Max(20000f, Destination.arrivalDistance * 2f))
                    {
                        Doing = $"arrived at {Destination.displayName}";
                        Current = Order.Hold;
                        break;
                    }
                    TryWarp(Destination, $"warping to {Destination.displayName}");
                    break;
                case Order.Dock:
                {
                    Steer(0f, 0f);
                    cc.throttle = 0f;
                    if (Docking.Docked) { Doing = "docked"; Current = Order.Hold; break; }
                    var d = Docking.Instance;
                    if (d == null || d.Current != Docking.State.Free) break;   // (docking: wait for it)
                    if (d.Candidate(out string why) == null) { Doing = $"can't dock: {why}"; Current = Order.Hold; break; }
                    if (d.Dock()) Doing = "docking";
                    break;
                }
            }
        }

        void Steer(float rudder, float pitch)
        {
            demo.autoRudder = rudder;
            demo.autoPitch = pitch;
            demo.autoElevator = 0f;
            demo.autoRoll = 0f;
        }

        /// <summary>Leave the station it is docked at (true while it is still docked or undocking).</summary>
        bool Undocking()
        {
            var d = Docking.Instance;
            if (d == null || d.Current == Docking.State.Free) return false;
            if (d.Current == Docking.State.Docked) d.Undock();
            Steer(0f, 0f);
            return true;
        }

        void TryWarp(WarpPoint p, string doing)
        {
            Doing = doing;
            if (Undocking()) return;
            if (drive == null) { Doing = $"can't warp to {p.displayName}: no warp drive"; return; }
            if (Time.time < nextWarp) return;
            nextWarp = Time.time + 3f;
            if (!drive.Engage(p, out string why)) Doing = $"can't warp to {p.displayName}: {why}";
        }

        void FollowLeader(CarrierController cc)
        {
            var leader = Leader();
            if (leader == null)
            {
                Steer(0f, 0f);
                cc.throttle = 0f;
                Doing = LeaderId != 0 ? $"waiting for {LeaderName}" : "holding position";
                return;
            }
            Vector3 lp = leader.transform.position;
            float far = Vector3.Distance(cc.transform.position, lp);
            if (far > warpAfter && drive != null)
            {
                var near = WarpPoint.Nearest(lp);
                if (near != null) { TryWarp(near, $"warping after {LeaderName}, to {near.displayName}"); return; }
            }
            if (Undocking()) { Doing = $"undocking to follow {LeaderName}"; return; }
            Vector3 to = lp + leader.transform.rotation * (slot * size);
            float d = Vector3.Distance(cc.transform.position, to);
            float leaderSpeed = leader.Velocity.magnitude;
            float matched = Mathf.Clamp01(leaderSpeed / Mathf.Max(1f, cc.maxSpeed));
            if (d < size * 1.2f)
            {
                // in the slot: the leader's heading and speed
                var s = ShipAutopilot.BowOn(cc, cc.transform.position + leader.transform.forward * 2000f);
                Steer(s.x, s.y);
                cc.throttle = matched;
                Doing = $"in formation on {LeaderName}";
            }
            else
            {
                var s = ShipAutopilot.BowOn(cc, to);
                Steer(s.x, s.y);
                float catchUp = Mathf.Clamp01((d - size) / 3000f);
                float aligned = Mathf.Clamp01(1f - ShipAutopilot.BowError(cc, to) / 90f);
                cc.throttle = Mathf.Clamp01(Mathf.Max(matched, catchUp) * (0.3f + 0.7f * aligned));
                Doing = $"closing on {LeaderName} ({d / 1000f:F1} km)";
            }
        }

        void Fight(CarrierController cc, Transform t)
        {
            if (Undocking()) { Doing = $"undocking to engage {NameOf(t)}"; return; }
            if (cc.target != t) demo.SelectTarget(t);
            if (cc.mainTurrets != null) foreach (var g in cc.mainTurrets) if (g) g.autoFire = true;
            Vector3 p = t.position;
            float d = Vector3.Distance(cc.transform.position, p);
            string name = NameOf(t);
            var beam = demo.MainGun;
            if (beam != null && beam.Current != MainBeam.State.Idle)
            {
                // the bow beam charging or firing: keep the bow on
                var s = ShipAutopilot.BowOn(cc, p);
                Steer(s.x, s.y);
                cc.throttle = 0.3f;
                Doing = $"main gun on {name}";
                return;
            }
            if (d > gunRange * 2.5f)
            {
                var s = ShipAutopilot.BowOn(cc, p);
                Steer(s.x, s.y);
                cc.throttle = 1f;
                Doing = $"closing on {name} ({d / 1000f:F1} km)";
            }
            else
            {
                Steer(ShipAutopilot.Broadside(cc, p, 1, gunRange), 0f);
                cc.throttle = 0.55f;
                Doing = $"engaging {name} ({d / 1000f:F1} km)";
                if (beam != null && d < beam.range * 0.85f && ShipAutopilot.BowError(cc, p) < 12f) demo.ChargeMainGun();
            }
            if (cc.HasMissiles && Time.time >= nextMissiles && d < 4500f)
            {
                nextMissiles = Time.time + 12f;
                demo.FireMissiles();
            }
        }
    }
}

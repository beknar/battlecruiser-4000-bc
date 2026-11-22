using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Flies a ship of the player's fleet (FleetDirector) on its orders, beside the ship they
    /// command (the leader):
    ///   Follow  - keep station in loose formation, fighting back if attacked;
    ///   Guard   - keep station, and go for any enemy that comes near the leader ("protect me");
    ///   FormUp  - keep station in close formation;
    ///   Attack  - fight the leader's target (or the one given), then form back up;
    ///   Hold    - stop where it is (fighting back if attacked).
    /// A towed ship (no crew aboard) is held on its tow behind the leader and does nothing else.
    /// Its guns fire by themselves at whatever it is fighting. Left far behind (the leader in
    /// cruise), it catches up out of sight, as a warp would bring it; it never leaves the fleet
    /// to go off on its own.
    /// </summary>
    public class FleetPilot : MonoBehaviour
    {
        Health hull;
        Shield shield;
        /// <summary>Its hull and shield (looked up once, for the fleet's panels).</summary>
        public Health Hull { get { if (hull == null) hull = GetComponent<Health>(); return hull; } }
        public Shield ShieldGen { get { if (shield == null) shield = GetComponentInChildren<Shield>(); return shield; } }
        public enum Order { Follow, Guard, FormUp, Attack, Hold }

        public CarrierController ship;
        public CarrierController leader;
        public FleetShip entry;
        public int slot;
        public string displayName = "";

        public Order Current { get; private set; } = Order.Follow;
        public Transform AttackTarget { get; private set; }
        public Transform Fighting { get; private set; }
        public bool Towed => entry != null && entry.towed;
        /// <summary>What it is doing, in a few words (the fleet's menu and mini-HUD).</summary>
        public string Doing { get; private set; } = "following";
        /// <summary>Times it has caught up out of sight (for the self-test).</summary>
        public int CatchUps { get; private set; }

        Vector3 holdAt;
        float nextLook, nextUp, size = 150f;

        public void Give(Order o, Transform target = null)
        {
            Current = o;
            AttackTarget = o == Order.Attack ? target : null;
            if (o == Order.Hold && ship != null) holdAt = ship.transform.position;
        }

        void Start()
        {
            if (ship == null) ship = GetComponent<CarrierController>();
            var b = new Bounds(transform.position, Vector3.zero);
            foreach (var r in GetComponentsInChildren<MeshRenderer>()) b.Encapsulate(r.bounds);
            size = Mathf.Max(60f, b.extents.magnitude);
        }

        /// <summary>Where it keeps station, in the leader's frame (right, up, ahead), metres.</summary>
        public Vector3 SlotOffset(bool close)
        {
            int rank = slot / 2 + 1;
            float side = slot % 2 == 0 ? 1f : -1f;
            float spacing = (close ? 0.55f : 1f) * Mathf.Max(260f, size * 2.6f);
            return new Vector3(side * rank * spacing, rank * spacing * 0.08f, -rank * spacing * 1.1f);
        }

        public Vector3 Station(bool close) => leader.transform.position + leader.transform.rotation * SlotOffset(close);

        /// <summary>Put it on its station at once (after a warp, or catching up), as if just arrived.</summary>
        public void PlaceOnStation()
        {
            if (leader == null || ship == null) return;
            Vector3 p = Towed ? TowPoint() : Station(Current == Order.FormUp);
            var orbits = OrbitalSystem.Instance;
            if (orbits != null) orbits.Forget(transform);
            transform.SetPositionAndRotation(p, leader.transform.rotation);
            ship.SetMotion(leader.Speed);
            if (Current == Order.Hold) holdAt = p;
        }

        Vector3 TowPoint() => leader.transform.position - leader.transform.forward * (size + 140f) * (1f + 0.9f * slot);

        void Update()
        {
            if (ship == null || leader == null || ship.Destroyed) return;
            Vector3 me = transform.position;
            // its "up", as the players' ships have it: out from a planet near it
            if (Time.time >= nextUp)
            {
                nextUp = Time.time + 0.5f;
                var body = PlanetFlight.NearestBody(me, out _);
                Vector3 up = body != null ? PlanetFlight.UpAt(me, body, out _) : Vector3.up;
                ship.upFrame = Quaternion.FromToRotation(Vector3.up, up);
            }
            // the leader down on a planet, or docked: the fleet waits in orbit, where it is
            // (a ship on the tow too: it is cast off there, and taken up again on the way out)
            bool leaderAway = PlanetLanding.Busy || Docking.Docked;
            if (Towed && leaderAway)
            {
                ship.externalMotion = true;
                ship.enginesCut = true;
                ship.SetTarget(null);
                Doing = "waiting in orbit";
                return;
            }
            if (Towed)
            {
                // on the tow: held behind the leader, its own flying off
                ship.externalMotion = true;
                ship.enginesCut = true;
                ship.SetTarget(null);
                transform.SetPositionAndRotation(TowPoint(), leader.transform.rotation);
                Doing = "under tow";
                return;
            }
            ship.externalMotion = false;
            ship.enginesCut = false;
            // the leader in warp (or being docked): it waits, and is put on its station as the
            // leader arrives (FleetDirector); down on a planet or docked, it waits in orbit
            if (leader.externalMotion || leaderAway || Docking.Busy)
            {
                ship.throttle = 0f;
                ship.rudder = 0f;
                ship.pitchInput = 0f;
                Doing = leaderAway ? "waiting in orbit" : "waiting for the warp";
                return;
            }
            // what to fight: the order's target; an enemy near the leader (guarding); one that
            // has come for it
            if (Time.time >= nextLook)
            {
                nextLook = Time.time + 0.5f;
                Fighting = Pick();
                ship.SetTarget(Fighting);
                foreach (var t in ship.mainTurrets) if (t != null) t.autoFire = Fighting != null;
                if (Current == Order.Attack && (AttackTarget == null || Dead(AttackTarget))) { Current = Order.FormUp; AttackTarget = null; }
            }
            if (Fighting != null)
            {
                // broadside on, at gun range
                float rudder = ShipAutopilot.Broadside(ship, Fighting.position, slot % 2 == 0 ? 1 : -1, 1500f);
                ship.rudder = rudder;
                ship.pitchInput = Mathf.Clamp(-ship.Pitch / 8f, -1f, 1f);
                ship.throttle = 0.7f;
                Doing = $"fighting {Wingman.NameOf(Fighting)}";
                return;
            }
            if (Current == Order.Hold)
            {
                Vector3 back = holdAt - me;
                if (back.magnitude > 400f) { Steer(holdAt, 0.5f); Doing = "back to its post"; }
                else { ship.rudder = 0f; ship.pitchInput = 0f; ship.throttle = 0f; Doing = "holding position"; }
                return;
            }
            // keep station; left far behind, catch up out of sight
            bool close = Current == Order.FormUp;
            Vector3 station = Station(close);
            float dist = Vector3.Distance(me, station);
            if (dist > 25000f && !PlanetLanding.Busy)
            {
                PlaceOnStation();
                CatchUps++;
                return;
            }
            if (dist > 250f)
            {
                // closing on the station, a little faster than the leader
                float want = leader.Speed + Mathf.Clamp(dist * 0.15f, 20f, ship.maxSpeed);
                Steer(station + leader.transform.forward * Mathf.Max(200f, leader.Speed * 2f), Mathf.Clamp01(want / Mathf.Max(1f, ship.maxSpeed)));
            }
            else
            {
                // on station: the leader's heading and speed
                ship.rudder = Mathf.Clamp(Mathf.DeltaAngle(ship.Heading, leader.Heading) / 15f, -1f, 1f);
                ship.pitchInput = Mathf.Clamp((leader.Pitch - ship.Pitch) / 8f, -1f, 1f);
                ship.throttle = Mathf.Clamp01(leader.Speed / Mathf.Max(1f, ship.maxSpeed));
            }
            Doing = Current == Order.Guard ? "guarding you" : close ? "in close formation" : Current == Order.Attack ? "attacking" : "following";
        }

        void Steer(Vector3 to, float throttle)
        {
            // (round the other ships on the way: its leader, the rest of the fleet, other players)
            var v = ShipAutopilot.Travel(ship, to, null, out float clear);
            ship.rudder = v.x;
            ship.pitchInput = v.y;
            ship.throttle = throttle * clear;
        }

        static bool Dead(Transform t) => t == null || (t.TryGetComponent(out Health h) && h.Dead);

        Transform Pick()
        {
            if (Current == Order.Attack && !Dead(AttackTarget)) return AttackTarget;
            Vector3 me = transform.position, lead = leader.transform.position;
            Transform best = null;
            float bestD = float.MaxValue;
            foreach (var t in Targets.All)
            {
                if (t == null || !t.gameObject.activeInHierarchy || Dead(t) || Sensors.Hidden(t)) continue;
                // (hostiles only, as the wingmen pick them: not whatever is in the target list)
                if (!t.TryGetComponent(out TargetInfo info) || !info.Hostile) continue;
                float dMe = Vector3.Distance(t.position, me), dLead = Vector3.Distance(t.position, lead);
                // guarding: anything near the leader; otherwise only what has come close
                bool near = (Current == Order.Guard && dLead < 6000f) || dMe < 2500f;
                if (near && dMe < bestD) { bestD = dMe; best = t; }
            }
            return best;
        }
    }
}

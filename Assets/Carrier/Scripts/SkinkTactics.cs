using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// How a Vrask Skink fights, on top of its CombatPilot (which still flies it, and keeps it
    /// among the active enemy pilots for the warp lock and the music), with its point-defence guns
    /// (PointDefence) and, in a group, the fleet's phase (VraskFleet):
    ///
    /// - Combat patrol: a loose orbit, 1.5 to 2.5 km out, round one of the group's high-value
    ///   ships (its ward: the Maw, or a Snare holding a ship in its beam), each Skink its own.
    /// - High-G intercept: a threat within 6 km of the formation and it goes for it at its best
    ///   speed with its acceleration doubled: a players' missile on its way to one of them (it
    ///   puts itself across the missile's path, for its guns), a fighter or drone (it runs it
    ///   down), a ship closing on a ward (it goes at it and fights it 1.2 km out).
    /// - Point-defence web: its guns take the players' missiles, fighters and drones on their way
    ///   to the Vrask before anything else (PointDefence).
    /// - Pursuit and harassment: a light or damaged ship of the players' running, or the fleet's
    ///   pursuit after the main fight: hit-and-run passes, in to 500 m and out again.
    /// - Sensor picket: while the group closes on its target the fleet sends the Skinks ahead
    ///   (VraskFleet.PicketPoint); their sensors (VraskShip.sensorRange) give the Maw its aim.
    ///
    /// With no pilot (the siege group, on station) it doesn't move: its guns and sensors work.
    /// </summary>
    public class SkinkTactics : MonoBehaviour, CombatPilot.ITactics
    {
        public enum Duty { None, Patrol, Picket, Intercept, Pursue }

        [Tooltip("Its patrol orbit round its ward (m): nearest and farthest.")]
        public float capNear = 1500f, capFar = 2500f;
        [Tooltip("A threat this near the formation (m) and it goes for it.")]
        public float interceptRange = 6000f;
        [Tooltip("A players' ship this near a ward (m) is a threat even if it isn't closing.")]
        public float wardThreatRange = 3000f;
        [Tooltip("A players' ship closing on a ward this fast (m/s) is a threat.")]
        public float closingSpeed = 20f;
        [Tooltip("High-G: its acceleration this many times over while it intercepts or pursues.")]
        public float boost = 2f;
        [Tooltip("Without a fleet: Vrask ships this near it (m) are the formation it screens.")]
        public float formationRange = 12000f;
        [Tooltip("It chases a running cripple this near it (m).")]
        public float pursueRange = 9000f;
        [Tooltip("Hit and run: in to this far (m), then out this far past it.")]
        public float runIn = 500f, runOut = 1500f;
        [Tooltip("Light: a hull of at most this; damaged: under this share of its hull.")]
        public float lightHull = 3000f;
        [Range(0f, 1f)] public float damagedBelow = 0.5f;
        [Tooltip("Opening the range this fast (m/s) is running.")]
        public float fleeSpeed = 30f;

        public Duty Current { get; private set; }
        /// <summary>The ship it screens, what it is intercepting, what it is chasing.</summary>
        public Transform Ward { get; private set; }
        public Transform Threat { get; private set; }
        public Transform Quarry { get; private set; }
        /// <summary>Intercepts begun (and the last: "a missile at the Vrask Maw"), runs made on a
        /// quarry, chases begun, times on picket.</summary>
        public int Intercepts { get; private set; }
        public string LastIntercept { get; private set; } = "";
        public int Runs { get; private set; }
        public int Chases { get; private set; }
        public int Pickets { get; private set; }

        CombatPilot pilot;
        EnemyWeapons weapons;
        Health health;
        VraskShip self;
        float accel0 = -1f, nextThink, extendUntil;
        bool extending, threatIsShip, ownTarget;
        Vector3 extendPoint, picketAt;

        static readonly List<VraskShip> formation = new List<VraskShip>();
        static readonly List<VraskShip> wards = new List<VraskShip>();
        static readonly List<VraskShip> skinks = new List<VraskShip>();

        void Awake()
        {
            pilot = GetComponent<CombatPilot>();
            weapons = GetComponent<EnemyWeapons>();
            health = GetComponent<Health>();
            self = GetComponent<VraskShip>();
            if (pilot != null) pilot.tactics = this;
        }

        void OnDisable() => StandDown();

        void OnDestroy()
        {
            if (pilot != null && ReferenceEquals(pilot.tactics, this)) pilot.tactics = null;
        }

        void StandDown()
        {
            if (pilot != null)
            {
                if (accel0 > 0f) pilot.acceleration = accel0;
                if (ownTarget) pilot.keepTarget = false;
            }
            Current = Duty.None;
            Ward = Threat = Quarry = null;
            extending = ownTarget = false;
        }

        bool Alive => health == null || !health.Dead;

        void Update()
        {
            if (Time.time < nextThink) return;
            nextThink = Time.time + 0.25f;
            // (its pilot switched off: held still, as the self-test does)
            if (pilot != null && !pilot.enabled) { if (Current != Duty.None) StandDown(); return; }
            bool fighting = Alive && (pilot != null ? pilot.Current == CombatPilot.State.Engage : weapons != null && weapons.weaponsFree);
            if (!fighting) { if (Current != Duty.None) StandDown(); return; }
            if (pilot != null && accel0 < 0f) accel0 = pilot.acceleration;

            var fleet = VraskFleet.Of(self);
            Formation(fleet);
            var ward = PickWard(fleet);
            Ward = ward != null ? ward.transform : null;
            var lastThreat = Threat;
            Threat = FindThreat(out threatIsShip);
            var was = Current;
            Duty d;
            if (pilot == null) d = Threat != null ? Duty.Intercept : Duty.Patrol;           // (on station: its guns only)
            else if (Threat != null) d = Duty.Intercept;
            else if (fleet != null && fleet.PicketPoint(this, out picketAt)) d = Duty.Picket;
            else if ((Quarry = FindQuarry(fleet)) != null) d = Duty.Pursue;
            else d = Duty.Patrol;
            if (d != Duty.Pursue) Quarry = null;
            if (d == Duty.Intercept && (was != Duty.Intercept || Threat != lastThreat))
            {
                Intercepts++;
                LastIntercept = Describe(Threat);
            }
            if (d == Duty.Pursue && was != Duty.Pursue) Chases++;
            if (d == Duty.Picket && was != Duty.Picket) Pickets++;
            if (d != Duty.Pursue) extending = false;
            Current = d;
            if (pilot == null) return;

            // high-G while it intercepts or chases
            pilot.acceleration = d == Duty.Intercept || d == Duty.Pursue ? accel0 * boost : accel0;
            // its guns: on the ship it intercepts or chases; else the fleet's target, or its pilot's
            Transform gunsOn = d == Duty.Intercept && threatIsShip ? Threat : d == Duty.Pursue ? Quarry : fleet != null && fleet.Target != null ? fleet.Target : pilot.target;
            bool own = (d == Duty.Intercept && threatIsShip) || d == Duty.Pursue;
            if (own)
            {
                pilot.keepTarget = true;
                if (pilot.target != gunsOn) pilot.target = gunsOn;
            }
            else if (ownTarget) pilot.keepTarget = false;
            ownTarget = own;
            if (weapons != null && gunsOn != null && weapons.target != gunsOn) weapons.SetTarget(gunsOn);
        }

        static string Describe(Transform t)
        {
            if (t == null) return "";
            var m = t.GetComponent<Missile>();
            if (m != null) return $"a missile at the {Named(m.target)}";
            var c = t.GetComponent<Craft>();
            if (c != null) return $"a {c.kind.ToString().ToLowerInvariant()}";
            return $"{Named(t)} closing on its ward";
        }

        static string Named(Transform t)
        {
            if (t == null) return "-";
            var i = t.GetComponentInParent<TargetInfo>();
            return i != null ? i.Name : t.name;
        }

        static bool AliveShip(Component c)
        {
            if (c == null || !c.gameObject.activeInHierarchy) return false;
            var h = c.GetComponentInParent<Health>();
            return h == null || !h.Dead;
        }

        // ------------------------------------------------------------------ the formation

        /// <summary>The ships it screens: its fleet's, or the Vrask near it.</summary>
        void Formation(VraskFleet fleet)
        {
            formation.Clear();
            if (fleet != null) { foreach (var v in fleet.ships) if (AliveShip(v)) formation.Add(v); return; }
            float r2 = formationRange * formationRange;
            foreach (var v in VraskShip.All)
                if (AliveShip(v) && (v.transform.position - transform.position).sqrMagnitude < r2) formation.Add(v);
        }

        static bool HighValue(VraskShip v)
        {
            if (v.role == VraskShip.Role.Maw) return true;
            if (v.role != VraskShip.Role.Snare) return false;
            var beam = v.GetComponent<TractorBeam>();
            return beam != null && beam.Holding != null;
        }

        /// <summary>Its ward: the Maw first, then the Snares holding a ship, shared out among the
        /// Skinks in turn (none: its pilot's own escorting).</summary>
        VraskShip PickWard(VraskFleet fleet)
        {
            wards.Clear();
            skinks.Clear();
            foreach (var v in formation)
            {
                if (v.role == VraskShip.Role.Skink) skinks.Add(v);
                else if (HighValue(v)) wards.Add(v);
            }
            if (wards.Count == 0) return null;
            // (the Maws first, the rest in the formation's own order: every Skink sees the same list)
            int maws = 0;
            for (int k = 0; k < wards.Count; k++)
                if (wards[k].role == VraskShip.Role.Maw) { var m = wards[k]; wards.RemoveAt(k); wards.Insert(maws++, m); }
            int i = fleet != null ? fleet.SkinkIndex(self) : Mathf.Max(0, skinks.IndexOf(self));
            return wards[i % wards.Count];
        }

        // ------------------------------------------------------------------ threats

        /// <summary>The most urgent threat to the formation: a players' missile on its way to one
        /// of it, a players' fighter or drone near it, or a players' ship closing on a ward;
        /// soonest there first.</summary>
        Transform FindThreat(out bool isShip)
        {
            isShip = false;
            Transform best = null;
            float soonest = float.MaxValue;
            foreach (var m in Missile.Active)
            {
                if (m == null || m.Hostile || m.owner == null || m.target == null) continue;
                var at = m.target.GetComponentInParent<VraskShip>();
                if (at == null || !formation.Contains(at)) continue;
                float d = Vector3.Distance(m.transform.position, at.transform.position);
                if (d > interceptRange) continue;
                float t = d / Mathf.Max(50f, m.Velocity.magnitude);
                if (t < soonest) { soonest = t; best = m.transform; }
            }
            foreach (var s in PlayerShips.All)
            {
                var cc = s != null ? s.GetComponent<CarrierController>() : null;
                if (cc == null) continue;
                foreach (var c in cc.Wing)
                {
                    if (c == null || !c.isActiveAndEnabled || c.kind == Craft.Kind.Dropship) continue;
                    float d = NearestOf(formation, c.transform.position);
                    if (d > interceptRange) continue;
                    float t = d / Mathf.Max(50f, c.speed);
                    if (t < soonest) { soonest = t; best = c.transform; }
                }
            }
            foreach (var s in PlayerShips.All)
            {
                if (s == null || !PlayerShips.Present(s) || !AliveShip(s)) continue;
                // (one a Snare holds is the Snares' and the Fang's: no threat to the ships they screen)
                if (TractorBeam.BeamsOn(s) > 0) continue;
                var mover = Motion.Of(s);
                Vector3 v = mover != null ? mover.Velocity : Vector3.zero;
                foreach (var w in formation)
                {
                    if (!HighValue(w)) continue;
                    Vector3 off = s.position - w.transform.position;
                    float d = off.magnitude;
                    if (d > interceptRange) continue;
                    float closing = -Vector3.Dot(v, off / Mathf.Max(1f, d));
                    if (d > wardThreatRange && closing < closingSpeed) continue;
                    float t = d / Mathf.Max(closingSpeed, closing);
                    if (t < soonest) { soonest = t; best = s; isShip = true; }
                }
            }
            if (best != null && best.GetComponent<Missile>() != null) isShip = false;
            if (best != null && best.GetComponent<Craft>() != null) isShip = false;
            return best;
        }

        static float NearestOf(List<VraskShip> ships, Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var v in ships) best = Mathf.Min(best, Vector3.Distance(v.transform.position, p));
            return best;
        }

        /// <summary>A cripple to chase: in the fleet's pursuit, the target running or the nearest
        /// light or damaged players' ship; otherwise only one running (not while on picket).</summary>
        Transform FindQuarry(VraskFleet fleet)
        {
            bool pursuit = fleet != null && fleet.Current == VraskFleet.Phase.Pursue;
            if (fleet != null && fleet.Current == VraskFleet.Phase.Withdraw) return null;
            Vector3 from = fleet != null ? fleet.Centre(false) : transform.position;
            Transform best = null;
            float bestD = pursueRange;
            foreach (var s in PlayerShips.All)
            {
                if (s == null || !PlayerShips.Present(s) || !AliveShip(s)) continue;
                float d = Vector3.Distance(s.position, transform.position);
                if (d > bestD) continue;
                bool running = VraskFleet.Running(s, from, lightHull, damagedBelow, fleeSpeed, 0f);
                if (!running && !(pursuit && Cripple(s))) continue;
                bestD = d;
                best = s;
            }
            return best;
        }

        bool Cripple(Transform t)
        {
            var h = t.GetComponentInParent<Health>();
            var info = t.GetComponentInParent<TargetInfo>();
            return (info != null && info.IsSmallCraft) || (h != null && (h.maxHealth <= lightHull || h.Current < h.maxHealth * damagedBelow));
        }

        // ------------------------------------------------------------------ flying

        /// <summary>CombatPilot's hook: where it flies on each duty (nothing on its own: the
        /// pilot's escorting).</summary>
        public void Steer(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            if (!isActiveAndEnabled || pilot == null) return;
            switch (Current)
            {
                case Duty.Picket:
                {
                    Vector3 to = picketAt - pos;
                    float d = to.magnitude;
                    var fleet = VraskFleet.Of(self);
                    if (d < 300f && fleet != null && fleet.Target != null) { desired = fleet.Target.position - pos; wantSpeed = 0f; return; }
                    desired = to;
                    wantSpeed = d > 1500f ? pilot.attackSpeed : Mathf.Lerp(pilot.cruiseSpeed * 0.4f, pilot.cruiseSpeed, d / 1500f);
                    return;
                }
                case Duty.Patrol:
                    if (Ward != null) Orbit(pos, ref desired, ref wantSpeed);
                    return;
                case Duty.Intercept:
                    if (Threat != null) Intercept(pos, ref desired, ref wantSpeed);
                    return;
                case Duty.Pursue:
                    if (Quarry != null) HitAndRun(pos, ref desired, ref wantSpeed);
                    return;
            }
        }

        /// <summary>A loose orbit round its ward, a little above or below it, each Skink its own
        /// way round (its pilot's circling) and at its own radius.</summary>
        void Orbit(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            Vector3 w = Ward.position, axis = Ward.up;
            Vector3 off = Vector3.ProjectOnPlane(pos - w, axis);
            if (off.sqrMagnitude < 1f) off = Ward.right;
            float sign = pilot.circleSpeed < 0f ? -1f : 1f;
            float r = Mathf.Lerp(capNear, capFar, sign > 0f ? 0.35f : 0.75f);
            Vector3 next = w + Quaternion.AngleAxis(25f * sign, axis) * off.normalized * r + axis * (150f * sign);
            desired = next - pos;
            float miss = Mathf.Abs(off.magnitude - r);
            wantSpeed = miss > 1500f ? pilot.attackSpeed : pilot.cruiseSpeed * 0.6f;
        }

        /// <summary>At a missile: across its path to its target, the nearest point of it. At a
        /// craft: run it down. At a ship: at it, then round it 1.2 km out.</summary>
        void Intercept(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            wantSpeed = pilot.attackSpeed;
            var m = Threat.GetComponent<Missile>();
            if (m != null)
            {
                Vector3 a = m.transform.position, b = m.target != null ? m.target.position : a + m.Velocity;
                Vector3 ab = b - a;
                float k = ab.sqrMagnitude > 1f ? Mathf.Clamp01(Vector3.Dot(pos - a, ab) / ab.sqrMagnitude) : 0f;
                desired = a + ab * k - pos;
                return;
            }
            var mover = Motion.Of(Threat);
            Vector3 v = mover != null ? mover.Velocity : Vector3.zero;
            Vector3 to = Threat.position - pos;
            if (threatIsShip && to.magnitude < 1200f)
            {
                desired = Vector3.Cross(to, Threat.up).normalized + (to.magnitude < 900f ? -to.normalized * 0.5f : Vector3.zero);
                wantSpeed = pilot.cruiseSpeed;
                return;
            }
            desired = Ballistics.Lead(pos, Threat.position, v, Mathf.Max(1f, pilot.attackSpeed + 400f)) - pos;
        }

        /// <summary>Hit and run: in fast to 500 m (its guns firing), out past it and to one side,
        /// round and in again.</summary>
        void HitAndRun(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            wantSpeed = pilot.attackSpeed;
            Vector3 tp = Quarry.position;
            if (extending)
            {
                if (Vector3.Distance(pos, extendPoint) < 250f || Time.time > extendUntil) extending = false;
                else { desired = extendPoint - pos; return; }
            }
            Vector3 to = tp - pos;
            if (to.magnitude < runIn)
            {
                Vector3 side = Vector3.Cross(to.normalized, Quarry.up);
                if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(to.normalized, Vector3.right);
                extendPoint = tp + (to.normalized * 0.7f + side.normalized * (Random.value < 0.5f ? 0.7f : -0.7f)).normalized * runOut;
                extendUntil = Time.time + 8f;
                extending = true;
                Runs++;
                desired = extendPoint - pos;
                return;
            }
            var mover = Motion.Of(Quarry);
            desired = Ballistics.Lead(pos, tp, mover != null ? mover.Velocity : Vector3.zero, Mathf.Max(1f, pilot.attackSpeed + 400f)) - pos;
        }
    }
}

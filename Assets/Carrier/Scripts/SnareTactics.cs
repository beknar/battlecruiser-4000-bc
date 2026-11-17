using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// How a Vrask Snare fights, on top of its CombatPilot (which still flies it, and keeps it
    /// among the active enemy pilots for the warp lock and the music):
    ///
    /// - Stand-off disable: it holds 1.8 to 2.5 km off, bow on, and doesn't close. Its ion fire
    ///   goes for the target's shield generator (ShipSystems.SetAim), then, with the shield down,
    ///   for its engines.
    /// - Tractor drag: once the target's engines are knocked out, or it is crawling with its
    ///   shield down, it locks its tractor beam (TractorBeam) and pulls it in, standing off a
    ///   little closer while it holds it.
    /// - Wolf pack: Snares near each other work one target together. One keeps up the ion fire;
    ///   the others hold the target in their beams (two can't be flown out of) and turn their
    ///   guns on any other ship that comes near it.
    /// - No knife fights: when a ship comes within 900 m it breaks away, and comes back in at its
    ///   range.
    /// - Hand-off: it holds a disabled ship for the boarding ships to come (TractorBeam.HeldTargets).
    /// </summary>
    public class SnareTactics : MonoBehaviour, CombatPilot.ITactics
    {
        /// <summary>Solo: the only Snare on its target (ion, then the tractor). Disable: the
        /// pack's ion ship. Hold: one of the pack's beams (and its cover).</summary>
        public enum Duty { None, Solo, Disable, Hold }

        [Tooltip("The near edge of the range it fights at (m): it comes back in to here after breaking away.")]
        public float near = 1800f;
        [Tooltip("A ship this close (m) and it breaks away.")]
        public float knifeRange = 900f;
        [Tooltip("Its stand-off (m) while it holds a ship in its beam.")]
        public float holdStandoff = 1800f;
        [Tooltip("A target slower than this (m/s), its shield down, is ready to be held.")]
        public float slowSpeed = 30f;
        [Tooltip("Snares this near each other (m) work one target together.")]
        public float packRange = 8000f;
        [Tooltip("A holder turns its guns on another ship this near the one it holds (m).")]
        public float coverRange = 3000f;

        public Duty Current { get; private set; }
        /// <summary>The ship it is working (null out of the fight).</summary>
        public Transform Target { get; private set; }
        /// <summary>Breaking away from a ship too close (Threat).</summary>
        public bool BreakingOff { get; private set; }
        public Transform Threat { get; private set; }
        public int BreakOffs { get; private set; }

        CombatPilot pilot;
        EnemyWeapons weapons;
        TractorBeam beam;
        float standoff0, nextThink;
        // the target's parts, looked up once for each target
        Transform lookedAt;
        ShipSystems targetSystems;
        Shield targetShield;
        Demo.RemotePlayer targetRemote;

        static readonly Dictionary<Transform, List<SnareTactics>> packs = new Dictionary<Transform, List<SnareTactics>>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPacks() => packs.Clear();

        /// <summary>The Snares working a ship, in the order they came to it.</summary>
        public static IReadOnlyList<SnareTactics> PackOn(Transform ship) =>
            ship != null && packs.TryGetValue(ship, out var list) ? list : (IReadOnlyList<SnareTactics>)System.Array.Empty<SnareTactics>();

        void Awake()
        {
            pilot = GetComponent<CombatPilot>();
            weapons = GetComponent<EnemyWeapons>();
            beam = GetComponent<TractorBeam>();
            if (pilot != null)
            {
                pilot.tactics = this;
                standoff0 = pilot.standoff;
            }
        }

        void OnDisable() => StandDown();

        void OnDestroy()
        {
            if (pilot != null && ReferenceEquals(pilot.tactics, this)) pilot.tactics = null;
        }

        /// <summary>Out of the fight: it lets go, leaves the pack, and its pilot picks targets again.</summary>
        void StandDown()
        {
            Leave();
            if (beam != null && beam.Locked) beam.Release(beam.Working(out string why) ? "out of the fight" : why);
            ShipSystems.SetAim(transform.root, ShipSystem.Hull);
            if (pilot != null)
            {
                pilot.keepTarget = false;
                pilot.standoff = standoff0;
            }
            Current = Duty.None;
            BreakingOff = false;
            Threat = null;
        }

        void Update()
        {
            if (pilot == null || Time.time < nextThink) return;
            nextThink = Time.time + 0.25f;
            var t = pilot.Alive && pilot.Current == CombatPilot.State.Engage ? Choose() : null;
            if (t == null)
            {
                if (Current != Duty.None || Target != null) StandDown();
                return;
            }
            Join(t);
            pilot.keepTarget = true;
            if (pilot.target != t) pilot.target = t;
            Look(t);

            // its duty in the pack: the first that isn't holding it keeps up the ion fire
            var pack = packs[t];
            SnareTactics ion = null;
            foreach (var s in pack) if (s.beam == null || s.beam.Holding != t) { ion = s; break; }
            Current = pack.Count == 1 ? Duty.Solo : ion == this ? Duty.Disable : Duty.Hold;

            // ion on the shield generator, then (the shield down) on the engines
            bool shieldDown = targetShield == null || !targetShield.Up ||
                              (targetSystems != null && targetSystems.IsDown(ShipSystem.Shields)) ||
                              (targetRemote != null && targetRemote.Shield <= 0.01f);
            bool enginesOut = targetSystems != null && targetSystems.EnginesDown;
            var mover = Motion.Of(t);
            bool crawling = mover == null || mover.Velocity.magnitude < slowSpeed;
            ShipSystems.SetAim(transform.root, shieldDown ? ShipSystem.Engines : ShipSystem.Shields);

            // the knife fight: a ship too close (one held fast can't close on it)
            var threat = NearestShip(transform.position, null, out float threatAt, true);
            if (threat != null && threatAt < knifeRange)
            {
                if (!BreakingOff) BreakOffs++;
                BreakingOff = true;
                Threat = threat;
            }
            else if (BreakingOff && (Threat == null || !Alive(Threat) || Vector3.Distance(Threat.position, transform.position) > near))
            {
                BreakingOff = false;
                Threat = null;
            }

            // the tractor: alone, or holding for the pack, once the target is ready; let go only to
            // break away from it, or to take up the ion fire
            if (beam != null)
            {
                bool may = Current != Duty.Disable && !(BreakingOff && Threat == t);
                bool ready = enginesOut || (crawling && shieldDown);
                if (!may) { if (beam.Holding != null) beam.Release(BreakingOff ? "breaking away" : "on the ion fire"); }
                else if (beam.Holding != null && beam.Holding != t) beam.Release("on to another ship");
                else if (ready && beam.Holding != t && (t.position - beam.From).sqrMagnitude <= beam.range * beam.range) beam.Lock(t, out _);
            }
            bool holding = beam != null && beam.Holding == t;
            pilot.standoff = holding ? holdStandoff : standoff0;

            // a holder's guns go to another ship that comes near the one it holds
            Transform gunsOn = t;
            if (Current == Duty.Hold && holding)
            {
                var escort = NearestShip(t.position, t, out float escortAt, false);
                if (escort != null && escortAt < coverRange) gunsOn = escort;
            }
            if (weapons != null && weapons.target != gunsOn) weapons.SetTarget(gunsOn);
        }

        /// <summary>CombatPilot's hook: breaking away, it flies off from the ship too close, at its
        /// best speed and a little to one side (not straight down that ship's line of fire).</summary>
        public void Steer(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            // (holding a ship: it stays put, bow on it, so a ship with its engines can pull away
            // out of the beam's reach, and one without is drawn in)
            if (!BreakingOff && beam != null && beam.Holding != null && beam.Holding == Target)
            {
                desired = beam.Holding.position - pos;
                wantSpeed = 0f;
                return;
            }
            if (!BreakingOff || Threat == null) return;
            Vector3 away = pos - Threat.position;
            if (away.sqrMagnitude < 1f) away = -transform.forward;
            away.Normalize();
            desired = away + Vector3.Cross(away, transform.up) * 0.35f;
            wantSpeed = pilot.attackSpeed;
        }

        // ------------------------------------------------------------------ the pack

        /// <summary>The ship to work: the one it is on while that lives; else one a Snare near it
        /// is working; else its pilot's target.</summary>
        Transform Choose()
        {
            if (Target != null && Alive(Target)) return Target;
            float r2 = packRange * packRange;
            foreach (var kv in packs)
            {
                if (kv.Key == null || !Alive(kv.Key)) continue;
                foreach (var s in kv.Value)
                    if (s != null && s != this && (s.transform.position - transform.position).sqrMagnitude < r2) return kv.Key;
            }
            var t = TractorBeam.ShipOf(pilot.target);
            return t != null && Alive(t) ? t : null;
        }

        void Join(Transform t)
        {
            if (Target == t && packs.TryGetValue(t, out var mine) && mine.Contains(this)) return;
            Leave();
            Target = t;
            if (!packs.TryGetValue(t, out var list)) packs[t] = list = new List<SnareTactics>();
            list.Add(this);
        }

        void Leave()
        {
            if (!ReferenceEquals(Target, null) && packs.TryGetValue(Target, out var list))
            {
                list.Remove(this);
                if (list.Count == 0) packs.Remove(Target);
            }
            Target = null;
        }

        void Look(Transform t)
        {
            if (lookedAt == t) return;
            lookedAt = t;
            targetSystems = t.GetComponent<ShipSystems>();
            targetShield = t.GetComponentInChildren<Shield>(true);
            targetRemote = t.GetComponent<Demo.RemotePlayer>();
        }

        static bool Alive(Transform t)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return false;
            var h = t.GetComponent<Health>();
            return (h == null || !h.Dead) && PlayerShips.Present(t);
        }

        /// <summary>The nearest of the players' ships (theirs, their wingmen's, their fleets') to a
        /// point, but `except`; `skipHeld`: not one held fast in a beam.</summary>
        static Transform NearestShip(Vector3 p, Transform except, out float distance, bool skipHeld)
        {
            Transform best = null;
            float bestD = float.MaxValue;
            foreach (var s in PlayerShips.All)
            {
                if (s == null || s == except || !Alive(s)) continue;
                if (skipHeld && TractorBeam.Firm(s)) continue;
                float d = (s.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            distance = best != null ? Mathf.Sqrt(bestD) : float.MaxValue;
            return best;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// How a group of Vrask ships fights together: the raiders' band (an EncounterGroup) or the
    /// siege group on station over a city (a DefenseGrid, no pilots). It runs the fight as a
    /// sequence of phases, each moving on when something is so on the ship it is working (its
    /// priority target), and steers each ship's own tactics (SnareTactics, FangTactics, MawTactics,
    /// SkinkTactics) only through what they already listen to: the pilot's target, and a hold
    /// order on the pilot (CombatPilot.orders). It doesn't fly or shoot anything itself.
    ///
    /// - Approach and picket: the Skinks run ahead of the rest toward the target as the group's
    ///   eyes (SkinkTactics); the Snares and the Fang come no nearer than the hold line. It ends at
    ///   contact: one of them has the target within its sensors (a Skink's reach further:
    ///   VraskShip.sensorRange), which is also what the Maw needs to fire (MawTactics).
    /// - Bombard: the Maw's artillery opens at long range while the rest hold the line, to break
    ///   the shield and make the target react; the Skinks fall back to screen the high-value ships.
    ///   It ends when the shield is under half (or down), when the target closes on the line, or
    ///   after a while.
    /// - Screen and disable: the Snares go in (ion, then the tractor); the Fang still holds.
    /// - Board: once the target is disabled (its engines out, held fast in the beams, held with its
    ///   shield down, or boarders already aboard) the Fang is let go to breach and take it. Free
    ///   again for a while, it goes back to the Snares.
    /// - Pursue: the target taken, destroyed, gone, or a light or damaged one running: the Skinks
    ///   chase cripples (SkinkTactics); the rest pick up the next target after a while.
    /// - Withdraw: the group has lost its Maw and every Snare. The rest break off and go back into
    ///   hiding (they defend themselves if shot at), and the band doesn't spring again until its
    ///   ships are back (EncounterGroup.holdOff). The siege's escorts go quiet till the Maw is back.
    ///
    /// The priority target: a player's ship first (the nearest, in co-op), else the one most worth
    /// taking and easiest to disable (Score). Host-side only: a co-op guest's copy is switched off
    /// (CoopWorld), as the group's ambush is.
    /// </summary>
    public class VraskFleet : MonoBehaviour
    {
        public enum Phase { Idle, Picket, Bombard, Disable, Board, Pursue, Withdraw }

        [Tooltip("Its ships.")]
        public VraskShip[] ships = new VraskShip[0];
        [Tooltip("The raiders' band (piloted ships; it springs the ambush), if it is one.")]
        public EncounterGroup group;
        [Tooltip("The siege group's defence grid (no pilots: its guns open up by themselves), if it is one.")]
        public DefenseGrid grid;

        [Header("The sequence")]
        [Tooltip("The Snares and the Fang come no nearer the target (m) until their phase.")]
        public float holdLine = 5500f;
        [Tooltip("The longest the bombardment goes on alone (s).")]
        public float bombardTime = 45f;
        [Tooltip("The target's shield under this share and the bombardment has done its work.")]
        [Range(0f, 1f)] public float bombardShield = 0.5f;
        [Tooltip("The target this near a Snare or the Fang (m) is reacting: the Snares go in.")]
        public float reactRange = 4000f;
        [Tooltip("The target free of being disabled this long (s), and the boarding is off: back to the Snares.")]
        public float recoverTime = 15f;
        [Tooltip("After the target is taken or destroyed, how long (s) before the next one.")]
        public float pursueTime = 20f;

        [Header("The picket")]
        [Tooltip("How far (m) ahead of the rest the Skinks run.")]
        public float picketLead = 2500f;
        [Tooltip("And no nearer the target (m) than this.")]
        public float picketStandoff = 4000f;
        [Tooltip("How far (m) any of its ships sees (a Skink's own sensors reach further).")]
        public float sensorRange = 6000f;

        [Header("Targets")]
        [Tooltip("A ship this near the group (m) can be its target.")]
        public float engageRange = 20000f;
        [Tooltip("A light (hull at most this) or damaged ship opening the range this fast (m/s) beyond fleeRange is running.")]
        public float lightHull = 3000f;
        [Range(0f, 1f)] public float damagedBelow = 0.5f;
        public float fleeSpeed = 30f, fleeRange = 6000f;

        // ------------------------------------------------------------------ what it knows

        /// <summary>What the phases go by, about the group and its target.</summary>
        public struct Facts
        {
            /// <summary>In the fight (a pilot out of hiding; the siege's grid engaged).</summary>
            public bool engaged;
            public bool hasTarget;
            /// <summary>One of them has the target within its sensors.</summary>
            public bool contact;
            /// <summary>A Maw alive with its artillery; a Snare or the Fang alive.</summary>
            public bool artillery, captors;
            /// <summary>No Snare of the group alive (nothing to disable the target with ion and tractor).</summary>
            public bool noSnares;
            /// <summary>The Maw and every Snare lost.</summary>
            public bool withdraw;
            /// <summary>The ship it was working was taken or destroyed just now.</summary>
            public bool targetLost;
            /// <summary>The target, light or damaged, is running.</summary>
            public bool fleeing;
            /// <summary>The target's shield (a share; 0: down).</summary>
            public float shield;
            public bool shieldDown, enginesDown, held, heldFast, boardersAboard;
            /// <summary>The target has come within reactRange of a Snare or the Fang.</summary>
            public bool closing;
            /// <summary>Seconds the target has been free of being disabled.</summary>
            public float freeFor;

            /// <summary>Ready to be boarded: its engines out, held fast, held with its shield
            /// down, or boarders aboard already.</summary>
            public bool Disabled => enginesDown || heldFast || (held && shieldDown) || boardersAboard;
        }

        /// <summary>The phase after `now`, `inPhase` seconds into it, on these facts.</summary>
        public static Phase Next(Phase now, Facts f, float inPhase, float bombardTime = 45f, float bombardShield = 0.5f,
                                 float recoverTime = 15f, float pursueTime = 20f)
        {
            // lost the Maw and the Snares: away, until they are back
            if (f.withdraw) return now == Phase.Idle && !f.engaged ? Phase.Idle : Phase.Withdraw;
            if (!f.engaged) return Phase.Idle;
            if (f.targetLost || (f.hasTarget && f.fleeing)) return Phase.Pursue;
            if (now == Phase.Pursue && (!f.hasTarget || inPhase < pursueTime)) return Phase.Pursue;
            // (every new start goes by way of the picket, however short)
            if (!f.hasTarget || now == Phase.Idle || now == Phase.Pursue || now == Phase.Withdraw) return Phase.Picket;
            switch (now)
            {
                case Phase.Picket:
                    if (!f.contact) return Phase.Picket;
                    if (f.Disabled) return f.captors ? Phase.Board : Phase.Bombard;
                    return f.artillery ? Phase.Bombard : Phase.Disable;
                case Phase.Bombard:
                    if (!f.captors) return f.artillery ? Phase.Bombard : Phase.Disable;
                    if (f.noSnares) return Phase.Board;
                    if (f.Disabled) return Phase.Board;
                    if (!f.artillery || f.shieldDown || f.shield <= bombardShield || f.closing || inPhase >= bombardTime) return Phase.Disable;
                    return Phase.Bombard;
                case Phase.Disable:
                    // (with no Snare to disable it, the Fang goes in as it is)
                    return f.Disabled || (f.noSnares && f.captors) ? Phase.Board : Phase.Disable;
                case Phase.Board:
                    // (back to the Snares, if it has any: with none, the Fang goes on as it is)
                    return !f.Disabled && f.freeFor >= recoverTime && !f.noSnares ? Phase.Disable : Phase.Board;
            }
            return now;
        }

        /// <summary>How much a ship (not a player's) is worth going for: what it is worth (hull and
        /// shield), how easy it is to disable now (its shield and engines worn down), nearer first.</summary>
        public static float Score(float value, float shieldShare, float engineCondition, float distance) =>
            value * (0.25f + (1f - Mathf.Clamp01(shieldShare)) * 0.5f + (1f - Mathf.Clamp01(engineCondition)) * 0.5f) /
            (1f + Mathf.Max(0f, distance) / 10000f);

        /// <summary>A phase as the logs and the docs name it.</summary>
        public static string Name(Phase p)
        {
            switch (p)
            {
                case Phase.Picket: return "approach and picket";
                case Phase.Bombard: return "bombard";
                case Phase.Disable: return "screen and disable";
                case Phase.Board: return "board";
                case Phase.Pursue: return "pursue";
                case Phase.Withdraw: return "withdraw";
                default: return "idle";
            }
        }

        // ------------------------------------------------------------------ where it is

        public Phase Current { get; private set; }
        /// <summary>When the phase began, and why.</summary>
        public float Since { get; private set; }
        public string Why { get; private set; } = "";
        /// <summary>The ship it is working.</summary>
        public Transform Target { get; private set; }
        /// <summary>Who has the target in sight, and from how far (m).</summary>
        public VraskShip ContactBy { get; private set; }
        public float ContactRange { get; private set; }
        /// <summary>Who made contact when the picket ended, and from how far (m).</summary>
        public VraskShip ContactMadeBy { get; private set; }
        public float ContactMadeAt { get; private set; }
        public Facts Last { get; private set; }
        /// <summary>Every change of phase so far ("12.3 s bombard: ...").</summary>
        public readonly List<string> History = new List<string>();
        /// <summary>Each phase it has been in, in order.</summary>
        public readonly List<Phase> Phases = new List<Phase>();
        public static event System.Action<VraskFleet> Changed;

        static readonly Dictionary<VraskShip, VraskFleet> of = new Dictionary<VraskShip, VraskFleet>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOf() => of.Clear();

        /// <summary>The fleet a ship fights in (null: none, or it is switched off).</summary>
        public static VraskFleet Of(VraskShip v) => v != null && of.TryGetValue(v, out var f) && f != null && f.isActiveAndEnabled ? f : null;

        // the hold order on each pilot, and the pilots it has given a target
        sealed class Hold : CombatPilot.ITactics
        {
            public VraskFleet fleet;
            public CombatPilot pilot;
            public void Steer(Vector3 pos, ref Vector3 desired, ref float wantSpeed) => fleet.HoldLine(pilot, pos, ref desired, ref wantSpeed);
        }
        readonly Dictionary<CombatPilot, Hold> holds = new Dictionary<CombatPilot, Hold>();
        readonly HashSet<CombatPilot> kept = new HashSet<CombatPilot>();
        readonly List<EnemyWeapons> quieted = new List<EnemyWeapons>();
        Transform lastTarget;
        float nextThink, disabledAt = -1f;

        void OnEnable()
        {
            foreach (var v in ships) if (v != null) of[v] = this;
            Since = Time.time;
        }

        void OnDisable()
        {
            foreach (var v in ships) if (v != null && of.TryGetValue(v, out var f) && f == this) of.Remove(v);
            if (Current == Phase.Withdraw) LeaveWithdraw();
            ClearOrders();
            Current = Phase.Idle;
            Target = null;
        }

        void Update()
        {
            if (Time.time < nextThink) return;
            nextThink = Time.time + 0.25f;
            var f = Gather();
            Last = f;
            var next = Next(Current, f, Time.time - Since, bombardTime, bombardShield, recoverTime, pursueTime);
            if (next != Current) Enter(next, f);
            Apply();
        }

        /// <summary>Into a phase: what changes, and the log.</summary>
        void Enter(Phase next, Facts f)
        {
            var was = Current;
            if (was == Phase.Withdraw) LeaveWithdraw();
            if (was == Phase.Picket && f.contact) { ContactMadeBy = ContactBy; ContactMadeAt = ContactRange; }
            Current = next;
            Since = Time.time;
            Why = Reason(next, f);
            Phases.Add(next);
            History.Add($"{Time.time:0.0} s {Name(next)}: {Why}");
            if (next == Phase.Withdraw) StartWithdraw();
            Changed?.Invoke(this);
        }

        string Reason(Phase p, Facts f)
        {
            string t = Target != null ? NameOf(Target) : "no target";
            switch (p)
            {
                case Phase.Picket: return Target != null ? $"closing on {t}, the Skinks ahead" : "in the fight, no target";
                case Phase.Bombard: return $"contact: {NameOf(ContactBy)} has {t} at {ContactRange / 1000f:0.0} km; the artillery opens";
                case Phase.Disable:
                    return !f.artillery ? $"no artillery: the Snares go in on {t}" : f.shieldDown || f.shield <= bombardShield ? $"{t}'s shield at {f.shield:P0}: the Snares go in"
                         : f.closing ? $"{t} closes on the line: the Snares go in" : f.Disabled ? $"{t} free again" : $"{t} shelled: the Snares go in";
                case Phase.Board:
                    if (f.noSnares && !f.Disabled) return $"no Snares left: the Fang goes in on {t} as it is";
                    return $"{t} disabled ({(f.enginesDown ? "engines out" : f.heldFast ? "held fast" : f.held ? "held, its shield down" : "boarders aboard")}): the Fang goes in";
                case Phase.Pursue: return f.targetLost ? "the target taken or destroyed: after the cripples" : f.fleeing ? $"{t} is running: after it" : "nothing left to work";
                case Phase.Withdraw: return "the Maw and the Snares lost: breaking off";
                default: return "out of the fight";
            }
        }

        static string NameOf(Component c)
        {
            if (c == null) return "-";
            var i = c.GetComponentInParent<TargetInfo>();
            return i != null ? i.Name : c.name;
        }

        // ------------------------------------------------------------------ the facts

        static bool AliveShip(VraskShip v)
        {
            if (v == null || !v.gameObject.activeInHierarchy) return false;
            var h = v.GetComponent<Health>();
            return h == null || !h.Dead;
        }

        static bool Gone(Transform t)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return true;
            var h = t.GetComponentInParent<Health>();
            return h != null && h.Dead;
        }

        Facts Gather()
        {
            var f = new Facts();
            bool hadMaw = false, hadSnare = false, maw = false, snare = false, fang = false;
            foreach (var v in ships)
            {
                if (v == null) continue;
                bool alive = AliveShip(v);
                if (v.role == VraskShip.Role.Maw) { hadMaw = true; if (alive && v.GetComponentInChildren<ArtilleryGun>(true) != null) maw = true; }
                if (v.role == VraskShip.Role.Snare) { hadSnare = true; snare |= alive; }
                if (v.role == VraskShip.Role.Fang) fang |= alive;
                if (!alive) continue;
                var pl = v.GetComponent<CombatPilot>();
                if (pl != null && pl.enabled && pl.Current != CombatPilot.State.Lurk) f.engaged = true;
            }
            if (grid != null) f.engaged = grid.Engaged;
            f.artillery = maw;
            f.captors = snare || fang;
            f.noSnares = !snare;
            f.withdraw = (hadMaw || hadSnare) && !maw && !snare;

            Target = f.engaged && !f.withdraw ? Choose() : null;
            f.targetLost = lastTarget != null && Target != lastTarget && Gone(lastTarget);
            if (Target != lastTarget) disabledAt = -1f;
            lastTarget = Target;
            f.hasTarget = Target != null;
            if (!f.hasTarget) { ContactBy = null; return f; }

            // (the ship its Snares, else its Fang, are working: in co-op it can be another player's
            // than the one the fleet would choose)
            var t = Working() ?? Target;
            ContactBy = Sees(t, out float at);
            ContactRange = at;
            f.contact = ContactBy != null;
            f.shield = ShieldShare(t);
            var sys = t.GetComponent<ShipSystems>();
            f.shieldDown = f.shield <= 0.01f || (sys != null && sys.IsDown(ShipSystem.Shields));
            f.enginesDown = sys != null && sys.EnginesDown;
            f.held = TractorBeam.BeamsOn(t) > 0;
            f.heldFast = TractorBeam.Firm(t);
            f.boardersAboard = Boarders.CountOn(t) > 0;
            if (f.Disabled || disabledAt < 0f) disabledAt = Time.time;
            f.freeFor = Time.time - disabledAt;
            float near = float.MaxValue;
            foreach (var v in ships)
                if ((v != null && (v.role == VraskShip.Role.Snare || v.role == VraskShip.Role.Fang)) && AliveShip(v))
                    near = Mathf.Min(near, Vector3.Distance(v.transform.position, t.position));
            f.closing = near < reactRange;
            f.fleeing = Running(t, Centre(false), lightHull, damagedBelow, fleeSpeed, fleeRange);
            return f;
        }

        /// <summary>The ship its Snares (else its Fang) are working on, if any.</summary>
        Transform Working()
        {
            Transform fangs = null;
            foreach (var v in ships)
            {
                if (v == null || !AliveShip(v)) continue;
                var st = v.GetComponent<SnareTactics>();
                if (st != null && st.isActiveAndEnabled && st.Target != null) return st.Target;
                var ft = v.GetComponent<FangTactics>();
                if (fangs == null && ft != null && ft.isActiveAndEnabled && ft.Target != null) fangs = ft.Target;
            }
            return fangs;
        }

        /// <summary>The one of its ships that has `t` in its sensors (the nearest), and how far.</summary>
        VraskShip Sees(Transform t, out float distance)
        {
            VraskShip by = null;
            distance = float.MaxValue;
            foreach (var v in ships)
            {
                if (!AliveShip(v)) continue;
                float d = Vector3.Distance(v.transform.position, t.position);
                if (d <= Mathf.Max(sensorRange, v.sensorRange) && d < distance) { distance = d; by = v; }
            }
            return by;
        }

        /// <summary>A shield's strength as a share (a co-op stand-in's as its game sends it).</summary>
        public static float ShieldShare(Transform t)
        {
            var rp = t.GetComponent<Demo.RemotePlayer>();
            if (rp != null) return rp.Shield;
            var s = t.GetComponentInChildren<Shield>(true);
            return s != null && s.Up ? s.Fraction : 0f;
        }

        /// <summary>A light or damaged ship opening the range from `from` (it is running), or
        /// charging its warp drive to get away.</summary>
        public static bool Running(Transform t, Vector3 from, float lightHull, float damagedBelow, float speed, float range)
        {
            if (t == null) return false;
            var h = t.GetComponentInParent<Health>();
            var info = t.GetComponentInParent<TargetInfo>();
            bool light = (info != null && info.IsSmallCraft) || (h != null && h.maxHealth <= lightHull);
            bool damaged = h != null && h.Current < h.maxHealth * damagedBelow;
            if (!light && !damaged) return false;
            var drive = t.GetComponent<WarpDrive>();
            if (drive != null && drive.Current == WarpDrive.State.Charging) return true;
            Vector3 away = t.position - from;
            if (away.magnitude < range) return false;
            var mover = Motion.Of(t);
            return mover != null && Vector3.Dot(mover.Velocity, away.normalized) > speed;
        }

        /// <summary>The middle of its ships alive (`skinks`: the Skinks counted too).</summary>
        public Vector3 Centre(bool skinks)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var v in ships)
            {
                if (!AliveShip(v) || (!skinks && v.role == VraskShip.Role.Skink)) continue;
                sum += v.transform.position;
                n++;
            }
            if (n > 0) return sum / n;
            return skinks ? transform.position : Centre(true);
        }

        // ------------------------------------------------------------------ the priority target

        static bool IsPlayers(Transform t) => t == PlayerShips.Primary || t.GetComponent<Demo.RemotePlayer>() != null;

        bool Candidate(Transform t, Vector3 centre)
        {
            if (t == null || !t.gameObject.activeInHierarchy || !PlayerShips.Present(t) || Gone(t)) return false;
            return (t.position - centre).sqrMagnitude <= engageRange * engageRange;
        }

        /// <summary>A player's ship first (the nearest); else the best of the others by Score.
        /// It keeps the one it has while it lasts, unless a player's ship turns up.</summary>
        Transform Choose()
        {
            Vector3 c = Centre(true);
            Transform player = null, other = null;
            float playerD = float.MaxValue, otherS = -1f;
            foreach (var s in PlayerShips.All)
            {
                if (!Candidate(s, c)) continue;
                float d = Vector3.Distance(s.position, c);
                if (IsPlayers(s)) { if (d < playerD) { playerD = d; player = s; } continue; }
                var h = s.GetComponent<Health>();
                var sh = s.GetComponentInChildren<Shield>(true);
                var sys = s.GetComponent<ShipSystems>();
                float value = (h != null ? h.maxHealth : 1000f) + (sh != null ? sh.capacity : 0f);
                float score = Score(value, ShieldShare(s), sys != null ? sys.Condition(ShipSystem.Engines) : 1f, d);
                if (score > otherS) { otherS = score; other = s; }
            }
            if (Candidate(Target, c) && (IsPlayers(Target) || player == null)) return Target;
            return player != null ? player : other;
        }

        // ------------------------------------------------------------------ orders

        /// <summary>Its ship's own tactics have a ship of their own to work (the pack's, the
        /// Fang's run): the fleet doesn't change its pilot's target under it.</summary>
        static bool OwnTarget(VraskShip v)
        {
            var st = v.GetComponent<SnareTactics>();
            if (st != null && st.isActiveAndEnabled && st.Target != null) return true;
            var ft = v.GetComponent<FangTactics>();
            return ft != null && ft.isActiveAndEnabled && ft.Target != null;
        }

        bool Holds(VraskShip v)
        {
            switch (Current)
            {
                case Phase.Picket:
                case Phase.Bombard: return v.role == VraskShip.Role.Snare || v.role == VraskShip.Role.Fang;
                case Phase.Disable: return v.role == VraskShip.Role.Fang;
                default: return false;
            }
        }

        void Apply()
        {
            // (withdrawn on station: its escorts kept quiet, whatever the grid says)
            if (Current == Phase.Withdraw && grid != null) Quiet();
            bool assign = Target != null && Current != Phase.Idle && Current != Phase.Withdraw && Current != Phase.Pursue;
            foreach (var v in ships)
            {
                if (v == null) continue;
                var pl = v.GetComponent<CombatPilot>();
                if (pl == null) continue;
                bool fighting = AliveShip(v) && pl.enabled && pl.Current == CombatPilot.State.Engage;
                SetHold(pl, fighting && Holds(v));
                if (v.role == VraskShip.Role.Skink) continue;   // (its own tactics pick: SkinkTactics)
                if (fighting && assign && !OwnTarget(v))
                {
                    kept.Add(pl);
                    pl.keepTarget = true;
                    if (pl.target != Target)
                    {
                        pl.target = Target;
                        var w = v.GetComponent<EnemyWeapons>();
                        if (w != null && v.role == VraskShip.Role.Maw) w.SetTarget(Target);
                    }
                }
                else if (!assign && kept.Remove(pl)) pl.keepTarget = false;
            }
        }

        void SetHold(CombatPilot pl, bool on)
        {
            if (!holds.TryGetValue(pl, out var h)) holds[pl] = h = new Hold { fleet = this, pilot = pl };
            if (on) { if (pl.orders != h) pl.orders = h; }
            else if (ReferenceEquals(pl.orders, h)) pl.orders = null;
        }

        void ClearOrders()
        {
            foreach (var kv in holds) if (kv.Key != null && ReferenceEquals(kv.Key.orders, kv.Value)) kv.Key.orders = null;
            foreach (var pl in kept) if (pl != null) pl.keepTarget = false;
            kept.Clear();
        }

        /// <summary>A hold order: no nearer the target than the hold line (it stops there, bow on,
        /// or backs out to it). Not when the target has come in close: then it fights.</summary>
        void HoldLine(CombatPilot pl, Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            var against = pl.target != null ? pl.target : Target;
            if (against == null || !isActiveAndEnabled) return;
            Vector3 to = against.position - pos;
            float d = to.magnitude;
            if (d > holdLine + 300f || d < holdLine * 0.6f) return;
            if (d >= holdLine - 300f)
            {
                if (Vector3.Dot(desired, to) <= 0f) return;    // (on its way out already)
                desired = to;
                wantSpeed = 0f;
                return;
            }
            desired = -to;
            wantSpeed = pl.cruiseSpeed * 0.5f;
        }

        /// <summary>Where a Skink on picket goes: ahead of the rest toward the target, each to
        /// its own side (false: not on picket now).</summary>
        public bool PicketPoint(SkinkTactics s, out Vector3 at)
        {
            at = Vector3.zero;
            if (Current != Phase.Picket || Target == null || s == null) return false;
            Vector3 c = Centre(false);
            Vector3 to = Target.position - c;
            float d = to.magnitude;
            if (d < 1f) return false;
            Vector3 dir = to / d;
            int i = SkinkIndex(s.GetComponent<VraskShip>());
            Vector3 lateral = Vector3.Cross(dir, Target.up);
            if (lateral.sqrMagnitude < 0.01f) lateral = Vector3.Cross(dir, Vector3.right);
            at = c + dir * Mathf.Clamp(d - picketStandoff, 0f, picketLead) + lateral.normalized * (i % 2 == 0 ? 1f : -1f) * 700f * (1 + i / 2);
            return true;
        }

        /// <summary>Its Skinks in a fixed order (each takes its own side, its own ward).</summary>
        public int SkinkIndex(VraskShip v)
        {
            int i = 0;
            foreach (var s in ships)
            {
                if (s == null || s.role != VraskShip.Role.Skink) continue;
                if (s == v) return i;
                i++;
            }
            return 0;
        }

        // ------------------------------------------------------------------ withdrawing

        void StartWithdraw()
        {
            ClearOrders();
            if (group != null)
            {
                group.holdOff = true;
                // (one shot at later comes out to fight back: CombatPilot)
                foreach (var v in ships)
                {
                    var pl = v != null ? v.GetComponent<CombatPilot>() : null;
                    if (pl != null && AliveShip(v)) pl.Disengage();
                }
                if (group.isActiveAndEnabled) Notices.Post($"{group.groupName} withdrawing");
            }
            // the siege's escorts can't leave their station: they go quiet till the Maw is back
            if (grid != null) Quiet();
        }

        void Quiet()
        {
            foreach (var v in ships)
            {
                if (v == null || v.role == VraskShip.Role.Maw) continue;
                var w = v.GetComponent<EnemyWeapons>();
                if (w == null || !w.weaponsFree) continue;
                w.weaponsFree = false;
                if (!quieted.Contains(w)) quieted.Add(w);
            }
        }

        void LeaveWithdraw()
        {
            if (group != null) group.holdOff = false;
            foreach (var w in quieted) if (w != null) w.weaponsFree = grid != null && grid.Engaged;
            quieted.Clear();
        }
    }
}

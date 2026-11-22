using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// How a band of the Empire fights together, phase by phase (on its EncounterGroup), a
    /// disciplined battle line where the Greys ambush:
    /// - Patrol: none of them sees a player's ship;
    /// - Contact: the Recon paints one (the fleet's missiles fire on it from further out);
    /// - Line: the Assault frigates in their line, drones out, the Patrol frigates screening;
    /// - Raid: the Raiders on their runs at the light targets;
    /// - Board: a player's ship disabled, the marines' pods on their way;
    /// - Retreat: under half its ships left: it falls back in order, the Patrol frigates covering,
    ///   the band holding off (EncounterGroup.holdOff) till its ships are back.
    /// Each ship's own tactics do the fighting; this says what the band is doing, and when it
    /// falls back. Only the game running the enemies runs it (co-op: the host's).
    /// </summary>
    public class EmpireFleet : MonoBehaviour
    {
        public enum Phase { Patrol, Contact, Line, Raid, Board, Retreat }

        public EncounterGroup group;
        public EmpireShip[] ships = new EmpireShip[0];
        [Tooltip("It falls back when fewer than this share of its ships are left.")]
        public float retreatBelow = 0.5f;

        public Phase Current { get; private set; }
        public string Why { get; private set; } = "";
        public readonly List<Phase> History = new List<Phase>();
        bool retreating;
        readonly Dictionary<CombatPilot, float> retreatAt = new Dictionary<CombatPilot, float>();
        [Tooltip("The line falls back fighting this long, then breaks off for home.")]
        public float fightingRetreat = 20f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void HostOnly() => Demo.CoopWorld.HostOnlyBehaviour(typeof(EmpireFleet));

        int Alive()
        {
            int n = 0;
            foreach (var s in ships) if (s != null && s.Alive) n++;
            return n;
        }

        void Update()
        {
            if (group == null || ships.Length == 0) return;
            float share = (float)Alive() / ships.Length;
            // its ships back, every one in hiding at home: it can fight again
            if (retreating && share >= 0.999f && AllHome()) { SetRetreat(false); group.holdOff = false; }
            // the line's fighting retreat over: home, into hiding
            if (retreating && retreatAt.Count > 0)
                foreach (var kv in new List<KeyValuePair<CombatPilot, float>>(retreatAt))
                    if (kv.Key == null || Time.time - kv.Value >= fightingRetreat)
                    {
                        if (kv.Key != null && kv.Key.Current == CombatPilot.State.Engage) { kv.Key.target = null; kv.Key.Disengage(); }
                        retreatAt.Remove(kv.Key);
                    }
            if (!retreating) retreatAt.Clear();
            Phase next = Decide(share, out string why);
            if (next != Current)
            {
                if (next == Phase.Retreat) StartRetreat();
                Current = next;
                Why = why;
                History.Add(next);
            }
        }

        Phase Decide(float share, out string why)
        {
            if (retreating || (group.Active && share < retreatBelow)) { why = $"{Alive()} of {ships.Length} left: falling back in order"; return Phase.Retreat; }
            if (!group.Active) { why = "on patrol"; return Phase.Patrol; }
            foreach (var s in ships)
            {
                if (s == null || !s.Alive) continue;
                if (s.role == EmpireShip.Role.Patrol && s.TryGetComponent(out PatrolTactics pt) && pt.Target != null && SensorTactic.Disabled(pt.Target)) { why = $"{pt.Target.name} disabled: the marines"; return Phase.Board; }
            }
            foreach (var s in ships)
                if (s != null && s.Alive && s.role == EmpireShip.Role.Raider && s.TryGetComponent(out RaiderTactics rt) && rt.Prey != null) { why = $"the Raiders on {rt.Prey.name}"; return Phase.Raid; }
            foreach (var s in ships)
                if (s != null && s.Alive && s.role == EmpireShip.Role.Assault && s.TryGetComponent(out AssaultTactics at) && at.Target != null) { why = "the line of battle"; return Phase.Line; }
            foreach (var s in ships)
                if (s != null && s.Alive && s.role == EmpireShip.Role.Recon && s.TryGetComponent(out Designator d) && d.Painting != null) { why = $"the Recon painting {d.Painting.name}"; return Phase.Contact; }
            why = "on patrol";
            return Phase.Patrol;
        }

        /// <summary>Every living ship of the band back in hiding at its place.</summary>
        bool AllHome()
        {
            foreach (var s in ships)
            {
                if (s == null || !s.Alive || !s.TryGetComponent(out CombatPilot p)) continue;
                if (p.Current != CombatPilot.State.Lurk || !p.AtHome) return false;
            }
            return true;
        }

        void StartRetreat()
        {
            group.holdOff = true;
            SetRetreat(true);
            if (group.isActiveAndEnabled) Notices.Post($"{group.groupName} falling back in order");
        }

        void SetRetreat(bool on)
        {
            retreating = on;
            foreach (var s in ships)
            {
                if (s == null || !s.Alive) continue;
                if (s.TryGetComponent(out AssaultTactics a)) a.retreating = on;
                if (s.TryGetComponent(out PatrolTactics p)) p.retreating = on;
                // all of them home (CombatPilot flies them back to their places and into hiding);
                // the Raiders and the Recon don't hold a line: they break off at once, the line
                // falls back facing the target, the guns on it as it goes (AssaultTactics)
                if (on && s.TryGetComponent(out CombatPilot pl))
                {
                    if (s.role == EmpireShip.Role.Raider || s.role == EmpireShip.Role.Recon) pl.Disengage();
                    else retreatAt[pl] = Time.time;
                }
            }
        }
    }
}

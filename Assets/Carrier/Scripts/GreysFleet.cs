using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// How a band of the Greys fights together, phase by phase (on its EncounterGroup):
    /// - Watch: none of them sees a player's ship;
    /// - Paint: the Scout has one under its laser (it calls the others in: ScoutTactics);
    /// - Blind: the Shroud jams it;
    /// - Strike: the Lances are on their runs;
    /// - Take: the Reaper holds a ship and abducts from it (Abductor);
    /// - Vanish: the Reaper has its prize, or the Shroud and the Reaper are lost: they break off,
    ///   the Scout cloaked, the Reaper towing what it holds off with it for `towTime` seconds
    ///   before letting go, the group holding off (EncounterGroup.holdOff) till its ships are
    ///   back. Each ship's own tactics do the fighting; this says what the band is doing, and
    ///   when it leaves. Only the game running the enemies runs it (co-op: the host's).
    /// </summary>
    public class GreysFleet : MonoBehaviour
    {
        public enum Phase { Watch, Paint, Blind, Strike, Take, Vanish }

        public EncounterGroup group;
        public GreysShip[] ships = new GreysShip[0];
        public float towTime = 60f;

        public Phase Current { get; private set; }
        public string Why { get; private set; } = "";
        /// <summary>Phases it has been through, in order (the tests).</summary>
        public readonly List<Phase> History = new List<Phase>();
        float vanishAt = -1f;
        bool gone;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void HostOnly() => Demo.CoopWorld.HostOnlyBehaviour(typeof(GreysFleet));

        GreysShip Of(GreysShip.Role r)
        {
            foreach (var s in ships) if (s != null && s.role == r && s.Alive) return s;
            return null;
        }

        bool Lost(GreysShip.Role r)
        {
            bool had = false;
            foreach (var s in ships) if (s != null && s.role == r) { had = true; if (s.Alive) return false; }
            return had;
        }

        void Update()
        {
            if (group == null) return;
            var reaper = Of(GreysShip.Role.Reaper);
            var abductor = reaper != null ? reaper.GetComponent<Abductor>() : null;
            // the band back whole after withdrawing, every ship in hiding at home: it can fight again
            if (gone && vanishAt < 0f && !Lost(GreysShip.Role.Reaper) && !Lost(GreysShip.Role.Shroud) && AllHome())
            {
                gone = false;
                group.holdOff = false;
            }
            Phase next = Decide(reaper, abductor, out string why);
            if (next != Current)
            {
                if (next == Phase.Vanish) StartVanish(reaper);
                Current = next;
                Why = why;
                History.Add(next);
            }
            if (Current == Phase.Vanish && vanishAt >= 0f && Time.time - vanishAt > towTime)
            {
                // far enough: let go of what it towed
                vanishAt = -1f;
                if (reaper != null && reaper.TryGetComponent(out TractorBeam b) && b.Locked) b.Release("away with its prize");
                if (reaper != null && reaper.TryGetComponent(out ReaperTactics rt)) rt.towing = false;
                // (and home, like the rest: not round again for the ship it let go)
                if (reaper != null && reaper.TryGetComponent(out CombatPilot rp)) { rp.target = null; rp.Disengage(); }
            }
        }

        Phase Decide(GreysShip reaper, Abductor abductor, out string why)
        {
            if (gone || (abductor != null && abductor.Done)) { why = abductor != null && abductor.Done ? "the prize taken: vanishing" : "breaking off"; return Phase.Vanish; }
            if (Lost(GreysShip.Role.Reaper) && Lost(GreysShip.Role.Shroud)) { why = "the Shroud and the Reaper lost: vanishing"; return Phase.Vanish; }
            if (!group.Active) { why = "watching"; return Phase.Watch; }
            if (reaper != null && reaper.TryGetComponent(out TractorBeam beam) && beam.Locked) { why = $"the Reaper holds {beam.Holding.name}"; return Phase.Take; }
            foreach (var s in ships)
                if (s != null && s.Alive && s.role == GreysShip.Role.Lance && s.TryGetComponent(out LanceTactics lt) && lt.Current != LanceTactics.Phase.None)
                { why = "the Lances on their runs"; return Phase.Strike; }
            var shroud = Of(GreysShip.Role.Shroud);
            if (shroud != null && shroud.TryGetComponent(out EcmEmitter ecm) && ecm.On) { why = "the Shroud jamming"; return Phase.Blind; }
            var scout = Of(GreysShip.Role.Scout);
            if (scout != null && scout.TryGetComponent(out Designator d) && d.Painting != null) { why = $"the Scout painting {d.Painting.name}"; return Phase.Paint; }
            why = "watching";
            return Phase.Watch;
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

        /// <summary>The band as it was before any fight (the battle simulator's, between battles).</summary>
        public void ResetBand()
        {
            gone = false;
            vanishAt = -1f;
            Current = Phase.Watch;
            Why = "watching";
            foreach (var s in ships)
                if (s != null && s.TryGetComponent(out ReaperTactics rt)) rt.towing = false;
        }

        void StartVanish(GreysShip reaper)
        {
            gone = true;
            group.holdOff = true;
            vanishAt = Time.time;
            Vector3 home = group.transform.position;
            foreach (var s in ships)
            {
                if (s == null || !s.Alive) continue;
                if (s.TryGetComponent(out Cloak c)) c.Engage(true);
                // the Reaper tows its catch away; the rest go back into hiding
                if (s == reaper && s.TryGetComponent(out ReaperTactics rt) && s.TryGetComponent(out TractorBeam b) && b.Locked)
                {
                    rt.towing = true;
                    rt.towTo = home + (home - b.Holding.position).normalized * 20000f;
                    continue;
                }
                if (s.TryGetComponent(out CombatPilot pl)) pl.Disengage();
            }
            if (group.isActiveAndEnabled) Notices.Post($"{group.groupName} vanishing");
        }
    }
}

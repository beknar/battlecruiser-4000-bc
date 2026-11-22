using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// One of the Greys' ships: a mystery faction of saucers and a manta, quiet on the sensors
    /// (Signature), that watch, blind, strike and take (Data/greys.json, CarrierBuilder.Greys):
    /// - the Scout: deep reconnaissance, cloaked (Cloak), shadowing a ship at the edge of its
    ///   sensors, painting it for the rest (Designator), calling the others in, two hit-and-run
    ///   drones (DroneBay) (ScoutTactics);
    /// - the Shroud: electronic warfare, jamming and putting ghosts on the sensors (EcmEmitter),
    ///   an EMP burst and an EMP projector, escorting the Reaper (ShroudTactics);
    /// - the Lance: precision strike, fast runs from the flanks and the rear on engines and
    ///   weapons, running down cripples (LanceTactics);
    /// - the Reaper: boarding and capture by abduction, its tractor emitters holding a disabled
    ///   ship (ReaperTactics).
    /// Hostile like the Vrask (TargetInfo.faction "Hostile"); TargetInfo.group says "Greys".
    /// </summary>
    public class GreysShip : FactionShip
    {
        public const string Faction = "Greys";

        public enum Role { Scout, Shroud, Lance, Reaper }

        public Role role;

        public override string FactionName => Faction;
        public override string RoleName => role.ToString();
        /// <summary>O (the observer), H (the Shroud), L, R.</summary>
        public override char RoleLetter => role == Role.Scout ? 'O' : role == Role.Shroud ? 'H' : RoleName[0];
        /// <summary>No armour plating: their hulls are something else.</summary>
        public override int PlateCount => 0;

        public override float SectionWeight(float distance)
        {
            switch (role)
            {
                case Role.Reaper: return 3f;          // the one that takes ships
                case Role.Shroud: return 2.8f;        // the one that blinds them
                case Role.Lance: return distance < 3000f ? 2.6f : 1.8f;
                default: return 1.2f;
            }
        }

        /// <summary>Every Greys ship in the scene (kept while a co-op puppet, too).</summary>
        public static readonly List<GreysShip> All = new List<GreysShip>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAll() => All.Clear();

        protected override void Awake()
        {
            base.Awake();
            if (!All.Contains(this)) All.Add(this);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            All.Remove(this);
        }

        /// <summary>The nearest living Greys ship of a role to a point, or null.</summary>
        public static GreysShip Nearest(Role r, Vector3 from, GreysShip not = null)
        {
            GreysShip best = null;
            float bd = float.MaxValue;
            foreach (var g in All)
            {
                if (g == null || g == not || g.role != r || (g.health != null && g.health.Dead)) continue;
                float d = (g.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = g; }
            }
            return best;
        }

        /// <summary>Alive (not destroyed).</summary>
        public bool Alive => health == null || !health.Dead;
    }
}

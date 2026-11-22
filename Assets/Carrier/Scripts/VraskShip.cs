using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// One of the Vrask's ships: which of their four it is, and the places on its hull that the
    /// rest of their way of fighting works from (from the model's hardpoints, Data/lizard_frigateN_
    /// hardpoints.json): the Snare's tractor projector and ion tubes, the Fang's breaching prow
    /// and boarding-pod hatches, the Maw's spinal gun, silos and missile cells, the Skink's
    /// point-defence guns. Its shield goes down with it when it is destroyed and comes back when
    /// it does.
    ///
    /// The faction's common parts (its points, its sensors, its shield and plates) are FactionShip's.
    ///
    /// The Vrask are hostile like the pirates (TargetInfo.faction "Hostile": the co-op world, the
    /// CIWS and the wingmen treat them as enemies); TargetInfo.group says "Vrask".
    /// </summary>
    public class VraskShip : FactionShip
    {
        /// <summary>What TargetInfo.group says for them.</summary>
        public const string Faction = "Vrask";

        /// <summary>Snare: capture and disable (ion guns, a tractor). Fang: assault and boarding
        /// (heavy short-range guns, missiles, a ram). Maw: long-range fire support and siege (a
        /// spinal gun; nuclear silos). Skink: interceptor and escort (rapid point-defence guns).</summary>
        public enum Role { Snare, Fang, Maw, Skink }

        public Role role;

        public override string FactionName => Faction;
        public override string RoleName => role.ToString();
        /// <summary>M, S, F, K (the Skink's K: the Snare has the S).</summary>
        public override char RoleLetter => role == Role.Skink ? 'K' : RoleName[0];
        public override int PlateCount => PlatesFor(role);

        /// <summary>Every Vrask ship in the scene (kept while a co-op puppet, too).</summary>
        public static readonly List<VraskShip> All = new List<VraskShip>();
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

        /// <summary>How many ablative plates its hull wears, by its kind.</summary>
        public static int PlatesFor(Role r) => r == Role.Maw ? 16 : r == Role.Snare ? 12 : r == Role.Fang ? 10 : 8;
    }
}

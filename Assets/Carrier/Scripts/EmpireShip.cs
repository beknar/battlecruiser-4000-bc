using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// One of the Empire's ships: an aggressive military power of long grey warships that fights
    /// as a disciplined line (Data/empire.json, CarrierBuilder.Empire):
    /// - the Assault frigate: the line, ablative armour against energy and kinetics alike, turrets,
    ///   a missile battery, CIWS and a bay of combat drones (AssaultTactics);
    /// - the Patrol frigate: escort and screen, its CIWS over the Assault frigates, fast patrols, a
    ///   sensor platform, a warp inhibitor, marines in boarding pods, and it restocks the Assault
    ///   frigates' drones (PatrolTactics);
    /// - the Raider: fast attack on the lightest targets (drones, fighters, the wingman, then the
    ///   lightest ship), its guns doubling as point defence against missiles (RaiderTactics);
    /// - the Recon frigate: scouting, the longest sensors, a targeting laser the fleet's missiles
    ///   fire on from further out, and a warp inhibitor (ReconTactics).
    /// Hostile (TargetInfo.faction "Hostile"); TargetInfo.group says "Empire".
    /// </summary>
    public class EmpireShip : FactionShip
    {
        public const string Faction = "Empire";

        public enum Role { Assault, Patrol, Raider, Recon }

        public Role role;

        public override string FactionName => Faction;
        public override string RoleName => role.ToString();
        /// <summary>A, P, R (the Raider), C (the reCon).</summary>
        public override char RoleLetter => role == Role.Recon ? 'C' : RoleName[0];
        /// <summary>Ablative plates on the armoured ones (the Assault frigate's the most).</summary>
        public override int PlateCount => role == Role.Assault ? 16 : role == Role.Patrol ? 10 : 6;

        public override float SectionWeight(float distance)
        {
            switch (role)
            {
                case Role.Recon: return 2.6f;            // the eyes: its laser brings the missiles
                case Role.Raider: return distance < 3000f ? 2.4f : 1.4f;
                case Role.Patrol: return 2f;
                default: return 1.6f;
            }
        }

        public static readonly List<EmpireShip> All = new List<EmpireShip>();
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

        public bool Alive => health == null || !health.Dead;

        /// <summary>The nearest living Imperial ship of a role to a point, or null.</summary>
        public static EmpireShip Nearest(Role r, Vector3 from, float within = float.MaxValue)
        {
            EmpireShip best = null;
            float bd = within * within;
            foreach (var e in All)
            {
                if (e == null || e.role != r || !e.Alive) continue;
                float d = (e.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }
    }
}

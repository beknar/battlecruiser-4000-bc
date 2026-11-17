using System.Collections.Generic;
using System.Linq;
using CarrierGame;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The Skink's escort work and the Vrask's fleet coordination (Data/vrask.json: the Skink's
    /// sensorRange, cap, interceptRange and pdMissileScale; the "fleet" block): its point defence
    /// (PointDefence, on the guns at its "pd" points) and its tactics (SkinkTactics) on the
    /// prefab; a VraskFleet on the raiding group and on the siege group.
    /// </summary>
    public static partial class CarrierBuilder
    {
        /// <summary>The Skink's point defence and escort tactics: its "pd" guns as the mounts.</summary>
        static void AddEscort(GameObject root, VraskShip vs, VraskShipStats s, List<CarrierTurret> turrets)
        {
            var set = (s.guns ?? new VraskWeapon[0]).FirstOrDefault(w => w.on == "pd");
            var mounts = set == null ? new CarrierTurret[0]
                : turrets.Where(t => t != null && (t.label == set.label || t.label.StartsWith(set.label + ":"))).ToArray();
            var pd = root.AddComponent<PointDefence>();
            pd.mounts = mounts;
            if (s.pdMissileScale > 0f) pd.missileScale = s.pdMissileScale;
            var st = root.AddComponent<SkinkTactics>();
            if (s.interceptRange > 0f) st.interceptRange = s.interceptRange;
            if (s.cap != null && s.cap.Length >= 2) { st.capNear = s.cap[0]; st.capFar = s.cap[1]; }
            Log($"{s.displayName}: point defence on {mounts.Length} guns (x{pd.missileScale:0.#} on missiles), sensors {vs.sensorRange / 1000f:0.#} km, " +
                $"patrol {st.capNear / 1000f:0.#} to {st.capFar / 1000f:0.#} km round its ward, intercepts within {st.interceptRange / 1000f:0.#} km; Skink tactics");
        }

        /// <summary>A Vrask group's fleet coordination (VraskFleet), with the numbers from the data.</summary>
        static VraskFleet AddFleet(GameObject on, IEnumerable<VraskShip> ships, EncounterGroup group, DefenseGrid grid)
        {
            var f = on.AddComponent<VraskFleet>();
            f.ships = ships.Where(v => v != null).ToArray();
            f.group = group;
            f.grid = grid;
            var d = vrask?.fleet;
            if (d != null)
            {
                if (d.holdLine > 0f) f.holdLine = d.holdLine;
                if (d.bombardTime > 0f) f.bombardTime = d.bombardTime;
                if (d.bombardShield > 0f) f.bombardShield = d.bombardShield;
                if (d.reactRange > 0f) f.reactRange = d.reactRange;
                if (d.recoverTime > 0f) f.recoverTime = d.recoverTime;
                if (d.pursueTime > 0f) f.pursueTime = d.pursueTime;
                if (d.picketLead > 0f) f.picketLead = d.picketLead;
                if (d.picketStandoff > 0f) f.picketStandoff = d.picketStandoff;
                if (d.sensorRange > 0f) f.sensorRange = d.sensorRange;
                if (d.engageRange > 0f) f.engageRange = d.engageRange;
            }
            Log($"{on.name}: Vrask fleet of {f.ships.Length} ({string.Join(", ", f.ships.Select(v => v.role).GroupBy(r => r).Select(g => $"{g.Count()} {g.Key}"))}), " +
                $"{(group != null ? "piloted" : "on station")}; hold line {f.holdLine / 1000f:0.#} km, bombard up to {f.bombardTime:0} s, picket {f.picketLead / 1000f:0.#} km ahead");
            return f;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// A Vrask band made over to another mix of its four kinds of ship (the battle simulator's
    /// -simmix, the CIC reel's -cicmix): "2,3,2,3" is two Maws, three Snares, two Fangs and three
    /// Skinks. Ships over the count go (the last named first); one short is a copy of one of its
    /// kind already in the band (none of a kind left in it: that kind can't be added), put in the
    /// band's formation near the others and named on from them ("Snare Four"). The band's pilots
    /// and its fleet's ships are made over to match. Before the band is in a fight: in the first
    /// frames, or between the simulator's battles. For a band that keeps its post (the Sheol
    /// picket): one that patrols or moves on (EncounterPatrol, PostedOnApproach) keeps its ships'
    /// places by their order, which this changes.
    ///
    /// Any faction's band too: its counts in its roles' order (the Greys: Scout, Shroud, Lance,
    /// Reaper; the Empire: Assault, Patrol, Raider, Recon), "1,2,1,1".
    /// </summary>
    public static class VraskMix
    {
        static readonly VraskShip.Role[] Order = { VraskShip.Role.Maw, VraskShip.Role.Snare, VraskShip.Role.Fang, VraskShip.Role.Skink };
        static readonly string[] Words = { "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten" };

        /// <summary>"2,3,2,3" as counts in a faction's roles' order (the Vrask's: Maw, Snare, Fang,
        /// Skink): null if it isn't one.</summary>
        public static int[] Parse(string mix)
        {
            if (string.IsNullOrEmpty(mix)) return null;
            var parts = mix.Split(',', '+', '/');
            if (parts.Length < 1 || parts.Length > 6) return null;
            var n = new int[parts.Length];
            for (int i = 0; i < n.Length; i++) if (!int.TryParse(parts[i].Trim(), out n[i]) || n[i] < 0 || n[i] > 10) return null;
            return n.Sum() > 0 ? n : null;
        }

        /// <summary>A faction's roles, in a mix's order (the band's first ship's faction).</summary>
        public static string[] RolesOf(FactionShip ship)
        {
            if (ship is VraskShip) return Order.Select(r => r.ToString()).ToArray();
            if (ship is GreysShip) return Enum.GetNames(typeof(GreysShip.Role));
            if (ship is EmpireShip) return Enum.GetNames(typeof(EmpireShip.Role));
            return new string[0];
        }

        /// <summary>The counts in words ("2 Maws, 3 Snares, 2 Fangs, 3 Skinks").</summary>
        public static string Describe(IEnumerable<FactionShip> ships)
        {
            // (the Vrask's roles in their order, then any other faction's as they come)
            var list = ships.Where(v => v != null).ToList();
            var roles = RolesOf(list.FirstOrDefault()).Concat(list.Select(v => v.RoleName)).Distinct();
            return string.Join(", ", roles.Select(r => (r, n: list.Count(v => v.RoleName == r))).Where(x => x.n > 0)
                                          .Select(x => $"{x.n} {x.r}{(x.n > 1 ? "s" : "")}"));
        }

        /// <summary>Make the band `g` over to `counts` (its faction's roles: the Vrask's Maw, Snare,
        /// Fang, Skink). What it did, for a log.</summary>
        public static string Apply(EncounterGroup g, int[] counts)
        {
            if (g == null || g.pilots == null || counts == null) return "no band or no mix";
            var pilots = g.pilots.Where(p => p != null).ToList();
            var roleNames = RolesOf(pilots.Select(p => p.GetComponent<FactionShip>()).FirstOrDefault(f => f != null));
            if (roleNames.Length == 0) return "not a faction's band";
            if (counts.Length != roleNames.Length) return $"a mix of {roleNames.Length} ({string.Join(", ", roleNames)}), not {counts.Length}";
            Vector3 centre = pilots.Count > 0 ? pilots.Aggregate(Vector3.zero, (a, p) => a + p.transform.position) / pilots.Count : g.transform.position;
            var said = new List<string>();
            for (int r = 0; r < roleNames.Length; r++)
            {
                var role = roleNames[r];
                var mine = pilots.Where(p => p.TryGetComponent(out FactionShip v) && v.RoleName == role).OrderBy(p => Ordinal(p.name)).ToList();
                // too many: the last named go
                for (int k = mine.Count - 1; k >= counts[r]; k--)
                {
                    pilots.Remove(mine[k]);
                    said.Add($"-{mine[k].name}");
                    mine[k].gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(mine[k].gameObject);
                    mine.RemoveAt(k);
                }
                if (mine.Count == 0 || mine.Count >= counts[r]) continue;
                // too few: copies of the first, round the band's middle where its kind keeps
                var template = mine[0];
                string baseName = BaseName(template.name);
                for (int k = mine.Count; k < counts[r]; k++)
                {
                    Vector3 off = template.transform.position - centre;
                    // (one at the middle: the copies round it, clear of each other)
                    off = Vector3.ProjectOnPlane(off, Vector3.up).sqrMagnitude < 800f * 800f
                        ? (Vector3.ProjectOnPlane(off, Vector3.up).sqrMagnitude > 1f ? Vector3.ProjectOnPlane(off, Vector3.up).normalized : Vector3.forward) * 800f + Vector3.up * off.y
                        : off;
                    Vector3 at = centre + Quaternion.AngleAxis(37f * (k + 1), Vector3.up) * off + Vector3.up * (k % 2 == 0 ? 150f : -150f);
                    var go = UnityEngine.Object.Instantiate(template.gameObject, at, template.transform.rotation, template.transform.parent);
                    string name = $"{baseName} {(k < Words.Length ? Words[k] : (k + 1).ToString())}";
                    go.name = name;
                    if (go.TryGetComponent(out TargetInfo ti)) ti.displayName = name;
                    var p = go.GetComponent<CombatPilot>();
                    pilots.Add(p);
                    mine.Add(p);
                    said.Add($"+{name}");
                }
            }
            g.pilots = pilots.ToArray();
            T[] Ships<T>() where T : FactionShip => pilots.Select(p => p.GetComponent<T>()).Where(v => v != null).ToArray();
            void Remake(Behaviour fleet, Action set)
            {
                if (fleet == null) return;
                bool was = fleet.enabled;
                fleet.enabled = false;
                set();
                fleet.enabled = was;
            }
            var vf = g.GetComponent<VraskFleet>();
            Remake(vf, () => vf.ships = Ships<VraskShip>());
            var gf = g.GetComponent<GreysFleet>();
            Remake(gf, () => gf.ships = Ships<GreysShip>());
            var ef = g.GetComponent<EmpireFleet>();
            Remake(ef, () => ef.ships = Ships<EmpireShip>());
            return $"{Describe(pilots.Select(p => p.GetComponent<FactionShip>()))} ({(said.Count > 0 ? string.Join(" ", said) : "as it was")})";
        }

        static string BaseName(string name)
        {
            int i = name.LastIndexOf(' ');
            return i > 0 && Array.IndexOf(Words, name.Substring(i + 1)) >= 0 ? name.Substring(0, i) : name;
        }

        static int Ordinal(string name)
        {
            int i = name.LastIndexOf(' ');
            int k = i > 0 ? Array.IndexOf(Words, name.Substring(i + 1)) : -1;
            return k < 0 ? 99 : k;
        }
    }
}

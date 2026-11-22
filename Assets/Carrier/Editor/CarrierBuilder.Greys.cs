using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrierGame;
using UnityEditor;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The Greys, a mystery faction (Data/greys.json): the Scout (reconnaissance: cloak, targeting
    /// laser, drones), the Shroud (electronic warfare: jammer, ghosts, EMP), the Lance (precision
    /// strike) and the Reaper (abduction: tractor emitters), from the AI-made models
    /// Models/Greys_&lt;Id&gt;.fbx (Tools/Blender/build_greys.sh). Each is built as a faction's
    /// ship is (CarrierBuilder.Faction), with what its numbers give it (AddFactionGear) and its
    /// role's tactics (ScoutTactics, ShroudTactics, LanceTactics, ReaperTactics).
    ///
    /// Their bands: in the Testing System a Greys cell in the Outer Drift (the file's group) and
    /// a lone watcher at the Jovara Trojans; in the Nine Hells a cell picking over the dead
    /// race's ruins on Goliath and a watcher at Ptolomea (CarrierBuilder.NineHellsEncounters).
    /// </summary>
    public static partial class CarrierBuilder
    {
        public const string GreysDataPath = "Assets/Carrier/Data/greys.json";
        static FactionTable greys;

        static FactionTable LoadGreys() => greys = LoadFaction<FactionTable>(GreysDataPath);

        static IEnumerable<string> GreysModelPaths() => FactionModelPaths(LoadGreys());

        static void MakeGreys(Prefabs p)
        {
            if (LoadGreys() == null) { Log("greys: no " + GreysDataPath); return; }
            var bolts = MakeFactionKit(p, greys);
            foreach (var s in greys.ships) p.greys[s.id] = MakeGreysShip(p, s, bolts);
        }

        static GameObject MakeGreysShip(Prefabs p, FactionShipStats s, Dictionary<string, Projectile> bolts) =>
            MakeFactionShip(p, greys, s, bolts,
                root =>
                {
                    var gs = root.AddComponent<GreysShip>();
                    gs.role = (GreysShip.Role)Enum.Parse(typeof(GreysShip.Role), s.id);
                    return gs;
                },
                (root, ship, turrets) =>
                {
                    var gs = (GreysShip)ship;
                    AddFactionGear(root, ship, greys, s);
                    if (s.tractorRange > 0f) AddTractorBeam(root, ship, greys, s.tractorRange);
                    switch (gs.role)
                    {
                        case GreysShip.Role.Scout: root.AddComponent<ScoutTactics>(); break;
                        case GreysShip.Role.Shroud: root.AddComponent<ShroudTactics>(); break;
                        case GreysShip.Role.Lance: root.AddComponent<LanceTactics>(); break;
                        case GreysShip.Role.Reaper: root.AddComponent<ReaperTactics>(); break;
                    }
                });

        /// <summary>The Greys' bands in the Testing System: the cell in the Outer Drift (the
        /// file's group) and a lone watcher at the Jovara Trojans.</summary>
        static void GreysBands(Prefabs p, Transform sys,
            Func<GameObject, string, string, Vector3, Vector3, Action<CombatPilot>, float, GameObject> enemy)
        {
            if (greys?.group?.slots == null || p.greys.Count == 0) { Log("greys: no bands"); return; }
            GameObject Make(GameObject prefab, string name, Vector3 pos, Vector3 face, Action<CombatPilot> setup, float respawn)
            {
                var go = enemy(prefab, name, name, pos, face, setup, 180f);
                go.GetComponent<Health>().respawnDelay = respawn;
                return go;
            }
            var points = sys.GetComponentsInChildren<WarpPoint>(true);
            WarpPoint At(string n) => points.FirstOrDefault(w => w.displayName == n);

            var g = greys.group;
            var cellAt = At(g.at);
            if (cellAt != null)
            {
                FactionBand(greys, p.greys, sys, null, g.name, cellAt.transform.position, g.spotRange,
                            g.slots.Select(sl => (sl.ship, sl.name, sl.angle, sl.distance, sl.height)), Make);
                Warn(cellAt, g.note, "Harder", g.name);
            }
            else Log($"greys: no place '{g.at}'");
            var watchAt = At("Jovara Trojans");
            if (watchAt != null)
            {
                FactionBand(greys, p.greys, sys, null, "Greys watcher", watchAt.transform.position, 12000f,
                            new[] { ("Scout", "Trojans", 90f, 6000f, 600f) }, Make);
                Warn(watchAt, "", "Easy", "Greys watcher");
            }
        }

        /// <summary>A place a band lies in wait by: warned of on the map ("danger near"), not made
        /// hostile; its description says what lies there.</summary>
        static void Warn(WarpPoint at, string note, string tier, string band)
        {
            at.dangerNear = true;
            string add = string.IsNullOrEmpty(note) ? $"({tier}: {band}, the Greys, lie in wait here.)" : $"{note} ({tier}: {band}, the Greys.)";
            if (at.description == null || !at.description.Contains(band)) at.description = (at.description + " " + add).Trim();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using CarrierGame;
using UnityEditor;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The Empire, an aggressive military power (Data/empire.json): the Assault frigate (the line:
    /// ablative armour, turrets, missiles, CIWS, drones), the Patrol frigate (escort: CIWS, marines,
    /// a warp inhibitor, the drones' logistics), the Raider (fast attack, its guns point defence too)
    /// and the Recon frigate (scout: a targeting laser, the longest sensors, a warp inhibitor), from
    /// the AI-made models Models/Empire_&lt;Id&gt;.fbx (Tools/Blender/build_empire.sh). Each is built
    /// as a faction's ship is (CarrierBuilder.Faction), with what its numbers give it
    /// (AddFactionGear), its CIWS or point defence (PointDefence), its marines' pods (PodLauncher)
    /// and its role's tactics.
    ///
    /// Their bands: in the Testing System a task force holding Brontes (the file's group) and a
    /// patrol at Arges; in the Nine Hells a forward base's picket at Emim and a battle group on
    /// patrol round Rephaim (CarrierBuilder.NineHellsEncounters).
    /// </summary>
    public static partial class CarrierBuilder
    {
        public const string EmpireDataPath = "Assets/Carrier/Data/empire.json";
        static FactionTable empire;
        static BoardingPod empirePod;

        static FactionTable LoadEmpire() => empire = LoadFaction<FactionTable>(EmpireDataPath);

        static IEnumerable<string> EmpireModelPaths() => FactionModelPaths(LoadEmpire());

        static void MakeEmpire(Prefabs p)
        {
            if (LoadEmpire() == null) { Log("empire: no " + EmpireDataPath); return; }
            var bolts = MakeFactionKit(p, empire);
            // their marines' pod: the missile's model, a red-gold trail
            empirePod = MakeBoardingPod(p, empire.Prefix, empire.TrailGlow);
            foreach (var s in empire.ships) p.empire[s.id] = MakeEmpireShip(p, s, bolts);
        }

        static GameObject MakeEmpireShip(Prefabs p, FactionShipStats s, Dictionary<string, Projectile> bolts) =>
            MakeFactionShip(p, empire, s, bolts,
                root =>
                {
                    var es = root.AddComponent<EmpireShip>();
                    es.role = (EmpireShip.Role)Enum.Parse(typeof(EmpireShip.Role), s.id);
                    return es;
                },
                (root, ship, turrets) =>
                {
                    var es = (EmpireShip)ship;
                    AddFactionGear(root, ship, empire, s);
                    AddFactionPointDefence(root, s, turrets);
                    if (s.pods > 0) AddFactionPods(root, ship, s, empirePod);
                    switch (es.role)
                    {
                        case EmpireShip.Role.Assault: root.AddComponent<AssaultTactics>(); break;
                        case EmpireShip.Role.Patrol: root.AddComponent<PatrolTactics>(); break;
                        case EmpireShip.Role.Raider: root.AddComponent<RaiderTactics>(); break;
                        case EmpireShip.Role.Recon: root.AddComponent<ReconTactics>(); break;
                    }
                });

        /// <summary>A ship's point defence (PointDefence): its CIWS (the guns at its ciws points),
        /// or, for one with a missile scale and no CIWS (the Raider), its fixed guns.</summary>
        static void AddFactionPointDefence(GameObject root, FactionShipStats s, List<CarrierTurret> turrets)
        {
            var ciws = (s.guns ?? new FactionWeapon[0]).FirstOrDefault(w => w.on == "ciws");
            var pdSet = ciws ?? (s.pdMissileScale > 0f ? (s.guns ?? new FactionWeapon[0]).FirstOrDefault(w => w.on == "guns") : null);
            if (pdSet == null) return;
            var mounts = turrets.Where(t => t != null && (t.label == pdSet.label || t.label.StartsWith(pdSet.label + ":"))).ToArray();
            if (mounts.Length == 0) return;
            var pd = root.AddComponent<PointDefence>();
            pd.mounts = mounts;
            if (s.pdMissileScale > 0f) pd.missileScale = s.pdMissileScale;
            Log($"{s.displayName}: point defence on {mounts.Length} {pdSet.label} (x{pd.missileScale:0.#} on missiles)");
        }

        /// <summary>A ship's marines: boarding pods at its boarding_pod points (PodLauncher), its
        /// tactics launching them at a disabled ship.</summary>
        static void AddFactionPods(GameObject root, FactionShip ship, FactionShipStats s, BoardingPod pod)
        {
            if (pod == null) { Log($"{s.displayName}: no boarding pod prefab"); return; }
            var hatches = ship.PointsOf("boarding_pod");
            if (hatches.Length == 0) hatches = new[] { root.transform };
            var pl = root.AddComponent<PodLauncher>();
            pl.podPrefab = pod;
            pl.hatches = hatches;
            pl.perWave = s.pods;
            pl.waveInterval = s.podInterval > 0f ? s.podInterval : 20f;
            pl.range = s.podRange > 0f ? s.podRange : 3000f;
            Log($"{s.displayName}: marines, {s.pods} pods a wave every {pl.waveInterval:0} s, {pl.range / 1000f:0.#} km, from {hatches.Length} hatch(es)");
        }

        /// <summary>How a band of the Empire fights together (EmpireFleet).</summary>
        static void AddEmpireFleet(EncounterGroup g, List<CombatPilot> pilots)
        {
            var f = g.gameObject.AddComponent<EmpireFleet>();
            f.group = g;
            f.ships = pilots.Where(p => p != null).Select(p => p.GetComponent<EmpireShip>()).Where(s => s != null).ToArray();
        }

        /// <summary>The Empire's bands in the Testing System: the task force holding Brontes (the
        /// file's group) and a patrol at Arges.</summary>
        static void EmpireBands(Prefabs p, Transform sys,
            Func<GameObject, string, string, Vector3, Vector3, Action<CombatPilot>, float, GameObject> enemy)
        {
            if (empire?.group?.slots == null || p.empire.Count == 0) { Log("empire: no bands"); return; }
            GameObject Make(GameObject prefab, string name, Vector3 pos, Vector3 face, Action<CombatPilot> setup, float respawn)
            {
                var go = enemy(prefab, name, name, pos, face, setup, 180f);
                go.GetComponent<Health>().respawnDelay = respawn;
                return go;
            }
            var points = sys.GetComponentsInChildren<WarpPoint>(true);
            WarpPoint At(string n) => points.FirstOrDefault(w => w.displayName == n);
            var g = empire.group;
            // (a body's band out by where ships drop out of warp, on the side away from the star: not
            // in the middle of the moon)
            Vector3 Off(WarpPoint at, float frac)
            {
                Vector3 c = at.transform.position;
                Vector3 away = Vector3.ProjectOnPlane(c - sys.position, Vector3.up);
                away = away.sqrMagnitude > 1f ? away.normalized : Vector3.forward;
                float r = at.kind == WarpPoint.Kind.AsteroidField ? at.bodyRadius * frac : at.bodyRadius + (at.arrivalDistance - at.bodyRadius) * frac;
                return c + away * r;
            }
            var forceAt = At(g.at);
            if (forceAt != null)
            {
                FactionBand(empire, p.empire, sys, null, g.name, Off(forceAt, 0.6f), g.spotRange,
                            g.slots.Select(sl => (sl.ship, sl.name, sl.angle, sl.distance, sl.height)), Make, AddEmpireFleet);
                WarnEmpire(forceAt, g.note, "Boss", g.name);
            }
            else Log($"empire: no place '{g.at}'");
            var patrolAt = At("Arges");
            if (patrolAt != null)
            {
                FactionBand(empire, p.empire, sys, null, "Imperial patrol", Off(patrolAt, 0.6f), 12000f,
                            new[] { ("Patrol", "Arges", 0f, 4000f, 0f), ("Raider", "Arges", 40f, 4500f, 200f), ("Recon", "Arges", -30f, 8000f, 500f) },
                            Make, AddEmpireFleet);
                WarnEmpire(patrolAt, "", "Harder", "Imperial patrol");
            }
        }

        /// <summary>A place an Imperial band holds or lies by: warned of on the map, not made hostile.</summary>
        static void WarnEmpire(WarpPoint at, string note, string tier, string band)
        {
            at.dangerNear = true;
            string add = string.IsNullOrEmpty(note) ? $"({tier}: {band}, of the Empire, is here.)" : $"{note} ({tier}: {band}, of the Empire.)";
            if (at.description == null || !at.description.Contains(band)) at.description = (at.description + " " + add).Trim();
        }
    }
}

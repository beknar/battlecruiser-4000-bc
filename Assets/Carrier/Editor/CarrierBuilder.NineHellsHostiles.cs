using System;
using System.Collections.Generic;
using System.Linq;
using CarrierGame;
using CarrierGame.Demo;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The Nine Hells' enemies in their strongholds:
    /// - Judecca, held by human pirates: two fortified cities, Dis and Malebolge (their ground and
    ///   sea forces are the hostile world's own: GroundForces), each with geostationary defence
    ///   platforms over it.
    /// - Gilgal: pirate bases on its moons Jericho and Bethel, as well defended as Judecca's, and
    ///   the picket fleet over its rings: a dreadnought, three cruisers, five frigates and ten
    ///   fighters.
    /// - Sheol: the Vrask outpost on its ice, its geostationary defence satellites, and a picket
    ///   of ten Vrask frigates of all four kinds.
    /// </summary>
    public static partial class CarrierBuilder
    {
        /// <summary>An enemy ship made in the Nine Hells (as the Testing System's Enemy is): it
        /// lies in wait, fights, and is put back by the SpawnDirector.</summary>
        static GameObject NhEnemy(NineHellsRefs R, GameObject prefab, string name, Vector3 pos, Vector3 face, Action<CombatPilot> setup, float fireArc, float respawn = 60f)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = name;
            // (a scene root, as the Testing System's enemies are: carried by its OrbitHome and told when the
            // origin moves; inside an orbit frame its lurking place would never be moved with it)
            go.GetComponent<TargetInfo>().displayName = name;
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(face - pos));
            var om = go.GetComponent<OrbitMover>();
            if (om) om.enabled = false;
            var w = go.GetComponent<EnemyWeapons>();
            if (w) { w.holdFire = 0f; w.fireArc = fireArc; }
            go.GetComponent<Health>().respawnDelay = respawn;
            var pilot = go.AddComponent<CombatPilot>();
            pilot.leashDistance = 30000f;
            pilot.relurkOnRespawn = true;
            setup(pilot);
            R.enemies.Add(go);
            return go;
        }

        static void NhCapital(CombatPilot pl, float cruise, float attack, float turn, float accel, float standoff, float circle)
        {
            pl.style = CombatPilot.Style.Capital;
            pl.concealed = false;
            pl.cruiseSpeed = cruise; pl.attackSpeed = attack; pl.turnRate = turn; pl.acceleration = accel;
            pl.standoff = standoff; pl.circleSpeed = circle;
            pl.hideBelow = 0f;
            pl.leashDistance = 36000f;
        }

        /// <summary>A band of pirates lying in wait round a point: each (prefab, name, setup,
        /// respawn) in a loose formation round it, facing `face`.</summary>
        static EncounterGroup NhPirates(NineHellsRefs R, string groupName, Vector3 centre, Vector3 face, float spotRange,
                                        (GameObject prefab, string name, Action<CombatPilot> setup, float respawn, float arc)[] ships)
        {
            var pilots = new List<CombatPilot>();
            var rng = new System.Random(groupName.GetHashCode());
            float Rn(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int i = 0; i < ships.Length; i++)
            {
                var sh = ships[i];
                if (sh.prefab == null) continue;
                // (the first ship in the middle, the rest in rings round it)
                float ring = i == 0 ? 0f : 1200f + (i / 6) * 900f;
                float a = i * 137.5f * Mathf.Deg2Rad;
                Vector3 pos = centre + new Vector3(Mathf.Cos(a) * ring, Rn(-250f, 250f), Mathf.Sin(a) * ring);
                var go = NhEnemy(R, sh.prefab, sh.name, pos, face, sh.setup, sh.arc, sh.respawn);
                pilots.Add(go.GetComponent<CombatPilot>());
            }
            var g = new GameObject(groupName).AddComponent<EncounterGroup>();
            g.transform.SetParent(R.sys, false);
            g.transform.position = centre;
            g.groupName = groupName;
            g.pilots = pilots.ToArray();
            g.spotRange = spotRange;
            Log($"nine hells: {groupName}: {pilots.Count} ships");
            return g;
        }

        /// <summary>A band of Vrask lying in wait round a point (as the raiders at Comet Mirren:
        /// VraskRaiders), the ships `slots` (ship id, name, angle, distance, height).</summary>
        static EncounterGroup NhVrask(NineHellsRefs R, string groupName, Vector3 centre, float spotRange, (string ship, string name, float angle, float dist, float height)[] slots)
        {
            var p = R.prefabs;
            if (vrask == null || p.vrask.Count == 0) { Log($"nine hells: {groupName}: no Vrask"); return null; }
            Vector3 outward = Vector3.ProjectOnPlane(centre - R.sys.position, Vector3.up);
            outward = outward.sqrMagnitude > 1f ? outward.normalized : Vector3.forward;
            var pilots = new List<(CombatPilot pilot, FactionShipStats s)>();
            foreach (var slot in slots)
            {
                var s = vrask.Find(slot.ship);
                if (s == null || !p.vrask.TryGetValue(s.id, out var prefab) || prefab == null) continue;
                Vector3 pos = centre + Quaternion.AngleAxis(slot.angle, Vector3.up) * outward * slot.dist + Vector3.up * slot.height;
                string name = $"{s.displayName} {slot.name}";
                var go = NhEnemy(R, prefab, name, pos, centre, pl => VraskPilot(pl, s), 180f, s.respawn);
                pilots.Add((go.GetComponent<CombatPilot>(), s));
            }
            int side = 0;
            foreach (var (pl, s) in pilots.Where(x => !string.IsNullOrEmpty(x.s.guard)))
            {
                var ward = pilots.FirstOrDefault(x => x.s.id == s.guard).pilot;
                if (ward == null) continue;
                pl.guard = ward.transform;
                pl.guardLeash = s.guardLeash;
                pl.circleSpeed = Mathf.Abs(pl.circleSpeed) * (side++ % 2 == 0 ? 1f : -1f);
            }
            var eg = new GameObject(groupName).AddComponent<EncounterGroup>();
            eg.transform.SetParent(R.sys, false);
            eg.transform.position = centre;
            eg.groupName = groupName;
            eg.pilots = pilots.Select(x => x.pilot).ToArray();
            eg.spotRange = spotRange;
            AddFleet(eg.gameObject, pilots.Select(x => x.pilot.GetComponent<VraskShip>()), eg, null);
            Log($"nine hells: {groupName}: {pilots.Count} Vrask ({string.Join(", ", pilots.Select(x => x.pilot.name))})");
            return eg;
        }

        /// <summary>Geostationary defence platforms over a stronghold: each turning with its body,
        /// a hostile destination that opens fire within 6.5 km (as the strike satellites are, but
        /// they only defend).</summary>
        static void NhDefenceSats(NineHellsRefs R, WarpPoint over, string label, int count, float altitude, Built b)
        {
            if (over == null || over.landsOn == null) return;
            // (the platform prefab the Testing System made: built once, its turret template gone after)
            var platform = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/OrbitalPlatform.prefab") ?? OrbitalPrefab(R.prefabs, false);
            var body = over.landsOn.transform;
            Vector3 radial = (over.Position - body.position).normalized;
            for (int i = 0; i < count; i++)
            {
                string name = $"{label} {(char)('A' + i)}";
                // (spread a little round the point over the base)
                Vector3 r = Quaternion.AngleAxis((i - (count - 1) * 0.5f) * 6f, Vector3.Cross(radial, Vector3.up).sqrMagnitude > 0.01f ? Vector3.Cross(radial, Vector3.up) : Vector3.right) * radial;
                Vector3 along = Vector3.Cross(r, Vector3.up);
                var f = BodyFrame(body, name, r, along.sqrMagnitude > 0.01f ? along : Vector3.right);
                Object.DestroyImmediate(f.GetComponent<RaiseToGround>());
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(platform);
                inst.name = name;
                inst.transform.SetParent(f, false);
                inst.transform.localPosition = new Vector3(0f, over.landsOn.bodyRadius + altitude, 0f);
                inst.transform.localRotation = Quaternion.identity;
                inst.GetComponent<TargetInfo>().displayName = name;
                inst.GetComponent<Health>().respawnDelay = 1200f;
                var grid = inst.AddComponent<DefenseGrid>();
                grid.gridName = name;
                grid.weapons = new[] { inst.GetComponent<EnemyWeapons>() };
                grid.engageRange = 6500f;
                grid.disengageRange = 10000f;
                Facility(inst, name, WarpPoint.Kind.Weapons, true);
                Destination(inst, name, WarpPoint.Kind.Weapons, $"A defence platform in geostationary orbit over {over.displayName}, {altitude / 1000f:F0} km up. " +
                            "It opens fire on anything within 6.5 km.", 300f, 7000f, HostileWorks, over.landsOn, true);
                b.targets.Add(inst.transform);
            }
            Log($"nine hells: {count} defence platforms over {over.displayName}");
        }

        static void NineHellsHostiles(NineHellsRefs R)
        {
            var p = R.prefabs;
            var b = new Built();
            var s = new SystemRefs { sys = R.sys, star = R.star, layer = R.layer, kit = R.kit, rockMat = R.rockMat, thalassa = R.bodies["K'rzyn"] };
            WarpPoint P(string n) => R.sys.GetComponentsInChildren<WarpPoint>(true).FirstOrDefault(w => w.displayName == n);
            WarpPoint Body(string n) => R.points.TryGetValue(n, out var w) ? w : null;
            void FighterPilot(CombatPilot pl) { PilotPresets.Fighter(pl); pl.hideSearch = 2500f; }

            // ---- Judecca: the pirates' world, two fortified cities with platforms over them
            var judecca = Body("Judecca");
            if (judecca != null)
            {
                SurfaceBaseInSpace(s, "Dis", judecca, true, 22f, b,
                    "The pirates' capital on Judecca, a fortified city on the ice by a methane sea: gun batteries, missile platforms, tanks, " +
                    "LAVs and mobile launchers, ships on the sea and submarines under it, fighters on its pads. Geostationary platforms over it.");
                SurfaceBaseInSpace(s, "Malebolge", judecca, true, -28f, b,
                    "The pirates' second city on Judecca, a fortress port on a methane sea, defended as Dis is. Geostationary platforms over it.");
                NhDefenceSats(R, P("Dis"), "Dis Defence Platform", 2, 260000f, b);
                NhDefenceSats(R, P("Malebolge"), "Malebolge Defence Platform", 2, 260000f, b);
            }

            // ---- Gilgal: the moon bases and the picket fleet
            foreach (var (moon, baseName) in new[] { ("Jericho", "Jericho Base"), ("Bethel", "Bethel Base") })
            {
                var m = Body(moon);
                if (m == null) continue;
                m.hostile = true;
                m.description += " Pirates hold it.";
                SurfaceBaseInSpace(s, baseName, m, true, 10f, b,
                    $"A pirate base on Gilgal's moon {moon}, as well defended as the cities on Judecca: batteries, missile sites, armour and fighters, " +
                    "with platforms over it.");
                NhDefenceSats(R, P(baseName), $"{moon} Defence Platform", 2, 120000f, b);
            }
            var gilgal = Body("Gilgal");
            if (gilgal != null)
            {
                Vector3 gc = gilgal.Position;
                Vector3 outward = Vector3.ProjectOnPlane(gc - R.sys.position, Vector3.up).normalized;
                // (in the ring, 1.35 to 2.3 times her radius, on the side toward the star: it keeps the rings)
                Vector3 post = gc - outward * (gilgal.bodyRadius * 1.8f) + Vector3.up * 4000f;
                var ships = new List<(GameObject, string, Action<CombatPilot>, float, float)>
                {
                    (p.enemyDreadnought, "Pirate Dreadnought Leviathan", pl => NhCapital(pl, 45f, 55f, 6f, 8f, 3300f, 1.5f), 180f, 180f),
                };
                for (int i = 0; i < 3; i++) ships.Add((p.enemyCruiser, $"Gilgal Picket Cruiser {(char)('A' + i)}", pl => NhCapital(pl, 75f, 90f, 12f, 18f, 1900f, 3f), 120f, 180f));
                for (int i = 0; i < 5; i++) ships.Add((p.enemyDestroyer, $"Gilgal Picket Frigate {(char)('A' + i)}", pl => NhCapital(pl, 110f, 140f, 18f, 25f, 2300f, 4f), 90f, 180f));
                for (int i = 0; i < 10; i++) ships.Add((p.enemyFighter, $"Gilgal Picket Fighter {(char)('A' + i)}", FighterPilot, 45f, 18f));
                NhPirates(R, "Gilgal picket fleet", post, gc, 12000f, ships.ToArray());
            }

            // ---- Sheol: the Vrask outpost, its satellites, the picket
            var sheol = Body("Sheol");
            if (sheol != null)
            {
                SurfaceBaseInSpace(s, "Sheol Outpost", sheol, true, 5f, b,
                    "A Vrask outpost on Sheol's dark ice: their stronghold in the Nine Hells, ringed by defence satellites, a picket of ten frigates over it.");
                NhDefenceSats(R, P("Sheol Outpost"), "Vrask Defence Satellite", 4, 90000f, b);
                Vector3 sc = sheol.Position;
                Vector3 outward = Vector3.ProjectOnPlane(sc - R.sys.position, Vector3.up).normalized;
                Vector3 post = sc + outward * (sheol.bodyRadius + 60000f);
                NhVrask(R, "Sheol picket", post, 9000f, new[]
                {
                    ("Maw", "One", 0f, 0f, 0f), ("Maw", "Two", 180f, 2500f, 300f),
                    ("Snare", "One", 40f, 2200f, 150f), ("Snare", "Two", -40f, 2200f, -150f), ("Snare", "Three", 120f, 2600f, 200f),
                    ("Fang", "One", 80f, 1800f, -200f), ("Fang", "Two", -80f, 1800f, 200f),
                    ("Skink", "One", 20f, 1200f, 100f), ("Skink", "Two", -20f, 1200f, -100f), ("Skink", "Three", 200f, 1400f, 0f),
                });
            }
            R.targets.AddRange(b.targets);
        }
    }
}

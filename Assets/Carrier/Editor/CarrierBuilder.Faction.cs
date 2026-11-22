using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrierGame;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The ships of a faction (Data/vrask.json, greys.json, empire.json: FactionTable), from the
    /// AI-made models converted by Tools/Blender/convert_lizards.py, each built much as a player's
    /// modelled frigate is: the turrets cut out of the model traverse on their pivots
    /// (ModelledTurrets, FitArc); the guns the model has fixed in its hull are aiming joints at
    /// their mounts that can swing their line of fire a few degrees, firing from the muzzles at the
    /// model's barrels; guns at points (point defence, ion tubes) swing as far as their data says.
    /// Engine glows at the engines, a shield in the faction's colour, armour plating, the ship's
    /// systems, hull points, breakup into chunks, and its target information (hostile, of the
    /// faction). The places on the hull the rest of its fighting uses are kept as points
    /// (FactionShip). What only one faction has (the Vrask's tractor, pods, artillery, escorts) is
    /// that faction's builder's to add (CarrierBuilder.Vrask).
    /// </summary>
    public static partial class CarrierBuilder
    {
        // what the faction models' hardpoints have beside ModelHP's: the fixed guns left in the
        // hull and the role points (convert_lizards.py)
        [Serializable] class FactionGun { public string name; public float[] pos, facing; public V[] muzzles; }
        [Serializable] class FactionPoint { public string name, kind; public float[] pos, dir; public float size; }
        [Serializable] class FactionHP { public FactionGun[] guns; public FactionPoint[] points; }

        /// <summary>A faction's table, from its file (null: none).</summary>
        static T LoadFaction<T>(string path) where T : FactionTable =>
            File.Exists(path) ? FactionTable.Parse<T>(File.ReadAllText(path)) : null;

        /// <summary>A faction's models, for the import settings.</summary>
        static IEnumerable<string> FactionModelPaths(FactionTable t) =>
            t?.ships == null ? Enumerable.Empty<string>() : t.ships.Select(s => $"{Root}/Models/{s.model}.fbx");

        /// <summary>A faction's shield (the frigates' bubble, in its colour: Shield_&lt;prefix&gt;)
        /// and its bolts (by name).</summary>
        static Dictionary<string, Projectile> MakeFactionKit(Prefabs p, FactionTable t)
        {
            if (Mats.TryGetValue("Shield_Bubble", out var bubble))
            {
                var m = new Material(bubble);
                m.SetColor("_Color", t.ShieldTint);
                SaveMat("Shield_" + t.Prefix, m);
            }
            var bolts = new Dictionary<string, Projectile>();
            foreach (var b in t.bolts ?? new FactionBolt[0])
            {
                Additive(b.material, b.Glow);
                var bolt = MakeBolt(b.name, b.material, V3(b.size), b.speed, b.damage, p.explosionSmall);
                bolt.kind = b.Kind;
                EditorUtility.SetDirty(bolt);
                bolts[b.name] = bolt;
                Log($"{t.Name.ToLowerInvariant()} bolt {b.name}: {b.kind} -> {bolt.kind}, {b.damage} a shot, {b.speed} m/s");
            }
            AssetDatabase.SaveAssets();
            return bolts;
        }

        /// <summary>
        /// One of a faction's ships, its prefab saved: the hull and its guns, points, engines,
        /// shield, breakup, health, armour, systems and target information, built from `s`.
        /// `addShip` adds the faction's own FactionShip (its role set); `addRole` then adds what
        /// that ship has beside (a tractor, pods, artillery...), with its guns.
        /// </summary>
        static GameObject MakeFactionShip(Prefabs p, FactionTable t, FactionShipStats s, Dictionary<string, Projectile> bolts,
            Func<GameObject, FactionShip> addShip, Action<GameObject, FactionShip, List<CarrierTurret>> addRole)
        {
            string json = File.ReadAllText($"{Root}/Data/{s.hardpoints}");
            var hpModel = JsonUtility.FromJson<ModelHP>(json);
            var hpFaction = JsonUtility.FromJson<FactionHP>(json);
            // two-sided, so the pieces show their inside where the hull breaks apart
            var hullMat = Lit(s.prefab + "_Hull", Color.white, Load<Texture2D>($"{Root}/Models/{s.texture}"), null, 0.3f, 0.4f, twoSided: true);

            var root = new GameObject(s.prefab);
            var hull = Place(OwnModel(s.model), root.transform, "Hull");
            // (ModelledTurrets and FitArc read the ship being built from fs and mhp, as for a frigate)
            fs = new FrigateSpec { id = s.id, displayName = s.displayName, prefab = s.prefab };
            mhp = hpModel;
            var turretParts = TakeTurretParts(hull);
            Paint(hull, hullMat);
            foreach (var mf in hull.GetComponentsInChildren<MeshFilter>())
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            var hb = LocalBounds(hull.transform);
            Log($"{s.displayName}: hull {hb.size:F0} m, centre {hb.center:F1}");

            // the guns
            var turrets = new List<CarrierTurret>();
            var fittings = new List<Transform>();
            Transform fixedNode = null;
            foreach (var w in s.guns ?? new FactionWeapon[0])
            {
                if (!bolts.TryGetValue(w.bolt, out var bolt)) { Log($"{s.displayName} {w.label}: no bolt {w.bolt}"); continue; }
                if (w.on == "turrets")
                {
                    if (hpModel.turrets == null || hpModel.turrets.Length == 0) { Log($"{s.displayName} {w.label}: no turrets in the model"); continue; }
                    fs.gunYawSpeed = w.yawSpeed;
                    fs.gunInterval = w.interval;
                    fs.gunRange = w.range;
                    foreach (var tu in ModelledTurrets(root, turretParts, hullMat, bolt))
                    {
                        ArmFactionGun(tu, w, bolt);
                        turrets.Add(tu);
                        fittings.Add(tu.transform);
                    }
                    continue;
                }
                if (fixedNode == null) fixedNode = Node("Guns", root.transform).transform;
                if (w.on == "guns")
                {
                    foreach (var g in hpFaction.guns ?? new FactionGun[0])
                        turrets.Add(FixedGun(fixedNode, $"{w.label}: {g.name}", V3(g.pos), V3(g.facing), g.muzzles.Select(m => V3(m.pos)).ToArray(), bolt, w));
                    continue;
                }
                // guns at points of a kind: one each, or one gun firing from them all (a volley)
                var at = (hpFaction.points ?? new FactionPoint[0]).Where(pt => pt.kind == w.on).ToArray();
                if (at.Length == 0) { Log($"{s.displayName} {w.label}: no '{w.on}' points"); continue; }
                if (w.volley)
                {
                    Vector3 dir = V3(at[0].dir).normalized;
                    Vector3 mount = at.Aggregate(Vector3.zero, (a, pt) => a + V3(pt.pos)) / at.Length - dir * 6f;
                    turrets.Add(FixedGun(fixedNode, w.label, mount, dir, at.Select(pt => V3(pt.pos)).ToArray(), bolt, w));
                }
                else
                    foreach (var pt in at)
                    {
                        Vector3 dir = V3(pt.dir).normalized;
                        // (a point is at the barrel's tip: the joint a little behind it)
                        turrets.Add(FixedGun(fixedNode, $"{w.label}: {pt.name}", V3(pt.pos) - dir * Mathf.Max(2f, pt.size * 2f), dir, new[] { V3(pt.pos) }, bolt, w));
                    }
            }
            if (turretParts.Count > 0 && !(s.guns ?? new FactionWeapon[0]).Any(w => w.on == "turrets"))
                foreach (var part in turretParts.Values.Where(tp => tp != null && tp.parent == null))
                {
                    // (a model with turrets but no turret guns in the data: they go back on the hull, fixed)
                    part.SetParent(hull.transform, true);
                }

            // the places on the hull the rest of their fighting works from
            var pointNode = Node("Points", root.transform).transform;
            var points = (hpFaction.points ?? new FactionPoint[0]).Select(pt => new FactionShip.Point
            {
                name = pt.name,
                kind = pt.kind,
                size = pt.size,
                at = Node(pt.name, pointNode, V3(pt.pos), Quaternion.LookRotation(V3(pt.dir).sqrMagnitude > 0.01f ? V3(pt.dir) : Vector3.forward,
                                                                                  Mathf.Abs(V3(pt.dir).y) > 0.9f ? Vector3.forward : Vector3.up)).transform,
            }).ToArray();

            // engine glows at the engines: the kit plume, in the faction's colour; a light at only
            // the three biggest (the Skink has ten)
            var plumeModel = Model("Spaceship_Kit/SM_reactor_FX");
            var engineNode = Node("Engines", root.transform).transform;
            var engines = hpModel.engines ?? new Engine[0];
            var lit = new HashSet<int>(engines.Select((e, i) => (e, i)).OrderByDescending(x => x.e.radius).Take(3).Select(x => x.i));
            for (int i = 0; i < engines.Length; i++)
            {
                var e = engines[i];
                var eg = Node("Engine" + (i + 1), engineNode, V3(e.exit), Quaternion.LookRotation(V3(e.dir), Vector3.up));
                var et = Exhaust(eg, plumeModel, e.radius, s.EngineGlow);
                et.throttle = 0.55f;
                if (!lit.Contains(i) && et.glow != null) { UnityEngine.Object.DestroyImmediate(et.glow.gameObject); et.glow = null; }
                fittings.Add(eg.transform);
            }

            // the shield: the frigates' hex bubble round the hull, in the faction's colour
            if (s.shield > 0f)
            {
                var src = OwnModel("ShieldHex");
                var mesh = src != null ? src.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
                if (mesh == null) Log("shield mesh missing (Models/ShieldHex.fbx)");
                else
                {
                    var go = Node("Shield", root.transform, hb.center);
                    float r = Mathf.Max(hb.size.x, hb.size.y) * 0.87f;
                    go.transform.localScale = new Vector3(r, r, hb.size.z * 0.6f);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = Mats.TryGetValue("Shield_" + t.Prefix, out var sm) ? sm : Mats["Shield_Bubble"];
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    var shield = go.AddComponent<Shield>();
                    shield.bubble = mr;
                    shield.capacity = s.shield;
                    shield.rechargeRate = s.shieldRecharge;
                }
            }

            // the hull comes apart in chunks, the turrets and engines breaking off as parts
            MakeBreakable(root, hull, s.prefab, Mathf.Clamp(Mathf.RoundToInt(hb.size.z / 14f), 10, 16), true, fittings.ToArray());
            var col = root.AddComponent<BoxCollider>();
            col.center = hb.center;
            col.size = hb.size;

            var h = root.AddComponent<Health>();
            h.maxHealth = s.hull;
            h.deathEffect = p.explosion;
            h.deathEffectScale = s.explosion;
            h.respawnDelay = s.respawn;
            if (s.armor > 0f)
            {
                var armor = root.AddComponent<Armor>();
                armor.rating = s.armor;
                armor.plating = s.plating;
            }
            // the ship's systems: its guns, engines and shield generator, damaged one by one
            root.AddComponent<ShipSystems>();

            var info = root.AddComponent<TargetInfo>();
            info.displayName = s.displayName;
            info.shipClass = s.shipClass;
            info.faction = "Hostile";   // (the co-op world, the CIWS and the wingmen go by this)
            info.group = t.Name;
            info.threat = s.threat;
            info.weapons = s.weapons;
            info.kind = TargetInfo.Kind.Capital;

            var w2 = root.AddComponent<EnemyWeapons>();
            w2.turrets = turrets.ToArray();
            w2.holdFire = 4f;
            if (s.salvoSize > 0)
            {
                w2.missilePrefab = p.missile;
                w2.salvoSize = s.salvoSize;
                w2.salvoInterval = s.salvoInterval;
                w2.missileRange = s.missileRange;
            }

            var ship = addShip(root);
            ship.points = points;
            ship.sensorRange = s.sensorRange;
            addRole?.Invoke(root, ship, turrets);

            Log($"{s.prefab}: {turrets.Count} guns ({string.Join(", ", turrets.GroupBy(tu => tu.projectilePrefab.name).Select(g => $"{g.Count()} x {g.Key}"))}), " +
                $"{engines.Length} engines, {points.Length} points ({string.Join(", ", points.Select(pt => pt.kind).Distinct())}), " +
                $"hull {s.hull}, shield {s.shield}, armour {s.armor:F2}");
            fs = null;
            mhp = null;
            return SavePrefab(root, s.prefab);
        }

        /// <summary>A turret cut out of the model, given the faction gun's numbers (it fires on what
        /// EnemyWeapons hands it).</summary>
        static void ArmFactionGun(CarrierTurret tu, FactionWeapon w, Projectile bolt)
        {
            tu.projectilePrefab = bolt;
            tu.fireInterval = w.interval;
            tu.range = w.range;
            tu.yawSpeed = w.yawSpeed;
            tu.pitchSpeed = w.yawSpeed * 0.7f;
            tu.fireSound = bolt.kind == HitKind.Cannon || bolt.kind == HitKind.Penetrator ? Sfx.Cannon : Sfx.Blaster;
            tu.autoFire = true;
            tu.target = null;   // EnemyWeapons hands it the target once the ship is in the fight
        }

        /// <summary>A gun the model has fixed in its hull (or at a point: an ion tube, the spinal
        /// gun, a point-defence gun): an aiming joint at `mount`, resting along `facing`, that can
        /// swing its line of fire `w.cone` degrees, firing from `muzzles` (in the ship's space).
        /// Nothing on it that shows moves.</summary>
        static CarrierTurret FixedGun(Transform under, string label, Vector3 mount, Vector3 facing, Vector3[] muzzles, Projectile bolt, FactionWeapon w)
        {
            facing = facing.sqrMagnitude > 0.01f ? facing.normalized : Vector3.forward;
            var go = Node("Gun_" + label.Replace(": ", "_").Replace(" ", ""), under, mount,
                          Quaternion.LookRotation(facing, Mathf.Abs(facing.y) > 0.9f ? Vector3.forward : Vector3.up));
            var aim = Node("Aim", go.transform).transform;
            var ms = muzzles.Select((m, i) => Node("Muzzle" + (i + 1), aim, aim.InverseTransformPoint(under.TransformPoint(m))).transform).ToArray();
            var tu = go.AddComponent<CarrierTurret>();
            tu.label = label;
            tu.yaw = aim;
            tu.muzzles = ms;
            tu.ballMount = true;
            tu.coneLimit = Mathf.Max(1f, w.cone);
            tu.fireCone = Mathf.Min(4f, Mathf.Max(2f, w.cone * 0.5f));
            tu.leadTarget = true;
            ArmFactionGun(tu, w, bolt);
            return tu;
        }

        /// <summary>How a faction's ship flies, from its numbers.</summary>
        static void FactionPilot(CombatPilot pl, FactionShipStats s)
        {
            pl.style = s.style == "Fighter" ? CombatPilot.Style.Fighter : CombatPilot.Style.Capital;
            pl.cruiseSpeed = s.cruiseSpeed;
            pl.attackSpeed = s.attackSpeed;
            pl.turnRate = s.turnRate;
            pl.acceleration = s.acceleration;
            if (s.standoff > 0f) pl.standoff = s.standoff;
            if (s.circleSpeed != 0f) pl.circleSpeed = s.circleSpeed;
            if (s.breakOffDistance > 0f) pl.breakOffDistance = s.breakOffDistance;
            if (s.extendDistance > 0f) pl.extendDistance = s.extendDistance;
            pl.hideBelow = s.hideBelow;
            pl.holdFacing = s.holdFacing;
        }

        /// <summary>
        /// What the factions after the Vrask carry, from a ship's numbers (FactionShipStats): its
        /// signature and cloak (Signature, Cloak), a targeting laser at its "designator" point
        /// (Designator), a jammer (EcmEmitter), an EMP burst with a glow at its "emp" point
        /// (EmpBurst), a drone bay at its "drone_bay" points (DroneBay), a warp inhibitor
        /// (Interdictor). None of it if its numbers say none.
        /// </summary>
        static void AddFactionGear(GameObject root, FactionShip ship, FactionTable t, FactionShipStats s)
        {
            var said = new List<string>();
            if (s.signature > 0f)
            {
                root.AddComponent<Signature>().value = s.signature;
                said.Add($"signature {s.signature:0.##}");
            }
            if (s.cloak > 0f)
            {
                if (root.GetComponent<Signature>() == null) root.AddComponent<Signature>();
                root.AddComponent<Cloak>().hiddenScale = s.cloak;
                said.Add($"cloak to {s.cloak:0.##}");
            }
            if (s.designatorRange > 0f)
            {
                var d = root.AddComponent<Designator>();
                d.range = s.designatorRange;
                d.emitter = ship.PointOf("designator");
                var lr = Node("Laser", d.emitter != null ? d.emitter : root.transform).AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.widthMultiplier = 1.2f;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sharedMaterial = Additive($"FX_{t.Prefix}Laser", t.BeamGlow * 0.6f);
                lr.enabled = false;
                d.line = lr;
                said.Add($"targeting laser {s.designatorRange / 1000f:0.#} km");
            }
            if (s.ecm != null && s.ecm.radius > 0f)
            {
                var e = root.AddComponent<EcmEmitter>();
                e.radius = s.ecm.radius;
                e.strength = s.ecm.strength;
                if (s.ecm.pdRadius > 0f) e.pdRadius = s.ecm.pdRadius;
                e.ghosts = s.ecm.ghosts;
                e.active = false;   // (its tactics turn it on)
                said.Add($"jammer {s.ecm.radius / 1000f:0.#} km at {s.ecm.strength:P0}, {s.ecm.ghosts} ghosts");
            }
            if (s.emp != null && s.emp.radius > 0f)
            {
                var b = root.AddComponent<EmpBurst>();
                b.radius = s.emp.radius;
                b.strength = s.emp.strength;
                if (s.emp.chargeTime > 0f) b.chargeTime = s.emp.chargeTime;
                if (s.emp.cooldown > 0f) b.cooldown = s.emp.cooldown;
                // its glow as it charges: a light at its emp point
                var at = ship.PointOf("emp");
                var lg = Node("EmpGlow", at != null ? at : root.transform).AddComponent<Light>();
                lg.type = LightType.Point;
                lg.range = Mathf.Max(200f, s.emp.radius * 0.15f);
                Color glow = t.BeamGlow;
                lg.color = glow.maxColorComponent > 0f ? glow / glow.maxColorComponent : Color.cyan;
                lg.enabled = false;
                b.glowLight = lg;
                said.Add($"EMP burst {s.emp.radius / 1000f:0.#} km, {s.emp.strength:0}");
            }
            if (s.drones != null && s.drones.stock > 0)
            {
                var bay = root.AddComponent<DroneBay>();
                bay.stock = s.drones.stock;
                if (s.drones.maxOut > 0) bay.maxOut = s.drones.maxOut;
                if (s.drones.perLaunch > 0) bay.perLaunch = s.drones.perLaunch;
                if (s.drones.launchInterval > 0f) bay.launchInterval = s.drones.launchInterval;
                if (s.drones.launchRange > 0f) bay.launchRange = s.drones.launchRange;
                bay.bays = ship.PointsOf("drone_bay");
                said.Add($"drone bay: {s.drones.stock} drones, {bay.bays.Length} bays");
            }
            if (s.interdict > 0f)
            {
                root.AddComponent<Interdictor>().radius = s.interdict;
                said.Add($"warp inhibitor {s.interdict / 1000f:0.#} km");
            }
            if (said.Count > 0) Log($"{s.displayName}: {string.Join(", ", said)}");
        }

        /// <summary>
        /// A faction's band lying in wait round a point (as the Vrask raiders: VraskRaiders,
        /// NhVrask), its ships `slots` (ship id, name, angle round from the side away from the
        /// star, distance, height), each made by `make` (the system's own enemy maker: prefab,
        /// name, place, what it faces, its pilot's set-up, its respawn), and `fleet` given the band
        /// and its pilots (its tactics as a group, if it has any).
        /// </summary>
        static EncounterGroup FactionBand(FactionTable t, Dictionary<string, GameObject> prefabs, Transform sys, Transform parent, string groupName,
            Vector3 centre, float spotRange, IEnumerable<(string ship, string name, float angle, float dist, float height)> slots,
            Func<GameObject, string, Vector3, Vector3, Action<CombatPilot>, float, GameObject> make, Action<EncounterGroup, List<CombatPilot>> fleet = null)
        {
            if (t == null || prefabs.Count == 0) { Log($"{groupName}: no {(t != null ? t.Name : "faction")} ships"); return null; }
            Vector3 outward = Vector3.ProjectOnPlane(centre - sys.position, Vector3.up);
            outward = outward.sqrMagnitude > 1f ? outward.normalized : Vector3.forward;
            var pilots = new List<(CombatPilot pilot, FactionShipStats s)>();
            foreach (var slot in slots)
            {
                var s = t.Find(slot.ship);
                if (s == null || !prefabs.TryGetValue(s.id, out var prefab) || prefab == null) { Log($"{groupName}: no ship '{slot.ship}'"); continue; }
                Vector3 pos = centre + Quaternion.AngleAxis(slot.angle, Vector3.up) * outward * slot.dist + Vector3.up * slot.height;
                string name = string.IsNullOrEmpty(slot.name) ? s.displayName : $"{s.displayName} {slot.name}";
                var go = make(prefab, name, pos, centre, pl => FactionPilot(pl, s), s.respawn);
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
            if (parent != null) eg.transform.SetParent(parent, false);
            eg.transform.position = centre;
            eg.groupName = groupName;
            eg.pilots = pilots.Select(x => x.pilot).ToArray();
            eg.spotRange = spotRange;
            fleet?.Invoke(eg, eg.pilots.ToList());
            Log($"{groupName}: {pilots.Count} {t.Name} ({string.Join(", ", pilots.Select(x => x.pilot.name))}), spotting at {spotRange / 1000f:0.#} km");
            return eg;
        }

        /// <summary>A tractor beam (TractorBeam) from the ship's "tractor" point (its bow if it has
        /// none), drawn as a line in the faction's colour while it holds a ship.</summary>
        static TractorBeam AddTractorBeam(GameObject root, FactionShip ship, FactionTable t, float range)
        {
            var beam = root.AddComponent<TractorBeam>();
            var at = ship.PointOf("tractor");
            beam.emitter = at != null ? at : root.transform;
            beam.range = range;
            var lr = Node("TractorBeam", beam.emitter).AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 4;
            lr.widthMultiplier = beam.width;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = Additive($"FX_{t.Prefix}Tractor", t.BeamGlow);
            lr.enabled = false;
            beam.line = lr;
            return beam;
        }
    }
}

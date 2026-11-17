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
    /// The Vrask, an alien faction, and their four ships (Data/vrask.json): the Snare (capture),
    /// the Fang (assault), the Maw (siege) and the Skink (escort), from the AI-made models
    /// Models/Lizard_Frigate1..4.fbx in their own paint, converted by Tools/Blender/convert_lizards.py.
    ///
    /// Each is built much as a player's modelled frigate is: the turrets cut out of the model
    /// traverse on their pivots (ModelledTurrets, FitArc); the guns the model has fixed in its hull
    /// are aiming joints at their mounts that can swing their line of fire a few degrees, firing from
    /// the muzzles at the model's barrels; the point-defence guns swing further, as the model can't.
    /// Engine glows at the engines, a shield (the Vrask are the first enemies with one), armour
    /// plating, the ship's systems, hull points, breakup into chunks, and its target information
    /// (hostile, of the Vrask). The places on the hull the rest of their fighting will use (the
    /// tractor, the breaching prow, the boarding pods, the silos) are kept as points (VraskShip).
    /// The Maw's spinal gun is its artillery, with its silos (CarrierBuilder.Artillery).
    ///
    /// The raiding group goes in the star system with VraskRaiders; the siege group over a city
    /// with VraskSiege.
    /// </summary>
    public static partial class CarrierBuilder
    {
        // what the Vrask models' hardpoints have beside ModelHP's: the fixed guns left in the hull
        // and the role points (convert_lizards.py)
        [Serializable] class VraskGun { public string name; public float[] pos, facing; public V[] muzzles; }
        [Serializable] class VraskPoint { public string name, kind; public float[] pos, dir; public float size; }
        [Serializable] class VraskHP { public VraskGun[] guns; public VraskPoint[] points; }

        static VraskTable vrask;

        static VraskTable LoadVrask() => vrask = VraskTable.Parse(File.ReadAllText(VraskTable.DataPath));

        /// <summary>The Vrask models, for the import settings.</summary>
        static IEnumerable<string> VraskModelPaths()
        {
            var t = File.Exists(VraskTable.DataPath) ? LoadVrask() : null;
            return t?.ships == null ? Enumerable.Empty<string>() : t.ships.Select(s => $"{Root}/Models/{s.model}.fbx");
        }

        /// <summary>Their bolts, then each of their ships' prefabs (into p.vrask, by id).</summary>
        static void MakeVrask(Prefabs p)
        {
            if (!File.Exists(VraskTable.DataPath)) { Log("vrask: no " + VraskTable.DataPath); return; }
            LoadVrask();
            // their shield: the frigates' bubble, in green
            if (Mats.TryGetValue("Shield_Bubble", out var bubble))
            {
                var m = new Material(bubble);
                m.SetColor("_Color", new Color(0.45f, 1.6f, 0.55f));
                SaveMat("Shield_Vrask", m);
            }
            var bolts = new Dictionary<string, Projectile>();
            foreach (var b in vrask.bolts)
            {
                Additive(b.material, b.Glow);
                var bolt = MakeBolt(b.name, b.material, V3(b.size), b.speed, b.damage, p.explosionSmall);
                bolt.kind = b.Kind;
                EditorUtility.SetDirty(bolt);
                bolts[b.name] = bolt;
                Log($"vrask bolt {b.name}: {b.kind} -> {bolt.kind}, {b.damage} a shot, {b.speed} m/s");
            }
            AssetDatabase.SaveAssets();
            // their boarding pod (CarrierBuilder.Boarding)
            vraskPod = MakeBoardingPod(p);
            foreach (var s in vrask.ships) p.vrask[s.id] = MakeVraskShip(p, s, bolts);
        }

        static GameObject MakeVraskShip(Prefabs p, VraskShipStats s, Dictionary<string, Projectile> bolts)
        {
            string json = File.ReadAllText($"{Root}/Data/{s.hardpoints}");
            var hpModel = JsonUtility.FromJson<ModelHP>(json);
            var hpVrask = JsonUtility.FromJson<VraskHP>(json);
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
            foreach (var w in s.guns ?? new VraskWeapon[0])
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
                        ArmVraskGun(tu, w, bolt);
                        turrets.Add(tu);
                        fittings.Add(tu.transform);
                    }
                    continue;
                }
                if (fixedNode == null) fixedNode = Node("Guns", root.transform).transform;
                if (w.on == "guns")
                {
                    foreach (var g in hpVrask.guns ?? new VraskGun[0])
                        turrets.Add(FixedGun(fixedNode, $"{w.label}: {g.name}", V3(g.pos), V3(g.facing), g.muzzles.Select(m => V3(m.pos)).ToArray(), bolt, w));
                    continue;
                }
                // guns at points of a kind: one each, or one gun firing from them all (a volley)
                var at = (hpVrask.points ?? new VraskPoint[0]).Where(pt => pt.kind == w.on).ToArray();
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
            if (turretParts.Count > 0 && !(s.guns ?? new VraskWeapon[0]).Any(w => w.on == "turrets"))
                foreach (var part in turretParts.Values.Where(t => t != null && t.parent == null))
                {
                    // (a model with turrets but no turret guns in the data: they go back on the hull, fixed)
                    part.SetParent(hull.transform, true);
                }

            // the places on the hull the rest of their fighting works from
            var pointNode = Node("Points", root.transform).transform;
            var points = (hpVrask.points ?? new VraskPoint[0]).Select(pt => new VraskShip.Point
            {
                name = pt.name,
                kind = pt.kind,
                size = pt.size,
                at = Node(pt.name, pointNode, V3(pt.pos), Quaternion.LookRotation(V3(pt.dir).sqrMagnitude > 0.01f ? V3(pt.dir) : Vector3.forward,
                                                                                  Mathf.Abs(V3(pt.dir).y) > 0.9f ? Vector3.forward : Vector3.up)).transform,
            }).ToArray();

            // engine glows at the engines: the kit plume, in the Vrask's colour; a light at only
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

            // the shield: the frigates' hex bubble round the hull, in green
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
                    mr.sharedMaterial = Mats.TryGetValue("Shield_Vrask", out var sm) ? sm : Mats["Shield_Bubble"];
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
            var armor = root.AddComponent<Armor>();
            armor.rating = s.armor;
            armor.plating = s.plating;
            // the ship's systems: its guns, engines and shield generator, damaged one by one
            root.AddComponent<ShipSystems>();

            var info = root.AddComponent<TargetInfo>();
            info.displayName = s.displayName;
            info.shipClass = s.shipClass;
            info.faction = "Hostile";   // (the co-op world, the CIWS and the wingmen go by this)
            info.group = VraskShip.Faction;
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

            var vs = root.AddComponent<VraskShip>();
            vs.role = (VraskShip.Role)Enum.Parse(typeof(VraskShip.Role), s.id);
            vs.points = points;
            // the Snare's tractor beam, at its projector, and how it fights with it
            if (s.tractorRange > 0f) AddTractor(root, vs, s);
            // boarding pods (the Fang's, with its tactics; the Snare's, at what it holds)
            if (s.pods > 0) AddBoarding(root, vs, s);
            // the Maw's spinal artillery, its nuclear silos and its tactics (CarrierBuilder.Artillery)
            if (s.artillery != null && s.artillery.range > 0f) AddArtillery(p, root, vs, s);

            Log($"{s.prefab}: {turrets.Count} guns ({string.Join(", ", turrets.GroupBy(t => t.projectilePrefab.name).Select(g => $"{g.Count()} x {g.Key}"))}), " +
                $"{engines.Length} engines, {points.Length} points ({string.Join(", ", points.Select(pt => pt.kind).Distinct())}), " +
                $"hull {s.hull}, shield {s.shield}, armour {s.armor:F2}");
            fs = null;
            mhp = null;
            return SavePrefab(root, s.prefab);
        }

        /// <summary>A tractor beam (TractorBeam) from the ship's "tractor" point (its bow if it has
        /// none), drawn as a green line while it holds a ship, and the Snare's tactics with it.</summary>
        static void AddTractor(GameObject root, VraskShip vs, VraskShipStats s)
        {
            var beam = root.AddComponent<TractorBeam>();
            var at = vs.PointOf("tractor");
            beam.emitter = at != null ? at : root.transform;
            beam.range = s.tractorRange;
            var lr = Node("TractorBeam", beam.emitter).AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 4;
            lr.widthMultiplier = beam.width;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = Additive("FX_VraskTractor", new Color(0.35f, 1.4f, 0.6f) * 2.5f);
            lr.enabled = false;
            beam.line = lr;
            root.AddComponent<SnareTactics>();
            Log($"{s.displayName}: tractor beam from {(at != null ? at.name : "the hull (no tractor point)")}, {s.tractorRange / 1000f:0.#} km");
        }

        /// <summary>A turret cut out of the model, given the Vrask gun's numbers (it fires on what
        /// EnemyWeapons hands it).</summary>
        static void ArmVraskGun(CarrierTurret tu, VraskWeapon w, Projectile bolt)
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
        static CarrierTurret FixedGun(Transform under, string label, Vector3 mount, Vector3 facing, Vector3[] muzzles, Projectile bolt, VraskWeapon w)
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
            ArmVraskGun(tu, w, bolt);
            return tu;
        }

        /// <summary>
        /// The Vrask raiding group (Data/vrask.json "group"): lying in wait round a place in the
        /// star system (Comet Mirren: far out, away from the corsairs' haunts, where ships come to
        /// mine ice), hidden till a ship comes within its spotting range, then all of them attack.
        /// It is an EncounterGroup like the raiders in the Trojans, so the SpawnDirector brings its
        /// destroyed ships back, and its pilots are CombatPilots (the warp lock, the music, co-op).
        /// The Maw holds well back, bow on; the Snares stand off at medium range, bow on; the Fang
        /// makes fast attack runs; the Skinks screen the Maw.
        /// </summary>
        static EncounterGroup VraskRaiders(Prefabs p, Transform sys,
            Func<GameObject, string, string, Vector3, Vector3, Action<CombatPilot>, float, GameObject> enemy)
        {
            if (vrask?.group?.slots == null || p.vrask.Count == 0) { Log("vrask: no raiding group"); return null; }
            var g = vrask.group;
            var at = sys.GetComponentsInChildren<WarpPoint>(true).FirstOrDefault(w => w.displayName == g.at);
            if (at == null) { Log($"vrask: no place '{g.at}' for the raiders"); return null; }
            if (!string.IsNullOrEmpty(g.note) && (at.description == null || !at.description.Contains(g.note))) at.description = (at.description + " " + g.note).Trim();
            at.hostile = true;
            Vector3 c = at.transform.position;
            // round the place, from the side away from the star (the star is at the system's centre)
            Vector3 outward = Vector3.ProjectOnPlane(c - sys.position, Vector3.up);
            outward = outward.sqrMagnitude > 1f ? outward.normalized : Vector3.forward;
            var pilots = new List<(CombatPilot pilot, VraskShipStats s)>();
            foreach (var slot in g.slots)
            {
                var s = vrask.Find(slot.ship);
                if (s == null || !p.vrask.TryGetValue(s.id, out var prefab) || prefab == null) { Log($"vrask: no ship '{slot.ship}'"); continue; }
                Vector3 pos = c + Quaternion.AngleAxis(slot.angle, Vector3.up) * outward * slot.distance + Vector3.up * slot.height;
                string name = string.IsNullOrEmpty(slot.name) ? s.displayName : $"{s.displayName} {slot.name}";
                var go = enemy(prefab, name, name, pos, c, pl => VraskPilot(pl, s), 180f);
                go.GetComponent<Health>().respawnDelay = s.respawn;
                pilots.Add((go.GetComponent<CombatPilot>(), s));
            }
            // the escorts screen the ship they guard (each to its own side of it)
            int side = 0;
            foreach (var (pl, s) in pilots.Where(x => !string.IsNullOrEmpty(x.s.guard)))
            {
                var ward = pilots.FirstOrDefault(x => x.s.id == s.guard).pilot;
                if (ward == null) continue;
                pl.guard = ward.transform;
                pl.guardLeash = s.guardLeash;
                pl.circleSpeed = Mathf.Abs(pl.circleSpeed) * (side++ % 2 == 0 ? 1f : -1f);
            }
            var eg = new GameObject(g.name).AddComponent<EncounterGroup>();
            eg.groupName = g.name;
            eg.pilots = pilots.Select(x => x.pilot).ToArray();
            eg.spotRange = g.spotRange;
            float nearest = pilots.Count > 0 ? pilots.Min(x => Vector3.Distance(x.pilot.transform.position, c)) : 0f;
            Log($"{g.name}: {pilots.Count} ships lying in wait round {g.at} ({string.Join(", ", pilots.Select(x => x.pilot.name))}), " +
                $"the nearest {nearest / 1000f:F1} km from it, the arrival {at.arrivalDistance / 1000f:F1} km out; spotting at {g.spotRange / 1000f:F1} km");
            return eg;
        }

        /// <summary>How a Vrask ship flies, from its numbers.</summary>
        static void VraskPilot(CombatPilot pl, VraskShipStats s)
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
    }
}

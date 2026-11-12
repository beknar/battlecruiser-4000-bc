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
    /// The frigates a player can fly (Data/frigates.json): a prefab of each, and the roster the
    /// start menu offers them from (Resources/FrigateRoster.asset). The starting frigate, the
    /// Sentinel, is the prefab the levels are built with; the level swaps in the chosen one as it
    /// starts (PlayerFrigate).
    ///
    /// The Sentinel's guns are ball mounts on its gun pods. The others' are the turrets modelled on
    /// them, cut out of the hull by Tools/Blender/convert_frigates.py: each housing traverses on
    /// its own pivot, its barrels (where they are separate) elevate on theirs, and it fires from
    /// the barrels' muzzles. Every frigate gets the same systems (AddFrigateSystems) with its own
    /// numbers: at least one CIWS mount, the bow beam, the shield, the mining gear.
    /// </summary>
    public static partial class CarrierBuilder
    {
        [Serializable] class XZ { public float x, z; }

        [Serializable]
        class FrigateSpec
        {
            public string id, displayName, role, description, prefab, model, texture, hardpoints, shipName, shipClass;
            public float maxSpeed, acceleration, turnRate, pitchRate, maxPitch, elevatorSpeed, elevatorAcceleration;
            public float hull, shield, shieldRecharge;
            public float gunInterval, gunRange, gunYawSpeed, gunDamage, boltSpeed;
            public float[] boltSize, shieldScale;
            public float beamCharge, beamDamage, cargo;
            public int slots;
            public XZ[] ciws, liftSpots;
        }

        [Serializable] class FrigateSpecs { public FrigateSpec[] frigates; }

        // a frigate whose turrets are modelled on it (convert_frigates.py)
        [Serializable]
        class ModelTurret
        {
            public string name, yaw_mesh, pitch_mesh;
            public float[] yaw_pivot, pitch_pivot, facing, up;
            public V[] muzzles;
        }

        [Serializable]
        class ModelHP
        {
            public string id;
            public float length;
            public ModelTurret[] turrets;
            public Engine[] engines;
            public XZ[] ciws;
            public float[] bounds_size;
        }

        static FrigateSpecs frigateSpecs;
        /// <summary>The frigate being built (its numbers, read by AddFrigateSystems and the rest).</summary>
        static FrigateSpec fs;
        /// <summary>Its hardpoints, when its turrets are modelled on it (null for the Sentinel).</summary>
        static ModelHP mhp;

        static void LoadFrigateSpecs()
        {
            frigateSpecs = JsonUtility.FromJson<FrigateSpecs>(File.ReadAllText(Root + "/Data/frigates.json"));
            Log("frigates: " + string.Join(", ", frigateSpecs.frigates.Select(f => f.id)));
        }

        /// <summary>The frigates' models, for the import settings.</summary>
        static IEnumerable<string> FrigateModelPaths() =>
            frigateSpecs.frigates.Select(f => f.model).Where(m => m != "Frigate_Hull").Select(m => $"{Root}/Models/{m}.fbx");

        /// <summary>Every frigate's prefab, and the roster; returns the starting one.</summary>
        static GameObject BuildFrigates(Prefabs p)
        {
            var made = new List<(FrigateSpec spec, GameObject prefab)>();
            foreach (var spec in frigateSpecs.frigates)
            {
                fs = spec;
                mhp = spec.hardpoints == "frigate_hardpoints.json"
                    ? null
                    : JsonUtility.FromJson<ModelHP>(File.ReadAllText(Root + "/Data/" + spec.hardpoints));
                made.Add((spec, BuildFrigatePrefab(p)));
            }
            fs = null;
            mhp = null;
            SaveRoster(made);
            return made[0].prefab;
        }

        static void SaveRoster(List<(FrigateSpec spec, GameObject prefab)> made)
        {
            const string path = Root + "/Resources/FrigateRoster.asset";
            var roster = AssetDatabase.LoadAssetAtPath<FrigateRoster>(path);
            bool fresh = roster == null;
            if (fresh) roster = ScriptableObject.CreateInstance<FrigateRoster>();
            roster.frigates = made.Select(m => new FrigateRoster.Entry
            {
                id = m.spec.id, displayName = m.spec.displayName, role = m.spec.role, description = m.spec.description, prefab = m.prefab,
            }).ToArray();
            if (fresh) AssetDatabase.CreateAsset(roster, path);
            else EditorUtility.SetDirty(roster);
            AssetDatabase.SaveAssets();
            Log("frigate roster: " + string.Join(", ", roster.frigates.Select(e => e.displayName)));
        }

        /// <summary>The turret parts out of the placed model (named "&lt;turret&gt;_Yaw" and
        /// "&lt;turret&gt;_Pitch"), so what is left is the hull alone.</summary>
        static Dictionary<string, Transform> TakeTurretParts(GameObject hull)
        {
            var parts = new Dictionary<string, Transform>();
            if (mhp == null) return parts;
            if (PrefabUtility.IsPartOfPrefabInstance(hull))
                PrefabUtility.UnpackPrefabInstance(hull, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var t in hull.GetComponentsInChildren<Transform>().Where(t => t.name.EndsWith("_Yaw") || t.name.EndsWith("_Pitch")).ToArray())
            {
                t.SetParent(null, true);
                parts[t.name] = t;
            }
            return parts;
        }

        /// <summary>The turrets modelled on the hull: each traverses about its foot and elevates
        /// its barrels about their breech, firing from the barrels' muzzles.</summary>
        static CarrierTurret[] ModelledTurrets(GameObject root, Dictionary<string, Transform> parts, Material hullMat, Projectile bolt)
        {
            var node = Node("Turrets", root.transform).transform;
            var made = new List<CarrierTurret>();
            foreach (var mt in mhp.turrets)
            {
                Vector3 facing = V3(mt.facing), up = V3(mt.up);
                var mount = Node("Turret_" + mt.name, node, V3(mt.yaw_pivot), Quaternion.LookRotation(facing, up));
                var yaw = Node("Yaw", mount.transform).transform;
                var pitchAt = mount.transform.InverseTransformPoint(root.transform.TransformPoint(V3(mt.pitch_pivot)));
                var pitch = Node("Pitch", yaw, pitchAt).transform;
                // the parts sit where they were on the hull; their vertices are about their pivots
                void Fit(string mesh, Transform under, float[] pivot)
                {
                    if (string.IsNullOrEmpty(mesh) || !parts.TryGetValue(mesh, out var part)) return;
                    part.SetParent(under, true);
                    part.SetPositionAndRotation(root.transform.TransformPoint(V3(pivot)), root.transform.rotation);
                    part.localScale = Vector3.one;
                    foreach (var r in part.GetComponentsInChildren<Renderer>()) r.sharedMaterial = hullMat;
                }
                Fit(mt.yaw_mesh, yaw, mt.yaw_pivot);
                Fit(mt.pitch_mesh, pitch, mt.pitch_pivot);
                var muzzles = mt.muzzles.Select((m, i) =>
                    Node("Muzzle" + (i + 1), pitch, pitch.InverseTransformPoint(root.transform.TransformPoint(V3(m.pos)))).transform).ToArray();

                var tu = mount.AddComponent<CarrierTurret>();
                tu.label = mt.name;
                tu.yaw = yaw;
                tu.pitch = pitch;
                tu.muzzles = muzzles;
                tu.projectilePrefab = bolt;
                tu.yawSpeed = fs.gunYawSpeed;
                tu.pitchSpeed = fs.gunYawSpeed * 0.7f;
                tu.yawLimit = 160f;
                tu.minPitch = -10f;
                tu.maxPitch = 70f;
                tu.checkHullClearance = true;
                tu.fireInterval = fs.gunInterval;
                tu.range = fs.gunRange;
                tu.fireCone = 3f;
                tu.leadTarget = true;
                FitArc(tu, root.transform);
                made.Add(tu);
                Log($"{fs.displayName} turret {mt.name}: at {mount.transform.localPosition:F1} facing {facing:F1}, " +
                    $"{(string.IsNullOrEmpty(mt.pitch_mesh) ? "barrels fixed to the housing" : "barrels elevate")}, {muzzles.Length} muzzles; " +
                    (tu.unevenArc ? $"traverse {tu.yawMin:F0} to {tu.yawMax:F0} deg" : $"traverse all round") + $", elevation {tu.minPitch:F0} to {tu.maxPitch:F0} deg");
            }
            foreach (var left in parts.Values.Where(t => t != null && t.parent == null))
            {
                Log($"{fs.displayName}: turret part {left.name} not used");
                UnityEngine.Object.DestroyImmediate(left.gameObject);
            }
            return made.ToArray();
        }

        /// <summary>
        /// How far a modelled turret can turn without its barrels pointing into its own ship: it is
        /// swung through every angle either side of rest, and at each its gun line (from its foot to
        /// its breech, and from the breech along each barrel to just past the muzzle) is checked
        /// against the hull. The widest clear arc round its rest direction is its traverse (a flank
        /// turret doesn't swing in across the deck); then the lowest and highest barrel angles clear
        /// all across that arc are its elevation. A line already touching the hull at rest (a
        /// turret sitting in a recess) is left out of the check.
        /// </summary>
        static void FitArc(CarrierTurret tu, Transform root)
        {
            var cols = root.Find("Hull").GetComponentsInChildren<MeshCollider>();
            Physics.SyncTransforms();
            Transform yaw = tu.yaw, pitch = tu.pitch;
            Quaternion y0 = yaw.localRotation, p0 = pitch.localRotation;
            bool Hit(Vector3 a, Vector3 b)
            {
                Vector3 d = b - a;
                float len = d.magnitude;
                if (len < 0.01f) return false;
                d /= len;
                foreach (var c in cols)
                    if (c.Raycast(new Ray(a, d), out _, len) || c.Raycast(new Ray(b, -d), out _, len)) return true;
                return false;
            }
            bool[] Clear(float ya, float pa)
            {
                yaw.localRotation = y0 * Quaternion.AngleAxis(ya, Vector3.up);
                pitch.localRotation = p0 * Quaternion.AngleAxis(-pa, Vector3.right);
                var lines = new List<bool> { !Hit(yaw.position + yaw.up * 0.5f, pitch.position) };
                foreach (var m in tu.muzzles) lines.Add(!Hit(pitch.position, m.position + m.forward * 1.5f));
                return lines.ToArray();
            }
            var rest = Clear(0f, 0f);
            bool Ok(float ya, float pa)
            {
                var now = Clear(ya, pa);
                for (int i = 0; i < now.Length; i++) if (rest[i] && !now[i]) return false;
                return true;
            }
            float lo = 0f, hi = 0f;
            for (float a = 2f; a <= 178f && Ok(a, 0f); a += 2f) hi = a;
            for (float a = -2f; a >= -178f && Ok(a, 0f); a -= 2f) lo = a;
            bool AllAcross(float pa)
            {
                for (float a = lo; a <= hi; a += 6f) if (!Ok(a, pa)) return false;
                return Ok(hi, pa);
            }
            float minP = 0f, maxP = 0f;
            for (float p = -1f; p >= -10f && AllAcross(p); p -= 1f) minP = p;
            for (float p = 5f; p <= 70f && AllAcross(p); p += 5f) maxP = p;
            yaw.localRotation = y0;
            pitch.localRotation = p0;
            if (hi >= 178f && lo <= -178f) { tu.unevenArc = false; tu.yawLimit = 180f; }
            else { tu.unevenArc = true; tu.yawMin = lo; tu.yawMax = hi; }
            tu.minPitch = minP;
            tu.maxPitch = maxP;
            if (rest.Any(c => !c)) Log($"{fs.displayName} turret {tu.label}: {rest.Count(c => !c)} of its gun lines touch the hull at rest (left out)");
        }

        /// <summary>Where the elevator thrusters go: from the data, or fore and aft on each side
        /// from the hull's size.</summary>
        static Vector2[] LiftSpots(Bounds hb)
        {
            if (fs.liftSpots != null && fs.liftSpots.Length > 0) return fs.liftSpots.Select(s => new Vector2(s.x, s.z)).ToArray();
            float x = hb.size.x * 0.28f, fore = hb.size.z * 0.32f, aft = -hb.size.z * 0.18f;
            return new[] { new Vector2(-x, fore), new Vector2(x, fore), new Vector2(-x, aft), new Vector2(x, aft) };
        }

        /// <summary>Where the CIWS mounts go (on the deck nearest these spots), with their names.</summary>
        static (string name, Vector2 at)[] CiwsSpots()
        {
            var spots = fs.ciws != null && fs.ciws.Length > 0 ? fs.ciws : mhp?.ciws ?? new XZ[0];
            int p = 0, s = 0, c = 0;
            return spots.Select(x =>
            {
                string side = x.x < -0.5f ? "P" + ++p : x.x > 0.5f ? "S" + ++s : "C" + ++c;
                return (side, new Vector2(x.x, x.z));
            }).ToArray();
        }

        /// <summary>The shield bubble's size: from the data, or round the hull's bounds.</summary>
        static Vector3 ShieldScale(Bounds hb)
        {
            if (fs.shieldScale != null && fs.shieldScale.Length == 3) return V3(fs.shieldScale);
            float r = Mathf.Max(hb.size.x, hb.size.y) * 0.87f;
            return new Vector3(r, r, hb.size.z * 0.6f);
        }
    }
}

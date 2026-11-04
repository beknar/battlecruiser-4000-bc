using System;
using System.Collections.Generic;
using System.Linq;
using CarrierGame;
using CarrierGame.Demo;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Carrier.EditorTools
{
    /// <summary>
    /// Enemy capital ships for the star system: a destroyer (the frigate's hull class, in pirate
    /// colours) and a dreadnought (the 700 m carrier hull). Both carry real triple turrets that
    /// track and fire on the frigate, missiles, and (the dreadnought) a heavy beam; they come
    /// apart in chunks when destroyed, turrets and all.
    /// </summary>
    public static partial class CarrierBuilder
    {
        /// <summary>A turret mount, in the hull model's own space.</summary>
        struct Mount { public Vector3 pos, facing; public Mount(Vector3 p, Vector3 f) { pos = p; facing = f; } }

        static void MakeCapitalShips(Prefabs p)
        {
            // destroyer: the frigate's hull at 85 %, two triple turrets on the centreline
            {
                var model = OwnModel("Frigate_Hull");
                p.enemyDestroyer = MakeCapital(p, "EnemyDestroyer", model, 0.85f, 2200f, 1.6f,
                    "Hostile Destroyer", "Destroyer", "High", "Twin triple turrets, missiles",
                    deck => new[]
                    {
                        new Mount(deck(0f, 48f), Vector3.forward),
                        new Mount(deck(0f, -30f), Vector3.back),
                    },
                    0.42f, fhp.engines, 1f,
                    (go, turrets) =>
                    {
                        foreach (var t in turrets) { t.fireInterval = 3.2f; t.range = 4200f; t.yawSpeed = 40f; t.pitchSpeed = 30f; }
                        var w = go.AddComponent<EnemyWeapons>();
                        w.turrets = turrets;
                        w.missilePrefab = p.missile;
                        w.missileRange = 5000f;
                        w.salvoInterval = 18f;
                        w.salvoSize = 2;
                        w.holdFire = 2f;
                    });
            }

            // dreadnought: the carrier hull, its eight main turret mounts armed, plus a heavy beam
            {
                var model = OwnModel("Carrier_Hull");
                p.enemyDreadnought = MakeCapital(p, "EnemyDreadnought", model, 1f, 18000f, 4f,
                    "Hostile Dreadnought", "Dreadnought", "Extreme", "Eight triple turrets, missiles, heavy beam",
                    _ => hp.main_turrets.Select(t => new Mount(V3(t.pos), V3(t.facing))).ToArray(),
                    1f, hp.engines, 1f,
                    (go, turrets) =>
                    {
                        foreach (var t in turrets) { t.fireInterval = 6.5f; t.range = 5500f; t.yawSpeed = 20f; t.pitchSpeed = 14f; }
                        var w = go.AddComponent<EnemyWeapons>();
                        w.turrets = turrets;
                        w.missilePrefab = p.missile;
                        w.missileRange = 6000f;
                        w.salvoInterval = 16f;
                        w.salvoSize = 4;
                        w.holdFire = 3f;
                        // a heavy red beam from the bridge tower
                        var b = LocalBounds(go.transform);
                        var node = Node("Beam", go.transform, new Vector3(0f, b.max.y + 6f, b.center.z + b.size.z * 0.1f));
                        var muzzle = Node("Muzzle", node.transform).transform;
                        var beam = node.AddComponent<MainBeam>();
                        beam.muzzle = muzzle;
                        beam.chargeTime = 5f;
                        beam.fireDuration = 2f;
                        beam.damagePerSecond = 320f;
                        beam.range = 6500f;
                        beam.radius = 6f;
                        beam.aimAssist = 120f;
                        beam.streamSpeed = 2600f;
                        BeamEffects(beam, node.transform, muzzle, new Color(1f, 0.22f, 0.15f), 1.4f);
                        w.beam = beam;
                        w.beamRange = 6000f;
                        w.beamInterval = 18f;
                    });
            }
        }

        /// <summary>Build a capital ship prefab: hull in pirate colours, turrets on its mounts, engine
        /// glow at its nozzles, breakable into chunks (turrets and engines fly off as parts).</summary>
        static GameObject MakeCapital(Prefabs p, string name, GameObject model, float scale, float health, float deathScale,
            string displayName, string shipClass, string threat, string weapons,
            Func<Func<float, float, Vector3>, Mount[]> mounts, float turretScale, Engine[] engines, float engineScale,
            Action<GameObject, CarrierTurret[]> arm)
        {
            var go = new GameObject(name);
            var m = Place(model, go.transform, "Model");
            m.transform.localScale = Vector3.one * scale;
            foreach (var r in m.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(_ => Mats["Enemy_Hull"]).ToArray();
            var b = LocalBounds(go.transform);
            m.transform.localPosition = -b.center;
            b = LocalBounds(go.transform);
            var col = go.AddComponent<BoxCollider>();
            col.center = b.center;
            col.size = b.size;

            // the deck height at a point (model space x, z), found by casting down onto the hull mesh
            var probes = m.GetComponentsInChildren<MeshFilter>().Select(mf => mf.gameObject.AddComponent<MeshCollider>()).ToList();
            Physics.SyncTransforms();
            Vector3 Deck(float x, float z)
            {
                Vector3 top = m.transform.TransformPoint(new Vector3(x, 1000f, z));
                var ray = new Ray(top, -go.transform.up);
                float best = float.MaxValue; Vector3 hitAt = Vector3.zero;
                foreach (var c in probes)
                    if (c.Raycast(ray, out var hit, 3000f) && hit.distance < best) { best = hit.distance; hitAt = hit.point; }
                if (best == float.MaxValue) { Log($"{name}: no deck at x {x} z {z}"); return new Vector3(x, 0f, z); }
                return m.transform.InverseTransformPoint(hitAt);
            }
            var spots = mounts(Deck);
            foreach (var c in probes) UnityEngine.Object.DestroyImmediate(c);

            var parts = new List<Transform>();
            var turrets = new List<CarrierTurret>();
            var turretNode = Node("Turrets", go.transform).transform;
            for (int i = 0; i < spots.Length; i++)
            {
                var t = Place(p.mainTurret.gameObject, turretNode, $"Turret{i + 1}");
                t.transform.localPosition = go.transform.InverseTransformPoint(m.transform.TransformPoint(spots[i].pos));
                var facing = go.transform.InverseTransformDirection(m.transform.TransformDirection(spots[i].facing));
                t.transform.localRotation = Quaternion.LookRotation(facing, Vector3.up);
                t.transform.localScale = Vector3.one * turretScale;
                foreach (var r in t.GetComponentsInChildren<Renderer>())
                    r.sharedMaterials = r.sharedMaterials.Select(_ => Mats["Enemy_Hull"]).ToArray();
                var tu = t.GetComponent<CarrierTurret>();
                tu.projectilePrefab = p.enemyCannon;
                tu.fireSound = Sfx.Cannon;
                tu.leadTarget = true;
                tu.fireCone = 4f;
                tu.yawLimit = 165f;
                tu.autoFire = true;
                tu.target = null;   // EnemyWeapons hands it the target once the ship is in the fight
                tu.label = $"{displayName} turret {i + 1}";
                EditorUtility.SetDirty(tu);
                turrets.Add(tu);
                parts.Add(t.transform);
            }

            // engine glow at the stern nozzles: the kit exhaust plume, tinted red, and a light
            var plumeModel = Model("Spaceship_Kit/SM_reactor_FX");
            var engineNode = Node("Engines", go.transform).transform;
            foreach (var (e, i) in engines.Select((e, i) => (e, i)))
            {
                Vector3 exit = go.transform.InverseTransformPoint(m.transform.TransformPoint(V3(e.exit)));
                Vector3 dir = go.transform.InverseTransformDirection(m.transform.TransformDirection(V3(e.dir)));
                float radius = e.radius * scale * engineScale;
                var eg = Node("Engine" + (i + 1), engineNode, exit, Quaternion.LookRotation(dir, Vector3.up));
                var wrap = Node("Plume", eg.transform).transform;
                if (plumeModel != null)
                {
                    var pm = Place(plumeModel, wrap, "PlumeMesh");
                    var (ax, len) = AlignLongAxis(pm.transform, Vector3.forward);
                    var pb = ParentBounds(pm.transform);
                    float width = Mathf.Max(0.1f, Mathf.Max(pb.size.x, pb.size.y));
                    Stretch(pm.transform, ax, len, radius * 6f, radius * 1.5f / width);
                    pb = ParentBounds(pm.transform);
                    pm.transform.localPosition += new Vector3(-pb.center.x, -pb.center.y, -1f - pb.min.z);
                    foreach (var r in pm.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
                }
                var l = Node("Glow", eg.transform, new Vector3(0, 0, radius * 3f)).AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.45f, 0.3f);
                l.range = radius * 14f;
                var et = eg.AddComponent<EngineThrust>();
                et.plume = wrap;
                et.glow = l;
                et.maxIntensity = 600f;
                et.throttle = 0.55f;
                parts.Add(eg.transform);
            }

            MakeBreakable(go, m, name, 16, true, parts.ToArray());
            var h = go.AddComponent<Health>();
            h.maxHealth = health;
            h.deathEffect = p.explosion;
            h.deathEffectScale = deathScale;
            h.respawnDelay = 60f;
            var info = go.AddComponent<TargetInfo>();
            info.displayName = displayName;
            info.shipClass = shipClass;
            info.threat = threat;
            info.weapons = weapons;
            info.kind = TargetInfo.Kind.Capital;
            arm(go, turrets.ToArray());
            Log($"{name}: size {b.size:F0} m, {turrets.Count} turrets, {engines.Length} engines");
            return SavePrefab(go, name);
        }
    }
}

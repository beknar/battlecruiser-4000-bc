using System;
using System.Collections.Generic;
using System.Linq;
using CarrierGame;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Carrier.EditorTools
{
    /// <summary>
    /// What has been built in the Kestrel system, each one a mini-warp destination:
    ///
    /// - Thalassa Highport, a space station: a 1.4 km habitat ring spinning round a docking hub.
    /// - Deepcut Station, a mining base dug into a rock at the edge of the Inner Belt, and
    ///   Corsair Haven, a pirate base in a rock at the edge of the Outer Drift, its gun
    ///   emplacements covering every approach.
    /// - The Cinder Orbital Battery: three gun platforms and a beam lance over Cinder's pole.
    /// - Brontes Shipyard (a drydock with a destroyer half-built in it) and Ochre Relay (a
    ///   communications mast with three big dishes).
    /// - At Lagrange points: a solar observatory at Thalassa's L1, between it and the star, and a
    ///   propellant depot at Jovara's L5, trailing it by 60 degrees (the Trojans are at L4).
    /// - Bases on the surfaces, seen from orbit: Selene Base (a colony) and Cinder Fortress (the
    ///   corsairs' stronghold). Landing from over one comes down beside it.
    ///
    /// The friendly station, mining base, depot and shipyard can be docked at. The hostile ones
    /// are defended (DefenseGrid): their guns are targets that can be destroyed, and come back.
    /// </summary>
    public static partial class CarrierBuilder
    {
        /// <summary>The places and bodies the installations are built round.</summary>
        class SystemRefs
        {
            public Transform sys, star;
            public GameObject cinder, ochre, thalassa, jovara;
            public WarpPoint wCinder, wThalassa, wSelene, wBrontes;
            public Vector3 innerC, outerC;
            public Material rockMat;
            public int layer;
            public SurfaceKit kit;
        }

        /// <summary>What the builder hands back: the hostile guns (to target) and review shots.</summary>
        class Built
        {
            public readonly List<Transform> targets = new List<Transform>();
            public readonly List<(string name, Vector3 from, Vector3 at)> shots = new List<(string, Vector3, Vector3)>();
        }

        static readonly Color FriendlyStation = new Color(0.55f, 0.85f, 1f);
        static readonly Color FriendlyWorks = new Color(0.7f, 0.92f, 0.78f);
        static readonly Color HostileWorks = new Color(1f, 0.42f, 0.36f);
        static readonly Color LagrangeColour = new Color(0.78f, 0.72f, 1f);

        static Built BuildInstallations(SystemRefs s, Prefabs p)
        {
            var b = new Built();
            // the prefabs made earlier in the build, in case anything has reloaded them since
            if (p.mainTurret == null) p.mainTurret = Load<CarrierTurret>(PrefabDir + "/TripleTurret.prefab");
            if (p.enemyCannon == null) p.enemyCannon = Load<Projectile>(PrefabDir + "/EnemyCannonShell.prefab");
            if (p.missile == null) p.missile = Load<Missile>(PrefabDir + "/Missile.prefab");
            if (p.explosion == null) p.explosion = Load<Explosion>(PrefabDir + "/Explosion.prefab");
            var root = new GameObject("Installations").transform;
            root.SetParent(s.sys, false);
            Physics.SyncTransforms();

            Highport(s, root, b);
            Deepcut(s, root, b);
            CorsairHaven(s, p, root, b);
            OrbitalBattery(s, p, root, b);
            Shipyard(s, root, b);
            Relay(s, root, b);
            Observatory(s, root, b);
            Depot(s, root, b);
            IndustrySites(s, root, b);
            Settlements(s, root, b);
            StrikeSatellites(s, p, root, b);
            // every place that recruits crew is a port (its screen opens when docked, or landed
            // on its pad, for the crew page)
            foreach (var inst in root.GetComponentsInChildren<Installation>(true).Concat(s.sys.GetComponentsInChildren<Installation>(true)).Distinct())
                if (!inst.hostile && CrewTable.Get().Place(inst.displayName)?.Recruits == true && inst.GetComponent<IndustrySite>() == null)
                    inst.gameObject.AddComponent<IndustrySite>().siteName = inst.displayName;
            SurfaceBaseInSpace(s, "Selene Base", s.wSelene, false, 8f, b,
                "A colony on Selene: pressure domes, habitats, a control tower and tanks round a landing pad. " +
                "From orbit over it, land (K) to come down beside it.");
            SurfaceBaseInSpace(s, "Cinder Fortress", s.wCinder, true, -30f, b,
                "The corsairs' fortress on Cinder: bunkers, a command post, radar and walls inside a ring of gun batteries " +
                "and missile sites, with fighters and a gunship on its pad. From orbit over it, land (K) to attack it from the ground.");

            foreach (var w in root.GetComponentsInChildren<WarpPoint>(true).Concat(s.cinder.GetComponentsInChildren<WarpPoint>(true))
                                  .Concat(s.wSelene.GetComponentsInChildren<WarpPoint>(true)).Where(w => w.Built).Distinct())
                Log($"installation {w.displayName,-24} {w.kind,-13} {(w.hostile ? "hostile" : "friendly"),-8} " +
                    $"{w.transform.position.magnitude / 1000f,6:F0} km from the star");
            return b;
        }

        static WarpPoint Destination(GameObject go, string name, WarpPoint.Kind kind, string desc, float size, float arrival, Color c,
                               WarpPoint parent = null, bool hostile = false, string label = "")
        {
            var wp = MakeWarpPoint(go, name, kind, desc, size, arrival, c, parent, hostile);
            wp.kindLabel = label;
            return wp;
        }

        static Installation Facility(GameObject go, string name, WarpPoint.Kind kind, bool hostile, Transform berth = null, bool market = false)
        {
            var i = go.AddComponent<Installation>();
            i.displayName = name;
            i.kind = kind;
            i.hostile = hostile;
            i.canDock = berth != null && !hostile;
            i.market = market;
            i.berth = berth;
            i.dockRange = 1600f;   // close in: from where the ship arrives, K lands on the body nearby
            return i;
        }

        static void Obstruct(GameObject go, float radius, float core, bool cover)
        {
            var ob = go.AddComponent<Obstacle>();
            ob.radius = radius;
            ob.coreRadius = core;
            ob.cover = cover;
        }

        /// <summary>A point `altitude` above a body's surface (centre `c`, radius `R`), toward `dir`.</summary>
        static Vector3 Above(Vector3 c, float R, Vector3 dir, float altitude) => c + dir.normalized * (R + altitude);

        static float RadiusOf(GameObject body) => body.GetComponent<WarpPoint>().bodyRadius;

        /// <summary>A berth: where a docked frigate lies (its forward is the way out).</summary>
        static Transform Berth(Transform parent, Vector3 pos, Vector3 forward)
        {
            var t = Node("Berth", parent, pos, Quaternion.LookRotation(forward, Vector3.up)).transform;
            return t;
        }

        // ------------------------------------------------------------------ Thalassa Highport

        static void Highport(SystemRefs s, Transform parent, Built b)
        {
            // at the top of the space elevator: in geostationary orbit over Thalassa's equator,
            // its docking arm (and the tether, on to the counterweight) pointing straight out
            Vector3 th = s.thalassa.transform.position, axis = s.thalassa.transform.up;
            Vector3 radial = Vector3.ProjectOnPlane(new Vector3(Mathf.Cos(150f * Mathf.Deg2Rad), 0f, Mathf.Sin(150f * Mathf.Deg2Rad)), axis).normalized;
            Vector3 pos = th + radial * ThalassaGeo;
            var go = new GameObject("Thalassa Highport");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(radial, axis));

            // the hub, its docking arm, solar wings and radiators (still)
            var hub = new Geo(go.transform, "Hub");
            hub.Cyl(sm.hull, new Vector3(0f, 0f, -230f), new Vector3(0f, 0f, 230f), 85f);
            hub.Ball(sm.hull, new Vector3(0f, 0f, -230f), new Vector3(85f, 85f, 40f), Quaternion.identity);
            hub.Ball(sm.dark, new Vector3(0f, 0f, 230f), new Vector3(85f, 85f, 30f), Quaternion.identity);
            foreach (float z in new[] { -170f, -100f, 150f, 200f }) hub.WindowBand(new Vector3(0f, 0f, z - 4f), new Vector3(0f, 0f, z + 4f), 85f);
            hub.Cyl(sm.dark, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 120f), 105f);     // the bearing the ring turns on
            hub.Cyl(sm.dark, new Vector3(0f, 0f, 230f), new Vector3(0f, 0f, 640f), 22f);    // docking arm
            hub.Truss(sm.hull, new Vector3(0f, 0f, 250f), new Vector3(0f, 0f, 620f), 60f, 3f);
            hub.Cyl(sm.hull, new Vector3(0f, 0f, 620f), new Vector3(0f, 0f, 660f), 42f);    // docking collar
            hub.Torus(sm.hazard, new Vector3(0f, 0f, 661f), Vector3.forward, 34f, 3f);
            foreach (float z in new[] { 360f, 540f })
            {
                hub.Box(sm.dark, new Vector3(55f, 0f, z), new Vector3(70f, 14f, 16f));       // mooring clamps toward the berth
                hub.Box(sm.hazard, new Vector3(91f, 0f, z), new Vector3(4f, 18f, 20f));
            }
            hub.Solar(new Vector3(85f, 0f, -190f), Vector3.right, Vector3.up, 540f, 120f);
            hub.Solar(new Vector3(-85f, 0f, -190f), Vector3.left, Vector3.up, 540f, 120f);
            hub.Radiator(new Vector3(0f, 85f, -110f), Vector3.up, Vector3.forward, 300f, 90f);
            hub.Radiator(new Vector3(0f, -85f, -110f), Vector3.down, Vector3.forward, 300f, 90f);
            hub.Dish(sm.white, new Vector3(0f, 95f, 210f), new Vector3(0f, 0.4f, 1f), 30f, 7f);
            hub.Rod(sm.dark, new Vector3(0f, 85f, 205f), new Vector3(0f, 95f, 210f), 3f);
            Bake(hub, "Highport_Hub", true, s.layer);

            // the habitat ring and its spokes (turning, for gravity)
            var ringNode = Node("Ring", go.transform, new Vector3(0f, 0f, 60f));
            var ring = new Geo(ringNode.transform, "Mesh");
            ring.Torus(sm.hull, Vector3.zero, Vector3.forward, 650f, 42f);
            ring.Torus(sm.window, Vector3.zero, Vector3.forward, 691f, 3.5f);
            ring.Torus(sm.window, new Vector3(0f, 0f, 30f), Vector3.forward, 670f, 2.5f);
            ring.Torus(sm.window, new Vector3(0f, 0f, -30f), Vector3.forward, 670f, 2.5f);
            for (int k = 0; k < 6; k++)
            {
                float a = k * 60f * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                ring.Cyl(sm.hull, d * 100f, d * 615f, 11f);
                ring.Box(sm.dark, d * 650f, new Vector3(110f, 110f, 96f), Quaternion.LookRotation(Vector3.forward, d));   // a module where each spoke meets the ring
                ring.Box(sm.window, d * 650f + Vector3.forward * 48.5f, new Vector3(80f, 20f, 1f), Quaternion.LookRotation(Vector3.forward, d));
            }
            Bake(ring, "Highport_Ring", true, s.layer);
            ringNode.AddComponent<Drift>().spin = new Vector3(0f, 0f, 1.2f);
            for (int k = 0; k < 6; k++)
            {
                float a = (k * 60f + 30f) * Mathf.Deg2Rad;
                NavLight(ringNode.transform, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 694f, k % 2 == 0 ? sm.red : sm.green, 9f, 2.4f, k * 0.15f);
            }
            NavLight(go.transform, new Vector3(0f, 0f, 668f), sm.green, 10f, 1.2f, 0f, 500f, new Color(0.3f, 1f, 0.4f));
            NavLight(go.transform, new Vector3(625f, 0f, -190f), sm.red, 8f, 2f, 0.5f);
            NavLight(go.transform, new Vector3(-625f, 0f, -190f), sm.green, 8f, 2f, 0.5f);
            Flood(go.transform, new Vector3(70f, 60f, 450f), new Color(1f, 0.9f, 0.75f), 600f, 9f);

            var berth = Berth(go.transform, new Vector3(150f, 0f, 450f), Vector3.forward);
            Facility(go, "Thalassa Highport", WarpPoint.Kind.Station, false, berth, true);
            Obstruct(go, 760f, 90f, false);
            var hw = Destination(go, "Thalassa Highport", WarpPoint.Kind.Station,
                "Thalassa's orbital port, at the top of its space elevator in geostationary orbit: a habitat ring 1.4 km across, " +
                "turning slowly round its docking hub. Dock here (K) for repairs and to sell your ore.", 700f, 3200f, FriendlyStation, s.wThalassa);
            // arrive off its berth side, level with it: from the near side, the way in could be from
            // the planet; along the hub it would be down the tether
            hw.overhead = true;
            hw.overheadAxis = Vector3.right;
            hw.overheadSide = 0.1f;
            b.shots.Add(("inst_highport", pos + go.transform.rotation * new Vector3(1500f, 500f, 1700f), pos));
            b.shots.Add(("inst_highport_berth", go.transform.TransformPoint(new Vector3(420f, 120f, 760f)), go.transform.TransformPoint(new Vector3(100f, 0f, 420f))));
            go.transform.SetParent(s.thalassa.transform, true);   // it turns with the planet
        }

        // ------------------------------------------------------------------ asteroid bases

        /// <summary>A big rock for a base (no minerals to mine: it's lived in).</summary>
        static GameObject BaseRock(SystemRefs s, Transform parent, string name, Vector3 pos, float radius, int seed)
        {
            var rock = MakeAsteroid(name + " (rock)", parent, pos, Quaternion.Euler(10f, seed % 360, 5f), 1f, seed, 5, 4,
                new Vector3(radius, radius * 0.85f, radius * 0.92f), 0.22f, 18, radius / 5f, s.rockMat, s.layer, radius * 1.15f, radius * 0.8f);
            Object.DestroyImmediate(rock.GetComponent<MineralDeposit>());
            return rock;
        }

        /// <summary>A part of a base, set on the rock's (or a body's) surface under a direction.</summary>
        static Transform OnSurface(Transform parent, Transform body, Vector3 dir, float radiusGuess, string name, Vector3? forwardHint = null, float sink = 3f)
        {
            dir.Normalize();
            Vector3 fwd = Vector3.ProjectOnPlane(forwardHint ?? Vector3.forward, dir);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.right, dir);
            var t = Node(name, parent).transform;
            t.SetPositionAndRotation(body.position + dir * radiusGuess, Quaternion.LookRotation(fwd.normalized, dir));
            var a = t.gameObject.AddComponent<SurfaceAnchor>();
            a.body = body;
            a.sink = sink;
            a.Settle();
            return t;
        }

        static void Deepcut(SystemRefs s, Transform parent, Built b)
        {
            Vector3 out_ = s.innerC.normalized;
            Vector3 pos = s.innerC + out_ * 7500f + Vector3.up * 300f;
            var go = new GameObject("Deepcut Station");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var rock = BaseRock(s, go.transform, "Deepcut", pos, 620f, 4401);
            Physics.SyncTransforms();
            var body = rock.transform;
            Vector3 toStar = -out_;

            // the refinery on top: towers, a processing hall, tanks
            var top = OnSurface(go.transform, body, Vector3.up, 600f, "Refinery");
            var g = new Geo(top, "Mesh");
            g.Box(sm.hull, new Vector3(0f, 20f, 0f), new Vector3(150f, 40f, 80f));
            g.Box(sm.window, new Vector3(0f, 26f, 40.4f), new Vector3(140f, 6f, 1f));
            g.Box(sm.window, new Vector3(0f, 26f, -40.4f), new Vector3(140f, 6f, 1f));
            foreach (float x in new[] { -50f, -10f, 30f })
            {
                g.Cyl(sm.dark, new Vector3(x, 40f, 10f), new Vector3(x, 150f, 10f), 14f);
                g.Torus(sm.hazard, new Vector3(x, 140f, 10f), Vector3.up, 14.5f, 1.5f);
            }
            foreach (var (x, z) in new[] { (75f, 60f), (110f, 20f), (95f, -40f), (55f, -75f) })
                g.Ball(sm.white, new Vector3(x, 30f, z), 30f);
            g.Cyl(sm.dark, new Vector3(60f, 20f, 0f), new Vector3(110f, 20f, 20f), 4f);
            g.Truss(sm.dark, new Vector3(-75f, 20f, 0f), new Vector3(-180f, 60f, -30f), 10f, 1.2f);   // the ore conveyor
            Bake(g, "Deepcut_Refinery", true, s.layer);
            NavLight(top, new Vector3(-50f, 152f, 10f), sm.red, 6f, 1.6f, 0f);
            NavLight(top, new Vector3(30f, 152f, 10f), sm.red, 6f, 1.6f, 0.5f);
            Flood(top, new Vector3(0f, 90f, 90f), new Color(1f, 0.85f, 0.6f), 450f, 7f);

            // the docking pier, sticking out toward the inner system
            var pierDir = (toStar + Vector3.up * 0.15f).normalized;
            var pier = OnSurface(go.transform, body, pierDir, 600f, "Pier", Vector3.up);
            var pg = new Geo(pier, "Mesh");
            pg.Cyl(sm.hull, new Vector3(0f, -10f, 0f), new Vector3(0f, 30f, 0f), 45f);
            pg.Truss(sm.hull, new Vector3(0f, 20f, 0f), new Vector3(0f, 420f, 0f), 34f, 2.4f);
            pg.Cyl(sm.dark, new Vector3(0f, 20f, 0f), new Vector3(0f, 430f, 0f), 9f);
            pg.Cyl(sm.hull, new Vector3(0f, 410f, 0f), new Vector3(0f, 440f, 0f), 30f);
            foreach (float y in new[] { 180f, 330f })
            {
                pg.Box(sm.dark, new Vector3(50f, y, 0f), new Vector3(70f, 12f, 12f));
                pg.Box(sm.hazard, new Vector3(86f, y, 0f), new Vector3(4f, 16f, 16f));
            }
            Bake(pg, "Deepcut_Pier", true, s.layer);
            NavLight(pier, new Vector3(0f, 446f, 0f), sm.green, 8f, 1.2f, 0f, 400f, new Color(0.3f, 1f, 0.4f));
            // the frigate lies alongside the pier, nose out
            var berth = Node("Berth", pier, new Vector3(140f, 270f, 0f)).transform;
            berth.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);

            // the mass driver: a long rail of coils, flinging ore toward the inner planets
            var mdDir = (Vector3.Cross(Vector3.up, out_).normalized * 0.8f + Vector3.up * 0.2f).normalized;
            var md = OnSurface(go.transform, body, mdDir, 600f, "Mass driver", toStar);
            var mg = new Geo(md, "Mesh");
            mg.Box(sm.dark, new Vector3(0f, 10f, 0f), new Vector3(60f, 20f, 60f));
            mg.Truss(sm.dark, new Vector3(0f, 20f, 0f), new Vector3(0f, 120f, 600f), 16f, 1.6f);
            for (int k = 1; k <= 8; k++) mg.Torus(sm.hull, Vector3.Lerp(new Vector3(0f, 20f, 0f), new Vector3(0f, 120f, 600f), k / 8.5f), new Vector3(0f, 100f, 580f).normalized, 18f, 3f);
            Bake(mg, "Deepcut_MassDriver", true, s.layer);

            // living quarters: domes on the far side
            var hab = OnSurface(go.transform, body, (-toStar + Vector3.up * 0.4f).normalized, 600f, "Quarters");
            var hg = new Geo(hab, "Mesh");
            foreach (var (x, z, r) in new[] { (0f, 0f, 46f), (80f, 30f, 32f), (-70f, 40f, 28f) })
            {
                hg.Cyl(sm.hull, new Vector3(x, -6f, z), new Vector3(x, 6f, z), r + 3f);
                hg.Ball(sm.glass, new Vector3(x, 4f, z), new Vector3(r, r * 0.75f, r), Quaternion.identity);
                hg.Torus(sm.window, new Vector3(x, 6f, z), Vector3.up, r + 2f, 1.5f);
            }
            hg.Cyl(sm.hull, new Vector3(0f, 6f, 0f), new Vector3(80f, 6f, 30f), 5f);
            hg.Cyl(sm.hull, new Vector3(0f, 6f, 0f), new Vector3(-70f, 6f, 40f), 5f);
            Bake(hg, "Deepcut_Quarters", true, s.layer);

            Facility(go, "Deepcut Station", WarpPoint.Kind.AsteroidBase, false, berth, true);
            Destination(go, "Deepcut Station", WarpPoint.Kind.AsteroidBase,
                "A mining station dug into a big rock at the edge of the Inner Belt: refinery towers, ore tanks, " +
                "and a mass driver flinging ore toward the inner planets. It buys ore. Dock here (K) for repairs and to sell.",
                700f, 3200f, FriendlyWorks);
            b.shots.Add(("inst_deepcut", berth.position + toStar * 1600f + Vector3.up * 600f, pos));
        }

        static void CorsairHaven(SystemRefs s, Prefabs p, Transform parent, Built b)
        {
            Vector3 inward = -new Vector3(s.outerC.x, 0f, s.outerC.z).normalized;
            Vector3 pos = s.outerC + inward * 9500f + Vector3.up * 600f;
            var go = new GameObject("Corsair Haven");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var rock = BaseRock(s, go.transform, "Corsair Haven", pos, 700f, 4502);
            Physics.SyncTransforms();
            var body = rock.transform;

            // the hangar mouth, facing the inner system, glowing red
            var hangar = OnSurface(go.transform, body, inward, 680f, "Hangar", Vector3.up);
            var g = new Geo(hangar, "Mesh");
            g.Box(sm.pirate, new Vector3(0f, 20f, 0f), new Vector3(220f, 40f, 140f));
            g.Box(sm.dark, new Vector3(0f, 41f, 0f), new Vector3(170f, 2f, 90f));
            g.Box(sm.red, new Vector3(0f, 41.4f, -46f), new Vector3(170f, 1.5f, 3f));
            g.Box(sm.red, new Vector3(0f, 41.4f, 46f), new Vector3(170f, 1.5f, 3f));
            foreach (float x in new[] { -90f, 90f }) g.Box(sm.pirate, new Vector3(x, 60f, 0f), new Vector3(30f, 40f, 140f));
            Bake(g, "Haven_Hangar", true, s.layer);
            Flood(hangar, new Vector3(0f, 70f, 0f), new Color(1f, 0.3f, 0.2f), 400f, 10f);

            // habitat stacks and antenna masts, pirate colours
            var stack = OnSurface(go.transform, body, (Vector3.up + inward * 0.4f).normalized, 600f, "Quarters");
            var sg = new Geo(stack, "Mesh");
            sg.Box(sm.pirate, new Vector3(0f, 25f, 0f), new Vector3(70f, 50f, 50f));
            sg.Box(sm.dark, new Vector3(40f, 15f, 30f), new Vector3(50f, 30f, 40f));
            sg.Box(sm.window, new Vector3(0f, 35f, 25.4f), new Vector3(60f, 5f, 1f));
            sg.Ball(sm.dark, new Vector3(-55f, 20f, -10f), 22f);
            sg.Truss(sm.dark, new Vector3(-20f, 50f, -10f), new Vector3(-20f, 210f, -10f), 8f, 1f);
            sg.Truss(sm.dark, new Vector3(25f, 30f, 30f), new Vector3(25f, 140f, 30f), 6f, 0.8f);
            Bake(sg, "Haven_Quarters", true, s.layer);
            NavLight(stack, new Vector3(-20f, 214f, -10f), sm.red, 6f, 1.3f, 0f);
            NavLight(stack, new Vector3(25f, 143f, 30f), sm.red, 5f, 1.3f, 0.5f);

            // six gun emplacements round the rock (two with missiles)
            var guns = new List<EnemyWeapons>();
            Vector3 side = Vector3.Cross(Vector3.up, inward).normalized;
            var dirs = new[]
            {
                (inward + side * 0.6f + Vector3.up * 0.3f), (inward - side * 0.6f + Vector3.up * 0.3f), (inward + Vector3.down * 0.7f),
                (side + Vector3.up * 0.5f), (-side + Vector3.up * 0.5f), (-inward + Vector3.up * 0.6f),
            };
            for (int i = 0; i < dirs.Length; i++)
            {
                string name = $"Haven Gun {(char)('A' + i)}";
                // the emplacement's own point (what is aimed at, and what it sees from) stands up
                // above the turret, clear of the rock round it; the bunker goes down into the ground
                var unit = OnSurface(go.transform, body, dirs[i], 680f, name, Vector3.up, -16f);
                var ug = new Geo(unit, "Mesh");
                ug.Cyl(sm.pirate, new Vector3(0f, -22f, 0f), new Vector3(0f, -6f, 0f), 22f);
                ug.Torus(sm.dark, new Vector3(0f, -6f, 0f), Vector3.up, 21f, 2.5f);
                if (i < 2) { ug.Box(sm.dark, new Vector3(20f, -2f, -10f), new Vector3(10f, 10f, 18f)); ug.Box(sm.dark, new Vector3(-20f, -2f, -10f), new Vector3(10f, 10f, 18f)); }
                Bake(ug, name.Replace(' ', '_'), false, s.layer);
                var t = Place(p.mainTurret.gameObject, unit, "Turret");
                t.transform.localPosition = new Vector3(0f, -6f, 0f);
                t.transform.localScale = Vector3.one * 0.62f;
                Paint(t, Mats["Enemy_Hull"]);
                var tu = HostileTurret(t.GetComponent<CarrierTurret>(), p, name, 4800f, 3.6f);
                var col = unit.gameObject.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, -6f, 0f);
                col.size = new Vector3(46f, 34f, 46f);
                var w = Defence(unit.gameObject, p, name, "Gun emplacement", "Medium", i < 2 ? "Triple turret, missiles" : "Triple turret",
                    1100f, 0.7f, 120f, new[] { tu });
                if (i < 2) { w.missilePrefab = p.missile; w.missileRange = 5500f; w.salvoInterval = 18f; w.salvoSize = 2; }
                guns.Add(w);
                b.targets.Add(unit);
            }
            var grid = go.AddComponent<DefenseGrid>();
            grid.gridName = "Corsair Haven";
            grid.weapons = guns.ToArray();
            grid.engageRange = 5200f;
            grid.disengageRange = 8500f;

            Facility(go, "Corsair Haven", WarpPoint.Kind.AsteroidBase, true);
            Destination(go, "Corsair Haven", WarpPoint.Kind.AsteroidBase,
                "A pirate base hollowed out of an asteroid at the edge of the Outer Drift, its hangar mouth glowing red. " +
                "Six gun emplacements cover every approach, and open fire inside 5 km.", 750f, 8000f, HostileWorks, null, true);
            b.shots.Add(("inst_haven", pos + inward * 2600f + side * 900f + Vector3.up * 700f, pos));
        }

        /// <summary>A turret set up to defend a hostile installation (fires the enemy cannon,
        /// turns all the way round, gets its target from EnemyWeapons when the grid is firing).</summary>
        static CarrierTurret HostileTurret(CarrierTurret tu, Prefabs p, string label, float range, float interval)
        {
            tu.projectilePrefab = p.enemyCannon;
            tu.fireSound = Sfx.Cannon;
            tu.leadTarget = true;
            tu.fireCone = 4f;
            tu.yawLimit = 180f;
            tu.autoFire = true;
            tu.target = null;
            tu.range = range;
            tu.fireInterval = interval;
            tu.yawSpeed = 30f;
            tu.pitchSpeed = 22f;
            tu.label = label;
            EditorUtility.SetDirty(tu);
            return tu;
        }

        /// <summary>Hull, target info and weapons for a gun or platform of a hostile installation
        /// (cold until its DefenseGrid opens fire).</summary>
        static EnemyWeapons Defence(GameObject go, Prefabs p, string display, string cls, string threat, string weapons,
                                    float health, float blast, float respawn, CarrierTurret[] turrets)
        {
            if (!go.TryGetComponent(out Health h)) h = go.AddComponent<Health>();
            h.maxHealth = health;
            h.deathEffect = p.explosion;
            h.deathEffectScale = blast;
            h.respawnDelay = respawn;
            if (!go.TryGetComponent(out TargetInfo info)) info = go.AddComponent<TargetInfo>();
            info.displayName = display;
            info.shipClass = cls;
            info.threat = threat;
            info.weapons = weapons;
            info.kind = TargetInfo.Kind.Capital;
            if (!go.TryGetComponent(out EnemyWeapons w)) w = go.AddComponent<EnemyWeapons>();
            w.turrets = turrets;
            w.holdFire = 0f;
            w.fireArc = 180f;
            w.weaponsFree = false;
            return w;
        }

        // ------------------------------------------------------------------ orbital weapons

        /// <summary>A gun platform (three triple turrets, missiles) or the beam lance (a heavy beam
        /// and two turrets): pirate-armoured, breaking up when destroyed.</summary>
        static GameObject OrbitalPrefab(Prefabs p, bool lance)
        {
            string name = lance ? "OrbitalLance" : "OrbitalPlatform";
            var go = new GameObject(name);
            var g = new Geo(go.transform, "Model");
            var mounts = new List<(Vector3 pos, Vector3 facing)>();
            var lights = new List<Vector3>();
            var hull = Mats["Enemy_Hull"];
            if (!lance)
            {
                g.Cyl(hull, new Vector3(0f, -14f, 0f), new Vector3(0f, 14f, 0f), 48f);
                g.Disc(sm.dark, new Vector3(0f, 14.6f, 0f), Vector3.up, 44f, 1.2f);
                g.Ball(hull, new Vector3(0f, -14f, 0f), new Vector3(40f, 20f, 40f), Quaternion.identity);
                g.Torus(sm.dark, Vector3.zero, Vector3.up, 49f, 5f);
                for (int k = 0; k < 3; k++)
                {
                    float a = (k * 120f + 30f) * Mathf.Deg2Rad;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    g.Box(hull, d * 64f, new Vector3(40f, 22f, 26f), Quaternion.LookRotation(d));
                    g.Box(sm.dark, d * 80f + Vector3.up * 5f, new Vector3(16f, 12f, 8f), Quaternion.LookRotation(d));   // missile cells
                    lights.Add(d * 86f + Vector3.up * 13f);
                    float m = (k * 120f + 90f) * Mathf.Deg2Rad;
                    Vector3 md = new Vector3(Mathf.Cos(m), 0f, Mathf.Sin(m));
                    mounts.Add((md * 26f + Vector3.up * 15f, md));
                }
                g.Rod(sm.dark, new Vector3(0f, 14f, 0f), new Vector3(0f, 52f, 0f), 2f);
                g.Dish(sm.white, new Vector3(0f, 48f, 2f), new Vector3(0f, 0.3f, 1f), 8f, 2f);
            }
            else
            {
                g.Cyl(sm.dark, new Vector3(0f, 0f, -160f), new Vector3(0f, 0f, 150f), 16f);
                for (int k = 0; k < 6; k++) g.Torus(hull, new Vector3(0f, 0f, -100f + k * 38f), Vector3.forward, 27f, 6f);
                g.Cyl(hull, new Vector3(0f, 0f, 150f), new Vector3(0f, 0f, 176f), 24f);
                g.Ball(sm.red, new Vector3(0f, 0f, 178f), 9f);
                g.Box(hull, new Vector3(0f, 0f, -150f), new Vector3(70f, 46f, 80f));
                g.Box(sm.white, new Vector3(0f, 52f, -160f), new Vector3(4f, 60f, 70f));
                g.Box(sm.white, new Vector3(0f, -52f, -160f), new Vector3(4f, 60f, 70f));
                mounts.Add((new Vector3(0f, 23f, -130f), Vector3.forward));
                mounts.Add((new Vector3(0f, 23f, -175f), Vector3.back));
                lights.Add(new Vector3(36f, 0f, -150f));
                lights.Add(new Vector3(-36f, 0f, -150f));
            }
            var bounds = LocalBounds(go.transform);
            var col = go.AddComponent<BoxCollider>();
            col.center = bounds.center;
            col.size = bounds.size;

            var parts = new List<Transform>();
            var turrets = new List<CarrierTurret>();
            var tn = Node("Turrets", go.transform).transform;
            for (int i = 0; i < mounts.Count; i++)
            {
                var t = Place(p.mainTurret.gameObject, tn, $"Turret{i + 1}");
                t.transform.localPosition = mounts[i].pos;
                t.transform.localRotation = Quaternion.LookRotation(mounts[i].facing, Vector3.up);
                t.transform.localScale = Vector3.one * 0.55f;
                Paint(t, hull);
                turrets.Add(HostileTurret(t.GetComponent<CarrierTurret>(), p, $"{name} turret {i + 1}", 5200f, 3.4f));
                parts.Add(t.transform);
            }
            foreach (var l in lights) parts.Add(NavLight(go.transform, l, sm.red, 4f, 1.5f, l.x * 0.01f).transform);
            MakeBreakable(go, g.t.gameObject, name, lance ? 12 : 10, true, parts.ToArray());

            var w = Defence(go, p, lance ? "Beam Lance" : "Battery Platform", lance ? "Orbital beam lance" : "Orbital weapons platform",
                lance ? "Extreme" : "High", lance ? "Heavy beam, two triple turrets" : "Three triple turrets, missiles",
                lance ? 4200f : 2600f, lance ? 1.6f : 1.3f, 150f, turrets.ToArray());
            if (!lance)
            {
                w.missilePrefab = p.missile;
                w.missileRange = 6000f;
                w.salvoInterval = 16f;
                w.salvoSize = 3;
            }
            else
            {
                var node = Node("Beam", go.transform, new Vector3(0f, 0f, 182f));
                var muzzle = Node("Muzzle", node.transform).transform;
                var beam = node.AddComponent<MainBeam>();
                beam.muzzle = muzzle;
                beam.chargeTime = 5f;
                beam.fireDuration = 2.2f;
                beam.damagePerSecond = 280f;
                beam.range = 7500f;
                beam.radius = 6f;
                beam.aimAssist = 120f;
                beam.streamSpeed = 2600f;
                BeamEffects(beam, node.transform, muzzle, new Color(1f, 0.25f, 0.15f), 1.4f);
                w.beam = beam;
                w.beamRange = 7000f;
                w.beamInterval = 20f;
            }
            Log($"{name}: size {bounds.size:F0} m, {turrets.Count} turrets");
            return SavePrefab(go, name);
        }

        static void OrbitalBattery(SystemRefs s, Prefabs p, Transform parent, Built b)
        {
            var platform = OrbitalPrefab(p, false);
            var lance = OrbitalPrefab(p, true);
            Vector3 c = Above(s.cinder.transform.position, RadiusOf(s.cinder), Vector3.up, 120000f);   // (over the pole, 120 km up)
            Vector3 toThalassa = s.thalassa.transform.position - c; toThalassa.y = 0f; toThalassa.Normalize();
            var go = new GameObject("Cinder Orbital Battery");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(c, Quaternion.LookRotation(toThalassa, Vector3.up));
            var weapons = new List<EnemyWeapons>();
            for (int k = 0; k < 3; k++)
            {
                float a = (k * 120f) * Mathf.Deg2Rad;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(platform);
                inst.name = $"Battery Platform {(char)('A' + k)}";
                inst.transform.SetParent(go.transform, false);
                inst.transform.localPosition = new Vector3(Mathf.Cos(a), 0.05f * k, Mathf.Sin(a)) * 1150f;
                inst.transform.localRotation = Quaternion.Euler(0f, 90f - k * 120f, 0f);
                inst.GetComponent<TargetInfo>().displayName = inst.name;
                weapons.Add(inst.GetComponent<EnemyWeapons>());
                b.targets.Add(inst.transform);
            }
            var li = (GameObject)PrefabUtility.InstantiatePrefab(lance);
            li.name = "Cinder Lance";
            li.transform.SetParent(go.transform, false);
            li.transform.localPosition = new Vector3(0f, 120f, 0f);
            li.transform.localRotation = Quaternion.identity;
            li.GetComponent<TargetInfo>().displayName = "Cinder Lance";
            weapons.Add(li.GetComponent<EnemyWeapons>());
            b.targets.Add(li.transform);
            // the command node in the middle, and the trusses tying the platforms to it
            var g = new Geo(go.transform, "Frame");
            g.Ball(sm.pirate, new Vector3(0f, -40f, 0f), 40f);
            for (int k = 0; k < 3; k++)
            {
                float a = (k * 120f) * Mathf.Deg2Rad;
                g.Truss(sm.dark, new Vector3(0f, -40f, 0f), new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1080f + Vector3.up * (0.05f * k * 1150f), 10f, 1.2f);
            }
            Bake(g, "Battery_Frame", true, s.layer);
            var grid = go.AddComponent<DefenseGrid>();
            grid.gridName = "Cinder Orbital Battery";
            grid.weapons = weapons.ToArray();
            grid.engageRange = 6500f;
            grid.disengageRange = 10000f;
            Facility(go, "Cinder Orbital Battery", WarpPoint.Kind.Weapons, true);
            Destination(go, "Cinder Orbital Battery", WarpPoint.Kind.Weapons,
                "Corsair weapons platforms over Cinder's north pole: three gun platforms and a beam lance, tied to a command node. " +
                "They open fire on anything within 6.5 km.", 1300f, 8600f, HostileWorks, s.wCinder, true);
            b.shots.Add(("inst_battery", c + toThalassa * 3200f + Vector3.up * 900f, c));
        }

        // ------------------------------------------------------------------ strike satellites

        /// <summary>Corsair strike satellites, each hanging over a settlement and turning with its
        /// body, firing on it every so often (StrikeSatellite): an ion cannon (the beam lance) over
        /// Port Ochre, a mass driver (a gun platform) over Pyrois Domes. Each is a hostile
        /// destination that fights back, and can be destroyed (the corsairs put it back in half
        /// an hour).</summary>
        static void StrikeSatellites(SystemRefs s, Prefabs p, Transform parent, Built b)
        {
            var lance = OrbitalPrefab(p, true);
            var platform = OrbitalPrefab(p, false);
            void One(string name, string target, bool ion, float altitude, float minutes, string desc)
            {
                var tw = s.sys.GetComponentsInChildren<WarpPoint>(true).FirstOrDefault(w => w.displayName == target);
                if (tw == null || tw.landsOn == null) { Log($"strike satellite {name}: no {target} to hang over"); return; }
                var body = tw.landsOn.transform;
                Vector3 radial = (tw.Position - body.position).normalized;
                Vector3 along = Vector3.Cross(radial, Vector3.up);
                var f = BodyFrame(body, name, radial, along.sqrMagnitude > 0.01f ? along : Vector3.right);
                Object.DestroyImmediate(f.GetComponent<RaiseToGround>());   // (it hangs in space, not on the ground)
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(ion ? lance : platform);
                inst.name = name;
                inst.transform.SetParent(f, false);
                inst.transform.localPosition = new Vector3(0f, tw.landsOn.bodyRadius + altitude, 0f);
                inst.transform.localRotation = Quaternion.identity;
                inst.GetComponent<TargetInfo>().displayName = name;
                inst.GetComponent<Health>().respawnDelay = 1800f;
                var sat = inst.AddComponent<CarrierGame.Demo.StrikeSatellite>();
                sat.weapon = ion ? CarrierGame.Demo.StrikeSatellite.Weapon.IonCannon : CarrierGame.Demo.StrikeSatellite.Weapon.MassDriver;
                sat.target = target;
                sat.intervalMinutes = minutes;
                var grid = inst.AddComponent<DefenseGrid>();
                grid.gridName = name;
                grid.weapons = new[] { inst.GetComponent<EnemyWeapons>() };
                grid.engageRange = 6000f;
                grid.disengageRange = 9500f;
                Facility(inst, name, WarpPoint.Kind.Weapons, true);
                Destination(inst, name, WarpPoint.Kind.Weapons, desc, 300f, 7000f, HostileWorks, tw.landsOn, true);
                b.targets.Add(inst.transform);
                Log($"strike satellite {name}: over {target} on {tw.landsOn.displayName}, {altitude / 1000f:F0} km up, every {minutes:F0} min");
            }
            One("Corsair Ion Satellite", "Port Ochre", true, 250000f, 30f,
                "A corsair strike satellite hanging 250 km over Port Ochre: an ion cannon that burns into the city every half hour, " +
                "ruining buildings and killing its people. It fights back with its beam and two turrets. Destroy it to stop the strikes " +
                "(the corsairs put it back in half an hour).");
            One("Corsair Mass Driver", "Pyrois Domes", false, 120000f, 20f,
                "A corsair mass driver hanging 120 km over Pyrois Domes: every twenty minutes it fires a salvo of slugs down onto the domes. " +
                "A gun platform with three triple turrets and missiles. Destroy it to stop the strikes (the corsairs put it back in half an hour).");
        }

        // ------------------------------------------------------------------ installations

        static void Shipyard(SystemRefs s, Transform parent, Built b)
        {
            Vector3 br = s.wBrontes.Position, jv = s.jovara.transform.position;
            Vector3 radial = (br - jv); radial.y = 0f; radial.Normalize();
            Vector3 pos = Above(br, s.wBrontes.bodyRadius, Quaternion.Euler(0f, 90f, 0f) * radial * 7500f + Vector3.up * 600f, 80000f);
            var go = new GameObject("Brontes Shipyard");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(radial, Vector3.up));
            var g = new Geo(go.transform, "Drydock");
            const float L = 520f, W = 85f, H = 75f;
            foreach (var (x, y) in new[] { (-W, -H), (W, -H), (W, H), (-W, H) })
                g.Box(sm.dark, new Vector3(x, y, 0f), new Vector3(10f, 10f, L));
            for (int k = 0; k <= 8; k++)
            {
                float z = -L * 0.5f + L * k / 8f;
                g.Box(sm.dark, new Vector3(0f, H, z), new Vector3(2f * W, 8f, 8f));
                g.Box(sm.dark, new Vector3(0f, -H, z), new Vector3(2f * W, 8f, 8f));
                g.Box(sm.dark, new Vector3(W, 0f, z), new Vector3(8f, 2f * H, 8f));
                g.Box(sm.dark, new Vector3(-W, 0f, z), new Vector3(8f, 2f * H, 8f));
                g.Box(sm.work, new Vector3(W - 6f, H - 6f, z), new Vector3(3f, 3f, 3f));
                g.Box(sm.work, new Vector3(-W + 6f, H - 6f, z), new Vector3(3f, 3f, 3f));
            }
            foreach (float z in new[] { -90f, 120f })   // gantry cranes
            {
                g.Box(sm.hazard, new Vector3(0f, H + 12f, z), new Vector3(2f * W + 20f, 10f, 14f));
                g.Box(sm.hazard, new Vector3(20f, H - 6f, z), new Vector3(12f, 26f, 12f));
            }
            g.Cyl(sm.hull, new Vector3(0f, 0f, -L * 0.5f - 10f), new Vector3(0f, 0f, -L * 0.5f - 140f), 32f);   // the yard's quarters
            g.WindowBand(new Vector3(0f, 0f, -L * 0.5f - 60f), new Vector3(0f, 0f, -L * 0.5f - 72f), 32f);
            g.Solar(new Vector3(32f, 0f, -L * 0.5f - 110f), Vector3.right, Vector3.up, 300f, 90f);
            g.Solar(new Vector3(-32f, 0f, -L * 0.5f - 110f), Vector3.left, Vector3.up, 300f, 90f);
            // a destroyer, half built
            var hullModel = Place(OwnModel("Frigate_Hull"), g.t, "Hull under construction");
            hullModel.transform.localScale = Vector3.one * 1.45f;
            foreach (var r in hullModel.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select((_, i) => i % 2 == 0 ? sm.hull : sm.dark).ToArray();
            var hb = ParentBounds(hullModel.transform);
            hullModel.transform.localPosition -= hb.center;
            PrefabUtility.UnpackPrefabInstance(hullModel, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Bake(g, "Shipyard_Drydock", true, s.layer);
            Flood(go.transform, new Vector3(0f, H - 20f, 0f), new Color(1f, 0.9f, 0.75f), 400f, 10f);
            NavLight(go.transform, new Vector3(W, H, L * 0.5f), sm.green, 7f, 1.5f, 0f);
            NavLight(go.transform, new Vector3(-W, H, L * 0.5f), sm.red, 7f, 1.5f, 0f);
            var berth = Berth(go.transform, new Vector3(W + 110f, 0f, 60f), Vector3.forward);
            Facility(go, "Brontes Shipyard", WarpPoint.Kind.Installation, false, berth, false);
            Obstruct(go, 300f, 60f, false);
            Destination(go, "Brontes Shipyard", WarpPoint.Kind.Installation,
                "A drydock in orbit of Brontes, Jovara's inner moon, with a destroyer half built inside its gantries. " +
                "Dock here (K) for repairs (no market).", 300f, 3000f, FriendlyWorks, s.wBrontes, false, "Shipyard");
            b.shots.Add(("inst_shipyard", go.transform.TransformPoint(new Vector3(600f, 260f, 520f)), pos));
        }

        static void Relay(SystemRefs s, Transform parent, Built b)
        {
            Vector3 oc = s.ochre.transform.position;
            Vector3 toStar = (-oc); toStar.y = 0f; toStar.Normalize();
            Vector3 pos = Above(oc, RadiusOf(s.ochre), Quaternion.Euler(0f, -60f, 0f) * toStar * 10500f + Vector3.up * 1500f, 150000f);
            var go = new GameObject("Ochre Relay");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(toStar, Vector3.up));
            var g = new Geo(go.transform, "Mast");
            g.Truss(sm.hull, new Vector3(0f, -200f, 0f), new Vector3(0f, 220f, 0f), 24f, 2.4f);
            g.Cyl(sm.hull, new Vector3(0f, -260f, 0f), new Vector3(0f, -200f, 0f), 30f);
            g.WindowBand(new Vector3(0f, -240f, 0f), new Vector3(0f, -232f, 0f), 30f);
            void DishOn(Vector3 at, Vector3 facing, float r)
            {
                Vector3 c = at + facing.normalized * 30f;
                g.Rod(sm.dark, at, c, 4f);
                g.Dish(sm.white, c, facing, r, r * 0.22f);
                g.Rod(sm.dark, c, c + facing.normalized * r * 0.7f, 1.6f);
                g.Ball(sm.dark, c + facing.normalized * r * 0.7f, 3f);
            }
            DishOn(new Vector3(0f, 200f, 0f), new Vector3(0f, 0.3f, 1f), 70f);        // toward the inner system
            DishOn(new Vector3(0f, 60f, 0f), new Vector3(0.9f, 0.1f, -0.4f), 55f);
            DishOn(new Vector3(0f, -80f, 0f), new Vector3(-0.8f, -0.1f, -0.6f), 45f);
            g.Solar(new Vector3(12f, -20f, 0f), Vector3.right, Vector3.forward, 280f, 80f);
            g.Solar(new Vector3(-12f, -20f, 0f), Vector3.left, Vector3.forward, 280f, 80f);
            g.Radiator(new Vector3(0f, -150f, 12f), Vector3.forward, Vector3.right, 120f, 50f);
            Bake(g, "Relay_Mast", true, s.layer);
            NavLight(go.transform, new Vector3(0f, 226f, 0f), sm.red, 7f, 1.8f, 0f);
            NavLight(go.transform, new Vector3(292f, -20f, 0f), sm.red, 6f, 1.8f, 0.4f);
            NavLight(go.transform, new Vector3(-292f, -20f, 0f), sm.green, 6f, 1.8f, 0.4f);
            Facility(go, "Ochre Relay", WarpPoint.Kind.Installation, false);
            Obstruct(go, 320f, 40f, false);
            Destination(go, "Ochre Relay", WarpPoint.Kind.Installation,
                "A deep-space communications relay over Ochre: three big dishes on a 480 m mast, linking the outer system to the inner.",
                300f, 3000f, FriendlyWorks, null, false, "Communications relay");
            b.shots.Add(("inst_relay", pos + toStar * 1100f + go.transform.right * 500f + Vector3.up * 150f, pos));
        }

        // ------------------------------------------------------------------ Lagrange points

        static void Observatory(SystemRefs s, Transform parent, Built b)
        {
            Vector3 th = s.thalassa.transform.position, st = s.star.position;
            Vector3 toStar = (st - th).normalized;
            Vector3 pos = LagrangeAtStart(s.thalassa, 1);
            var go = new GameObject("Thalassa L1 Observatory");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(toStar, Vector3.up));
            var g = new Geo(go.transform, "Observatory");
            // the sunshield: five layers, the outer one gold, facing the star
            for (int k = 0; k < 5; k++) g.Disc(k == 0 ? sm.gold : sm.mirror, new Vector3(0f, 0f, 40f - k * 9f), Vector3.forward, 300f - k * 6f, 0.8f);
            g.Truss(sm.dark, new Vector3(-280f, 0f, 2f), new Vector3(280f, 0f, 2f), 8f, 1f);
            g.Truss(sm.dark, new Vector3(0f, -280f, 2f), new Vector3(0f, 280f, 2f), 8f, 1f);
            for (int k = 0; k < 8; k++)   // solar cells round the sunward rim
            {
                float a = k * 45f * Mathf.Deg2Rad;
                g.Box(sm.solar, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 250f + Vector3.forward * 41f, new Vector3(60f, 60f, 0.8f), Quaternion.Euler(0f, 0f, k * 45f));
            }
            // the telescope in the shield's shadow
            g.Cyl(sm.hull, new Vector3(0f, 0f, -10f), new Vector3(0f, 0f, -280f), 45f);
            g.Torus(sm.dark, new Vector3(0f, 0f, -280f), Vector3.forward, 45f, 4f);
            g.Disc(sm.mirror, new Vector3(0f, 0f, -20f), Vector3.forward, 40f, 2f);
            g.Box(sm.dark, new Vector3(0f, -70f, -120f), new Vector3(60f, 40f, 70f));
            g.Box(sm.window, new Vector3(0f, -70f, -84.6f), new Vector3(40f, 6f, 1f));
            g.Dish(sm.white, new Vector3(40f, -90f, -150f), new Vector3(0.2f, -0.2f, -1f), 18f, 4f);
            g.Radiator(new Vector3(30f, -70f, -120f), Vector3.right, Vector3.forward, 120f, 50f);
            Bake(g, "Observatory_L1", true, s.layer);
            NavLight(go.transform, new Vector3(300f, 0f, 45f), sm.red, 6f, 2f, 0f);
            NavLight(go.transform, new Vector3(-300f, 0f, 45f), sm.green, 6f, 2f, 0f);
            // station-keeping thrusters (L1 is unstable): plumes shown during a burn
            foreach (var (x, y, z, dz) in new[] { (60f, -70f, -100f, -1f), (-60f, -70f, -100f, -1f), (0f, 30f, -290f, -1f), (0f, 0f, 50f, 1f) })
            {
                var plume = new GameObject("Thruster", typeof(MeshFilter), typeof(MeshRenderer));
                plume.transform.SetParent(go.transform, false);
                plume.transform.localPosition = new Vector3(x, y, z + dz * 25f);
                plume.transform.localScale = new Vector3(10f, 10f, 50f);
                plume.GetComponent<MeshFilter>().sharedMesh = Prim(PrimitiveType.Sphere);
                var pr = plume.GetComponent<MeshRenderer>();
                pr.sharedMaterial = Mats["FX_EnginePlume"];
                pr.shadowCastingMode = ShadowCastingMode.Off;
            }
            Facility(go, "Thalassa L1 Observatory", WarpPoint.Kind.Lagrange, false);
            Obstruct(go, 320f, 40f, false);
            Destination(go, "Thalassa L1 Observatory", WarpPoint.Kind.Lagrange,
                "A solar observatory at the L1 point between Kestrel and Thalassa, 27.6 km sunward of the planet, where the pulls of " +
                "the star and the planet balance and it goes round with Thalassa. L1 is unstable: it drifts off, faster and faster, " +
                "unless it fires its thrusters, every few minutes. A 600 m sunshield, and a telescope in its shadow.",
                320f, 3200f, LagrangeColour, null, false, "Lagrange point L1 of Thalassa");
            b.shots.Add(("inst_observatory", pos - toStar * 1300f + go.transform.right * 700f + Vector3.up * 300f, pos));
        }

        static void Depot(SystemRefs s, Transform parent, Built b)
        {
            Vector3 pos = LagrangeAtStart(s.jovara, 5);
            Vector3 along = Vector3.Cross(Vector3.up, pos).normalized;   // along the orbit
            var go = new GameObject("Jovara L5 Depot");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(along, Vector3.up));
            var g = new Geo(go.transform, "Depot");
            g.Truss(sm.dark, new Vector3(0f, 0f, -330f), new Vector3(0f, 0f, 330f), 26f, 2.4f);
            foreach (float z in new[] { -240f, -80f, 80f, 240f })
                foreach (float x in new[] { -78f, 78f })
                {
                    g.Ball(sm.white, new Vector3(x, 0f, z), 50f);
                    g.Torus(sm.hazard, new Vector3(x, 0f, z), Vector3.forward, 50.5f, 2f);
                    g.Rod(sm.dark, new Vector3(0f, 0f, z), new Vector3(x, 0f, z), 6f);
                }
            g.Cyl(sm.hull, new Vector3(0f, 0f, -330f), new Vector3(0f, 0f, -460f), 36f);   // crew module
            g.WindowBand(new Vector3(0f, 0f, -380f), new Vector3(0f, 0f, -392f), 36f);
            g.Torus(sm.hull, new Vector3(0f, 0f, -420f), Vector3.forward, 110f, 14f);
            g.Torus(sm.window, new Vector3(0f, 0f, -420f), Vector3.forward, 124f, 2f);
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f * Mathf.Deg2Rad;
                g.Rod(sm.hull, new Vector3(0f, 0f, -420f), new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 100f + new Vector3(0f, 0f, -420f), 6f);
            }
            g.Solar(new Vector3(0f, 26f, 330f), Vector3.up, Vector3.forward, 260f, 90f);
            g.Solar(new Vector3(0f, -26f, 330f), Vector3.down, Vector3.forward, 260f, 90f);
            g.Radiator(new Vector3(0f, 13f, 0f), Vector3.up, Vector3.right, 160f, 60f);
            g.Cyl(sm.hull, new Vector3(0f, 0f, 330f), new Vector3(0f, 0f, 370f), 30f);     // docking collar
            Bake(g, "Depot_L5", true, s.layer);
            NavLight(go.transform, new Vector3(0f, 0f, 374f), sm.green, 8f, 1.2f, 0f, 450f, new Color(0.3f, 1f, 0.4f));
            NavLight(go.transform, new Vector3(0f, 286f, 330f), sm.red, 6f, 2f, 0.3f);
            NavLight(go.transform, new Vector3(0f, -286f, 330f), sm.red, 6f, 2f, 0.3f);
            Flood(go.transform, new Vector3(140f, 60f, 0f), new Color(1f, 0.9f, 0.75f), 500f, 8f);
            var berth = Berth(go.transform, new Vector3(205f, 0f, 0f), Vector3.forward);
            Facility(go, "Jovara L5 Depot", WarpPoint.Kind.Lagrange, false, berth, true);
            Obstruct(go, 470f, 60f, false);
            Destination(go, "Jovara L5 Depot", WarpPoint.Kind.Lagrange,
                "A propellant depot at Jovara's trailing Lagrange point, L5, 60 degrees behind the planet in its orbit and going round " +
                "with it. L4 and L5 are stable: the depot swings slowly round the point and stays (the Trojan rocks gather at the leading " +
                "point, L4). Dock here (K) for repairs and to sell ore.",
                470f, 3200f, LagrangeColour, null, false, "Lagrange point L5 of Jovara");
            b.shots.Add(("inst_depot", go.transform.TransformPoint(new Vector3(900f, 350f, 700f)), pos));
        }

        // ------------------------------------------------------------------ bases seen from orbit

        /// <summary>A base on a moon or planet, seen from orbit: its buildings (a smaller copy of
        /// the ones on the surface) round a landing pad, standing on the body's surface at the
        /// given latitude: on Selene (which doesn't turn) on its sunlit side, toward Thalassa; on
        /// Cinder (which does) to start with on the side away from Thalassa. A
        /// destination of its own: the frigate arrives straight above it, and lands beside it.</summary>
        static void SurfaceBaseInSpace(SystemRefs s, string name, WarpPoint body, bool hostile, float latitude, Built b, string desc)
        {
            var bt = body.transform;
            float radius = body.bodyRadius;
            // the longitude: sunward and toward Thalassa on Selene, away from Thalassa on Cinder
            Vector3 toThalassa = (s.thalassa.transform.position - bt.position).normalized;
            Vector3 face = hostile ? -toThalassa : (s.star.position - bt.position).normalized + toThalassa * 0.5f;
            Vector3 local = bt.InverseTransformDirection(face);
            float lon = Mathf.Atan2(local.z, local.x);
            float lat = latitude * Mathf.Deg2Rad;
            Vector3 dirLocal = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
            Vector3 dir = bt.TransformDirection(dirLocal).normalized;

            var go = new GameObject(name);
            go.transform.SetParent(bt, true);
            float sc = bt.lossyScale.x;
            go.transform.localScale = Vector3.one / Mathf.Max(1e-6f, sc);
            go.transform.SetPositionAndRotation(bt.position + dir * radius, Quaternion.FromToRotation(Vector3.up, dir));
            // onto the ground once (it is part of the body after that)
            var anchor = go.AddComponent<SurfaceAnchor>();
            anchor.body = bt;
            anchor.sink = 0f;
            Physics.SyncTransforms();
            anchor.Settle();
            Object.DestroyImmediate(anchor);

            float k = hostile ? 0.6f : 0.5f;
            var set = hostile ? s.kit.fort : s.kit.colony;
            if (s.kit.outpost != null)
            {
                var pad = (GameObject)PrefabUtility.InstantiatePrefab(s.kit.outpost);
                pad.transform.SetParent(go.transform, false);
                pad.transform.localScale = Vector3.one * k;
                pad.transform.localPosition = Vector3.zero;
                if (hostile) foreach (Transform c in pad.transform) if (c.name == "Parked craft") Object.DestroyImmediate(c.gameObject);
                var pa = pad.AddComponent<SurfaceAnchor>(); pa.body = bt; pa.sink = 0.5f; pa.Settle();
            }
            if (set != null && set.Length > 0)
            {
                int n = 0;
                for (int ring = 0; ring < 2; ring++)
                {
                    int count = ring == 0 ? 7 : 11;
                    float r = (ring == 0 ? 150f : 245f) * k / 0.5f;
                    for (int i = 0; i < count; i++)
                    {
                        float a = (i + 0.5f * ring) * Mathf.PI * 2f / count;
                        var m = (GameObject)PrefabUtility.InstantiatePrefab(set[n++ % set.Length]);
                        m.transform.SetParent(go.transform, false);
                        m.transform.localScale = Vector3.one * k;
                        m.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                        m.transform.localRotation = Quaternion.LookRotation(new Vector3(-Mathf.Cos(a), 0f, -Mathf.Sin(a)), Vector3.up);
                        var an = m.AddComponent<SurfaceAnchor>(); an.body = bt; an.sink = 1f; an.Settle();
                    }
                }
            }
            Flood(go.transform, new Vector3(0f, 120f, 0f), hostile ? new Color(1f, 0.4f, 0.3f) : new Color(1f, 0.88f, 0.7f), 500f, 6f);
            var wp = Destination(go, name, WarpPoint.Kind.SurfaceBase, desc, 300f, hostile ? 3600f : 2600f, hostile ? HostileWorks : FriendlyWorks, body, hostile);
            wp.landsOn = body;
            wp.overhead = true;
            wp.surface = body.surface;
            Facility(go, name, WarpPoint.Kind.SurfaceBase, hostile);
            Vector3 tangent = Vector3.Cross(dir, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.5f) tangent = Vector3.right;
            b.shots.Add(($"inst_{name.ToLower().Replace(' ', '_')}", go.transform.position + dir * (hostile ? 1400f : 900f) + tangent * 700f, go.transform.position));
        }
    }
}

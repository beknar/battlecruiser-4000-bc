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
    /// The Nine Hells' friendly places:
    /// - K'rzyn: its capital Heaven under an energy shield (orbital strikes break on it), with gun
    ///   batteries and missile platforms; a space elevator from Heaven up to Heaven Highport; the
    ///   orbital refinery and factory; the space dock; its defence satellites; and at its L4 the
    ///   colony Bastion with its gun emplacements.
    /// - Rephaim: the refuelling station and the mineral refiner in its orbit.
    /// - Goliath: the small research station (landed on its pad, a ship is repaired) and the
    ///   ruins of a race long dead.
    /// - Nineveh: the observatory on its ice, and the Deep Watch station over it.
    /// </summary>
    public static partial class CarrierBuilder
    {
        /// <summary>A Lagrange point site (set on its orbit by SetUpNineHellsOrbits).</summary>
        class NhLagrange { public Transform t; public string planet; public int n; }

        static Material heavenShield, ruinStone, ruinGlow;

        /// <summary>The Nine Hells' settlement layouts (with the others: SettlementLayouts).</summary>
        static void NineHellsLayouts(Func<string, Action<Transform>, GameObject> Layout)
        {
            float Yaw(Vector3 at) => Mathf.Atan2(-at.x, -at.z) * Mathf.Rad2Deg;
            heavenShield = Transparent("Heaven_Shield", "Universal Render Pipeline/Unlit", 2f, BlendMode.One, BlendMode.One, BlendMode.One,
                                       new Color(0.18f, 0.42f, 0.75f, 1f), null);
            ruinStone = Lit("Ruin_Stone", new Color(0.22f, 0.2f, 0.24f), metallic: 0.1f, smooth: 0.25f);
            ruinGlow = Lit("Ruin_Glow", new Color(0.3f, 0.9f, 0.8f), metallic: 0f, smooth: 0.4f, emission: new Color(0.25f, 1f, 0.85f) * 4f);

            Layout("Heaven", t =>
            {
                // the capital: towers round the spaceport, two rings of city round them, walls and gun
                // batteries outside, missile platforms between, under a dome of light 3.4 km across
                Piece(MegaSpire, t, new Vector3(0f, 0f, 700f), 0f, 0.6f);
                Piece(MegaNeedle, t, new Vector3(-650f, 0f, 380f), 20f, 0.8f);
                Piece(MegaCapsule, t, new Vector3(660f, 0f, 380f), -30f, 0.5f);
                Piece(MegaMain, t, new Vector3(-560f, 0f, -560f), 45f, 0.65f);
                Piece(MegaSlabTower, t, new Vector3(600f, 0f, -520f), -20f, 0.55f);
                Piece(MegaTwinDome, t, new Vector3(0f, 0f, -760f), 0f, 0.45f);
                for (int i = 0; i < 10; i++)
                {
                    float a = (i * 36f + 18f) * Mathf.Deg2Rad;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 420f;
                    Piece(i % 2 == 0 ? MegaBlock : SandTower, t, at, Yaw(at), i % 2 == 0 ? 1.4f : 3f);
                }
                string[] flats = { VistaTall, VistaOctagon, VistaOffice, VistaTall, VistaOffice, VistaOctagon, VistaTall, VistaOffice };
                for (int i = 0; i < flats.Length; i++)
                {
                    float a = (i * 45f + 5f) * Mathf.Deg2Rad;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1000f;
                    Piece(flats[i], t, at, Yaw(at), 1f);
                }
                // the batteries: gun towers round the edge, and the missile platforms between them
                for (int i = 0; i < 8; i++)
                {
                    float a = (i * 45f) * Mathf.Deg2Rad;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1400f;
                    Piece("own:Mil_WallTower", t, at, Yaw(at), 1f);
                    float socket = IndPointsFor("Mil_WallTower") is IndModelPoints tp && tp.deckHeight > 0f ? tp.deckHeight : 55f;
                    Piece(Turret, t, at + Vector3.up * socket, Yaw(at), 7f, ground: true, sink: 0f);
                    float b = (i * 45f + 22.5f) * Mathf.Deg2Rad;
                    var mp = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * 1250f;
                    Piece(MonoSilo, t, mp, Yaw(mp), 2.2f);
                    Piece(MonoSilo, t, mp + Quaternion.Euler(0f, Yaw(mp), 0f) * new Vector3(60f, 0f, 0f), Yaw(mp), 2.2f);
                }
                Piece(MegaAntenna, t, new Vector3(200f, 0f, 200f), 0f, 3f);
                // the shield: a dome of light over the whole city (no collider: ships land under it)
                var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(dome.GetComponent<Collider>());
                dome.name = "Energy shield";
                dome.transform.SetParent(t, false);
                dome.transform.localScale = Vector3.one * 3400f;
                var dr = dome.GetComponent<MeshRenderer>();
                dr.sharedMaterial = heavenShield;
                dr.shadowCastingMode = ShadowCastingMode.Off;
                dr.receiveShadows = false;
            });

            Layout("Goliath Research Station", t =>
            {
                // a few prefab habitats round the pad, a repair hangar, its comms and scopes
                for (int i = 0; i < 4; i++)
                {
                    float a = (45f + i * 90f) * Mathf.Deg2Rad;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 330f;
                    Piece(ColonyBlock, t, at, Yaw(at), 4f, mat: "Ind_Panel");
                }
                Piece(MegaHall, t, new Vector3(0f, 0f, 420f), 180f, 0.45f);
                Piece(ColonyAntenna, t, new Vector3(-300f, 0f, -300f), 0f, 5f, mat: "Ind_Panel");
                Piece(RadarDish, t, new Vector3(300f, 0f, -320f), 45f, 1.5f);
                Piece(Dome, t, new Vector3(-420f, 0f, 80f), 0f, 4f, sink: 1f);
            });

            Layout("Nineveh Observatory", t =>
            {
                // the great scopes on the ice, the domes of the research station between them
                for (int i = 0; i < 6; i++)
                {
                    float a = (i * 60f) * Mathf.Deg2Rad;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 520f;
                    Piece(RadarDish, t, at, Yaw(at) + 180f, 3f);
                }
                for (int i = 0; i < 3; i++)
                {
                    float a = (30f + i * 120f) * Mathf.Deg2Rad;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 300f;
                    Piece(Dome, t, at, Yaw(at), 5f, sink: 1f);
                    Piece(DomeGlass, t, at, Yaw(at), 5f, sink: 1f);
                }
                Piece(ColonyAntenna, t, new Vector3(0f, 0f, 700f), 0f, 7f, mat: "Ind_Panel");
            });

            Layout("Ruins of the Ancients", t =>
            {
                // what is left of the old ones: a ring of leaning monoliths round a stepped mound, a
                // broken arch, fallen pillars, and lines of faint light in the stone
                var g = new Geo(t, "Ruins");
                for (int i = 0; i < 12; i++)
                {
                    float a = i * 30f * Mathf.Deg2Rad;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 520f;
                    float h = 160f + (i * 37 % 5) * 40f;
                    var rot = Quaternion.Euler((i % 3 - 1) * 6f, -a * Mathf.Rad2Deg, (i % 2 == 0 ? 4f : -5f));
                    g.Box(ruinStone, at + Vector3.up * (h * 0.5f - 10f), new Vector3(36f, h, 22f), rot);
                    g.Box(ruinGlow, at + Vector3.up * (h * 0.6f), new Vector3(37f, 3f, 23f), rot);
                }
                for (int k = 0; k < 5; k++)
                    g.Box(ruinStone, new Vector3(0f, 12f + k * 24f, 0f), new Vector3(300f - k * 55f, 24f, 300f - k * 55f));
                g.Box(ruinGlow, new Vector3(0f, 135f, 0f), new Vector3(40f, 6f, 40f));
                g.Torus(ruinStone, new Vector3(0f, 150f, -820f), Vector3.forward, 150f, 18f);
                for (int i = 0; i < 6; i++)
                {
                    float a = (15f + i * 60f) * Mathf.Deg2Rad;
                    var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 860f;
                    g.Cyl(ruinStone, at + Vector3.up * 8f, at + new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a)) * 110f + Vector3.up * 8f, 9f);
                }
                Bake(g, "Ruins_Ancients", true, ObstacleLayer());
            });
        }

        /// <summary>The friendly places (see the class summary).</summary>
        static void NineHellsSites(NineHellsRefs R)
        {
            var s = new SystemRefs { sys = R.sys, star = R.star, layer = R.layer, kit = R.kit, rockMat = R.rockMat };
            var b = new Built();
            var ip = IndustryPrefabs();
            var lay = SettlementLayouts();
            WarpPoint P(string n) => R.points.TryGetValue(n, out var w) ? w : null;
            var krzyn = P("K'rzyn");

            // ---- K'rzyn
            SurfaceSettlement(s, "Heaven", krzyn, 0f, 70f, lay, b, "Capital city",
                "The capital of K'rzyn and of the Nine Hells, under an energy shield that turns bombardment from orbit; gun batteries " +
                "and missile platforms ring it. A space elevator rises from it to Heaven Highport.");
            var heaven = R.sys.GetComponentsInChildren<WarpPoint>(true).FirstOrDefault(w => w.displayName == "Heaven");
            if (heaven != null) heaven.shielded = true;
            var highport = Elevator(R, krzyn, heaven, b);
            if (ip.refinerySpace == null || ip.factorySpace == null)
                Log("nine hells: no orbital refinery or factory prefab: K'rzyn's orbital sites, its defence satellites and Bastion not built");
            if (ip.refinerySpace != null && ip.factorySpace != null)
            {
                Vector3 kc = krzyn.Position;
                Vector3 toStar = (R.star.position - kc).normalized;
                Vector3 Round(float deg, float alt) => kc + Quaternion.AngleAxis(deg, Vector3.up) * toStar * (krzyn.bodyRadius + alt);
                SpaceSite("K'rzyn Orbital Refinery", ip.refinerySpace, R.sys, Round(150f, 260000f), Quaternion.LookRotation(-toStar), b, krzyn);
                SpaceSite("K'rzyn Orbital Factory", ip.factorySpace, R.sys, Round(200f, 270000f), Quaternion.LookRotation(toStar), b, krzyn);
                SpaceSite("Heaven Space Dock", ip.factorySpace, R.sys, Round(250f, 300000f), Quaternion.LookRotation(Vector3.Cross(toStar, Vector3.up)), b, krzyn);
                // the defence satellites, on guard round her (they are K'rzyn's: no target for anyone)
                for (int i = 0; i < 8; i++)
                    DefenceSatellite(R.sys, $"K'rzyn Defence Satellite {i + 1}", kc + Quaternion.Euler(i % 2 == 0 ? 10f : -12f, i * 45f + 20f, 0f) * toStar * (krzyn.bodyRadius + 180000f));
                // the colony Bastion at K'rzyn's L4, with its gun emplacements
                var (pp, qq) = OrbitPlane(kc);
                Vector3 l4 = Orbit.LagrangePoint(kc, pp, qq, R.data.Masses["K'rzyn"], 4);
                var bastion = NhStation(R.sys, "Bastion", l4, Vector3.up, 1.3f, true, WarpPoint.Kind.Lagrange, "Colony (L4)",
                    "A large colony at K'rzyn's leading Lagrange point, ringed by gun emplacements: forty thousand people, docks and a market.", null, b, guns: 10);
                R.lagrange.Add(new NhLagrange { t = bastion.transform, planet = "K'rzyn", n = 4 });
            }

            // ---- Rephaim: fuel and a refiner, in orbit
            var rephaim = P("Rephaim");
            if (rephaim != null)
            {
                Vector3 rc = rephaim.Position;
                Vector3 toStar = (R.star.position - rc).normalized;
                NhStation(R.sys, "Rephaim Fuel Station", rc + Quaternion.Euler(0f, 40f, 0f) * toStar * (rephaim.bodyRadius + 250000f), Vector3.up, 1f, true,
                    WarpPoint.Kind.Station, "Refuelling station", "A refuelling station in orbit of Rephaim, skimming hydrogen from the storms below: repairs and a market.", rephaim, b);
                if (ip.refinerySpace != null)
                    SpaceSite("Rephaim Mineral Refiner", ip.refinerySpace, R.sys, rc + Quaternion.Euler(0f, -50f, 0f) * toStar * (rephaim.bodyRadius + 280000f),
                              Quaternion.LookRotation(toStar), b, rephaim);
            }

            // ---- Goliath: the research station, and the ruins
            var goliath = P("Goliath");
            SurfaceSettlement(s, "Goliath Research Station", goliath, 12f, 40f, lay, b, "Research station",
                "A small allied research station studying the ruins of the race that died here. Land on its pad (K) and your ship is repaired.");
            var station = R.sys.GetComponentsInChildren<WarpPoint>(true).FirstOrDefault(w => w.displayName == "Goliath Research Station");
            if (station != null) station.repairsLanded = true;
            SurfaceSettlement(s, "Ruins of the Ancients", goliath, 18f, 52f, lay, b, "Ancient ruins",
                "Monoliths and a stepped mound of black stone, older than anything else in the Nine Hells, with faint light still running in its seams.");

            // ---- Nineveh: the observatory on the ice, and the Deep Watch over it
            var nineveh = P("Nineveh");
            SurfaceSettlement(s, "Nineveh Observatory", nineveh, 30f, 0f, lay, b, "Observatory",
                "An allied observatory and research station on Nineveh's ice, its great scopes turned outward, watching what moves beyond the Nine Hells.");
            if (nineveh != null)
            {
                Vector3 nc = nineveh.Position;
                NhStation(R.sys, "Nineveh Deep Watch", nc + (R.star.position - nc).normalized * -(nineveh.bodyRadius + 200000f), Vector3.up, 0.9f, false,
                    WarpPoint.Kind.Station, "Observation station", "The Deep Watch: an allied station over Nineveh's night side, its dishes turned away from the star. Repairs.", nineveh, b);
            }
            R.targets.AddRange(b.targets);
            Log($"nine hells: friendly sites built");
        }

        /// <summary>The space elevator: a tether from Heaven straight up to Heaven Highport, 400 km
        /// up (K'rzyn doesn't turn in its frame: the city is always under it, and the station holds
        /// still to dock at).</summary>
        static GameObject Elevator(NineHellsRefs R, WarpPoint krzyn, WarpPoint heaven, Built b)
        {
            if (krzyn == null || heaven == null) return null;
            var bt = krzyn.transform;
            Vector3 up = (heaven.transform.position - bt.position).normalized;
            float geo = krzyn.bodyRadius + 400000f;
            var go = NhStation(R.sys, "Heaven Highport", bt.position + up * geo, up, 1.2f, true, WarpPoint.Kind.Station, "Space station",
                "K'rzyn's orbital port, at the top of the space elevator from Heaven in geostationary orbit. Repairs, a market and crew.", krzyn, b);
            // the tether, from the city up to the station
            var tether = new GameObject("Space elevator", typeof(MeshFilter), typeof(MeshRenderer));
            tether.transform.SetParent(R.sys, false);
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tether.GetComponent<MeshFilter>().sharedMesh = c.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(c);
            float len = geo - krzyn.bodyRadius;
            tether.transform.SetPositionAndRotation(bt.position + up * (krzyn.bodyRadius + len * 0.5f), Quaternion.FromToRotation(Vector3.up, up));
            tether.transform.localScale = new Vector3(40f, len * 0.5f, 40f);
            var tr = tether.GetComponent<MeshRenderer>();
            tr.sharedMaterial = sm.hull;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A station: a hub on its axis `up`, a habitat ring, a docking arm with its
        /// berth, solar wings and lights, scaled by `k`; docked at with K (repairs; a market if
        /// `market`), a crew port if crew.json says it recruits.</summary>
        static GameObject NhStation(Transform parent, string name, Vector3 pos, Vector3 up, float k, bool market, WarpPoint.Kind kind, string label, string desc,
                                    WarpPoint orbitOf, Built b, int guns = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Vector3 fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(Vector3.right, up);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd.normalized, up));
            // (along its forward: the hub, and ahead of it the docking arm out to the berth; the
            // habitat ring round the hub, square to the arm, so a ship leaving the berth goes clear)
            var g = new Geo(go.transform, "Hull");
            g.Cyl(sm.hull, new Vector3(0f, 0f, -220f) * k, new Vector3(0f, 0f, 120f) * k, 70f * k);
            g.Ball(sm.hull, new Vector3(0f, 0f, -220f) * k, new Vector3(70f, 70f, 30f) * k, Quaternion.identity);
            g.Ball(sm.dark, new Vector3(0f, 0f, 120f) * k, new Vector3(70f, 70f, 26f) * k, Quaternion.identity);
            g.Torus(sm.hull, new Vector3(0f, 0f, -60f) * k, Vector3.forward, 480f * k, 34f * k);
            g.Torus(sm.window, new Vector3(0f, 0f, -60f) * k, Vector3.forward, 514f * k, 3f * k);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                g.Cyl(sm.hull, d * 70f * k + new Vector3(0f, 0f, -60f) * k, d * 450f * k + new Vector3(0f, 0f, -60f) * k, 10f * k);
            }
            g.Cyl(sm.dark, new Vector3(0f, 0f, 120f) * k, new Vector3(0f, 0f, 560f) * k, 20f * k);   // docking arm
            g.Truss(sm.hull, new Vector3(0f, 0f, 140f) * k, new Vector3(0f, 0f, 540f) * k, 50f * k, 3f * k);
            g.Cyl(sm.hull, new Vector3(0f, 0f, 540f) * k, new Vector3(0f, 0f, 580f) * k, 38f * k);
            g.Torus(sm.hazard, new Vector3(0f, 0f, 581f) * k, Vector3.forward, 30f * k, 3f * k);
            g.Solar(new Vector3(70f, 0f, -190f) * k, Vector3.right, Vector3.up, 380f * k, 100f * k);
            g.Solar(new Vector3(-70f, 0f, -190f) * k, Vector3.left, Vector3.up, 380f * k, 100f * k);
            for (int i = 0; i < guns; i++)
            {
                // gun emplacements round the ring, on its far side from the arm
                float a = (i + 0.5f) * Mathf.PI * 2f / guns;
                Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 at = d * 480f * k + new Vector3(0f, 0f, -60f - 38f) * k;
                g.Cyl(sm.dark, at, at - Vector3.forward * 14f * k, 14f * k);
                g.Cyl(sm.dark, at - Vector3.forward * 8f * k + d * 6f * k, at - Vector3.forward * 8f * k + d * 42f * k, 3f * k);
                g.Cyl(sm.dark, at - Vector3.forward * 14f * k + d * 6f * k, at - Vector3.forward * 14f * k + d * 42f * k, 3f * k);
            }
            Bake(g, "NH_" + new string(name.Where(char.IsLetterOrDigit).ToArray()), true, ObstacleLayer());
            for (int i = 0; i < 6; i++)
            {
                float a = (i * 60f + 30f) * Mathf.Deg2Rad;
                NavLight(go.transform, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 518f * k + new Vector3(0f, 0f, -60f) * k, i % 2 == 0 ? sm.red : sm.green, 8f * k, 2.4f, i * 0.15f);
            }
            NavLight(go.transform, new Vector3(0f, 0f, 590f) * k, sm.green, 9f * k, 1.2f, 0f, 400f * k, new Color(0.3f, 1f, 0.4f));
            var berth = Berth(go.transform, new Vector3(110f, 0f, 400f) * k, Vector3.forward);
            Facility(go, name, kind, false, berth, market);
            Obstruct(go, 600f * k, 120f * k, false);
            Destination(go, name, kind, desc + " Dock here (K).", 600f * k, Mathf.Max(3200f, 2400f * k), FriendlyStation, orbitOf, false, label);
            if ((CrewTable.Get().Place(name)?.Recruits ?? false) || market) go.AddComponent<IndustrySite>().siteName = name;
            b.shots.Add(($"nh_{name.ToLower().Replace(' ', '_').Replace("'", "")}", pos + go.transform.rotation * new Vector3(1200f, 500f, 1400f) * k, pos));
            return go;
        }

        /// <summary>One of K'rzyn's defence satellites: a gun platform on solar wings (its own; not
        /// a target, no warp point).</summary>
        static void DefenceSatellite(Transform parent, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var g = new Geo(go.transform, "Hull");
            g.Box(sm.dark, Vector3.zero, new Vector3(40f, 24f, 60f));
            g.Cyl(sm.hull, new Vector3(0f, 12f, 0f), new Vector3(0f, 22f, 0f), 12f);
            g.Cyl(sm.dark, new Vector3(-4f, 18f, 4f), new Vector3(-4f, 18f, 70f), 2.2f);
            g.Cyl(sm.dark, new Vector3(4f, 18f, 4f), new Vector3(4f, 18f, 70f), 2.2f);
            g.Solar(new Vector3(20f, 0f, 0f), Vector3.right, Vector3.up, 120f, 30f);
            g.Solar(new Vector3(-20f, 0f, 0f), Vector3.left, Vector3.up, 120f, 30f);
            Bake(g, "NH_DefenceSatellite", false, ObstacleLayer());
            NavLight(go.transform, new Vector3(0f, 26f, 0f), sm.red, 4f, 1.6f, 0f);
        }
    }
}

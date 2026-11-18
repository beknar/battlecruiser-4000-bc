using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrierGame;
using CarrierGame.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The second star system, the Nine Hells (Scenes/NineHells.unity): its star, its nine
    /// worlds, their moons and its two asteroid belts from Data/nine_hells.json, on their orbits,
    /// with the frigate and everything that flies and lands it, as the Testing System has. What
    /// is built there (stations, cities, the pirates, the Vrask) is added in its own parts
    /// (NineHellsSites and the rest), each from the scene's `NineHellsRefs`.
    /// </summary>
    public static partial class CarrierBuilder
    {
        /// <summary>What the Nine Hells' parts are built round.</summary>
        class NineHellsRefs
        {
            public Transform sys, star;
            public StarSystemData data;
            public readonly Dictionary<string, GameObject> bodies = new Dictionary<string, GameObject>();
            public readonly Dictionary<string, WarpPoint> points = new Dictionary<string, WarpPoint>();
            public readonly Dictionary<string, Vector3> fieldCentre = new Dictionary<string, Vector3>();
            public readonly Dictionary<string, List<Vector3>> fieldRocks = new Dictionary<string, List<Vector3>>();
            public readonly List<GameObject> enemies = new List<GameObject>();
            public readonly List<Transform> targets = new List<Transform>();
            public Material rockMat;
            public int layer;
            public SurfaceKit kit;
            public Prefabs prefabs;
            public readonly List<NhComet> comets = new List<NhComet>();
            public readonly List<NhStream> streams = new List<NhStream>();
            public readonly List<NhLagrange> lagrange = new List<NhLagrange>();
        }

        static string NineHellsDataPath => Path.Combine(Application.dataPath, "Carrier", "Data", "nine_hells.json");

        static void BuildNineHellsScene(Prefabs p, GameObject frigatePrefab)
        {
            int layer = ObstacleLayer();
            UnityEngine.Random.InitState(9119);
            ProceduralAsteroid.ClearCache();
            var data = StarSystemData.Parse(File.ReadAllText(NineHellsDataPath));
            var rockMat = Mats.TryGetValue("Asteroid_Rock", out var rm) ? rm : Load<Material>(MatDir + "/Asteroid_Rock.mat");
            var gasTex = Load<Texture2D>(EnvDir + "/T_GasGiant.png");
            var (volcAlbedo, volcGlow) = VolcanicMaps();
            var looks = new Dictionary<SurfaceKind, (Material mat, Material atmo)>
            {
                { SurfaceKind.Volcanic, (PlanetMat("Planet_Volcanic", volcAlbedo, volcGlow, new Color(2.2f, 1.1f, 0.4f), 0.02f), AtmosphereMat("Atmo_Volcanic", new Color(1.4f, 0.45f, 0.2f), 6f, 0.8f)) },
                { SurfaceKind.Desert, (PlanetMat("Planet_Desert", DesertMap(), null, Color.black, 0.025f), AtmosphereMat("Atmo_Desert", new Color(1.2f, 0.75f, 0.45f), 6f, 0.9f)) },
                { SurfaceKind.Ocean, (PlanetMat("Planet_Ocean", OceanMap(), null, Color.black, 0.025f), AtmosphereMat("Atmo_Ocean", new Color(0.35f, 0.6f, 1.6f), 4f, 1.7f)) },
                { SurfaceKind.GasGiant, (PlanetMat("Planet_GasGiantLit", gasTex, null, Color.black, 0.03f), AtmosphereMat("Atmo_GasGiant", new Color(1.1f, 0.85f, 0.6f), 5f, 1.4f)) },
                { SurfaceKind.IceGiant, (PlanetMat("Planet_IceGiant", IceGiantMap(), null, Color.black, 0.03f), AtmosphereMat("Atmo_IceGiant", new Color(0.5f, 0.95f, 1.2f), 4.5f, 1.5f)) },
            };
            var starMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            // (a hard white star, hotter than Kestrel)
            starMat.SetColor("_BaseColor", new Color(4.2f, 4.2f, 4.6f));
            starMat = SaveMat("Star_NineHells", starMat);
            var coronaMat = ParticleAdditive("Star_NineHells_Corona", StarGlowTexture(), new Color(0.5f, 0.52f, 0.62f, 1f));
            var streakMat = ParticleAdditive("FX_WarpStreak", KenneyTex("trace_01_rotated"), new Color(1.6f, 1.8f, 2.2f, 1f));
            var hazeMat = ParticleAdditive("FX_WarpHaze", KenneyTex("light_03"), new Color(0.6f, 0.75f, 1.4f, 0.6f));
            var flashMat = ParticleAdditive("FX_WarpFlash", KenneyTex("star_06"), new Color(2.5f, 2.7f, 3f, 1f));
            var ringMat = ParticleAdditive("FX_WarpRing", KenneyTex("circle_05"), new Color(1.4f, 1.8f, 2.6f, 1f));
            var sparkMat = ParticleAdditive("FX_WarpSpark", KenneyTex("flare_01"), new Color(1.5f, 1.8f, 2.4f, 1f));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            StructureMaterials();
            var surfaceKit = SurfaceAssets(rockMat, p);
            SpaceLighting(Quaternion.identity, 2.5f, new Color(0.95f, 0.96f, 1f), new Color(0.05f, 0.05f, 0.07f));
            var sun = RenderSettings.sun;
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.55f, 0.65f, 0.9f);
            fill.intensity = 0.3f;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.LookRotation(new Vector3(-0.2f, 1f, -0.3f));
            var sys = new GameObject("Nine Hells").transform;

            // the star
            const float StarRadius = 5.5e7f;
            var star = new GameObject("Abaddon", typeof(MeshFilter), typeof(MeshRenderer));
            star.transform.SetParent(sys, false);
            star.transform.localScale = Vector3.one * StarRadius * 2f;
            star.layer = layer;
            star.AddComponent<ProceduralSphere>().Build();
            star.GetComponent<MeshRenderer>().sharedMaterial = starMat;
            star.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            star.AddComponent<SphereCollider>().radius = 0.5f;
            var sob = star.AddComponent<Obstacle>();
            sob.radius = StarRadius * 1.08f; sob.coreRadius = StarRadius;
            star.AddComponent<ScaledSpace>().starLit = false;
            var corona = GameObject.CreatePrimitive(PrimitiveType.Quad);
            StripColliders(corona);
            corona.name = "Corona";
            corona.transform.SetParent(sys, false);
            corona.transform.localScale = Vector3.one * StarRadius * 9.1f;
            corona.AddComponent<ScaledSpace>().starLit = false;
            var cr = corona.GetComponent<MeshRenderer>();
            cr.sharedMaterial = coronaMat;
            cr.shadowCastingMode = ShadowCastingMode.Off;
            var sl = star.AddComponent<StarLight>();
            sl.sun = sun;
            sl.corona = corona.transform;
            MakeWarpPoint(star, "Abaddon", WarpPoint.Kind.Star,
                "The Nine Hells' star: a hard white sun, hotter than most. Close in, its light is dangerous; there is nowhere to hide.",
                StarRadius, StarRadius * 3f, new Color(0.9f, 0.92f, 1f));

            // the star's mass, from K'rzyn's year (30 days)
            var year = data.planets.FirstOrDefault(pl => pl.yearSetsStarMass) ?? data.planets[0];
            SetStarMass(year.Position);
            var R = new NineHellsRefs { sys = sys, star = star.transform, data = data, rockMat = rockMat, layer = layer, kit = surfaceKit, prefabs = p };
            var masses = data.Masses;
            foreach (var pd in data.planets)
            {
                Material mat, atmo = null;
                // (a world like a moon, airless and cratered: its own grey map)
                if (looks.TryGetValue(pd.Surface, out var look)) (mat, atmo) = look;
                else mat = MoonLook(pd.name, 1000 + R.bodies.Count * 113);
                float spin = pd.spinMatchesElevator ? GeoSpin(masses[pd.name], pd.radius + 400000f) : pd.spin;
                var planet = MakePlanet(pd.name, sys, pd.Position, pd.radius, mat, pd.airless ? null : atmo, pd.atmosphereScale, layer, spin);
                var wp = MakeWarpPoint(planet, pd.name, WarpPoint.Kind.Planet, pd.description, pd.radius, pd.arrival, pd.MapColor, null, pd.hostile);
                AddTerrain(planet, pd.Surface, mat, 901 + R.bodies.Count * 37);
                wp.surface = pd.Surface;
                wp.airless = pd.airless;
                if (pd.Surface == SurfaceKind.Moon)
                {
                    planet.AddComponent<MineralDeposit>().seed = MineralDeposit.PositionSeed(pd.Position);
                    planet.GetComponent<MineralDeposit>().body = BodyClass.Moon;
                }
                R.bodies[pd.name] = planet;
                R.points[pd.name] = wp;
            }
            foreach (var md in data.moons)
            {
                var planet = R.bodies[md.planet];
                Vector3 pos = planet.transform.position + new Vector3(Mathf.Cos(md.angle * Mathf.Deg2Rad), 0.03f, Mathf.Sin(md.angle * Mathf.Deg2Rad)) * md.distanceKm * 1000f;
                var moonMat = MoonLook(md.name, md.seed);
                var m = MakePlanet(md.name, sys, pos, md.radius, moonMat, null, 1f, layer, 0f);
                m.GetComponent<Obstacle>().cover = true;
                m.AddComponent<MineralDeposit>().seed = MineralDeposit.PositionSeed(pos);
                m.GetComponent<MineralDeposit>().body = BodyClass.Moon;
                AddTerrain(m, SurfaceKind.Moon, moonMat, md.seed);
                var mw = MakeWarpPoint(m, md.name, WarpPoint.Kind.Moon, md.description, md.radius, md.radius + 80000f, new Color(0.75f, 0.75f, 0.78f), R.points[md.planet]);
                mw.surface = SurfaceKind.Moon;
                R.bodies[md.name] = m;
                R.points[md.name] = mw;
            }
            foreach (var fd in data.fields)
            {
                Vector3 c = fd.Position;
                R.fieldCentre[fd.name] = c;
                R.fieldRocks[fd.name] = MakeField(fd.name, sys, c, fd.radius, fd.rocks, fd.seed, rockMat, layer);
                var at = new GameObject(fd.name + " (warp point)");
                at.transform.SetParent(sys, false);
                at.transform.position = c;
                MakeWarpPoint(at, fd.name, WarpPoint.Kind.AsteroidField, fd.description, fd.radius, fd.arrival, fd.MapColor, null, fd.hostile).surface = SurfaceKind.Asteroid;
            }
            Physics.SyncTransforms();

            // the frigate, in orbit over K'rzyn's day side (on to Heaven, the capital, from there)
            var home = R.points["K'rzyn"];
            var frigate = (GameObject)PrefabUtility.InstantiatePrefab(frigatePrefab);
            Vector3 start = home.ArrivalPoint(home.Position + Quaternion.Euler(0f, 70f, 0f) * -home.Position);
            Vector3 look2 = home.Position - start;
            look2.y = 0f;
            frigate.transform.SetPositionAndRotation(start, Quaternion.LookRotation(look2));
            var cc = frigate.GetComponent<CarrierController>();
            cc.target = null;
            AddPlayerFrigate(cc);
            var drive = frigate.AddComponent<WarpDrive>();
            drive.streakMaterial = streakMat;
            drive.hazeMaterial = hazeMat;
            drive.flashMaterial = flashMat;
            drive.ringMaterial = ringMat;
            drive.sparkMaterial = sparkMat;
            var flight = frigate.AddComponent<PlanetFlight>();
            var ks = frigate.AddComponent<KineticStrike>();
            ks.glowMaterial = ParticleAdditive("FX_StrikeGlow", KenneyTex("light_01"), new Color(3f, 2.6f, 2f, 1f));
            ks.beamMaterial = ParticleAdditive("FX_StrikeBeam", null, new Color(2.4f, 2.6f, 3f, 1f));
            ks.ringMaterial = ParticleAdditive("FX_StrikeRing", KenneyTex("circle_05"), new Color(2f, 1.8f, 1.5f, 1f));
            flight.skyDome = SaveMat("SkyDome", new Material(Shader.Find("Carrier/SkyDome")));

            // what is built and what waits there (each part its own; none yet but the bodies)
            NineHellsParts(R);
            // the game starts at Heaven, K'rzyn's capital: the frigate over the city (and, in play,
            // landed there once the start menu is closed: StartLandedAt)
            var heavenWp = sys.GetComponentsInChildren<WarpPoint>(true).FirstOrDefault(w => w.displayName == "Heaven");
            if (heavenWp != null)
            {
                start = heavenWp.ArrivalPoint(heavenWp.Position + (R.star.position - heavenWp.Position).normalized * 100000f);
                Vector3 hl = Vector3.ProjectOnPlane(heavenWp.Position - start, (start - home.Position).normalized);
                frigate.transform.SetPositionAndRotation(start, Quaternion.LookRotation(hl.sqrMagnitude > 1f ? hl : Vector3.forward, (start - home.Position).normalized));
            }

            BerthRings(sys, layer);
            SetUpNineHellsOrbits(R, frigate.transform);

            var orbit = MainCamera(frigate.transform, 1f, 1200000f, start + new Vector3(-600f, 200f, -300f), start);
            orbit.maxDistance = 6000f;
            var demoGo = new GameObject("Demo");
            var demo = demoGo.AddComponent<CarrierDemoInput>();
            demo.carrier = cc;
            demo.orbitCamera = orbit;
            demo.targetOnStart = false;
            demo.targets = R.enemies.Select(g => g.transform).Concat(R.targets).ToArray();
            var map = demoGo.AddComponent<SystemMap>();
            map.carrier = cc;
            map.drive = drive;
            map.systemName = "Nine Hells";
            var landing = demoGo.AddComponent<PlanetLanding>();
            landing.carrier = cc;
            landing.drive = drive;
            landing.fill = fill;
            landing.kit = surfaceKit;
            var docking = demoGo.AddComponent<Docking>();
            docking.carrier = cc;
            docking.drive = drive;
            demoGo.AddComponent<IndustryDirector>().carrier = cc;
            demoGo.AddComponent<IndustryPanel>().carrier = cc;
            demoGo.AddComponent<StartLandedAt>().place = "Heaven";

            foreach (var w in sys.GetComponentsInChildren<WarpPoint>())
                Log($"nine hells: warp point {w.displayName,-18} {w.kind,-13} {w.transform.position.magnitude / 1000f,6:F0} km from the star");

            foreach (var pa in sys.GetComponentsInChildren<ProceduralAsteroid>(true)) pa.Clear();
            foreach (var ps in sys.GetComponentsInChildren<ProceduralSphere>(true)) ps.Clear();
            ProceduralAsteroid.ClearCache();
            ProceduralSphere.ClearCache();
            EditorSceneManager.SaveScene(scene, NineHellsScenePath);
            Log("scene saved " + NineHellsScenePath);
        }

        /// <summary>The Nine Hells' parts, each its own: the sky (Gilgal's ring, the comets and
        /// their streams), and on in later parts the stations, cities, pirates and Vrask.</summary>
        static void NineHellsParts(NineHellsRefs R)
        {
            NineHellsSky(R);
            NineHellsSites(R);
            NineHellsHostiles(R);
            NineHellsEncounters(R);
        }

        static Material MoonLook(string name, int seed) => PlanetMat($"Moon_{name.Replace("'", "")}", MoonMap(name.Replace("'", ""), seed), null, Color.black, 0.03f);

        /// <summary>Degrees a second a planet turns for an orbit `r` out (round it) to keep pace with it.</summary>
        static float GeoSpin(float massRatio, double r)
        {
            double gm = massRatio * starGM;
            double t = 2.0 * Math.PI * Math.Sqrt(r * r * r / gm);
            return (float)(360.0 / t);
        }

        /// <summary>Everything in the Nine Hells on its orbit: the planets round the star, the moons
        /// round their planets, the belts round the star, and whatever is built or waits near each
        /// (in its reach) carried with it.</summary>
        static OrbitalSystem SetUpNineHellsOrbits(NineHellsRefs R, Transform frigate)
        {
            var sys = R.sys;
            var masses = R.data.Masses;
            var orbits = new Dictionary<string, Orbit>();
            foreach (var pd in R.data.planets)
            {
                var planet = R.bodies[pd.name].transform;
                var o = Frame(pd.name, sys, null, planet.position, masses[pd.name], R.points[pd.name].arrivalDistance * 1.5f, new[] { planet });
                o.lagrangeOnMap = pd.yearSetsStarMass;
                orbits[pd.name] = o;
            }
            foreach (var md in R.data.moons)
            {
                var moon = R.bodies[md.name].transform;
                orbits[md.name] = Frame(md.name, sys, orbits[md.planet], moon.position, masses[md.name], R.points[md.name].arrivalDistance * 1.4f, new[] { moon });
            }
            foreach (var fd in R.data.fields)
            {
                var field = sys.Find(fd.name);
                var at = sys.Find(fd.name + " (warp point)");
                orbits[fd.name] = Frame(fd.name, sys, null, at.position, 0f, fd.radius * 2.2f, new[] { field, at });
            }
            // the comets on their ellipses (Patmos near the star now, fast; Ophir far out, slow),
            // and each meteoroid stream on its comet's, a little behind it
            var cometOrbit = new Dictionary<string, Orbit>();
            foreach (var cm in R.comets)
            {
                var o = cm.t.gameObject.AddComponent<Orbit>();
                Vector3 r = cm.t.position - sys.position;
                Vector3 v = Vector3.ProjectOnPlane(cm.t.GetComponent<Comet>().orbitDirection, r).normalized;
                o.eccentricity = cm.e;
                o.semiMajor = cm.near ? r.magnitude / (1.0 - cm.e) : r.magnitude / (1.0 + cm.e);
                o.periapsis = cm.near ? r.normalized : -r.normalized;
                o.ahead = cm.near ? v : -v;
                o.meanAnomaly0 = cm.near ? 0.0 : System.Math.PI;
                o.reach = cm.t.GetComponent<WarpPoint>().arrivalDistance * 1.5f;
                cometOrbit[cm.t.name] = o;
                orbits[cm.t.name] = o;
            }
            foreach (var st in R.streams)
            {
                if (!cometOrbit.TryGetValue(st.comet, out var co)) continue;
                var o = st.t.gameObject.AddComponent<Orbit>();
                o.eccentricity = co.eccentricity;
                o.semiMajor = co.semiMajor;
                o.periapsis = co.periapsis;
                o.ahead = co.ahead;
                o.meanAnomaly0 = co.meanAnomaly0 - st.behind;
                o.reach = 40000f;
                orbits[st.t.name] = o;
            }
            // the Lagrange point sites: on their planet's point, following it round
            foreach (var lg in R.lagrange)
            {
                if (lg.t == null || !orbits.TryGetValue(lg.planet, out var planetOrbit)) continue;
                var go = new GameObject($"{lg.t.name} (L{lg.n})");
                go.transform.SetParent(sys, false);
                var sp = planetOrbit.transform.position - sys.position;
                go.transform.position = sys.position + Orbit.LagrangePoint(sp, planetOrbit.periapsis, planetOrbit.ahead, planetOrbit.massRatio, lg.n);
                lg.t.SetParent(go.transform, true);
                var o = go.AddComponent<Orbit>();
                o.kind = Orbit.Kind.Lagrange;
                o.secondary = planetOrbit;
                o.point = lg.n;
                o.reach = 6000f;
            }
            // what is left at the top of the system (the installations): on the orbit
            // of the body or belt it is nearest, if it is in that one's reach
            foreach (Transform t in sys.Cast<Transform>().ToList())
            {
                if (t.GetComponent<Orbit>() != null || t == R.star || t.name == "Corona") continue;
                var best = Nearest(orbits.Values, t.position, 1.2f);
                if (best != null) t.SetParent(best.transform, true);
            }
            foreach (var e in R.enemies)
            {
                if (e == null) continue;
                var best = Nearest(orbits.Values, e.transform.position, 1.6f);
                if (best != null) e.AddComponent<OrbitHome>().home = best.transform;
            }
            foreach (var o in sys.GetComponentsInChildren<Orbit>(true))
                if (o.kind == Orbit.Kind.Kepler && o.parent == null) o.semiMajor *= DistanceScale;
            var runner = sys.gameObject.AddComponent<OrbitalSystem>();
            runner.system = sys;
            runner.starGM = starGM;
            runner.player = frigate;
            foreach (var o in sys.GetComponentsInChildren<Orbit>(true).OrderBy(o => o.transform.position.magnitude))
                Log($"nine hells: orbit {o.name,-30} round {(o.parent != null ? o.parent.name.Replace(" (orbit)", "") : "Abaddon")}, " +
                    $"a {o.semiMajor / 1000.0:F0} km, period {o.Period / 3600.0:F1} h, reach {o.reach / 1000f:F1} km");
            return runner;
        }

        static Orbit Nearest(IEnumerable<Orbit> orbits, Vector3 p, float slack)
        {
            Orbit best = null;
            float bestD = float.MaxValue;
            foreach (var o in orbits)
            {
                if (o.reach <= 0f) continue;
                float d = Vector3.Distance(p, o.transform.position);
                // (the smallest reach it is in: a moon rather than its planet)
                if (d < o.reach * slack && o.reach < bestD) { bestD = o.reach; best = o; }
            }
            return best;
        }
    }
}

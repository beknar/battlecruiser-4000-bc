using System.Collections.Generic;
using System.Linq;
using CarrierGame;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The orbits of the Kestrel system. The distances are compressed (the whole system is
    /// 260 km across) and so is time: Thalassa goes round the star in 90 minutes, and every
    /// other orbit follows from Kepler's third law with the masses below. So in ten minutes of
    /// watching Cinder goes almost half way round, the moons and stations a good part of the way
    /// round their planets, and the outer planets move visibly on the map. Where the bodies are and
    /// their masses are in Data/star_system.json.
    ///
    /// Each planet, moon, field, comet, orbiting station and Lagrange point becomes a frame
    /// (an Orbit) with what belongs to it inside: a planet with its bases and its elevator, a
    /// moon's frame inside its planet's, a field with its rocks and its base. The masses are
    /// a game's: big enough to put Thalassa's L1 point well clear of the planet (27.6 km out),
    /// and Jovara's just under the limit (1/26 of the star) for its L4 and L5 to be stable.
    /// </summary>
    public static partial class CarrierBuilder
    {
        /// <summary>Thalassa's year (s), which sets the star's mass.</summary>
        const double ThalassaYear = 5400.0;
        static double starGM;

        /// <summary>Each body's mass as a fraction of what it goes round (Data/star_system.json).</summary>
        static Dictionary<string, float> MassRatio => StarSystemData.Load().Masses;

        /// <summary>Thalassa's geostationary orbit (m from its centre): the top of its space
        /// elevator, where the Highport is. The planet's spin is set to match.</summary>
        const float ThalassaGeo = 10000f;

        static void SetStarMass(Vector3 thalassaPos)
        {
            double a = thalassaPos.magnitude;
            starGM = 4.0 * System.Math.PI * System.Math.PI * a * a * a / (ThalassaYear * ThalassaYear);
            OrbitalSystem.StarGM = starGM;
        }

        /// <summary>Degrees a second Thalassa turns for an orbit `ThalassaGeo` out to keep pace with it.</summary>
        static float ThalassaSpin()
        {
            double gm = MassRatio["Thalassa"] * starGM;
            double r = ThalassaGeo;
            double t = 2.0 * System.Math.PI * System.Math.Sqrt(r * r * r / gm);
            return (float)(360.0 / t);
        }

        /// <summary>A circular orbit's plane through a starting offset: toward it, and 90 degrees
        /// on (anticlockwise seen from above, the way everything in the system goes round).</summary>
        static (Vector3 p, Vector3 q) OrbitPlane(Vector3 offset, Vector3? normalHint = null)
        {
            Vector3 p = offset.normalized;
            Vector3 q = Vector3.Cross(normalHint ?? Vector3.down, p).normalized;
            if (q.sqrMagnitude < 0.5f) q = Vector3.Cross(Vector3.right, p).normalized;
            return (p, q);
        }

        /// <summary>A planet's Lagrange point at the start, from where it is.</summary>
        static Vector3 LagrangeAtStart(GameObject planet, int n)
        {
            Vector3 s = planet.transform.position;
            var (p, q) = OrbitPlane(s);
            return Orbit.LagrangePoint(s, p, q, MassRatio[planet.name], n);
        }

        /// <summary>A frame round `contents`, on a circular orbit round `parent` (null: the star).</summary>
        static Orbit Frame(string name, Transform sys, Orbit parent, Vector3 at, float mass, float reach, IEnumerable<Transform> contents,
                           Vector3? normal = null, bool face = false)
        {
            var go = new GameObject(name + " (orbit)");
            go.transform.SetParent(parent != null ? parent.transform : sys, false);
            go.transform.position = at;
            foreach (var c in contents) if (c != null) c.SetParent(go.transform, true);
            var o = go.AddComponent<Orbit>();
            o.parent = parent;
            o.massRatio = mass;
            Vector3 offset = at - (parent != null ? parent.transform.position : sys.position);
            var (p, q) = OrbitPlane(offset, normal);
            o.semiMajor = offset.magnitude;
            o.periapsis = p;
            o.ahead = q;
            o.reach = reach;
            o.faceParent = face;
            return o;
        }

        static double PeriodOf(double a, double gm) => 2.0 * System.Math.PI * System.Math.Sqrt(a * a * a / gm);

        /// <summary>Put everything on its orbit. Returns the system's orbit runner.</summary>
        static OrbitalSystem SetUpOrbits(Transform sys, Transform frigate, List<GameObject> enemies)
        {
            Transform T(string path) => sys.Find(path);
            WarpPoint W(string name) => sys.GetComponentsInChildren<WarpPoint>(true).First(w => w.displayName == name);
            var orbits = new Dictionary<string, Orbit>();

            // the planets round the star (each frame carries the planet, its bases and launchers),
            // and the moons round their planets: all of them in Data/star_system.json
            var data = StarSystemData.Load();
            foreach (var n in data.planets.Select(p => p.name))
            {
                var planet = T(n);
                var w = planet.GetComponent<WarpPoint>();
                var o = Frame(n, sys, null, planet.position, MassRatio[n], w.arrivalDistance * 1.5f, new[] { planet });
                o.lagrangeOnMap = n == "Thalassa" || n == "Jovara";
                orbits[n] = o;
            }
            foreach (var (n, planet) in data.moons.Select(m => (m.name, m.planet)))
            {
                var moon = W(n).transform;
                orbits[n] = Frame(n, sys, orbits[planet], moon.position, MassRatio[n], W(n).arrivalDistance * 1.4f, new[] { moon });
            }
            // stations in orbit: the relay round Ochre, the shipyard round Brontes, the battery
            // over Cinder's poles (a polar orbit, turning to keep its guns toward the planet)
            Frame("Ochre Relay", sys, orbits["Ochre"], T("Installations/Ochre Relay").position, 0f, 4500f, new[] { T("Installations/Ochre Relay") }, face: true);
            Frame("Brontes Shipyard", sys, orbits["Brontes"], T("Installations/Brontes Shipyard").position, 0f, 4500f, new[] { T("Installations/Brontes Shipyard") }, face: true);
            {
                var bat = T("Installations/Cinder Orbital Battery");
                Vector3 off = bat.position - orbits["Cinder"].transform.position;
                Vector3 side = Vector3.Cross(off, Vector3.Cross(Vector3.up, off).sqrMagnitude > 1f ? Vector3.Cross(Vector3.up, off) : Vector3.right).normalized;
                var o = Frame("Cinder Orbital Battery", sys, orbits["Cinder"], bat.position, 0f, 7000f, new[] { bat }, normal: Vector3.Cross(off, side).normalized, face: true);
                orbits["Battery"] = o;
            }
            // the fields round the star, with their bases
            orbits["Inner Belt"] = Frame("Inner Belt", sys, null, T("Inner Belt (warp point)").position, 0f, 10500f,
                new[] { T("Inner Belt"), T("Inner Belt (warp point)"), T("Installations/Deepcut Station"), T("Installations/Belt Smelter") });
            orbits["Outer Drift"] = Frame("Outer Drift", sys, null, T("Outer Drift (warp point)").position, 0f, 13500f,
                new[] { T("Outer Drift"), T("Outer Drift (warp point)"), T("Installations/Corsair Haven") });
            // the industrial sites in space: the gas works round Jovara, the deep yard round the star
            if (T("Installations/Jovara Gas Works") is Transform gas)
                Frame("Jovara Gas Works", sys, orbits["Jovara"], gas.position, 0f, 5000f, new[] { gas }, face: true);
            if (T("Installations/Kestrel Deep Yard") is Transform yard)
                Frame("Kestrel Deep Yard", sys, null, yard.position, 0f, 5000f, new[] { yard }, face: true);

            // the Lagrange points: L4 (the Trojans) and L5 (the depot) of Jovara, L1 of Thalassa (the observatory)
            Orbit Lagrange(string name, Orbit planet, int n, float reach, IEnumerable<Transform> contents)
            {
                var go = new GameObject($"{name} (L{n})");
                go.transform.SetParent(sys, false);
                var s = planet.transform.position - sys.position;
                go.transform.position = sys.position + Orbit.LagrangePoint(s, planet.periapsis, planet.ahead, planet.massRatio, n);
                foreach (var c in contents) if (c != null) c.SetParent(go.transform, true);
                var o = go.AddComponent<Orbit>();
                o.kind = Orbit.Kind.Lagrange;
                o.secondary = planet;
                o.point = n;
                o.reach = reach;
                return o;
            }
            var trojans = Lagrange("Jovara Trojans", orbits["Jovara"], 4, 12000f, new[] { T("Jovara Trojans"), T("Jovara Trojans (warp point)") });
            var swarm = trojans.gameObject.AddComponent<TrojanSwarm>();
            swarm.point = trojans;
            swarm.rocks = trojans.transform.Find("Jovara Trojans");
            orbits["Trojans"] = trojans;
            var depot = Lagrange("Jovara L5 Depot", orbits["Jovara"], 5, 5000f, new[] { T("Installations/Jovara L5 Depot") });
            depot.librationDegrees = 0.25f;     // a slow swing of about 800 m to and fro round L5
            var l1 = Lagrange("Thalassa L1 Observatory", orbits["Thalassa"], 1, 5000f, new[] { T("Installations/Thalassa L1 Observatory") });
            var keep = l1.gameObject.AddComponent<StationKeeping>();
            keep.orbit = l1;
            keep.thrusters = l1.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "Thruster").ToArray();

            // the comets on their ellipses: Iskra at its nearest to the star now (fast), Mirren
            // at its farthest (slow)
            foreach (var comet in sys.GetComponentsInChildren<Comet>(true))
            {
                var o = comet.gameObject.AddComponent<Orbit>();
                Vector3 r = comet.transform.position - sys.position;
                bool near = comet.name.Contains("Iskra");
                double e = near ? 0.6 : 0.45;
                Vector3 v = Vector3.ProjectOnPlane(comet.orbitDirection, r).normalized;
                o.eccentricity = e;
                o.semiMajor = near ? r.magnitude / (1.0 - e) : r.magnitude / (1.0 + e);
                o.periapsis = near ? r.normalized : -r.normalized;
                o.ahead = near ? v : -v;
                o.meanAnomaly0 = near ? 0.0 : System.Math.PI;
                o.reach = comet.GetComponent<WarpPoint>().arrivalDistance * 1.5f;
                orbits[comet.name] = o;
            }

            // each enemy goes along with where it is posted
            foreach (var e in enemies)
            {
                if (e == null) continue;
                Orbit best = null;
                float bestD = float.MaxValue;
                foreach (var o in orbits.Values)
                {
                    if (o.reach <= 0f) continue;
                    float d = Vector3.Distance(e.transform.position, o.transform.position);
                    if (d < o.reach * 1.6f && d < bestD) { bestD = d; best = o; }
                }
                if (best != null) e.AddComponent<OrbitHome>().home = best.transform;
            }

            var runner = sys.gameObject.AddComponent<OrbitalSystem>();
            runner.system = sys;
            runner.starGM = starGM;
            runner.player = frigate;
            var highport = sys.GetComponentsInChildren<Installation>(true).FirstOrDefault(i => i.displayName == "Thalassa Highport");
            if (highport != null) { runner.extraReach = new[] { highport.transform }; runner.extraReachRadius = new[] { 5000f }; }

            foreach (var o in sys.GetComponentsInChildren<Orbit>(true).OrderBy(o => o.transform.position.magnitude))
            {
                string what = o.kind == Orbit.Kind.Lagrange ? $"L{o.point} of {o.secondary.name.Replace(" (orbit)", "")}" :
                    $"round {(o.parent != null ? o.parent.name.Replace(" (orbit)", "") : "Kestrel")}, a {o.semiMajor / 1000.0:F1} km, e {o.eccentricity:F2}";
                Log($"orbit {o.name,-34} {what,-40} period {o.Period / 60.0,6:F1} min, reach {o.reach / 1000f:F1} km");
            }
            Log($"orbits: star GM {starGM:E3}; Thalassa turns in {360f / ThalassaSpin():F0} s, geostationary at {ThalassaGeo / 1000f:F0} km; " +
                $"Thalassa's L1 {Orbit.HillFraction(MassRatio["Thalassa"]) * 128.0:F1} km from it; Jovara's Trojans swing round L4 every " +
                $"{PeriodOf(178000.0, starGM) / Orbit.LibrationFraction(MassRatio["Jovara"]) / 60.0:F0} min");
            return runner;
        }
    }
}

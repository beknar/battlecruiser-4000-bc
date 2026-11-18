using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using K = UnityEngine.InputSystem.Key;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -ninehellstest (with -system ninehells): the second star system. It is the level now, the
    /// Nine Hells with its own save; its star, nine worlds, sixteen moons and two belts are all
    /// there to warp to; the airless worlds have no air and the rest do; and the frigate warps out
    /// to a giant and lands on her home world.
    /// </summary>
    public partial class SystemTest
    {
        bool nineHellsOnly;

        IEnumerator NineHellsTest(Keyboard kb)
        {
            int checks = 0, passed = 0;
            void Check(bool ok, string what)
            {
                checks++;
                if (ok) passed++;
                Log($"{(ok ? "ok" : "FAIL")}: {what}");
            }
            yield return Key(kb, K.H);
            var sys = StarSystems.Current;
            Check(sys.id == "ninehells" && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "NineHells",
                  $"the level is the Nine Hells ({sys.name}), its save {StarSystems.SaveName(sys)}");

            var points = WarpPoint.All.Where(w => w != null).ToList();
            var star = points.FirstOrDefault(w => w.kind == WarpPoint.Kind.Star);
            var planets = points.Where(w => w.kind == WarpPoint.Kind.Planet).Select(w => w.displayName).OrderBy(n => n).ToList();
            var moons = points.Where(w => w.kind == WarpPoint.Kind.Moon).ToList();
            var fields = points.Where(w => w.kind == WarpPoint.Kind.AsteroidField).Select(w => w.displayName).OrderBy(n => n).ToList();
            string[] nine = { "Gilgal", "Goliath", "Judecca", "K'rzyn", "Nineveh", "Paradise", "Rephaim", "Sheol", "The Forge" };
            Check(star != null && star.displayName == "Abaddon", $"its star: {star?.displayName}");
            Check(nine.All(planets.Contains) && planets.Count == 9, $"its nine worlds: {string.Join(", ", planets)}");
            int MoonsOf(string p) => moons.Count(m => m.orbitParent != null && m.orbitParent.displayName == p);
            Check(moons.Count == 16 && MoonsOf("K'rzyn") == 2 && MoonsOf("Rephaim") == 4 && MoonsOf("Judecca") == 3 && MoonsOf("Gilgal") == 7,
                  $"its moons: {moons.Count} (K'rzyn {MoonsOf("K'rzyn")}, Rephaim {MoonsOf("Rephaim")}, Judecca {MoonsOf("Judecca")}, Gilgal {MoonsOf("Gilgal")})");
            Check(fields.Contains("Inner Belt") && fields.Contains("Outer Belt"), $"its belts: {string.Join(", ", fields)}");
            WarpPoint P(string n) => points.FirstOrDefault(w => w.displayName == n);
            Check(PlanetFlight.ScaleHeight(P("The Forge")) == 0f && PlanetFlight.ScaleHeight(P("Sheol")) == 0f && PlanetFlight.ScaleHeight(P("Nineveh")) == 0f &&
                  PlanetFlight.ScaleHeight(P("K'rzyn")) > 0f && PlanetFlight.ScaleHeight(P("Judecca")) > 0f,
                  "the airless worlds (The Forge, Nineveh, Sheol) have no air; K'rzyn and Judecca do");
            Check(P("Judecca") != null && P("Judecca").hostile && P("Sheol").hostile && P("Gilgal").hostile && !P("K'rzyn").hostile,
                  "Judecca, Gilgal and Sheol are hostile; K'rzyn is home");
            float startFrom = P("K'rzyn") != null ? Vector3.Distance(cc.transform.position, P("K'rzyn").Position) : -1f;
            // the sky: Gilgal's ring, the comets and the streams that follow them
            var gilgal = P("Gilgal");
            var ring = gilgal != null ? gilgal.transform.Find("Ring") : null;
            Check(ring != null && ring.GetComponent<MeshRenderer>() != null, $"Gilgal has its ring ({(ring != null ? "there" : "none")})");
            var patmos = P("Comet Patmos");
            var ophir = P("Comet Ophir");
            Check(patmos != null && patmos.kind == WarpPoint.Kind.Comet && ophir != null && ophir.kind == WarpPoint.Kind.Comet,
                  "the comets Patmos and Ophir");
            Orbit O(WarpPoint w) => w != null ? w.GetComponent<Orbit>() : null;
            bool Follows(string stream, WarpPoint comet)
            {
                var so = O(P(stream));
                var co = O(comet);
                return so != null && co != null && System.Math.Abs(so.semiMajor - co.semiMajor) < 1.0 && so.eccentricity == co.eccentricity &&
                       so.meanAnomaly0 < co.meanAnomaly0;
            }
            Check(Follows("Havilah", patmos) && Follows("Sheba", ophir),
                  $"the meteoroid streams Havilah and Sheba on their comets' orbits, behind them " +
                  $"({(P("Sheba") != null && ophir != null ? Vector3.Distance(P("Sheba").Position, ophir.Position) / 1000f : -1f):F0} km behind Ophir)");
            yield return WarpTo(kb, "Sheba", "nh_sheba");
            int meteoroids = 0;
            var sheba = P("Sheba");
            if (sheba != null)
                foreach (Transform t in sheba.transform)
                    if (t.name.StartsWith("Meteoroid") && Vector3.Distance(t.position, cc.transform.position) < 60000f) meteoroids++;
            Check(sheba != null && WarpPoint.Nearest(cc.transform.position) == sheba && meteoroids > 50,
                  $"warped to Sheba: {meteoroids} meteoroids round the frigate ({WarpPoint.Nearest(cc.transform.position)?.displayName})");

            var home = P("K'rzyn");
            Check(home != null && startFrom >= 0f && startFrom < home.arrivalDistance * 1.6f,
                  $"the frigate starts over K'rzyn ({startFrom / 1000f:F0} km off)");

            // out to Rephaim on the map, and back home to land
            yield return WarpTo(kb, "Rephaim", "nh_rephaim");
            var reph = P("Rephaim");
            Check(reph != null && WarpPoint.Nearest(cc.transform.position) == reph, $"warped to Rephaim: {WarpPoint.Nearest(cc.transform.position)?.displayName}");
            yield return WarpTo(kb, "K'rzyn", "nh_krzyn");
            var landing = PlanetLanding.Instance;
            if (landing != null && home != null)
            {
                landing.LandOn(home);
                yield return WaitLanding(landing, PlanetLanding.State.OnSurface, 90f);
                yield return new WaitForSeconds(2f);
                Shot("nh_krzyn_surface");
                Check(landing.Current == PlanetLanding.State.OnSurface && landing.Body == home, $"landed on K'rzyn: {landing.Current}, '{landing.Site?.description}'");
                landing.TakeOff();
                yield return WaitLanding(landing, PlanetLanding.State.InSpace, 30f);
                Check(landing.Current == PlanetLanding.State.InSpace, "back in orbit");
            }
            Log($"{(passed == checks ? "PASS" : "FAIL")}: the Nine Hells ({passed} of {checks})");
        }
    }
}

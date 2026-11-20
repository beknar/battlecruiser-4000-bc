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
            float startOverHeaven = P("Heaven") != null ? Vector3.Distance(cc.transform.position, P("Heaven").Position) : -1f;
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
            Check(startOverHeaven >= 0f && startOverHeaven < 200000f, $"the game starts over Heaven, K'rzyn's capital ({startOverHeaven / 1000f:F0} km above it)");

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
            // ---- the friendly places
            string[] docks = { "Heaven Highport", "Bastion", "Rephaim Fuel Station", "Nineveh Deep Watch", "K'rzyn Orbital Refinery",
                               "K'rzyn Orbital Factory", "Heaven Space Dock", "Rephaim Mineral Refiner" };
            var missing = docks.Where(n => !Installation.All.Any(i => i != null && i.displayName == n && i.canDock && i.Berths.Length >= 15)).ToList();
            Check(missing.Count == 0, $"the stations and sites to dock at, 15 berths each ({docks.Length - missing.Count} of {docks.Length}{(missing.Count > 0 ? "; missing " + string.Join(", ", missing) : "")})");
            string[] grounds = { "Heaven", "Goliath Research Station", "Ruins of the Ancients", "Nineveh Observatory" };
            var noGround = grounds.Where(n => P(n) == null || P(n).kind != WarpPoint.Kind.SurfaceBase || P(n).landsOn == null).ToList();
            Check(noGround.Count == 0, $"the places on the ground ({grounds.Length - noGround.Count} of {grounds.Length}{(noGround.Count > 0 ? "; missing " + string.Join(", ", noGround) : "")})");
            var heavenWp = P("Heaven");
            Check(heavenWp != null && heavenWp.shielded && P("Bastion") != null && P("Bastion").GetComponentInParent<Orbit>()?.kind == Orbit.Kind.Lagrange,
                  "Heaven is under its shield; Bastion keeps K'rzyn's L4");
            // a strike on Heaven breaks on the shield
            if (heavenWp != null && OrbitalStrikes.SiteOf(heavenWp, out var hb, out var hs))
            {
                int held0 = OrbitalStrikes.ShieldHeld;
                var r = OrbitalStrikes.Hit(hb, hs.su, hs.sv, 400f, 20f, "A kinetic strike", false);
                Check(OrbitalStrikes.ShieldHeld == held0 + 1 && r.buildings == 0, $"a strike on Heaven breaks on its shield ({r.buildings} buildings ruined)");
            }
            else Check(false, "Heaven's site on K'rzyn");
            // ---- the enemies in their strongholds
            string[] holds = { "Dis", "Malebolge", "Jericho Base", "Bethel Base", "Sheol Outpost" };
            var weak = holds.Where(n => P(n) == null || !P(n).hostile || P(n).kind != WarpPoint.Kind.SurfaceBase).ToList();
            Check(weak.Count == 0, $"the strongholds: {holds.Length - weak.Count} of {holds.Length} hostile bases{(weak.Count > 0 ? "; missing " + string.Join(", ", weak) : "")}");
            int sats = DefenseGrid.All.Count(g => g != null && (g.gridName.Contains("Defence Platform") || g.gridName.Contains("Defence Satellite")));
            Check(sats == 12, $"their defence platforms and satellites in orbit over them: {sats}");
            var picket = FindObjectsByType<EncounterGroup>(FindObjectsSortMode.None).FirstOrDefault(g => g.groupName == "Gilgal picket fleet");
            var vpicket = FindObjectsByType<EncounterGroup>(FindObjectsSortMode.None).FirstOrDefault(g => g.groupName == "Sheol picket");
            Check(picket != null && picket.pilots.Length == 19 && vpicket != null && vpicket.pilots.Length == 10 &&
                  vpicket.pilots.All(x => x != null && x.GetComponent<VraskShip>() != null),
                  $"the Gilgal picket fleet ({picket?.pilots.Length ?? 0} ships) and the Vrask picket over Sheol ({vpicket?.pilots.Length ?? 0})");
            Check(P("Jericho") != null && P("Jericho").hostile && P("Bethel").hostile, "Jericho and Bethel are the pirates'");
            // ---- what is at each place: a band lying in wait warns of danger there, but doesn't
            // make a friendly place the enemy's (its surface stays friendly, its station one to dock at)
            string[] warned = { "Paradise", "Anak", "Rephaim Fuel Station", "Inner Belt", "Outer Belt", "Comet Patmos" };
            var wrongly = warned.Where(n => P(n) == null || P(n).hostile || !P(n).dangerNear || !P(n).Threatened).ToList();
            Check(wrongly.Count == 0, $"the places bands lie in wait by are warned of, not made hostile ({warned.Length - wrongly.Count} of {warned.Length}{(wrongly.Count > 0 ? "; wrong: " + string.Join(", ", wrongly) : "")})");
            // the gas giants: an orbit over the equator as well as the pole
            var eq = new[] { "Rephaim", "Gilgal" }.Where(n => P($"{n} Equatorial Orbit") == null).ToList();
            Check(eq.Count == 0, $"the gas giants have their equatorial orbits{(eq.Count > 0 ? "; missing " + string.Join(", ", eq) : "")}");
            // something at The Forge's high orbit: the smugglers' derelict, to salvage
            var forgeOrbit = P("The Forge High Orbit");
            var derelict = Wreck.All.FirstOrDefault(x => x != null && x.Stays && !x.Empty);
            float derelictKm = forgeOrbit != null && derelict != null ? Vector3.Distance(derelict.transform.position, forgeOrbit.Position) / 1000f : -1f;
            Check(derelict != null && derelictKm >= 0f && derelictKm < 1f && derelict.Tonnes > 0f,
                  $"at The Forge High Orbit, a derelict to salvage: {(derelict != null ? $"{derelict.Name}, {derelict.Summary()}" : "none")} ({derelictKm:F1} km off it)");
            // every orbit landmark says what is there
            var bare = Landmarks.All.Where(x => x != null && x.kindLabel == "orbit" && string.IsNullOrEmpty(x.description)).Select(x => x.displayName).ToList();
            Check(bare.Count == 0, $"every orbit landmark says what is there{(bare.Count > 0 ? "; not: " + string.Join(", ", bare) : "")}");
            // ---- the encounters: 18 bands, some on patrol, seen moving on the map
            string[] bands = { "Scavenger skiffs", "Belt claim-jumpers", "Patmos ice thieves", "Vrask scout", "Outer Belt lurkers", "Forge smugglers",
                               "Anak frigate pair", "Vrask hunting pair", "Fuel station raiders", "Rephaim corsair patrol",
                               "Judecca blockade", "Vrask strike group", "Sheba destroyer squadron", "Ophir ambush",
                               "The Butcher's flagship", "Vrask brood", "Behemoth battle group", "Vrask war fleet" };
            var groups = FindObjectsByType<EncounterGroup>(FindObjectsSortMode.None);
            var absent = bands.Where(n => !groups.Any(g => g.groupName == n && g.pilots != null && g.pilots.Length > 0 && g.pilots.All(x => x != null))).ToList();
            Check(absent.Count == 0, $"the 18 encounters ({bands.Length - absent.Count} of {bands.Length}{(absent.Count > 0 ? "; missing " + string.Join(", ", absent) : "")})");
            var patrols = EncounterPatrol.All.Where(x => x != null).ToList();
            var far = patrols.OrderByDescending(x => Vector3.Distance(x.transform.position, cc.transform.position)).FirstOrDefault();
            float a0 = far != null ? far.Angle : 0f;
            Vector3 p0 = far != null ? far.transform.position - far.body.Position : Vector3.zero;
            yield return new WaitForSeconds(4f);
            bool moved = far != null && far.Angle > a0 && Vector3.Distance(far.transform.position - far.body.Position, p0) > 1f;
            var onMap = patrols.Where(x => x.TryGetComponent(out WarpPoint w) && w.hostile && Places.IsPlace(w)).Count();
            Check(patrols.Count == 7 && moved && onMap == 7,
                  $"7 of them on patrol, places of their own on the map ({onMap}); the {far?.group?.groupName} moving round {far?.body?.displayName} ({a0:F3} -> {(far != null ? far.Angle : 0f):F3} deg)");
            // docked at the top of the elevator
            yield return WarpTo(kb, "Heaven Highport", "nh_highport");
            bool docked = false;
            yield return DockTest(kb, "Heaven Highport", "nh_highport", r2 => docked = r2);
            Check(docked, "docked at Heaven Highport, at the top of the space elevator, and away again");
            // down at the research station on Goliath: repaired on its pad
            var research = P("Goliath Research Station");
            if (landing != null && research != null)
            {
                yield return WarpTo(kb, "Goliath Research Station", "nh_research");
                var hull = cc.GetComponent<Health>();
                if (hull != null) hull.Damage(900f);
                // (a hit counts as being under fire a few seconds: no landing till it has passed)
                yield return new WaitForSeconds(drive.recentHitSeconds + 0.5f);
                int repairs0 = PlanetLanding.LandedRepairs;
                float hp0 = hull != null ? hull.Current : 0f;
                // (straight down by it, as the other landing tests go; K from this far up flies the whole descent)
                landing.LandOn(research);
                yield return WaitLanding(landing, PlanetLanding.State.OnSurface, 120f);
                // (the station's own pad, under the ship: set down on it from just over it)
                yield return new WaitForSeconds(2f);
                var site = IndustrySite.All.FirstOrDefault(x => x != null && x.siteName == "Goliath Research Station" && x.pad != null);
                if (site != null)
                {
                    cc.transform.position = site.pad.position + site.pad.up * 40f;
                    cc.SetMotion(0f);
                    cc.throttle = 0f;
                }
                Log($"  the research station's pad {(site != null ? "found" : "not found")}; landed {landing.Landed}");
                float settle = Time.time + 30f;
                // (down onto it on the lift thrusters)
                while (Time.time < settle && !landing.Landed) { demo.autoElevator = -0.7f; yield return null; }
                demo.autoElevator = null;
                yield return new WaitForSeconds(2f);
                Shot("nh_research_pad");
                Check(landing.OnPad && PlanetLanding.LandedRepairs > repairs0 && hull != null && hull.Current > hp0,
                      $"down on the research station's pad on Goliath ({landing.OnPad}): repaired, hull {hp0:F0} -> {(hull != null ? hull.Current : 0f):F0}; '{Notices.Last}'");
                landing.TakeOff();
                yield return WaitLanding(landing, PlanetLanding.State.InSpace, 30f);
            }
            Log($"{(passed == checks ? "PASS" : "FAIL")}: the Nine Hells ({passed} of {checks})");
        }
    }
}

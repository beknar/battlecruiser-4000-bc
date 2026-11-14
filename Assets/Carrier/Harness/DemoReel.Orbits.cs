using System.Collections;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The orbits and stations reel: StarSystem.exe -reel &lt;folder&gt; -orbits. It shows what the
    /// last two features added: the orbits (the system map in time-lapse, Selene going round
    /// Thalassa), the Lagrange points (the L1 observatory's station-keeping burns, the L5 depot,
    /// the Trojans at L4), the geostationary Highport and its space elevator, docking, the five
    /// launch systems, the stations, and the armed places: the pirate asteroid base, the orbital
    /// battery, and the bases on Selene and Cinder. It uses the feature reel's captions, fades
    /// and camera helpers.
    /// </summary>
    public partial class DemoReel
    {
        /// <summary>Recording the orbits and stations reel (-orbits).</summary>
        public static bool Orbits;

        OrbitalSystem orbitSys;

        IEnumerator OrbitReel()
        {
            recording = false;
            while (frame < Warmup) yield return null;
            landing = PlanetLanding.Instance;
            sysDrive = cc.GetComponent<WarpDrive>();
            hullF = cc.GetComponent<Health>();
            shieldF = cc.GetComponentInChildren<Shield>(true);
            orbitSys = OrbitalSystem.Instance;
            if (hullF != null) hullF.cannotDie = true;     // the reel must not end in a wreck
            SideGuns(false);
            Note($"orbits reel: {(orbitSys != null ? orbitSys.Orbits.Count : 0)} orbits, {Installation.All.Count} installations, " +
                 $"{FindObjectsByType<LaunchSystem>(FindObjectsSortMode.None).Length} launchers");
            if (orbitSys == null) { Note("no orbital system in the scene"); finished = true; yield break; }

            yield return OrbitsIntro();
            yield return MapTimeLapse();
            yield return MoonTimeLapse();
            yield return LagrangeL1();
            yield return LagrangeJovara();
            yield return HighportAndElevator();
            yield return DockAtHighport();
            yield return Launchers();
            yield return Stations();
            yield return HostileBase("Corsair Haven", "ASTEROID BASE", "Corsair Haven: a pirate base in an asteroid, and its six gun emplacements", 4300f, 470);
            yield return HostileBase("Cinder Orbital Battery", "ORBITAL WEAPONS", "The Cinder Orbital Battery: gun platforms and a beam lance over Cinder's pole", 5600f, 475);
            yield return SurfaceBases();
            yield return OrbitsEnd();

            ReportSystems();
            Note($"the end: dockings {(Docking.Instance != null ? Docking.Instance.Dockings : 0)}, landings {landing.Landings}");
            finished = true;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Run the orbits `rate` times faster, easing there over `seconds`.</summary>
        IEnumerator TimeLapse(float rate, float seconds)
        {
            float from = orbitSys.timeLapse;
            for (float k = 0f; k < seconds; k += Time.deltaTime)
            {
                orbitSys.timeLapse = Mathf.Lerp(from, rate, Smooth(k / seconds));
                yield return null;
            }
            orbitSys.timeLapse = rate;
        }

        Installation Inst(string name) => Installation.All.FirstOrDefault(i => i != null && i.displayName == name);

        /// <summary>A heading for the camera looking at `p` with the star more or less behind it
        /// (so what it looks at is lit), turned `extra` degrees round.</summary>
        float SunBehind(Vector3 p, float extra)
        {
            var star = WP("Kestrel");
            return (star != null ? Bearing(star.Position, p) : cc.Heading) + extra;
        }

        // ------------------------------------------------------------------ the scenes

        IEnumerator OrbitsIntro()
        {
            var home = WP("Thalassa");
            cc.throttle = 0.12f;
            LookPast(400, home.Position, D * 2.2f, 25f, float.NaN, 3f);
            yield return Wait(1f);
            yield return FadeIn(1f);
            Caption("BATTLECRUISER 4000 BC", "Star System: orbits, stations and launch systems", 5.5f);
            yield return Wait(5.5f);
            yield return FadeOut();
            cc.throttle = 0f;
        }

        // ---------------------------------------------------- orbits

        /// <summary>The orbit clock back to where it would be without the time-lapse (off camera):
        /// the system as it was laid out, so the rest of the reel finds things where they were put
        /// (the capital ships wait on the way in from Thalassa).</summary>
        void UndoTimeLapse(double clock0, float at0)
        {
            orbitSys.timeLapse = 1f;
            orbitSys.ContinueFrom(clock0 + (Time.timeSinceLevelLoad - at0));
        }

        IEnumerator MapTimeLapse()
        {
            double clock0 = orbitSys.Time;
            float at0 = Time.timeSinceLevelLoad;
            sysMap.Open();
            sysMap.Select(WP("Thalassa"));
            yield return Wait(0.4f);
            yield return FadeIn();
            Caption("ORBITS", $"Every planet, moon and comet on its Kepler orbit, here sped up {MapLapse:N0} times", 6f);
            yield return TimeLapse(MapLapse, 2f);
            yield return Wait(4.5f);
            sysMap.Select(WP("Thalassa Highport"));
            Caption("ORBITS", "The five Lagrange points of Thalassa and Jovara go round with them", 5f);
            yield return Wait(5f);
            sysMap.Select(WP("Comet Iskra"));
            Caption("ORBITS", "Comet Iskra swings out from the star on a long ellipse", 4.5f);
            yield return Wait(4f);
            Note($"map time-lapse: orbit clock {orbitSys.Time:F0} s at {R:F1} s into the reel");
            yield return FadeOut();
            UndoTimeLapse(clock0, at0);
            Clear();
        }

        /// <summary>How much faster the orbits go on the map: Cinder's year (8 days) in about
        /// ten seconds, the outer planets a little way round.</summary>
        const float MapLapse = 60000f;

        /// <summary>Hours and minutes, for a caption.</summary>
        static string Span(double seconds) => seconds >= 5400.0 ? $"{seconds / 3600.0:F0} hours" : $"{seconds / 60.0:F0} minutes";

        IEnumerator MoonTimeLapse()
        {
            var thalassa = WP("Thalassa");
            var selene = WP("Selene");
            if (thalassa == null || selene == null) yield break;
            Jump(thalassa);
            yield return Wait(1f);
            double clock0 = orbitSys.Time;
            float at0 = Time.timeSinceLevelLoad;
            // far out over Thalassa with the star behind the camera, Selene's whole orbit in view
            // (thousands of kilometres out: ScaledSpace draws the bodies for this camera)
            float a = Vector3.Distance(selene.Position, thalassa.Position);
            var orbit = OrbitalSystem.OrbitOf(selene.transform);
            double period = orbit != null ? orbit.Period : 18.0 * 3600.0;
            // about two thirds of the way round in the eight seconds shown, at a round number
            float rate = (float)(0.65 * period / 8.0);
            float mag = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(rate)));
            rate = Mathf.Round(rate / mag) * mag;
            // (looking along the star's light, a little from above and to one side: the lit face)
            var star = WP("Kestrel");
            Vector3 light = star != null ? (thalassa.Position - star.Position).normalized : Vector3.forward;
            Vector3 across = Vector3.Cross(Vector3.up, light).normalized;
            ShotAlong(410, thalassa.transform, Vector3.zero, (light * 0.8f + Vector3.down * 0.5f + across * 0.3f).normalized, a * 2.4f, 0f);
            orbitSys.timeLapse = rate * 0.25f;
            yield return FadeIn();
            Caption("ORBITS", $"Selene goes round Thalassa every {Span(period)}, {a / 1000f:N0} km out: here {rate:N0} times faster", 6f);
            Note($"Selene: {a / 1000f:F0} km from Thalassa, period {period / 3600.0:F1} h, shown {rate:N0}x");

            yield return TimeLapse(rate, 1.5f);
            yield return Wait(7f);
            yield return FadeOut();
            UndoTimeLapse(clock0, at0);
            Clear();
        }

        // ---------------------------------------------------- Lagrange points

        IEnumerator LagrangeL1()
        {
            var wp = WP("Thalassa L1 Observatory");
            var keeper = FindAnyObjectByType<StationKeeping>();
            var thalassa = WP("Thalassa");
            if (wp == null || keeper == null || thalassa == null) { Note("no L1 observatory"); yield break; }
            Jump(wp);
            yield return Wait(1f);
            // from its sunward side, looking past it at the planet it balances against
            Vector3 look = (thalassa.Position - keeper.transform.position).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, look).normalized;
            ShotAlong(450, keeper.transform, Vector3.zero, (look + side * 0.55f - Vector3.up * 0.25f).normalized, 1500f, 1.5f);
            yield return FadeIn();
            Caption("LAGRANGE POINT L1", $"{Vector3.Distance(keeper.transform.position, thalassa.Position) / 1000f:N0} km sunward of Thalassa: an unstable balance between the star's pull and the planet's", 5.5f);
            // drifting off: it fires its thrusters to get back (at the real size the drift takes
            // days to grow, so it is shown just past the point where it burns)
            int burns = keeper.Burns;
            yield return Wait(5.5f);
            Caption("LAGRANGE POINT L1", "Left alone it drifts off, faster and faster, so the observatory fires its thrusters to stay", 4.5f);
            yield return Wait(0.8f);
            keeper.Nudge(keeper.tolerance * 1.02f);
            yield return Wait(3.7f);
            Note($"L1: {keeper.Burns - burns} burns while watching");
            yield return FadeOut();
            Clear();
        }

        IEnumerator LagrangeJovara()
        {
            var depot = WP("Jovara L5 Depot");
            if (depot != null)
            {
                Jump(depot);
                yield return Wait(1f);
                Shot(455, depot.transform, Vector3.zero, 2600f, SunBehind(depot.Position, 35f), 16f, 2.5f);
                yield return FadeIn();
                Caption("LAGRANGE POINT L5", "60° behind Jovara, a stable point: the propellant depot swings slowly round it", 5.5f);
                yield return Wait(5.5f);
                yield return FadeOut();
            }
            var trojans = WP("Jovara Trojans");
            if (trojans != null)
            {
                Jump(trojans);
                yield return Wait(1f);
                LookPast(456, trojans.Position, D * 3.5f, -20f, 14f, 2f);
                yield return FadeIn();
                Caption("LAGRANGE POINT L4", "60° ahead of Jovara: the Trojan asteroids gather round it", 5f);
                yield return Wait(5f);
                yield return FadeOut();
            }
            Clear();
        }

        // ---------------------------------------------------- the Highport and the space elevator

        IEnumerator HighportAndElevator()
        {
            var hp = Inst("Thalassa Highport");
            if (hp == null) { Note("no Highport"); yield break; }
            Transform t = hp.transform;
            // the frigate off to one side, out of the way
            Place(t.TransformPoint(new Vector3(2400f, 300f, 900f)), t.position);
            yield return Wait(1f);

            // from outside, looking in: the ring, the docking arm, the tether down to the planet
            Vector3 look = (-t.forward + t.right * 0.45f - t.up * 0.3f).normalized;
            ShotAlong(420, t, Vector3.zero, look, 3600f, 1.2f);
            yield return FadeIn();
            var home = WP("Thalassa");
            float up = home != null ? (Vector3.Distance(t.position, home.Position) - home.bodyRadius) / 1000f : 0f;
            // (the planet's own turning: the station is carried round with it)
            var turn = home != null ? home.GetComponent<Drift>() : null;
            double day = turn != null && turn.spin.magnitude > 0f ? 360.0 / turn.spin.magnitude : 6.0 * 3600.0;
            Caption("THALASSA HIGHPORT", $"In geostationary orbit, {up:N0} km up: Thalassa turns once in {Span(day)}, and so does the station", 6f);
            Note($"Highport {up:F0} km up, Thalassa's day {day / 3600.0:F1} h");
            yield return Wait(6.5f);

            // a climber on the tether
            var lift = FindAnyObjectByType<ElevatorClimbers>();
            if (lift != null && lift.climbers != null)
            {
                Transform car = null;
                float best = float.MaxValue;
                foreach (var c in lift.climbers)
                {
                    if (c == null) continue;
                    // the one most nearly halfway up
                    float k = Vector3.Distance(lift.bottom, c.localPosition) / Mathf.Max(1f, Vector3.Distance(lift.bottom, lift.top));
                    if (Mathf.Abs(k - 0.5f) < best) { best = Mathf.Abs(k - 0.5f); car = c; }
                }
                if (car != null)
                {
                    Vector3 tether = lift.transform.up;
                    Vector3 across = Vector3.Cross(tether, Vector3.up);
                    if (across.sqrMagnitude < 0.01f) across = Vector3.Cross(tether, Vector3.forward);
                    ShotAlong(421, car, Vector3.zero, (across.normalized - tether * 0.35f).normalized, 320f, 0f);
                    Caption("THE SPACE ELEVATOR", "Climbers ride the tether from a platform in Thalassa's sea up to the Highport", 5.5f);
                    yield return Wait(5.5f);
                }
            }
            yield return FadeOut();
            Clear();
        }

        IEnumerator DockAtHighport()
        {
            var hp = Inst("Thalassa Highport");
            var dock = Docking.Instance;
            if (hp == null || dock == null) yield break;
            // something to repair and something to sell; then out of combat before docking
            // (the hold may already have what the frigate mined)
            if (hullF != null) hullF.Damage(900f);
            if (demo.Cargo.Used < 50f)
            {
                demo.Cargo.Add(Mineral.Iron, 140f);
                demo.Cargo.Add(Mineral.Nickel, 40f);
                demo.Cargo.Add(Mineral.Platinum, 6f);
            }
            yield return Wait(sysDrive.recentHitSeconds + 0.5f);
            Vector3 berth = hp.BerthPosition;
            Vector3 way = hp.berth != null ? hp.berth.forward : hp.transform.forward;
            Place(berth + way * 1000f + Vector3.up * 40f, berth);
            yield return Wait(0.8f);
            LookPast(430, berth, D * 1.7f, 28f, 10f, 1.5f);
            yield return FadeIn();
            Caption("DOCKING", "Dock (K) at a friendly station: the hull is repaired and the ore sold", 6f);
            yield return Wait(1.2f);
            bool ok = dock.Dock();
            float until = Time.time + 8f;
            while (dock.Current != Docking.State.Docked && Time.time < until) yield return null;
            Note($"docking at the Highport: {(ok ? "docked " + (dock.Current == Docking.State.Docked) : "refused")}; repaired {dock.LastRepair:F0}, sold {dock.LastSoldTonnes:F0} t for {dock.LastSoldCredits:N0} cr");
            // moored at the berth, the ring turning beyond it
            // from out beyond the berth, looking back in: the frigate moored, the station behind it
            Vector3 across = Vector3.Cross(way, hp.transform.up).normalized;
            ShotAlong(431, cc.transform, demo.ShipViewOffset, (-way + across * 0.6f - hp.transform.up * 0.25f).normalized, D * 2.6f, 1.5f);
            yield return Wait(4.5f);
            dock.Undock();
            yield return Wait(2.5f);
            yield return FadeOut();
            Clear();
        }

        // ---------------------------------------------------- launch systems

        IEnumerator Launchers()
        {
            (string name, string title, string sub)[] list =
            {
                ("Thalassa Launch Loop", "LAUNCH LOOP", "Thalassa: payloads ride a cable loop held up over the sea, and are let go"),
                ("Ochre StarTram", "STARTRAM", "Ochre: a maglev tube along the desert, rising out of the thin air"),
                ("Ochre Spin Launcher", "SPIN LAUNCHER", "Ochre: a vacuum centrifuge whirls a payload up and lets it go"),
                ("Selene Mass Driver", "MASS DRIVER", "Selene: an electromagnetic catapult flings ore off the airless moon"),
                ("Steropes Slingatron", "SLINGATRON", "Steropes: payloads spiral outward, faster each turn, and leave along a ramp"),
            };
            int n = 0;
            foreach (var (name, title, sub) in list)
            {
                var wp = WP(name);
                var ls = wp != null ? wp.GetComponentInParent<LaunchSystem>() : null;
                if (ls == null || ls.track == null || ls.track.Length < 2) { Note($"no launcher {name}"); continue; }
                Jump(wp);
                // off camera, till the next launch is about to go
                float until = Time.time + ls.interval + 2f;
                while (ls.NextLaunchIn > 1.1f && Time.time < until) yield return null;

                // the whole track, from above and to the side, the ground below it
                Vector3 first = ls.transform.TransformPoint(ls.track[0]);
                Vector3 last = ls.transform.TransformPoint(ls.track[ls.track.Length - 1]);
                Vector3 centre = Vector3.zero;
                foreach (var p in ls.track) centre += ls.transform.TransformPoint(p);
                centre /= ls.track.Length;
                float span = 0f;
                foreach (var p in ls.track) span = Mathf.Max(span, Vector3.Distance(centre, ls.transform.TransformPoint(p)));
                var body = wp.landsOn != null ? wp.landsOn : wp;
                Vector3 up = (centre - body.Position).normalized;
                Vector3 along = Vector3.ProjectOnPlane(last - first, up);
                if (along.sqrMagnitude < 1f) along = Vector3.ProjectOnPlane(ls.transform.forward, up);
                Vector3 side = Vector3.Cross(up, along.normalized);
                Vector3 look = (side - up * 0.55f + along.normalized * 0.25f).normalized;
                ShotAlong(440 + n, ls.transform, centre - ls.transform.position, look, Mathf.Max(1100f, span * 1.7f + 450f), 0f);
                int launched = ls.Launched;
                yield return FadeIn();
                Caption(title, sub, ls.trackTime + 3.5f);
                yield return Wait(ls.trackTime + 3.8f);
                Note($"{name}: {ls.Launched - launched} launched while watching; track {ls.TrackLength:F0} m, {ls.exitSpeed:F0} m/s off the end");
                yield return FadeOut();
                Clear();
                n++;
            }
        }

        // ---------------------------------------------------- stations

        IEnumerator Stations()
        {
            (string name, string title, string sub, float dist)[] list =
            {
                ("Deepcut Station", "ASTEROID BASE", "Deepcut Station: a mining base dug into a rock at the edge of the Inner Belt", 3.6f),
                ("Ochre Relay", "INSTALLATION", "Ochre Relay: three big dishes on a 480 m mast, linking the outer system to the inner", 2.6f),
                ("Brontes Shipyard", "INSTALLATION", "Brontes Shipyard: a drydock in orbit of Jovara's inner moon, a destroyer half built", 3.2f),
            };
            int n = 0;
            foreach (var (name, title, sub, dist) in list)
            {
                var wp = WP(name);
                if (wp == null) continue;
                Jump(wp);
                yield return Wait(1f);
                Shot(460 + n, wp.transform, Vector3.zero, Mathf.Max(1800f, wp.bodyRadius * dist * 2f), SunBehind(wp.Position, n % 2 == 0 ? 35f : -35f), 14f, 3f);
                yield return FadeIn();
                Caption(title, sub, 5f);
                yield return Wait(5f);
                yield return FadeOut();
                n++;
            }
            Clear();
        }

        // ---------------------------------------------------- armed places

        /// <summary>Close on a hostile installation until its guns open up, and trade fire.</summary>
        IEnumerator HostileBase(string name, string title, string sub, float standOff, int shot)
        {
            var wp = WP(name);
            var grid = DefenseGrid.All.Where(g => g != null).OrderBy(g => wp != null ? Vector3.Distance(g.transform.position, wp.Position) : 0f).FirstOrDefault();
            if (wp == null || grid == null || grid.weapons == null) { Note($"no {name}"); yield break; }
            var gun = grid.weapons.Where(w => w != null).OrderBy(w => Vector3.Distance(w.transform.position, wp.ArrivalPoint(cc.transform.position))).FirstOrDefault();
            if (gun == null) { Note($"{name} has no guns left"); yield break; }
            Repair();
            Vector3 outward = (gun.transform.position - wp.Position).normalized;
            Place(ClearSpot(gun.transform.position + outward * standOff, outward), gun.transform.position);
            demo.SelectTarget(gun.transform);
            SideGuns(true);
            // off camera, till the guns open up
            float until = Time.time + 8f;
            while (!grid.Engaged && Time.time < until) yield return null;
            yield return Wait(1.5f);
            Note($"{name}: engaged {grid.Engaged}, {grid.GunsLeft} guns");
            LookPast(shot, gun.transform.position, D * 1.6f, 24f, 9f, 1.5f);
            yield return FadeIn();
            Caption(title, sub, 5.5f);
            if (demo.MainGun != null && demo.MainGun.Current == MainBeam.State.Idle) demo.ChargeMainGun();
            yield return Wait(5f);
            // from beside the frigate, the fire going both ways
            Shot(shot + 1, cc.transform, demo.ShipViewOffset, D * 2.4f, FrameBearing(cc.transform.position, gun.transform.position) + 80f, 12f, 2f);
            yield return Wait(5f);
            yield return FadeOut();
            SideGuns(false);
            cc.SetTarget(null);
            Clear();
            Repair();
        }

        IEnumerator SurfaceBases()
        {
            (string name, string sub)[] bases =
            {
                ("Selene Base", "Selene Base: a colony on Thalassa's moon, with its planet in the sky"),
                ("Cinder Fortress", "Cinder Fortress: walls, bunkers and batteries on the lava plains, and its garrison"),
            };
            int n = 0;
            foreach (var (name, sub) in bases)
            {
                var wp = WP(name);
                if (wp == null) continue;
                // from orbit over it (a landing from far off takes a long way down)
                Jump(wp);
                yield return Wait(1f);
                if (!landing.LandOn(wp)) { Note($"couldn't land at {name}"); continue; }
                yield return WaitLanding(PlanetLanding.State.OnSurface, 90f);
                if (landing.Current != PlanetLanding.State.OnSurface) { Note($"didn't get down at {name}: {landing.Current}"); continue; }
                yield return Wait(2f);
                var surf = landing.Surface;
                var group = surf != null ? surf.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Base: " + name) : null;
                if (group == null || group.childCount == 0) { Note($"no buildings at {name}"); yield return TakeOffNow(); continue; }
                Vector3 middle = Vector3.zero;
                foreach (Transform c in group) middle += c.position;
                middle /= group.childCount;
                // the garrison (if any) wakes up
                foreach (var g in surf.GetComponentsInChildren<GroundGarrison>(true))
                    if (g != null && Vector3.Distance(g.transform.position, middle) < 3500f) g.Raise();
                perFrame = () =>
                {
                    if (surf != null) HoldHeight(surf, 180f);
                    demo.autoPitch = 0f;
                    demo.autoRudder = 0f;
                };
                cc.throttle = 0.15f;
                Shot(480 + n * 2, group, middle - group.position, 1500f, Bearing(cc.transform.position, middle) + 35f, 20f, 4f);
                yield return FadeIn();
                Caption("PLANETARY BASES", sub, 6f);
                Note($"at {name}: {group.childCount} buildings");
                yield return Wait(5.5f);
                // the frigate over it
                Shot(481 + n * 2, cc.transform, demo.ShipViewOffset, D * 2.2f, FrameBearing(cc.transform.position, middle) - 30f, 12f, 3f);
                yield return Wait(4f);
                yield return FadeOut();
                perFrame = null;
                Clear();
                cc.throttle = 0f;
                yield return TakeOffNow();
                n++;
            }
        }

        IEnumerator OrbitsEnd()
        {
            var home = WP("Thalassa");
            if (home == null) yield break;
            Repair();
            Jump(home);
            yield return Wait(1f);
            cc.throttle = 0.12f;
            Shot(499, cc.transform, demo.ShipViewOffset, D * 1.6f, FrameBearing(cc.transform.position, home.Position) + 160f, 8f, 3f);
            orbitSys.timeLapse = 40f;
            yield return FadeIn();
            float t0 = Time.time;
            perFrame = () => cam.distance = Mathf.Lerp(D * 1.6f, D * 6f, Smooth((Time.time - t0) / 6f));
            Caption("BATTLECRUISER 4000 BC", "Orbits  ·  Lagrange points  ·  stations  ·  launch systems", 6f);
            yield return Wait(5f);
            yield return FadeOut(1.5f);
            perFrame = null;
            orbitSys.timeLapse = 1f;
        }
    }
}

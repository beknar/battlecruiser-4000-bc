using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using K = UnityEngine.InputSystem.Key;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The navigation mode (-navtest): the system map warps to places (the bodies, the fields,
    /// the Lagrange points, what is built out on its own) and lists each one's points of
    /// interest; at a place, the menu lists them; auto-travel flies there in real time (no warp),
    /// and docks at a station or lands on a base's pad; a course set is marked; X stops a trip.
    /// </summary>
    public partial class SystemTest
    {
        bool navOnly;

        IEnumerator NavigationTest(Keyboard kb)
        {
            int checks = 0, passed = 0;
            void Check(bool ok, string what)
            {
                checks++;
                if (ok) passed++;
                Log($"{(ok ? "ok" : "FAIL")}: {what}");
            }
            var hull = cc.GetComponent<Health>();
            if (hull != null) hull.cannotDie = true;
            var nav = demo.Navigation;
            var landing = PlanetLanding.Instance;
            if (nav == null || landing == null) { Log("FAIL: navigation (no navigation mode or landing)"); yield break; }

            // ---- what is a place, and what is a point of interest at one
            foreach (var g in WarpPoint.All.Where(w => w != null && !Places.IsPlace(w)).GroupBy(w => Places.PlaceOf(w)))
                Log($"  at {g.Key?.displayName}: {string.Join(", ", g.Select(w => w.displayName))}");
            var highport = Point("Thalassa Highport");
            var loop = Point("Thalassa Launch Loop");
            var thalassa = Point("Thalassa");
            Check(highport != null && loop != null && Places.PlaceOf(highport) == thalassa && Places.PlaceOf(loop) == thalassa
                  && Places.IsPlace(thalassa) && Places.IsPlace(Point("Selene")) && Places.IsPlace(Point("Kestrel Deep Yard")) && Places.IsPlace(Point("Inner Belt")),
                  "Thalassa, Selene, the Inner Belt and the Kestrel Deep Yard are places; the Highport and the launch loop are Thalassa's points of interest");

            // ---- the map: places only
            yield return Key(kb, K.Digit3);
            Check(Modes.Is(ModeOfPlay.Navigation), "3: navigation mode");
            yield return Key(kb, K.M);
            var seen = new HashSet<string>();
            for (int i = 0; i < 80 && SystemMap.IsOpen; i++)
            {
                if (map.Selected != null) seen.Add(map.Selected.displayName);
                yield return Key(kb, K.Tab);
            }
            Shot("nav_map");
            Check(SystemMap.IsOpen && seen.Contains("Thalassa") && seen.Contains("Kestrel Deep Yard") && !seen.Contains("Thalassa Highport") && !seen.Contains("Port Ochre"),
                  $"the map steps through {seen.Count} places, not their points of interest");
            yield return Key(kb, K.Escape);

            // ---- at Thalassa: the menu lists its points of interest
            if (Places.At(cc.transform.position) != thalassa) yield return WarpTo(kb, "Thalassa", "nav_thalassa");
            yield return Key(kb, K.Digit3);
            yield return Key(kb, K.Tab);
            var menu = ModeMenu.Instance;
            var listed = menu != null ? menu.Rows.Where(r => r.tag is WarpPoint).Select(r => ((WarpPoint)r.tag).displayName).ToList() : new List<string>();
            Check(ModeMenu.IsOpen && listed.Contains("Thalassa Highport") && listed.Contains("Thalassa Launch Loop"),
                  $"Tab lists Thalassa's points of interest: {string.Join(", ", listed)}");
            Shot("nav_menu");

            // ---- K on the Highport: fly there and dock, in real time
            for (int i = 0; i < 30 && menu != null && !ReferenceEquals(menu.Selected?.tag, highport); i++) yield return Key(kb, K.DownArrow);
            int warps0 = drive.Warps;
            float flown = 0f;
            yield return Key(kb, K.K);
            Check(nav.Travelling && nav.Course == highport && nav.Finish == NavigationMode.Then.Dock, $"K: flying to the Highport to dock ('{Notices.Last}')");
            float until = Time.time + 300f, shotAt = Time.time + 10f;
            while (Time.time < until && (nav.Travelling || !Docking.Docked))
            {
                // (the way flown, from the speed: the world's origin moves with the ship)
                flown += cc.Speed * Time.deltaTime;
                if (Time.time > shotAt) { Shot("nav_to_highport"); shotAt = float.MaxValue; }
                if (!nav.Travelling && !Docking.Docked && Docking.Instance.Current == Docking.State.Free) break;
                yield return null;
            }
            Check(Docking.Docked && drive.Warps == warps0,
                  $"docked at the Highport after {nav.LastTrip:F0} s of flying ({flown / 1000f:F0} km flown), with no warp ({drive.Warps - warps0} warps)");
            Shot("nav_docked");

            // ---- K on a base on the ground: fly there, and the auto-lander takes her down
            yield return new WaitForSeconds(1f);
            if (Docking.Docked) { Docking.Instance.Undock(); until = Time.time + 30f; while (Docking.Busy && Time.time < until) yield return null; }
            yield return Key(kb, K.Digit3);
            yield return Key(kb, K.Tab);
            menu = ModeMenu.Instance;
            for (int i = 0; i < 30 && menu != null && !ReferenceEquals(menu.Selected?.tag, loop); i++) yield return Key(kb, K.DownArrow);
            yield return Key(kb, K.K);
            Check(nav.Travelling && nav.Course == loop && nav.Finish == NavigationMode.Then.Land, $"K on the launch loop: flying there to land ('{Notices.Last}')");
            until = Time.time + 600f;
            shotAt = Time.time + 20f;
            float logAt = Time.time + 20f;
            while (Time.time < until && !(landing.Current == PlanetLanding.State.OnSurface && landing.Landed))
            {
                if (Time.time > shotAt) { Shot("nav_to_loop"); shotAt = float.MaxValue; }
                if (Time.time > logAt) { logAt = Time.time + 20f; Log($"    {nav.Phase}; landing {landing.Current}, speed {cc.Speed:F0} m/s"); }
                if (!nav.Travelling && !PlanetLanding.Busy) break;
                yield return null;
            }
            float off = landing.Site != null ? Vector3.Distance(cc.transform.position, landing.Site.pad) : -1f;
            Check(landing.Current == PlanetLanding.State.OnSurface && landing.Landed && drive.Warps == warps0,
                  $"down on Thalassa by the launch loop, flown all the way ({landing.Current}, landed {landing.Landed}, {off:F0} m from the pad, {drive.Warps - warps0} warps)");
            Shot("nav_landed");
            landing.TakeOff();
            until = Time.time + 60f;
            while (landing.Current != PlanetLanding.State.InSpace && Time.time < until) yield return null;

            // ---- a course set (C) is marked; X stops a trip
            yield return Key(kb, K.Digit3);
            yield return Key(kb, K.Tab);
            menu = ModeMenu.Instance;
            for (int i = 0; i < 30 && menu != null && !ReferenceEquals(menu.Selected?.tag, highport); i++) yield return Key(kb, K.DownArrow);
            yield return Key(kb, K.C);
            yield return Key(kb, K.Tab);
            Check(nav.Course == highport && !nav.Travelling, $"C sets the course without flying it ('{Notices.Last}')");
            yield return Key(kb, K.Tab);
            yield return Key(kb, K.Enter);
            bool going = nav.Travelling;
            yield return new WaitForSeconds(2f);
            yield return Key(kb, K.X);
            Check(going && !nav.Travelling && nav.Course == highport && demo.autoRudder == null, "Enter flies it; X stops the trip, the course still marked");
            Shot("nav_course");

            yield return Key(kb, K.Digit1);
            if (hull != null) hull.cannotDie = false;
            Log($"{(passed == checks ? "PASS" : "FAIL")}: navigation ({passed} of {checks})");
        }
    }
}

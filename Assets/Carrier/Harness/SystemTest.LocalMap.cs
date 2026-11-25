using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using K = UnityEngine.InputSystem.Key;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The system map's local map (part of -navtest): at Thalassa, L shows the planet, its moons
    /// and every point of interest round them, Thalassa's high orbit drawn off its disc; Esc goes
    /// back to the system map and L comes back; WARP on Selene's high orbit warps there; and from
    /// there, FLY TO on a point of interest at Selene flies there, in navigation mode, with the map
    /// (opened from combat mode) closed behind it and the trip still going.
    /// </summary>
    public partial class SystemTest
    {
        IEnumerator LocalMapTest(Keyboard kb, System.Action<bool, string> Check)
        {
            var thalassa = Point("Thalassa");
            var selene = Point("Selene");
            var seleneHigh = Point("Selene High Orbit");
            var thalassaHigh = Point("Thalassa High Orbit");
            var nav = demo.Navigation;
            if (thalassa == null || selene == null || seleneHigh == null || nav == null)
            {
                Check(false, "the local map: Thalassa, Selene, Selene High Orbit and navigation mode are there");
                yield break;
            }
            if (Places.At(cc.transform.position) != thalassa) yield return WarpTo(kb, "Thalassa", "lm_thalassa");

            // ---- at Thalassa: the map opens on it, with LOCAL MAP beside WARP
            yield return Key(kb, K.Digit3);
            yield return Key(kb, K.M);
            Check(SystemMap.IsOpen && map.Selected == thalassa && map.LocalTarget == thalassa && map.LocalButtonRect.width > 0f && !map.Local,
                  $"at Thalassa the map opens on Thalassa ({map.Selected?.displayName}), with LOCAL MAP beside WARP");

            // ---- L: the planet, its moons and every point of interest round them
            yield return Key(kb, K.L);
            var shown = map.LocalPoints.Where(w => w != null).Select(w => w.displayName).ToList();
            float highOff = thalassaHigh != null ? Vector2.Distance(map.ScreenPosOf(thalassaHigh), map.ScreenPosOf(thalassa)) : -1f;
            Check(SystemMap.IsOpen && map.Local && map.LocalPlanet == thalassa && shown.FirstOrDefault() == "Thalassa"
                  && shown.Contains("Selene") && shown.Contains("Mnemos") && shown.Contains("Thalassa Highport") && shown.Contains("Selene High Orbit"),
                  $"L: the local map of Thalassa, {shown.Count} points: {string.Join(", ", shown)}");
            Check(thalassaHigh != null && highOff > 11f * Hud.S,
                  $"Thalassa High Orbit (straight up) is drawn off the planet's disc, not on its middle ({highOff:F0} px from it)");
            Vector2 c0 = map.ScreenPosOf(thalassa);
            Shot("lm_local_map");
            yield return new WaitForSeconds(1f);
            float moved = Vector2.Distance(map.ScreenPosOf(thalassa), c0);
            Check(moved < 1f, $"the local map stays on Thalassa as it goes round ({moved:F2} px in a second)");

            // ---- Esc: back to the system map (still open); L: the local map again
            yield return Key(kb, K.Escape);
            bool back = SystemMap.IsOpen && !map.Local;
            yield return Key(kb, K.L);
            Check(back && SystemMap.IsOpen && map.Local, $"Esc goes back to the system map, the map still open ({back}); L shows the local map again ({map.Local})");

            // ---- WARP on Selene's high orbit (a point of interest at a moon): warped there
            for (int i = 0; i < 40 && map.Selected != seleneHigh; i++) yield return Key(kb, K.Tab);
            bool chosen = map.Selected == seleneHigh;
            Check(chosen && !map.CanFlyTo(seleneHigh), $"Tab on the local map reaches Selene High Orbit ({map.Selected?.displayName}): from Thalassa, a warp");
            if (!chosen) { if (SystemMap.IsOpen) map.Close(); yield break; }
            yield return Warp(() => Key(kb, K.Enter), "the local map's WARP", "lm_selene_high", false);
            float off = Vector3.Distance(cc.transform.position, seleneHigh.Position);
            var at = Places.At(cc.transform.position);
            Check(!SystemMap.IsOpen && at == selene && off < 20000f,
                  $"warped to Selene High Orbit from the local map ({off / 1000f:F1} km from it, at {at?.displayName})");

            // ---- the map opened from combat mode; on the local map, FLY TO a point of interest
            // at Selene: flown to in real time, and the trip goes on once the map has closed (it
            // doesn't go back to combat mode, which would stop it)
            yield return Key(kb, K.Digit1);
            yield return Key(kb, K.M);
            bool fromCombat = SystemMap.IsOpen && Modes.Is(ModeOfPlay.Navigation);
            yield return Key(kb, K.L);
            var flyTo = map.LocalPoints.FirstOrDefault(w => w != null && w.displayName == "Selene Northern Reach")
                        ?? map.LocalPoints.FirstOrDefault(w => w != null && w != seleneHigh && Places.PlaceOf(w) == selene);
            for (int i = 0; i < 40 && flyTo != null && map.Selected != flyTo; i++) yield return Key(kb, K.Tab);
            bool canFly = flyTo != null && map.Selected == flyTo && map.CanFlyTo(flyTo);
            int warps0 = drive.Warps;
            yield return Key(kb, K.Enter);
            bool flying = flyTo != null && nav.Travelling && nav.Course == flyTo;
            yield return new WaitForSeconds(3f);
            Shot("lm_fly_to");
            Check(fromCombat && canFly && flying && nav.Travelling && !SystemMap.IsOpen && Modes.Is(ModeOfPlay.Navigation) && drive.Warps == warps0,
                  $"FLY TO on {flyTo?.displayName} (at Selene, where the ship is: {canFly}) flies there with no warp, and 3 s on still is " +
                  $"({nav.Travelling}, {nav.Phase}; map open {SystemMap.IsOpen}; mode {Modes.Current}; '{Notices.Last}')");
            yield return Key(kb, K.X);
            cc.throttle = 0f;
        }
    }
}

using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using K = UnityEngine.InputSystem.Key;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -approachtest (?approachtest): the real-size system and flying down to its worlds. The
    /// planets are hundreds of kilometres across and millions of kilometres apart, the world is
    /// drawn round the frigate (it stays near the origin after a warp), the far bodies are drawn
    /// nearer than they are; over Ochre its ground is seen close to (PlanetTerrain), the
    /// auto-lander (K) takes the frigate from orbit down through the air, over to the surface and
    /// onto Port Ochre's pad, K again takes it back up to orbit; and over Selene (no air) it is
    /// flown down by hand, nose down with the cruise on, and back up.
    /// </summary>
    public partial class SystemTest
    {
        bool approachOnly;

        IEnumerator ApproachTest(Keyboard kb)
        {
            int checks = 0, passed = 0;
            void Check(bool ok, string what)
            {
                checks++;
                if (ok) passed++;
                Log($"{(ok ? "ok" : "FAIL")}: {what}");
            }
            yield return Key(kb, K.H);
            var hull = cc.GetComponent<Health>();
            if (hull != null) hull.cannotDie = true;
            var landing = PlanetLanding.Instance;
            var pf = PlanetFlight.Instance;
            var os = OrbitalSystem.Instance;
            if (landing == null || pf == null || os == null) { Log("FAIL: approach (no landing, flight or orbits in this level)"); yield break; }
            yield return new WaitForSeconds(2f);

            // ---- the scale, and the world drawn round the frigate
            var th = Point("Thalassa");
            var oc = Point("Ochre");
            double apart = Double3.Distance(os.SystemPosition(OrbitalSystem.OrbitOf(th.transform)), os.SystemPosition(OrbitalSystem.OrbitOf(oc.transform)));
            Check(th.bodyRadius >= 300000f && apart > 1e9, $"Thalassa is {th.bodyRadius * 2f / 1000f:F0} km across, {apart / 1e9:F2} million km from Ochre");
            Check(cc.transform.position.magnitude < os.recentreDistance * 1.5f,
                  $"the world is drawn round the frigate ({cc.transform.position.magnitude / 1000f:F1} km from the origin; {os.Recentres} recentres, {os.FarCount} far things kept in doubles)");
            int compressed = FindObjectsByType<ScaledSpace>(FindObjectsSortMode.None).Count(s => s.Compressed);
            var thTerrain = th.GetComponent<PlanetTerrain>();
            Log($"  Thalassa {Vector3.Distance(Camera.main.transform.position, th.Position) / 1000f:F0} km from the camera: terrain active {thTerrain?.Active}, " +
                $"{thTerrain?.Drawn} patches, {thTerrain?.Pending} pending; nearest {WarpPoint.Nearest(cc.transform.position)?.displayName}");
            Shot("ap_start");
            // Thalassa from 1,500 km, drawn nearer than it is: all of it, not only its air's outline
            {
                Vector3 back = cc.transform.position, dir = (back - th.Position).normalized;
                var thSs = th.GetComponent<ScaledSpace>();
                cc.transform.position = th.Position + dir * 1500000f;
                cc.SetUpFrame(Quaternion.identity);
                cc.Face(th.Position - cc.transform.position);
                cc.SetMotion(0f);
                yield return new WaitForSeconds(1.5f);
                Shot("ap_thalassa_far");
                Log($"  Thalassa from {Vector3.Distance(Camera.main.transform.position, th.Position) / 1000f:F0} km: drawn nearer {thSs?.Compressed}, sphere drawn {th.GetComponent<MeshRenderer>()?.enabled}, " +
                    $"terrain {th.GetComponent<PlanetTerrain>()?.Active}");
                cc.transform.position = back;
                cc.Face(th.Position - back);
                yield return new WaitForSeconds(1f);
            }
            Check(compressed >= 4, $"the far bodies are drawn nearer than they are ({compressed} of {ScaledSpace.Count})");

            // ---- Ochre from orbit, its ground seen close to
            yield return WarpTo(kb, "Port Ochre", "ap_port_ochre");
            yield return new WaitForSeconds(1f);
            Check(cc.transform.position.magnitude < os.recentreDistance * 1.5f,
                  $"after a warp of {apart / 1e9:F1} million km the world is round the frigate again ({cc.transform.position.magnitude / 1000f:F1} km from the origin)");
            var terrain = oc.GetComponent<PlanetTerrain>();
            float until = Time.time + 25f;
            while (terrain != null && (terrain.Drawn < 6 || terrain.Pending > 0) && Time.time < until) yield return null;
            Check(terrain != null && terrain.Active && terrain.Drawn >= 6,
                  $"Ochre's ground seen close to: {(terrain != null ? terrain.Drawn : 0)} patches drawn, {(terrain != null ? terrain.Pending : 0)} still being worked out");
            Log($"  over Ochre at {pf.Altitude / 1000f:F1} km: air {pf.Density * 100f:F1}%, sky {pf.AirLook:F2}");
            Shot("ap_ochre_high");

            // ---- the auto-lander, all the way down
            float t0 = Time.time;
            yield return Key(kb, K.K);
            Check(landing.Autopilot, $"K: the auto-lander takes over ({Plain(landing.HudText())})");
            bool shot30 = false, shot8 = false;
            float logAt = Time.time, hottest = 0f, densest = 0f, fastest = 0f;
            until = Time.time + 240f;
            while (landing.Current == PlanetLanding.State.InSpace && landing.Autopilot && Time.time < until)
            {
                hottest = Mathf.Max(hottest, pf.Heat);
                densest = Mathf.Max(densest, pf.Density);
                fastest = Mathf.Max(fastest, cc.Speed);
                if (Time.time >= logAt)
                {
                    logAt = Time.time + 6f;
                    Log($"  {landing.AutopilotStage}: alt {pf.Altitude / 1000f:F1} km, {cc.Speed:F0} m/s, air {pf.Density * 100f:F1}%, heat {pf.Heat:F2}, sky {pf.AirLook:F2}, " +
                        $"ground built ahead {(landing.Prepared ? "yes" : "no")}, {terrain?.Drawn} patches");
                }
                if (!shot30 && pf.Altitude < 30000f) { shot30 = true; Shot("ap_ochre_30km"); }
                if (!shot8 && pf.Altitude < 8000f) { shot8 = true; Shot("ap_ochre_8km"); }
                yield return null;
            }
            Check(landing.Current == PlanetLanding.State.OnSurface && landing.Handoffs >= 1,
                  $"down through the air (fastest {fastest:F0} m/s, air up to {densest * 100f:F0}%, hull heat up to {hottest:F2}) and over to the surface after {Time.time - t0:F0} s");
            Shot("ap_ochre_handoff");
            until = Time.time + 150f;
            while (landing.Current == PlanetLanding.State.OnSurface && !landing.Landed && Time.time < until) yield return null;
            yield return new WaitForSeconds(1f);
            Check(landing.Landed && landing.OnPad, $"onto Port Ochre's pad (landed {landing.Landed}, on the pad {landing.OnPad}) {Time.time - t0:F0} s after K");
            Shot("ap_ochre_on_pad");

            // ---- and back up to orbit
            t0 = Time.time;
            yield return Key(kb, K.K);
            until = Time.time + 150f;
            bool shotAir = false;
            while (landing.Current != PlanetLanding.State.InSpace && Time.time < until) yield return null;
            Check(landing.Current == PlanetLanding.State.InSpace && landing.Handoffs >= 2, $"K: up off the pad and out of the surface into the air over Ochre after {Time.time - t0:F0} s");
            until = Time.time + 240f;
            while (landing.Autopilot && Time.time < until)
            {
                if (!shotAir && landing.Current == PlanetLanding.State.InSpace && pf.Altitude > 20000f) { shotAir = true; Shot("ap_ochre_climb"); }
                yield return null;
            }
            float orbit = oc.arrivalDistance - oc.bodyRadius;
            Check(!landing.Autopilot && pf.Altitude > orbit * 0.9f, $"back in orbit {pf.Altitude / 1000f:F0} km up (orbit {orbit / 1000f:F0} km) {Time.time - t0:F0} s after K");
            // (the ground built ahead is hidden while in space: never the ship itself)
            int shipHidden = cc.GetComponentsInChildren<Renderer>(true).Count(r => r.forceRenderingOff);
            Check(shipHidden == 0, $"the ship is drawn after the climb ({shipHidden} of its renderers switched off)");
            Shot("ap_ochre_orbit_again");

            // ---- by hand, onto a moon with no air
            yield return WarpTo(kb, "Selene", "ap_selene");
            yield return new WaitForSeconds(1f);
            if (!pf.Cruise) yield return Key(kb, K.O);
            demo.autoPitch = -1f;
            cc.throttle = 1f;
            t0 = Time.time;
            until = Time.time + 200f;
            float maxSpeed = 0f;
            while (landing.Current == PlanetLanding.State.InSpace && Time.time < until) { maxSpeed = Mathf.Max(maxSpeed, cc.Speed); yield return null; }
            demo.autoPitch = null;
            Check(landing.Current == PlanetLanding.State.OnSurface, $"by hand, nose down with the cruise on (up to {maxSpeed:F0} m/s), down to Selene's surface after {Time.time - t0:F0} s");
            Shot("ap_selene_down");
            demo.autoPitch = 1f;
            demo.autoElevator = 1f;
            until = Time.time + 120f;
            while (landing.Current == PlanetLanding.State.OnSurface && Time.time < until) yield return null;
            demo.autoPitch = null;
            demo.autoElevator = null;
            Check(landing.Current == PlanetLanding.State.InSpace, "and back up by hand, out over Selene");
            yield return new WaitForSeconds(2f);
            Shot("ap_selene_up");
            cc.throttle = 0.2f;
            if (pf.Cruise) yield return Key(kb, K.O);

            if (hull != null) hull.cannotDie = false;
            Log($"{(passed == checks ? "PASS" : "FAIL")}: flying down to the worlds ({passed} of {checks})");
        }
    }
}

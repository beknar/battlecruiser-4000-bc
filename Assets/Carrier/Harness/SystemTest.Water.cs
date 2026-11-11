using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The sea (-watertest, with -frigate &lt;id&gt; for another frigate): down on Pelagia, the
    /// pirates' water world. The ship rolls and levels off; comes down on the water and floats;
    /// sails; dives, the view going under with it, hidden from above by the surface; comes back
    /// up gently, afloat; lifts off the water; and launches from under it. Then the sea forces:
    /// a torpedo base on the sea floor and submarines, which fire torpedoes at the ship under the
    /// water, and which its guns hit there. Pictures of all of it.
    /// </summary>
    public partial class SystemTest
    {
        bool waterOnly;

        IEnumerator WaterTest(Keyboard kb)
        {
            int checks = 0, passed = 0;
            void Check(bool ok, string what)
            {
                checks++;
                if (ok) passed++;
                Log((ok ? "ok: " : "FAIL: ") + what);
            }
            var landing = PlanetLanding.Instance;
            var pelagia = Point("Pelagia");
            if (landing == null || pelagia == null) { Log("FAIL: no landing, or no Pelagia"); yield break; }
            var hull = cc.GetComponent<Health>();
            if (hull != null) hull.cannotDie = true;   // a test, not a fight to the death
            landing.LandOn(pelagia);
            yield return WaitLanding(landing, PlanetLanding.State.OnSurface, 25f);
            var sf = Seafaring.Instance;
            Check(landing.Site != null && landing.Site.water && Sea.Here && sf != null && sf.enabled,
                  $"down on {pelagia.displayName}: its sea is water the ship can go into (sea at {Sea.Level:F0})");
            if (sf == null || !Sea.Here) { Log($"FAIL: the sea ({passed} of {checks})"); yield break; }
            yield return new WaitForSeconds(2f);

            // ---- roll
            demo.autoRoll = 1f;
            yield return new WaitForSeconds(2f);
            float rolled = cc.Roll;
            demo.autoRoll = null;
            Shot("w_roll");
            cc.LevelOff();
            yield return new WaitForSeconds(3f);
            Check(Mathf.Abs(rolled) > 60f && Mathf.Abs(cc.Roll) < 2f, $"rolls ({rolled:F0} deg in 2 s) and levels off again ({cc.Roll:F1} deg)");

            // ---- over deep water, down onto it
            var surface = landing.Surface;
            Vector3 deep = DeepWater(surface, cc.transform.position, out float seaShare, out float deepest);
            Log($"  round about (8 km): {seaShare * 100f:F0}% sea, the deepest {deepest:F0} m");
            Vector3 heading = deep - cc.transform.position;
            heading.y = 0f;
            PlaceShip(new Vector3(deep.x, Sea.Level + 160f, deep.z), heading.sqrMagnitude > 1f ? heading : cc.transform.forward);
            Log($"  over the sea: {Sea.Level - surface.GroundAt(cc.transform.position):F0} m of water under the ship");
            demo.autoElevator = -1f;
            float until = Time.time + 45f, fastest = 0f;
            while (sf.Current == Seafaring.State.Flying && Time.time < until)
            {
                fastest = Mathf.Min(fastest, cc.ClimbRate);
                yield return null;
            }
            demo.autoElevator = null;
            float touchdown = fastest;
            yield return new WaitForSeconds(0.3f);
            int spray = sf.SprayParticles, ring = sf.FoamParticles;
            Shot("w_splash");
            yield return new WaitForSeconds(1.7f);
            Check(spray > 150 && ring > 80, $"splashes down: water thrown up ({spray}) and a ring of foam ({ring})");
            Check(sf.Current == Seafaring.State.Afloat && sf.Splashdowns >= 1,
                  $"comes down on the water and floats (descent eased to {Mathf.Abs(touchdown):F0} m/s at the most)");
            Shot("w_afloat");

            // ---- sailing
            cc.throttle = 1f;
            yield return new WaitForSeconds(9f);
            float drift = Mathf.Abs(cc.transform.position.y - sf.FloatHeight);
            Shot("w_sailing");
            Check(sf.FoamParticles > 300, $"a wake behind it ({sf.FoamParticles} foam)");
            Check(sf.Current == Seafaring.State.Afloat && cc.Speed > cc.maxSpeed * 0.45f && cc.Speed <= cc.maxSpeed * 0.61f && drift < 3f,
                  $"sails on the surface at {cc.Speed:F0} m/s ({cc.maxSpeed:F0} in the air), riding the swell ({drift:F1} m off its waterline)");

            yield return null;   // (the picture is taken at the end of the frame: before the ship is moved)
            yield return null;

            // ---- diving: from deep water again (it has sailed a way)
            cc.throttle = 0f;
            deep = DeepWater(surface, cc.transform.position, out _, out float diveDepth);
            PlaceShip(new Vector3(deep.x, sf.FloatHeight, deep.z), cc.transform.forward);
            Log($"  diving where there are {diveDepth:F0} m of water");
            yield return new WaitForSeconds(1f);
            demo.autoElevator = -1f;
            int foamBefore = sf.FoamParticles;
            float diveUntil = Time.time + 6f;
            int churn = 0;
            while (Time.time < diveUntil)
            {
                churn = Mathf.Max(churn, sf.FoamParticles - foamBefore);
                yield return null;
            }
            demo.autoElevator = null;
            Check(churn > 100, $"the water churns as it goes down through it ({churn} foam)");
            yield return new WaitForSeconds(1f);
            var cam = Camera.main;
            bool blue = cam != null && Sea.Under(cam.transform.position) && RenderSettings.fog && cam.clearFlags == CameraClearFlags.SolidColor;
            Check(sf.Current == Seafaring.State.Under && sf.Depth > 15f, $"dives ({sf.Depth:F0} m down)");
            Check(blue, "the view goes under with it, blue and close");
            Shot("w_under");
            cc.throttle = 1f;
            yield return new WaitForSeconds(5f);
            Check(cc.Speed <= cc.maxSpeed * 0.46f && cc.Speed > 0f, $"slower under the water ({cc.Speed:F0} m/s)");
            Check(Cover.Blocked(cc.transform.position, cc.transform.position + Vector3.up * 600f + cc.transform.forward * 300f),
                  "the surface hides it from anything above");

            // ---- up again: gently to the surface, then off the water
            demo.autoElevator = 0.15f;
            until = Time.time + 60f;
            while (sf.Current == Seafaring.State.Under && Time.time < until) yield return null;
            demo.autoElevator = null;
            yield return new WaitForSeconds(1.5f);
            Check(sf.Current == Seafaring.State.Afloat && sf.Surfacings >= 1, "comes back up to the surface gently, and floats");
            demo.autoElevator = 1f;
            yield return new WaitForSeconds(4f);
            demo.autoElevator = null;
            Check(sf.Current == Seafaring.State.Flying && sf.Depth < -30f && sf.Launches >= 1, $"lifts off the water and flies ({-sf.Depth:F0} m up)");

            // ---- launching from under the water (from over deep water)
            deep = DeepWater(surface, cc.transform.position, out _, out _);
            PlaceShip(new Vector3(deep.x, cc.transform.position.y, deep.z), cc.transform.forward);
            demo.autoElevator = -1f;
            until = Time.time + 45f;
            while (sf.Current == Seafaring.State.Flying && Time.time < until) yield return null;
            yield return new WaitForSeconds(6f);
            bool wentUnder = sf.Current == Seafaring.State.Under;
            int launches0 = sf.Launches;
            demo.autoElevator = 1f;
            until = Time.time + 30f;
            while (sf.Current == Seafaring.State.Under && Time.time < until) yield return null;
            yield return new WaitForSeconds(0.4f);
            Shot("w_launch");
            yield return new WaitForSeconds(3f);
            demo.autoElevator = null;
            Check(wentUnder && sf.Launches > launches0 && sf.Current == Seafaring.State.Flying && sf.Depth < -20f,
                  $"launches straight out of the water from under it ({-sf.Depth:F0} m up)");
            cc.throttle = 0f;

            // ---- the sea forces
            var units = FindObjectsByType<GroundUnit>(FindObjectsSortMode.None);
            var seaBases = units.Where(u => u.name == "Sea Base").ToArray();
            var subs = units.Where(u => u.name == "Attack Submarine").ToArray();
            var islands = units.Where(u => !u.sonar).ToArray();
            Log($"  near by: {seaBases.Length} sea bases, {subs.Length} submarines, {islands.Length} island and air units");
            Check(seaBases.Length + subs.Length > 0, $"sea bases and submarines in {pelagia.displayName}'s waters ({seaBases.Length} and {subs.Length})");
            var foe = seaBases.Concat(subs).OrderBy(u => Vector3.Distance(u.transform.position, cc.transform.position)).FirstOrDefault();
            string foeName = foe != null ? foe.name.ToLower() : "";
            if (foe != null)
            {
                // under the water near it, facing it, with a clear line to it (not behind a rise
                // in the sea bed)
                Vector3 to = foe.transform.position - cc.transform.position;
                to.y = 0f;
                Vector3 at = Vector3.zero;
                for (int k = 0; k < 16; k++)
                {
                    Vector3 from = Quaternion.Euler(0f, k * 22.5f, 0f) * (to.sqrMagnitude > 1f ? to.normalized : Vector3.forward);
                    Vector3 p = foe.transform.position - from * 900f;
                    p.y = Mathf.Clamp(foe.transform.position.y + 12f, surface.GroundAt(p) + 30f, Sea.Level - 25f);
                    if (k == 0) at = p;
                    if (!Cover.Blocked(p, foe.transform.position)) { at = p; break; }
                }
                PlaceShip(at, foe.transform.position - at);
                sf.Submerge();
                var fh = foe.GetComponent<Health>();
                float fd0 = fh != null ? fh.TotalDamage : 0f;
                var seen = new HashSet<Missile>();
                float hp0 = hull != null ? hull.TotalDamage : 0f;
                until = Time.time + 30f;
                while (Time.time < until && seen.Count == 0)
                {
                    foreach (var m in Missile.Active) if (m != null && m.underwater) seen.Add(m);
                    yield return null;
                }
                yield return new WaitForSeconds(2f);
                Shot("w_torpedo");
                var running = seen.Where(m => m != null).ToArray();
                bool flameless = running.All(m => m.GetComponentInChildren<TrailRenderer>() == null &&
                    m.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(x => x == null || !x.IsKeywordEnabled("_EMISSION"))));
                int bubbles = running.Sum(m => m.TryGetComponent(out TorpedoWake w) ? w.Bubbles : 0);
                if (running.Length > 0)
                    Check(flameless && bubbles > 20, $"torpedoes run with no flame, trailing bubbles ({bubbles})");
                yield return new WaitForSeconds(4f);
                foreach (var m in Missile.Active) if (m != null && m.underwater) seen.Add(m);
                Check(seen.Count > 0, $"the {foeName} fires torpedoes at the ship under the water ({seen.Count}; " +
                                      $"{(hull != null ? hull.TotalDamage - hp0 : 0f):F0} hull damage so far)");
                if (foe != null) demo.SelectTarget(foe.transform);
                foreach (var t in cc.mainTurrets) if (t != null) t.autoFire = true;
                var shots0 = cc.mainTurrets.Where(t => t != null).ToDictionary(t => t, t => t.ShotsFired);
                int blasts0 = Explosion.UnderwaterBlasts, bubbled0 = Explosion.BubblesReleased;
                yield return new WaitForSeconds(14f);
                Shot("w_seabattle");
                int blasts = Explosion.UnderwaterBlasts - blasts0;
                Check(blasts > 0 && Explosion.FireUnderwater == 0 && Explosion.BubblesReleased - bubbled0 > 40,
                      $"blasts under the water are bubbles, not fire ({blasts} blasts, {Explosion.BubblesReleased - bubbled0} bubbles, {Explosion.FireUnderwater} on fire)");
                if (foe != null)
                    Log($"  at it: {Vector3.Distance(cc.transform.position, foe.transform.position):F0} m, ship {sf.Depth:F0} m down, " +
                        $"line {(Cover.Blocked(cc.transform.position, foe.transform.position) ? "blocked" : "clear")}; turrets fired " +
                        string.Join(", ", shots0.Select(kv => $"{kv.Key.Label} {kv.Key.ShotsFired - kv.Value}")));
                bool killed = foe == null || fh == null || fh.Dead;
                Check(killed || fh.TotalDamage > fd0, killed ? "the ship's guns hit it under the water (destroyed)"
                                                            : $"the ship's guns hit it under the water ({fh.TotalDamage - fd0:F0} damage)");
                var sub = subs.FirstOrDefault(s => s != null && s.Alive);
                var subPilot = sub != null ? sub.GetComponent<Submarine>() : null;
                if (subPilot != null)
                {
                    Log($"  a submarine: {(subPilot.Hunting ? "hunting the ship" : "on patrol")}, {Vector3.Distance(sub.transform.position, cc.transform.position):F0} m off");
                    // alerted, it goes for the ship (the only one: single-player)
                    sub.Engage();
                    yield return new WaitForSeconds(1.5f);
                    Check(subPilot.Hunting && subPilot.Target != null && subPilot.Target.IsChildOf(cc.transform.root),
                          $"an alerted submarine hunts the ship ({(subPilot.Hunting ? "hunting" : "not hunting")})");
                    sub.StandDown();
                }
                foreach (var t in cc.mainTurrets) if (t != null) t.autoFire = false;
                cc.SetTarget(null);
                var oc = demo.orbitCamera;
                if (sub != null && oc != null)
                {
                    var keep = oc.target;
                    oc.target = sub.transform;
                    float d0 = oc.distance;
                    oc.distance = 140f;
                    yield return new WaitForSeconds(0.8f);
                    Shot("w_submarine");
                    oc.target = keep;
                    oc.distance = d0;
                }
                var station = seaBases.FirstOrDefault(s => s != null && s.Alive);
                if (station != null && oc != null)
                {
                    var keep = oc.target;
                    oc.target = station.transform;
                    float d0 = oc.distance;
                    oc.distance = 260f;
                    yield return new WaitForSeconds(0.8f);
                    Shot("w_seabase");
                    oc.target = keep;
                    oc.distance = d0;
                }
            }
            if (hull != null) hull.cannotDie = false;
            Log($"{(passed == checks ? "PASS" : "FAIL")}: the sea ({passed} of {checks})");
        }

        /// <summary>The deepest water near (within 8 km), and how much of it is sea.</summary>
        static Vector3 DeepWater(PlanetSurface surface, Vector3 near, out float seaShare, out float deepest)
        {
            Vector3 best = near;
            deepest = float.NegativeInfinity;
            int sea = 0, all = 0;
            for (float r = 0f; r <= 8000f; r += 300f)
                for (int k = 0; k < (r == 0f ? 1 : 24); k++)
                {
                    float a = k * Mathf.PI * 2f / 24f;
                    var p = near + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    float d = Sea.Level - surface.GroundAt(p);
                    all++;
                    if (d > 0f) sea++;
                    // deep, and the nearer the better
                    if (d - r * 0.005f > deepest) { deepest = d - r * 0.005f; best = p; }
                }
            seaShare = all > 0 ? sea / (float)all : 0f;
            deepest = Sea.Level - surface.GroundAt(best);
            return best;
        }

        /// <summary>The ship somewhere, facing a way, stopped.</summary>
        void PlaceShip(Vector3 at, Vector3 facing)
        {
            cc.transform.position = at;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1f) facing = cc.transform.forward;
            cc.SetAttitude(Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg, 0f);
            cc.SetMotion(0f);
            cc.throttle = 0f;
        }
    }
}

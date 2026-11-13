using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using K = UnityEngine.InputSystem.Key;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -striketest (?striketest): strikes from orbit. The corsairs' mass driver over Pyrois Domes
    /// fires on its own time with nobody near (the settlement loses buildings and people all the
    /// same); their ion satellite over Port Ochre is watched firing (the beam, the flash); the
    /// frigate drops a kinetic rod on Cinder Fortress (Y) and its ground forces are destroyed;
    /// landing there, they're gone and the crater is in the ground; Port Ochre has rubble where
    /// its buildings were; the ion satellite destroyed fires no more; and the save has it all.
    /// </summary>
    public partial class SystemTest
    {
        bool strikeOnly;

        /// <summary>Turn the orbit camera to look at a point past the ship (in its up frame).</summary>
        void Aim(OrbitCamera oc, Vector3 p, float distMul, float down)
        {
            if (oc == null) return;
            Vector3 to = Quaternion.Inverse(cc.upFrame) * (p - cc.transform.position);
            oc.yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            oc.pitch = down - Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            oc.distance = demo.ShipViewDistance * distMul;
        }

        IEnumerator StrikeTest(Keyboard kb)
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
            var sats = StrikeSatellite.All.ToList();
            var ion = sats.FirstOrDefault(x => x.weapon == StrikeSatellite.Weapon.IonCannon);
            var md = sats.FirstOrDefault(x => x.weapon == StrikeSatellite.Weapon.MassDriver);
            Check(ion != null && md != null, $"two corsair strike satellites: {string.Join(", ", sats.Select(x => $"{x.Label} over {x.target} every {x.intervalMinutes:F0} min"))}");
            if (ion == null || md == null || landing == null) { Log("FAIL: strikes (no satellites)"); yield break; }
            yield return new WaitForSeconds(2f);

            // ---- off-screen: the mass driver's time comes with nobody near
            float pool0 = IndustryLedger.Pool("Pyrois Domes");
            int strikes0 = WorldState.Strikes.Count;
            WorldState.Advance((float)(md.NextAt - WorldState.Clock) + 1f);
            yield return null;
            yield return null;
            var s1 = WorldState.Strikes.Count > strikes0 ? WorldState.Strikes[WorldState.Strikes.Count - 1] : default;
            Check(md.Strikes >= 1 && s1.place == "Pyrois Domes" && !s1.seen && s1.buildings > 0 && IndustryLedger.Pool("Pyrois Domes") < pool0,
                  $"off-screen, on its own time, the mass driver strikes Pyrois Domes: {s1.buildings} buildings ruined, {s1.casualties} killed " +
                  $"(recruits there {pool0:F0} -> {IndustryLedger.Pool("Pyrois Domes"):F0}); a crater on {s1.body}");

            // ---- watched: the ion satellite over Port Ochre
            yield return WarpTo(kb, ion.Label, "sk_ion");
            yield return new WaitForSeconds(1f);
            var port = Point("Port Ochre");
            OrbitalStrikes.SiteOf(port, out var ochre, out var site);
            float portPool0 = IndustryLedger.Pool("Port Ochre");
            int ionStrikes0 = ion.Strikes;
            // (the camera turned to the city first, from beside the satellite)
            var oc = demo.orbitCamera;
            Vector3 city = OrbitalStrikes.GroundPoint(ochre, site.su, site.sv);
            Aim(oc, city, 3f, 4f);
            yield return new WaitForSeconds(0.8f);
            ion.FireNow();
            yield return new WaitForSeconds(0.9f);
            Shot("sk_ion_beam");
            yield return new WaitForSeconds(3f);
            Check(ion.Strikes == ionStrikes0 + 1 && ion.Last.buildings > 0 && IndustryLedger.Pool("Port Ochre") < portPool0,
                  $"watched from beside it, the ion satellite burns into Port Ochre: {ion.Last.buildings} buildings ruined, {ion.Last.casualties} killed");

            // ---- the frigate's kinetic strike on Cinder Fortress
            yield return WarpTo(kb, "Cinder Fortress", "sk_fortress");
            yield return new WaitForSeconds(1.5f);
            var ks = KineticStrike.Instance;
            var cinder = Point("Cinder");
            string hud = ks != null ? Plain(ks.HudText() ?? "") : "";
            int destroyed0 = WorldState.Body("Cinder").destroyed.Count;
            Check(ks != null && hud.Contains("strike"), $"over the fortress the HUD offers a strike: '{hud}'");
            yield return Key(kb, K.Y);
            // (the camera on the fortress below before the rod lands, the pictures as it falls and lands)
            OrbitalStrikes.PlaceOf(cinder, cc.transform.position, out double su0, out double sv0);
            OrbitalStrikes.NearestTarget(cinder, su0, sv0, 30000f, out double tu0, out double tv0, out _, out _);
            Aim(oc, OrbitalStrikes.GroundPoint(cinder, tu0, tv0), 3f, 4f);
            float until = Time.time + 30f;
            bool falling = false;
            while (ks != null && ks.Landed == 0 && Time.time < until)
            {
                if (!falling && Time.time > until - 28.8f) { falling = true; Shot("sk_rod_falling"); }
                yield return null;
            }
            var hit = ks != null ? ks.Last : default;
            yield return new WaitForSeconds(0.35f);
            Shot("sk_rod_impact");
            yield return new WaitForSeconds(1.2f);
            Shot("sk_rod_after");
            var impact = WorldState.Body("Cinder").scars.LastOrDefault();
            Check(ks != null && ks.Landed == 1 && hit.units > 0 && WorldState.Body("Cinder").destroyed.Count >= destroyed0 + hit.units,
                  $"Y: a rod falls on the fortress, {hit.units} of its ground forces destroyed");
            yield return new WaitForSeconds(2.5f);

            // ---- down there: the forces gone, the crater in the ground
            landing.LandOn(Point("Cinder Fortress"));
            yield return WaitLanding(landing, PlanetLanding.State.OnSurface, 60f);
            yield return new WaitForSeconds(3f);
            float dip = landing.World != null ? landing.World.ScarAt(impact.u, impact.v, out float scorch) : 0f;
            scorch = 0f;
            if (landing.World != null) landing.World.ScarAt(impact.u, impact.v, out scorch);
            int near = 0;
            if (landing.Surface != null)
            {
                Vector3 at = landing.Surface.WorldOf(landing.Surface.OriginU + SurfaceWorld.WrapDelta(impact.u - landing.Surface.OriginU, landing.World.C), impact.v, 0f);
                foreach (var u in landing.Surface.GetComponentsInChildren<GroundUnit>(true))
                    if (u.Alive && new Vector2(u.transform.position.x - at.x, u.transform.position.z - at.z).magnitude < 400f) near++;
                Shot("sk_crater_surface");
                yield return LookAt(at, "sk_crater", 4f, 25f);
            }
            Check(landing.Current == PlanetLanding.State.OnSurface && near == 0 && dip < -20f && scorch > 0.5f,
                  $"landed at the fortress: no ground forces left in the blast ({near}); the crater {-dip:F0} m deep, scorched {scorch:F2}");
            landing.TakeOff();
            yield return WaitLanding(landing, PlanetLanding.State.InSpace, 30f);

            // ---- Port Ochre: rubble where its buildings stood
            landing.LandOn(port);
            yield return WaitLanding(landing, PlanetLanding.State.OnSurface, 60f);
            yield return new WaitForSeconds(3f);
            int ruined = landing.Surface != null ? landing.Surface.Ruined : 0;
            var group = landing.Surface != null ? landing.Surface.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Settlement: Port Ochre") : null;
            if (group != null) yield return LookAt(group.position, "sk_port_ochre_ruins", 6f, 18f);
            Check(ruined > 0, $"down at Port Ochre: {ruined} buildings in rubble");
            landing.TakeOff();
            yield return WaitLanding(landing, PlanetLanding.State.InSpace, 30f);

            // ---- the satellite destroyed: no more strikes on its time
            var ih = ion.GetComponent<Health>();
            if (ih != null) ih.Damage(ih.maxHealth * 50f, ion.transform.position, HitKind.Beam);
            yield return new WaitForSeconds(1f);
            int ionStrikes1 = ion.Strikes;
            WorldState.Advance(ion.intervalMinutes * 60f + 5f);
            yield return null;
            yield return null;
            Check(ih != null && ih.Dead && ion.Strikes == ionStrikes1, $"destroyed, the ion satellite fires no more ({ion.Strikes} strikes, dead {ih?.Dead})");

            // ---- saved
            IndustrySave.TestPath = Path.Combine(dir, "strike_save.json");
            bool saved = IndustryDirector.Instance != null && IndustryDirector.Instance.Save();
            var back = IndustrySave.Read(IndustrySave.TestPath);
            var ochreBack = back?.world?.bodies?.FirstOrDefault(b => b.name == "Ochre");
            var cinderBack = back?.world?.bodies?.FirstOrDefault(b => b.name == "Cinder");
            Check(saved && ochreBack != null && ochreBack.scars.Count > 0 && cinderBack != null && cinderBack.scars.Count > 0 && back.world.timers.Count >= 2,
                  $"the save holds the craters (Ochre {ochreBack?.scars.Count ?? 0}, Cinder {cinderBack?.scars.Count ?? 0}) and the satellites' clocks ({back?.world?.timers.Count ?? 0})");
            if (hull != null) hull.cannotDie = false;
            Log($"{(passed == checks ? "PASS" : "FAIL")}: strikes from orbit ({passed} of {checks})");
        }
    }
}

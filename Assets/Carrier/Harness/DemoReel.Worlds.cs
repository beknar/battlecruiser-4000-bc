using System.Collections;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The all-in-one reel's newer world scenes: the real-size planets (Ochre from orbit, then the
    /// auto-lander flying down through the air to Port Ochre's pad without a break), and the
    /// orbital strikes (the corsair ion satellite burning into Port Ochre, the frigate's kinetic
    /// rod on Cinder Fortress, and Port Ochre's ruins afterwards: the world remembers).
    /// </summary>
    public partial class DemoReel
    {
        /// <summary>Height above a body's mean surface (m).</summary>
        float Above(WarpPoint body) => Vector3.Distance(cc.transform.position, body.Position) - body.bodyRadius;

        /// <summary>Off camera until `done` (or `seconds`), then back on: a cut in a long flight.</summary>
        IEnumerator CutUntil(System.Func<bool> done, float seconds)
        {
            yield return FadeOut(0.4f);
            float until = Time.time + seconds;
            while (!done() && Time.time < until) yield return null;
        }

        IEnumerator RealWorlds()
        {
            var ochre = WP("Ochre");
            var port = WP("Port Ochre");
            var star = WP("Kestrel");
            if (ochre == null || port == null || landing == null) { Note("no Ochre to fly down to"); yield break; }
            Repair();

            // ---- Ochre from orbit, on its sunlit side
            Place(DaySide(ochre, ochre.arrivalDistance), ochre.Position);
            yield return Wait(1f);
            float up = Above(ochre) / 1000f;
            LookPast(800, ochre.Position, D * 2.4f, 35f, 8f, 2f);
            yield return FadeIn();
            Caption("REAL-SIZE WORLDS", $"Ochre is {ochre.bodyRadius * 2f / 1000f:N0} km across and " +
                    $"{(star != null ? Vector3.Distance(ochre.Position, star.Position) / 1e9f : 0f):F1} million km from the star: here from {up:N0} km up", 6f);
            Note($"Ochre: {ochre.bodyRadius * 2f / 1000f:F0} km across, ship {up:F0} km up");
            yield return Wait(6f);
            yield return FadeOut();

            // ---- K: the auto-lander, from 12 km over a site down onto its pad: the one of Ochre's
            // in daylight (the planet turns), warped to as a player would
            Vector3 sunward = star != null ? (star.Position - ochre.Position).normalized : Vector3.up;
            var site = WarpPoint.All.Where(w => w != null && w.kind == WarpPoint.Kind.SurfaceBase && w.LandingBody == ochre)
                           .OrderByDescending(w => Vector3.Dot((w.Position - ochre.Position).normalized, sunward)).FirstOrDefault() ?? port;
            Note($"down to {site.displayName}: sun {Mathf.Asin(Mathf.Clamp(Vector3.Dot((site.Position - ochre.Position).normalized, sunward), -1f, 1f)) * Mathf.Rad2Deg:F0} deg up there");
            float until = Time.time + 25f;
            while (!sysDrive.CanWarp(out _) && Time.time < until) yield return null;
            if (sysDrive.Engage(site, out string refused))
            {
                until = Time.time + 60f;
                while (sysDrive.Current != WarpDrive.State.Ready && Time.time < until) yield return null;
            }
            else { Note($"warp to {site.displayName} refused ({refused}): jumping"); Jump(site); }
            yield return Wait(1.5f);
            until = Time.time + 25f;
            while (!landing.CanLand(out _, out _) && Time.time < until) yield return null;
            bool going = landing.Land();
            Note($"auto-lander over {site.displayName} from {Above(ochre) / 1000f:F1} km: {going}");
            if (!going) yield break;
            yield return Wait(1.5f);
            Shot(801, cc.transform, demo.ShipViewOffset, D * 1.9f, cc.Heading + 160f, 16f, 1.5f);
            yield return FadeIn();
            Caption("FLYING DOWN", "K: the auto-lander takes the ship down from orbit, through the air and onto the pad, without a break", 6f);
            yield return Wait(5.5f);

            // lower, in the thickening air
            yield return CutUntil(() => landing.Current != PlanetLanding.State.InSpace || Above(ochre) < 6000f, 90f);
            if (landing.Current == PlanetLanding.State.InSpace)
            {
                Shot(802, cc.transform, demo.ShipViewOffset, D * 1.6f, cc.Heading - 140f, 10f, 1f);
                yield return FadeIn(0.4f);
                Caption("THE AIR", $"{Above(ochre) / 1000f:F0} km up: drag, heating and the sky coming in, the ground built ahead as it comes", 4.5f);
                yield return Wait(4.5f);
                // down past the hand-off, to the surface
                yield return CutUntil(() => landing.Current == PlanetLanding.State.OnSurface, 90f);
            }
            Note($"over to the surface: {landing.Current}, hand-offs {landing.Handoffs}, auto-lander {landing.Autopilot} ({landing.AutopilotStage})");
            if (landing.Current == PlanetLanding.State.OnSurface)
            {
                yield return Wait(1f);
                Shot(803, cc.transform, demo.ShipViewOffset, D * 1.7f, cc.Heading + 165f, 12f, 1f);
                yield return FadeIn(0.4f);
                Caption("THE SURFACE", $"Below 3 km it goes over to the ground below, and flies on to {site.displayName}'s pad", 4.5f);
                yield return Wait(4.5f);
                // the last of it: over the pad, and down
                yield return CutUntil(() => landing.Landed, 150f);
                Shot(804, cc.transform, demo.ShipViewOffset, D * 1.5f, cc.Heading - 130f, 16f, 3f);
                yield return FadeIn(0.4f);
                Caption(landing.OnPad ? "ON THE PAD" : "DOWN", $"{site.displayName}: down from orbit in one flight", 4f);
                Note($"down at {site.displayName}: landed {landing.Landed}, on the pad {landing.OnPad}");
                yield return Wait(4f);
            }
            yield return FadeOut();
            Clear();
            yield return TakeOffNow();
        }

        IEnumerator StrikesShow()
        {
            var ion = StrikeSatellite.All.FirstOrDefault(x => x != null && x.weapon == StrikeSatellite.Weapon.IonCannon);
            var port = WP("Port Ochre");
            var fortress = WP("Cinder Fortress");
            var cinder = WP("Cinder");
            var ks = KineticStrike.Instance;
            if (ion == null || port == null || landing == null) { Note("no strike satellite to show"); yield break; }
            Repair();

            // ---- the ion satellite over Port Ochre
            var ionWp = WP(ion.Label);
            if (ionWp != null) Jump(ionWp);
            yield return Wait(1f);
            if (OrbitalStrikes.SiteOf(port, out var ochre, out var site))
            {
                Vector3 city = OrbitalStrikes.GroundPoint(ochre, site.su, site.sv);
                // behind the satellite, looking down past it at the city
                ShotAlong(810, ion.transform, Vector3.zero, (city - ion.transform.position).normalized, 1100f, 0f);
            }
            else Shot(810, ion.transform, Vector3.zero, 1100f, cc.Heading, 20f, 2f);
            yield return FadeIn();
            Caption("ORBITAL STRIKES", $"A corsair ion satellite over Port Ochre fires every {ion.intervalMinutes:F0} minutes on the world's clock, watched or not", 6f);
            yield return Wait(2f);
            ion.FireNow();
            yield return Wait(4.5f);
            Note($"ion strike on Port Ochre: {ion.Last.buildings} buildings ruined, {ion.Last.casualties} killed");
            yield return FadeOut();

            // ---- the frigate's own: a kinetic rod on Cinder Fortress
            if (ks != null && fortress != null && cinder != null)
            {
                Jump(fortress);
                yield return Wait(1.5f);
                float until = Time.time + 25f;
                while (ks.ReadyIn > 0f && Time.time < until) yield return null;
                OrbitalStrikes.PlaceOf(cinder, cc.transform.position, out double su, out double sv);
                bool target = OrbitalStrikes.NearestTarget(cinder, su, sv, ks.reach, out double tu, out double tv, out string what, out int count);
                var mark = new GameObject("Strike mark").transform;
                mark.position = target ? OrbitalStrikes.GroundPoint(cinder, tu, tv) : cinder.Position;
                LookPast(811, mark.position, D * 1.7f, 160f, 30f);
                yield return FadeIn();
                Caption("KINETIC STRIKE", $"Y: a tungsten rod dropped from orbit on the ground forces below: {what}", 5f);
                yield return Wait(1.5f);
                int landed0 = ks.Landed;
                bool fired = ks.Fire();
                Note($"rod on {fortress.displayName}: fired {fired} at {what} ({count})");
                yield return Wait(0.6f);
                // on the target as it comes down
                Shot(812, mark, Vector3.zero, 3800f, Bearing(cc.transform.position, mark.position) + 25f, 40f, 0f);
                until = Time.time + 12f;
                while (fired && ks.Landed == landed0 && Time.time < until) yield return null;
                yield return Wait(1f);
                Caption("KINETIC STRIKE", $"{ks.Last.units} ground forces destroyed, and a crater cut into the ground", 4f);
                yield return Wait(3f);
                Note($"the rod landed: {ks.Last.units} destroyed");
                yield return FadeOut();
                Destroy(mark.gameObject);
            }

            // ---- the world remembers: Port Ochre's ruins
            Jump(port);
            yield return Wait(1f);
            if (landing.LandOn(port))
            {
                yield return WaitLanding(PlanetLanding.State.OnSurface, 90f);
                yield return Wait(2f);
                var surf = landing.Surface;
                var group = surf != null ? surf.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Settlement: Port Ochre") : null;
                if (group != null)
                {
                    perFrame = () =>
                    {
                        if (surf != null) HoldHeight(surf, 220f);
                        demo.autoPitch = 0f;
                        demo.autoRudder = 0f;
                    };
                    cc.throttle = 0.1f;
                    Shot(813, group, Vector3.zero, 1900f, Bearing(cc.transform.position, group.position) + 30f, 24f, 4f);
                    yield return FadeIn();
                    Caption("THE WORLD REMEMBERS", $"Port Ochre after the strike: {surf.Ruined} buildings in rubble until they are rebuilt; ground forces come back in their own time", 6f);
                    Note($"Port Ochre: {surf.Ruined} buildings ruined");
                    yield return Wait(5.5f);
                    yield return FadeOut();
                }
            }
            perFrame = null;
            Clear();
            cc.throttle = 0f;
            yield return TakeOffNow();
        }
    }
}

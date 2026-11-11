using System.Collections;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The sea reel (-reel &lt;folder&gt; -sea, in the Star System): each of the four frigates in
    /// turn comes in from space to Pelagia, the pirates' water world, comes down on the sea, sails
    /// in on an island base with its guns going, dives, takes on a torpedo base on the sea floor,
    /// and launches straight out of the water, back to space.
    /// </summary>
    public partial class DemoReel
    {
        public static bool SeaReel;

        IEnumerator SeaShowReel()
        {
            recording = false;
            while (frame < Warmup) yield return null;
            landing = PlanetLanding.Instance;
            Arm();
            SideGuns(false);
            var roster = FrigateRoster.Get();
            var pelagia = WP("Pelagia");
            if (roster == null || pelagia == null || landing == null) { Note("no roster, Pelagia or landing"); finished = true; yield break; }
            Note($"sea reel: {string.Join(", ", roster.frigates.Select(f => f.displayName))} at {pelagia.displayName}");

            for (int i = 0; i < roster.frigates.Length; i++)
            {
                var e = roster.frigates[i];
                if (!string.Equals(PlayerFrigate.Flying, e.id, System.StringComparison.OrdinalIgnoreCase)) PlayerFrigate.SwapTo(e.id);
                Arm();
                yield return SeaRun(i, e, pelagia);
            }
            Note($"the end: flying the {PlayerFrigate.Flying}");
            finished = true;
        }

        /// <summary>One frigate's run: down from orbit, onto the sea, the island, under, out.</summary>
        IEnumerator SeaRun(int index, FrigateRoster.Entry e, WarpPoint pelagia)
        {
            // ---- over Pelagia (somewhere new each time, so each comes down on fresh water)
            Repair();
            float a = 40f + index * 83f;
            Vector3 dir = Quaternion.Euler(18f - index * 9f, a, 0f) * Vector3.forward;
            Vector3 orbit = pelagia.Position + dir * pelagia.arrivalDistance * 0.85f;
            Place(orbit, pelagia.Position);
            cc.throttle = 0.35f;
            Shot(500 + index * 10, cc.transform, demo.ShipViewOffset, D * 0.9f, cc.Heading + 150f, 12f, 5f);
            yield return FadeIn();
            Caption(e.displayName.ToUpperInvariant(), $"{e.role}: down to {pelagia.displayName}, the pirates' water world", 5f);
            yield return Wait(4f);
            landing.LandOn(pelagia);
            yield return WaitLanding(PlanetLanding.State.OnSurface, 40f);
            var sf = Seafaring.Instance;
            if (landing.Current != PlanetLanding.State.OnSurface || sf == null || !Sea.Here) { Note($"  {e.displayName}: didn't get down"); yield break; }
            Arm();
            var surface = landing.Surface;

            // ---- the sea forces and an island base near by
            var units = FindObjectsByType<GroundUnit>(FindObjectsSortMode.None).Where(u => u != null && u.Alive).ToArray();
            var seaBase = units.Where(u => u.name == "Sea Base").OrderBy(u => Vector3.Distance(u.transform.position, cc.transform.position)).FirstOrDefault();
            var island = units.Where(u => !u.sonar && (u.name == "Ground Battery" || u.name == "Missile Site") && u.transform.position.y > Sea.Level + 2f)
                              .OrderBy(u => seaBase != null ? Vector3.Distance(u.transform.position, seaBase.transform.position) : Vector3.Distance(u.transform.position, cc.transform.position))
                              .FirstOrDefault();
            Note($"  {e.displayName}: island guns {(island != null ? island.name : "none")}, sea base {(seaBase != null ? "found" : "none")}");

            // ---- onto the sea, in sight of the island
            Vector3 islandAt = island != null ? island.transform.position : cc.transform.position + cc.transform.forward * 3000f;
            Vector3 water = SeaNear(surface, islandAt, 1500f, 2600f);
            Vector3 toIsland = islandAt - water;
            toIsland.y = 0f;
            yield return FadeOut(0.3f);
            Place(new Vector3(water.x, Sea.Level + 170f, water.z), new Vector3(islandAt.x, Sea.Level + 170f, islandAt.z));
            Shot(501 + index * 10, cc.transform, demo.ShipViewOffset, D * 0.75f, cc.Heading + 115f, 6f, 4f);
            yield return Wait(0.2f);
            yield return FadeIn(0.3f);
            Caption("SPLASHDOWN", "Down on the sea, afloat", 4f);
            demo.autoElevator = -1f;
            float until = Time.time + 25f;
            while (sf.Current == Seafaring.State.Flying && Time.time < until) yield return null;
            demo.autoElevator = null;
            yield return Wait(1.5f);

            // ---- sailing in on the island base, guns going
            if (island != null) demo.SelectTarget(island.transform);
            SideGuns(true);
            cc.throttle = 0.7f;
            Shot(502 + index * 10, cc.transform, demo.ShipViewOffset, D * 0.6f, cc.Heading - 60f, 5f, -4f);
            Caption("ISLAND BASE", "Sailing in under its guns", 5f);
            yield return Wait(5f);
            if (island != null) Shot(503 + index * 10, island.transform, Vector3.up * 30f, 420f, Bearing(island.transform.position, cc.transform.position) + 25f, 12f, 3f);
            yield return Wait(4f);

            // ---- under the sea, on to the torpedo base
            Shot(504 + index * 10, cc.transform, demo.ShipViewOffset, D * 0.6f, cc.Heading + 160f, 14f, 0f);
            Caption("DIVE", "Under the sea", 3.5f);
            demo.autoElevator = -1f;
            until = Time.time + 8f;
            while (sf.Current != Seafaring.State.Under && Time.time < until) yield return null;
            yield return Wait(2.5f);
            demo.autoElevator = null;
            SideGuns(false);
            cc.SetTarget(null);
            if (seaBase != null && seaBase.Alive)
            {
                yield return FadeOut(0.3f);
                // near it, with a clear line to it (not behind a rise in the sea bed)
                Vector3 to = seaBase.transform.position - cc.transform.position;
                to.y = 0f;
                Vector3 at = Vector3.zero;
                for (int k = 0; k < 16; k++)
                {
                    Vector3 from = Quaternion.Euler(0f, k * 22.5f, 0f) * (to.sqrMagnitude > 1f ? to.normalized : Vector3.forward);
                    Vector3 p = seaBase.transform.position - from * 950f;
                    p.y = Mathf.Clamp(seaBase.transform.position.y + 20f, surface.GroundAt(p) + 30f, Sea.Level - 30f);
                    if (k == 0) at = p;
                    if (!Cover.Blocked(p, seaBase.transform.position)) { at = p; break; }
                }
                Place(at, seaBase.transform.position);
                sf.Submerge();
                Shot(505 + index * 10, cc.transform, demo.ShipViewOffset, D * 0.55f, cc.Heading + 140f, 8f, 3f);
                yield return Wait(0.2f);
                yield return FadeIn(0.3f);
                Caption("TORPEDO BASE", "On the sea floor, with submarines about", 5f);
                demo.SelectTarget(seaBase.transform);
                SideGuns(true);
                cc.throttle = 0.3f;
                yield return Wait(5f);
                if (seaBase != null) Shot(506 + index * 10, seaBase.transform, Vector3.up * 5f, 230f, Bearing(seaBase.transform.position, cc.transform.position) + 35f, 10f, 4f);
                yield return Wait(5f);
                SideGuns(false);
                cc.SetTarget(null);
            }

            // ---- and straight out of the water
            Shot(507 + index * 10, cc.transform, demo.ShipViewOffset, D * 0.7f, cc.Heading + 100f, 4f, 6f);
            Caption("LAUNCH", "Straight up out of the water", 4f);
            cc.throttle = 0.6f;
            demo.autoElevator = 1f;
            until = Time.time + 25f;
            while (sf.Current == Seafaring.State.Under && Time.time < until) yield return null;
            float t0 = Time.time;
            perFrame = () => cam.distance = Mathf.Lerp(D * 0.7f, D * 1.3f, Smooth((Time.time - t0) / 4f));
            yield return Wait(4f);
            perFrame = null;
            demo.autoElevator = null;
            Note($"  {e.displayName}: splashdowns {sf.Splashdowns}, dives {sf.Dives}, launches {sf.Launches}; " +
                 $"island {(island == null ? "-" : island.Alive ? "standing" : "destroyed")}, sea base {(seaBase == null ? "-" : seaBase.Alive ? "standing" : "destroyed")}");
            yield return FadeOut();
            Clear();
            // back to space for the next one
            if (landing.Current == PlanetLanding.State.OnSurface)
            {
                landing.TakeOff();
                yield return WaitLanding(PlanetLanding.State.InSpace, 30f);
            }
        }

        /// <summary>Open water near a place: as deep as may be, between `near` and `far` off it.</summary>
        static Vector3 SeaNear(PlanetSurface surface, Vector3 place, float near, float far)
        {
            Vector3 best = place;
            float deepest = float.NegativeInfinity;
            for (float r = near; r <= far; r += 150f)
                for (int k = 0; k < 24; k++)
                {
                    float ang = k * Mathf.PI * 2f / 24f;
                    var p = place + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                    float d = Sea.Level - surface.GroundAt(p);
                    if (d > deepest) { deepest = d; best = p; }
                }
            return best;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The places a ship warps to, and the points of interest at each. A place is the star, a
    /// planet, a moon, an asteroid field, a comet, a Lagrange point, or anything built out in
    /// deep space on its own. Everything else is a point of interest at a place: a base on a
    /// planet or moon (its LandingBody), a station or battery in orbit close by, a base in an
    /// asteroid field. The system map warps to places; at one, the navigation mode's menu lists its
    /// points of interest, to fly to (or land or dock at) in real time.
    /// </summary>
    public static class Places
    {
        /// <summary>A body (or a point) that is a place in itself.</summary>
        public static bool IsBody(WarpPoint w) =>
            w != null && (w.kind == WarpPoint.Kind.Star || w.kind == WarpPoint.Kind.Planet || w.kind == WarpPoint.Kind.Moon
                          || w.kind == WarpPoint.Kind.AsteroidField || w.kind == WarpPoint.Kind.Comet || w.kind == WarpPoint.Kind.Lagrange);

        /// <summary>A place the system map warps to (a body, or something built out on its own).</summary>
        public static bool IsPlace(WarpPoint w) => w != null && PlaceOf(w) == null;

        /// <summary>How far out from a planet or moon (above its surface) its points of interest are.</summary>
        public static float Reach(WarpPoint body) => Mathf.Max(body.bodyRadius * 2.5f, 60000f);

        // (worked out for each point now and then, not every call: the map and the menu ask
        // for every point's place many times a frame, and each answer scans every point)
        static readonly Dictionary<WarpPoint, WarpPoint> placeOf = new Dictionary<WarpPoint, WarpPoint>();
        static float cachedAt = float.NegativeInfinity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { placeOf.Clear(); cachedAt = float.NegativeInfinity; }

        /// <summary>The place a point of interest is at (null if it is a place itself).</summary>
        public static WarpPoint PlaceOf(WarpPoint w)
        {
            if (w == null || IsBody(w)) return null;
            if (w.kind == WarpPoint.Kind.Landmark) return w.follow;
            // (a band on patrol: a place of its own, seen moving on the map)
            if (w.kind == WarpPoint.Kind.Patrol) return null;
            float now = Time.unscaledTime;
            if (now - cachedAt > 2f || now < cachedAt) { placeOf.Clear(); cachedAt = now; }
            if (placeOf.TryGetValue(w, out var known)) return known;
            var found = Find(w);
            placeOf[w] = found;
            return found;
        }

        static WarpPoint Find(WarpPoint w)
        {
            // a base on a planet or moon
            if (w.LandingBody != w && IsBody(w.LandingBody)) return w.LandingBody;
            Vector3 p = w.Position;
            // in orbit of a planet or moon (the nearest, close enough)
            var body = Demo.PlanetFlight.NearestBody(p, out float alt);
            if (body != null && alt < Reach(body)) return body;
            // in an asteroid field
            WarpPoint field = null;
            float best = float.MaxValue;
            foreach (var f in WarpPoint.All)
            {
                if (f == null || f.kind != WarpPoint.Kind.AsteroidField) continue;
                float d = Vector3.Distance(p, f.Position);
                if (d < Mathf.Max(f.bodyRadius * 1.6f, 80000f) && d < best) { best = d; field = f; }
            }
            return field;
        }

        /// <summary>The points of interest at a place, nearest to `from` first.</summary>
        public static List<WarpPoint> PointsOfInterest(WarpPoint place, Vector3 from) =>
            WarpPoint.All.Where(w => w != null && w != place && PlaceOf(w) == place)
                         .OrderBy(w => (w.Position - from).sqrMagnitude).ToList();

        /// <summary>Everything round a place, as the system map lists it: for a planet, its moons
        /// first (nearest it first), then its own points of interest (its landmarks, stations and
        /// bases), then each moon's in turn; for any other place, its points of interest. (A moon
        /// is a place of its own, so it is never among its planet's PointsOfInterest.)</summary>
        public static List<WarpPoint> Around(WarpPoint place)
        {
            var list = new List<WarpPoint>();
            if (place == null) return list;
            Vector3 at = place.Position;
            var moons = place.kind != WarpPoint.Kind.Planet ? new List<WarpPoint>()
                      : WarpPoint.All.Where(w => w != null && w != place && w.kind == WarpPoint.Kind.Moon && w.orbitParent == place)
                                     .OrderBy(w => (w.Position - at).sqrMagnitude).ToList();
            list.AddRange(moons);
            list.AddRange(PointsOfInterest(place, at));
            foreach (var m in moons) list.AddRange(PointsOfInterest(m, m.Position));
            return list;
        }

        /// <summary>The place a point is at: the planet or moon it is near, the asteroid field
        /// it is in, or the nearest place out in space within a few hundred kilometres (null in
        /// open space).</summary>
        public static WarpPoint At(Vector3 p)
        {
            var body = Demo.PlanetFlight.NearestBody(p, out float alt);
            if (body != null && alt < Reach(body)) return body;
            WarpPoint best = null;
            float bestD = float.MaxValue;
            foreach (var w in WarpPoint.All)
            {
                if (w == null || !IsPlace(w)) continue;
                float reach = w.kind == WarpPoint.Kind.AsteroidField ? Mathf.Max(w.bodyRadius * 1.6f, 80000f) : Mathf.Max(w.arrivalDistance * 40f, 200000f);
                float d = Vector3.Distance(p, w.Position);
                if (d < reach && d < bestD) { bestD = d; best = w; }
            }
            return best;
        }
    }
}

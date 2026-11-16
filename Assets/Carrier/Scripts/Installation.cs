using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Something built in the star system: a space station, an installation, an orbital weapons
    /// battery, an asteroid base, a base on a planet's surface, or an installation at a Lagrange
    /// point. A friendly one with a berth can be docked at (the frigate is repaired there, and
    /// where there is a market its ore is sold). A hostile one is defended by a DefenseGrid.
    /// </summary>
    public class Installation : MonoBehaviour
    {
        public string displayName = "Installation";
        public WarpPoint.Kind kind = WarpPoint.Kind.Installation;
        public bool hostile;
        [Tooltip("Ships can dock here (friendly only).")]
        public bool canDock;
        [Tooltip("Buys ore (when docked, the hold is sold).")]
        public bool market;
        [Tooltip("Where a docked ship sits; its forward is the way out.")]
        public Transform berth;
        // the last of the way in: from here the ship is brought in to the berth (under a fade)
        [Tooltip("Docking is possible within this distance of the berth (m).")]
        public float dockRange = 1600f;

        /// <summary>From where a warp or an auto-flight leaves the ship (its warp point's arrival
        /// distance, and the way navigation counts as there), K docks here: within dockRange at
        /// once, farther out by flying in to the berth first. Never less than dockRange.</summary>
        public float ApproachRange
        {
            get
            {
                var wp = Point;
                if (wp == null) return dockRange;
                float far = wp.arrivalDistance + Vector3.Distance(BerthPosition, wp.Position) + 2500f;
                return Mathf.Max(dockRange, far);
            }
        }

        WarpPoint point;
        bool pointLooked;
        /// <summary>Its warp point (the one of its name), or null.</summary>
        public WarpPoint Point
        {
            get
            {
                if (point == null && !pointLooked)
                {
                    pointLooked = true;
                    foreach (var w in WarpPoint.All) if (w != null && w.displayName == displayName) { point = w; break; }
                }
                return point;
            }
        }

        public static readonly List<Installation> All = new List<Installation>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }

        // its crew (CrewTable): none on an automated one
        void Awake()
        {
            if (GetComponent<Crew>() != null) return;
            var table = CrewTable.Get();
            int n = table.CrewOf(displayName);
            if (n <= 0) return;
            var c = gameObject.AddComponent<Crew>();
            c.complement = n;
            var place = table.Place(displayName);
            c.reinforcePerMinute = hostile && place != null ? place.refillPerMinute : 0f;
        }
        void OnDisable() => All.Remove(this);

        public Vector3 BerthPosition => berth != null ? berth.position : transform.position;

        /// <summary>The nearest friendly place to dock within its docking range of a point, or null.</summary>
        public static Installation DockableNear(Vector3 p)
        {
            Installation best = null;
            float bestD = float.MaxValue;
            foreach (var s in All)
            {
                if (s == null || !s.canDock || s.hostile) continue;
                float d = Vector3.Distance(p, s.BerthPosition);
                if (d < s.dockRange && d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        /// <summary>The friendly place to dock whose approach a point is in (where a warp or an
        /// auto-flight there leaves the ship), the one in docking range first, else the nearest; null if none.</summary>
        public static Installation ApproachableNear(Vector3 p)
        {
            var close = DockableNear(p);
            if (close != null) return close;
            Installation best = null;
            float bestD = float.MaxValue;
            foreach (var s in All)
            {
                if (s == null || !s.canDock || s.hostile) continue;
                float d = Vector3.Distance(p, s.BerthPosition);
                if (d >= s.ApproachRange || d >= bestD) continue;
                // (nearer the middle of the field or body it is at than to it: there, K lands
                // as it always has; only on its own side of the place is it the station's)
                var wp = s.Point;
                var place = wp != null ? Places.PlaceOf(wp) : null;
                if (place != null && place.surface != SurfaceKind.None &&
                    Vector3.Distance(p, place.Position) - (place.kind == WarpPoint.Kind.AsteroidField ? 0f : place.bodyRadius) < d) continue;
                bestD = d; best = s;
            }
            return best;
        }

        /// <summary>The ship at `p` is close enough to be brought in to the berth now.</summary>
        public bool InDockRange(Vector3 p) => Vector3.Distance(p, BerthPosition) < dockRange;

        /// <summary>Where to steer for on the way in: out in front of the berth (its way out),
        /// so the ship comes in on the open side rather than through the rock or the hull it is
        /// built into; the berth itself once in front of it.</summary>
        public Vector3 ApproachPoint(Vector3 from)
        {
            if (berth == null) return BerthPosition;
            Vector3 b = BerthPosition, f = berth.forward;
            float ahead = Vector3.Dot(from - b, f);
            if (ahead > dockRange * 0.25f) return b;
            Vector3 front = b + f * Mathf.Min(dockRange * 0.6f, 1000f);
            // (what the berth is built into, round its middle: as far out as the berth, and a margin)
            Vector3 c = Point != null ? Point.Position : transform.position;
            float r = Vector3.Distance(b, c) + 300f;
            if (SegmentClear(from, front, c, r)) return front;
            // the way to the front blocked: out to the side first, level with the berth
            Vector3 side = Vector3.ProjectOnPlane(from - c, f);
            if (side.sqrMagnitude < 1f) side = Vector3.Cross(f, Vector3.up);
            if (side.sqrMagnitude < 1f) side = Vector3.Cross(f, Vector3.right);
            return c + side.normalized * (r + 500f) + f * Mathf.Max(0f, Vector3.Dot(b - c, f));
        }

        /// <summary>The straight way from a to b keeps more than r from c.</summary>
        static bool SegmentClear(Vector3 a, Vector3 b, Vector3 c, float r)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(c - a, ab) / ab.sqrMagnitude) : 0f;
            return (a + ab * t - c).sqrMagnitude > r * r;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A place in the star system the frigate can mini-warp to: the star, a planet, a moon, an
    /// asteroid field or a comet. The ship drops out of warp `arrivalDistance` from it, on the side it came
    /// from, facing it. The system map draws it at its position (seen from above), with its orbit.
    /// </summary>
    public class WarpPoint : MonoBehaviour
    {
        public enum Kind { Star, Planet, Moon, AsteroidField, Comet }

        public string displayName = "Unknown";
        public Kind kind = Kind.Planet;
        [TextArea] public string description = "";
        [Tooltip("Radius of the body itself (m), 0 for a field.")]
        public float bodyRadius;
        [Tooltip("How far from its centre the ship drops out of warp (m).")]
        public float arrivalDistance = 5000f;
        public Color mapColor = Color.white;
        [Tooltip("Shown on the map (in m): a planet's orbit is round the star, a moon's round its planet.")]
        public WarpPoint orbitParent;
        [Tooltip("Hostiles are known to be here (shown on the map).")]
        public bool hostile;
        [Tooltip("What its surface is like, for landing (None: can't land).")]
        public SurfaceKind surface = SurfaceKind.None;

        public Vector3 Position => transform.position;

        public static readonly List<WarpPoint> All = new List<WarpPoint>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        /// <summary>Where a ship coming from `from` drops out of warp: on the near side, clear of
        /// the body and of anything solid.</summary>
        public Vector3 ArrivalPoint(Vector3 from)
        {
            Vector3 dir = from - Position;
            dir.y *= 0.3f;                              // arrive roughly level with it
            if (dir.sqrMagnitude < 1f) dir = Vector3.back;
            Vector3 p = Position + dir.normalized * arrivalDistance + Vector3.up * (arrivalDistance * 0.08f);
            if (Cover.Mask != 0)
                for (int i = 0; i < 40 && Physics.CheckSphere(p, 250f, Cover.Mask, QueryTriggerInteraction.Ignore); i++)
                    p += dir.normalized * 200f + Vector3.up * 60f;
            return p;
        }

        /// <summary>The nearest destination to a point (where the ship is now), or null.</summary>
        public static WarpPoint Nearest(Vector3 p)
        {
            // inside some arrival zones: the smallest of them (a moon rather than its planet);
            // otherwise the one whose zone is closest, relative to its size
            WarpPoint best = null;
            float bestZone = float.MaxValue, bestRatio = float.MaxValue;
            foreach (var w in All)
            {
                if (w == null) continue;
                float d = Vector3.Distance(p, w.Position);
                if (d < w.arrivalDistance * 1.4f && w.arrivalDistance < bestZone) { bestZone = w.arrivalDistance; best = w; }
            }
            if (best != null) return best;
            foreach (var w in All)
            {
                if (w == null) continue;
                float r = Vector3.Distance(p, w.Position) / Mathf.Max(1f, w.arrivalDistance);
                if (r < bestRatio) { bestRatio = r; best = w; }
            }
            return best;
        }
    }
}

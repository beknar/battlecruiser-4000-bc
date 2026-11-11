using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Something solid enough to hide behind: an asteroid or a large piece of wreckage. Its
    /// colliders sit on the "Obstacle" layer, so they block shots, beams and missiles, and they
    /// block line of sight: guns won't fire at, and enemies won't shoot at, a ship behind one.
    /// Pilots use the rough sphere (`radius`) to find hiding places and to keep clear of it.
    /// </summary>
    public class Obstacle : MonoBehaviour
    {
        [Tooltip("Radius of a sphere that roughly encloses the obstacle (m).")]
        public float radius = 100f;
        [Tooltip("Radius of a sphere that is safely inside it (m): pilots never fly inside this.")]
        public float coreRadius = 60f;
        [Tooltip("Big enough for a ship to hide behind.")]
        public bool cover = true;
        [Tooltip("Middle of the obstacle in its own space (wreck sections aren't centred on their pivot).")]
        public Vector3 centerOffset;

        public Vector3 Center => transform.TransformPoint(centerOffset);

        public static readonly List<Obstacle> All = new List<Obstacle>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
    }

    /// <summary>Line-of-sight tests against the obstacles in the scene (none in open space).</summary>
    public static class Cover
    {
        public const string LayerName = "Obstacle";
        static int mask = -1;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => mask = -1;

        /// <summary>Layer mask of the obstacles (0 if the project has no Obstacle layer).</summary>
        public static int Mask
        {
            get
            {
                if (mask < 0) { int l = LayerMask.NameToLayer(LayerName); mask = l < 0 ? 0 : 1 << l; }
                return mask;
            }
        }

        /// <summary>The scene has obstacles to hide behind.</summary>
        public static bool Active => Obstacle.All.Count > 0 && Mask != 0;

        /// <summary>Is the straight line from a to b blocked by an asteroid or wreck?</summary>
        public static bool Blocked(Vector3 a, Vector3 b) =>
            Sea.Across(a, b) || (Active && Physics.Linecast(a, b, Mask, QueryTriggerInteraction.Ignore));

        /// <summary>Can a see b (nothing solid in between)?</summary>
        public static bool Visible(Vector3 a, Vector3 b) => !Blocked(a, b);

        /// <summary>Is this collider part of an obstacle?</summary>
        public static bool IsObstacle(Collider c) => c != null && Mask != 0 && ((1 << c.gameObject.layer) & Mask) != 0;

        /// <summary>A place on the far side of an obstacle from `threat`, near `from`, where a ship
        /// of the given size is out of sight. Returns false if there is none within `searchRadius`.</summary>
        public static bool FindHidingPlace(Vector3 from, Vector3 threat, float shipRadius, float searchRadius, out Vector3 place)
        {
            place = from;
            float best = float.MaxValue;
            bool found = false;
            foreach (var o in Obstacle.All)
            {
                if (o == null || !o.cover || o.radius < shipRadius * 2f) continue;
                Vector3 c = o.Center;
                float d = Vector3.Distance(from, c);
                if (d - o.radius > searchRadius) continue;
                Vector3 away = c - threat;
                if (away.sqrMagnitude < 1f) continue;
                Vector3 spot = c + away.normalized * (o.radius + shipRadius * 2f + 60f);
                if (!Blocked(spot, threat)) continue;
                if (Physics.CheckSphere(spot, shipRadius * 2f + 20f, Mask, QueryTriggerInteraction.Ignore)) continue;
                float cost = Vector3.Distance(from, spot);
                if (cost < best) { best = cost; place = spot; found = true; }
            }
            return found;
        }

        /// <summary>Steer a flight direction around obstacles: if something solid lies ahead
        /// within `lookAhead`, bend the direction along its surface and away from it.</summary>
        public static Vector3 Avoid(Vector3 position, Vector3 direction, float bodyRadius, float lookAhead)
        {
            if (!Active || direction.sqrMagnitude < 1e-4f) return direction;
            Vector3 d = direction.normalized;
            if (Physics.SphereCast(position, bodyRadius, d, out var hit, lookAhead, Mask, QueryTriggerInteraction.Ignore))
            {
                float urgency = 1f - hit.distance / lookAhead;
                Vector3 along = Vector3.ProjectOnPlane(d, hit.normal);
                if (along.sqrMagnitude < 1e-3f) along = Vector3.Cross(hit.normal, Vector3.up);
                d = (along.normalized + hit.normal * (0.6f + 2f * urgency)).normalized;
            }
            return d;
        }

        /// <summary>Push a point out of any obstacle's solid core (for pilots that strayed inside).</summary>
        public static Vector3 PushOut(Vector3 position, float bodyRadius)
        {
            foreach (var o in Obstacle.All)
            {
                if (o == null) continue;
                float r = o.coreRadius + bodyRadius;
                Vector3 v = position - o.Center;
                if (v.sqrMagnitude < r * r) position = o.Center + (v.sqrMagnitude > 1e-3f ? v.normalized : Vector3.up) * r;
            }
            return position;
        }
    }
}

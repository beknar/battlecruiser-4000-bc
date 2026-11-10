using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The players' ships enemies can go for: the local player's, and in co-op the other players'
    /// (their stand-ins in this game). In single-player there is just the one.
    /// </summary>
    public static class PlayerShips
    {
        static readonly List<Transform> all = new List<Transform>();

        /// <summary>Whether a ship can be gone for now (co-op: not one in warp or down on a planet).</summary>
        public static System.Func<Transform, bool> Available;

        public static void Add(Transform t) { if (t != null && !all.Contains(t)) all.Add(t); }
        public static void Remove(Transform t) => all.Remove(t);
        public static int Count { get { all.RemoveAll(t => t == null); return all.Count; } }
        public static IReadOnlyList<Transform> All => all;

        /// <summary>The local player's ship (the one with the controls; the other players' ships
        /// in co-op are copies without them), or null.</summary>
        public static Transform Primary
        {
            get
            {
                var cc = Object.FindAnyObjectByType<CarrierController>();
                return cc != null ? cc.transform : null;
            }
        }

        /// <summary>For a hostile going for a ship: with more than one about (co-op), the nearest
        /// to `from`, looked for at most every `interval` seconds (`next` keeps the time); else
        /// `current`, unchanged.</summary>
        public static Transform Retarget(Transform current, Vector3 from, ref float next, float interval = 2f)
        {
            if (Count < 2 || Time.time < next) return current;
            next = Time.time + interval;
            var nearest = Nearest(from);
            return nearest != null ? nearest : current;
        }

        /// <summary>Is this ship here to be seen (co-op: not one in warp or down on a planet)?</summary>
        public static bool Present(Transform t) => t != null && (Available == null || Available(t));

        /// <summary>The nearest ship still in the fight (the nearest at all if none is), or null.</summary>
        public static Transform Nearest(Vector3 p)
        {
            Transform best = null, any = null;
            float bestD = float.MaxValue, anyD = float.MaxValue;
            foreach (var t in all)
            {
                if (t == null) continue;
                float d = (t.position - p).sqrMagnitude;
                if (d < anyD) { anyD = d; any = t; }
                var h = t.GetComponent<Health>();
                if (h != null && h.Dead) continue;
                if (Available != null && !Available(t)) continue;
                if (d < bestD) { bestD = d; best = t; }
            }
            return best != null ? best : any;
        }
    }
}

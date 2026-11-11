using System;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The sea, while the ship is down on a water world: how high its surface is, so that whatever
    /// moves or shoots knows which side of it something is. The surface hides what is under it:
    /// nothing sees or shoots across it (Cover.Blocked), so a frigate can dive out of the fight
    /// above, and only torpedoes reach a ship under water.
    /// </summary>
    public static class Sea
    {
        static Func<float> level;

        /// <summary>There is a sea here (the ship is on a water world's surface).</summary>
        public static bool Here => level != null;

        /// <summary>The surface's height (world y; -infinity with no sea).</summary>
        public static float Level => level != null ? level() : float.NegativeInfinity;

        /// <summary>The sea's height comes from here while the ship is on this world.</summary>
        public static void Set(Func<float> height) => level = height;

        public static void Clear() => level = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => level = null;

        /// <summary>Under the surface.</summary>
        public static bool Under(Vector3 p) => level != null && p.y < level();

        /// <summary>How far under the surface (negative above it; 0 with no sea).</summary>
        public static float Depth(Vector3 p) => level != null ? level() - p.y : 0f;

        /// <summary>A line from a to b goes through the surface (one end under it, one above).</summary>
        public static bool Across(Vector3 a, Vector3 b)
        {
            if (level == null) return false;
            float l = level();
            return (a.y < l) != (b.y < l);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// What the player can target: the level's hostiles (from the scene), and things that come
    /// and go while it runs (ground forces, built as the ship reaches them and thrown away with
    /// their part of a planet). Gameplay adds and removes; the controls and the HUD read it.
    /// </summary>
    public static class Targets
    {
        static readonly List<Transform> list = new List<Transform>();

        /// <summary>Everything targetable, in the order it came (destroyed ones may be null).</summary>
        public static IReadOnlyList<Transform> All => list;

        /// <summary>Something left the list, and where it was in it.</summary>
        public static event Action<Transform, int> Removed;

        /// <summary>Start a level's list over with what its scene holds.</summary>
        public static void Reset(IEnumerable<Transform> initial)
        {
            list.Clear();
            if (initial != null) list.AddRange(initial);
        }

        /// <summary>Something new to target.</summary>
        public static void Add(Transform t)
        {
            if (t == null || list.Contains(t)) return;
            list.Add(t);
        }

        /// <summary>Something gone for good.</summary>
        public static void Remove(Transform t)
        {
            int i = list.IndexOf(t);
            if (i < 0) return;
            list.RemoveAt(i);
            Removed?.Invoke(t, i);
        }
    }
}

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

        /// <summary>The friendly stations and installations in space: they can be targeted too, to
        /// fly to (U) and see on the target display, but the guns never fire on them. (Not the
        /// bases down on a surface: navigation mode flies down to those, and lands.)</summary>
        public static IEnumerable<Transform> Friendly
        {
            get
            {
                foreach (var i in Installation.All)
                    if (i != null && !i.hostile && i.isActiveAndEnabled && i.kind != WarpPoint.Kind.SurfaceBase) yield return i.transform;
            }
        }

        /// <summary>Hostiles first, in the list's order, then the friendly installations.</summary>
        public static List<Transform> AllWithFriendly()
        {
            var l = new List<Transform>(list);
            foreach (var f in Friendly) if (!l.Contains(f)) l.Add(f);
            return l;
        }

        /// <summary>A friendly installation (never fired on).</summary>
        public static bool IsFriendly(Transform t) => t != null && t.TryGetComponent(out Installation i) && !i.hostile && i.kind != WarpPoint.Kind.SurfaceBase;

        /// <summary>What to call it: its target info's name, an installation's, or its object's.</summary>
        public static string NameOf(Transform t) =>
            t == null ? "" : t.TryGetComponent(out TargetInfo info) ? info.Name : t.TryGetComponent(out Installation i) ? i.displayName : t.name;

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

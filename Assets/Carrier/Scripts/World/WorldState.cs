using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// What happens in the world whether or not anyone is there to see it, kept apart from what is
    /// drawn: a game clock, and for each body the areas explored and the ground forces destroyed
    /// (with when, so they are rebuilt in time, off-screen: when an area is built again, whatever
    /// has had its time is back). It outlives the level and is saved with the game (IndustrySave).
    /// The industry's lines and the recruit pools (IndustryLedger) are the same kind of thing.
    /// </summary>
    public static class WorldState
    {
        /// <summary>Game seconds since the game began (saved: it goes on from where it was).</summary>
        public static double Clock { get; private set; }
        /// <summary>How much faster than game time the clock runs (the self-test speeds it up).</summary>
        public static float SpeedUp = 1f;

        static readonly Dictionary<string, BodyMemory> bodies = new Dictionary<string, BodyMemory>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Clock = 0; SpeedUp = 1f; bodies.Clear(); }

        public static void Advance(float dt) { if (dt > 0f) Clock += dt * SpeedUp; }

        /// <summary>A body's memory (made empty the first time).</summary>
        public static BodyMemory Body(string name)
        {
            name ??= "";
            if (!bodies.TryGetValue(name, out var m)) bodies[name] = m = new BodyMemory();
            return m;
        }

        public static IEnumerable<KeyValuePair<string, BodyMemory>> Bodies => bodies;

        public class BodyMemory
        {
            public readonly HashSet<int> explored = new HashSet<int>();
            public readonly DestroyedSet destroyed = new DestroyedSet();
        }

        // ------------------------------------------------------------ saving

        [Serializable]
        public class SavedBody
        {
            public string name;
            public List<int> explored = new List<int>();
            public List<long> units = new List<long>();
            public List<double> destroyedAt = new List<double>();
            public List<float> rebuildMinutes = new List<float>();
        }

        [Serializable]
        public class Saved
        {
            public double clock;
            public List<SavedBody> bodies = new List<SavedBody>();
        }

        public static Saved Snapshot()
        {
            var s = new Saved { clock = Clock };
            foreach (var kv in bodies)
            {
                var b = new SavedBody { name = kv.Key, explored = new List<int>(kv.Value.explored) };
                foreach (var (key, at, minutes) in kv.Value.destroyed.Entries())
                {
                    b.units.Add(key);
                    b.destroyedAt.Add(at);
                    b.rebuildMinutes.Add(minutes);
                }
                s.bodies.Add(b);
            }
            return s;
        }

        public static void Restore(Saved s)
        {
            bodies.Clear();
            Clock = s != null ? Math.Max(0.0, s.clock) : 0.0;
            if (s?.bodies == null) return;
            foreach (var b in s.bodies)
            {
                if (b == null || b.name == null) continue;
                var m = Body(b.name);
                if (b.explored != null) foreach (int i in b.explored) m.explored.Add(i);
                if (b.units == null) continue;
                for (int i = 0; i < b.units.Count; i++)
                    m.destroyed.Restore(b.units[i], b.destroyedAt != null && i < b.destroyedAt.Count ? b.destroyedAt[i] : Clock,
                                        b.rebuildMinutes != null && i < b.rebuildMinutes.Count ? b.rebuildMinutes[i] : 0f);
            }
        }

        public static void Clear()
        {
            bodies.Clear();
            Clock = 0;
        }
    }

    /// <summary>The ground forces destroyed on a body, by their key: each with when it was
    /// destroyed and how long it takes to be rebuilt (0: never). One whose time has come counts as
    /// there again.</summary>
    public class DestroyedSet
    {
        readonly Dictionary<long, (double at, float minutes)> units = new Dictionary<long, (double, float)>();

        public void Add(long key, float rebuildMinutes = 0f) => units[key] = (WorldState.Clock, Mathf.Max(0f, rebuildMinutes));

        public void Restore(long key, double at, float minutes) => units[key] = (at, minutes);

        /// <summary>Still destroyed (not yet rebuilt).</summary>
        public bool Contains(long key)
        {
            if (!units.TryGetValue(key, out var u)) return false;
            if (u.minutes <= 0f || WorldState.Clock < u.at + u.minutes * 60.0) return true;
            units.Remove(key);     // (rebuilt)
            return false;
        }

        /// <summary>When a destroyed one is rebuilt (game seconds; infinity if never; NaN if it isn't destroyed).</summary>
        public double RebuiltAt(long key) =>
            units.TryGetValue(key, out var u) ? (u.minutes <= 0f ? double.PositiveInfinity : u.at + u.minutes * 60.0) : double.NaN;

        public int Count => units.Count;

        public IEnumerable<(long key, double at, float minutes)> Entries()
        {
            foreach (var kv in units) yield return (kv.Key, kv.Value.at, kv.Value.minutes);
        }
    }
}

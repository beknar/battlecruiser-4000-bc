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
        /// <summary>Game seconds since the game began (saved: it goes on from where it was). It
        /// runs as fast as the industry's clock (IndustryLedger.SpeedUp), so a sped-up test speeds
        /// up both.</summary>
        public static double Clock { get; private set; }

        static readonly Dictionary<string, BodyMemory> bodies = new Dictionary<string, BodyMemory>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Clock = 0; bodies.Clear(); timers.Clear(); Strikes.Clear(); ScarsChanged = null; }

        public static void Advance(float dt) { if (dt > 0f) Clock += dt * IndustryLedger.SpeedUp; }

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
            /// <summary>Ground forces and settlement buildings destroyed, each till it is rebuilt.</summary>
            public readonly DestroyedSet destroyed = new DestroyedSet();
            /// <summary>Craters left by strikes from orbit (they stay).</summary>
            public readonly List<Scar> scars = new List<Scar>();
        }

        /// <summary>A crater a strike from orbit left on a body's ground: where (its surface's u, v),
        /// how wide and how deep (m), and when.</summary>
        [Serializable]
        public struct Scar
        {
            public double u, v;
            public float radius, depth;
            public double at;
        }

        /// <summary>A strike from orbit: when, where, by what, and what it did (for the HUD and the self-test).</summary>
        public struct Strike
        {
            public double at;
            public string body, place, weapon;
            public int units, buildings, casualties;
            public bool seen;
        }

        /// <summary>The strikes so far this game, the newest last.</summary>
        public static readonly List<Strike> Strikes = new List<Strike>();
        /// <summary>Told when a body's craters change (the ground built from them is built again).</summary>
        public static event Action<string> ScarsChanged;

        public static void AddScar(string body, Scar s)
        {
            Body(body).scars.Add(s);
            ScarsChanged?.Invoke(body);
        }

        /// <summary>When things in the world are due next (a strike satellite's next strike), by name.</summary>
        static readonly Dictionary<string, double> timers = new Dictionary<string, double>();
        public static bool TryTimer(string name, out double at) => timers.TryGetValue(name, out at);
        public static void SetTimer(string name, double at) => timers[name] = at;

        /// <summary>The key a settlement's building is remembered by in its body's DestroyedSet
        /// (apart from the ground forces' keys: the top bit is theirs never).</summary>
        public static long BuildingKey(string place, int index)
        {
            uint h = 2166136261;
            foreach (char c in place ?? "") { h ^= c; h *= 16777619; }
            return (1L << 62) | ((long)(h & 0xFFFFFF) << 16) | (uint)(index & 0xFFFF);
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
            public List<Scar> scars = new List<Scar>();
        }

        [Serializable]
        public class SavedTimer
        {
            public string name;
            public double at;
        }

        [Serializable]
        public class Saved
        {
            public double clock;
            public List<SavedBody> bodies = new List<SavedBody>();
            public List<SavedTimer> timers = new List<SavedTimer>();
        }

        public static Saved Snapshot()
        {
            var s = new Saved { clock = Clock };
            foreach (var kv in bodies)
            {
                var b = new SavedBody { name = kv.Key, explored = new List<int>(kv.Value.explored), scars = new List<Scar>(kv.Value.scars) };
                foreach (var (key, at, minutes) in kv.Value.destroyed.Entries())
                {
                    b.units.Add(key);
                    b.destroyedAt.Add(at);
                    b.rebuildMinutes.Add(minutes);
                }
                s.bodies.Add(b);
            }
            foreach (var kv in timers) s.timers.Add(new SavedTimer { name = kv.Key, at = kv.Value });
            return s;
        }

        public static void Restore(Saved s)
        {
            bodies.Clear();
            timers.Clear();
            Strikes.Clear();
            Clock = s != null ? Math.Max(0.0, s.clock) : 0.0;
            if (s?.timers != null) foreach (var t in s.timers) if (t != null && t.name != null) timers[t.name] = t.at;
            if (s?.bodies == null) return;
            foreach (var b in s.bodies)
            {
                if (b == null || b.name == null) continue;
                var m = Body(b.name);
                if (b.explored != null) foreach (int i in b.explored) m.explored.Add(i);
                if (b.scars != null) m.scars.AddRange(b.scars);
                if (b.units == null) continue;
                for (int i = 0; i < b.units.Count; i++)
                    m.destroyed.Restore(b.units[i], b.destroyedAt != null && i < b.destroyedAt.Count ? b.destroyedAt[i] : Clock,
                                        b.rebuildMinutes != null && i < b.rebuildMinutes.Count ? b.rebuildMinutes[i] : 0f);
            }
        }

        public static void Clear()
        {
            bodies.Clear();
            timers.Clear();
            Strikes.Clear();
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

        /// <summary>How many are destroyed now (those whose time has come are rebuilt).</summary>
        public int Count { get { Prune(); return units.Count; } }

        /// <summary>Those destroyed now (to save).</summary>
        public IEnumerable<(long key, double at, float minutes)> Entries()
        {
            Prune();
            foreach (var kv in units) yield return (kv.Key, kv.Value.at, kv.Value.minutes);
        }

        readonly List<long> rebuilt = new List<long>();
        void Prune()
        {
            rebuilt.Clear();
            foreach (var kv in units)
                if (kv.Value.minutes > 0f && WorldState.Clock >= kv.Value.at + kv.Value.minutes * 60.0) rebuilt.Add(kv.Key);
            foreach (var k in rebuilt) units.Remove(k);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The hostile factions at war with each other as well as with the players (off in the game:
    /// the three-way CIC reel turns it on). On, a faction's ship hunts any other faction's ships
    /// as it does the players' (SensorTactic and the tactics that pick their own targets), and a
    /// hostile ship's shot spares only its own faction (Projectile). What only works on a player's
    /// ship stays with the players: tractor locks, abduction, boarding.
    ///
    /// RevealAll: every ship seen by every other (Sensors): the sides have found each other, cloaks
    /// and jamming or not.
    /// </summary>
    public static class Rivalry
    {
        /// <summary>The factions fight each other too.</summary>
        public static bool On;

        /// <summary>Every ship seen by every other, within RevealRange.</summary>
        public static bool RevealAll;

        /// <summary>How far a ship is seen with RevealAll (m).</summary>
        public const float RevealRange = 60000f;

        /// <summary>The factions' ships refitted for a battle: what each faction's weapons do,
        /// times this (by faction name; none: as in the game). The three-way CIC reel's
        /// -cic3refit and -cic3refitgreys.</summary>
        public static readonly Dictionary<string, float> Refit = new Dictionary<string, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { On = false; RevealAll = false; Refit.Clear(); work.Clear(); letGo.Clear(); struck.Clear(); }

        /// <summary>A hit's damage times its faction's refit, when it is a faction ship's (Health.Source).</summary>
        public static float RefitOf(Transform source)
        {
            if (!On || Refit.Count == 0 || source == null) return 1f;
            var f = FactionShip.Of(source);
            return f != null && Refit.TryGetValue(f.FactionName, out float k) ? k : 1f;
        }

        // what each ship is on, since when and how strong it was then; and the ships it has let go
        // (no headway on them: a shield that comes back as fast as it is hit, a ship that slips its fire)
        struct Work { public Transform on; public float from, s0; }
        static readonly Dictionary<Transform, Work> work = new Dictionary<Transform, Work>();
        static readonly Dictionary<(Transform, Transform), float> letGo = new Dictionary<(Transform, Transform), float>();
        const float HeadwayTime = 45f, LetGoTime = 60f;

        // who has hit whom, and when last (ships, by their Health)
        static readonly Dictionary<(Transform, Transform), float> struck = new Dictionary<(Transform, Transform), float>();

        /// <summary>A hit on `target` from `source`'s ship (Health.Damage), noted with Rivalry on.</summary>
        public static void Struck(Transform target, Transform source)
        {
            if (!On || target == null || source == null) return;
            var sh = source.GetComponentInParent<Health>();
            if (sh != null && sh.transform != target) struck[(target, sh.transform)] = Time.time;
        }

        /// <summary>Has `by` hit `self` in the last `within` seconds?</summary>
        public static bool StruckBy(Transform self, Transform by, float within) =>
            self != null && by != null && struck.TryGetValue((self, by), out float at) && Time.time - at < within;

        /// <summary>Has `self` let `t` go for now (45 s on it, and it isn't 5 % weaker)?</summary>
        public static bool LetGo(Transform self, Transform t) =>
            self != null && t != null && letGo.TryGetValue((self, t), out float until) && Time.time < until;

        /// <summary>What `self` is on now: kept on a while, and let go for a minute when it makes no headway.</summary>
        static void Working(Transform self, Transform t)
        {
            var id = self;
            if (t == null) { work.Remove(id); return; }
            float s = StrengthLeft(t, out float whole) * whole;
            if (!work.TryGetValue(id, out var w) || w.on != t) { work[id] = new Work { on = t, from = Time.time, s0 = s }; return; }
            if (Time.time - w.from < HeadwayTime) return;
            if (s > w.s0 * 0.95f) letGo[(id, t)] = Time.time + LetGoTime;
            work[id] = new Work { on = t, from = Time.time, s0 = s };
        }

        /// <summary>Which side a ship is on: its faction's name for a hostile one, "players" for
        /// the rest.</summary>
        public static string SideOf(Transform t)
        {
            if (t == null) return "";
            var f = FactionShip.Of(t);
            if (f != null) return f.FactionName;
            var ti = t.GetComponentInParent<TargetInfo>();
            if (ti != null && ti.Hostile) return string.IsNullOrEmpty(ti.group) ? "hostile" : ti.group;
            return "players";
        }

        /// <summary>Are two ships enemies? (Off: a hostile and a player's ship.)</summary>
        public static bool Enemies(Transform a, Transform b)
        {
            if (a == null || b == null || a.root == b.root) return false;
            string sa = SideOf(a), sb = SideOf(b);
            if (sa == sb) return false;
            return On || sa == "players" || sb == "players";
        }

        /// <summary>Is it one of the players' ships (what tractor locks, abduction and marines are for)?</summary>
        public static bool IsPlayer(Transform t) => t != null && SideOf(t) == "players";

        /// <summary>What is left of a ship, hull and shield together, as a share of its whole (1:
        /// whole or not a ship), and how much that whole is.</summary>
        public static float StrengthLeft(Transform t, out float whole)
        {
            whole = 0f;
            if (t == null || !t.TryGetComponent(out Health h) || h.maxHealth <= 0f) return 1f;
            var sh = t.GetComponentInChildren<Shield>();
            float cap = sh != null ? sh.capacity : 0f;
            whole = h.maxHealth + cap;
            float now = h.Current + (sh != null && sh.Up ? sh.Fraction * cap : 0f);
            return whole > 0f ? Mathf.Clamp01(now / whole) : 1f;
        }

        static readonly List<Transform> buffer = new List<Transform>();

        static readonly List<Transform> dropped = new List<Transform>();

        /// <summary>The ships a faction's ship `self` may go for: the players' ships here, and (On)
        /// every other faction's living ships, less (On) the ones it has let go, unless that
        /// leaves none. The list is reused: don't keep it.</summary>
        public static List<Transform> TargetsFor(Transform self)
        {
            buffer.Clear();
            dropped.Clear();
            bool sort = On && self != null;
            if (sort && self.TryGetComponent(out CombatPilot cp)) Working(self, cp.target);
            foreach (var p in PlayerShips.All)
                if (p != null && p.gameObject.activeInHierarchy && PlayerShips.Present(p)) (sort && LetGo(self, p) ? dropped : buffer).Add(p);
            if (!sort) return buffer;
            string mine = SideOf(self);
            foreach (var s in FactionShip.AllShips)
            {
                if (s == null || !s.isActiveAndEnabled || s.transform.root == self.root || s.FactionName == mine) continue;
                if (s.TryGetComponent(out Health h) && (h.Dead || h.Captured)) continue;
                (LetGo(self, s.transform) ? dropped : buffer).Add(s.transform);
            }
            if (buffer.Count == 0) buffer.AddRange(dropped);
            return buffer;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// How hard a ship is to see on sensors (its signature): 1 is a ship like any other, seen to
    /// the full range of a sensor; less is harder (a cloaked Grey scout: a tenth). Firing, a
    /// missile launched or a hit taken lights it up for a while, to at least 1. Ships without one
    /// (the pirates, the Vrask, installations) are seen as they always were (Sensors).
    /// </summary>
    public class Signature : MonoBehaviour
    {
        [Tooltip("How much of a sensor's range it is seen at, quiet (1: like any ship).")]
        public float value = 1f;
        [Tooltip("For this long (s) after it fires or is hit, it is seen at least as well as any ship.")]
        public float lightUp = 8f;

        /// <summary>Kept low by something that hides it (a cloak: Cloak), on top of value.</summary>
        public float hiddenScale = 1f;

        /// <summary>When it last fired, or was hit (Time.time).</summary>
        public float FiredAt { get; private set; } = -1000f;
        public float HitAt { get; private set; } = -1000f;

        Health health;

        void Awake() => health = GetComponent<Health>();

        void OnEnable() => Sensors.Add(this);
        void OnDisable() => Sensors.Remove(this);

        /// <summary>It fired (a shot, a missile, a beam): seen for a while.</summary>
        public void Fired() => FiredAt = Time.time;

        /// <summary>Lit up: firing or hit within lightUp seconds.</summary>
        public bool Lit
        {
            get
            {
                float hit = health != null ? health.LastHitTime : HitAt;
                return Time.time - Mathf.Max(FiredAt, Mathf.Max(hit, HitAt)) < lightUp;
            }
        }

        /// <summary>How much of a sensor's range it is seen at now.</summary>
        public float Current => Lit ? Mathf.Max(1f, value) : value * hiddenScale;

        /// <summary>A hit taken (for one without Health: the tests' fixtures).</summary>
        public void Hit() => HitAt = Time.time;
    }

    /// <summary>
    /// What the players' sensors see. A contact is seen within a sensor's range times its
    /// signature (Signature), and identified (named, its class known) within half that; beyond
    /// that it is an unknown contact. A ship without a Signature is seen and named as it always
    /// was: inside the HUD's range (TargetDisplay.MarkerRange), unless it lies hidden in ambush
    /// (CombatPilot.IsHidden), so the pirates and the Vrask are unchanged.
    ///
    /// Every place the players' side finds enemies goes through here (the HUD's markers, the
    /// target list and cycling, the mouse pick, the console's count, the fleet, the wingman, the
    /// CIWS, the co-op bot, the battle simulator, the warp's "hostiles nearby" and the combat
    /// music), so an unseen ship gives nothing away. What the enemies see of the players is
    /// CanSee, used only by the factions that hunt by their sensors (the Greys, the Empire): the
    /// older enemies know where every player is, as before.
    ///
    /// Worked out at most ten times a second for each contact, not once for each caller.
    /// </summary>
    public static class Sensors
    {
        /// <summary>How far (m) a player's ship sees a ship of signature 1.</summary>
        public const float PlayerRange = 30000f;
        /// <summary>How far (m) an enemy sees a player's ship, by default.</summary>
        public const float EnemyRange = 30000f;
        /// <summary>The share of the seen range inside which a contact is identified.</summary>
        public const float IdentifyShare = 0.5f;
        /// <summary>How long (s) what is seen of a contact is kept before it is worked out again.</summary>
        public const float Refresh = 0.1f;

        // (by the transform the Signature is on: a contact is that or a child of it)
        static readonly Dictionary<Transform, Signature> byShip = new Dictionary<Transform, Signature>();
        static readonly Dictionary<Transform, Seen> cache = new Dictionary<Transform, Seen>();

        struct Seen { public float at; public bool detected, identified; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            byShip.Clear();
            cache.Clear();
            seenThrough.Clear();
            GhostsSeenThrough = 0;
            RangeOf = null;
            // (once each: off first, as the statics outlive a play session in the editor)
            Projectile.Spawned -= OnShot;
            Missile.Spawned -= OnMissile;
            MainBeam.Began -= OnBeam;
            ArtilleryShell.Spawned -= OnShell;
            Projectile.Spawned += OnShot;
            Missile.Spawned += OnMissile;
            MainBeam.Began += OnBeam;
            ArtilleryShell.Spawned += OnShell;
        }

        static void OnShot(Projectile p) => FiredBy(p != null ? p.owner : null);
        static void OnMissile(Missile m) => FiredBy(m != null ? m.owner : null);
        static void OnBeam(MainBeam b) => FiredBy(b != null ? b.transform : null);
        static void OnShell(ArtilleryShell a) => FiredBy(a != null ? a.owner : null);

        internal static void Add(Signature s)
        {
            if (s == null) return;
            byShip[s.transform] = s;
            cache.Clear();
        }

        internal static void Remove(Signature s)
        {
            if (s == null) return;
            if (byShip.TryGetValue(s.transform, out var had) && had == s) byShip.Remove(s.transform);
            cache.Clear();
        }

        static void FiredBy(Transform owner)
        {
            var s = Of(owner);
            if (s != null) s.Fired();
        }

        /// <summary>The signature of a ship, or of the ship a part belongs to (null: none, seen
        /// as it always was).</summary>
        public static Signature Of(Transform t)
        {
            if (byShip.Count == 0) return null;
            for (var x = t; x != null; x = x.parent)
                if (byShip.TryGetValue(x, out var s) && s != null) return s;
            return null;
        }

        /// <summary>A player's sensor range (m), for a seen signature of 1: the frigate's (later:
        /// its ECCM), else PlayerRange. Set by what changes it.</summary>
        public static System.Func<Transform, float> RangeOf;

        static float RangeFrom(Transform observer)
        {
            float r = RangeOf != null && observer != null ? RangeOf(observer) : 0f;
            // (jammed: the sensors see that much less far)
            return (r > 0f ? r : PlayerRange) * (1f - Jamming.On(observer));
        }

        /// <summary>Is the contact `t` seen by any of the players' ships (or, with `from`, by a
        /// ship there)?</summary>
        public static bool Detected(Transform t) => Look(t).detected;

        /// <summary>Is it named and its class known (seen, and close enough), or only an unknown contact?</summary>
        public static bool Identified(Transform t) => Look(t).identified;

        /// <summary>A ship hard to see (with a Signature) that none of the players' ships sees:
        /// false for every ship without one, so the older enemies are found as before.</summary>
        public static bool Unseen(Transform t) => t != null && Of(t) != null && !Detected(t);

        /// <summary>Hidden from the players: lying in ambush, or out of their sensors' reach.</summary>
        public static bool Hidden(Transform t) => t == null || CombatPilot.IsHidden(t) || !Detected(t);

        /// <summary>Seen from a point (the HUD's own ship): for a ship without a signature, inside
        /// the HUD's range as before; for one with, inside a player's range times its signature
        /// from there.</summary>
        public static bool DetectedFrom(Transform t, Vector3 from, Transform observer = null)
        {
            if (t == null || CombatPilot.IsHidden(t)) return false;
            var s = Of(t);
            if ((s != null || Jamming.Any) && observer == null) observer = PlayerShips.Primary;
            float range = s == null ? (Jamming.Any ? RangeFrom(observer) : PlayerRange) : RangeFrom(observer) * Against(s, observer);
            return (t.position - from).sqrMagnitude <= range * range;
        }

        static Seen Look(Transform t)
        {
            if (t == null) return default;
            var s = Of(t);
            // (no signature: seen as always, by the ambush rule alone, its range the caller's to check;
            // named at any range, unless jamming cuts what the sensors can make out)
            if (s == null && !Jamming.Any) return new Seen { detected = !CombatPilot.IsHidden(t), identified = true };
            if (s == null) return Plain(t);
            float now = Time.time;
            if (cache.TryGetValue(t, out var c) && now - c.at < Refresh) return c;
            c = new Seen { at = now };
            if (!CombatPilot.IsHidden(t))
            {
                foreach (var p in PlayerShips.All)
                {
                    // (a ship that has warped off, landed, or is destroyed and waiting sees nothing)
                    if (p == null || !p.gameObject.activeInHierarchy || !PlayerShips.Present(p)) continue;
                    if (p.TryGetComponent(out Health ph) && ph.Dead) continue;
                    float range = RangeFrom(p) * Against(s, p);
                    float d2 = (t.position - p.position).sqrMagnitude;
                    if (d2 > range * range) continue;
                    c.detected = true;
                    float id = range * IdentifyShare;
                    if (d2 <= id * id) { c.identified = true; break; }
                }
                // (lit up, firing or hit: named at once, as its guns give it away)
                if (c.detected && s.Lit) c.identified = true;
            }
            if (cache.Count > 512) Prune();
            cache[t] = c;
            return c;
        }

        /// <summary>The signature a contact shows an observer: its own, and if it is hidden (a
        /// cloak) and quiet, up to twice that against a ship with good ECCM.</summary>
        static float Against(Signature s, Transform observer)
        {
            float sig = s.Current;
            if (s.hiddenScale < 1f && !s.Lit && observer != null) sig *= 1f + FrigateClass.EccmOf(observer);
            return sig;
        }

        /// <summary>A ship without a signature while something jams: seen as always, named only
        /// within half of what a player's (jammed) sensors see, from any of them unjammed.</summary>
        static Seen Plain(Transform t)
        {
            float now = Time.time;
            if (cache.TryGetValue(t, out var c) && now - c.at < Refresh) return c;
            c = new Seen { at = now, detected = !CombatPilot.IsHidden(t) };
            foreach (var p in PlayerShips.All)
            {
                if (p == null || !p.gameObject.activeInHierarchy || !PlayerShips.Present(p)) continue;
                float id = RangeFrom(p) * IdentifyShare;
                if (Jamming.On(p) <= 0f || (t.position - p.position).sqrMagnitude <= id * id) { c.identified = true; break; }
            }
            if (cache.Count > 512) Prune();
            cache[t] = c;
            return c;
        }

        // ------------------------------------------------------------------ ghosts and masking

        /// <summary>A ghost contact (EcmEmitter): a false return on the players' sensors, an
        /// unknown contact that moves like a ship, gone once looked at closely.</summary>
        public struct Ghost { public int id; public Vector3 at; }

        static readonly List<(int id, Vector3 at)> ghostBuf = new List<(int, Vector3)>();
        static readonly Dictionary<int, float> seenThrough = new Dictionary<int, float>();
        /// <summary>How near (m) a ghost has to be to be seen through: a close look.</summary>
        public const float GhostLook = 1500f;
        /// <summary>How long (s) a ghost seen through stays gone.</summary>
        public const float GhostGone = 90f;
        /// <summary>Ghosts seen through so far (the tests, the HUD's notice).</summary>
        public static int GhostsSeenThrough { get; private set; }

        /// <summary>The ghost contacts the players' sensors show now (each jammer's, less those seen
        /// through: one a player's ship comes within GhostLook of is seen through, and goes).</summary>
        public static void Ghosts(List<Ghost> into)
        {
            into.Clear();
            if (!Jamming.Any) return;
            ghostBuf.Clear();
            foreach (var e in EcmEmitter.All) if (e != null) e.GhostPlaces(ghostBuf);
            float now = Time.time;
            foreach (var (id, at) in ghostBuf)
            {
                if (seenThrough.TryGetValue(id, out float until) && now < until) continue;
                bool shown = false, through = false;
                foreach (var p in PlayerShips.All)
                {
                    if (p == null || !p.gameObject.activeInHierarchy || !PlayerShips.Present(p)) continue;
                    float range = RangeFrom(p), d2 = (at - p.position).sqrMagnitude, idr = GhostLook;
                    // (only jammed sensors are fooled; looked at closely, it is seen through)
                    if (Jamming.On(p) <= 0f) continue;
                    if (d2 <= idr * idr) { through = true; break; }
                    if (d2 <= range * range) shown = true;
                }
                if (through)
                {
                    seenThrough[id] = now + GhostGone;
                    GhostsSeenThrough++;
                    Notices.Post("Ghost contact: a false return, gone");
                    continue;
                }
                if (shown) into.Add(new Ghost { id = id, at = at });
            }
        }

        /// <summary>Where the players' sensors show a ship: a jammer's escort not yet named at a
        /// false place near its own (masking); any other where it is.</summary>
        public static Vector3 ShownAt(Transform t)
        {
            if (t == null) return Vector3.zero;
            if (!Jamming.Any || Identified(t)) return t.position;
            var e = Jamming.Strongest(t.position);
            return e != null && e.transform.root != t.root ? e.Masked(t) : t.position;
        }

        /// <summary>Can an enemy `observer` see the player's ship `target`: within its sensor
        /// range (its FactionShip's, else EnemyRange) times the target's signature (1 if it has
        /// none)?</summary>
        public static bool CanSee(Transform observer, Transform target)
        {
            if (observer == null || target == null) return false;
            var f = FactionShip.Of(observer);
            float range = f != null && f.sensorRange > 0f ? f.sensorRange : EnemyRange;
            var s = Of(target);
            float sig = s != null ? s.Current : 1f;
            return (target.position - observer.position).sqrMagnitude <= range * sig * range * sig;
        }

        /// <summary>A contact's name as the players' side may show it: its own, or "Unknown contact".</summary>
        public static string NameOf(Transform t, string name) => Identified(t) ? name : UnknownContact;

        public const string UnknownContact = "Unknown contact";

        static readonly List<Transform> gone = new List<Transform>();
        static void Prune()
        {
            gone.Clear();
            foreach (var k in cache.Keys) if (k == null) gone.Add(k);
            foreach (var k in gone) cache.Remove(k);
            if (cache.Count > 512) cache.Clear();
        }

        /// <summary>Forget what was seen (the tests: worked out again at once).</summary>
        public static void Forget() => cache.Clear();
    }
}

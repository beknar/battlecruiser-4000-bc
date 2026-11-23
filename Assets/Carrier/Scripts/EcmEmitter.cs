using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A jammer (the Grey Shroud's ECM): a field `radius` m across its ship, `strength` at its
    /// heart and falling off to nothing at its edge, that inside it, for a player's ship (less its
    /// ECCM: Jamming):
    /// - cuts the ship's sensors (Sensors: its range down by the jamming, contacts unknown sooner);
    /// - throws its turrets' aim off (CarrierTurret) and makes its missiles lose their lock
    ///   (Missile), taking it again once out of the jamming;
    /// - garbles its comms: orders to its fighters and fleet may not get through;
    /// - puts ghost contacts on its sensors (Ghosts: false returns, unknown contacts that move like
    ///   ships, that vanish when looked at closely) and shows the jammer's escorts at a false place
    ///   till they are identified (masking).
    /// Close in (`pdRadius`), its emitters scramble the players' missiles (they go off short) and
    /// stun their drones.
    ///
    /// Kept on in a co-op guest's game, its on/off as the host says (CoopEntity.ec), so each game
    /// works out the jamming on its own player and makes the same ghosts (from the ship's place in
    /// the level, on the orbits' clock, which is the room's). Each game scrambles only its own
    /// player's missiles and craft.
    /// </summary>
    public class EcmEmitter : MonoBehaviour
    {
        public float radius = 6000f;
        [Range(0f, 1f)] public float strength = 0.6f;
        [Tooltip("Inside this (m), the players' missiles are scrambled and their craft stunned.")]
        public float pdRadius = 1500f;
        [Tooltip("How long (s) a stunned craft drifts.")]
        public float stunTime = 8f;
        [Tooltip("Ghost contacts it puts on the players' sensors.")]
        public int ghosts = 3;
        [Tooltip("How far out (m) its ghosts go round it, nearest and farthest.")]
        public Vector2 ghostRange = new Vector2(2500f, 5500f);
        [Tooltip("How far (m) from its true place an escort is shown, till identified.")]
        public float maskOffset = 900f;
        [Tooltip("On: jamming (its tactics turn it on and off).")]
        public bool active = true;

        public static readonly List<EcmEmitter> All = new List<EcmEmitter>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAll() => All.Clear();

        Health health;
        int seed;

        /// <summary>Players' missiles it has scrambled, and craft it has stunned (the tests).</summary>
        public int Scrambled { get; private set; }
        public int Stunned { get; private set; }

        /// <summary>Jamming now: on, and its ship alive.</summary>
        public bool On => active && isActiveAndEnabled && (health == null || !health.Dead);

        void Awake() => health = GetComponent<Health>();

        bool seeded;
        /// <summary>Its ghosts' and masks' seed: the same on every machine, and another jammer's
        /// not (from its place in the level), worked out once.</summary>
        int Seed
        {
            get
            {
                if (seeded) return seed;
                seeded = true;
                var parts = new System.Text.StringBuilder();
                for (var x = transform; x != null; x = x.parent) parts.Insert(0, "/" + x.name + "#" + x.GetSiblingIndex());
                return seed = Stable(parts.ToString());
            }
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        /// <summary>Co-op: the host's game says whether it is jamming.</summary>
        public void NetSet(bool on) => active = on;

        void Update()
        {
            if (!On) return;
            float pd2 = pdRadius * pdRadius;
            Vector3 at = transform.position;
            // the players' missiles close in: scrambled (they go off short)
            // (only this game's own: another player's shown missile is that game's to scramble,
            // and to say so)
            for (int i = Missile.Active.Count - 1; i >= 0; i--)
            {
                var m = Missile.Active[i];
                if (m == null || m.Hostile || m.Spent || Jamming.Remote(m.owner) || (m.transform.position - at).sqrMagnitude > pd2) continue;
                m.ShootDown(transform);
                Scrambled++;
            }
            // their drones close in: stunned (Craft.Stunnable: drones only)
            foreach (var s in PlayerShips.All)
            {
                var cc = s != null ? s.GetComponent<CarrierController>() : null;
                if (cc == null) continue;
                if (Jamming.Remote(s)) continue;
                foreach (var c in cc.Wing)
                {
                    if (c == null || !c.isActiveAndEnabled || !c.Stunnable || (c.transform.position - at).sqrMagnitude > pd2) continue;
                    c.Stun(stunTime);
                    Stunned++;
                }
            }
        }

        /// <summary>The jamming it puts on a point, before any ECCM: its strength, falling off to 0 at its edge.</summary>
        public float At(Vector3 point)
        {
            if (!On) return 0f;
            float d2 = (point - transform.position).sqrMagnitude, r2 = radius * radius;
            if (d2 >= r2) return 0f;
            return strength * (1f - d2 / r2);
        }

        /// <summary>Its ghost contacts' places now (each going slowly round it, the same on every machine).</summary>
        public void GhostPlaces(List<(int id, Vector3 at)> into)
        {
            if (!On) return;
            var rng = new System.Random(Seed);
            for (int i = 0; i < ghosts; i++)
            {
                float r = Mathf.Lerp(ghostRange.x, ghostRange.y, (float)rng.NextDouble());
                float a0 = (float)rng.NextDouble() * 360f, h = ((float)rng.NextDouble() - 0.5f) * 1200f;
                float speed = 2f + (float)rng.NextDouble() * 3f;      // degrees a second
                // (on the orbits' clock: in co-op the room's, so every game has them in the same place)
                float clock = OrbitalSystem.Instance != null ? (float)(OrbitalSystem.Instance.Time % 36000.0) : Time.time;
                float a = (a0 + clock * speed * (i % 2 == 0 ? 1f : -1f)) * Mathf.Deg2Rad;
                into.Add((Seed * 31 + i, transform.position + new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r)));
            }
        }

        /// <summary>Where an escort of its (within its field) is shown till identified: a false place near the true one.</summary>
        public Vector3 Masked(Transform t)
        {
            int h = Stable(t.name) ^ Seed;
            var rng = new System.Random(h);
            var dir = new Vector3((float)rng.NextDouble() - 0.5f, ((float)rng.NextDouble() - 0.5f) * 0.4f, (float)rng.NextDouble() - 0.5f);
            return t.position + (dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.right) * maskOffset;
        }

        static int Stable(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s ?? "") h = h * 31 + c;
                return h;
            }
        }
    }

    /// <summary>
    /// The jamming on a ship (EcmEmitter): the strongest jammer's at its place, cut by its ECCM
    /// (a ship of ECCM 1 keeps 70 % of what a ship with none loses), at most 0.9.
    /// </summary>
    public static class Jamming
    {
        /// <summary>A share of the jamming that ECCM 1 cancels.</summary>
        public const float EccmCut = 0.7f;
        public const float Most = 0.9f;

        /// <summary>Another player's ship (or a missile or craft of theirs) shown in co-op: its
        /// own game does the jamming on it.</summary>
        public static bool Remote(Transform t) => t != null && t.GetComponentInParent<Demo.RemotePlayer>() != null;

        /// <summary>Any jammer on (a quick test before the work).</summary>
        public static bool Any
        {
            get
            {
                foreach (var e in EcmEmitter.All) if (e != null && e.On) return true;
                return false;
            }
        }

        /// <summary>The raw jamming at a point (no ECCM).</summary>
        public static float At(Vector3 point)
        {
            float j = 0f;
            foreach (var e in EcmEmitter.All) if (e != null) j = Mathf.Max(j, e.At(point));
            return Mathf.Min(Most, j);
        }

        /// <summary>The jamming on a ship, less its ECCM (0: none).</summary>
        public static float On(Transform ship)
        {
            if (ship == null || EcmEmitter.All.Count == 0) return 0f;
            float j = At(ship.position);
            return j <= 0f ? 0f : Mathf.Min(Most, j * (1f - EccmCut * FrigateClass.EccmOf(ship)));
        }

        /// <summary>The jammer whose field holds a point, if any (the strongest).</summary>
        public static EcmEmitter Strongest(Vector3 point)
        {
            EcmEmitter best = null;
            float bj = 0f;
            foreach (var e in EcmEmitter.All)
            {
                if (e == null) continue;
                float j = e.At(point);
                if (j > bj) { bj = j; best = e; }
            }
            return best;
        }

        /// <summary>An order from a jammed ship to its craft or fleet: lost (a notice says so) with
        /// a chance of half the jamming on it. True: lost.</summary>
        public static bool OrderLost(Transform from)
        {
            float j = On(from);
            if (j <= 0.05f || Random.value >= j * 0.5f) return false;
            SoundFX.Ui(Sfx.UiError);
            Notices.Post("Comms jammed: the order didn't get through");
            OrdersLost++;
            return true;
        }

        /// <summary>Orders lost to jamming (the tests).</summary>
        public static int OrdersLost { get; private set; }

        /// <summary>A message as it comes through jamming `j`: letters lost to static.</summary>
        public static string Garble(string text, float j, int seed)
        {
            if (string.IsNullOrEmpty(text) || j <= 0.05f) return text;
            var rng = new System.Random(seed);
            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                // (both drawn for every character, so a little more jamming loses more letters
                // without the rest changing: the line doesn't flicker as the ship moves)
                double lose = rng.NextDouble(), which = rng.NextDouble();
                if (char.IsLetterOrDigit(chars[i]) && lose < j * 0.6) chars[i] = which < 0.5 ? '#' : '~';
            }
            return new string(chars);
        }
    }
}

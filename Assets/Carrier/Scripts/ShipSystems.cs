using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>A part of a ship that can be aimed at and knocked out (ShipSystems). Hull is none
    /// in particular: the ship as a whole. A new one only ever goes on the end (the co-op wire
    /// numbers them).</summary>
    public enum ShipSystem { Hull, Weapons, Engines, Shields }

    /// <summary>
    /// A ship's components: its weapons, its engines and its shield generator, each in a condition
    /// from 1 (working) down to 0.
    ///
    /// - Hits: a component takes damage only from what gets through the shield, as hard as the
    ///   kind of weapon is on components (DamageTable.Component). Ion is the exception: 40% of
    ///   what the shield stops still reaches the engines or the shield generator. With the
    ///   attacker aiming at a component (SetAim), 70% of that goes to it; otherwise 25% goes to
    ///   one at random. The hull takes the hit as it would anyway: this is on top.
    /// - Knocked out below 25%, and out until repaired above 50%: the weapons don't fire, the
    ///   engines give 15% of top speed and 30% of acceleration, and the shield drops and doesn't
    ///   recharge.
    /// - Repair: 1% a second each after 20 s without a hit, and whole at once in a repair
    ///   (Health.Repair: docked) or a respawn.
    ///
    /// Each component can take `toughness` of the hull's full strength. Its condition isn't
    /// saved: a ship comes into a game whole.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShipSystems : MonoBehaviour
    {
        [Tooltip("How much damage each component takes to wreck, as a share of the hull's full strength.")]
        public float toughness = 0.12f;
        [Tooltip("Repairs start after this many seconds without a hit.")]
        public float quietTime = 20f;
        [Tooltip("Repair per second, per component, once it is quiet (1 = whole).")]
        public float repairRate = 0.01f;

        public const float DownBelow = 0.25f, BackAbove = 0.5f;
        public const float AimedShare = 0.7f, StrayShare = 0.25f, IonThroughShield = 0.4f;
        public const float EngineSpeed = 0.15f, EngineAcceleration = 0.3f;

        /// <summary>The components (every ShipSystem but Hull), in order.</summary>
        public static readonly ShipSystem[] Components = MakeComponents();
        static readonly int Slots = System.Enum.GetValues(typeof(ShipSystem)).Length;

        static ShipSystem[] MakeComponents()
        {
            var list = new List<ShipSystem>();
            foreach (ShipSystem s in System.Enum.GetValues(typeof(ShipSystem))) if (s != ShipSystem.Hull) list.Add(s);
            return list.ToArray();
        }

        readonly float[] condition = Whole();
        readonly bool[] down = new bool[Slots];
        Health health;
        CarrierController ship;
        float netUntil = -1f;

        static float[] Whole()
        {
            var c = new float[Slots];
            for (int i = 0; i < c.Length; i++) c[i] = 1f;
            return c;
        }

        /// <summary>When a weapon last hit it.</summary>
        public float LastHitTime { get; private set; } = -100f;
        /// <summary>Times one of its components has been knocked out here (not co-op's copies).</summary>
        public int KnockedOut { get; private set; }

        public float Condition(ShipSystem s) => s == ShipSystem.Hull ? 1f : condition[(int)s];
        public bool IsDown(ShipSystem s) => s != ShipSystem.Hull && down[(int)s];
        public bool WeaponsDown => down[(int)ShipSystem.Weapons];
        public bool EnginesDown => down[(int)ShipSystem.Engines];
        /// <summary>Its top speed and acceleration, as shares of what they would be (the engines).</summary>
        public float SpeedScale => EnginesDown ? EngineSpeed : 1f;
        public float AccelerationScale => EnginesDown ? EngineAcceleration : 1f;

        /// <summary>The ship's components, fitted if it hasn't any yet (ships fit their own:
        /// CarrierController, CombatPilot, co-op's RemotePlayer).</summary>
        public static ShipSystems Fit(GameObject go)
        {
            looked.Clear();   // (parts looked at before it was fitted look again)
            if (go == null) return null;
            return go.TryGetComponent(out ShipSystems s) ? s : go.AddComponent<ShipSystems>();
        }

        void Awake()
        {
            health = GetComponent<Health>();
            // (its hits come here from now on, if it has been hit already)
            if (health != null) health.Relook();
        }

        // ------------------------------------------------------------------ hits

        /// <summary>A weapon's hit (Health.Damage): `through` got through the shield and
        /// `stopped` the shield took (both as the hit's own damage, before the hull's share);
        /// `source` fired it.</summary>
        public void Hit(float through, float stopped, HitKind kind, Transform source)
        {
            LastHitTime = Time.time;
            float k = DamageTable.Component(kind);
            var aim = AimOf(source != null ? source.root : null);
            if (through > 0f) Take(through * k, aim, false);
            if (kind == HitKind.Ion && stopped > 0f) Take(stopped * k * IonThroughShield, aim, true);
        }

        /// <summary>Component damage from a hit, to the one aimed at or one at random (through a
        /// shield, only the engines or the shield generator).</summary>
        void Take(float damage, ShipSystem aim, bool throughShield)
        {
            if (damage <= 0f) return;
            if (throughShield && aim != ShipSystem.Engines && aim != ShipSystem.Shields) aim = ShipSystem.Hull;
            if (aim != ShipSystem.Hull) { Damage(aim, damage * AimedShare); return; }
            var any = throughShield ? (Random.value < 0.5f ? ShipSystem.Engines : ShipSystem.Shields)
                                    : Components[Random.Range(0, Components.Length)];
            Damage(any, damage * StrayShare);
        }

        /// <summary>Damage straight to one component (in hull points: it can take `toughness` of
        /// the hull's full strength).</summary>
        public void Damage(ShipSystem s, float amount)
        {
            if (s == ShipSystem.Hull || amount <= 0f || (health != null && health.Dead)) return;
            float full = Mathf.Max(1f, toughness * (health != null ? health.maxHealth : 1000f));
            SetCondition(s, condition[(int)s] - amount / full);
        }

        /// <summary>A component's condition (0 to 1): knocked out below 25%, and working again
        /// only above 50%.</summary>
        public void SetCondition(ShipSystem s, float value) => Set(s, value, true);

        void Set(ShipSystem s, float value, bool tell)
        {
            if (s == ShipSystem.Hull) return;
            int i = (int)s;
            condition[i] = Mathf.Clamp01(value);
            bool was = down[i];
            if (!was && condition[i] < DownBelow) down[i] = true;
            else if (was && condition[i] > BackAbove) down[i] = false;
            if (down[i] == was) return;
            if (down[i] && tell) KnockedOut++;
            Apply(s, tell);
        }

        /// <summary>Every component whole and working (a repair, a respawn).</summary>
        public void RepairAll()
        {
            foreach (var s in Components) Set(s, 1f, false);
        }

        void Update()
        {
            // (co-op's copy of another game's ship: that game repairs it)
            if (Time.time < netUntil || (health != null && health.Dead)) return;
            float lastHit = Mathf.Max(LastHitTime, health != null ? health.LastHitTime : -100f);
            if (Time.time - lastHit < quietTime) return;
            float step = repairRate * Time.deltaTime;
            foreach (var s in Components) if (condition[(int)s] < 1f) Set(s, condition[(int)s] + step, true);
        }

        /// <summary>What a component knocked out (or working again) does to the ship.</summary>
        void Apply(ShipSystem s, bool tell)
        {
            bool off = down[(int)s];
            switch (s)
            {
                case ShipSystem.Engines:
                    if (ship == null) ship = GetComponent<CarrierController>();
                    if (ship != null)
                    {
                        ship.engineScale = off ? EngineSpeed : 1f;
                        ship.engineAccelScale = off ? EngineAcceleration : 1f;
                    }
                    break;
                case ShipSystem.Shields:
                    foreach (var sh in GetComponentsInChildren<Shield>(true)) if (sh != null) sh.SetDisabled(off);
                    break;
                case ShipSystem.Weapons:
                    // (a beam charging or firing stops; the guns and launchers ask before each shot)
                    if (off) foreach (var b in GetComponentsInChildren<MainBeam>(true)) if (b != null) b.Abort();
                    break;
            }
            if (tell && transform == PlayerShips.Primary)
                Notices.Post(off ? $"Our {Label(s)} {Are(s)} knocked out!" : $"Our {Label(s)} {Are(s)} working again");
        }

        /// <summary>Is the ship a part (a gun, a launcher) is on without its weapons? `cache` keeps
        /// the ship's components once found.</summary>
        public static bool WeaponsDownOn(Component part, ref ShipSystems cache)
        {
            if (part == null) return false;
            if (cache == null)
            {
                // (looked for once per part, found or not: a turret on a station has none)
                if (!looked.TryGetValue(part, out cache)) looked[part] = cache = part.GetComponentInParent<ShipSystems>(true);
            }
            return cache != null && cache.WeaponsDown;
        }

        static readonly System.Collections.Generic.Dictionary<Component, ShipSystems> looked = new System.Collections.Generic.Dictionary<Component, ShipSystems>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetLooked() => looked.Clear();

        public static bool WeaponsDownOn(Component part)
        {
            ShipSystems none = null;
            return WeaponsDownOn(part, ref none);
        }

        // ------------------------------------------------------------------ aiming

        static readonly Dictionary<Transform, ShipSystem> aims = new Dictionary<Transform, ShipSystem>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ClearAims() => aims.Clear();

        /// <summary>What a ship (by its root) is aiming its fire at: Hull for nothing in particular.</summary>
        public static ShipSystem AimOf(Transform shipRoot) =>
            shipRoot != null && aims.TryGetValue(shipRoot, out var s) ? s : ShipSystem.Hull;

        /// <summary>Aim a ship's (by its root) fire at a component of whatever it hits (Hull: none
        /// in particular). The player's choice (J) and an AI's.</summary>
        public static void SetAim(Transform shipRoot, ShipSystem s)
        {
            if (shipRoot == null) return;
            if (s == ShipSystem.Hull) aims.Remove(shipRoot);
            else aims[shipRoot] = s;
        }

        /// <summary>The next to aim at: the hull, then each component, and round again.</summary>
        public static ShipSystem Next(ShipSystem s) => (ShipSystem)(((int)s + 1) % Slots);

        public static string Label(ShipSystem s)
        {
            switch (s)
            {
                case ShipSystem.Hull: return "hull";
                case ShipSystem.Weapons: return "weapons";
                case ShipSystem.Engines: return "engines";
                case ShipSystem.Shields: return "shield generator";
                default: return s.ToString().ToLowerInvariant();
            }
        }

        static string Are(ShipSystem s) => s == ShipSystem.Shields ? "is" : "are";

        // ------------------------------------------------------------------ co-op

        /// <summary>Six bits a component, in order: how worn it is in five (0 whole, 31 wrecked:
        /// so one an older game doesn't know reads as whole) and whether it is knocked out.</summary>
        const int Bits = 6;

        /// <summary>Every component's condition, packed (see Bits).</summary>
        public int Pack()
        {
            int v = 0;
            for (int n = 0; n < Components.Length && (n + 1) * Bits <= 30; n++)
            {
                int i = (int)Components[n];
                int worn = Mathf.Clamp(Mathf.RoundToInt((1f - condition[i]) * 31f), 0, 31);
                v |= (worn | (down[i] ? 32 : 0)) << (n * Bits);
            }
            return v;
        }

        /// <summary>For the co-op wire: 1 + Pack(), so 0 (left out, by an older game) means whole.</summary>
        public int ToWire() => 1 + Pack();
        public static int ToWire(ShipSystems s) => s != null ? s.ToWire() : 0;

        /// <summary>Co-op: the component a hit was aimed at (a hit's `cp`; 0, or one this game
        /// doesn't know: the hull).</summary>
        public static ShipSystem FromWire(int cp) => cp > 0 && cp < Slots ? (ShipSystem)cp : ShipSystem.Hull;

        /// <summary>Co-op: the components as the game that owns the ship has them (ToWire; 0: whole).</summary>
        public void NetSet(int wire)
        {
            netUntil = Time.time + 3f;
            int v = wire > 0 ? wire - 1 : 0;
            for (int n = 0; n < Components.Length; n++)
            {
                var s = Components[n];
                int i = (int)s;
                int g = (n + 1) * Bits <= 30 ? (v >> (n * Bits)) & 63 : 0;
                condition[i] = 1f - (g & 31) / 31f;
                bool off = (g & 32) != 0;
                if (down[i] == off) continue;
                down[i] = off;
                Apply(s, false);
            }
        }
    }
}

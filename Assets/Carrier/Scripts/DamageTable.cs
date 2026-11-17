using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// What each kind of weapon does to a ship's layers, from the outside in: the shield, the
    /// armour (how much of it the hit goes straight through, and how hard it wears it), the hull,
    /// and the ship's components (their condition: ShipSystems).
    ///
    /// - Guns, cannon, missiles and beams: as they always were against the shield and hull,
    ///   cannon and missiles going through a little of the armour.
    /// - Ion: twice as hard on a shield and three times as hard on a component, a quarter as
    ///   hard on the hull. It is for disabling ships, not for wrecking them.
    /// - Penetrator (armour-piercing): straight through the armour and a little harder on the
    ///   hull, but half as hard on a shield.
    /// - Nullifier: three times as hard on a shield, but under a third as hard on armour and hull.
    /// - Artillery: heavy shells over a wide area (Artillery), half through the armour.
    /// - Nuclear: hard on everything, half through the armour.
    /// </summary>
    public static class DamageTable
    {
        public struct Row
        {
            /// <summary>Damage to a shield, per point of the hit.</summary>
            public float shield;
            /// <summary>Damage to the hull, per point that got through the shield.</summary>
            public float hull;
            /// <summary>The share of the armour the hit goes straight through (0: all of it stops the hit; 1: none).</summary>
            public float pierce;
            /// <summary>How hard what the armour stops wears it away.</summary>
            public float wear;
            /// <summary>Damage to a component (a gun, the engines, the shield generator), per point.</summary>
            public float component;
            public Row(float shield, float hull, float pierce, float wear, float component)
            {
                this.shield = shield; this.hull = hull; this.pierce = pierce; this.wear = wear; this.component = component;
            }
        }

        static readonly Row[] rows =
        {
            new Row(1f, 1f, 0f, 1f, 1f),          // Projectile
            new Row(1f, 1f, 0.1f, 1f, 1f),        // Cannon
            new Row(1f, 1f, 0.3f, 1.5f, 1.2f),    // Missile
            new Row(1f, 1f, 0.2f, 0.6f, 1f),      // Beam
            new Row(2f, 0.25f, 0f, 0.2f, 3f),     // Ion
            new Row(0.5f, 1.2f, 1f, 0f, 1.2f),    // Penetrator
            new Row(3f, 0.3f, 0f, 0.3f, 0.5f),    // Nullifier
            new Row(1f, 1f, 0.5f, 1.5f, 1f),      // Artillery
            new Row(1.5f, 1f, 0.5f, 2f, 1.5f),    // Nuclear
        };

        /// <summary>The kinds there are.</summary>
        public static readonly int Count = System.Enum.GetValues(typeof(HitKind)).Length;

        public static Row Of(HitKind kind)
        {
            int i = (int)kind;
            return i >= 0 && i < rows.Length ? rows[i] : rows[0];
        }

        public static float Shield(HitKind kind) => Of(kind).shield;
        public static float Hull(HitKind kind) => Of(kind).hull;
        public static float Pierce(HitKind kind) => Of(kind).pierce;
        public static float Wear(HitKind kind) => Of(kind).wear;
        public static float Component(HitKind kind) => Of(kind).component;

        /// <summary>The nearest of the first four kinds, for games released before there were
        /// more (co-op's `kd`: they read it as one of the four; the new kind goes in `kx`). An
        /// old game applies the old kind's full rates, so an ion or nullifier hit sent to one
        /// does a beam's hull damage there (only the Vrask fire the new kinds, on the host).</summary>
        public static HitKind Legacy(HitKind kind)
        {
            switch (kind)
            {
                case HitKind.Ion:
                case HitKind.Nullifier: return HitKind.Beam;
                case HitKind.Penetrator:
                case HitKind.Artillery: return HitKind.Cannon;
                case HitKind.Nuclear: return HitKind.Missile;
                default: return kind;
            }
        }

        /// <summary>The kind a co-op hit carries: the new kind (`kx`, the kind + 1) if it says,
        /// else the old one (`kd`).</summary>
        public static HitKind FromWire(int kd, int kx)
        {
            if (kx > 0 && kx <= Count) return (HitKind)(kx - 1);
            return kd >= 0 && kd < Count ? (HitKind)kd : HitKind.Cannon;
        }

        /// <summary>A kind as a co-op hit sends it: (kd, kx).</summary>
        public static (int kd, int kx) ToWire(HitKind kind) => ((int)Legacy(kind), (int)kind + 1);

        /// <summary>What to call a kind of weapon (the target display).</summary>
        public static string Label(HitKind kind)
        {
            switch (kind)
            {
                case HitKind.Projectile: return "guns";
                case HitKind.Cannon: return "cannon";
                case HitKind.Missile: return "missiles";
                case HitKind.Beam: return "beam";
                case HitKind.Ion: return "ion";
                case HitKind.Penetrator: return "armour-piercing";
                case HitKind.Nullifier: return "shield nullifier";
                case HitKind.Artillery: return "artillery";
                case HitKind.Nuclear: return "nuclear";
                default: return kind.ToString().ToLowerInvariant();
            }
        }
    }
}

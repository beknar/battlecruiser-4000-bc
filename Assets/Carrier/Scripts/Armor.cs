using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Armour plating between a ship's shield and its hull. Whole, it stops `rating` of each hit
    /// that gets through the shield (less of a hit that pierces it: DamageTable.Pierce), and what
    /// it stops wears it away: `plating` is how much it can stop before it is worn through. Worn,
    /// it stops less in proportion. A repair (docked, landed at a base, respawned) makes it whole,
    /// and it can be grown back out of a fight (Mend: the Vrask do, AblativePlates); it never
    /// comes back in one. Its condition isn't saved: a ship comes into a game with its armour
    /// whole. In co-op the game that owns the ship sends how whole it is (NetSetIntegrity).
    /// </summary>
    public class Armor : MonoBehaviour
    {
        [Tooltip("The share of each hit (through the shield) the armour stops when whole.")]
        [Range(0f, 0.8f)] public float rating = 0.3f;
        [Tooltip("How much damage the armour can stop before it is worn through.")]
        public float plating = 3000f;

        /// <summary>What is left of the plating.</summary>
        public float Left { get; private set; } = -1f;
        /// <summary>How whole the armour is (1 new, 0 worn through).</summary>
        public float Integrity => plating > 0f ? Mathf.Clamp01(Current / plating) : 0f;
        /// <summary>All the damage it has stopped.</summary>
        public float Stopped { get; private set; }

        float Current => Left < 0f ? plating : Left;

        void Awake() { if (Left < 0f) Left = plating; }

        /// <summary>Where the last hit that wore it landed (world), if one has (HasHitPoint).</summary>
        public Vector3 LastHitPoint { get; private set; }
        public bool HasHitPoint { get; private set; }

        /// <summary>A hit's damage at a point (for what shows where the armour wore: AblativePlates).</summary>
        public float Reduce(float damage, HitKind kind, Vector3 point)
        {
            LastHitPoint = point;
            HasHitPoint = true;
            return Reduce(damage, kind);
        }

        /// <summary>A hit's damage to the hull after the armour (it stops its share, and wears).</summary>
        public float Reduce(float damage, HitKind kind)
        {
            if (damage <= 0f || rating <= 0f) return damage;
            var row = DamageTable.Of(kind);
            float share = Mathf.Clamp01(rating * Integrity * (1f - row.pierce));
            float stop = damage * share;
            if (stop <= 0f) return damage;
            Left = Mathf.Max(0f, Current - stop * row.wear);
            Stopped += stop;
            return damage - stop;
        }

        /// <summary>Whole again.</summary>
        public void Repair() => Left = plating;

        /// <summary>Some of it grown back (out of a fight), up to whole.</summary>
        public void Mend(float amount)
        {
            if (amount > 0f) Left = Mathf.Min(plating, Current + amount);
        }

        /// <summary>A new plating (parts fitted), keeping how worn it is.</summary>
        public void SetPlating(float value)
        {
            // (from none at all: whole)
            float k = plating > 0f ? Integrity : 1f;
            plating = Mathf.Max(0f, value);
            Left = plating * k;
        }

        /// <summary>Co-op: its condition, as the game that owns the ship has it.</summary>
        public void NetSetIntegrity(float integrity)
        {
            Left = plating * Mathf.Clamp01(integrity);
            netAt = Time.time;
        }

        float netAt = -100f;
        /// <summary>Another game owns this ship and sends how whole its armour is (co-op, the last
        /// word under 2 s ago): it isn't grown back here, only as that game says.</summary>
        public bool NetDriven => Time.time - netAt < 2f;
    }
}

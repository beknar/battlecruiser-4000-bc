using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Armour plating between a ship's shield and its hull. Whole, it stops `rating` of each hit
    /// that gets through the shield (less of a hit that pierces it: DamageTable.Pierce), and what
    /// it stops wears it away: `plating` is how much it can stop before it is worn through. Worn,
    /// it stops less in proportion. A repair (docked, landed at a base, respawned) makes it whole.
    /// Its condition isn't saved: a ship comes into a game with its armour whole.
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

        /// <summary>A new plating (parts fitted), keeping how worn it is.</summary>
        public void SetPlating(float value)
        {
            // (from none at all: whole)
            float k = plating > 0f ? Integrity : 1f;
            plating = Mathf.Max(0f, value);
            Left = plating * k;
        }

        /// <summary>Co-op: its condition, as the game that owns the ship has it.</summary>
        public void NetSetIntegrity(float integrity) => Left = plating * Mathf.Clamp01(integrity);
    }
}

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
}

using UnityEngine;
using UnityEngine.Events;

namespace CarrierGame
{
    /// <summary>What kind of weapon a hit came from (shields react to each).</summary>
    public enum HitKind { Projectile, Cannon, Missile, Beam }

    /// <summary>
    /// Hit points. Spawns an explosion and fires onDestroyed when they run out. Also keeps the
    /// damage record the target display shows: hits, total damage, times destroyed. If the ship
    /// has a Shield, hits that say where they landed go to the shield first.
    /// </summary>
    public class Health : MonoBehaviour
    {
        public float maxHealth = 1000f;
        [Tooltip("The hull bottoms out at 1 instead of being destroyed (the player's ship in the demo).")]
        public bool cannotDie = false;
        public Explosion deathEffect;
        public float deathEffectScale = 1f;
        [Tooltip("Respawn after this many seconds instead of being destroyed (0 = destroy).")]
        public float respawnDelay = 0f;
        public UnityEvent onDestroyed;
        public UnityEvent onRespawned;

        public float Current { get; private set; }
        public bool Dead { get; private set; }

        /// <summary>Damage taken since it was last at full strength (respawn resets it).</summary>
        public float DamageTaken => maxHealth - Mathf.Max(0f, Current);
        /// <summary>All damage it has absorbed, across respawns.</summary>
        public float TotalDamage { get; private set; }
        public int Hits { get; private set; }
        public int TimesDestroyed { get; private set; }
        public float LastHitTime { get; private set; } = -100f;
        public float LastHitAmount { get; private set; }
        /// <summary>When a destroyed ship comes back (only meaningful while Dead and respawning).</summary>
        public float RespawnAt { get; private set; }

        Shield shield;
        bool shieldLooked;

        void Awake() => Current = maxHealth;

        /// <summary>A hit at a point from a kind of weapon: the shield (if any, and up) takes it
        /// first, and only what gets through reaches the hull.</summary>
        public void Damage(float amount, Vector3 point, HitKind kind)
        {
            if (Dead || amount <= 0f) return;
            if (!shieldLooked) { shield = GetComponentInChildren<Shield>(true); shieldLooked = true; }
            if (shield != null && shield.Up) amount = shield.Absorb(amount, point, kind);
            if (amount > 0f) Damage(amount);
        }

        public void Damage(float amount)
        {
            if (Dead || amount <= 0f) return;
            float applied = Mathf.Min(amount, cannotDie ? Mathf.Max(0f, Current - 1f) : Current);
            Current -= applied;
            TotalDamage += applied;
            Hits++;
            LastHitTime = Time.time;
            LastHitAmount = applied;
            if (Current > 0f) return;
            Current = 0f;
            Dead = true;
            TimesDestroyed++;
            // a ship built from parts comes apart first and sets off its own explosion
            var breakup = GetComponent<ShipBreakup>();
            if (breakup != null) breakup.Break(deathEffect, deathEffectScale);
            else if (deathEffect != null)
            {
                var e = Instantiate(deathEffect, transform.position, Quaternion.identity);
                e.transform.localScale *= deathEffectScale;
            }
            onDestroyed?.Invoke();
            if (respawnDelay > 0f)
            {
                if (breakup == null) foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
                RespawnAt = Time.time + Mathf.Max(respawnDelay, breakup != null ? breakup.breakTime + 0.5f : 0f);
                Invoke(nameof(Respawn), RespawnAt - Time.time);
            }
            else
            {
                Destroy(gameObject, breakup != null ? breakup.breakTime + breakup.duration : 0f);
            }
        }

        void Respawn()
        {
            Current = maxHealth;
            Dead = false;
            var breakup = GetComponent<ShipBreakup>();
            // a ship that came apart is put back together; one that was hidden is shown again
            if (breakup != null) breakup.Restore();
            else foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true;
            onRespawned?.Invoke();
        }
    }
}

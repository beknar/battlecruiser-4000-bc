using UnityEngine;
using UnityEngine.Events;

namespace CarrierGame
{
    /// <summary>What kind of weapon a hit came from: shields react to each, and each does its
    /// own share to a shield, armour, the hull and a ship's components (DamageTable). The
    /// numbers go over the co-op wire, so a kind is only ever added at the end.</summary>
    public enum HitKind { Projectile, Cannon, Missile, Beam, Ion, Penetrator, Nullifier, Artillery, Nuclear, Emp }

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
        /// <summary>One that respawns is brought back by its location's SpawnDirector (every few
        /// minutes, not when the place has been cleared), not by its own timer.</summary>
        [System.NonSerialized] public bool respawnHeld;
        public UnityEvent onDestroyed;
        /// <summary>Anything with Health destroyed (after its breakup has started), every time
        /// (one that respawns too): salvage leaves a wreck where a hostile died. A listener that
        /// throws doesn't stop the death going on.</summary>
        public static event System.Action<Health> AnyDestroyed;
        public UnityEvent onRespawned;
        /// <summary>Taken by boarders (Capture) rather than destroyed: told instead of onDestroyed.</summary>
        public event System.Action Taken;
        /// <summary>Anything with Health taken by boarders, every time.</summary>
        public static event System.Action<Health> AnyCaptured;

        public float Current { get; private set; }
        public bool Dead { get; private set; }
        /// <summary>Down because it was taken by boarders (Capture), not destroyed.</summary>
        public bool Captured { get; private set; }
        public int TimesCaptured { get; private set; }

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
        /// <summary>When it was last destroyed.</summary>
        public float DiedAt { get; private set; } = -1f;

        /// <summary>When one held for its SpawnDirector comes back (for the target panel's countdown).</summary>
        public void SetRespawnAt(float t) { if (respawnHeld && Dead) RespawnAt = t; }

        Shield shield;
        Armor armor;
        ShipSystems systems;
        bool shieldLooked;

        /// <summary>Co-op: looks at each hit first, and takes it (returns true) when it belongs
        /// to another player's game: a hit on an enemy whose real self is on the host, or on
        /// another player's ship. Null in single-player.</summary>
        public static System.Func<Health, float, Vector3, HitKind, bool> Intercept;
        /// <summary>What dealt the hit being made now (the ship whose shot, missile or beam it
        /// is), set just before Damage by the weapon; null when unknown.</summary>
        public static Transform Source;

        void Awake()
        {
            Current = maxHealth;
            // a Health added in code has no events until something makes them
            onDestroyed ??= new UnityEvent();
            onRespawned ??= new UnityEvent();
        }

        /// <summary>A hit at a point from a kind of weapon, from the outside in: the shield (if
        /// any, and up) takes it first, as hard as that kind is on shields; what gets through
        /// does that kind's share to the hull, less what the armour (if any) stops; and the
        /// ship's components (ShipSystems, if it has them) take theirs on top.</summary>
        /// `toComponents`: false for knocks that aren't a weapon's (a collision, heat, lava),
        /// which wear the hull but not the ship's components.
        public void Damage(float amount, Vector3 point, HitKind kind, bool toComponents = true)
        {
            if (Dead || amount <= 0f) return;
            amount *= Rivalry.RefitOf(Source);
            Rivalry.Struck(transform, Source);
            if (Intercept != null && Intercept(this, amount, point, kind)) return;
            if (!shieldLooked) { shield = GetComponentInChildren<Shield>(true); armor = GetComponent<Armor>(); systems = GetComponent<ShipSystems>(); shieldLooked = true; }
            var row = DamageTable.Of(kind);
            float hit = amount;
            bool shielded = shield != null && shield.Up && row.shield > 0f;
            if (shielded) amount = shield.Absorb(amount * row.shield, point, kind) / row.shield;
            // (the components: from what got through the shield, and ion's from what it stopped too)
            if (systems != null && toComponents) systems.Hit(amount, shielded ? hit - amount : 0f, kind, Source);
            amount *= row.hull;
            if (armor != null) amount = armor.Reduce(amount, kind, point);
            LastHitKind = kind;
            if (amount > 0f) Damage(amount);
        }

        /// <summary>The kind of the last hit from a weapon (Damage with a kind).</summary>
        public HitKind LastHitKind { get; private set; }

        /// <summary>Armour added (or taken off) in play (parts fitted), or components: looked for again.</summary>
        public void Relook() => shieldLooked = false;

        /// <summary>Put the hull back to full (between the scenes of a demo reel).</summary>
        public void Repair()
        {
            if (Dead) return;
            Current = maxHealth;
            var a = GetComponent<Armor>();
            if (a != null) a.Repair();
            var sy = GetComponent<ShipSystems>();
            if (sy != null) sy.RepairAll();
        }

        float builtMax = -1f;

        /// <summary>Its full strength as built (before any co-op scaling).</summary>
        public float BuiltMaxHealth => builtMax >= 0f ? builtMax : maxHealth;

        /// <summary>Co-op: `k` times as strong as it was built (an enemy's, with more players),
        /// keeping how damaged it is.</summary>
        public void ScaleTo(float k)
        {
            if (builtMax < 0f) builtMax = maxHealth;
            SetMax(builtMax * k);
        }

        /// <summary>A new full strength (parts fitted: ShipFittings), keeping how damaged it is.</summary>
        public void SetMax(float max)
        {
            float fraction = maxHealth > 0f ? Current / maxHealth : 1f;
            maxHealth = Mathf.Max(1f, max);
            // (a hull at nothing stays at nothing: whatever calls this mid-hit can't undo a kill)
            if (!Dead && Current > 0f) Current = Mathf.Max(fraction * maxHealth, Mathf.Min(1f, maxHealth));
        }

        /// <summary>Damage that reached the hull (after the shield), as it is taken: its crew
        /// (Crew) lose people by it.</summary>
        public event System.Action<float> HullDamaged;

        public void Damage(float amount)
        {
            if (Dead || amount <= 0f) return;
            if (Intercept != null && Intercept(this, amount, transform.position, HitKind.Cannon)) return;
            float applied = Mathf.Min(amount, cannotDie ? Mathf.Max(0f, Current - 1f) : Current);
            Current -= applied;
            TotalDamage += applied;
            Hits++;
            LastHitTime = Time.time;
            LastHitAmount = applied;
            // (destroyed first if it is: what the crew's loss sets off can't undo a killing hit)
            if (Current <= 0f) Die();
            if (applied > 0f) HullDamaged?.Invoke(applied);
        }

        /// <summary>Co-op: this one's hull, as the host has it.</summary>
        public void NetSetHull(float current)
        {
            if (Dead) return;
            float before = Current;
            Current = Mathf.Clamp(current, 1f, maxHealth);
            // (the host's damage costs crew here too, so both games show the same crew; not the
            // first time, which only catches up with damage done before this game joined)
            if (netSynced && Current < before) HullDamaged?.Invoke(before - Current);
            netSynced = true;
        }

        bool netSynced;

        /// <summary>Co-op: destroyed on the host. It stays down until the host has it back.</summary>
        public void NetKill()
        {
            if (Dead) return;
            float rest = Current;
            Current = 0f;
            Die();
            // (the last of the hull, as the host lost it)
            if (netSynced && rest > 0f) HullDamaged?.Invoke(rest);
            CancelInvoke(nameof(Respawn));
        }

        /// <summary>Co-op: back on the host.</summary>
        public void NetRespawn()
        {
            if (Dead) Respawn();
        }

        /// <summary>
        /// Taken by boarders (Boarders) rather than destroyed: down (Dead), but in one piece. Nothing
        /// comes apart or explodes and no wreck is left: onDestroyed and AnyDestroyed aren't told,
        /// Taken and AnyCaptured are. The hull is left as it was. One that respawns (the player's
        /// ship) comes back after its delay, whole, as from a death (onRespawned: at its respawn
        /// point); one that doesn't (a fleet ship) is taken away.
        /// </summary>
        public void Capture()
        {
            if (Dead) return;
            Dead = true;
            Captured = true;
            DiedAt = Time.time;
            TimesCaptured++;
            if (Taken != null)
                foreach (System.Action listener in Taken.GetInvocationList())
                {
                    try { listener(); }
                    catch (System.Exception ex) { Debug.LogException(ex, this); }
                }
            if (AnyCaptured != null)
                foreach (System.Action<Health> listener in AnyCaptured.GetInvocationList())
                {
                    try { listener(this); }
                    catch (System.Exception ex) { Debug.LogException(ex, this); }
                }
            if (respawnDelay > 0f)
            {
                if (respawnHeld) RespawnAt = float.PositiveInfinity;   // (its SpawnDirector's to bring back)
                else
                {
                    RespawnAt = Time.time + respawnDelay;
                    Invoke(nameof(Respawn), respawnDelay);
                }
            }
            else Destroy(gameObject, 2f);
        }

        void Die()
        {
            Current = 0f;
            Dead = true;
            DiedAt = Time.time;
            TimesDestroyed++;
            // a ship built from parts comes apart first and sets off its own explosion
            var breakup = GetComponent<ShipBreakup>();
            if (breakup != null) breakup.Break(deathEffect, deathEffectScale);
            else if (deathEffect != null)
            {
                var e = Pool.Take(deathEffect, transform.position, Quaternion.identity);
                e.transform.localScale *= deathEffectScale;
            }
            onDestroyed?.Invoke();
            if (AnyDestroyed != null)
                foreach (System.Action<Health> listener in AnyDestroyed.GetInvocationList())
                {
                    try { listener(this); }
                    catch (System.Exception ex) { Debug.LogException(ex, this); }
                }
            if (respawnDelay > 0f)
            {
                if (breakup == null) foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
                if (respawnHeld) RespawnAt = float.PositiveInfinity;   // (its SpawnDirector's to bring back)
                else
                {
                    RespawnAt = Time.time + Mathf.Max(respawnDelay, breakup != null ? breakup.breakTime + 0.5f : 0f);
                    Invoke(nameof(Respawn), RespawnAt - Time.time);
                }
            }
            else
            {
                Destroy(gameObject, breakup != null ? breakup.breakTime + breakup.duration : 0f);
            }
        }

        /// <summary>Back now, if it is down (a SpawnDirector's spawn).</summary>
        /// <summary>Back to whole, alive or not (the battle simulator puts an encounter back as it was).</summary>
        public void Restore()
        {
            if (Dead) { RespawnNow(); return; }
            Current = maxHealth;
            var a = GetComponent<Armor>();
            if (a != null) a.Repair();
            var sy = GetComponent<ShipSystems>();
            if (sy != null) sy.RepairAll();
        }

        public void RespawnNow()
        {
            if (!Dead) return;
            CancelInvoke(nameof(Respawn));
            Respawn();
        }

        void Respawn()
        {
            Current = maxHealth;
            Dead = false;
            Captured = false;
            var a = GetComponent<Armor>();
            if (a != null) a.Repair();
            // (before onRespawned: the ship's own systems come back on after its components)
            var sy = GetComponent<ShipSystems>();
            if (sy != null) sy.RepairAll();
            var breakup = GetComponent<ShipBreakup>();
            // a ship that came apart is put back together; one that was hidden is shown again
            if (breakup != null) breakup.Restore();
            else foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = true;
            onRespawned?.Invoke();
        }
    }
}

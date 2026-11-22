using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// What the Greys' four ways of fighting share (ScoutTactics, ShroudTactics, LanceTactics,
    /// ReaperTactics): a ship that hunts by its sensors (Sensors.CanSee: it goes only for a
    /// player's ship it can see, and searches where one was last seen), thinking four times a
    /// second, with the last word on where its pilot goes while it fights.
    /// </summary>
    public abstract class GreysTactic : MonoBehaviour, CombatPilot.ITactics
    {
        protected CombatPilot pilot;
        protected EnemyWeapons weapons;
        protected Health health;
        protected GreysShip ship;
        float nextThink;

        /// <summary>The player's ship it is after (seen now, or last seen).</summary>
        public Transform Target { get; protected set; }
        /// <summary>Where it last saw its target, and when.</summary>
        public Vector3 LastSeen { get; private set; }
        public float LastSeenAt { get; private set; } = -1000f;
        /// <summary>What it is doing, in words (the tests, the CIC).</summary>
        public string Doing { get; protected set; } = "";

        protected virtual void Awake()
        {
            pilot = GetComponent<CombatPilot>();
            weapons = GetComponent<EnemyWeapons>();
            health = GetComponent<Health>();
            ship = GetComponent<GreysShip>();
            if (pilot != null) pilot.tactics = this;
        }

        protected virtual void OnDestroy()
        {
            if (pilot != null && ReferenceEquals(pilot.tactics, this)) pilot.tactics = null;
        }

        protected virtual void OnDisable()
        {
            if (pilot != null) pilot.keepTarget = false;
            Target = null;
        }

        /// <summary>Fighting: alive and out of hiding.</summary>
        protected bool Engaged => pilot != null && pilot.Alive && pilot.Current == CombatPilot.State.Engage;

        void Update()
        {
            if (pilot == null || Time.time < nextThink) return;
            nextThink = Time.time + 0.25f;
            if (!Engaged) { if (Target != null || pilot.keepTarget) { Target = null; pilot.keepTarget = false; Stood(); } return; }
            // the player's ship it goes for (the nearest it can see, unless its role says otherwise);
            // else the last one, where it was last seen
            var seen = Pick(NearestSeen());
            if (seen != null) { Target = seen; LastSeen = seen.position; LastSeenAt = Time.time; }
            else if (Target != null && Time.time - LastSeenAt > 30f)
            {
                // lost: searched long enough; the pilot let go of it too (it chases nothing, fires at nothing)
                Target = null;
                pilot.keepTarget = false;
                pilot.target = null;
                if (weapons != null) weapons.SetTarget(null);
                Stood();
            }
            if (Target != null)
            {
                pilot.keepTarget = true;
                if (pilot.target != Target) pilot.target = Target;
                if (weapons != null && weapons.target != Target) weapons.SetTarget(Target);
            }
            Think(seen != null);
        }

        /// <summary>The nearest player's ship it can see (its sensors, the target's signature).</summary>
        protected Transform NearestSeen()
        {
            Transform best = null;
            float bd = float.MaxValue;
            foreach (var p in PlayerShips.All)
            {
                if (p == null || !p.gameObject.activeInHierarchy || !PlayerShips.Present(p)) continue;
                if (p.TryGetComponent(out Health h) && h.Dead) continue;
                if (!Sensors.CanSee(transform, p)) continue;
                float d = (p.position - transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        /// <summary>Which seen ship it goes for (the nearest it can see, by default).</summary>
        protected virtual Transform Pick(Transform nearestSeen) => nearestSeen;

        /// <summary>Is the target seen by this ship's sensors now?</summary>
        protected bool Sees(Transform t) => t != null && t.gameObject.activeInHierarchy && Sensors.CanSee(transform, t);

        /// <summary>Four times a second while it fights: `seeing` whether it sees its target now.</summary>
        protected abstract void Think(bool seeing);

        /// <summary>Out of the fight (back in hiding, or nothing left to go for).</summary>
        protected virtual void Stood() { }

        public abstract void Steer(Vector3 pos, ref Vector3 desired, ref float wantSpeed);

        /// <summary>Where it would go after its target: where it is, or where it was last seen.</summary>
        protected Vector3 TargetPos => Target != null && Time.time - LastSeenAt < 0.6f ? Target.position : LastSeen;

        /// <summary>Is a ship disabled: its engines out, or its shield down and its hull under half?</summary>
        public static bool Disabled(Transform t)
        {
            if (t == null) return false;
            var sys = t.GetComponent<ShipSystems>();
            if (sys != null && sys.EnginesDown) return true;
            var h = t.GetComponent<Health>();
            var sh = t.GetComponentInChildren<Shield>();
            return h != null && h.Current < h.maxHealth * 0.5f && (sh == null || !sh.Up);
        }

        /// <summary>The share of its hull a ship has left (1: no Health).</summary>
        protected static float HullOf(Transform t) => t != null && t.TryGetComponent(out Health h) ? h.Current / Mathf.Max(1f, h.maxHealth) : 1f;
    }
}

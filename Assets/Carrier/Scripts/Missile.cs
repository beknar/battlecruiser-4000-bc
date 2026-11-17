using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A guided missile: boosts straight out of its launch cell, then accelerates and steers
    /// toward its target, exploding on contact or when it runs out of fuel. A torpedo
    /// (`underwater`) is a slow one that keeps under the sea's surface: it can reach a ship in the
    /// water, or one afloat (it runs into the hull below the waterline), but nothing above.
    /// Point defence can shoot one down (a hostile one: CiwsDirector, Projectile): `toughness`
    /// of damage from bolts passing within `hitRadius` of it. A Vrask boarding pod is one too
    /// (BoardingPod).
    /// </summary>
    public class Missile : MonoBehaviour, IVelocity
    {
        public Transform target;
        public float boostTime = 1.2f;
        public float boostSpeed = 120f;
        public float maxSpeed = 900f;
        public float acceleration = 400f;
        public float turnRate = 70f;          // degrees per second once guiding
        public float lifetime = 20f;
        public float proximity = 25f;
        public float damage = 250f;
        public Explosion explosion;
        [Tooltip("A torpedo: it stays under the sea's surface (and goes off if there is no sea).")]
        public bool underwater;
        [HideInInspector] public Vector3 inheritedVelocity;
        /// <summary>The ship that launched it (for co-op: whose hit it is).</summary>
        [System.NonSerialized] public Transform owner;
        /// <summary>Co-op: a missile has been launched (announced to the other players' games).</summary>
        public static event System.Action<Missile> Spawned;
        [Tooltip("Damage from point-defence bolts that shoots it down.")]
        public float toughness = 16f;
        [Tooltip("How near (m) a point-defence bolt must pass to hit it.")]
        public float hitRadius = 7f;
        /// <summary>Shot down by point defence: the missile, and the ship whose shot it was (null:
        /// told by another player's game).</summary>
        public static event System.Action<Missile, Transform> Downed;
        /// <summary>Missiles and pods shot down since the level started.</summary>
        public static int ShotDownCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDowned() => ShotDownCount = 0;

        float age, speed, hurt;
        int side = -1;   // 1: launched by a hostile ship, 0: not (worked out on first use)
        bool gone;
        /// <summary>Exploded, breached or shot down (it goes at the frame's end).</summary>
        protected bool Gone => gone;
        Transform moverOf;
        IVelocity mover;

        /// <summary>Missiles in flight, so the target display can count those inbound.</summary>
        public static readonly System.Collections.Generic.List<Missile> Active = new System.Collections.Generic.List<Missile>();

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        void Start()
        {
            speed = boostSpeed;
            SoundFX.Play(Sfx.MissileLaunch, transform.position);
            Spawned?.Invoke(this);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (age > boostTime)
            {
                speed = Mathf.Min(maxSpeed, speed + acceleration * dt);
                if (target != null)
                {
                    Vector3 to = target.position - transform.position;
                    // lead the target a little if it moves
                    if (moverOf != target) { moverOf = target; mover = Motion.Of(target); }
                    if (mover != null) to = Ballistics.Lead(transform.position, target.position, mover.Velocity, Mathf.Max(1f, speed), maxTime: 3f) - transform.position;
                    var want = Quaternion.LookRotation(to, transform.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnRate * dt);
                    if (to.magnitude < proximity) { Detonate(); return; }
                }
            }
            Vector3 step = (transform.forward * speed + inheritedVelocity) * dt;
            // explode on contact with anything belonging to the target (its shield included),
            // or on an asteroid or wreck that got in the way
            if (Physics.Raycast(transform.position, step.normalized, out var hit, step.magnitude + 1f, ~0, QueryTriggerInteraction.Ignore)
                && ((target != null && hit.transform.IsChildOf(target.root)) || Cover.IsObstacle(hit.collider)))
            {
                transform.position = hit.point;
                Detonate();
                return;
            }
            transform.position += step;
            if (underwater)
            {
                // a torpedo runs just under the surface at most
                if (!Sea.Here) { Detonate(); return; }
                float top = Sea.Level - 1.5f;
                if (transform.position.y > top) transform.position = new Vector3(transform.position.x, top, transform.position.z);
            }
            inheritedVelocity = Vector3.MoveTowards(inheritedVelocity, Vector3.zero, 50f * dt);
            if (age > lifetime) Detonate();
        }

        protected virtual void Detonate()
        {
            if (gone) return;
            if (AtTarget())
            {
                Health.Source = owner;
                target.GetComponentInParent<Health>()?.Damage(damage, transform.position, HitKind.Missile);
                Health.Source = null;
            }
            Finish(explosion);
        }

        /// <summary>Near enough its target that it counts as reaching it.</summary>
        protected bool AtTarget() =>
            target != null && Vector3.Distance(target.position, transform.position) < Mathf.Max(proximity * 2f, TargetRadius());

        /// <summary>Gone: an explosion where it is (or none), a torpedo's wake left behind.</summary>
        protected void Finish(Explosion fx)
        {
            gone = true;
            if (fx != null) Pool.Take(fx, transform.position, Quaternion.identity);
            // (a torpedo's bubbles and wake stay behind, fading)
            if (underwater && TryGetComponent(out TorpedoWake wake)) wake.Leave();
            Destroy(gameObject);
        }

        /// <summary>Rough size of the target, so a hit on a big ship's shield still counts.</summary>
        float TargetRadius()
        {
            var col = target.GetComponentInChildren<Collider>();
            return col != null ? col.bounds.extents.magnitude + 5f : 0f;
        }

        /// <summary>How fast it is going (for point defence to lead it).</summary>
        public Vector3 Velocity => transform.forward * speed + inheritedVelocity;

        /// <summary>Launched by a hostile ship (the players' point defence shoots these down).</summary>
        public bool Hostile
        {
            get
            {
                if (side < 0)
                {
                    var oi = owner != null ? owner.GetComponentInParent<TargetInfo>() : null;
                    side = oi != null && oi.Hostile ? 1 : 0;
                }
                return side == 1;
            }
        }

        /// <summary>A point-defence hit: shot down once its toughness is used up.</summary>
        public void Shoot(float amount, Transform by)
        {
            if (gone || amount <= 0f) return;
            hurt += amount;
            if (hurt >= toughness) ShootDown(by);
        }

        /// <summary>Shot down: a burst where it is, nothing to its target. `by`: the ship whose
        /// shot it was (null: told by another player's game, CoopWorld.Boarding).</summary>
        public void ShootDown(Transform by)
        {
            if (gone) return;
            ShotDownCount++;
            Downed?.Invoke(this, by);
            Finish(explosion);
        }

        /// <summary>The hostile missile nearest along a shot's path (from `from`, `length` along
        /// `dir`) that the shot passes within its hit radius of, and how far along: null if none.</summary>
        public static Missile InPath(Vector3 from, Vector3 dir, float length, out float at)
        {
            Missile best = null;
            at = float.MaxValue;
            foreach (var m in Active)
            {
                if (m == null || m.gone || !m.Hostile) continue;
                Vector3 w = m.transform.position - from;
                float t = Mathf.Clamp(Vector3.Dot(w, dir), 0f, length);
                if (t >= at || (w - dir * t).sqrMagnitude > m.hitRadius * m.hitRadius) continue;
                at = t;
                best = m;
            }
            return best;
        }
    }

    /// <summary>Anything that reports its velocity, for missiles to lead.</summary>
    public interface IVelocity
    {
        Vector3 Velocity { get; }
    }

    public static class Motion
    {
        /// <summary>The component that is actually moving this ship (enabled, on it or a parent).</summary>
        static readonly System.Collections.Generic.List<MonoBehaviour> Found = new System.Collections.Generic.List<MonoBehaviour>();

        public static IVelocity Of(Transform t)
        {
            if (t == null) return null;
            t.GetComponentsInParent(false, Found);
            IVelocity result = null;
            foreach (var c in Found)
                if (c is IVelocity v && c.enabled) { result = v; break; }
            Found.Clear();
            return result;
        }
    }
}

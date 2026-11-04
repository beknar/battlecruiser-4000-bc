using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A guided missile: boosts straight out of its launch cell, then accelerates and steers
    /// toward its target, exploding on contact or when it runs out of fuel.
    /// </summary>
    public class Missile : MonoBehaviour
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
        [HideInInspector] public Vector3 inheritedVelocity;

        float age, speed;

        /// <summary>Missiles in flight, so the target display can count those inbound.</summary>
        public static readonly System.Collections.Generic.List<Missile> Active = new System.Collections.Generic.List<Missile>();

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        void Start()
        {
            speed = boostSpeed;
            SoundFX.Play(Sfx.MissileLaunch, transform.position);
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
                    var mover = Motion.Of(target);
                    if (mover != null) to += mover.Velocity * Mathf.Min(3f, to.magnitude / Mathf.Max(1f, speed));
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
            inheritedVelocity = Vector3.MoveTowards(inheritedVelocity, Vector3.zero, 50f * dt);
            if (age > lifetime) Detonate();
        }

        void Detonate()
        {
            if (target != null && Vector3.Distance(target.position, transform.position) < Mathf.Max(proximity * 2f, TargetRadius()))
                target.GetComponentInParent<Health>()?.Damage(damage, transform.position, HitKind.Missile);
            if (explosion != null) Instantiate(explosion, transform.position, Quaternion.identity);
            Destroy(gameObject);
        }

        /// <summary>Rough size of the target, so a hit on a big ship's shield still counts.</summary>
        float TargetRadius()
        {
            var col = target.GetComponentInChildren<Collider>();
            return col != null ? col.bounds.extents.magnitude + 5f : 0f;
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
        public static IVelocity Of(Transform t)
        {
            if (t == null) return null;
            foreach (var c in t.GetComponentsInParent<MonoBehaviour>())
                if (c is IVelocity v && c.enabled) return v;
            return null;
        }
    }
}

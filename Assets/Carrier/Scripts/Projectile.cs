using UnityEngine;

namespace CarrierGame
{
    /// <summary>A bolt that flies straight, hits the first collider in its path and deals damage.
    /// Bolts come from the Pool and go back to it, so each one starts over as it is switched on.</summary>
    public class Projectile : MonoBehaviour
    {
        public float speed = 1500f;
        public float lifetime = 4f;
        public float damage = 40f;
        public Explosion hitEffect;
        [Tooltip("Small arms (projectile) or a heavy gun (cannon), for shields to react to.")]
        public HitKind kind = HitKind.Projectile;
        [HideInInspector] public Transform owner;

        float age, prefabDamage;
        int ownerSide = -1;   // 1: fired by a hostile ship, 0: not (worked out on first use)
        bool announced;

        // the damage as the prefab has it: whoever fires one may change it (co-op scaling, or none
        // for a shot shown from another player's game)
        void Awake() => prefabDamage = damage;

        void OnEnable()
        {
            age = 0f;
            ownerSide = -1;
            announced = false;
            damage = prefabDamage;
            owner = null;
        }

        /// <summary>A hostile ship's shot doesn't hurt the other hostiles (a battle group firing
        /// past its own flagship). The player's side is unchanged.</summary>
        bool Friendly(Transform hit)
        {
            if (owner == null) return false;
            if (ownerSide < 0)
            {
                var oi = owner.GetComponentInParent<TargetInfo>();
                ownerSide = oi != null && oi.Hostile ? 1 : 0;
            }
            if (ownerSide == 0) return false;
            var hi = hit.GetComponentInParent<TargetInfo>();
            return hi != null && hi.Hostile;
        }

        /// <summary>Co-op: a shot has been fired (announced to the other players' games).</summary>
        public static event System.Action<Projectile> Spawned;

        void Update()
        {
            // announced once its owner and damage are set (as Start would, before its first move)
            if (!announced) { announced = true; Spawned?.Invoke(this); }
            float step = speed * Time.deltaTime;
            if (Physics.Raycast(transform.position, transform.forward, out var hit, step, ~0, QueryTriggerInteraction.Ignore)
                && (owner == null || !hit.transform.IsChildOf(owner)) && !Friendly(hit.transform))
            {
                var h = hit.collider.GetComponentInParent<Health>();
                bool onShield = hit.collider.GetComponent<Shield>() != null;
                Health.Source = owner;
                if (h != null) h.Damage(damage, hit.point, kind);
                Health.Source = null;
                // a shield shows its own ripple and sound; anything else gets the usual spark
                if (!onShield)
                {
                    if (h != null && hitEffect == null) SoundFX.Play(Sfx.Impact, hit.point, 0.6f);
                    if (hitEffect != null) Pool.Take(hitEffect, hit.point, Quaternion.identity);
                }
                Pool.Give(gameObject);
                return;
            }
            transform.position += transform.forward * step;
            age += Time.deltaTime;
            if (age > lifetime) Pool.Give(gameObject);
        }
    }
}

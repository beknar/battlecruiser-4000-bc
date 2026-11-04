using UnityEngine;

namespace CarrierGame
{
    /// <summary>A bolt that flies straight, hits the first collider in its path and deals damage.</summary>
    public class Projectile : MonoBehaviour
    {
        public float speed = 1500f;
        public float lifetime = 4f;
        public float damage = 40f;
        public Explosion hitEffect;
        [Tooltip("Small arms (projectile) or a heavy gun (cannon), for shields to react to.")]
        public HitKind kind = HitKind.Projectile;
        [HideInInspector] public Transform owner;

        float age;
        int ownerSide = -1;   // 1: fired by a hostile ship, 0: not (worked out on first use)

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

        void Update()
        {
            float step = speed * Time.deltaTime;
            if (Physics.Raycast(transform.position, transform.forward, out var hit, step, ~0, QueryTriggerInteraction.Ignore)
                && (owner == null || !hit.transform.IsChildOf(owner)) && !Friendly(hit.transform))
            {
                var h = hit.collider.GetComponentInParent<Health>();
                bool onShield = hit.collider.GetComponent<Shield>() != null;
                if (h != null) h.Damage(damage, hit.point, kind);
                // a shield shows its own ripple and sound; anything else gets the usual spark
                if (!onShield)
                {
                    if (h != null && hitEffect == null) SoundFX.Play(Sfx.Impact, hit.point, 0.6f);
                    if (hitEffect != null) Instantiate(hitEffect, hit.point, Quaternion.identity);
                }
                Destroy(gameObject);
                return;
            }
            transform.position += transform.forward * step;
            age += Time.deltaTime;
            if (age > lifetime) Destroy(gameObject);
        }
    }
}

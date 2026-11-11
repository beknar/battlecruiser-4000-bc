using UnityEngine;

namespace CarrierGame
{
    /// <summary>A bolt that flies straight, hits the first collider in its path and deals damage.
    /// Bolts come from the Pool and go back to it, so each one starts over as it is switched on.
    /// Under the sea it doesn't glow: it leaves a streak of bubbles behind it instead.</summary>
    public class Projectile : MonoBehaviour
    {
        public float speed = 1500f;
        public float lifetime = 4f;
        public float damage = 40f;
        public Explosion hitEffect;
        [Tooltip("Small arms (projectile) or a heavy gun (cannon), for shields to react to.")]
        public HitKind kind = HitKind.Projectile;
        [HideInInspector] public Transform owner;
        [Tooltip("Soft round particle (alpha-blended), for its trail of bubbles under the water.")]
        public Material bubbles;

        /// <summary>Shots that have gone under the water, and the bubbles they left (for the self-test).</summary>
        public static int UnderwaterShots, BubblesLeft;
        /// <summary>It is under the water now (its glow hidden).</summary>
        public bool Underwater { get; private set; }
        const float BubblesPerMetre = 0.12f;
        // (a shot that misses flies on for km under the water: only its first stretch trails)
        const float TrailLength = 1500f;
        // (one system for every shot's bubbles; it goes with the level)
        static ParticleSystem trailFx;
        Renderer[] glow;
        bool wentUnder;
        float bubbleDue, trailRun;

        float age, prefabDamage;
        int ownerSide = -1;   // 1: fired by a hostile ship, 0: not (worked out on first use)
        bool announced;

        // the damage as the prefab has it: whoever fires one may change it (co-op scaling, or none
        // for a shot shown from another player's game)
        void Awake()
        {
            prefabDamage = damage;
            // (what it shows above the water: whatever it has switched on)
            glow = System.Array.FindAll(GetComponentsInChildren<Renderer>(true), r => r.enabled);
        }

        void OnEnable()
        {
            age = 0f;
            ownerSide = -1;
            announced = false;
            damage = prefabDamage;
            owner = null;
            wentUnder = false;
            bubbleDue = trailRun = 0f;
            // (the Pool has put it at the muzzle by now: fired under the water, it never shows)
            Glow(Sea.Depth(transform.position) <= 0.5f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            UnderwaterShots = BubblesLeft = 0;
            trailFx = null;
        }

        void Glow(bool on)
        {
            Underwater = !on;
            if (glow != null) foreach (var r in glow) if (r != null) r.enabled = on;
        }

        /// <summary>It has flown from `from` to `to` this frame: under the water, no glow, and a
        /// streak of bubbles along the stretch of that under the surface.</summary>
        void Wake(Vector3 from, Vector3 to)
        {
            if (!Sea.Here) { if (Underwater) Glow(true); return; }
            float sea = Sea.Level;
            bool under = to.y < sea - 0.5f;
            if (under != Underwater) Glow(!under);
            if (under && !wentUnder) { wentUnder = true; UnderwaterShots++; }
            if (!under && from.y >= sea - 0.5f) return;
            if (trailFx == null)
                trailFx = WaterFx.Make("Shell Bubbles", bubbles, 6000, new Color(0.84f, 0.95f, 1f, 0.7f), 1.2f, 2.4f, 0.6f, 1.6f, -0.4f, grow: 1.6f, drag: 2f);
            else if (bubbles != null && trailFx.TryGetComponent(out ParticleSystemRenderer tr) && tr.sharedMaterial != bubbles)
                tr.sharedMaterial = bubbles;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return;
            Vector3 dir = d / len;
            if (trailRun > TrailLength) return;
            trailRun += len;
            bubbleDue += len * BubblesPerMetre;
            while (bubbleDue >= 1f)
            {
                bubbleDue -= 1f;
                Vector3 p = Vector3.Lerp(from, to, Random.value) + Random.insideUnitSphere * 0.6f;
                float u = sea - p.y;
                if (u < 0.5f) continue;
                // dragged a little after the shell, then rising (and gone before the top)
                Vector3 v = dir * Random.Range(2f, 8f) + Random.insideUnitSphere * 1.5f + Vector3.up * Random.Range(0.5f, 2f);
                WaterFx.Emit(trailFx, p, v, Random.Range(0.6f, 1.6f), Mathf.Min(Random.Range(1.2f, 2.4f), 0.3f + u / 2.5f));
                BubblesLeft++;
            }
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
            Vector3 from = transform.position;
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
                Wake(from, hit.point);
                Pool.Give(gameObject);
                return;
            }
            transform.position += transform.forward * step;
            Wake(from, transform.position);
            age += Time.deltaTime;
            if (age > lifetime) Pool.Give(gameObject);
        }
    }
}

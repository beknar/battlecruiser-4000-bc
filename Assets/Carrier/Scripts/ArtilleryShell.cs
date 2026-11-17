using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A heavy artillery shell (the Vrask Maw's spinal gun: ArtilleryGun). It is slow, and it is
    /// not guided: it flies straight to the point it was aimed at (where the gun worked out its
    /// target would be when it got there), so it takes many seconds to arrive and a ship that turns
    /// or changes speed meanwhile isn't there when it does. On arrival (or on anything solid in its
    /// way) it bursts in a wide blast, and every ship within `blastRadius` takes damage of its kind,
    /// less the further it is from the centre (Falloff); the ships of the side that fired it are
    /// spared. The blast is shown with a flash and an explosion sized to it.
    ///
    /// Its kind is the round the gun chose (ArtilleryGun.Round): high explosive (HitKind.Artillery),
    /// shield nullifier (Nullifier: three times as hard on a shield) or armour penetrator
    /// (Penetrator: through the armour). A shell shown from another player's game (no damage) only
    /// flies and bursts. The blast is also what a nuclear missile does in space (NukeMissile).
    /// </summary>
    public class ArtilleryShell : MonoBehaviour, IOriginShift
    {
        [Tooltip("How fast it flies (m/s).")]
        public float speed = 550f;
        [Tooltip("Damage at the centre of its blast.")]
        public float damage = 700f;
        [Tooltip("Its blast's reach (m).")]
        public float blastRadius = 500f;
        [Tooltip("The share of its damage at the blast's edge (all of it within a sixth of the radius).")]
        [Range(0f, 1f)] public float edge = 0.2f;
        [Tooltip("The longest it flies (s) before it bursts anyway.")]
        public float maxFlight = 40f;
        public HitKind kind = HitKind.Artillery;
        [Tooltip("The burst (scaled to the blast).")]
        public Explosion blastEffect;
        [Tooltip("The colour of its flash.")]
        public Color flashColor = new Color(0.6f, 1f, 0.45f);

        /// <summary>The ship that fired it (its Health's transform: spared by its blast, with its
        /// side; co-op: whose hit it is).</summary>
        [System.NonSerialized] public Transform owner;
        /// <summary>Where it is going, and how long it takes to get there (s).</summary>
        public Vector3 Aim { get; private set; }
        public float Flight { get; private set; }
        public bool Burst { get; private set; }

        /// <summary>Co-op: a shell has been fired (shown in the other players' games).</summary>
        public static event System.Action<ArtilleryShell> Spawned;
        /// <summary>Every shell and nuclear blast so far: how many, and the last.</summary>
        public static int Fired { get; private set; }
        public static int Blasts { get; private set; }
        public static BlastResult LastBlast { get; private set; }
        /// <summary>Shells in flight.</summary>
        public static readonly List<ArtilleryShell> Active = new List<ArtilleryShell>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Fired = Blasts = 0; LastBlast = default; Active.Clear(); }

        Vector3 from;
        string ownerGroup = "";

        /// <summary>The world moved under it (the origin recentred, or its reach moved): its line of
        /// flight moves with it.</summary>
        public void OriginShifted(Vector3 d)
        {
            from += d;
            Aim += d;
        }
        float age;
        bool announced;

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        /// <summary>A shell from `at` to `aim`, of a kind, fired by `owner` (damage 0: one shown
        /// from another game). It arrives in distance / speed seconds (at most maxFlight).</summary>
        public static ArtilleryShell Launch(ArtilleryShell prefab, Vector3 at, Vector3 aim, HitKind kind, Transform owner, float damage = -1f)
        {
            if (prefab == null) return null;
            Vector3 dir = aim - at;
            var s = Instantiate(prefab, at, Quaternion.LookRotation(dir.sqrMagnitude > 1e-4f ? dir : Vector3.forward));
            s.owner = owner;
            OrbitHome.Inherit(s.gameObject, owner);
            s.kind = kind;
            if (damage >= 0f) s.damage = damage;
            s.from = at;
            s.Aim = aim;
            s.Flight = Mathf.Clamp(Vector3.Distance(at, aim) / Mathf.Max(1f, s.speed), 0.1f, s.maxFlight);
            if (!s.gameObject.activeSelf) s.gameObject.SetActive(true);     // (from a prefab kept switched off)
            return s;
        }

        void Start()
        {
            if (Flight <= 0f) { from = transform.position; Aim = from + transform.forward * speed * maxFlight; Flight = maxFlight; }
            if (announced) return;
            announced = true;
            Fired++;
            Spawned?.Invoke(this);
        }

        void Update()
        {
            if (Burst) return;
            age += Time.deltaTime;
            float k = Mathf.Clamp01(age / Flight);
            Vector3 next = Vector3.Lerp(from, Aim, k);
            Vector3 step = next - transform.position;
            // (bursting on anything solid in its way: a hull, a shield, a rock; not a ship of its own side)
            if (step.sqrMagnitude > 1e-6f && InTheWay(transform.position, step, out Vector3 point))
            {
                transform.position = point;
                Detonate();
                return;
            }
            transform.position = next;
            if (step.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(step);
            if (k >= 1f) Detonate();
        }

        static readonly RaycastHit[] Hits = new RaycastHit[16];
        int side = -1;     // 1: fired by a hostile ship, 0: not (worked out on first use)

        /// <summary>The nearest thing along a step that it bursts on: anything but the ships of the
        /// side that fired it.</summary>
        bool InTheWay(Vector3 p, Vector3 step, out Vector3 point)
        {
            point = p;
            if (side < 0)
            {
                var oi = owner != null ? owner.GetComponentInParent<TargetInfo>() : null;
                side = oi != null && oi.Hostile ? 1 : 0;
                ownerGroup = oi != null ? oi.group : "";
            }
            int n = Physics.RaycastNonAlloc(p, step.normalized, Hits, step.magnitude + 1f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider == null || h.distance >= best) continue;
                if (owner != null && h.transform.IsChildOf(owner)) continue;
                var info = side == 1 ? h.collider.GetComponentInParent<TargetInfo>() : null;
                if (info != null && info.Hostile && info.group == ownerGroup) continue;
                best = h.distance;
                point = h.point;
            }
            return best < float.MaxValue;
        }

        /// <summary>Burst where it is now.</summary>
        public void Detonate()
        {
            if (Burst) return;
            Burst = true;
            if (damage > 0f) Blast(transform.position, blastRadius, damage, edge, kind, owner);
            Show(transform.position, blastRadius, flashColor, blastEffect);
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------ the blast

        /// <summary>What a blast did: where, how wide, and the ships it hit (with the share of its
        /// damage each took).</summary>
        public struct BlastResult
        {
            public Vector3 at;
            public float radius, damage;
            public HitKind kind;
            public List<(Health health, float share)> hits;
            public int Count => hits != null ? hits.Count : 0;
            public bool Hit(Health h)
            {
                if (hits != null) foreach (var x in hits) if (x.health == h) return true;
                return false;
            }
            public float ShareOf(Health h)
            {
                if (hits != null) foreach (var x in hits) if (x.health == h) return x.share;
                return 0f;
            }
        }

        /// <summary>The share of a blast's damage at `distance` from its centre: all of it within a
        /// sixth of the radius, falling off evenly to `edge` at the radius, none beyond.</summary>
        public static float Falloff(float distance, float radius, float edge)
        {
            if (radius <= 0f || distance > radius) return 0f;
            float inner = radius / 6f;
            if (distance <= inner) return 1f;
            return Mathf.Lerp(1f, Mathf.Clamp01(edge), (distance - inner) / (radius - inner));
        }

        static readonly Collider[] Near = new Collider[256];
        static readonly Dictionary<Health, float> nearest = new Dictionary<Health, float>();

        /// <summary>A blast at a point: every ship (anything with a Health) with a part within
        /// `radius` takes `damage` x Falloff(its nearest part's distance) of `kind`, but the ships
        /// of `owner`'s side (a Vrask blast spares the Vrask). Co-op: a hit on another player's
        /// ship goes to their game, as any of the host's enemies' hits does (Health.Intercept).</summary>
        public static BlastResult Blast(Vector3 at, float radius, float damage, float edge, HitKind kind, Transform owner)
        {
            var res = new BlastResult { at = at, radius = radius, damage = damage, kind = kind, hits = new List<(Health, float)>() };
            Blasts++;
            if (damage <= 0f || radius <= 0f) { LastBlast = res; return res; }
            var ownerInfo = owner != null ? owner.GetComponentInParent<TargetInfo>() : null;
            bool ownerHostile = ownerInfo != null && ownerInfo.Hostile;
            Physics.SyncTransforms();
            int n = Physics.OverlapSphereNonAlloc(at, radius, Near, ~0, QueryTriggerInteraction.Ignore);
            nearest.Clear();
            for (int i = 0; i < n; i++)
            {
                var c = Near[i];
                if (c == null) continue;
                var h = c.GetComponentInParent<Health>();
                if (h == null || h.Dead) continue;
                if (owner != null && h.transform.IsChildOf(owner)) continue;
                if (ownerInfo != null)
                {
                    // (its own: the same side and the same group; the corsairs are fair game)
                    var info = h.GetComponentInParent<TargetInfo>();
                    if (info != null && info.Hostile == ownerHostile && info.group == ownerInfo.group) continue;
                }
                float d = Vector3.Distance(at, c.bounds.ClosestPoint(at));
                if (!nearest.TryGetValue(h, out float was) || d < was) nearest[h] = d;
            }
            foreach (var kv in nearest)
            {
                float share = Falloff(kv.Value, radius, edge);
                if (share <= 0f) continue;
                res.hits.Add((kv.Key, share));
            }
            nearest.Clear();
            // (after the look: a hit can destroy a ship and change what is there)
            foreach (var (h, share) in res.hits)
            {
                if (h == null || h.Dead) continue;
                Health.Source = owner;
                Vector3 p = h.transform.position;
                var col = h.GetComponentInChildren<Collider>();
                if (col != null) p = col.bounds.ClosestPoint(at);
                h.Damage(damage * share, p, kind);
                Health.Source = null;
            }
            LastBlast = res;
            return res;
        }

        /// <summary>A blast seen: a flash as wide as it (from far off, StrikeFx keeps it in
        /// sight), and the explosion scaled up to it.</summary>
        public static void Show(Vector3 at, float radius, Color color, Explosion fx)
        {
            if (Demo.StrikeFx.glowMaterial != null) Demo.StrikeFx.Flash(at, radius * 2f, color, fx);
            else if (fx != null)
            {
                var e = Pool.Take(fx, at, Quaternion.identity);
                e.transform.localScale = fx.transform.localScale * Mathf.Max(1f, radius / Mathf.Max(1f, fx.endSize));
                SoundFX.Play(Sfx.ExplosionBig, at, 1f, 0.6f);
            }
        }
    }
}

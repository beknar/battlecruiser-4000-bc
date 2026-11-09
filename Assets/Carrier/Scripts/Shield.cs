using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// An energy shield round a ship: a hexagon-tiled bubble (the StylizedShieldsVol1 sphere and
    /// hexagon texture from the Unreal export) with a collider, so incoming fire stops on it.
    ///
    /// Every hit that lands on it shows: the hexagons round the impact light up and a ring spreads
    /// out from it, sized by the weapon (light for small arms, bigger for cannon shells, a wide
    /// flash for missiles, a steady burning spot under a beam), and the bubble briefly glows.
    /// The shield soaks up damage until it runs out, drops for a few seconds, then comes back
    /// and recharges; whatever gets through goes to the hull.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Shield : MonoBehaviour
    {
        public float capacity = 6000f;
        [Tooltip("Recharge per second, once it has gone this long without a hit.")]
        public float rechargeRate = 250f;
        public float rechargeDelay = 3f;
        [Tooltip("After collapsing, the shield is down for this long, then returns at 30%.")]
        public float downTime = 6f;
        public Renderer bubble;

        public float Strength { get; private set; }
        public bool Up { get; private set; } = true;
        public float Fraction => capacity > 0f ? Strength / capacity : 0f;
        public float LastHitTime { get; private set; } = -100f;
        /// <summary>Hits taken, counted per weapon kind (for the HUD and the self-test).</summary>
        public readonly int[] HitsByKind = new int[4];
        public int Collapses { get; private set; }

        const int MaxHits = 8;
        readonly Vector4[] hits = new Vector4[MaxHits];       // xyz world position, w age (s), -1 = unused
        readonly Vector4[] strengths = new Vector4[MaxHits];  // x strength, y size
        readonly float[] born = new float[MaxHits];
        int nextSlot;
        float flash, downUntil, lastBeamRipple = -1f;
        Collider col;
        MaterialPropertyBlock block;
        static readonly int HitsId = Shader.PropertyToID("_Hits");
        static readonly int StrengthId = Shader.PropertyToID("_HitStrength");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int IdleId = Shader.PropertyToID("_Idle");

        void Awake()
        {
            col = GetComponent<Collider>();
            if (bubble == null) bubble = GetComponent<Renderer>();
            Strength = capacity;
            for (int i = 0; i < MaxHits; i++) { hits[i] = new Vector4(0, 0, 0, -1); born[i] = -100f; }
            block = new MaterialPropertyBlock();
            Push();
        }

        float baseCapacity = -1f, baseRecharge;

        /// <summary>Co-op: `k` times as strong as it was built (an enemy's, with more players),
        /// recharging `k` times as fast, keeping how charged it is.</summary>
        public void ScaleTo(float k)
        {
            if (baseCapacity < 0f) { baseCapacity = capacity; baseRecharge = rechargeRate; }
            float fraction = Fraction;
            capacity = baseCapacity * k;
            rechargeRate = baseRecharge * k;
            if (Strength > 0f) Strength = fraction * capacity;
        }

        /// <summary>Take a hit: returns the damage the shield couldn't stop (0 while it holds).</summary>
        public float Absorb(float damage, Vector3 point, HitKind kind)
        {
            if (!Up) return damage;
            HitsByKind[(int)kind]++;
            LastHitTime = Time.time;
            float taken = Mathf.Min(damage, Strength);
            Strength -= taken;

            // how the hit looks: (strength, size); a beam re-ripples its burning spot a few times a second
            Vector2 look = kind switch
            {
                HitKind.Projectile => new Vector2(0.6f, 0.45f),
                HitKind.Cannon => new Vector2(1.0f, 0.8f),
                HitKind.Missile => new Vector2(1.6f, 1.6f),
                _ => new Vector2(1.3f, 0.7f),
            };
            if (kind != HitKind.Beam || Time.time - lastBeamRipple > 0.12f)
            {
                if (kind == HitKind.Beam) lastBeamRipple = Time.time;
                Ripple(point, look.x, look.y);
                flash = Mathf.Max(flash, kind == HitKind.Missile ? 0.5f : kind == HitKind.Beam ? 0.25f : kind == HitKind.Cannon ? 0.18f : 0.1f);
                if (kind != HitKind.Beam || Time.time - lastSound > 0.3f)
                {
                    lastSound = Time.time;
                    SoundFX.Play(Sfx.ShieldHit, point, kind == HitKind.Projectile ? 0.5f : 1f,
                                 kind == HitKind.Missile ? 0.75f : kind == HitKind.Projectile ? 1.25f : 1f);
                }
            }
            if (Strength <= 0f) Collapse();
            return damage - taken;
        }

        float lastSound = -1f;

        void Ripple(Vector3 point, float strength, float size)
        {
            int i = nextSlot;
            nextSlot = (nextSlot + 1) % MaxHits;
            hits[i] = new Vector4(point.x, point.y, point.z, 0f);
            strengths[i] = new Vector4(strength, size, 0f, 0f);
            born[i] = Time.time;
        }

        void Collapse()
        {
            Up = false;
            Strength = 0f;
            Collapses++;
            downUntil = Time.time + downTime;
            flash = 1f;
            if (col) col.enabled = false;
            SoundFX.Play(Sfx.ShieldHit, transform.position, 1f, 0.5f);
        }

        bool offline;

        /// <summary>Switch the shield off completely (its ship destroyed) or back on at full strength.</summary>
        public void SetOffline(bool off)
        {
            offline = off;
            Up = !off;
            Strength = off ? 0f : capacity;
            flash = 0f;
            if (col) col.enabled = !off;
            for (int i = 0; i < MaxHits; i++) born[i] = -100f;
            Push();
        }

        void Update()
        {
            if (offline) { if (bubble) bubble.enabled = false; return; }
            if (!Up && Time.time >= downUntil)
            {
                Up = true;
                Strength = capacity * 0.3f;
                if (col) col.enabled = true;
                flash = 0.6f;
            }
            if (Up && Time.time - LastHitTime > rechargeDelay)
                Strength = Mathf.Min(capacity, Strength + rechargeRate * Time.deltaTime);
            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 1.6f);
            Push();
        }

        void Push()
        {
            if (bubble == null) return;
            bubble.enabled = Up || flash > 0.01f;
            for (int i = 0; i < MaxHits; i++)
            {
                float age = Time.time - born[i];
                hits[i].w = age >= 0f && age < 1.6f ? age : -1f;
            }
            bubble.GetPropertyBlock(block);
            block.SetVectorArray(HitsId, hits);
            block.SetVectorArray(StrengthId, strengths);
            block.SetFloat(FlashId, flash);
            // a faint shimmer while up, a little stronger when weakened
            block.SetFloat(IdleId, Up ? Mathf.Lerp(0.09f, 0.04f, Fraction) : 0f);
            bubble.SetPropertyBlock(block);
        }
    }
}

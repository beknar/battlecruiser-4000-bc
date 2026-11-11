using UnityEngine;

namespace CarrierGame
{
    /// <summary>A flash that grows and fades: an emissive sphere plus a point light. Explosions
    /// come from the Pool and go back to it, so each one starts over as it is switched on.
    /// Under the sea there is no fire: a blast there is a burst of bubbles rising (and, near
    /// enough the top, a dome of spray and a ring of foam thrown up on the surface).</summary>
    public class Explosion : MonoBehaviour
    {
        public float duration = 1.2f;
        public float startSize = 2f;
        public float endSize = 30f;
        public Renderer flash;
        public Light glow;
        [Tooltip("Soft round particle (alpha-blended), for a blast under the water.")]
        public Material bubbles;

        /// <summary>This one went off under the water (bubbles, no flash).</summary>
        public bool Underwater { get; private set; }
        /// <summary>Blasts under the water so far, those of them that showed fire (should be
        /// none), and the bubbles they let go (for the self-test).</summary>
        public static int UnderwaterBlasts, FireUnderwater, BubblesReleased;

        // (one set of systems for every blast under the water; they go with the level)
        static ParticleSystem bubbleFx, foamFx, sprayFx;

        float age;
        bool begun;
        Color baseColor;
        float baseIntensity;
        MaterialPropertyBlock block;
        static readonly int ColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            block = new MaterialPropertyBlock();
            if (flash != null) baseColor = flash.sharedMaterial.GetColor(ColorId);
            if (glow != null) baseIntensity = glow.intensity;
        }

        void OnEnable()
        {
            age = 0f;
            begun = false;
            Underwater = false;
            if (flash != null) flash.enabled = true;
            if (glow != null) glow.enabled = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            UnderwaterBlasts = FireUnderwater = BubblesReleased = 0;
            bubbleFx = foamFx = sprayFx = null;
        }

        /// <summary>Its first frame (as Start would be: whoever set it off has scaled it by now).</summary>
        void Begin()
        {
            begun = true;
            // big blasts rumble, small ones pop; scaled-up blasts (a cruiser dying) sound deeper
            float scale = transform.localScale.x;
            bool big = endSize * scale >= 50f;
            float pitch = Mathf.Clamp(1.1f - 0.08f * scale, 0.65f, 1.1f);
            Underwater = Sea.Depth(transform.position) > 1f;
            if (Underwater)
            {
                // no fire under the sea: bubbles, and a duller, deeper thud
                if (flash != null) flash.enabled = false;
                if (glow != null) glow.enabled = false;
                UnderwaterBlasts++;
                Bubble(endSize * scale);
                SoundFX.Play(big ? Sfx.ExplosionBig : Sfx.ExplosionSmall, transform.position, 0.7f, pitch * 0.55f);
                return;
            }
            Apply(0f);
            SoundFX.Play(big ? Sfx.ExplosionBig : Sfx.ExplosionSmall, transform.position, 1f, pitch);
        }

        /// <summary>A blast `size` across, under the water.</summary>
        void Bubble(float size)
        {
            if (bubbleFx == null)
            {
                bubbleFx = WaterFx.Make("Blast Bubbles", bubbles, 4000, new Color(0.84f, 0.95f, 1f, 0.8f), 2f, 4f, 1f, 4f, -0.4f, grow: 1.8f, drag: 1.5f);
                foamFx = WaterFx.Make("Blast Foam", bubbles, 1500, new Color(0.94f, 0.98f, 1f, 0.8f), 5f, 8f, 8f, 16f, 0f, flat: true, grow: 3f);
                sprayFx = WaterFx.Make("Blast Spray", bubbles, 1500, new Color(0.93f, 0.97f, 1f, 0.9f), 1.4f, 3f, 4f, 14f, 1.6f);
            }
            if (bubbles != null && bubbleFx.TryGetComponent(out ParticleSystemRenderer br) && br.sharedMaterial != bubbles)
                foreach (var ps in new[] { bubbleFx, foamFx, sprayFx }) ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = bubbles;
            Vector3 at = transform.position;
            float sea = Sea.Level;
            // the burst: thrown out all round, slowed by the water, then rising; none flung up
            // through the surface, and each one's life ends before it would rise to it
            int n = Mathf.Clamp(Mathf.RoundToInt(40f + size * 5f), 40, 700);
            float r = size * 0.15f;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = at + Random.insideUnitSphere * r;
                float under = sea - p.y;
                if (under < 0.5f) continue;
                Vector3 v = Random.onUnitSphere * Random.Range(0.3f, 1f) * size * 0.6f + Vector3.up * Random.Range(1f, 4f);
                // (the water's drag, 1.5, stops it within speed / 1.5; then it rises at about
                // 2.6 m/s, gravity -0.4 against the drag)
                float coast = Mathf.Max(0f, v.y) / 1.5f;
                if (coast > under * 0.7f) { v.y *= under * 0.7f / coast; coast = under * 0.7f; }
                float life = Mathf.Min(Random.Range(2f, 4f), 0.3f + (under - coast) / 2.6f);
                WaterFx.Emit(bubbleFx, p, v, Random.Range(1f, 2f) + size * Random.Range(0.02f, 0.06f), life);
                BubblesReleased++;
            }
            // near enough the top, the water above it heaves: spray and a ring of foam (less of
            // it the deeper the blast)
            float depth = sea - at.y;
            float show = 1f - Mathf.InverseLerp(size * 0.3f, size * 1.5f + 20f, depth);
            if (show <= 0f) return;
            Vector3 top = new Vector3(at.x, sea + 0.3f, at.z);
            int m = Mathf.RoundToInt((30f + size * 3f) * show);
            for (int i = 0; i < m; i++)
            {
                Vector2 d = Random.insideUnitCircle.normalized;
                Vector3 away = new Vector3(d.x, 0f, d.y);
                Vector3 p = top + away * Random.Range(0f, size * 0.35f);
                WaterFx.Emit(foamFx, p, away * Random.Range(4f, 10f + size * 0.3f), Random.Range(6f, 12f) + size * 0.15f, Random.Range(5f, 8f), show);
                if (i % 2 == 0)
                    WaterFx.Emit(sprayFx, p + Vector3.up, away * Random.Range(2f, 6f + size * 0.1f) + Vector3.up * Random.Range(8f, 14f + size * 0.6f) * show,
                                 Random.Range(4f, 8f) + size * 0.1f, Random.Range(1.4f, 2.8f), show);
            }
        }

        void Update()
        {
            if (!begun) Begin();
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / duration);
            if (!Underwater) Apply(t);
            else if (flash != null && flash.enabled) FireUnderwater++;
            if (age >= duration) Pool.Give(gameObject);
        }

        void Apply(float t)
        {
            float s = Mathf.Lerp(startSize, endSize, 1f - (1f - t) * (1f - t)) * transform.localScale.x;
            if (flash != null)
            {
                flash.transform.localScale = Vector3.one * Mathf.Lerp(startSize, endSize, 1f - (1f - t) * (1f - t));
                block.SetColor(ColorId, baseColor * (1f - t));
                flash.SetPropertyBlock(block);
            }
            if (glow != null)
            {
                glow.intensity = baseIntensity * (1f - t);
                glow.range = s * 3f;
            }
        }
    }
}

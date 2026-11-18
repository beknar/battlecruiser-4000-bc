using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The cloud a nuclear burst raises on a world with air: a fireball that glows and rises, a stem
    /// drawn up under it, a cap that spreads as it climbs, and a skirt of dust thrown out across the
    /// ground. Kilometres high, so it can be seen from orbit; it drifts apart and fades after about a
    /// minute. Kept on the body (it turns and moves with it), made of camera-facing puffs of smoke.
    /// </summary>
    public class MushroomCloud : MonoBehaviour
    {
        /// <summary>How many have been raised (the self-tests), and the last one.</summary>
        public static int Raised { get; private set; }
        public static MushroomCloud Last { get; private set; }

        /// <summary>How high it will climb (m), how high its cap is now, and its age (s).</summary>
        public float Height => height;
        public float CapHeight => height * Rise(age);
        public float Age => age;

        const float Life = 75f, FadeFrom = 45f;

        enum Part { Stem, Ring, Top, Skirt }
        class Puff
        {
            public Transform t;
            public Renderer r;
            public Part part;
            public float angle, frac, size, roll, spin, wobble;
        }

        WarpPoint body;
        Vector3 local;                 // ground zero, in the body's own frame
        float height, capR, age;
        readonly List<Puff> puffs = new List<Puff>();
        Transform fire;
        Renderer fireR;

        static Material smoke;
        static Texture2D puffTex;
        static MaterialPropertyBlock block;

        /// <summary>Raise one at `at` on `body`, for a burst whose blast reaches `radius` metres.</summary>
        public static MushroomCloud Rise(WarpPoint body, Vector3 at, float radius)
        {
            if (body == null || StrikeFx.glowMaterial == null) return null;
            var go = new GameObject("Mushroom cloud");
            var m = go.AddComponent<MushroomCloud>();
            m.body = body;
            m.local = body.transform.InverseTransformPoint(at);
            m.height = Mathf.Clamp(radius * 8f, 14000f, 24000f);
            m.capR = m.height * 0.3f;
            m.Build();
            m.Place();
            Raised++;
            Last = m;
            return m;
        }

        // the cap climbs fast, then slows; it spreads as it goes
        static float Rise(float t) => 1f - Mathf.Exp(-t / 7f);
        static float Spread(float t) => 1f - Mathf.Exp(-t / 10f);

        void Build()
        {
            var rng = new System.Random(4000);     // (its own: the game's Random is left as it was)
            float R() => (float)rng.NextDouble();
            void Add(Part part, int n)
            {
                for (int i = 0; i < n; i++)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(q.GetComponent<Collider>());
                    q.name = "Puff";
                    q.transform.SetParent(transform, false);
                    var r = q.GetComponent<MeshRenderer>();
                    r.sharedMaterial = Smoke();
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    puffs.Add(new Puff
                    {
                        t = q.transform, r = r, part = part,
                        angle = (i + R() * 0.6f) / n * Mathf.PI * 2f,
                        frac = (i + 0.5f) / n,
                        size = 0.8f + R() * 0.45f,
                        roll = R() * 360f,
                        spin = (R() - 0.5f) * 8f,
                        wobble = R() * 10f,
                    });
                }
            }
            Add(Part.Skirt, 14);
            Add(Part.Stem, 9);
            Add(Part.Ring, 16);
            Add(Part.Top, 8);
            // the fireball's glow, inside the cap while it is hot
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(g.GetComponent<Collider>());
            g.name = "Fireball glow";
            g.transform.SetParent(transform, false);
            fireR = g.GetComponent<MeshRenderer>();
            fireR.sharedMaterial = StrikeFx.glowMaterial;
            fireR.shadowCastingMode = ShadowCastingMode.Off;
            fire = g.transform;
        }

        /// <summary>Where ground zero is now (the body moves and turns), its up out from the body.</summary>
        void Place()
        {
            if (body == null) return;
            Vector3 p = body.transform.TransformPoint(local);
            Vector3 up = (p - body.Position).normalized;
            transform.SetPositionAndRotation(p, Quaternion.FromToRotation(Vector3.up, up));
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age > Life || body == null) { Destroy(gameObject); return; }
            Place();
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            float rise = Rise(age), spread = Spread(age), dust = 1f - Mathf.Exp(-age / 4f);
            float h = height * rise;
            float capRadius = capR * (0.35f + 0.65f * spread);
            float alpha = Mathf.Clamp01(age / 0.8f) * (age < FadeFrom ? 1f : 1f - (age - FadeFrom) / (Life - FadeFrom));
            float heat = Mathf.Exp(-age / 5f);
            var lit = new Color(0.97f, 0.95f, 0.92f);      // the sunlit smoke (pale: it stands out from the ground)
            var stem = new Color(0.8f, 0.77f, 0.74f);
            var sand = new Color(0.78f, 0.66f, 0.5f);       // dust off the ground
            var hot = new Color(1.8f, 0.95f, 0.45f);        // glowing (HDR: it blooms)
            Vector3 camUp = cam.transform.up, camPos = cam.transform.position;

            foreach (var p in puffs)
            {
                float w = Mathf.Sin(age * 0.4f + p.wobble);
                Vector3 at;
                float size;
                Color c;
                float a = alpha;
                switch (p.part)
                {
                    case Part.Stem:
                    {
                        float r = capR * 0.12f * (0.5f + 0.5f * p.frac);
                        at = new Vector3(Mathf.Cos(p.angle) * r, h * p.frac * 0.92f, Mathf.Sin(p.angle) * r);
                        size = capR * 0.55f * p.size * (0.6f + 0.4f * spread);
                        c = Color.Lerp(stem, hot, heat * (0.4f + 0.6f * p.frac));
                        break;
                    }
                    case Part.Ring:
                    {
                        float r = capRadius * 0.78f;
                        at = new Vector3(Mathf.Cos(p.angle) * r, h + capR * 0.08f * w, Mathf.Sin(p.angle) * r);
                        size = capR * 0.85f * p.size * (0.6f + 0.4f * spread);
                        c = Color.Lerp(lit, hot, heat);
                        break;
                    }
                    case Part.Top:
                    {
                        float r = capRadius * 0.45f * p.frac;
                        at = new Vector3(Mathf.Cos(p.angle) * r, h + capR * (0.3f + 0.1f * w), Mathf.Sin(p.angle) * r);
                        size = capR * 0.9f * p.size * (0.6f + 0.4f * spread);
                        c = Color.Lerp(lit, hot, heat);
                        break;
                    }
                    default:
                    {
                        float r = capR * (0.2f + 1.5f * dust);
                        at = new Vector3(Mathf.Cos(p.angle) * r, capR * 0.1f, Mathf.Sin(p.angle) * r);
                        size = capR * 0.6f * p.size * (0.5f + 0.5f * dust);
                        c = sand;
                        a *= 0.75f * Mathf.Clamp01(1f - (age - 20f) / 25f);
                        break;
                    }
                }
                p.t.localPosition = at;
                p.t.localScale = Vector3.one * size;
                p.t.rotation = Quaternion.LookRotation(p.t.position - camPos, camUp) * Quaternion.Euler(0f, 0f, p.roll + p.spin * age);
                c.a = a;
                Tint(p.r, c);
            }
            // the fireball: white hot in the first moments, orange as it rises, out in about 15 s
            fire.localPosition = new Vector3(0f, h * 0.97f, 0f);
            fire.localScale = Vector3.one * capR * 2.6f * (0.5f + 0.5f * spread);
            fire.rotation = Quaternion.LookRotation(fire.position - camPos, camUp);
            var fc = Color.Lerp(new Color(1f, 0.45f, 0.15f), new Color(1f, 0.95f, 0.8f), heat) * (heat * 2.2f);
            fc.a = 1f;
            Tint(fireR, fc);
            fireR.enabled = heat > 0.02f;
        }

        static void Tint(Renderer r, Color c)
        {
            if (block == null) block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", c);
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }

        /// <summary>The smoke: the strikes' glow material made alpha-blended, with a soft, lumpy puff.</summary>
        static Material Smoke()
        {
            if (smoke != null) return smoke;
            smoke = new Material(StrikeFx.glowMaterial) { name = "FX_NukeSmoke" };
            smoke.SetFloat("_Blend", 0f);
            smoke.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            smoke.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            smoke.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            smoke.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            smoke.SetFloat("_ZWrite", 0f);
            smoke.SetTexture("_BaseMap", PuffTexture());
            smoke.SetTexture("_MainTex", PuffTexture());
            smoke.SetColor("_BaseColor", Color.white);
            smoke.renderQueue = (int)RenderQueue.Transparent;
            return smoke;
        }

        static Texture2D PuffTexture()
        {
            if (puffTex != null) return puffTex;
            const int n = 128;
            puffTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "NukePuff", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v);
                    // a soft disc, its edge broken up by lumps of noise; lighter towards the middle
                    float noise = 0.55f * Mathf.PerlinNoise(x * 0.06f + 3.1f, y * 0.06f + 7.7f)
                                + 0.3f * Mathf.PerlinNoise(x * 0.14f + 11.3f, y * 0.14f + 1.9f)
                                + 0.15f * Mathf.PerlinNoise(x * 0.3f + 5.2f, y * 0.3f + 13.4f);
                    float edge = Mathf.Clamp01((1f - d) * 2.2f + (noise - 0.5f) * 1.2f);
                    float a = edge * edge * (3f - 2f * edge);
                    byte shade = (byte)Mathf.Clamp(255f * (0.78f + 0.22f * noise - 0.1f * d), 0f, 255f);
                    px[y * n + x] = new Color32(shade, shade, shade, (byte)(a * 255f));
                }
            puffTex.SetPixels32(px);
            puffTex.Apply(true, true);
            return puffTex;
        }
    }
}

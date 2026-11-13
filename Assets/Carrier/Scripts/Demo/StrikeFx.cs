using System;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// How a strike from orbit looks: a kinetic rod or a mass-driver slug streaking down (a white
    /// point with a long trail, glowing hotter in the air), an ion cannon's beam from a satellite
    /// to the ground, and where it lands a flash that swells to kilometres across and fades, with
    /// a fireball. Seen from hundreds of kilometres up, so each keeps a size it can be seen at
    /// from the camera. The glow materials are the warp drive's.
    /// </summary>
    public class StrikeFx : MonoBehaviour
    {
        enum Kind { Rod, Beam, Flash }
        Kind kind;
        Func<Vector3> from, to;
        float seconds, age;
        Action landed;
        Transform core;
        LineRenderer line;
        Light glow;
        Color color;
        float size;
        Vector3 start;

        /// <summary>The strikes' materials (KineticStrike carries them from the builder): a soft
        /// glow for the sprites, a beam for the beams and streaks, a ring for the shock wave.</summary>
        public static Material glowMaterial, beamMaterial, ringMaterial;
        static Material Glow => glowMaterial;
        static Material Streak => beamMaterial;
        Transform ring;
        LineRenderer streak;

        /// <summary>The camera's distance to a point: what is seen from far is drawn bigger.</summary>
        static float Far(Vector3 p) => Camera.main != null ? Vector3.Distance(Camera.main.transform.position, p) : 1000f;

        /// <summary>A rod (or slug) from `from` to wherever `to` says the target is now, in `seconds`; then `landed`.</summary>
        public static StrikeFx Rod(Vector3 from, Func<Vector3> to, float seconds, Color color, Action landed)
        {
            var fx = Make("Strike rod", Kind.Rod, color);
            fx.start = from;
            fx.to = to;
            fx.seconds = Mathf.Max(0.5f, seconds);
            fx.landed = landed;
            fx.core = Ball(fx.transform, color);
            fx.streak = Line(fx.gameObject, color);
            fx.transform.position = from;
            return fx;
        }

        /// <summary>A beam from `from` to `to` for `seconds` (the ion cannon); `landed` when it reaches the ground.</summary>
        public static StrikeFx Beam(Func<Vector3> from, Func<Vector3> to, float seconds, Color color, Action landed)
        {
            var fx = Make("Ion beam", Kind.Beam, color);
            fx.from = from;
            fx.to = to;
            fx.seconds = seconds;
            fx.landed = landed;
            fx.line = Line(fx.gameObject, color);
            fx.core = Ball(fx.transform, color);
            return fx;
        }

        /// <summary>Where a strike lands: a flash `size` metres across at its widest, and a fireball.</summary>
        public static void Flash(Vector3 at, float size, Color color, Explosion fireball)
        {
            var fx = Make("Strike flash", Kind.Flash, color);
            fx.transform.position = at;
            fx.size = size;
            fx.seconds = 4f;
            fx.core = Ball(fx.transform, color);
            fx.ring = RingAt(fx.transform, at, color);
            fx.glow = fx.gameObject.AddComponent<Light>();
            fx.glow.type = LightType.Point;
            fx.glow.color = color;
            fx.glow.range = size * 6f;
            fx.glow.intensity = 8f;
            if (fireball != null)
            {
                var e = Pool.Take(fireball, at, Quaternion.identity);
                e.transform.localScale = fireball.transform.localScale * Mathf.Clamp(size / 60f, 4f, 40f);
            }
            SoundFX.Play(Sfx.ExplosionBig, at, 1f, 0.55f);
        }

        static StrikeFx Make(string name, Kind kind, Color color)
        {
            var go = new GameObject(name);
            var fx = go.AddComponent<StrikeFx>();
            fx.kind = kind;
            fx.color = color;
            return fx;
        }

        static LineRenderer Line(GameObject go, Color color)
        {
            var l = go.AddComponent<LineRenderer>();
            l.sharedMaterial = Streak;
            l.positionCount = 2;
            l.textureMode = LineTextureMode.Stretch;
            l.startColor = l.endColor = new Color(color.r * 2.5f, color.g * 2.5f, color.b * 2.5f, 1f);
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            l.useWorldSpace = true;
            return l;
        }

        /// <summary>A shock ring lying on the ground where a strike lands (face up, out from the body).</summary>
        static Transform RingAt(Transform parent, Vector3 at, Color color)
        {
            if (ringMaterial == null) return null;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            var body = PlanetFlight.Instance != null ? PlanetFlight.Instance.Body : null;
            Vector3 up = body != null ? (at - body.Position).normalized : Vector3.up;
            q.transform.SetPositionAndRotation(at + up * 20f, Quaternion.LookRotation(-up));
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = ringMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Tint(r, color);
            return q.transform;
        }

        static void Tint(Renderer r, Color c)
        {
            c = new Color(c.r * 2.5f, c.g * 2.5f, c.b * 2.5f, c.a);   // (HDR: bright enough to bloom)
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", c);
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }

        /// <summary>A glowing sprite (a quad that faces the camera).</summary>
        static Transform Ball(Transform parent, Color color)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(b.GetComponent<Collider>());
            b.transform.SetParent(parent, false);
            var r = b.GetComponent<MeshRenderer>();
            r.sharedMaterial = Glow;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Tint(r, color);
            return b.transform;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null && core != null) core.rotation = Quaternion.LookRotation(core.position - cam.transform.position, cam.transform.up);
        }

        void Update()
        {
            age += Time.deltaTime;
            float k = Mathf.Clamp01(age / seconds);
            switch (kind)
            {
                case Kind.Rod:
                {
                    Vector3 end = to();
                    // (falling faster as it goes, as a rod from orbit does)
                    float s0 = k * k * (0.6f + 0.4f * k), sBack = Mathf.Max(0f, s0 - 0.12f);
                    transform.position = Vector3.Lerp(start, end, s0);
                    float far = Far(transform.position);
                    core.localScale = Vector3.one * Mathf.Max(40f, far * 0.012f);
                    streak.SetPosition(0, Vector3.Lerp(start, end, sBack));
                    streak.SetPosition(1, transform.position);
                    streak.widthMultiplier = Mathf.Max(20f, far * 0.006f);
                    if (k >= 1f) { landed?.Invoke(); Done(); }
                    break;
                }
                case Kind.Beam:
                {
                    Vector3 a = from(), b = to();
                    line.SetPosition(0, a);
                    line.SetPosition(1, b);
                    float far = Mathf.Min(Far(a), Far(b));
                    float pulse = 0.75f + 0.25f * Mathf.Sin(age * 40f);
                    line.widthMultiplier = Mathf.Max(80f, far * 0.01f) * pulse * (k < 0.85f ? 1f : (1f - k) / 0.15f);
                    core.position = b;
                    core.localScale = Vector3.one * Mathf.Max(400f, Far(b) * 0.04f) * pulse;
                    if (landed != null && age > 0.6f) { landed(); landed = null; }
                    if (k >= 1f) Done();
                    break;
                }
                default:
                {
                    float far = Far(transform.position);
                    float grow = Mathf.Sqrt(k);
                    core.localScale = Vector3.one * Mathf.Max(size * (0.4f + grow), far * 0.05f * (1f - k));
                    if (glow != null) glow.intensity = 8f * (1f - k);
                    var c = color * (1f - k * k);
                    Tint(core.GetComponent<MeshRenderer>(), c);
                    if (ring != null)
                    {
                        ring.localScale = Vector3.one * size * (0.3f + 2.2f * grow);
                        Tint(ring.GetComponent<MeshRenderer>(), color * (1f - k));
                    }
                    if (k >= 1f) Done();
                    break;
                }
            }
        }

        void Done() => Destroy(gameObject);
    }
}

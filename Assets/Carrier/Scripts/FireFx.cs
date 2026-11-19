using UnityEngine;
using UnityEngine.VFX;

namespace CarrierGame
{
    /// <summary>A fire burning on something (a wreck, a settlement's rubble), or the smoke of one
    /// burnt out: the game's own copies of an Asset Store pack's VFX Graph effects, made by
    /// CarrierBuilder.FireFx in the git-ignored Kit and found through FireKit. A clone without the
    /// pack has none, and neither has a device without compute shaders (a web game on WebGL 2), a
    /// game without graphics or the battle simulator.
    ///
    /// A fire stands upright whatever it is on (a wreck's pieces tumble), flickers its light if it
    /// has one, and isn't drawn beyond `MaxDistance` of the camera (the pack's own cut-off, which
    /// kills its particles a few hundred metres off at the game's sizes, is set out of the way
    /// by the builder). Kindled for a time, it dies down at its end (its flames stop, its smoke
    /// thins out) and goes, leaving `then` (its smoke) burning on if it has one.</summary>
    public class FireFx : MonoBehaviour
    {
        [Tooltip("Its light's intensity, flickering (0: none).")]
        public float lightIntensity;
        [Tooltip("Its light's range at the pack's own size (m): it grows with the fire.")]
        public float lightRange = 12f;
        [Tooltip("Seconds its last flames and smoke take to clear once it is put out.")]
        public float linger = 10f;

        /// <summary>Farther than this from the camera a fire isn't drawn (metres).</summary>
        public const float MaxDistance = 9000f;
        /// <summary>Fires at once: past this, no more are kindled.</summary>
        public const int MaxFires = 40;

        /// <summary>Fires burning now.</summary>
        public static int Burning { get; private set; }
        /// <summary>How many have been kindled (for the self-test).</summary>
        public static int Kindled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Kindled = 0; Burning = 0; }

        /// <summary>What burns on in its place once it has burnt out (its smoke), if anything.</summary>
        [System.NonSerialized] public FireFx then;

        VisualEffect[] fx;
        Light lit;
        float scale = 1f, stopAt = float.PositiveInfinity, goneAt = float.PositiveInfinity, seed;
        bool shown = true;

        /// <summary>Its particles alive now (for the self-test: a fire that draws nothing has none).</summary>
        public int Particles
        {
            get { int n = 0; foreach (var v in fx) if (v != null) n += v.aliveParticleCount; return n; }
        }

        /// <summary>Whether fires can be shown here at all.</summary>
        public static bool Supported => !Application.isBatchMode && SystemInfo.supportsComputeShaders;

        /// <summary>Kindle `prefab` at a place, on `parent` (moving with it), `scale` times its
        /// own size, burning `seconds` (for ever if not given). Nothing (null) without the
        /// prefab, under the water, where fires aren't supported, or with too many burning.</summary>
        public static FireFx Kindle(FireFx prefab, Transform parent, Vector3 position, float scale, float seconds = float.PositiveInfinity)
        {
            if (prefab == null || !Supported || Burning >= MaxFires || Sea.Depth(position) > 0.5f) return null;
            var f = Instantiate(prefab, position, Quaternion.identity, parent);
            Vector3 s = parent != null ? parent.lossyScale : Vector3.one;
            f.transform.localScale = new Vector3(scale / Mathf.Max(1e-4f, s.x), scale / Mathf.Max(1e-4f, s.y), scale / Mathf.Max(1e-4f, s.z));
            f.scale = scale;
            if (f.lit != null) f.lit.range = f.lightRange * scale;
            f.stopAt = Time.time + seconds;
            return f;
        }

        void Awake()
        {
            fx = GetComponentsInChildren<VisualEffect>(true);
            lit = GetComponentInChildren<Light>(true);
            seed = Random.value * 100f;
        }

        void OnEnable() { Burning++; Kindled++; }
        void OnDisable() => Burning--;

        /// <summary>Put it out: its flames stop, and it goes once they and its smoke have cleared.</summary>
        public void PutOut()
        {
            if (goneAt < float.PositiveInfinity) return;
            foreach (var v in fx) if (v != null) v.Stop();
            goneAt = Time.time + linger;
        }

        void LateUpdate()
        {
            transform.rotation = Quaternion.identity;
            if (Time.time >= stopAt)
            {
                stopAt = float.PositiveInfinity;
                PutOut();
                if (then != null) Kindle(then, transform.parent, transform.position, scale);
            }
            if (Time.time >= goneAt) { Destroy(gameObject); return; }
            var cam = Camera.main;
            float d = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 0f;
            bool show = d < MaxDistance;
            if (show != shown)
            {
                shown = show;
                foreach (var v in fx) if (v != null) v.enabled = show;
                if (lit != null) lit.enabled = show;
            }
            if (!show) return;
            if (lit != null)
            {
                float k = goneAt < float.PositiveInfinity ? Mathf.Clamp01((goneAt - Time.time) / linger - 0.5f) * 2f : 1f;
                lit.intensity = lightIntensity * k * (0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 4f, seed));
            }
        }
    }
}

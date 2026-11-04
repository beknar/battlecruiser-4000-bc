using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A small manoeuvring thruster's flare: an exhaust plume and a light that grow with `thrust`
    /// (0 = off, 1 = full), with a slight flicker. Fully hidden when idle. The ship sets `thrust`
    /// every frame; the flare eases toward it so it lights up and dies away quickly but not instantly.
    /// </summary>
    public class ThrusterFlare : MonoBehaviour
    {
        [Range(0f, 1f)] public float thrust;
        public Transform plume;
        public Light glow;
        public float maxIntensity = 300f;
        [Tooltip("How quickly the flare follows the thrust (per second).")]
        public float response = 12f;

        float shown, seed;
        Vector3 plumeBase = Vector3.one;
        Renderer[] renderers;

        void Awake()
        {
            seed = Random.value * 100f;   // each flare flickers differently
            if (plume) plumeBase = plume.localScale;
            renderers = plume ? plume.GetComponentsInChildren<Renderer>() : new Renderer[0];
            Apply(0f);
        }

        void Update()
        {
            shown = Mathf.MoveTowards(shown, thrust, response * Time.deltaTime);
            Apply(shown);
        }

        /// <summary>Show the flare at a given strength now (also used for editor screenshots).</summary>
        public void Apply(float k)
        {
            bool on = k > 0.02f;
            if (renderers == null) renderers = plume ? plume.GetComponentsInChildren<Renderer>() : new Renderer[0];
            foreach (var r in renderers) if (r && r.enabled != on) r.enabled = on;
            if (glow && glow.enabled != on) glow.enabled = on;
            if (!on) return;
            float flicker = Application.isPlaying ? 0.9f + 0.1f * Mathf.PerlinNoise(Time.time * 23f, seed) : 1f;
            if (plume)
            {
                float len = Mathf.Lerp(0.25f, 1f, k) * flicker;
                plume.localScale = new Vector3(plumeBase.x * Mathf.Lerp(0.6f, 1f, k), plumeBase.y * Mathf.Lerp(0.6f, 1f, k), plumeBase.z * len);
            }
            if (glow) glow.intensity = maxIntensity * k * flicker;
        }
    }
}

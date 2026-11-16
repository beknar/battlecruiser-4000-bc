using UnityEngine;

namespace CarrierGame
{
    /// <summary>Scales an engine's exhaust plume and light with throttle, with a slight flicker.
    /// With no thrust (throttle at nothing) the plume and its light go out altogether. The ship
    /// sets `throttle` every frame (CarrierController from its throttle, CombatPilot from its
    /// speed); the plume eases toward it.</summary>
    public class EngineThrust : MonoBehaviour
    {
        [Range(0f, 1f)] public float throttle = 0.2f;
        public Transform plume;
        public Light glow;
        public float minLength = 0.4f;
        public float maxLength = 1.6f;
        public float maxIntensity = 40f;

        /// <summary>Below this the engine is off: no plume, no light.</summary>
        public const float Off = 0.02f;

        Vector3 plumeBase;
        float shown, seed;
        Renderer[] renderers;
        bool lit = true, recheck = true;

        /// <summary>The plume is showing (for the self-test).</summary>
        public bool Lit => lit;

        void Awake()
        {
            if (plume) plumeBase = plume.localScale;
            seed = Random.value * 10f;
            renderers = plume ? plume.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            shown = throttle;
        }

        // (switched back on, by a respawn that lit every renderer again: look afresh)
        void OnEnable() => recheck = true;

        void Update()
        {
            shown = Mathf.MoveTowards(shown, throttle, Time.deltaTime * 0.5f);
            // (out at once when the thrust is cut: an engine doesn't glow on after it)
            if (throttle < Off) shown = Mathf.Min(shown, throttle);
            bool on = shown >= Off;
            if (on != lit || recheck)
            {
                lit = on;
                recheck = false;
                foreach (var r in renderers) if (r) r.enabled = on;
                if (glow) glow.enabled = on;
            }
            if (!on) return;
            float flicker = 1f + (Mathf.PerlinNoise(seed, Time.time * 8f) - 0.5f) * 0.12f;
            float len = Mathf.Lerp(minLength, maxLength, shown) * flicker;
            if (plume) plume.localScale = new Vector3(plumeBase.x, plumeBase.y, plumeBase.z * len);
            if (glow) glow.intensity = maxIntensity * (0.2f + shown) * flicker;
        }
    }
}

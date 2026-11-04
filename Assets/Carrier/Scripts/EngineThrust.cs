using UnityEngine;

namespace CarrierGame
{
    /// <summary>Scales an engine's exhaust plume and light with throttle, with a slight flicker.</summary>
    public class EngineThrust : MonoBehaviour
    {
        [Range(0f, 1f)] public float throttle = 0.2f;
        public Transform plume;
        public Light glow;
        public float minLength = 0.4f;
        public float maxLength = 1.6f;
        public float maxIntensity = 40f;

        Vector3 plumeBase;
        float shown, seed;

        void Awake()
        {
            if (plume) plumeBase = plume.localScale;
            seed = Random.value * 10f;
        }

        void Update()
        {
            shown = Mathf.MoveTowards(shown, throttle, Time.deltaTime * 0.5f);
            float flicker = 1f + (Mathf.PerlinNoise(seed, Time.time * 8f) - 0.5f) * 0.12f;
            float len = Mathf.Lerp(minLength, maxLength, shown) * flicker;
            if (plume) plume.localScale = new Vector3(plumeBase.x, plumeBase.y, plumeBase.z * len);
            if (glow) glow.intensity = maxIntensity * (0.2f + shown) * flicker;
        }
    }
}

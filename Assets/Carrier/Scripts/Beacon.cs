using UnityEngine;

namespace CarrierGame
{
    /// <summary>A navigation light that blinks (a renderer, and its light if it has one).</summary>
    public class Beacon : MonoBehaviour
    {
        public float period = 1.6f;
        [Range(0f, 1f)] public float onFraction = 0.25f;
        public float phase;

        Renderer rend;
        Light lamp;

        void Awake()
        {
            rend = GetComponent<Renderer>();
            lamp = GetComponentInChildren<Light>();
        }

        void Update()
        {
            bool on = Mathf.Repeat(Time.time / Mathf.Max(0.05f, period) + phase, 1f) < onFraction;
            if (rend != null && rend.enabled != on) rend.enabled = on;
            if (lamp != null && lamp.enabled != on) lamp.enabled = on;
        }
    }
}

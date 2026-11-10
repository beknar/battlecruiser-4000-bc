using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarrierGame
{
    /// <summary>A warning light that blinks.</summary>
    public class Blink : MonoBehaviour
    {
        UnityEngine.Light l;
        Renderer r;
        float phase;
        void Start() { l = GetComponentInChildren<UnityEngine.Light>(); r = GetComponent<Renderer>(); phase = Random.value; }
        void Update()
        {
            bool on = Mathf.Repeat(Time.time * 0.8f + phase, 1f) < 0.5f;
            if (l != null) l.enabled = on;
            if (r != null) r.enabled = on;
        }
    }
}

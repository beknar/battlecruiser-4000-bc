using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Co-op: on an enemy whose real self is in the host's game. Its own flying and shooting are
    /// switched off; it goes where the host says, and this tells guns and missiles how fast it
    /// is going so they can lead it. Its engines burn as hard as it is going (out, lying still).
    /// </summary>
    public class NetPuppet : MonoBehaviour, IVelocity
    {
        public Vector3 velocity;
        public Vector3 Velocity => velocity;

        [Tooltip("The speed (m/s) its engines are at full burn (a capital ship's attack speed).")]
        public float fullBurn = 280f;

        EngineThrust[] engines;

        void Start() => engines = GetComponentsInChildren<EngineThrust>(true);

        void LateUpdate()
        {
            if (engines == null || engines.Length == 0) return;
            float k = Mathf.Clamp01(velocity.magnitude / Mathf.Max(1f, fullBurn));
            foreach (var e in engines) if (e != null) e.throttle = k;
        }
    }
}

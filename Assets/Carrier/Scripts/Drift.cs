using UnityEngine;

namespace CarrierGame
{
    /// <summary>Slow tumble and drift for a piece of debris or a small rock.</summary>
    public class Drift : MonoBehaviour
    {
        public Vector3 spin;       // degrees per second about each local axis
        public Vector3 velocity;   // metres per second

        bool clocked;
        Quaternion start;

        /// <summary>Turn by the orbit clock from here on (a planet or moon), rather than by frame time.</summary>
        public void LockToOrbitClock()
        {
            clocked = true;
            start = transform.localRotation;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var sys = OrbitalSystem.Instance;
            if (clocked && sys != null)
            {
                double t = sys.Time;
                transform.localRotation = start * Quaternion.Euler((float)(spin.x * t % 360.0), (float)(spin.y * t % 360.0), (float)(spin.z * t % 360.0));
            }
            else transform.Rotate(spin * dt, Space.Self);
            if (velocity.sqrMagnitude > 0f) transform.position += velocity * dt;
        }
    }
}

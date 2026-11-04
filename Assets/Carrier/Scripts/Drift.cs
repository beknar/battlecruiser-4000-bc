using UnityEngine;

namespace CarrierGame
{
    /// <summary>Slow tumble and drift for a piece of debris or a small rock.</summary>
    public class Drift : MonoBehaviour
    {
        public Vector3 spin;       // degrees per second about each local axis
        public Vector3 velocity;   // metres per second

        void Update()
        {
            float dt = Time.deltaTime;
            transform.Rotate(spin * dt, Space.Self);
            if (velocity.sqrMagnitude > 0f) transform.position += velocity * dt;
        }
    }
}

using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Co-op: on an enemy whose real self is in the host's game. Its own flying and shooting are
    /// switched off; it goes where the host says, and this tells guns and missiles how fast it
    /// is going so they can lead it.
    /// </summary>
    public class NetPuppet : MonoBehaviour, IVelocity
    {
        public Vector3 velocity;
        public Vector3 Velocity => velocity;
    }
}

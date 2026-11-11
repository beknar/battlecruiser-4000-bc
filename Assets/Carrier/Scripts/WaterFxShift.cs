using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Keeps a WaterFx system's particles with the world when the planet's floating origin
    /// shifts it (PlanetSurface.ShiftBy moves objects, not particles already let go in world
    /// space): without it a wake or a cloud of bubbles would jump a whole area away.
    /// </summary>
    public class WaterFxShift : MonoBehaviour, IOriginShift
    {
        ParticleSystem ps;
        ParticleSystem.Particle[] buffer;

        public void OriginShifted(Vector3 delta)
        {
            if (ps == null && !TryGetComponent(out ps)) return;
            int max = ps.main.maxParticles;
            if (buffer == null || buffer.Length < max) buffer = new ParticleSystem.Particle[max];
            int n = ps.GetParticles(buffer);
            for (int i = 0; i < n; i++) buffer[i].position += delta;
            ps.SetParticles(buffer, n);
        }
    }
}

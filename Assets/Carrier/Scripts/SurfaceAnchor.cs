using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Sits something on the ground of the body it is built on (a moon, a planet, an asteroid):
    /// when the level starts (once the body's own shape has been made) it looks straight down
    /// toward the body's centre, moves onto the surface there and stands upright on it.
    /// </summary>
    public class SurfaceAnchor : MonoBehaviour
    {
        public Transform body;
        [Tooltip("How far into the ground to set it (m), so it doesn't stand on the tips of the bumps.")]
        public float sink = 3f;

        public bool Settled { get; private set; }

        void Start() => Settle();

        public void Settle()
        {
            if (body == null) return;
            Vector3 c = body.position;
            Vector3 up = transform.position - c;
            if (up.sqrMagnitude < 1f) return;
            up.Normalize();
            float reach = (transform.position - c).magnitude * 2f + 1000f;
            var ray = new Ray(c + up * reach, -up);
            float best = float.MaxValue;
            Vector3 hitAt = Vector3.zero;
            // only the body's own colliders (not what is built on it)
            foreach (var col in body.GetComponents<Collider>())
                if (col.enabled && col.Raycast(ray, out var hit, reach * 2f) && hit.distance < best) { best = hit.distance; hitAt = hit.point; }
            if (best == float.MaxValue) return;
            transform.rotation = Quaternion.FromToRotation(transform.up, up) * transform.rotation;
            // (on a planet's real ground, its hills and pads, where it has one: PlanetTerrain)
            transform.position = hitAt + up * (PlanetTerrain.GroundAt(body, hitAt) - sink);
            Settled = true;
        }
    }
}

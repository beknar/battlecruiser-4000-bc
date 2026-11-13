using UnityEngine;

namespace CarrierGame
{
    /// <summary>A structure built on a body's mean surface (a frame at the body's centre, the
    /// structure `radius` out along the frame's up: a launcher, an elevator's platform), raised
    /// when the level starts onto the body's real ground there (PlanetTerrain's hills and seas).</summary>
    public class RaiseToGround : MonoBehaviour
    {
        public Transform body;

        void Start()
        {
            if (body == null || !body.TryGetComponent(out WarpPoint wp)) return;
            Vector3 site = body.position + transform.up * wp.bodyRadius;
            float h = PlanetTerrain.GroundAt(body, site);
            if (h != 0f) transform.position += transform.up * h;
        }
    }
}

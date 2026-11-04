using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The star at the middle of the system. Its directional light (`sun`) always shines from the
    /// star toward the camera's surroundings, so ships, moons and asteroids near the player are lit
    /// from the right direction wherever they are; planets use the star's real position instead
    /// (the Carrier/Planet shader reads it from a global). Also keeps the corona glow facing the
    /// camera.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class StarLight : MonoBehaviour
    {
        public Light sun;
        public Transform corona;
        public Color starColor = new Color(1f, 0.93f, 0.82f);
        public float starBrightness = 1.05f;

        static readonly int PosId = Shader.PropertyToID("_CarrierStarPos");
        static readonly int ColorId = Shader.PropertyToID("_CarrierStarColor");

        void LateUpdate()
        {
            var cam = Camera.main;
            Vector3 star = transform.position;
            Shader.SetGlobalVector(PosId, new Vector4(star.x, star.y, star.z, 1f));
            Shader.SetGlobalColor(ColorId, starColor * starBrightness);
            if (cam == null) return;
            Vector3 viewer = cam.transform.position;
            if (sun != null && (viewer - star).sqrMagnitude > 1f) sun.transform.rotation = Quaternion.LookRotation(viewer - star);
            if (corona != null) corona.rotation = Quaternion.LookRotation(corona.position - viewer, cam.transform.up);
        }

        void OnDisable() => Shader.SetGlobalVector(PosId, Vector4.zero);
    }
}

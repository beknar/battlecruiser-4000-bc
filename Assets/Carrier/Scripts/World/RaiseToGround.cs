using UnityEngine;

namespace CarrierGame
{
    /// <summary>A structure built on a body's mean surface (a frame at the body's centre, the
    /// structure `radius` out along the frame's up: a launcher, an elevator's platform), raised
    /// when the level starts onto the body's real ground there (PlanetTerrain's hills and seas).
    /// Not just at its middle: a launcher's track runs for kilometres over the ground, so it is
    /// raised until no point of the track is under the ground below it (a slingatron's spiral
    /// and ramp would otherwise be buried in the hills round it).</summary>
    public class RaiseToGround : MonoBehaviour
    {
        public Transform body;
        [Tooltip("How far above the ground the lowest point of the track is kept (m).")]
        public float clearance = 6f;

        /// <summary>How far it was raised (m), and how far below the ground its track would have
        /// been at worst without it (for the self-test and the reel's notes).</summary>
        public float Raised { get; private set; }
        public float WorstBuried { get; private set; }

        void Start()
        {
            if (body == null || !body.TryGetComponent(out WarpPoint wp)) return;
            Vector3 c = body.position;
            float R = wp.bodyRadius;
            Vector3 site = c + transform.up * R;
            // the ground at the middle; and under each point of a launcher's track, how far it is
            // above that point (the most of which the whole is raised by)
            float raise = PlanetTerrain.GroundAt(body, site);
            float worst = raise;
            var ls = GetComponentInChildren<LaunchSystem>();
            if (ls != null && ls.track != null)
                foreach (var p in ls.track)
                {
                    Vector3 w = ls.transform.TransformPoint(p);
                    Vector3 dir = w - c;
                    if (dir.sqrMagnitude < 1f) continue;
                    float pointHeight = dir.magnitude - R;
                    float ground = PlanetTerrain.GroundAt(body, c + dir.normalized * R);
                    worst = Mathf.Max(worst, ground - pointHeight);
                    raise = Mathf.Max(raise, ground - pointHeight + clearance);
                }
            WorstBuried = worst;
            Raised = raise;
            if (raise != 0f) transform.position += transform.up * raise;
        }
    }
}

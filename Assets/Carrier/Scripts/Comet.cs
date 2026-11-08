using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A comet: an icy nucleus inside a glowing coma, with two tails, as real comets have. The
    /// ion (plasma) tail is blue, thin and dead straight, blown directly away from the star by
    /// the solar wind. The dust tail is broader and yellow-white, and curves back along the
    /// comet's path, because the dust is pushed more gently by sunlight and lags behind. A trail
    /// of debris (ice and grit shed by the nucleus) lies along the dust tail; it can be mined.
    /// The tails are particle systems in the comet's own space: this turns them away from the
    /// star, with the dust tail's curve on the side the comet has come from.
    /// </summary>
    public class Comet : MonoBehaviour
    {
        public Transform star;
        [Tooltip("The tails (and the debris trail): their forward is turned directly away from the star.")]
        public Transform tails;
        [Tooltip("Which way the comet is moving along its orbit (world space); the dust tail curves the other way.")]
        public Vector3 orbitDirection = Vector3.forward;
        public ParticleSystem coma, ionTail, dustTail;

        /// <summary>World direction the tails point (away from the star).</summary>
        public Vector3 AntiSun
        {
            get
            {
                Vector3 s = star != null ? star.position : Vector3.zero;
                Vector3 d = transform.position - s;
                return d.sqrMagnitude > 1f ? d.normalized : Vector3.forward;
            }
        }

        /// <summary>Degrees between where the tails point and straight away from the star.</summary>
        public float TailError => tails != null ? Vector3.Angle(tails.forward, AntiSun) : 180f;

        Orbit orbit;

        void Start()
        {
            orbit = GetComponentInParent<Orbit>();
            Orient();
            foreach (var ps in new[] { coma, ionTail, dustTail })
                if (ps != null && !ps.isPlaying) ps.Play();
        }

        void LateUpdate() => Orient();

        void Orient()
        {
            if (orbit != null && orbit.kind == Orbit.Kind.Kepler)
            {
                Vector3 v = orbit.Velocity(OrbitalSystem.Instance != null ? OrbitalSystem.Instance.Time : UnityEngine.Time.timeSinceLevelLoad);
                if (v.sqrMagnitude > 1e-6f) orbitDirection = v.normalized;
            }
            if (tails != null) tails.rotation = TailRotation(transform.position, star != null ? star.position : Vector3.zero, orbitDirection);
        }

        /// <summary>The tails' rotation: forward straight away from the star, local +x on the side
        /// the dust lags to (against the direction of travel). The level builder lays the debris
        /// trail out with the same frame.</summary>
        public static Quaternion TailRotation(Vector3 comet, Vector3 star, Vector3 orbitDirection)
        {
            Vector3 fwd = comet - star;
            fwd = fwd.sqrMagnitude > 1f ? fwd.normalized : Vector3.forward;
            Vector3 lag = Vector3.ProjectOnPlane(-orbitDirection, fwd);
            if (lag.sqrMagnitude < 1e-4f) lag = Vector3.ProjectOnPlane(Vector3.up, fwd);
            Vector3 up = Vector3.Cross(fwd, lag.normalized);
            return Quaternion.LookRotation(fwd, up);
        }

        /// <summary>A point on the dust tail's curve, `along` metres out from the nucleus (in the
        /// tails' frame): it bends toward +x as the dust falls behind.</summary>
        public static Vector3 DustCurve(float along, float bend) => new Vector3(bend * along * along, 0f, along);
    }
}

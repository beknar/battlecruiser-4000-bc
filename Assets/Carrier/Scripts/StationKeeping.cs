using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Keeping station at an unstable Lagrange point (L1, L2 or L3). Left alone, something there
    /// drifts off along the line from the star, faster and faster: its distance from the point
    /// grows as e^(lambda t), lambda = 2.5 times the planet's orbital rate for L1 and L2 (the
    /// unstable root of the restricted three-body problem near a small planet), so it doubles
    /// every few minutes. When it is `tolerance` metres off, its thrusters fire and push it back
    /// past the point, and the drift starts again the other way.
    /// </summary>
    public class StationKeeping : MonoBehaviour
    {
        public Orbit orbit;
        [Tooltip("How far it may drift before a burn (m).")]
        public float tolerance = 150f;
        [Tooltip("Where it is when the level starts (m off the point).")]
        public float startDrift = 60f;
        [Tooltip("Where a burn leaves it (m on the far side of the point).")]
        public float overshoot = 20f;
        [Tooltip("The thruster plumes, shown during a burn.")]
        public Renderer[] thrusters;
        public float burnTime = 1.2f;

        /// <summary>How far it is off the point now (m, + away from the star).</summary>
        public float Drift { get; private set; }
        public int Burns { get; private set; }
        public float LargestDrift { get; private set; }
        public float Lambda { get; private set; }

        float burnUntil = -1f;

        /// <summary>Set how far off the point it has drifted (metres; for the reel, to show a burn).</summary>
        public void Nudge(float drift) => Drift = drift;

        void Start()
        {
            Drift = startDrift;
            if (orbit != null && orbit.secondary != null) Lambda = (float)(2.508 * orbit.secondary.MeanMotion);
            Show(false);
        }

        void Update()
        {
            if (orbit == null || orbit.secondary == null) return;
            float dt = Time.deltaTime;
            Drift *= Mathf.Exp(Lambda * dt);
            LargestDrift = Mathf.Max(LargestDrift, Mathf.Abs(Drift));
            if (Mathf.Abs(Drift) > tolerance)
            {
                Drift = -Mathf.Sign(Drift) * overshoot;
                Burns++;
                burnUntil = Time.time + burnTime;
                Show(true);
                SoundFX.Play(Sfx.Impact, transform.position, 0.2f, 1.6f);
            }
            else if (burnUntil > 0f && Time.time > burnUntil) { burnUntil = -1f; Show(false); }
            Vector3 radial = orbit.secondary.Offset(OrbitalSystem.Instance != null ? OrbitalSystem.Instance.Time : 0.0).normalized;
            orbit.drift = radial * Drift;
        }

        void Show(bool on)
        {
            if (thrusters == null) return;
            foreach (var r in thrusters) if (r != null) r.enabled = on;
        }
    }
}

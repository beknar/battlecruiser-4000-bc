using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The rocks gathered at a planet's L4 (or L5) point swing slowly round it, each on its own
    /// "tadpole" path: to and fro along the orbit, with a little in and out, once every libration
    /// period (the planet's period / sqrt(27 mu / 4): about two of its orbits for Jovara). The
    /// big rocks the raiders hide behind swing least.
    /// </summary>
    public class TrojanSwarm : MonoBehaviour
    {
        [Tooltip("The Lagrange point the swarm is at.")]
        public Orbit point;
        [Tooltip("The rocks (their parent).")]
        public Transform rocks;
        public float amplitude = 600f;
        public float bigRockAmplitude = 150f;
        public float bigRockRadius = 350f;

        Transform[] rock;
        Vector3[] start;
        float[] amp, phase;
        double omega;

        void Start()
        {
            if (rocks == null || point == null || point.secondary == null) { enabled = false; return; }
            int n = rocks.childCount;
            rock = new Transform[n];
            start = new Vector3[n];
            amp = new float[n];
            phase = new float[n];
            var rng = new System.Random(4242);
            for (int i = 0; i < n; i++)
            {
                rock[i] = rocks.GetChild(i);
                start[i] = rock[i].localPosition;
                bool big = rock[i].TryGetComponent(out Obstacle ob) && ob.radius > bigRockRadius;
                amp[i] = (big ? bigRockAmplitude : amplitude) * (0.3f + 0.7f * (float)rng.NextDouble());
                phase[i] = (float)(rng.NextDouble() * System.Math.PI * 2.0);
            }
            omega = point.secondary.MeanMotion * Orbit.LibrationFraction(point.secondary.massRatio);
        }

        /// <summary>One swing (s).</summary>
        public double LibrationPeriod => omega > 0.0 ? 2.0 * System.Math.PI / omega : 0.0;

        void Update()
        {
            var sys = OrbitalSystem.Instance;
            double t = sys != null ? sys.Time : Time.timeSinceLevelLoad;
            Vector3 radial = point.secondary.Offset(t).normalized;
            Vector3 normal = Vector3.Cross(point.secondary.periapsis, point.secondary.ahead).normalized;
            Vector3 along = Vector3.Cross(radial, normal).normalized;
            if (Vector3.Dot(along, point.secondary.Velocity(t)) < 0f) along = -along;
            for (int i = 0; i < rock.Length; i++)
            {
                if (rock[i] == null) continue;
                float a = (float)(omega * t) + phase[i];
                Vector3 o = along * (amp[i] * (Mathf.Sin(a) - Mathf.Sin(phase[i]))) + radial * (0.25f * amp[i] * (Mathf.Cos(a) - Mathf.Cos(phase[i])));
                rock[i].localPosition = start[i] + o;
            }
        }
    }
}

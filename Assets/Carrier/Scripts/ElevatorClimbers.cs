using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The climbers of a space elevator: cars that ride the tether up from the anchor platform
    /// to the station at the top and back down, slowing for each end and pausing there.
    /// </summary>
    public class ElevatorClimbers : MonoBehaviour
    {
        [Tooltip("The bottom and top of the climb, in this transform's space.")]
        public Vector3 bottom, top;
        public Transform[] climbers;
        public float speed = 120f;
        public float pause = 6f;

        /// <summary>Metres climbed and descended, all climbers together (for the self-test).</summary>
        public float Travelled { get; private set; }

        float[] phase;
        bool placed;

        void Start()
        {
            if (climbers == null) { enabled = false; return; }
            phase = new float[climbers.Length];
            for (int i = 0; i < climbers.Length; i++) phase[i] = i / (float)climbers.Length;
        }

        void Update()
        {
            float length = Vector3.Distance(bottom, top);
            float leg = length / Mathf.Max(1f, speed);
            float cycle = 2f * (leg + pause);
            for (int i = 0; i < climbers.Length; i++)
            {
                if (climbers[i] == null) continue;
                float t = Mathf.Repeat(Time.time / cycle + phase[i], 1f) * cycle;
                float k;
                if (t < leg) k = Mathf.SmoothStep(0f, 1f, t / leg);                   // going up
                else if (t < leg + pause) k = 1f;
                else if (t < 2f * leg + pause) k = 1f - Mathf.SmoothStep(0f, 1f, (t - leg - pause) / leg);   // coming down
                else k = 0f;
                Vector3 was = climbers[i].localPosition;
                climbers[i].localPosition = Vector3.Lerp(bottom, top, k);
                if (placed) Travelled += Vector3.Distance(was, climbers[i].localPosition);
            }
            placed = true;
        }
    }
}

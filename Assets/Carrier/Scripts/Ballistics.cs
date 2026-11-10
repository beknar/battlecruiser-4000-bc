using UnityEngine;

namespace CarrierGame
{
    /// <summary>Aiming ahead of a moving target.</summary>
    public static class Ballistics
    {
        /// <summary>
        /// Where a target at `p` moving at `v` will be when a shot fired from `origin` at `speed`
        /// gets there. The flight time is first taken to where the target is now, then worked out
        /// again `refine` times from where it will be (each pass closes in on the true meeting
        /// point); it is never more than `maxTime`. A shot with no speed aims at the target itself.
        /// </summary>
        public static Vector3 Lead(Vector3 origin, Vector3 p, Vector3 v, float speed, int refine = 0, float maxTime = float.PositiveInfinity)
        {
            if (speed <= 0f) return p;
            float t = Mathf.Min(maxTime, Vector3.Distance(origin, p) / speed);
            for (int i = 0; i < refine; i++) t = Mathf.Min(maxTime, Vector3.Distance(origin, p + v * t) / speed);
            return p + v * t;
        }
    }
}

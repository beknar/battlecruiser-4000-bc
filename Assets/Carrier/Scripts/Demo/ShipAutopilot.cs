using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Simple steering for the self-test and the demo reel: turn so the target sits abeam on one
    /// side (where the frigate's side guns bear), angling in when too far away and out when too
    /// close. Returns a rudder value from -1 to 1.
    /// </summary>
    public static class ShipAutopilot
    {
        /// <param name="side">+1 to keep the target to starboard, -1 to port.</param>
        /// <param name="standoff">Preferred range in metres.</param>
        public static float Broadside(CarrierController ship, Vector3 target, int side, float standoff)
        {
            Vector3 to = target - ship.transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < 1f) return 0f;
            float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            // too far: point up to 40 degrees toward it; too close: up to 40 degrees away
            float correction = Mathf.Clamp((dist - standoff) / standoff, -1f, 1f) * 40f;
            float want = bearing - side * (90f - correction);
            float diff = Mathf.DeltaAngle(ship.Heading, want);
            return Mathf.Clamp(diff / 15f, -1f, 1f);
        }

        /// <summary>Point the bow straight at the target (for the bow beam): rudder and pitch input.</summary>
        public static Vector2 BowOn(CarrierController ship, Vector3 target)
        {
            Vector3 to = target - ship.transform.position;
            float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float elevation = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            float rudder = Mathf.Clamp(Mathf.DeltaAngle(ship.Heading, bearing) / 10f, -1f, 1f);
            float pitch = Mathf.Clamp((elevation - ship.Pitch) / 5f, -1f, 1f);
            return new Vector2(rudder, pitch);
        }

        /// <summary>How far the bow points off the target, in degrees.</summary>
        public static float BowError(CarrierController ship, Vector3 target) =>
            Vector3.Angle(ship.transform.forward, target - ship.transform.position);
    }
}

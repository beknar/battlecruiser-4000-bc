using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// An attack submarine on a water world: it keeps between the sea floor (with room under its
    /// keel) and a little below the surface. Until its garrison is alerted it circles its patrol
    /// station; then it goes for the nearest player ship, in the water or afloat on it, closing to
    /// a stand-off and circling there at the ship's depth while its torpedoes (EnemyWeapons) fire.
    /// Built by GroundForces; its numbers are in Resources/Data/ground_units.json.
    /// </summary>
    public class Submarine : MonoBehaviour, IVelocity
    {
        public PlanetSurface surface;
        public float cruiseSpeed = 12f, attackSpeed = 26f;
        [Tooltip("Degrees a second.")]
        public float turnRate = 22f;
        public float climbRate = 6f;
        public float standoff = 800f, patrolRadius = 420f;
        [Tooltip("Room kept under the keel, and over the sail to the surface (m).")]
        public float keelClearance = 16f, surfaceClearance = 14f;

        public Vector3 Velocity { get; private set; }
        /// <summary>Going for a ship (for the self-test).</summary>
        public bool Hunting { get; private set; }
        public Transform Target => target;

        Vector3 homeLocal;
        float yaw, around, nextLook;
        EnemyWeapons weapons;
        Health health;
        Transform target;

        void Start()
        {
            homeLocal = transform.localPosition;
            yaw = transform.eulerAngles.y;
            around = Random.Range(0f, Mathf.PI * 2f);
            weapons = GetComponent<EnemyWeapons>();
            health = GetComponent<Health>();
        }

        void Update()
        {
            if (health != null && health.Dead) { Velocity = Vector3.zero; return; }
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;
            Hunting = weapons != null && weapons.weaponsFree;
            if (Hunting)
            {
                // (Retarget only changes ship with two or more about: alone, the nearest is the one)
                target = PlayerShips.Retarget(target, pos, ref nextLook);
                if (target == null) target = PlayerShips.Nearest(pos);
                if (target == null || target.position.y > Sea.Level + 40f) Hunting = false;
            }

            Vector3 goal;
            float speed;
            if (Hunting)
            {
                // close in, then circle at the stand-off, as deep as the target
                Vector3 to = target.position - pos;
                to.y = 0f;
                float d = to.magnitude;
                Vector3 dir = d > 1f ? to / d : transform.forward;
                goal = d > standoff * 1.3f ? target.position : pos + Vector3.Cross(Vector3.up, dir) * 250f + dir * (d - standoff);
                goal.y = target.position.y;
                speed = attackSpeed;
                if (weapons != null && weapons.target != target) weapons.SetTarget(target);
            }
            else
            {
                around += cruiseSpeed / patrolRadius * dt;
                Vector3 home = transform.parent != null ? transform.parent.TransformPoint(homeLocal) : homeLocal;
                goal = home + new Vector3(Mathf.Cos(around), 0f, Mathf.Sin(around)) * patrolRadius;
                goal.y = home.y;
                speed = cruiseSpeed;
            }

            // in the water: under the surface, over the floor
            float top = Sea.Here ? Sea.Level - surfaceClearance : float.PositiveInfinity;
            float bottom = surface != null ? surface.GroundAt(pos) + keelClearance : float.NegativeInfinity;
            if (bottom > top) bottom = top;
            goal.y = Mathf.Clamp(goal.y, bottom, top);

            Vector3 want = goal - pos;
            if (new Vector2(want.x, want.z).sqrMagnitude > 1f)
                yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(want.x, want.z) * Mathf.Rad2Deg, turnRate * dt);
            float vy = Mathf.Clamp(want.y * 0.4f, -climbRate, climbRate);
            Velocity = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * speed + Vector3.up * vy;
            pos += Velocity * dt;
            pos.y = Mathf.Clamp(pos.y, bottom, top);
            transform.SetPositionAndRotation(pos, Quaternion.Euler(-vy * 2.5f, yaw, 0f));
        }
    }
}

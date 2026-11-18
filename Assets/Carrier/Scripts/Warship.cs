using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A surface warship on a water world's sea: it rides at the surface, rolling a little on the
    /// swell, and keeps to water deep enough for its keel (turning away from the shallows). Until
    /// its garrison is alerted it cruises round its patrol station; then it goes for the nearest
    /// player ship that is afloat or low over the water, closing to a stand-off and circling
    /// there, its deck guns trained on the ship while its guns and anti-air missiles
    /// (EnemyWeapons) fire. Built by GroundForces; its numbers are in
    /// Resources/Data/ground_units.json.
    /// </summary>
    public class Warship : MonoBehaviour, IVelocity
    {
        public PlanetSurface surface;
        public float cruiseSpeed = 10f, attackSpeed = 18f;
        [Tooltip("Degrees a second.")]
        public float turnRate = 9f;
        public float acceleration = 2f;
        public float standoff = 1800f, patrolRadius = 600f;
        [Tooltip("The least depth of water it sails in (m).")]
        public float minDepth = 25f;
        [Tooltip("How high its middle rides over the surface (m).")]
        public float rideHeight = 3f;
        [Tooltip("It chases a ship no higher than this over the water (m); higher, it only shoots.")]
        public float pursueCeiling = 900f;
        [Tooltip("The deck guns, turning to the target (each one's rest is where it was built facing).")]
        public Transform[] guns;

        public Vector3 Velocity { get; private set; }
        /// <summary>Going for a ship (for the self-test).</summary>
        public bool Pursuing { get; private set; }
        public Transform Target => target;
        /// <summary>Its middle's height over the sea's surface (m; for the self-test).</summary>
        public float OverSea => surface != null ? transform.position.y - surface.SeaLevel : 0f;

        Vector3 homeLocal;
        float yaw, around, speed, bob, sign = 1f, nextLook;
        Quaternion[] gunRest;
        EnemyWeapons weapons;
        Health health;
        Transform target;

        void Start()
        {
            homeLocal = transform.localPosition;
            yaw = transform.eulerAngles.y;
            around = Random.Range(0f, Mathf.PI * 2f);
            bob = Random.Range(0f, 10f);
            sign = Random.value < 0.5f ? -1f : 1f;
            weapons = GetComponent<EnemyWeapons>();
            health = GetComponent<Health>();
            if (guns != null)
            {
                gunRest = new Quaternion[guns.Length];
                for (int i = 0; i < guns.Length; i++) gunRest[i] = guns[i] != null ? guns[i].localRotation : Quaternion.identity;
            }
        }

        void Update()
        {
            if (health != null && health.Dead) { Velocity = Vector3.zero; return; }
            if (surface == null) return;
            float sea = surface.SeaLevel;
            if (float.IsNegativeInfinity(sea)) return;
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;

            bool alert = weapons != null && weapons.weaponsFree;
            if (alert)
            {
                target = weapons.target != null ? weapons.target : PlayerShips.Retarget(target, pos, ref nextLook);
                if (target == null) target = PlayerShips.Nearest(pos);
            }
            else target = null;
            // afloat or low over the water (not deep under it, nor high in the sky)
            Pursuing = target != null && target.position.y < sea + pursueCeiling && target.position.y > sea - 30f;

            Vector3 goal;
            float want;
            if (Pursuing)
            {
                Vector3 to = target.position - pos;
                to.y = 0f;
                float d = to.magnitude;
                Vector3 dir = d > 1f ? to / d : transform.forward;
                goal = d > standoff * 1.3f ? target.position : pos + Vector3.Cross(Vector3.up, dir) * sign * 300f + dir * (d - standoff);
                want = attackSpeed;
            }
            else
            {
                around += cruiseSpeed / Mathf.Max(50f, patrolRadius) * dt * sign;
                Vector3 home = transform.parent != null ? transform.parent.TransformPoint(homeLocal) : homeLocal;
                goal = home + new Vector3(Mathf.Cos(around), 0f, Mathf.Sin(around)) * patrolRadius;
                want = cruiseSpeed;
            }

            Vector3 toGoal = goal - pos;
            toGoal.y = 0f;
            if (toGoal.sqrMagnitude > 1f)
                yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(toGoal.x, toGoal.z) * Mathf.Rad2Deg, turnRate * dt);
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            // the shallows ahead: turn away from them, slowing
            if (Shallow(pos + fwd * (45f + speed * 4f), sea))
            {
                yaw += turnRate * 2f * dt * sign;
                want = Mathf.Min(want, cruiseSpeed * 0.4f);
                fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            }
            speed = Mathf.MoveTowards(speed, want, acceleration * dt);
            Vector3 next = pos + fwd * speed * dt;
            if (Shallow(next + fwd * 30f, sea)) { next.x = pos.x; next.z = pos.z; speed = Mathf.Min(speed, 1f); }

            // at the surface, riding the swell
            bob += dt;
            next.y = sea + rideHeight + Mathf.Sin(bob * 0.8f) * 0.5f;
            Velocity = (next - pos) / Mathf.Max(dt, 1e-4f);
            Velocity = new Vector3(Velocity.x, 0f, Velocity.z);
            transform.SetPositionAndRotation(next, Quaternion.Euler(Mathf.Sin(bob * 0.6f) * 1.2f, yaw, Mathf.Sin(bob * 0.45f) * 2.5f));

            // the deck guns on the target, or back to their rest
            if (guns != null && gunRest != null)
                for (int i = 0; i < guns.Length; i++)
                {
                    var g = guns[i];
                    if (g == null) continue;
                    Quaternion aim = gunRest[i];
                    if (target != null && alert)
                    {
                        Vector3 l = g.parent.InverseTransformDirection(target.position - g.position);
                        aim = Quaternion.Euler(0f, Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg, 0f);
                    }
                    g.localRotation = Quaternion.RotateTowards(g.localRotation, aim, 45f * dt);
                }
        }

        bool Shallow(Vector3 p, float sea) => surface.GroundAt(p) > sea - minDepth * 0.6f;
    }
}

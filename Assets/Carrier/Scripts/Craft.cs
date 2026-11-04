using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A small craft the carrier launches and commands: fighter, drone or dropship.
    /// Orders: Launch (fly clear of the bay), Escort (circle the carrier), Attack (make gun
    /// runs on a target), Return (fly back into its bay and land).
    /// </summary>
    public class Craft : MonoBehaviour, IVelocity
    {
        public enum Order { Launch, Escort, Attack, Return }
        public enum Kind { Fighter, Drone, Dropship }

        public Kind kind = Kind.Fighter;
        public Order order = Order.Launch;

        [Header("Flight")]
        public float cruiseSpeed = 180f;
        public float attackSpeed = 260f;
        public float turnRate = 90f;
        public float launchTime = 3f;

        [Header("Escort")]
        public float escortRadius = 900f;
        public float escortHeight = 150f;

        [Header("Guns")]
        public Projectile gunPrefab;
        public Transform[] guns;
        public float fireInterval = 0.3f;
        public float gunRange = 1200f;
        public float breakOffDistance = 250f;

        [HideInInspector] public CarrierController carrier;
        [HideInInspector] public LaunchBay homeBay;
        [HideInInspector] public Transform homePoint;
        [HideInInspector] public float speed;
        public Transform target;

        public Vector3 Velocity => transform.forward * speed;

        float age, cooldown, orbitAngle, orbitDir = 1f, breakOffUntil, radiusJitter, heightJitter;
        int returnStage;

        void Start()
        {
            radiusJitter = Random.Range(-200f, 200f);
            heightJitter = Random.Range(-250f, 250f);
            orbitDir = Random.value < 0.5f ? 1f : -1f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            cooldown -= dt;
            Vector3 pos = transform.position;
            Vector3 desired = transform.forward;
            float wantSpeed = cruiseSpeed;

            switch (order)
            {
                case Order.Launch:
                    if (age > launchTime) order = target != null ? Order.Attack : Order.Escort;
                    break;

                case Order.Escort:
                    if (carrier == null) break;
                    Vector3 c = carrier.transform.position;
                    Vector3 flat = Vector3.ProjectOnPlane(pos - c, Vector3.up);
                    orbitAngle = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    float r = escortRadius + radiusJitter;
                    float ahead = orbitAngle + orbitDir * Mathf.Rad2Deg * (cruiseSpeed * 2f / r);
                    Vector3 point = c + Quaternion.Euler(0f, ahead, 0f) * Vector3.forward * r
                                    + Vector3.up * (escortHeight + heightJitter);
                    desired = point - pos;
                    break;

                case Order.Attack:
                    if (target == null || !target.gameObject.activeInHierarchy) { order = Order.Escort; break; }
                    Vector3 to = target.position - pos;
                    float dist = to.magnitude;
                    wantSpeed = attackSpeed;
                    if (Time.time < breakOffUntil) desired = transform.forward + Vector3.up * 0.3f;
                    else
                    {
                        desired = to;
                        if (dist < breakOffDistance) breakOffUntil = Time.time + Random.Range(2.5f, 4f);
                        if (dist < gunRange && Vector3.Angle(transform.forward, to) < 6f && cooldown <= 0f) FireGuns();
                    }
                    break;

                case Order.Return:
                    if (homeBay == null) { Destroy(gameObject); return; }
                    Transform mouth = homeBay.mouth != null ? homeBay.mouth : homeBay.transform;
                    Transform land = homePoint != null ? homePoint : homeBay.transform;
                    // approach a point outside the bay, then fly in along the bay axis and land
                    Vector3 approach = mouth.position + mouth.forward * 400f;
                    Vector3 goal = returnStage == 0 ? approach : returnStage == 1 ? mouth.position : land.position;
                    desired = goal - pos;
                    wantSpeed = returnStage == 0 ? cruiseSpeed : Mathf.Max(30f, cruiseSpeed * 0.25f);
                    if (desired.magnitude < (returnStage == 0 ? 120f : 15f))
                    {
                        returnStage++;
                        if (returnStage > 2) { carrier?.Recover(this); Destroy(gameObject); return; }
                    }
                    break;
            }

            if (desired.sqrMagnitude > 0.01f)
            {
                var want = Quaternion.LookRotation(desired, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnRate * dt);
            }
            speed = Mathf.MoveTowards(speed, wantSpeed, 120f * dt);
            transform.position = pos + transform.forward * speed * dt;
        }

        void FireGuns()
        {
            cooldown = fireInterval;
            if (gunPrefab == null) return;
            bool drone = kind == Kind.Drone;
            SoundFX.Play(Sfx.Blaster, transform.position, drone ? 0.8f : 1f, drone ? 1.25f : 1f);
            if (guns == null || guns.Length == 0)
            {
                Spawn(transform.position + transform.forward * 8f, transform.rotation);
                return;
            }
            foreach (var g in guns) if (g) Spawn(g.position, g.rotation);
        }

        void Spawn(Vector3 p, Quaternion r)
        {
            var b = Instantiate(gunPrefab, p, r);
            b.owner = transform;
        }

        public void Command(Order o, Transform newTarget = null)
        {
            if (o == Order.Attack && newTarget != null) target = newTarget;
            if (o == Order.Return) returnStage = 0;
            order = o;
        }
    }
}

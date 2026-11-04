using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Flies an enemy ship in a field of asteroids and wreckage.
    ///
    /// - Lurk: waits out of sight behind cover with its weapons cold, until an ambush is sprung
    ///   (Engage).
    /// - Engage: a fighter or drone makes attack runs on its target, breaking away when it gets
    ///   close and swinging back in; a capital ship holds a stand-off distance and circles.
    /// - Hide: when its hull runs low it breaks off to the far side of the nearest asteroid or
    ///   wreck, where the target can't see it (and so can't shoot it), waits, then comes back.
    ///
    /// It steers round obstacles all the time. Its weapons (EnemyWeapons) only fire with a clear
    /// line of sight, so a target behind a rock is safe from it too. After being destroyed it
    /// respawns at its lurking place and rejoins the fight from there.
    /// </summary>
    public class CombatPilot : MonoBehaviour, IVelocity
    {
        public enum State { Lurk, Engage, Hide }
        public enum Style { Fighter, Capital }

        public Style style = Style.Fighter;
        public bool startLurking = true;
        [Tooltip("Lies hidden while waiting (in cover, not shown or targetable). Off for ships that wait in the open, on station: they can be seen and targeted before they attack.")]
        public bool concealed = true;
        public Transform target;

        [Header("Flight")]
        public float cruiseSpeed = 200f;
        public float attackSpeed = 280f;
        public float turnRate = 80f;
        public float acceleration = 120f;

        [Header("Attack runs (fighters)")]
        public float breakOffDistance = 380f;
        public float extendDistance = 1300f;

        [Header("Stand-off (capital ships)")]
        public float standoff = 1900f;
        public float circleSpeed = 4f;     // degrees per second round the target

        [Header("Patrol")]
        [Tooltip("Go back into hiding when the target gets this far from the lurking place (0 = never).")]
        public float leashDistance = 0f;
        [Tooltip("After being destroyed, respawn in hiding (weapons cold) instead of straight into the fight.")]
        public bool relurkOnRespawn = false;

        [Header("Cover")]
        [Tooltip("Hide when the hull falls below this fraction.")]
        public float hideBelow = 0.35f;
        public float hideTime = 6f;
        public float hideCooldown = 25f;
        public float hideSearch = 2500f;

        public State Current { get; private set; }
        public Vector3 Velocity => transform.forward * speed;
        public int TimesHidden { get; private set; }
        public float EngagedAt { get; private set; } = -1f;

        Health health;
        EnemyWeapons weapons;
        float speed, bodyRadius, nextHide, hideUntil, hideGiveUp, extendUntil;
        int hitsWhileLurking;
        Health targetHealth;
        IVelocity targetMover;
        Transform cachedTarget;
        bool extending;
        Vector3 lurkPoint, extendPoint, hidePoint;
        Quaternion lurkRotation;

        /// <summary>In its lurking place (not still flying back to it after breaking off).</summary>
        public bool AtHome { get; private set; } = true;

        /// <summary>Every enemy pilot in the scene (the warp drive checks them for combat).</summary>
        public static readonly System.Collections.Generic.List<CombatPilot> Active = new System.Collections.Generic.List<CombatPilot>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetActive() => Active.Clear();
        void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        void OnDisable() => Active.Remove(this);

        /// <summary>Is any enemy out of hiding, alive and within `range` of a point? (The warp drive
        /// is locked, and the music stays on the combat track, while one is.)</summary>
        public static bool AnyEngagedNear(Vector3 point, float range)
        {
            foreach (var p in Active)
            {
                if (p == null || p.Current == State.Lurk || (p.health != null && p.health.Dead)) continue;
                if ((p.transform.position - point).sqrMagnitude < range * range) return true;
            }
            return false;
        }

        /// <summary>Break off and go back into hiding, weapons cold.</summary>
        public void Disengage()
        {
            if (Current == State.Lurk) return;
            Current = State.Lurk;
            extending = false;
            if (weapons != null) weapons.weaponsFree = false;
            if (health != null) hitsWhileLurking = health.Hits;
        }

        void Awake()
        {
            health = GetComponent<Health>();
            weapons = GetComponent<EnemyWeapons>();
            // in the asteroid field the pilot does the flying, not the open-space orbit
            var orbit = GetComponent<Demo.OrbitMover>();
            if (orbit != null) orbit.enabled = false;
            var col = GetComponentInChildren<Collider>();
            bodyRadius = col != null ? Mathf.Max(6f, col.bounds.extents.magnitude * 0.6f) : 10f;
            lurkPoint = transform.position;
            lurkRotation = transform.rotation;
            Current = startLurking ? State.Lurk : State.Engage;
        }

        void Start()
        {
            if (target == null)
            {
                var player = FindAnyObjectByType<CarrierController>();
                if (player != null) target = player.transform;
            }
            if (weapons != null)
            {
                weapons.target = target;
                weapons.weaponsFree = Current != State.Lurk;
            }
            if (health != null) { health.onRespawned.AddListener(OnRespawned); hitsWhileLurking = health.Hits; }
        }

        /// <summary>A ship lying in wait for an ambush (not shown or targetable yet). One flying
        /// back to its hiding place is out in the open, so it isn't hidden until it gets there.</summary>
        public static bool IsHidden(Transform t)
        {
            var pilot = t != null ? t.GetComponent<CombatPilot>() : null;
            return pilot != null && pilot.concealed && pilot.Current == State.Lurk && pilot.AtHome;
        }

        /// <summary>Is this pilot alive?</summary>
        public bool Alive => health == null || !health.Dead;

        /// <summary>The nearest of these pilots that is still alive (null if none).</summary>
        public static CombatPilot Nearest(System.Collections.Generic.IEnumerable<CombatPilot> pilots, Vector3 from)
        {
            CombatPilot best = null;
            float bestD = float.MaxValue;
            foreach (var p in pilots)
            {
                if (p == null || (p.health != null && p.health.Dead)) continue;
                float d = (p.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        /// <summary>Spring the ambush: leave cover and attack.</summary>
        public void Engage()
        {
            if (Current != State.Lurk) return;
            Current = State.Engage;
            EngagedAt = Time.time;
            if (weapons != null) { weapons.weaponsFree = true; weapons.OpenFireSoon(1.5f); }
        }

        /// <summary>Back from destruction: at its lurking place, straight back into the fight.</summary>
        void OnRespawned()
        {
            transform.SetPositionAndRotation(lurkPoint, lurkRotation);
            speed = 0f;
            extending = false;
            AtHome = true;
            bool wasLurking = Current == State.Lurk || relurkOnRespawn;
            Current = wasLurking ? State.Lurk : State.Engage;
            if (health != null) hitsWhileLurking = health.Hits;
            if (weapons != null) weapons.weaponsFree = !wasLurking;
        }

        void Update()
        {
            if (health != null && health.Dead) return;
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;
            Vector3 desired = transform.forward;
            float wantSpeed = cruiseSpeed;

            if (Current == State.Lurk)
            {
                // shot at while lying in wait (or on the way back): no point hiding any more
                if (health != null && health.Hits > hitsWhileLurking) { Engage(); return; }
                float home2 = (pos - lurkPoint).sqrMagnitude;
                if (home2 < 300f * 300f)
                {
                    // sit in cover, nose toward the threat, engines barely ticking over
                    AtHome = true;
                    speed = Mathf.MoveTowards(speed, 0f, acceleration * dt);
                    transform.position = Vector3.MoveTowards(pos, lurkPoint, 20f * dt);
                    return;
                }
                // broke off: fly back to the hiding place, round the rocks. If the player is far
                // off (warped out), just be back there.
                AtHome = false;
                if (target == null || (target.position - pos).sqrMagnitude > 20000f * 20000f)
                {
                    transform.SetPositionAndRotation(lurkPoint, lurkRotation);
                    speed = 0f;
                    AtHome = true;
                    return;
                }
                Steer(pos, lurkPoint - pos, home2 < 1500f * 1500f ? cruiseSpeed * 0.5f : cruiseSpeed, dt);
                return;
            }

            if (target != cachedTarget)
            {
                cachedTarget = target;
                targetHealth = target != null ? target.GetComponentInParent<Health>() : null;
                targetMover = Motion.Of(target);
            }
            bool targetAlive = target != null && (targetHealth == null || !targetHealth.Dead);
            // the target has left the area (warped out, or run): go back into hiding
            if (leashDistance > 0f && target != null && (target.position - lurkPoint).sqrMagnitude > leashDistance * leashDistance)
            {
                Disengage();
                return;
            }
            if (Current == State.Engage && health != null && targetAlive &&
                health.Current < health.maxHealth * hideBelow && Time.time >= nextHide && target != null &&
                Cover.FindHidingPlace(pos, target.position, bodyRadius, hideSearch, out hidePoint))
            {
                Current = State.Hide;
                hideUntil = float.MaxValue;
                hideGiveUp = Time.time + 12f + Vector3.Distance(pos, hidePoint) / Mathf.Max(30f, attackSpeed);
                TimesHidden++;
            }

            switch (Current)
            {
                case State.Engage:
                    if (!targetAlive) { desired = (lurkPoint - pos); wantSpeed = cruiseSpeed * 0.5f; break; }
                    if (style == Style.Fighter) desired = AttackRun(pos, ref wantSpeed);
                    else desired = StandOff(pos, dt, ref wantSpeed);
                    break;

                case State.Hide:
                    desired = hidePoint - pos;
                    float dist = desired.magnitude;
                    wantSpeed = dist > 300f ? attackSpeed : Mathf.Lerp(0f, cruiseSpeed, dist / 300f);
                    if (dist < 150f && hideUntil == float.MaxValue) hideUntil = Time.time + hideTime;
                    // blocked from getting there (rock in the way): give up and fight on
                    if (hideUntil == float.MaxValue && Time.time > hideGiveUp) hideUntil = Time.time;
                    if (Time.time > hideUntil)
                    {
                        Current = State.Engage;
                        nextHide = Time.time + hideCooldown;
                    }
                    break;
            }

            Steer(pos, desired, wantSpeed, dt);
        }

        /// <summary>Turn toward a direction (round anything solid) and fly on at a speed.</summary>
        void Steer(Vector3 pos, Vector3 desired, float wantSpeed, float dt)
        {
            float look = Mathf.Max(250f, speed * 2.5f) + bodyRadius * 3f;
            desired = Cover.Avoid(pos, desired, bodyRadius, look);
            if (desired.sqrMagnitude > 1e-4f)
            {
                var want = Quaternion.LookRotation(desired, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnRate * dt);
            }
            speed = Mathf.MoveTowards(speed, wantSpeed, acceleration * dt);
            transform.position = Cover.PushOut(pos + transform.forward * speed * dt, bodyRadius);
        }

        /// <summary>Fly at the target (leading it), break away when close, swing out and come again.</summary>
        Vector3 AttackRun(Vector3 pos, ref float wantSpeed)
        {
            wantSpeed = attackSpeed;
            Vector3 tp = target.position;
            if (extending)
            {
                if (Vector3.Distance(pos, extendPoint) < 200f || Time.time > extendUntil) extending = false;
                else return extendPoint - pos;
            }
            Vector3 to = tp - pos;
            if (to.magnitude < breakOffDistance)
            {
                // break off past the target and out to one side, then turn back in
                Vector3 side = Vector3.Cross(to.normalized, Random.value < 0.5f ? Vector3.up : Vector3.right).normalized;
                extendPoint = tp + (to.normalized * 0.6f + side + Random.insideUnitSphere * 0.4f).normalized * extendDistance;
                extendUntil = Time.time + 7f;
                extending = true;
                return extendPoint - pos;
            }
            float t = to.magnitude / Mathf.Max(1f, attackSpeed + 400f);
            return (tp + (targetMover != null ? targetMover.Velocity * t : Vector3.zero)) - pos;
        }

        /// <summary>Hold the stand-off range, circling the target slowly.</summary>
        Vector3 StandOff(Vector3 pos, float dt, ref float wantSpeed)
        {
            Vector3 tp = target.position;
            Vector3 flat = Vector3.ProjectOnPlane(pos - tp, Vector3.up);
            if (flat.sqrMagnitude < 1f) flat = Vector3.forward;
            // aim for a point a few degrees round the circle, so it keeps circling
            Vector3 dir = Quaternion.AngleAxis(circleSpeed * 3f, Vector3.up) * flat.normalized;
            Vector3 goal = tp + dir * standoff + Vector3.up * Mathf.Clamp(pos.y - tp.y, -400f, 400f);
            Vector3 to = goal - pos;
            wantSpeed = Mathf.Lerp(cruiseSpeed * 0.35f, cruiseSpeed, Mathf.Clamp01(to.magnitude / 800f));
            return to;
        }
    }
}

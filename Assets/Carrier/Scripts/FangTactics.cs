using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// How a Vrask Fang fights, on top of its CombatPilot (which still flies it between clamps,
    /// and keeps it among the active enemy pilots for the warp lock and the music):
    ///
    /// - Screened assault: a ship the Snares hold in their beams (TractorBeam.HeldTargets) comes
    ///   first, then one they have disabled (its engines or shield generator knocked out), then
    ///   its pilot's own target.
    /// - High-speed approach from an unexpected vector: each run starts at attack speed by way of a
    ///   point well above or below the target's plane, or from behind a Vrask ship near it.
    /// - Suppress: its heavy short-range guns aim at the target's weapons (ShipSystems.SetAim).
    /// - Pod saturation: while closing (within its pods' range) it launches a wave of boarding pods
    ///   whenever one is ready and the target's shield is under half.
    /// - Ram and dock: with the target's shield down (or under a quarter) it slows to its speed,
    ///   closes and clamps its prow to the hull at the breach point, matching its motion, and puts
    ///   its own party aboard (Boarders.RamParty). Clamped, it holds the ship
    ///   (CarrierController.Clamp: no warp, and a crawl).
    /// - Hold and extract: it stays clamped while its boarders fight, and casts off when they are
    ///   repelled, the ship is taken, or its own hull falls under 30 %; then it flies clear for a
    ///   while and comes round again. With the shield up it makes the pilot's own attack runs.
    /// </summary>
    public class FangTactics : MonoBehaviour, CombatPilot.ITactics
    {
        public enum Phase { None, Approach, Run, Clamped, Extract }

        [Tooltip("From this far (m) it heads straight in.")]
        public float runRange = 1400f;
        [Tooltip("How far above or below the target's plane (m) it comes in from.")]
        public float vectorOffset = 900f;
        [Tooltip("Its prow this near the hull (m) and it clamps on.")]
        public float clampRange = 150f;
        [Tooltip("It casts off with its own hull under this share.")]
        public float breakBelow = 0.3f;
        [Tooltip("Seconds it flies clear after casting off, and before it can clamp again.")]
        public float extractTime = 8f, reclampAfter = 25f;
        [Tooltip("A held or disabled ship this near (m) is gone for first.")]
        public float preferRange = 12000f;
        [Tooltip("Seconds its boarders have to show aboard (co-op: the boarded game's word) before it counts them gone.")]
        public float grace = 4f;

        public Phase Current { get; private set; }
        public Transform Target { get; private set; }
        /// <summary>The way it came in on its last run ("above", "below", "behind the Vrask Maw").</summary>
        public string Vector { get; private set; } = "";
        public int Clamps { get; private set; }
        public int CastOffs { get; private set; }
        public string LastCastOff { get; private set; } = "";
        public bool IsClamped => Current == Phase.Clamped;

        CombatPilot pilot;
        EnemyWeapons weapons;
        PodLauncher pods;
        Health health;
        Transform breach;
        float nextThink, extractUntil, nextClamp, clampedAt, targetRadius, myRadius;
        // the run's waypoint: an offset from the target, or from the Vrask ship it comes from behind
        Vector3 offset;
        Transform from;
        bool waypointSet;
        // the clamp: where it sits on the target (in its space), and where it started from
        Vector3 clampPos, startPos;
        Quaternion clampRot, startRot;
        CarrierController targetShip;

        void Awake()
        {
            pilot = GetComponent<CombatPilot>();
            weapons = GetComponent<EnemyWeapons>();
            pods = GetComponentInChildren<PodLauncher>();
            health = GetComponent<Health>();
            var vs = GetComponent<VraskShip>();
            breach = vs != null ? vs.PointOf("breach") : null;
            var col = GetComponentInChildren<Collider>();
            myRadius = col != null ? col.bounds.extents.magnitude * 0.5f : 40f;
            if (pilot != null) pilot.tactics = this;
        }

        void OnDisable() => StandDown();

        void OnDestroy()
        {
            if (pilot != null && ReferenceEquals(pilot.tactics, this)) pilot.tactics = null;
        }

        void StandDown()
        {
            CastOff("out of the fight");
            ShipSystems.SetAim(transform.root, ShipSystem.Hull);
            if (pilot != null) pilot.keepTarget = false;
            Current = Phase.None;
            Target = null;
            targetShip = null;
        }

        void Update()
        {
            if (pilot == null) return;
            // clamped: holding the ship every frame
            if (Current == Phase.Clamped && targetShip != null) targetShip.Clamp(transform, 0.3f);
            if (Time.time < nextThink) return;
            nextThink = Time.time + 0.25f;
            var t = pilot.Alive && pilot.Current == CombatPilot.State.Engage ? Choose() : null;
            if (t == null)
            {
                if (Current != Phase.None || Target != null) StandDown();
                return;
            }
            if (t != Target)
            {
                CastOff("another target");
                Target = t;
                targetShip = t.GetComponent<CarrierController>();
                var col = t.GetComponentInChildren<Collider>();
                targetRadius = col != null ? col.bounds.extents.magnitude * 0.6f : 60f;
                waypointSet = false;
                Current = Phase.Approach;
            }
            pilot.keepTarget = true;
            if (pilot.target != t) pilot.target = t;
            if (weapons != null && weapons.target != t) weapons.SetTarget(t);
            // suppress: the heavy guns on its weapons
            ShipSystems.SetAim(transform.root, ShipSystem.Weapons);
            Think(t);
        }

        void Think(Transform t)
        {
            Vector3 pos = transform.position;
            float d = Vector3.Distance(pos, t.position);
            bool lets = BoardingPod.ShieldLets(t);
            // pod saturation: a wave as it closes, the shield weakened
            if (pods != null && (Current == Phase.Approach || Current == Phase.Run) && BoardingPod.ShieldUnder(t, 0.5f)) pods.LaunchWave(t);
            switch (Current)
            {
                case Phase.Clamped:
                    if (health != null && health.Current < health.maxHealth * breakBelow) CastOff("its hull is failing");
                    else if (!Alive(t)) CastOff("the ship is taken");
                    else if (Time.time > clampedAt + grace && Boarders.CountOn(t) == 0) CastOff("its boarders are repelled");
                    break;
                case Phase.Extract:
                    if (Time.time > extractUntil) { Current = Phase.Approach; waypointSet = false; }
                    break;
                case Phase.Approach:
                    if (!waypointSet) PickVector(t);
                    if (Vector3.Distance(pos, Waypoint) < 350f || d < runRange * 0.6f) Current = Phase.Run;
                    break;
                case Phase.Run:
                    if (d > runRange * 2.5f) { Current = Phase.Approach; waypointSet = false; break; }
                    if (lets && Time.time >= nextClamp && Boarders.CanBoard(t) && HullGap(t, out RaycastHit hit) < clampRange) ClampOn(t, hit);
                    break;
            }
        }

        /// <summary>CombatPilot's hook: the run in, matching the target's speed at the end; flying
        /// clear after casting off. With the target's shield up, the pilot's own attack runs.</summary>
        public void Steer(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            if (Target == null) return;
            switch (Current)
            {
                case Phase.Approach:
                    desired = Waypoint - pos;
                    wantSpeed = pilot.attackSpeed;
                    break;
                case Phase.Run:
                {
                    if (!BoardingPod.ShieldLets(Target)) break;
                    Vector3 to = Target.position - pos;
                    float gap = to.magnitude - targetRadius - myRadius;
                    var mover = Motion.Of(Target);
                    float match = mover != null ? mover.Velocity.magnitude : 0f;
                    desired = to;
                    wantSpeed = Mathf.Lerp(match + 15f, pilot.attackSpeed, Mathf.Clamp01((gap - 100f) / 900f));
                    break;
                }
                case Phase.Extract:
                {
                    Vector3 away = pos - Target.position;
                    desired = (away.sqrMagnitude > 1f ? away.normalized : transform.forward) + Target.up * 0.3f;
                    wantSpeed = pilot.attackSpeed;
                    break;
                }
            }
        }

        // the clamp, after the target has moved this frame
        void LateUpdate()
        {
            if (Current != Phase.Clamped || Target == null) return;
            // (it eases in over a second and a half, then sits there)
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - clampedAt) / 1.5f));
            Vector3 p = Target.TransformPoint(Vector3.Lerp(startPos, clampPos, k));
            Quaternion q = Target.rotation * Quaternion.Slerp(startRot, clampRot, k);
            transform.SetPositionAndRotation(p, q);
        }

        // ------------------------------------------------------------------ the approach

        Vector3 Waypoint => from != null ? from.position + offset : Target != null ? Target.position + offset : transform.position;

        /// <summary>The run's way in: from behind a Vrask ship near the target (half the time, when
        /// there is one), else well above or below the target's plane, on this side of it.</summary>
        void PickVector(Transform t)
        {
            waypointSet = true;
            from = null;
            Vector3 tp = t.position, up = t.up;
            VraskShip cover = null;
            float best = float.MaxValue;
            foreach (var v in VraskShip.All)
            {
                if (v == null || v.transform == transform.root || !Alive(v.transform, false)) continue;
                float dt = Vector3.Distance(v.transform.position, tp);
                if (dt < 800f || dt > 4500f) continue;
                float dm = Vector3.Distance(v.transform.position, transform.position);
                if (dm < best) { best = dm; cover = v; }
            }
            if (cover != null && Random.value < 0.5f)
            {
                // just behind it, as the target sees it
                from = cover.transform;
                Vector3 back = cover.transform.position - tp;
                offset = back.normalized * 450f;
                var ci = cover.GetComponent<TargetInfo>();
                Vector = $"behind the {(ci != null ? ci.Name : cover.name)}";
                return;
            }
            float side = Random.value < 0.5f ? 1f : -1f;
            Vector3 flat = Vector3.ProjectOnPlane(transform.position - tp, up);
            if (flat.sqrMagnitude < 1f) flat = Vector3.ProjectOnPlane(t.forward, up);
            offset = flat.normalized * 1200f + up * side * vectorOffset;
            Vector = side > 0f ? "above" : "below";
        }

        // ------------------------------------------------------------------ the clamp

        /// <summary>How far its breaching prow is from the target's hull (not its shield), and where
        /// on the hull it would meet it.</summary>
        float HullGap(Transform t, out RaycastHit hull)
        {
            hull = default;
            Vector3 o = breach != null ? breach.position : transform.position;
            Vector3 to = t.position - o;
            float d = to.magnitude;
            if (d < 1f) return 0f;
            float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(o, to / d, d, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || !h.collider.enabled || !h.transform.IsChildOf(t) || h.collider.GetComponent<Shield>() != null) continue;
                if (h.distance < best) { best = h.distance; hull = h; }
            }
            if (best < float.MaxValue) return best;
            // (no hull to hit: by its size)
            hull.point = t.position - to / d * targetRadius;
            hull.normal = -to / d;
            return Mathf.Max(0f, d - targetRadius);
        }

        void ClampOn(Transform t, RaycastHit hit)
        {
            // prow into the hull at the breach point, held there in the target's space
            Vector3 n = hit.normal.sqrMagnitude > 0.5f ? hit.normal : (transform.position - t.position).normalized;
            Vector3 upHint = Vector3.ProjectOnPlane(t.up, n);
            Quaternion rot = Quaternion.LookRotation(-n, upHint.sqrMagnitude > 0.01f ? upHint : Vector3.ProjectOnPlane(Vector3.up, n));
            Vector3 prow = breach != null ? transform.InverseTransformPoint(breach.position) : Vector3.forward * myRadius;
            Vector3 pos = hit.point + n * 2f - rot * prow;
            clampPos = t.InverseTransformPoint(pos);
            clampRot = Quaternion.Inverse(t.rotation) * rot;
            startPos = t.InverseTransformPoint(transform.position);
            startRot = Quaternion.Inverse(t.rotation) * transform.rotation;
            Current = Phase.Clamped;
            clampedAt = Time.time;
            Clamps++;
            pilot.externalMotion = true;
            if (targetShip != null) targetShip.Clamp(transform, 0.3f);
            SoundFX.Play(Sfx.Impact, hit.point, 1f, 0.4f);
            // its own party through the breaching ram
            Boarders.Board(t, Boarders.RamParty);
        }

        /// <summary>Cast off (if clamped) and fly clear for a while.</summary>
        void CastOff(string why)
        {
            if (Current != Phase.Clamped) return;
            pilot.externalMotion = false;
            if (targetShip != null) targetShip.Unclamp();
            CastOffs++;
            LastCastOff = why;
            nextClamp = Time.time + reclampAfter;
            extractUntil = Time.time + extractTime;
            Current = Phase.Extract;
        }

        // ------------------------------------------------------------------ the target

        /// <summary>The ship to go for: the one it is clamped to while that lasts; else a held
        /// ship near; else a disabled one; else the one it is on; else its pilot's.</summary>
        Transform Choose()
        {
            if (Current == Phase.Clamped && Target != null && Alive(Target)) return Target;
            Vector3 p = transform.position;
            float r2 = preferRange * preferRange;
            Transform best = null;
            float bestD = float.MaxValue;
            foreach (var s in TractorBeam.HeldTargets)
            {
                if (s == null || !Alive(s)) continue;
                float d = (s.position - p).sqrMagnitude;
                if (d < r2 && d < bestD) { bestD = d; best = s; }
            }
            if (best != null) return best;
            foreach (var s in PlayerShips.All)
            {
                if (s == null || !Alive(s) || !Disabled(s)) continue;
                float d = (s.position - p).sqrMagnitude;
                if (d < r2 && d < bestD) { bestD = d; best = s; }
            }
            if (best != null) return best;
            if (Target != null && Alive(Target)) return Target;
            var t = TractorBeam.ShipOf(pilot.target);
            return t != null && Alive(t) ? t : null;
        }

        static bool Disabled(Transform s)
        {
            var sys = s.GetComponent<ShipSystems>();
            return sys != null && (sys.EnginesDown || sys.IsDown(ShipSystem.Shields));
        }

        static bool Alive(Transform t, bool player = true)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return false;
            var h = t.GetComponent<Health>();
            return (h == null || !h.Dead) && (!player || PlayerShips.Present(t));
        }
    }
}

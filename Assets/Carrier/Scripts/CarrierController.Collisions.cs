using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>Running into asteroids and wrecks: the ship stopped at the surface, and damage by how hard it hit.</summary>
    public partial class CarrierController
    {
        /// <summary>Times the ship has run into an asteroid or wreck.</summary>
        public int Collisions { get; private set; }
        public float LastCollisionTime { get; private set; } = -100f;

        const float Skin = 2f;
        Vector3 capCenter;
        float capHalf = -1f, capRadius;

        /// <summary>A capsule along the hull, from the hull collider's size.</summary>
        void EnsureCapsule()
        {
            if (capHalf >= 0f) return;
            FindSystems();
            var b = new Bounds(Vector3.zero, Vector3.one * 20f);
            bool any = false;
            foreach (var c in hullColliders)
            {
                if (!(c is MeshCollider mc) || mc.sharedMesh == null) continue;
                var mb = mc.sharedMesh.bounds;
                var s = mc.transform.lossyScale;
                var center = transform.InverseTransformPoint(mc.transform.TransformPoint(mb.center));
                var size = Vector3.Scale(mb.size, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
                var lb = new Bounds(center, size);
                if (!any) { b = lb; any = true; } else b.Encapsulate(lb);
            }
            capCenter = b.center;
            capRadius = Mathf.Max(5f, Mathf.Max(b.extents.x, b.extents.y) * 0.8f);
            capHalf = Mathf.Max(0f, b.extents.z - capRadius);
            // a stand-in capsule for working out how far the hull has sunk into rock; it lives on
            // its own (not under the ship, so turret hull checks never see it) and only its shape
            // is used: ComputePenetration is given the hull's pose each time
            var go = new GameObject($"{name} collision capsule") { hideFlags = HideFlags.HideAndDontSave };
            go.layer = 2;   // Ignore Raycast
            go.transform.position = new Vector3(0f, -100000f, 0f);
            proxy = go.AddComponent<CapsuleCollider>();
            proxy.isTrigger = true;
            proxy.direction = 2;
            proxy.radius = capRadius;
            proxy.height = 2f * (capHalf + capRadius);
        }

        CapsuleCollider proxy;
        static readonly Collider[] Overlaps = new Collider[16];

        void OnDestroy() { if (proxy != null) Destroy(proxy.gameObject); }

        /// <summary>Push the hull back out of any rock it overlaps (after turning into a rock face,
        /// or a drifting wreck section swinging into it): casts can't see what they start inside.</summary>
        Vector3 Depenetrate(Vector3 position)
        {
            Quaternion rot = transform.rotation;
            Vector3 c = position + rot * capCenter, f = rot * Vector3.forward * capHalf;
            int n = Physics.OverlapCapsuleNonAlloc(c + f, c - f, capRadius, Overlaps, Cover.Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = Overlaps[i];
                if (col == null || proxy == null) continue;
                if (Physics.ComputePenetration(proxy, c, rot, col, col.transform.position, col.transform.rotation, out var dir, out var depth))
                {
                    Vector3 push = dir * (depth + Skin);
                    position += push;
                    c += push;
                    if (Time.time - LastCollisionTime > 0.5f)
                    {
                        Collisions++;
                        if (health != null) health.Damage(depth * collisionDamage, c, HitKind.Cannon);
                    }
                    LastCollisionTime = Time.time;
                    float into = Vector3.Dot(Velocity, -dir);
                    if (into > 0f) speed *= 0.5f;
                }
            }
            return position;
        }

        /// <summary>Limit a move so the hull doesn't pass into an obstacle: stop at the surface and
        /// slide along it, losing speed (and taking a knock) in proportion to how hard it hit.</summary>
        Vector3 Collide(Vector3 move)
        {
            if (!Cover.Active || move.sqrMagnitude < 1e-8f) return move;
            EnsureCapsule();
            Vector3 c = transform.TransformPoint(capCenter), f = transform.forward * capHalf;
            float dist = move.magnitude;
            Vector3 dir = move / dist;
            if (!Physics.CapsuleCast(c + f, c - f, capRadius, dir, out var hit, dist + Skin, Cover.Mask, QueryTriggerInteraction.Ignore))
                return move;
            float allowed = Mathf.Max(0f, hit.distance - Skin);
            Vector3 slide = Vector3.ProjectOnPlane(move - dir * allowed, hit.normal);
            float impact = Vector3.Dot(Velocity, -hit.normal);
            // the higher allowance on a planet is for setting down on the ground, not hitting a wall
            if (impact > (hit.normal.y > 0.7f ? safeImpact : 3f))
            {
                speed *= Mathf.Clamp01(1f - 0.6f * impact / Mathf.Max(1f, Velocity.magnitude));
                if (Vector3.Dot(transform.up * liftSpeed, hit.normal) < 0f) liftSpeed = 0f;
                if (Time.time - LastCollisionTime > 0.5f)
                {
                    Collisions++;
                    if (health != null) health.Damage(impact * collisionDamage, hit.point, HitKind.Cannon);
                    SoundFX.Play(Sfx.Impact, hit.point, 1f, 0.6f);
                }
                LastCollisionTime = Time.time;
            }
            Vector3 moved = dir * allowed;
            if (slide.sqrMagnitude > 1e-8f &&
                Physics.CapsuleCast(c + f + moved, c - f + moved, capRadius, slide.normalized, out var h2, slide.magnitude + Skin, Cover.Mask, QueryTriggerInteraction.Ignore))
                slide = slide.normalized * Mathf.Max(0f, h2.distance - Skin);
            return moved + slide;
        }

        /// <summary>Clearance from the hull to the nearest obstacle surface below `range` (for the reel log).</summary>
        public float ObstacleClearance(float range)
        {
            if (!Cover.Active) return range;
            EnsureCapsule();
            Vector3 c = transform.TransformPoint(capCenter), f = transform.forward * capHalf;
            float best = range;
            foreach (var d in new[] { Vector3.down, Vector3.up, transform.right, -transform.right, transform.forward })
                if (Physics.CapsuleCast(c + f, c - f, capRadius, d, out var hit, range, Cover.Mask, QueryTriggerInteraction.Ignore))
                    best = Mathf.Min(best, hit.distance);
            return best;
        }

        /// <summary>Light the elevator thrusters that are pushing: the ventral ones to go up (or to
        /// stop a descent), the dorsal ones to go down (or to stop a climb). Full flare while
        /// changing speed, a steady smaller one while holding the thrusters on.</summary>
        void UpdateLiftFlares(float liftChange)
        {
            float push = Mathf.Clamp(liftChange / Mathf.Max(0.1f, elevatorSpeed * 0.25f), -1f, 1f);
            float up = Mathf.Max(push, 0f), down = Mathf.Max(-push, 0f);
            if (elevator > 0f) up = Mathf.Max(up, 0.35f * elevator);
            if (elevator < 0f) down = Mathf.Max(down, -0.35f * elevator);
            if (Mathf.Abs(liftChange) < 0.05f && Mathf.Abs(elevator) < 0.01f) up = down = 0f;
            if (liftThrustersUp != null) foreach (var f in liftThrustersUp) if (f) f.thrust = up;
            if (liftThrustersDown != null) foreach (var f in liftThrustersDown) if (f) f.thrust = down;
        }

        /// <summary>Point every gun (and later launches) at this target. Null stops targeting:
        /// the guns stow and craft attacking the old target fall back to escort.</summary>
        public void SetTarget(Transform t)
        {
            var old = target;
            target = t;
            if (mainTurrets != null) foreach (var tu in mainTurrets) if (tu) tu.target = t;
            if (pointDefense != null) foreach (var tu in pointDefense) if (tu) tu.target = t;
            if (t == null && old != null)
                foreach (var c in wing)
                    if (c != null && c.order == Craft.Order.Attack && c.target == old) c.Command(Craft.Order.Escort);
        }

        public void LaunchFighters(int perBay = 2) => LaunchFromFlanks(fighterPrefab, ref fighterStock, perBay);
        public void LaunchDrones(int perBay = 2) => LaunchFromFlanks(dronePrefab, ref droneStock, perBay);

        void LaunchFromFlanks(Craft prefab, ref int stock, int perBay)
        {
            if (prefab == null || flankBays == null) return;
            foreach (var bay in flankBays)
            {
                int n = Mathf.Min(perBay, stock);
                if (n <= 0 || bay == null || bay.Busy) continue;
                stock -= n;
                bay.Launch(prefab, n, this, target);
            }
        }

        public void LaunchDropships(int count = 2)
        {
            int n = Mathf.Min(count, dropshipStock);
            if (n <= 0 || ventralHangar == null || dropshipPrefab == null || ventralHangar.Busy) return;
            dropshipStock -= n;
            ventralHangar.Launch(dropshipPrefab, n, this, null);
        }

        public void FireMissiles(int count = 8)
        {
            if (missiles != null && target != null) missiles.FireSalvo(target, count, Velocity);
        }

        /// <summary>Order every craft in the air: attack the current target, escort, or return.</summary>
        public void OrderWing(Craft.Order order)
        {
            if (order == Craft.Order.Attack && target == null) return;
            foreach (var c in wing)
                if (c != null && c.kind != Craft.Kind.Dropship) c.Command(order, target);
        }

        public void Register(Craft c)
        {
            if (!wing.Contains(c)) wing.Add(c);
        }

        /// <summary>Called when a craft lands back aboard.</summary>
        public void Recover(Craft c)
        {
            wing.Remove(c);
            switch (c.kind)
            {
                case Craft.Kind.Fighter: fighterStock++; break;
                case Craft.Kind.Drone: droneStock++; break;
                case Craft.Kind.Dropship: dropshipStock++; break;
            }
        }
    }
}

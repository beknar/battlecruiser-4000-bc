using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Ships flown by an autopilot (the player's auto-travel and auto-fly, the AI wingman, the
    /// fleet) steer round the other ships instead of through them. For each ship near the way,
    /// the closest the two will come in the next few seconds (from where each is and how each is
    /// moving) is worked out; one that would come closer than the two hulls and a margin pushes
    /// the aim point off to the side, and eases the throttle if it is ahead. The side is away from
    /// where the miss would be, or, dead on, to starboard of the closing motion: the other ship,
    /// working out the same from its side, turns the other way, so the two never dodge into each
    /// other, in one game or in two (co-op, where each game steers only its own ship). A side once
    /// chosen is kept a few seconds, so the dodge doesn't flip back and forth.
    /// </summary>
    public static class ShipAvoidance
    {
        /// <summary>How long a chosen side is kept (s).</summary>
        public const float StickFor = 4f;

        static readonly Dictionary<long, (Vector3 side, float until)> sides = new Dictionary<long, (Vector3, float)>();
        static readonly Dictionary<Transform, float> radii = new Dictionary<Transform, float>();
        static readonly List<Collider> found = new List<Collider>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { sides.Clear(); radii.Clear(); }

        /// <summary>How far a ship's hull reaches from its middle (a CarrierController: its
        /// collision capsule's; another player's ship: from its colliders, worked out once).</summary>
        public static float RadiusOf(Transform t)
        {
            if (t == null) return 0f;
            if (t.TryGetComponent(out CarrierController cc)) return cc.HullRadius;
            if (radii.TryGetValue(t, out float r)) return r;
            t.GetComponentsInChildren(false, found);
            bool any = false;
            Bounds b = default;
            foreach (var c in found)
            {
                if (c == null || c.isTrigger || c.GetComponentInParent<Shield>() != null) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            found.Clear();
            r = any ? Mathf.Max(b.extents.x, b.extents.y, b.extents.z) : 80f;
            radii[t] = r;
            return r;
        }

        /// <summary>Is `t` a ship to keep clear of, for `me`? Not itself, not `ignore` (what it is
        /// flying at), not one gone (in warp, down on a planet, destroyed) or on a tow.</summary>
        static bool Avoid(Transform t, CarrierController me, Transform ignore)
        {
            if (t == null || t == me.transform || t == ignore) return false;
            if (!t.gameObject.activeInHierarchy || !PlayerShips.Present(t)) return false;
            if (t.TryGetComponent(out Health h) && h.Dead) return false;
            // (docked, on a tow, in warp, landing: held where it is, out of the way; steering round
            // one docked beside a launching fleet ship swung that into the station)
            if (t.TryGetComponent(out CarrierController occ) && (occ.Destroyed || occ.externalMotion)) return false;
            if (t.TryGetComponent(out FleetPilot fp) && fp.Towed) return false;
            if (t.TryGetComponent(out RemotePlayer rp) && (rp.Hidden || rp.Docked)) return false;
            return true;
        }

        /// <summary>The point for `me` to steer for on the way to `goal`: the goal itself, or off
        /// to the side of it round ships in the way. `throttleK` (0.35 to 1) eases the throttle
        /// for one ahead.</summary>
        public static Vector3 Aim(CarrierController me, Vector3 goal, Transform ignore, out float throttleK)
        {
            throttleK = 1f;
            if (me == null) return goal;
            Vector3 p0 = me.transform.position, toGoal = goal - p0;
            float gd = toGoal.magnitude;
            if (gd < 1f) return goal;
            float spd = Mathf.Max(me.Speed, 20f);
            Vector3 v0 = toGoal / gd * spd;
            float r0 = me.HullRadius;
            float horizon = Horizon(me.turnRate);
            Vector3 push = Vector3.zero;
            foreach (var t in PlayerShips.All)
            {
                if (!Avoid(t, me, ignore)) continue;
                var mv = Motion.Of(t);
                Vector3 vj = mv != null ? mv.Velocity : Vector3.zero;
                Vector3 pj = t.position;
                // (another player's ship is shown a little behind where it is)
                if (t.TryGetComponent(out RemotePlayer _)) pj += vj * (float)RemotePlayer.Delay;
                long key = ((long)me.GetHashCode() << 32) ^ (uint)t.GetHashCode();
                Vector3? kept = sides.TryGetValue(key, out var s) && Time.time < s.until ? s.side : (Vector3?)null;
                Vector3 p = Push(p0, v0, r0, spd, gd, horizon, me.Up, me.transform.forward, pj, vj, RadiusOf(t), kept, out Vector3 side, out float k);
                if (p == Vector3.zero) continue;
                if (kept == null) sides[key] = (side, Time.time + StickFor);
                push += p;
                throttleK = Mathf.Min(throttleK, k);
            }
            if (sides.Count > 256) sides.Clear();
            if (push == Vector3.zero) return goal;
            return p0 + (toGoal / gd + 2.5f * push).normalized * Mathf.Min(gd, 5000f);
        }

        /// <summary>How far ahead (s) to look: about the time to turn 60 degrees, and a little.</summary>
        public static float Horizon(float turnRate) => Mathf.Clamp(60f / Mathf.Max(1f, turnRate) + 4f, 8f, 20f);

        /// <summary>The push off the way (a direction, strength 0 to 1) for one other ship, and
        /// the throttle factor for it; zero when it won't come too close. `kept`: the side chosen
        /// for it before, if still kept; `side` the side used.</summary>
        public static Vector3 Push(Vector3 p0, Vector3 v0, float r0, float spd, float goalDist, float horizon, Vector3 up, Vector3 forward,
                                   Vector3 pj, Vector3 vj, float rj, Vector3? kept, out Vector3 side, out float throttleK)
        {
            throttleK = 1f;
            side = Vector3.zero;
            Vector3 rel = pj - p0, w = v0 - vj;
            float R = r0 + rj + Mathf.Max(60f, 0.25f * (r0 + rj));
            // (not closing, and not touching: a ship keeping station alongside is no matter)
            float closing = Vector3.Dot(rel, w);
            if (closing <= 0f && rel.magnitude > r0 + rj) return Vector3.zero;
            float tc = Mathf.Clamp(closing / Mathf.Max(1e-3f, w.sqrMagnitude), 0f, horizon);
            // (we are there before it matters)
            if (tc * spd > goalDist + R) return Vector3.zero;
            Vector3 miss = rel - w * tc;
            float d = miss.magnitude;
            if (d >= R) return Vector3.zero;
            if (kept.HasValue) side = kept.Value;
            else
            {
                // away from where the miss would be, leaning to starboard of the closing motion
                // (so dead on, it is starboard): worked out from the other ship's side, every term
                // changes sign, so the two always go opposite ways
                Vector3 starboard = Vector3.Cross(up, w.sqrMagnitude > 1e-4f ? w : forward);
                if (starboard.sqrMagnitude > 1e-6f) starboard.Normalize();
                side = Vector3.ProjectOnPlane(-miss + 0.2f * R * starboard, w.sqrMagnitude > 1e-4f ? w : forward);
                if (side.sqrMagnitude < 1e-6f) side = starboard.sqrMagnitude > 1e-6f ? starboard : Vector3.Cross(up, forward);
                side = side.normalized;
            }
            float u = (1f - d / R) * (1f - tc / Mathf.Max(0.01f, horizon));
            if (Vector3.Dot(rel, forward) > 0f) throttleK = Mathf.Lerp(1f, 0.35f, u);
            return side * u;
        }

        // ------------------------------------------------------------ hulls apart (the backstop)

        /// <summary>The closest points of two segments (a0-a1, b0-b1).</summary>
        public static void ClosestPoints(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, out Vector3 pa, out Vector3 pb)
        {
            Vector3 d1 = a1 - a0, d2 = b1 - b0, r = a0 - b0;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (a <= 1e-6f && e <= 1e-6f) { pa = a0; pb = b0; return; }
            if (a <= 1e-6f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-6f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s = denom > 1e-6f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            pa = a0 + d1 * s;
            pb = b0 + d2 * t;
        }

        /// <summary>How far `me`'s hull (a capsule: centre c, half-length along `along`, radius r)
        /// must move to be clear of another's, and which way; zero when clear.</summary>
        public static Vector3 Separation(Vector3 c, Vector3 along, float half, float r, Vector3 oc, Vector3 oalong, float ohalf, float or, Vector3 up)
        {
            ClosestPoints(c - along * half, c + along * half, oc - oalong * ohalf, oc + oalong * ohalf, out var pa, out var pb);
            Vector3 gap = pa - pb;
            float dist = gap.magnitude, need = r + or;
            if (dist >= need) return Vector3.zero;
            Vector3 dir = dist > 0.01f ? gap / dist : (c - oc).sqrMagnitude > 0.01f ? (c - oc).normalized : up;
            return dir * (need - dist);
        }

        /// <summary>The capsule of another ship as this game knows it: a CarrierController's own;
        /// another player's ship's, from how far it reaches (a long thin hull along its length).</summary>
        public static void CapsuleOf(Transform t, out Vector3 centre, out Vector3 along, out float half, out float radius)
        {
            if (t.TryGetComponent(out CarrierController cc)) { cc.HullCapsule(out centre, out along, out half, out radius); return; }
            float reach = RadiusOf(t);
            radius = Mathf.Clamp(reach * 0.3f, 10f, 45f);
            half = Mathf.Max(0f, reach - radius);
            centre = t.position;
            // (another player's ship is shown a little behind where it is)
            if (t.TryGetComponent(out RemotePlayer _)) { var mv = Motion.Of(t); if (mv != null) centre += mv.Velocity * (float)RemotePlayer.Delay; }
            along = t.forward;
        }

        /// <summary>Should `me` be kept out of `t`'s hull (both there, not on a tow, not docked or landed)?</summary>
        public static bool Solid(Transform t, CarrierController me) => Avoid(t, me, null);
    }
}

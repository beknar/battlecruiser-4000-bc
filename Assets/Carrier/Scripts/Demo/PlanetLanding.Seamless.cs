using System.Collections;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Flying down to a planet or moon and back up, without a break. The ship comes down through
    /// the air over the real-size body (PlanetFlight, PlanetTerrain); below `prepareHeight` the
    /// ground under it starts being built (the PlanetSurface, far below the system, following a
    /// stand-in for the ship), and at `handoffHeight` the ship goes over to it: the same place on
    /// the planet, the same height, heading, nose angle and speed, under the same sky, with a wisp
    /// of cloud as it goes. Down there it is the surface as before. Climbing back above the
    /// hand-off height takes it back to the air over the planet, where it was.
    ///
    /// K in orbit is the auto-lander: it picks the pad under the ship (or a hostile fortress's
    /// approach), flies down to it from orbit (cruising, slowing in the air), hands over and comes
    /// down on the pad. K on the ground is the climb back to orbit. Any flying (the stick, the
    /// keys) takes the controls back. Asteroids are landed on as before (a fade).
    /// </summary>
    public partial class PlanetLanding
    {
        [Header("Flying down")]
        [Tooltip("Above the body's mean surface (m): the ship goes over to the surface being built below it, or back (a world with air: its haze hides the edge of what is built).")]
        public float handoffHeight = 3200f;
        [Tooltip("The same on a moon with no air (lower: nothing hides the edge of what is built).")]
        public float airlessHandoffHeight = 2200f;
        [Tooltip("Below this (m above the mean surface) the ground under the ship starts being built.")]
        public float prepareHeight = 30000f;

        /// <summary>A planet or moon (flown down to); an asteroid field's rock is landed on with a fade.</summary>
        public static bool Seamless(WarpPoint b) => b != null && (b.kind == WarpPoint.Kind.Planet || b.kind == WarpPoint.Kind.Moon);

        /// <summary>The hand-off height for a world (above its cloud deck, for a gas giant).</summary>
        public float HandoffFor(SurfaceWorld w) =>
            w != null && !w.solid ? w.floorLevel + 3000f : w != null && w.kind == SurfaceKind.Moon ? airlessHandoffHeight : handoffHeight;

        /// <summary>The surface under the ship is being built (in space still).</summary>
        public bool Prepared => Surface != null && Current == State.InSpace;
        /// <summary>The auto-pilot is flying (down to the pad, or up to orbit).</summary>
        public bool Autopilot { get; private set; }
        public string AutopilotStage { get; private set; } = "";
        public int Handoffs { get; private set; }
        /// <summary>On the way down (flown by the auto-lander, or straight over to the surface).</summary>
        public bool GoingDown => jumping || (Autopilot && AutopilotStage != "climb" && AutopilotStage != "to orbit");
        /// <summary>On the way up to orbit (the auto-pilot's climb).</summary>
        public bool GoingUp => Autopilot && (AutopilotStage == "climb" || AutopilotStage == "to orbit");
        bool jumping;

        Transform focus;          // where the ship would be on the surface being built ahead of it
        WarpPoint preparedFor;
        float pulse;              // the wisp of cloud at a hand-off

        // ------------------------------------------------------------ where on the planet

        /// <summary>Where a world point is over a body: its place on the surface, and its height
        /// above the mean surface.</summary>
        void Under(WarpPoint body, Vector3 p, out double u, out double v, out float alt)
        {
            Vector3 c = body.Position;
            Vector3 local = body.transform.InverseTransformDirection(p - c);
            alt = local.magnitude - body.bodyRadius;
            World.FromDirection(local.sqrMagnitude > 1f ? local.normalized : Vector3.up, out u, out v);
        }

        /// <summary>The way east, up and north (world) at a place on a body.</summary>
        void Basis(WarpPoint body, double u, double v, out Vector3 east, out Vector3 up, out Vector3 north)
        {
            var bt = body.transform;
            up = bt.TransformDirection(World.Direction(u, v)).normalized;
            Vector3 n = bt.TransformDirection(World.Direction(u, v + 40.0) - World.Direction(u, v - 40.0));
            north = Vector3.ProjectOnPlane(n, up).normalized;
            if (north.sqrMagnitude < 0.5f) north = Vector3.ProjectOnPlane(bt.up, up).normalized;
            east = Vector3.Cross(up, north);
        }

        /// <summary>The surface's world point of a place (u near the surface's own origin) and height.</summary>
        Vector3 FlatAt(double u, double v, float height)
        {
            double du = SurfaceWorld.WrapDelta(u - Surface.OriginU, World.C);
            return Surface.WorldOf(Surface.OriginU + du, v, height);
        }

        // ------------------------------------------------------------ the ground built ahead

        /// <summary>Each frame in space: build the ground under the ship as it comes down, and go
        /// over to it at the hand-off height.</summary>
        void FlyDown()
        {
            var pf = PlanetFlight.Instance;
            var body = pf != null ? pf.Body : null;
            bool can = Seamless(body) && !carrier.Destroyed && (drive == null || drive.Current == WarpDrive.State.Ready) && !Docking.Busy;
            if (!can) { if (Prepared) Discard(); return; }
            if (Prepared && preparedFor != body) Discard();
            float alt = pf.Altitude;
            if (!Prepared)
            {
                if (alt < prepareHeight) Prepare(body);
                if (!Prepared) return;
            }
            else if (alt > prepareHeight + 8000f) { Discard(); return; }
            float hand = HandoffFor(World);
            Under(body, carrier.transform.position, out double u, out double v, out float a);
            focus.position = FlatAt(u, v, Mathf.Max(a, hand));
            if (a >= hand) return;
            if (Surface.Ready(focus.position)) { HandDown(body, u, v, a); return; }
            // (the ground isn't built yet: wait above the hand-off height for it)
            Vector3 c = body.Position, p = carrier.transform.position;
            carrier.transform.position = c + (p - c).normalized * (body.bodyRadius + hand);
        }

        void Prepare(WarpPoint body)
        {
            Body = body;
            World = WorldFor(body);
            preparedFor = body;
            Under(body, carrier.transform.position, out double u, out double v, out _);
            focus = new GameObject("Surface focus").transform;
            focus.position = SiteCenter + Vector3.up * HandoffFor(World);
            Material skyPlanet = null;
            if (body.kind == WarpPoint.Kind.Moon && body.orbitParent != null && body.orbitParent.TryGetComponent(out MeshRenderer pr)) skyPlanet = pr.sharedMaterial;
            var mem = WorldState.Body(body.displayName);
            // free what the last surface (or anything else) left behind before building this one
            SceneMemory.Collect($"before building {body.displayName}'s surface");
            Surface = PlanetSurface.Create(World, kit, focus, SiteCenter, u, v, mem.explored, mem.destroyed, skyPlanet);
            Surface.Running = true;
        }

        /// <summary>Climbed away before going down: the ground built ahead is let go.</summary>
        void Discard()
        {
            if (Surface != null) Destroy(Surface.gameObject);
            Surface = null;
            if (focus != null) Destroy(focus.gameObject);
            focus = null;
            preparedFor = null;
            SceneMemory.CollectSoon("let go of the ground built ahead", 2f);
        }

        // ------------------------------------------------------------ over to the surface, and back

        void HandDown(WarpPoint body, double u, double v, float a)
        {
            var tr = carrier.transform;
            Basis(body, u, v, out Vector3 east, out Vector3 up, out Vector3 north);
            Vector3 Flat(Vector3 w) => new Vector3(Vector3.Dot(w, east), Vector3.Dot(w, up), Vector3.Dot(w, north));
            Vector3 fwd = Flat(tr.forward), shipUp = Flat(tr.up);
            Vector3 toStar = OrbitalSystem.Instance != null ? Flat((OrbitalSystem.Instance.StarPosition - body.Position).normalized) : Vector3.up;
            Vector3 pos = FlatAt(u, v, a);
            orbitOffset = body.ArrivalPoint(tr.position) - body.Position;
            orbitRotation = tr.rotation;
            // the system out of the way (it would be in view of the surface otherwise)
            OrbitalSystem.Instance?.Park(true);
            ScaledSpace.Active = false;
            if (focus != null) Destroy(focus.gameObject);
            focus = null;
            preparedFor = null;
            Surface.Follow(tr);
            carrier.SetUpFrame(Quaternion.identity);
            tr.position = pos;
            carrier.TakeAttitude(Quaternion.LookRotation(fwd, shipUp));
            SetUpSite(body, World, u, v, toStar);
            Handoffs++;
            Pulse(Site.look.fog);
            World.LatLon(u, v, out float lat, out float lon);
            Notices.Post($"{body.displayName}, {LatLonText(lat, lon)}: {Site.description}{(World.hostile ? " (hostile)" : "")}");
        }

        /// <summary>The site: the surface's sea, pad and look, the ship flying over it.</summary>
        void SetUpSite(WarpPoint body, SurfaceWorld w, double u, double v, Vector3 toStar)
        {
            if (drive != null) drive.lockedBecause = "climb back to orbit first";
            int area = AreaOf(w, u, v);
            Seed = w.seed * 1000 + area % 1000;
            w.LatLon(u, v, out float lat, out _);
            Site = new SurfaceSite
            {
                surface = Surface, root = Surface.gameObject, kind = body.surface, center = SiteCenter,
                terrain = Surface.TerrainAt(carrier.transform.position), seaLevel = Surface.SeaLevel, solidGround = w.solid,
                floor = w.solid ? SiteCenter.y + SurfaceWorld.BaseLevel : Surface.CloudFloor,
                description = w.DescribeAt(lat), look = w.look,
                water = w.kind == SurfaceKind.Ocean && !float.IsNegativeInfinity(Surface.SeaLevel),
            };
            CombatPilot.FloorAt = Surface.FloorAt;
            if (Site.water)
            {
                var surface = Surface;
                Sea.Set(() => surface != null ? surface.SeaLevel : float.NegativeInfinity);
                if (!TryGetComponent(out Seafaring sf)) sf = gameObject.AddComponent<Seafaring>();
                sf.Begin(carrier, kit);
            }
            var outpost = Surface.NearestOutpost(carrier.transform.position, out float od);
            Site.hasOutpost = outpost != null && od < 30000f;
            Site.outpost = Site.hasOutpost ? outpost : null;
            Site.pad = Site.hasOutpost ? outpost.position : carrier.transform.position;
            Surface.Shifted += d => { if (Site != null) Site.pad += d; };
            // the sun where the star really is, seen from here
            ApplyLook(Site.look, toStar.y > -0.3f ? Quaternion.LookRotation(-toStar) : (Quaternion?)null);
            Landings++;
            Landed = false;
            touchedAt = -100f;
            carrier.externalMotion = false;
            Current = State.OnSurface;
        }

        /// <summary>Back up into the air over the planet, where the ship is over the surface.</summary>
        void HandUp()
        {
            var tr = carrier.transform;
            var body = Body;
            Surface.PlanetOf(tr.position, out double u, out double v);
            World.Canonical(u, v, out double cu, out double cv);
            float a = tr.position.y - Surface.Center.y;
            Vector3 fwdFlat = tr.forward, upFlat = tr.up;
            OrbitalSystem.Instance?.Park(false);
            ScaledSpace.Active = true;
            Basis(body, cu, cv, out Vector3 east, out Vector3 up, out Vector3 north);
            Vector3 World3(Vector3 f) => east * f.x + up * f.y + north * f.z;
            var look = Site.look;
            BackToSpace(false);
            tr.position = body.Position + up * (body.bodyRadius + a);
            carrier.SetUpFrame(Quaternion.FromToRotation(Vector3.up, up));
            carrier.TakeAttitude(Quaternion.LookRotation(World3(fwdFlat), World3(upFlat)));
            Handoffs++;
            Pulse(look.fog);
        }

        /// <summary>A wisp of cloud over the view (it hides the change of ground at a hand-off).</summary>
        void Pulse(Color c)
        {
            fadeColor = c;
            pulse = 0.85f;
        }

        void PulseUpdate()
        {
            if (pulse <= 0f) return;
            pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime * 1.1f);
            fade = Mathf.Max(fade, pulse);
            if (pulse <= 0f && Current != State.Descending && Current != State.Ascending) fade = 0f;
        }

        // ------------------------------------------------------------ at once (the self-test, the reels)

        /// <summary>Where to come down near a place on a body: the pad nearest it where people
        /// live, a few kilometres from a hostile fortress, or else just there.</summary>
        void Target(WarpPoint body, Vector3 over, out double tu, out double tv, out TileFeatures pad)
        {
            Under(body, over, out tu, out tv, out _);
            double keep = World.V - World.polarBand - 2500.0;
            if (keep > 0.0) tv = System.Math.Max(-keep, System.Math.Min(keep, tv));
            pad = World.outposts ? NearestSite(World, tu, tv, 4) : null;
            if (pad != null) { tu = pad.su; tv = pad.sv; }
            else if (World.NearBase(tu, tv, 3000.0, out double bu, out double bv, out bool hostileBase) && hostileBase) { tu = bu - HostileBaseStandoff; tv = bv; }
        }

        /// <summary>Straight over to the surface by a place (over it from the body's centre):
        /// the ground there built, then the ship over to it, coming in toward the pad.</summary>
        IEnumerator JumpDown(WarpPoint body, Vector3 over)
        {
            jumping = true;
            try { yield return JumpDownSteps(body, over); }
            finally { jumping = false; }
        }

        IEnumerator JumpDownSteps(WarpPoint body, Vector3 over)
        {
            World = WorldFor(body);
            if (Prepared && preparedFor != body) Discard();
            Target(body, over, out double tu, out double tv, out _);
            float hand = HandoffFor(World);
            Vector3 c = body.Position;
            Vector3 up = body.transform.TransformDirection(World.Direction(tu, tv)).normalized;
            carrier.transform.position = c + up * (body.bodyRadius + hand + 100f);
            carrier.SetUpFrame(Quaternion.FromToRotation(Vector3.up, up));
            carrier.Face(Vector3.ProjectOnPlane(carrier.transform.forward, up));
            carrier.SetMotion(0f);
            carrier.externalMotion = true;
            if (!Prepared) Prepare(body);
            Under(body, carrier.transform.position, out double u, out double v, out _);
            focus.position = FlatAt(u, v, hand);
            float until = Time.realtimeSinceStartup + 20f;
            while (Surface != null && !Surface.Ready(focus.position) && Time.realtimeSinceStartup < until) yield return null;
            carrier.externalMotion = false;
            if (Surface == null || Current != State.InSpace) yield break;
            Under(body, carrier.transform.position, out u, out v, out float a);
            HandDown(body, u, v, Mathf.Min(a, hand - 1f));
            pulse = 0f;
            fade = 0f;
            // (as a landing always began: a little way out from the pad, low, heading for it)
            ArriveOverPad();
        }

        /// <summary>Back up to orbit over where the ship is, at once (the self-test, the reels).</summary>
        void LeaveNow()
        {
            var body = Body;
            HandUp();
            pulse = 0f;
            fade = 0f;
            if (body == null) return;
            var tr = carrier.transform;
            Vector3 up = (tr.position - body.Position).normalized;
            tr.position = body.Position + (orbitOffset.sqrMagnitude > 1f ? orbitOffset : up * body.arrivalDistance);
            up = (tr.position - body.Position).normalized;
            carrier.SetUpFrame(Quaternion.FromToRotation(Vector3.up, up));
            carrier.Face(Vector3.ProjectOnPlane(-up + tr.forward, up).sqrMagnitude > 0.01f ? Vector3.ProjectOnPlane(tr.forward, up) : Vector3.Cross(up, Vector3.right));
            carrier.SetMotion(30f);
            carrier.throttle = Mathf.Max(carrier.throttle, 0.2f);
        }

        // ------------------------------------------------------------ the auto-pilots

        CarrierDemoInput input;
        CarrierDemoInput Input => input != null ? input : (input = FindAnyObjectByType<CarrierDemoInput>());

        /// <summary>Any flying from the pilot (the stick, the keys) takes the controls back from the auto-pilot.</summary>
        bool PilotTookOver() => Input != null && Input.PilotFlying;

        /// <summary>The elevator thrusters, through the controls (the pilot's own still win).</summary>
        void Lift(float k) { if (Input != null) Input.autoElevator = k == 0f ? (float?)null : Mathf.Clamp(k, -1f, 1f); }

        void StopAutopilot(string why)
        {
            if (!Autopilot) return;
            Autopilot = false;
            AutopilotStage = "";
            Lift(0f);
            if (PlanetFlight.Instance != null) PlanetFlight.Instance.autoCruise = false;
            if (why != null) Notices.Post(why);
        }

        /// <summary>Steer toward a world direction (turning at most so fast), the wings level.</summary>
        void Steer(Vector3 dir, float turn = 35f, float pitchRate = 18f)
        {
            carrier.Aim(dir, out float h, out float p);
            float dt = Time.deltaTime;
            carrier.SetAttitude(Mathf.MoveTowardsAngle(carrier.Heading, h, turn * dt), Mathf.MoveTowards(carrier.Pitch, p, pitchRate * dt));
        }

        /// <summary>Down from orbit to the pad under the ship (or a hostile fortress's approach).</summary>
        IEnumerator AutoLand(WarpPoint body)
        {
            Autopilot = true;
            AutopilotStage = "de-orbit";
            var pf = PlanetFlight.Instance;
            World = WorldFor(body);
            Target(body, carrier.transform.position, out double tu, out double tv, out var padSite);
            Notices.Post($"Auto-lander: down to {body.displayName}{(padSite != null ? ", the landing pad" : "")}");
            if (pf != null) pf.autoCruise = true;
            float hand = HandoffFor(World);
            while (Autopilot && Current == State.InSpace)
            {
                if (carrier.Destroyed || (pf != null && pf.Body != body)) { StopAutopilot("Auto-lander off"); yield break; }
                if (PilotTookOver()) { StopAutopilot("Auto-lander off: you have the controls"); yield break; }
                Vector3 c = body.Position, p = carrier.transform.position;
                Vector3 up = (p - c).normalized;
                Vector3 target = c + body.transform.TransformDirection(World.Direction(tu, tv)) * body.bodyRadius;
                float ground = body.bodyRadius * Mathf.Acos(Mathf.Clamp(Vector3.Dot(up, (target - c).normalized), -1f, 1f));
                float alt = (p - c).magnitude - body.bodyRadius;
                Vector3 horiz = Vector3.ProjectOnPlane(target - p, up);
                horiz = horiz.sqrMagnitude > 1f ? horiz.normalized : Vector3.ProjectOnPlane(carrier.transform.forward, up).normalized;
                // down a slope that reaches the hand-off height a few kilometres short of the pad
                float slope = -Mathf.Atan2(alt - (hand - 300f), Mathf.Max(ground - 6000f, 1f)) * Mathf.Rad2Deg;
                slope = Mathf.Clamp(slope, -carrier.maxPitch, 0f);
                AutopilotStage = alt > PlanetFlight.AirTop(body) ? "de-orbit" : "entry";
                Vector3 dir = horiz * Mathf.Cos(slope * Mathf.Deg2Rad) + up * Mathf.Sin(slope * Mathf.Deg2Rad);
                Steer(dir);
                carrier.throttle = 1f;
                yield return null;
            }
            if (!Autopilot) yield break;
            if (Current != State.OnSurface) { StopAutopilot(null); yield break; }
            if (pf != null) pf.autoCruise = false;
            yield return Approach();
        }

        /// <summary>Over the surface: to the pad (or straight down where there is none), and down onto it.</summary>
        IEnumerator Approach()
        {
            AutopilotStage = "approach";
            while (Autopilot && Current == State.OnSurface)
            {
                if (carrier.Destroyed) { StopAutopilot(null); yield break; }
                if (PilotTookOver()) { StopAutopilot("Auto-lander off: you have the controls"); yield break; }
                if (Landed) { StopAutopilot(OnPad ? "Auto-lander: down on the pad" : "Auto-lander: landed"); yield break; }
                var tr = carrier.transform;
                Vector3 p = tr.position;
                Vector3 pad = Site.hasOutpost ? PadNear(Site.pad) : new Vector3(p.x, Surface.FloorAt(p), p.z);
                Vector3 to = pad - p; to.y = 0f;
                float d = to.magnitude;
                float floor = Surface.FloorAt(p);
                float acc = carrier.acceleration * 0.8f;
                // as fast as it can stop by the pad
                float want = Mathf.Min(carrier.maxSpeed, Mathf.Sqrt(2f * acc * Mathf.Max(0f, d - 25f)));
                carrier.throttle = Mathf.Clamp01(want / carrier.maxSpeed);
                float height = d > 400f ? Mathf.Max(pad.y + 250f, floor + 180f) : pad.y + 60f;
                if (d > 40f)
                {
                    Vector3 dir = to.normalized;
                    float climb = Mathf.Clamp((height - p.y) / Mathf.Max(200f, d * 0.5f), -0.5f, 0.5f);
                    Steer(new Vector3(dir.x, climb, dir.z), 30f, 12f);
                }
                else
                {
                    AutopilotStage = "touchdown";
                    Steer(Vector3.ProjectOnPlane(tr.forward, Vector3.up), 10f, 12f);
                    carrier.throttle = 0f;
                }
                // the elevator thrusters for the height (set after the check above: they're the auto-pilot's own)
                float gap = (d > 40f ? height : floor - 5f) - p.y;
                Lift(gap / 40f);
                yield return null;
            }
            Lift(0f);
        }

        /// <summary>The pad to set down on by a landing site: a settlement's or industrial site's
        /// own pad, if there is one by it (where its screen opens), or else the site's.</summary>
        static Vector3 PadNear(Vector3 site)
        {
            Vector3 best = site;
            float bestD = 3000f * 3000f;
            foreach (var s in IndustrySite.All)
            {
                if (s == null || s.pad == null) continue;
                float d = (s.pad.position - site).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s.pad.position; }
            }
            return best;
        }

        /// <summary>Up from the ground and out of the air to orbit.</summary>
        IEnumerator AutoClimb()
        {
            Autopilot = true;
            AutopilotStage = "climb";
            var body = Body;
            Notices.Post($"Climbing to orbit of {body.displayName}");
            while (Autopilot && Current == State.OnSurface)
            {
                if (carrier.Destroyed) { StopAutopilot(null); yield break; }
                if (PilotTookOver()) { StopAutopilot("Auto-pilot off: you have the controls"); yield break; }
                var tr = carrier.transform;
                bool clear = Altitude > 60f && !Landed;
                Vector3 fwd = Vector3.ProjectOnPlane(tr.forward, Vector3.up).normalized;
                if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
                Steer(fwd * Mathf.Cos(30f * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(clear ? 30f * Mathf.Deg2Rad : 0f), 20f, 10f);
                carrier.throttle = clear ? 1f : 0.2f;
                Lift(clear ? 0f : 1f);
                yield return null;
            }
            Lift(0f);
            if (!Autopilot || Current != State.InSpace) { StopAutopilot(null); yield break; }
            var pf = PlanetFlight.Instance;
            if (pf != null) pf.autoCruise = true;
            float orbit = body.arrivalDistance - body.bodyRadius;
            while (Autopilot && Current == State.InSpace)
            {
                if (carrier.Destroyed) { StopAutopilot(null); yield break; }
                if (PilotTookOver()) { StopAutopilot("Auto-pilot off: you have the controls"); yield break; }
                Vector3 c = body.Position, p = carrier.transform.position;
                Vector3 up = (p - c).normalized;
                float alt = (p - c).magnitude - body.bodyRadius;
                if (alt >= orbit) break;
                Vector3 fwd = Vector3.ProjectOnPlane(carrier.transform.forward, up).normalized;
                float angle = Mathf.Lerp(40f, 10f, alt / orbit);
                Steer(fwd * Mathf.Cos(angle * Mathf.Deg2Rad) + up * Mathf.Sin(angle * Mathf.Deg2Rad), 20f, 10f);
                carrier.throttle = 1f;
                AutopilotStage = alt < PlanetFlight.AirTop(body) ? "climb" : "to orbit";
                yield return null;
            }
            if (!Autopilot) yield break;
            carrier.throttle = Mathf.Clamp01(30f / Mathf.Max(1f, carrier.maxSpeed));
            carrier.LevelOff();
            StopAutopilot($"In orbit of {body.displayName}");
        }
    }
}

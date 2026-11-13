using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The frigate's mini-warp drive, for hopping between places in the star system (WarpPoint).
    ///
    /// - Charging (chargeTime): the ship swings round onto the destination, sparks stream into
    ///   the hull and the engines run up. Taking fire while charging aborts the jump.
    /// - Warping: a flash and a shockwave ring as it jumps, then it crosses the distance with
    ///   streaks of starlight rushing past the camera. The trip takes longer the further it is:
    ///   baseTravelTime plus a second per `kmPerSecond` km, up to maxTravelTime.
    /// - Arriving: another flash and ring as it drops out, the streaks die away, and the ship
    ///   comes to rest facing the destination, with its throttle eased back.
    ///
    /// It can't be used in combat: while any hostile that has left cover is within combatRange,
    /// or the ship was hit in the last few seconds.
    ///
    /// This is the drive's rules and the flight; the sparks, flashes, tunnel and sounds are
    /// WarpEffects.
    /// </summary>
    [DefaultExecutionOrder(100)]   // LateUpdate after the camera's, so the warp tunnel sits on it
    public class WarpDrive : MonoBehaviour, IOriginShift
    {
        public enum State { Ready, Charging, Warping, Arriving }

        [Header("Timing")]
        public float chargeTime = 3f;
        public float arriveTime = 1.8f;
        public float baseTravelTime = 2.5f;
        public float kmPerSecond = 40f;
        public float maxTravelTime = 16f;

        [Header("Combat lock")]
        public float combatRange = 12000f;
        public float recentHitSeconds = 8f;

        [Header("Effects")]
        public Material streakMaterial;
        public Material hazeMaterial;
        public Material flashMaterial;
        public Material ringMaterial;
        public Material sparkMaterial;
        public Color warpColor = new Color(0.55f, 0.75f, 1f);

        public State Current { get; private set; }
        public WarpPoint Destination { get; private set; }
        public float StateTime => Time.time - stateStart;
        /// <summary>Length of the trip under way (s).</summary>
        public float TravelTime { get; private set; }
        /// <summary>Seconds left until the ship drops out of warp (charge included).</summary>
        public float TimeLeft => Current == State.Charging ? chargeTime - StateTime + TravelTime
                               : Current == State.Warping ? TravelTime - StateTime : 0f;
        public int Jumps { get; private set; }
        public float LastTravelSeconds { get; private set; }
        public float LastDistance { get; private set; }
        public string LastMessage { get; private set; } = "";
        /// <summary>0..1: how much of the screen the jump flash covers (drawn by the HUD).</summary>
        public float ScreenFlash { get; private set; }
        /// <summary>0..1: how strongly the warp tunnel is showing.</summary>
        public float TunnelLevel { get; private set; }
        public bool InWarp => Current == State.Warping;

        CarrierController ship;
        Health health;
        Shield shield;
        // the trip is a curve from `from` to `to` through `via` (a detour round any star, planet or
        // moon on the straight line)
        Vector3 from, to, via, travelDir, toOffset;
        float stateStart, chargeHitTime, arriveSpeed;
        WarpEffects fx;

        /// <summary>The world's origin moved (it is brought back to the ship all through a warp):
        /// the trip's points move with it.</summary>
        public void OriginShifted(Vector3 d)
        {
            from += d; to += d; via += d;
        }

        void Start()
        {
            ship = GetComponent<CarrierController>();
            health = GetComponent<Health>();
            shield = GetComponentInChildren<Shield>(true);
            fx = new WarpEffects(this);
        }

        // ------------------------------------------------------------ rules

        /// <summary>Trip time to a destination from where the ship is now (s).</summary>
        public float TravelTimeTo(WarpPoint p)
        {
            if (p == null) return 0f;
            float km = Vector3.Distance(transform.position, p.ArrivalPoint(transform.position)) / 1000f;
            return Mathf.Min(maxTravelTime, baseTravelTime + km / Mathf.Max(1f, kmPerSecond));
        }

        public float DistanceTo(WarpPoint p) => p == null ? 0f : Vector3.Distance(transform.position, p.ArrivalPoint(transform.position));

        /// <summary>When the ship last took a hit, on the shield or the hull.</summary>
        float LastHitTime => Mathf.Max(health != null ? health.LastHitTime : -100f, shield != null ? shield.LastHitTime : -100f);

        /// <summary>Is the ship in a fight (so the drive is locked)? Says why.</summary>
        public bool InCombat(out string why)
        {
            why = "";
            if (Time.time - LastHitTime < recentHitSeconds) { why = "under fire"; return true; }
            if (CombatPilot.AnyEngagedNear(transform.position, combatRange)) { why = "hostiles nearby"; return true; }
            if (DefenseGrid.AnyEngagedNear(transform.position, combatRange, out string grid)) { why = $"in range of {grid}"; return true; }
            return false;
        }

        /// <summary>Something else has the drive locked (landed on a planet): why, or null.</summary>
        [System.NonSerialized] public string lockedBecause;

        public bool CanWarp(out string why)
        {
            why = "";
            if (ship != null && ship.Destroyed) { why = "the frigate is destroyed"; return false; }
            if (Current != State.Ready) { why = "already in warp"; return false; }
            if (!string.IsNullOrEmpty(lockedBecause)) { why = lockedBecause; return false; }
            return !InCombat(out why);
        }

        /// <summary>Start a jump to a destination. Returns false (and says why) if it can't go.</summary>
        public bool Engage(WarpPoint p, out string why)
        {
            if (p == null) { why = "no destination"; return false; }
            if (!CanWarp(out why)) { LastMessage = "Can't warp: " + why; return false; }
            from = transform.position;
            to = p.ArrivalPoint(from);
            if (Vector3.Distance(from, to) < 1500f) { why = "already there"; LastMessage = "Already at " + p.displayName; return false; }
            Destination = p;
            travelDir = (to - from).normalized;
            TravelTime = TravelTimeTo(p);
            LastDistance = Vector3.Distance(from, to);
            Enter(State.Charging);
            chargeHitTime = LastHitTime;
            ship.externalMotion = true;
            fx.StartCharge(chargeTime);
            LastMessage = $"Warp drive charging: {p.displayName}";
            return true;
        }

        void Enter(State s)
        {
            Current = s;
            stateStart = Time.time;
        }

        // ------------------------------------------------------------ flight

        void Update()
        {
            float dt = Time.deltaTime;
            ScreenFlash = Mathf.MoveTowards(ScreenFlash, 0f, dt * 2.2f);
            if (ship == null) return;
            if (ship.Destroyed && Current != State.Ready) Cancel("the frigate was destroyed");

            switch (Current)
            {
                case State.Charging:
                {
                    float k = Mathf.Clamp01(StateTime / chargeTime);
                    // swing onto the destination, slowing to a crawl
                    TurnToward(travelDir, 50f, dt);
                    float speed = Mathf.MoveTowards(ship.Speed, 20f, 40f * dt);
                    ship.SetMotion(speed);
                    transform.position += transform.forward * speed * dt;
                    SetEngines(0.3f + 0.7f * k);
                    fx.Charging(k, dt);
                    if (LastHitTime > chargeHitTime) { Cancel("warp interrupted: under fire"); break; }
                    if (k >= 1f) Jump();
                    break;
                }
                case State.Warping:
                {
                    if (Destination != null) to = Destination.Position + toOffset;
                    float u = Mathf.Clamp01(StateTime / Mathf.Max(0.1f, TravelTime));
                    float s = Ease(u);
                    transform.position = PathAt(s);
                    Vector3 tangent = PathAt(Mathf.Min(1f, s + 0.01f)) - PathAt(Mathf.Max(0f, s - 0.01f));
                    if (tangent.sqrMagnitude > 1f) travelDir = tangent.normalized;
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(travelDir), 90f * dt);
                    TunnelLevel = Mathf.Clamp01(Mathf.Min(StateTime / 0.35f, (TravelTime - StateTime) / 0.5f + 0.15f));
                    if (u >= 1f) Arrive();
                    break;
                }
                case State.Arriving:
                {
                    float k = Mathf.Clamp01(StateTime / arriveTime);
                    TunnelLevel = Mathf.MoveTowards(TunnelLevel, 0f, dt * 2.5f);
                    if (Destination != null) TurnToward(Destination.Position - transform.position, 35f, dt);
                    float speed = Mathf.Lerp(arriveSpeed, 30f, k);
                    ship.SetMotion(speed);
                    transform.position += transform.forward * speed * dt;
                    SetEngines(Mathf.Lerp(1f, 0.2f, k));
                    if (k >= 1f) Finish();
                    break;
                }
                default:
                    TunnelLevel = Mathf.MoveTowards(TunnelLevel, 0f, dt * 3f);
                    break;
            }
        }

        // the camera has moved by now (it follows in LateUpdate), so the tunnel goes on it here
        void LateUpdate() => fx?.Follow(TunnelLevel, travelDir);

        void OnDestroy() => fx?.Dispose();

        void OnGUI() => fx?.Draw(TunnelLevel, ScreenFlash);

        /// <summary>Ease in and out: a hard surge away, a long rush, a hard stop.</summary>
        static float Ease(float u) => u < 0.5f ? 4f * u * u * u : 1f - Mathf.Pow(-2f * u + 2f, 3f) / 2f;

        /// <summary>A point along the trip (0 = start, 1 = arrival).</summary>
        Vector3 PathAt(float s)
        {
            float r = 1f - s;
            return r * r * from + 2f * r * s * via + s * s * to;
        }

        /// <summary>The bend that takes the trip clear of the bodies on the straight line: the
        /// closest point to the worst one is pushed out past it, and the curve passes through there.</summary>
        static Vector3 Detour(Vector3 a, Vector3 b)
        {
            Vector3 mid = (a + b) * 0.5f;
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1f) return mid;
            Vector3 best = mid;
            float worst = 0f;
            foreach (var w in WarpPoint.All)
            {
                if (w == null || w.bodyRadius <= 0f) continue;
                float clear = w.bodyRadius * 1.6f + 1500f;
                Vector3 c = w.Position;
                float t = Mathf.Clamp(Vector3.Dot(c - a, ab) / len2, 0.05f, 0.95f);
                Vector3 onLine = a + ab * t;
                Vector3 off = onLine - c;
                float d = off.magnitude;
                if (d >= clear || clear - d <= worst) continue;
                worst = clear - d;
                if (d < 1f) off = Vector3.Cross(ab, Vector3.up).sqrMagnitude > 1f ? Vector3.Cross(ab, Vector3.up) : Vector3.right;
                best = c + off.normalized * clear;
            }
            // a quadratic curve through `best` at its middle has its control point here
            return worst > 0f ? 2f * best - mid : mid;
        }

        void Jump()
        {
            Enter(State.Warping);
            from = transform.position;
            to = Destination.ArrivalPoint(from);
            // the destination moves along its orbit during the trip: the arrival point goes with it
            toOffset = to - Destination.Position;
            via = Detour(from, to);
            travelDir = (via - from).sqrMagnitude > 1f ? (via - from).normalized : (to - from).normalized;
            fx.Jumped(transform.position);
            ScreenFlash = 1f;
            LastMessage = $"In warp: {Destination.displayName}";
        }

        void Arrive()
        {
            transform.position = to;
            LastTravelSeconds = StateTime;
            Jumps++;
            Enter(State.Arriving);
            arriveSpeed = 140f;
            fx.Arrived(transform.position + travelDir * 120f);
            ScreenFlash = 0.75f;
            LastMessage = $"Arrived: {Destination.displayName}";
        }

        void Finish()
        {
            Enter(State.Ready);
            ship.externalMotion = false;
            ship.throttle = Mathf.Clamp01(30f / Mathf.Max(1f, ship.maxSpeed));
            ship.SetSpawn(transform.position, transform.rotation, Destination != null ? Destination.transform : null);
        }

        void Cancel(string why)
        {
            fx.Stopped();
            if (Current == State.Warping) transform.position = PathAt(Ease(Mathf.Clamp01(StateTime / Mathf.Max(0.1f, TravelTime))));
            Enter(State.Ready);
            ship.externalMotion = false;
            LastMessage = char.ToUpper(why[0]) + why.Substring(1);
            Notices.Post(LastMessage);
        }

        /// <summary>Turn the ship's nose toward a direction (no roll), through the controller so its
        /// own heading and pitch stay in step.</summary>
        void TurnToward(Vector3 dir, float degPerSec, float dt)
        {
            if (dir.sqrMagnitude < 1e-4f) return;
            ship.Aim(dir, out float wantHeading, out float wantPitch);
            ship.SetAttitude(Mathf.MoveTowardsAngle(ship.Heading, wantHeading, degPerSec * dt),
                             Mathf.MoveTowards(ship.Pitch, wantPitch, degPerSec * 0.6f * dt));
        }

        void SetEngines(float k)
        {
            if (ship.engines == null) return;
            foreach (var e in ship.engines) if (e) e.throttle = k;
        }
    }
}

using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Flying near a planet or moon, in space. The planets are hundreds of kilometres across, so:
    ///
    /// - **Up** is out from the body (CarrierController.upFrame), turned to gradually within a
    ///   body's radius or so of its surface: the ship flies level over the ground wherever it is.
    /// - **Cruise** (O, or the auto-pilots): out of combat and clear of stations and ships, the
    ///   top speed and the acceleration grow with the height above the surface (`cruisePerMetre`
    ///   of it a second, up to `cruiseMax`), so the ship comes down from orbit in a minute or so
    ///   and slows as it nears the ground.
    /// - **Air**: a world with an atmosphere thickens toward the ground (exp(-height/scale)); the
    ///   thicker it is the lower the cruise speed, and too fast in thick air the hull glows (the
    ///   entry plasma), shakes, and at worst takes heat damage.
    ///
    /// Down on a surface (PlanetLanding) the surface's own flight takes over and this rests.
    /// </summary>
    [DefaultExecutionOrder(-50)]   // before the controller moves the ship
    public partial class PlanetFlight : MonoBehaviour
    {
        public CarrierController ship;
        [Tooltip("Cruise speed per metre of height above the surface (1/s).")]
        public float cruisePerMetre = 0.07f;
        public float cruiseMax = 15000f;
        [Tooltip("Within this of a station or another ship, no cruise (m).")]
        public float massLock = 6000f;

        public static PlanetFlight Instance { get; private set; }

        /// <summary>Cruise switched on (O).</summary>
        public bool Cruise { get; private set; }
        /// <summary>The auto-pilots (landing, climbing back to orbit) cruise whatever the switch says.</summary>
        [System.NonSerialized] public bool autoCruise;
        /// <summary>Cruising now (switched on and allowed here).</summary>
        public bool Cruising { get; private set; }
        public string CruiseBlockedBy { get; private set; } = "";
        /// <summary>The body the ship is nearest (by its height above the surface), or null.</summary>
        public WarpPoint Body { get; private set; }
        /// <summary>Height above the body's mean surface (m).</summary>
        public float Altitude { get; private set; } = float.PositiveInfinity;
        /// <summary>The air's thickness here, 0 (space) to 1 (at the surface).</summary>
        public float Density { get; private set; }
        /// <summary>0..1: how hot the hull is running from the air.</summary>
        public float Heat { get; private set; }
        public float TopSpeed { get; private set; }
        /// <summary>How far up has turned toward out-from-the-body (0..1).</summary>
        public float UpBlend { get; private set; }

        WarpDrive drive;
        Health health;
        float nextCheck;
        bool combat;
        string combatWhy = "";

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (ship == null) ship = GetComponent<CarrierController>();
            drive = ship != null ? ship.GetComponent<WarpDrive>() : null;
            health = ship != null ? ship.GetComponent<Health>() : null;
        }

        public void ToggleCruise()
        {
            Cruise = !Cruise;
            Notices.Post(Cruise ? "Cruise on: the higher, the faster" : "Cruise off");
        }

        /// <summary>The air of a body: its scale height (m, 0: none) and where it ends.</summary>
        public static float ScaleHeight(WarpPoint w)
        {
            if (w == null || w.kind != WarpPoint.Kind.Planet) return 0f;
            return Mathf.Clamp(w.bodyRadius * 0.035f, 6000f, 18000f);
        }
        public static float AirTop(WarpPoint w) => ScaleHeight(w) * 8f;
        public static float DensityAt(WarpPoint w, float altitude)
        {
            float h = ScaleHeight(w);
            if (h <= 0f || altitude >= h * 8f) return 0f;
            return Mathf.Exp(-Mathf.Max(0f, altitude) / h);
        }

        /// <summary>The body nearest the point by height above its surface (planets and moons).</summary>
        public static WarpPoint NearestBody(Vector3 p, out float altitude)
        {
            WarpPoint best = null;
            altitude = float.PositiveInfinity;
            foreach (var w in WarpPoint.All)
            {
                if (w == null || w.bodyRadius <= 0f || (w.kind != WarpPoint.Kind.Planet && w.kind != WarpPoint.Kind.Moon)) continue;
                float a = Vector3.Distance(p, w.Position) - w.bodyRadius;
                if (a < altitude) { altitude = a; best = w; }
            }
            return best;
        }

        void Update()
        {
            if (ship == null) return;
            float dt = Time.deltaTime;
            bool inSpace = !PlanetLanding.Busy;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (inSpace && kb != null && kb.oKey.wasPressedThisFrame && !InputFocus.PanelUp) ToggleCruise();
            Vector3 p = ship.transform.position;
            Body = inSpace ? NearestBody(p, out float alt) : null;
            Altitude = Body != null ? Vector3.Distance(p, Body.Position) - Body.bodyRadius : float.PositiveInfinity;
            Density = Body != null ? DensityAt(Body, Altitude) : 0f;

            // ---- up: out from the body, turned to gradually
            Vector3 target = Vector3.up;
            UpBlend = 0f;
            if (Body != null)
            {
                float r = Body.bodyRadius;
                UpBlend = 1f - SurfaceWorld.Smooth(r * 0.6f, r * 1.6f, Altitude);
                Vector3 radial = (p - Body.Position).normalized;
                if (UpBlend > 0f) target = Vector3.Slerp(Vector3.up, radial, UpBlend).normalized;
            }
            if (!inSpace) target = Vector3.up;
            Vector3 up = ship.Up;
            // (moored at a berth or on a pad: it stays as it is; in a warp, or just out of one, it
            // is at once whatever the place it arrives at has)
            bool moored = Docking.Instance != null && Docking.Instance.Current != Docking.State.Free;
            bool jumped = drive != null && drive.Current != WarpDrive.State.Ready;
            if (Vector3.Angle(up, target) > 0.001f && !ship.Destroyed && !moored)
            {
                // (deep in, it follows the radial as the ship moves; blending in or out, it turns at most so fast)
                float rate = UpBlend > 0.98f ? 30f : 8f;
                Vector3 next = jumped ? target : Vector3.RotateTowards(up, target, rate * Mathf.Deg2Rad * dt, 0f);
                ship.SetUpFrame(Quaternion.FromToRotation(up, next) * ship.upFrame);
            }

            // ---- cruise and the air
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 0.25f;
                combat = drive != null && drive.InCombat(out combatWhy);
            }
            float top = ship.maxSpeed;
            Cruising = false;
            CruiseBlockedBy = "";
            if (inSpace && (Cruise || autoCruise) && !ship.externalMotion && !ship.Destroyed)
            {
                float locked = MassLockDistance(p);
                if (combat) CruiseBlockedBy = combatWhy;
                else if (locked < massLock) CruiseBlockedBy = "too near a station";
                else
                {
                    float height = Body != null ? Altitude : 1e7f;
                    float cruise = Mathf.Clamp(height * cruisePerMetre, ship.maxSpeed, cruiseMax);
                    cruise = Mathf.Min(cruise, ship.maxSpeed + (locked - massLock) * cruisePerMetre);
                    // the thicker the air, the slower
                    float thin = 1f - Density;
                    top = Mathf.Max(ship.maxSpeed, ship.maxSpeed + (cruise - ship.maxSpeed) * thin * thin * thin * thin);
                    Cruising = top > ship.maxSpeed * 1.05f;
                }
            }
            TopSpeed = top;
            if (inSpace)
            {
                ship.speedScale = top / Mathf.Max(1f, ship.maxSpeed);
                ship.accelScale = Mathf.Max(1f, ship.speedScale * 0.5f);
            }
            else if (ship.accelScale != 1f) ship.accelScale = 1f;

            // ---- heating: speed in thick air
            float q = Density * ship.Speed * ship.Speed;                 // (dynamic pressure, nearly)
            float heat = Mathf.Clamp01((q - 2.0e5f) / 1.2e6f);
            Heat = Mathf.MoveTowards(Heat, heat, dt * 0.8f);
            if (Heat > 0.85f && health != null && !health.Dead) health.Damage(Heat * 40f * dt, ship.transform.position, HitKind.Beam);
            if (Heat > 0.05f) PadSupport.Rumble(0.15f * Heat, 0.25f * Heat, 0.1f);
        }

        /// <summary>How far the nearest thing that holds the ship out of cruise is (a station, a
        /// settlement, an installation: anything that isn't a body or a field).</summary>
        static float MassLockDistance(Vector3 p)
        {
            float best = float.PositiveInfinity;
            foreach (var w in WarpPoint.All)
            {
                if (w == null) continue;
                switch (w.kind)
                {
                    case WarpPoint.Kind.Planet: case WarpPoint.Kind.Moon: case WarpPoint.Kind.Star: case WarpPoint.Kind.AsteroidField: continue;
                }
                float d = Vector3.Distance(p, w.Position);
                if (d < best) best = d;
            }
            return best;
        }
    }
}

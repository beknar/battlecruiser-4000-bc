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
    /// </summary>
    [DefaultExecutionOrder(100)]   // LateUpdate after the camera's, so the warp tunnel sits on it
    public class WarpDrive : MonoBehaviour
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
        AudioSource chargeVoice;
        // the trip is a curve from `from` to `to` through `via` (a detour round any star, planet or
        // moon on the straight line)
        Vector3 from, to, via, travelDir;
        float stateStart, chargeHitTime, baseFov = -1f, arriveSpeed, glowTimer;
        Texture2D vignette;
        ParticleSystem tunnel, haze, sparks, flash, ring;
        Light flashLight;
        AudioSource loop;

        void Start()
        {
            ship = GetComponent<CarrierController>();
            health = GetComponent<Health>();
            shield = GetComponentInChildren<Shield>(true);
            BuildEffects();
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
            chargeVoice = SoundFX.PlayFitted(Sfx.WarpCharge, transform.position, chargeTime, true);   // ends as it jumps
            sparks.Play();
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
                    var em = sparks.emission;
                    em.rateOverTimeMultiplier = Mathf.Lerp(60f, 900f, k * k);
                    // a blue glow building on the hull
                    flashLight.transform.position = transform.position;
                    flashLight.intensity = Mathf.Max(flashLight.intensity, 25000f * k * k);
                    glowTimer -= dt;
                    if (glowTimer <= 0f)
                    {
                        glowTimer = 0.08f;
                        var gp = new ParticleSystem.EmitParams { position = transform.position, applyShapeToPosition = false };
                        gp.startSize = 80f + 380f * k * k;
                        gp.startColor = new Color(warpColor.r, warpColor.g, warpColor.b, 0.25f + 0.35f * k);
                        flash.Emit(gp, 1);
                    }
                    if (LastHitTime > chargeHitTime) { Cancel("warp interrupted: under fire"); break; }
                    if (k >= 1f) Jump();
                    break;
                }
                case State.Warping:
                {
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
        void LateUpdate() => UpdateTunnel();

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
            chargeVoice = null;
            from = transform.position;
            to = Destination.ArrivalPoint(from);
            via = Detour(from, to);
            travelDir = (via - from).sqrMagnitude > 1f ? (via - from).normalized : (to - from).normalized;
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Burst(transform.position);
            ScreenFlash = 1f;
            SoundFX.Play(Sfx.WarpJump, transform.position);
            if (loop != null) { loop.volume = 0f; loop.Play(); }
            tunnel.Play();
            haze.Play();
            LastMessage = $"In warp: {Destination.displayName}";
        }

        void Arrive()
        {
            transform.position = to;
            LastTravelSeconds = StateTime;
            Jumps++;
            Enter(State.Arriving);
            arriveSpeed = 140f;
            Burst(transform.position + travelDir * 120f);
            ScreenFlash = 0.75f;
            SoundFX.Play(Sfx.WarpExit, transform.position);
            if (loop != null) loop.Stop();
            tunnel.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            haze.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            LastMessage = $"Arrived: {Destination.displayName}";
        }

        void Finish()
        {
            Enter(State.Ready);
            ship.externalMotion = false;
            ship.throttle = Mathf.Clamp01(30f / Mathf.Max(1f, ship.maxSpeed));
            ship.SetSpawn(transform.position, transform.rotation);
        }

        void Cancel(string why)
        {
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            tunnel.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            haze.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (loop != null) loop.Stop();
            if (chargeVoice != null) { chargeVoice.Stop(); chargeVoice = null; }
            if (Current == State.Warping) transform.position = PathAt(Ease(Mathf.Clamp01(StateTime / Mathf.Max(0.1f, TravelTime))));
            Enter(State.Ready);
            ship.externalMotion = false;
            LastMessage = char.ToUpper(why[0]) + why.Substring(1);
            Demo.CarrierDemoInput.Toast(LastMessage);
        }

        /// <summary>Turn the ship's nose toward a direction (no roll), through the controller so its
        /// own heading and pitch stay in step.</summary>
        void TurnToward(Vector3 dir, float degPerSec, float dt)
        {
            if (dir.sqrMagnitude < 1e-4f) return;
            float wantHeading = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float wantPitch = Mathf.Clamp(Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg, -ship.maxPitch, ship.maxPitch);
            ship.SetAttitude(Mathf.MoveTowardsAngle(ship.Heading, wantHeading, degPerSec * dt),
                             Mathf.MoveTowards(ship.Pitch, wantPitch, degPerSec * 0.6f * dt));
        }

        void SetEngines(float k)
        {
            if (ship.engines == null) return;
            foreach (var e in ship.engines) if (e) e.throttle = k;
        }

        // ------------------------------------------------------------ effects

        void BuildEffects()
        {
            // streaks of starlight rushing past the camera, and a soft blue haze down the tunnel
            tunnel = MakePs("Warp streaks", streakMaterial, 4000, false);
            {
                var m = tunnel.main;
                m.startLifetime = 0.75f;
                m.startSpeed = -4200f;
                m.startSize = new ParticleSystem.MinMaxCurve(4f, 11f);
                m.startColor = new ParticleSystem.MinMaxGradient(warpColor, Color.white);
                var sh = tunnel.shape;
                sh.enabled = true;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 1f;
                sh.radius = 650f;
                sh.radiusThickness = 0.85f;
                sh.position = new Vector3(0f, 0f, 2700f);
                var col = tunnel.colorOverLifetime;
                col.enabled = true;
                col.color = Fade(0.12f, 0.85f);
                var r = tunnel.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.065f;
                r.lengthScale = 2f;
            }
            haze = MakePs("Warp haze", hazeMaterial, 200, false);
            {
                var m = haze.main;
                m.startLifetime = 1.3f;
                m.startSpeed = -1800f;
                m.startSize = new ParticleSystem.MinMaxCurve(500f, 1100f);
                m.startColor = new Color(warpColor.r * 0.5f, warpColor.g * 0.55f, warpColor.b, 0.22f);
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var sh = haze.shape;
                sh.enabled = true;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 1f;
                sh.radius = 900f;
                sh.position = new Vector3(0f, 0f, 2200f);
                var col = haze.colorOverLifetime;
                col.enabled = true;
                col.color = Fade(0.25f, 0.7f);
            }
            // sparks drawn into the hull while the drive charges
            sparks = MakePs("Warp charge", sparkMaterial, 1500, false);
            sparks.transform.SetParent(transform, false);
            {
                var m = sparks.main;
                m.startLifetime = 1.1f;
                m.startSpeed = new ParticleSystem.MinMaxCurve(-120f, -70f);
                m.startSize = new ParticleSystem.MinMaxCurve(9f, 22f);
                m.startColor = new ParticleSystem.MinMaxGradient(warpColor, Color.white);
                var sh = sparks.shape;
                sh.enabled = true;
                sh.shapeType = ParticleSystemShapeType.Sphere;
                sh.radius = 160f;
                sh.radiusThickness = 0.1f;
                var col = sparks.colorOverLifetime;
                col.enabled = true;
                col.color = Fade(0.3f, 0.7f);
                var size = sparks.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            }
            // the jump and arrival flash, and its shockwave ring (both emitted on demand)
            flash = MakePs("Warp flash", flashMaterial, 8, true);
            {
                var m = flash.main;
                m.startLifetime = 0.55f;
                m.startSpeed = 0f;
                var size = flash.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0.6f)));
                var col = flash.colorOverLifetime;
                col.enabled = true;
                col.color = Fade(0.02f, 0.25f);
                var em = flash.emission;
                em.enabled = false;
            }
            ring = MakePs("Warp ring", ringMaterial, 8, true);
            {
                var m = ring.main;
                m.startLifetime = 0.9f;
                m.startSpeed = 0f;
                var size = ring.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.08f, 0f, 3f), new Keyframe(1f, 1f, 0.3f, 0f)));
                var col = ring.colorOverLifetime;
                col.enabled = true;
                col.color = Fade(0.02f, 0.4f);
                var em = ring.emission;
                em.enabled = false;
            }
            flashLight = new GameObject("Warp flash light").AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = warpColor;
            flashLight.range = 2500f;
            flashLight.intensity = 0f;
            flashLight.shadows = LightShadows.None;

            var clips = SoundFX.LoadLibrary("WarpLoop");
            if (clips != null)
            {
                loop = gameObject.AddComponent<AudioSource>();
                loop.clip = clips[0];
                loop.loop = true;
                loop.spatialBlend = 0f;
                loop.playOnAwake = false;
                loop.volume = 0f;
            }
        }

        static Gradient Fade(float inAt, float outAt)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inAt), new GradientAlphaKey(1f, outAt), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static ParticleSystem MakePs(string name, Material mat, int max, bool world)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = true;
            m.playOnAwake = false;
            m.maxParticles = max;
            m.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.maxParticleSize = 50f;    // let the flashes fill the screen
            return ps;
        }

        /// <summary>A flash, a shockwave ring and a burst of light at a point.</summary>
        void Burst(Vector3 at)
        {
            var p = new ParticleSystem.EmitParams { position = at, applyShapeToPosition = false };
            p.startSize = 900f;
            p.startColor = Color.white;
            flash.Emit(p, 1);
            p.startSize = 650f;
            p.startColor = warpColor;
            flash.Emit(p, 1);
            p.startSize = 2600f;
            p.startColor = new Color(warpColor.r, warpColor.g, warpColor.b, 0.9f);
            ring.Emit(p, 1);
            flashLight.transform.position = at;
            flashLight.intensity = 60000f;
        }

        void UpdateTunnel()
        {
            var cam = Camera.main;
            float lvl = TunnelLevel;
            if (cam != null)
            {
                Quaternion along = Quaternion.LookRotation(travelDir.sqrMagnitude > 0.1f ? travelDir : transform.forward);
                tunnel.transform.SetPositionAndRotation(cam.transform.position, along);
                haze.transform.SetPositionAndRotation(cam.transform.position, along);
                if (baseFov < 0f) baseFov = cam.fieldOfView;
                cam.fieldOfView = baseFov + 18f * lvl;
            }
            var em = tunnel.emission;
            em.rateOverTimeMultiplier = 5200f * lvl;
            var hm = haze.emission;
            hm.rateOverTimeMultiplier = 30f * lvl;
            if (loop != null && loop.isPlaying) loop.volume = 0.55f * lvl;
            if (flashLight.intensity > 0f) flashLight.intensity = Mathf.MoveTowards(flashLight.intensity, 0f, 120000f * Time.deltaTime);
        }

        static Texture2D MakeVignette(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.41f;
                    float a = Mathf.Clamp01((r - 0.25f) / 0.75f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        void OnDestroy()
        {
            if (tunnel) Destroy(tunnel.gameObject);
            if (haze) Destroy(haze.gameObject);
            if (flash) Destroy(flash.gameObject);
            if (ring) Destroy(ring.gameObject);
            if (flashLight) Destroy(flashLight.gameObject);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (TunnelLevel > 0.01f)
            {
                if (vignette == null) vignette = MakeVignette(128);
                var v = GUI.color;
                GUI.color = new Color(warpColor.r * 0.6f, warpColor.g * 0.7f, warpColor.b, 0.55f * TunnelLevel);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignette);
                GUI.color = v;
            }
            if (ScreenFlash <= 0.01f) return;
            var c = GUI.color;
            GUI.color = new Color(0.85f, 0.92f, 1f, Mathf.Clamp01(ScreenFlash) * 0.85f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = c;
        }
    }
}

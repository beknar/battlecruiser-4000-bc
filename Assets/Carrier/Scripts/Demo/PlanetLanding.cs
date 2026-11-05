using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Landing on the planets, moons and asteroids of the star system. In orbit of somewhere with
    /// a surface (inside its arrival zone) and out of combat, K (LB on the pad) takes the frigate
    /// down: the screen fades through the entry glow and the ship comes in low over the ground
    /// under where it was, near the nearest landing pad if anyone lives there. The whole planet
    /// can be flown over: its surface (a SurfaceWorld, sized from the body) is built in 5 km
    /// areas round the ship as it goes (PlanetSurface), the same each time, and wraps all the way
    /// round. Hostile worlds have ground forces. On the surface the ship flies as in space; it
    /// can't sink into the ground (or the sea, or the lava, or the deep clouds), and it settles
    /// when it touches down slowly. K again, or climbing high above the ground, takes it back to
    /// orbit where it left. The surface is built far below the star system (nothing there is in
    /// view from it) and thrown away on take-off; the sky, fog and light are swapped for the
    /// world's own.
    /// </summary>
    [DefaultExecutionOrder(300)]   // after the ship moves and the camera follows it
    public class PlanetLanding : MonoBehaviour
    {
        public enum State { InSpace, Descending, OnSurface, Ascending }

        public CarrierController carrier;
        public WarpDrive drive;
        [Tooltip("The scene's second, fill light (dimmed on the surface).")]
        public Light fill;
        public SurfaceKit kit = new SurfaceKit();
        [Tooltip("Landing is possible within this many times a place's arrival distance.")]
        public float landingRange = 1.3f;
        public float fadeTime = 1.3f;
        [Tooltip("Climbing this far above the highest ground takes the ship back to orbit (m).")]
        public float ceiling = 1800f;
        [Tooltip("A body's surface is this many times its size in space (the bodies out there are drawn small).")]
        public float surfaceScale = 4f;

        /// <summary>Where the landing sites are built: well away from everything in the system.</summary>
        public static readonly Vector3 SiteCenter = new Vector3(0f, -320000f, 0f);

        public static PlanetLanding Instance { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        public State Current { get; private set; }
        /// <summary>On a surface, or on the way down or up (no warp, no system map).</summary>
        public static bool Busy => Instance != null && Instance.Current != State.InSpace;
        public static bool OnSurface => Instance != null && Instance.Current == State.OnSurface;
        /// <summary>Fading down or up: the controls are off.</summary>
        public static bool Transitioning => Instance != null && (Instance.Current == State.Descending || Instance.Current == State.Ascending);

        public WarpPoint Body { get; private set; }
        public SurfaceSite Site { get; private set; }
        /// <summary>The planet's surface being flown over (null in space).</summary>
        public PlanetSurface Surface { get; private set; }
        public SurfaceWorld World { get; private set; }
        /// <summary>Resting on the ground (or the pad).</summary>
        public bool Landed { get; private set; }
        public bool OnPad { get; private set; }
        /// <summary>Height of the hull's underside above whatever is below it (m).</summary>
        public float Altitude { get; private set; }
        public int Landings { get; private set; }
        public int Touchdowns { get; private set; }
        public int Seed { get; private set; }
        public float LastBuildSeconds { get; private set; }

        float fade;
        Color fadeColor = Color.black;
        Vector3 orbitPosition;
        Quaternion orbitRotation;

        void Awake()
        {
            Instance = this;
            if (carrier != null && drive == null) drive = carrier.GetComponent<WarpDrive>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (Site != null) CombatPilot.FloorAt = null;   // (the surface may already be gone in a scene change)
            Site?.Destroy();
        }

        // ------------------------------------------------------------ where can we land

        /// <summary>The place the ship could land on now (null if none), and if it can't yet, why.</summary>
        public WarpPoint Candidate(out string why)
        {
            why = "";
            if (carrier == null) return null;
            WarpPoint best = null;
            float bestRatio = float.MaxValue;
            foreach (var w in WarpPoint.All)
            {
                if (w == null || w.surface == SurfaceKind.None) continue;
                float ratio = Vector3.Distance(carrier.transform.position, w.Position) / Mathf.Max(1f, w.arrivalDistance);
                if (ratio < landingRange && ratio < bestRatio) { bestRatio = ratio; best = w; }
            }
            if (best == null) return null;
            if (carrier.Destroyed) why = "the frigate is destroyed";
            else if (drive != null && drive.Current != WarpDrive.State.Ready) why = "in warp";
            else if (drive != null && drive.InCombat(out string c)) why = c;
            return best;
        }

        public bool CanLand(out WarpPoint body, out string why)
        {
            body = Candidate(out why);
            if (body == null && why == "") why = "not in orbit of anywhere with a surface";
            return Current == State.InSpace && body != null && why == "";
        }

        /// <summary>Land on the place in orbit (the K key). Returns false (and says why) if it can't.</summary>
        public bool Land()
        {
            if (!CanLand(out var body, out string why))
            {
                CarrierDemoInput.Toast(body != null ? $"Can't land on {body.displayName}: {why}" : $"Can't land: {why}");
                SoundFX.Ui(Sfx.UiError);
                return false;
            }
            StartCoroutine(Descend(body));
            return true;
        }

        /// <summary>Land on a place from wherever the ship is (the self-test), coming back here on take-off.</summary>
        public bool LandOn(WarpPoint body)
        {
            if (Current != State.InSpace || body == null || body.surface == SurfaceKind.None || carrier.Destroyed) return false;
            StartCoroutine(Descend(body));
            return true;
        }

        public bool TakeOff()
        {
            if (Current != State.OnSurface) return false;
            StartCoroutine(Ascend());
            return true;
        }

        // ------------------------------------------------------------ down and up

        IEnumerator Descend(WarpPoint body)
        {
            Current = State.Descending;
            Body = body;
            if (drive != null) drive.lockedBecause = "take off first";
            orbitPosition = carrier.transform.position;
            orbitRotation = carrier.transform.rotation;
            carrier.SetTarget(null);
            carrier.externalMotion = true;
            bool air = body.surface != SurfaceKind.Moon && body.surface != SurfaceKind.Asteroid;
            fadeColor = air ? Color.Lerp(body.mapColor, new Color(1f, 0.75f, 0.5f), 0.5f) : new Color(0.02f, 0.02f, 0.03f);
            SoundFX.Play(Sfx.WarpCharge, carrier.transform.position, 0.7f, 0.6f);
            CarrierDemoInput.Toast($"Descending to {body.displayName}");
            PadSupport.Rumble(0.4f, 0.2f, fadeTime);
            Vector3 toBody = (body.Position - carrier.transform.position).normalized;
            for (float t = 0f; t < fadeTime; t += Time.deltaTime)
            {
                fade = t / fadeTime;
                carrier.transform.position += (carrier.transform.forward * 80f + toBody * 200f * fade) * Time.deltaTime;
                yield return null;
            }
            fade = 1f;
            yield return null;

            // the ground under the point the ship was over
            Vector3 world = orbitPosition - body.Position;
            Vector3 local = body.kind == WarpPoint.Kind.AsteroidField ? world : body.transform.InverseTransformDirection(world);
            local = local.sqrMagnitude > 1f ? local.normalized : Vector3.up;
            var w = WorldFor(body);
            World = w;
            w.FromDirection(local, out double u, out double v);
            double keep = w.V - w.polarBand - 2500.0;
            if (keep > 0.0) v = System.Math.Max(-keep, System.Math.Min(keep, v));
            // somewhere people live: come in near the nearest landing pad
            var pad = w.outposts ? NearestSite(w, u, v, 4) : null;
            if (pad != null) { u = pad.su; v = pad.sv; }
            int area = AreaOf(w, u, v);
            Seed = w.seed * 1000 + area % 1000;
            w.SetSun(area);
            Material skyPlanet = null;
            if (body.kind == WarpPoint.Kind.Moon && body.orbitParent != null && body.orbitParent.TryGetComponent(out MeshRenderer pr)) skyPlanet = pr.sharedMaterial;
            float t0 = Time.realtimeSinceStartup;
            if (!memory.TryGetValue(body.displayName, out var mem)) memory[body.displayName] = mem = (new HashSet<int>(), new HashSet<long>());
            Surface = PlanetSurface.Create(w, kit, carrier.transform, SiteCenter, u, v, mem.Item1, mem.Item2, skyPlanet);
            CombatPilot.FloorAt = Surface.FloorAt;
            bool web = Application.platform == RuntimePlatform.WebGLPlayer;
            yield return Surface.Preload(SiteCenter, web ? 2600f : 5500f, web ? 20f : 10f);
            LastBuildSeconds = Time.realtimeSinceStartup - t0;
            w.LatLon(u, v, out float lat, out float lon);
            Site = new SurfaceSite
            {
                surface = Surface, root = Surface.gameObject, kind = body.surface, center = SiteCenter,
                terrain = Surface.TerrainAt(SiteCenter), seaLevel = Surface.SeaLevel, solidGround = w.solid,
                floor = w.solid ? SiteCenter.y + SurfaceWorld.BaseLevel : Surface.CloudFloor,
                description = w.DescribeAt(lat), look = w.look,
            };
            var outpost = pad != null ? Surface.NearestOutpost(SiteCenter, out float od) : null;
            Site.hasOutpost = outpost != null;
            Site.outpost = outpost;
            Site.pad = outpost != null ? outpost.position : new Vector3(SiteCenter.x, Surface.FloorAt(SiteCenter), SiteCenter.z);
            Surface.Shifted += d => { if (Site != null) Site.pad += d; };
            ApplyLook(Site.look);
            ArriveOverPad();
            Surface.Running = true;
            Landings++;
            Landed = false;
            touchedAt = -100f;
            carrier.externalMotion = false;
            Current = State.OnSurface;
            yield return null;
            if (Site == null) yield break;   // destroyed straight away, and back in space
            SoundFX.Play(Sfx.WarpExit, carrier.transform.position, 0.5f, 0.7f);
            CarrierDemoInput.Toast($"{body.displayName}, {LatLonText(lat, lon)}: {Site.description}{(w.hostile ? " (hostile)" : "")}");
            for (float t = 0f; t < fadeTime; t += Time.deltaTime)
            {
                fade = 1f - t / fadeTime;
                yield return null;
            }
            fade = 0f;
        }

        IEnumerator Ascend()
        {
            Current = State.Ascending;
            carrier.externalMotion = true;
            SoundFX.Play(Sfx.WarpCharge, carrier.transform.position, 0.7f, 0.75f);
            CarrierDemoInput.Toast($"Climbing back to orbit of {Body.displayName}");
            PadSupport.Rumble(0.35f, 0.2f, fadeTime);
            for (float t = 0f; t < fadeTime; t += Time.deltaTime)
            {
                fade = t / fadeTime;
                carrier.transform.position += (Vector3.up * 120f * (0.3f + fade) + carrier.transform.forward * 40f) * Time.deltaTime;
                yield return null;
            }
            fade = 1f;
            yield return null;
            BackToSpace(true);
            yield return null;
            SoundFX.Play(Sfx.WarpExit, carrier.transform.position, 0.5f, 0.8f);
            for (float t = 0f; t < fadeTime; t += Time.deltaTime)
            {
                fade = 1f - t / fadeTime;
                yield return null;
            }
            fade = 0f;
        }

        /// <summary>Put everything back as it was in space (and the ship in orbit, unless it was
        /// destroyed down there and has already come back at its respawn point).</summary>
        void BackToSpace(bool moveShip)
        {
            RestoreLook();
            CombatPilot.FloorAt = null;
            Site?.Destroy();
            Site = null;
            Surface = null;
            if (moveShip)
            {
                // back where it left, nose turned away from the body so it doesn't fly into it
                Vector3 away = orbitPosition - (Body != null ? Body.Position : Vector3.zero);
                away.y = 0f;
                if (away.sqrMagnitude < 1f) away = orbitRotation * Vector3.back;
                carrier.transform.position = orbitPosition;
                carrier.SetAttitude(Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg, 0f);
                carrier.SetMotion(30f);
                carrier.throttle = Mathf.Max(carrier.throttle, 0.2f);
            }
            carrier.externalMotion = false;
            if (drive != null) drive.lockedBecause = null;
            carrier.maxDescent = float.PositiveInfinity;
            carrier.safeImpact = 3f;
            Landed = false;
            OnPad = false;
            OnLava = false;
            Current = State.InSpace;
        }

        /// <summary>Come in low, a little way out from the landing pad and heading for it (or
        /// over the point it came down to, where there is no pad).</summary>
        void ArriveOverPad()
        {
            var rng = new System.Random(Seed + 5);
            float yaw = (float)rng.NextDouble() * 360f;
            Vector3 back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            Vector3 p = Site.hasOutpost ? Site.pad + back * 900f : Site.center;
            float ground = Surface.FloorAt(p);
            p.y = Mathf.Max(Site.hasOutpost ? Site.pad.y + 220f : ground + 220f, ground + 200f);
            carrier.transform.position = p;
            Vector3 to = Site.hasOutpost ? Site.pad - p : -back;
            carrier.SetAttitude(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0f);
            carrier.SetMotion(30f);
            carrier.throttle = 30f / Mathf.Max(1f, carrier.maxSpeed);
        }

        // ------------------------------------------------------------ the planets' surfaces

        readonly Dictionary<WarpPoint, SurfaceWorld> worlds = new Dictionary<WarpPoint, SurfaceWorld>();
        /// <summary>What has been explored and destroyed on each body (kept between landings).</summary>
        readonly Dictionary<string, (HashSet<int>, HashSet<long>)> memory = new Dictionary<string, (HashSet<int>, HashSet<long>)>();

        /// <summary>The surface of a body: its size is the body's (an asteroid field's: one of
        /// its big rocks), times surfaceScale.</summary>
        public SurfaceWorld WorldFor(WarpPoint body)
        {
            if (worlds.TryGetValue(body, out var w)) return w;
            float r = body.kind == WarpPoint.Kind.AsteroidField ? Mathf.Max(800f, body.bodyRadius * 0.3f) : Mathf.Max(500f, body.bodyRadius);
            w = new SurfaceWorld(body.surface, body.displayName, r * surfaceScale, body.hostile);
            worlds[body] = w;
            return w;
        }

        static int AreaOf(SurfaceWorld w, double u, double v)
        {
            int I = (int)System.Math.Floor(u / SurfaceWorld.T), J = (int)System.Math.Floor((v + w.V) / SurfaceWorld.T);
            w.CanonicalTile(I, J, out int ci, out int cj);
            return ci * w.M + cj;
        }

        /// <summary>The nearest area with a landing pad within `reach` areas of a point (null if none).</summary>
        static TileFeatures NearestSite(SurfaceWorld w, double u, double v, int reach)
        {
            int I = (int)System.Math.Floor(u / SurfaceWorld.T), J = (int)System.Math.Floor((v + w.V) / SurfaceWorld.T);
            TileFeatures best = null;
            double bestD = double.MaxValue;
            for (int dj = -reach; dj <= reach; dj++)
                for (int di = -reach; di <= reach; di++)
                {
                    var f = w.Features(I + di, J + dj);
                    if (f == null || f.site != SiteKind.Outpost) continue;
                    double du = SurfaceWorld.WrapDelta(f.su - u, w.C), dv = f.sv - v;
                    double d = du * du + dv * dv;
                    if (d < bestD) { bestD = d; best = f; }
                }
            return best;
        }

        public static string LatLonText(float lat, float lon) =>
            $"{Mathf.Abs(lat):F0}°{(lat >= 0 ? "N" : "S")} {Mathf.Abs(lon):F0}°{(lon >= 0 ? "E" : "W")}";

        // ------------------------------------------------------------ sky, fog and light

        struct Saved
        {
            public Material skybox;
            public bool fog;
            public FogMode fogMode;
            public Color fogColor;
            public float fogStart, fogEnd, fogDensity;
            public AmbientMode ambientMode;
            public Color ambientSky, ambientEquator, ambientGround, ambientLight;
            public Quaternion sunRotation;
            public Color sunColor;
            public float sunIntensity, fillIntensity, far, near;
            public bool starLight;
            public float reflections;
        }
        Saved saved;
        Material skyMat;
        StarLight starLight;

        void ApplyLook(Look look)
        {
            var sun = RenderSettings.sun;
            var cam = Camera.main;
            if (starLight == null) starLight = FindAnyObjectByType<StarLight>();
            saved = new Saved
            {
                skybox = RenderSettings.skybox, fog = RenderSettings.fog, fogMode = RenderSettings.fogMode, fogColor = RenderSettings.fogColor,
                fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance, fogDensity = RenderSettings.fogDensity,
                ambientMode = RenderSettings.ambientMode, ambientSky = RenderSettings.ambientSkyColor, ambientEquator = RenderSettings.ambientEquatorColor,
                ambientGround = RenderSettings.ambientGroundColor, ambientLight = RenderSettings.ambientLight,
                sunRotation = sun != null ? sun.transform.rotation : Quaternion.identity, sunColor = sun != null ? sun.color : Color.white,
                sunIntensity = sun != null ? sun.intensity : 1f, fillIntensity = fill != null ? fill.intensity : 0f,
                far = cam != null ? cam.farClipPlane : 1000f, near = cam != null ? cam.nearClipPlane : 1f,
                starLight = starLight != null && starLight.enabled,
                reflections = RenderSettings.reflectionIntensity,
            };
            if (starLight != null) starLight.enabled = false;
            Quaternion sunRot = Quaternion.Euler(look.sunElevation, look.sunAzimuth, 0f);
            if (!look.space && kit.sky != null)
            {
                skyMat = new Material(kit.sky);
                skyMat.SetColor("_Top", look.skyTop);
                skyMat.SetColor("_Horizon", look.fog);
                skyMat.SetColor("_Bottom", look.skyBottom);
                skyMat.SetVector("_SunDir", -(sunRot * Vector3.forward));
                skyMat.SetColor("_SunColor", look.sunColor);
                skyMat.SetFloat("_SunSize", look.sunSize);
                skyMat.SetFloat("_SunGlow", look.sunGlow);
                RenderSettings.skybox = skyMat;
            }
            // the space sky's reflections (black, and starry) don't belong down here
            RenderSettings.reflectionIntensity = look.space ? 0.5f : 0.2f;
            RenderSettings.fog = !look.space;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = look.fog;
            RenderSettings.fogStartDistance = look.fogStart;
            RenderSettings.fogEndDistance = look.fogEnd;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.ambientSky;
            RenderSettings.ambientEquatorColor = look.ambientEquator;
            RenderSettings.ambientGroundColor = look.ambientGround;
            if (sun != null)
            {
                sun.transform.rotation = sunRot;
                sun.color = look.sunColor;
                sun.intensity = look.sunIntensity;
            }
            if (fill != null) fill.intensity = saved.fillIntensity * (look.space ? 0.4f : 0.6f);
            if (cam != null) { cam.farClipPlane = 60000f; cam.nearClipPlane = 1f; }
        }

        void RestoreLook()
        {
            var sun = RenderSettings.sun;
            var cam = Camera.main;
            RenderSettings.skybox = saved.skybox;
            RenderSettings.fog = saved.fog;
            RenderSettings.fogMode = saved.fogMode;
            RenderSettings.fogColor = saved.fogColor;
            RenderSettings.fogStartDistance = saved.fogStart;
            RenderSettings.fogEndDistance = saved.fogEnd;
            RenderSettings.fogDensity = saved.fogDensity;
            RenderSettings.ambientMode = saved.ambientMode;
            RenderSettings.ambientSkyColor = saved.ambientSky;
            RenderSettings.ambientEquatorColor = saved.ambientEquator;
            RenderSettings.ambientGroundColor = saved.ambientGround;
            RenderSettings.ambientLight = saved.ambientLight;
            RenderSettings.reflectionIntensity = saved.reflections;
            if (sun != null) { sun.transform.rotation = saved.sunRotation; sun.color = saved.sunColor; sun.intensity = saved.sunIntensity; }
            if (fill != null) fill.intensity = saved.fillIntensity;
            if (cam != null) { cam.farClipPlane = saved.far; cam.nearClipPlane = saved.near; }
            if (starLight != null) starLight.enabled = saved.starLight;
            if (skyMat != null) { Destroy(skyMat); skyMat = null; }
        }

        // ------------------------------------------------------------ on the surface

        bool diedHere;
        float touchedAt = -100f;
        Vector3 hullMin, hullMax;
        bool hullKnown;

        void Update()
        {
            if (carrier == null) return;
            if (Current == State.OnSurface)
            {
                // destroyed down here: it comes back at its respawn point in space
                if (carrier.Destroyed) diedHere = true;
                else if (diedHere || RespawnedElsewhere())
                {
                    diedHere = false;
                    BackToSpace(false);
                    return;
                }
                if (!carrier.Destroyed)
                {
                    if (carrier.transform.position.y > Surface.HighestGround + ceiling) { TakeOff(); return; }
                    // after the ship has moved this frame (this runs after its controller), before
                    // the camera follows it (in LateUpdate), so the camera never lags a correction
                    KeepAboveGround();
                    carrier.maxDescent = Mathf.Lerp(6f, 40f, Mathf.InverseLerp(15f, 220f, Altitude));   // landing assist
                    carrier.safeImpact = 12f;
                }
            }
            if (Current == State.InSpace) candidate = Candidate(out candidateWhy);
            if (GameMenu.IsOpen || GameMenu.JustClosed || SystemMap.IsOpen || SystemMap.JustClosed) return;
            var k = Keyboard.current;
            var pad = Gamepad.current;
            bool press = (k != null && k.kKey.wasPressedThisFrame) || (pad != null && !carrier.HasMissiles && pad.leftShoulder.wasPressedThisFrame);
            if (!press || carrier.Destroyed) return;
            if (Current == State.InSpace) Land();
            else if (Current == State.OnSurface) TakeOff();
        }

        void LateUpdate()
        {
            if (Current != State.OnSurface || Site == null || carrier == null || carrier.Destroyed || RespawnedElsewhere()) return;
            KeepCameraAboveGround();
        }

        /// <summary>The ship is nowhere near the site: it was destroyed and has come back at its
        /// respawn point in space (the respawn can land between this script's Update and LateUpdate).</summary>
        bool RespawnedElsewhere() => Site != null && (carrier.transform.position - Site.center).sqrMagnitude > 30000f * 30000f;

        WarpPoint candidate;
        string candidateWhy = "";

        /// <summary>The hull's box in the ship's own space (from its meshes, shield left out).</summary>
        void FindHull()
        {
            if (hullKnown) return;
            hullKnown = true;
            hullMin = new Vector3(-12f, -10f, -90f);
            hullMax = new Vector3(12f, 20f, 90f);
            bool any = false;
            var toLocal = carrier.transform.worldToLocalMatrix;
            foreach (var mf in carrier.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponentInParent<Shield>() != null) continue;
                if (mf.GetComponent<MeshRenderer>() == null || mf.GetComponentInParent<ParticleSystem>() != null) continue;
                var b = mf.sharedMesh.bounds;
                var m = toLocal * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { hullMin = hullMax = c; any = true; }
                    else { hullMin = Vector3.Min(hullMin, c); hullMax = Vector3.Max(hullMax, c); }
                }
            }
        }

        static readonly RaycastHit[] Hits = new RaycastHit[8];

        /// <summary>What is under a point: ground, pad, rock, or the sea / lava / deep cloud.</summary>
        float FloorUnder(Vector3 p, out bool pad, out bool liquid)
        {
            pad = false;
            float best = Mathf.Max(Site.seaLevel, Site.floor);
            float ground = Site.solidGround ? Site.GroundAt(p) : float.NegativeInfinity;
            liquid = Site.seaLevel > ground && Site.seaLevel >= Site.floor;
            if (ground > best) best = ground;
            // from just above the hull, so a rock or ledge overhead isn't taken for the floor
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 25f, Vector3.down, Hits, 1400f, Cover.Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(carrier.transform)) continue;
                if (h.point.y > best - 0.25f)
                {
                    best = h.point.y;
                    liquid = false;
                    pad = h.collider.name.StartsWith("Pad ") && h.collider.transform.parent != null &&
                          h.collider.transform.parent.name.StartsWith("Outpost");
                }
            }
            return best;
        }

        void KeepAboveGround()
        {
            FindHull();
            var tr = carrier.transform;
            float push = float.MinValue, lowest = float.MaxValue;
            bool onPad = false, onLiquid = false;
            float zFront = hullMax.z * 0.85f, zBack = hullMin.z * 0.85f, xw = Mathf.Max(Mathf.Abs(hullMin.x), hullMax.x) * 0.6f;
            for (int i = 0; i < 9; i++)
            {
                float lz = i / 3 == 0 ? zBack : i / 3 == 1 ? (zBack + zFront) * 0.5f : zFront;
                float lx = (i % 3 - 1) * xw;
                Vector3 p = tr.TransformPoint(new Vector3(lx, hullMin.y, lz));
                float floor = FloorUnder(p, out bool pad, out bool liquid);
                float gap = p.y - floor;
                // a corner deep inside a cliff is the side of the hull against a wall (the hull's
                // own collision deals with that): only lift the ship off ground just under it
                if (gap < -30f) continue;
                if (gap < lowest) { lowest = gap; onPad = pad; onLiquid = liquid; }
                push = Mathf.Max(push, floor + 0.3f - p.y);
            }
            if (lowest == float.MaxValue)
            {
                // every sample deep under the ground. Against a wall the middle of the hull would
                // still be clear; this is ground built under a ship that was flying low over an
                // area not yet worked out. Lift it out.
                float mid = FloorUnder(tr.position, out _, out _);
                float bottom = tr.TransformPoint(new Vector3(0f, hullMin.y, 0f)).y;
                if (bottom < mid - 30f) tr.position += Vector3.up * (mid + 0.3f - bottom);
                return;
            }
            Altitude = Mathf.Max(0f, lowest);
            bool touching = push > -0.5f;
            if (push > 0f) tr.position += Vector3.up * push;
            bool wasLanded = Landed;
            Landed = touching && carrier.Speed < 15f;
            OnPad = Landed && onPad;
            if (Landed && !wasLanded && Time.time - touchedAt > 3f)
            {
                Touchdowns++;
                CarrierDemoInput.Toast(OnPad ? "Touchdown on the landing pad" : $"Touchdown on {Body.displayName}");
                PadSupport.Rumble(0.5f, 0.3f, 0.3f);
                SoundFX.Play(Sfx.Impact, tr.position, 0.5f, 0.5f);
            }
            if (touching) touchedAt = Time.time;
            // lava burns (the shield first, then the hull)
            OnLava = Site.kind == SurfaceKind.Volcanic && onLiquid && Altitude < 1f;
            if (OnLava)
            {
                if (lavaHealth == null) lavaHealth = carrier.GetComponent<Health>();
                // in pulses, not every frame (each hit ripples the shield and makes a sound)
                if (lavaHealth != null && Time.time - lavaHitAt > 0.5f)
                {
                    lavaHealth.Damage(75f, tr.position + Vector3.down * 10f, HitKind.Beam);
                    lavaHitAt = Time.time;
                }
                if (Time.time - lavaWarnedAt > 4f) { lavaWarnedAt = Time.time; CarrierDemoInput.Toast("The lava is burning the hull: climb!"); }
            }
        }

        /// <summary>Touching lava (taking damage).</summary>
        public bool OnLava { get; private set; }
        Health lavaHealth;
        float lavaWarnedAt = -100f, lavaHitAt = -100f;

        void KeepCameraAboveGround()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 c = cam.transform.position;
            float floor = Surface != null ? Surface.FloorAt(c) : Mathf.Max(Site.seaLevel, Site.floor);
            if (c.y < floor + 12f) cam.transform.position = new Vector3(c.x, floor + 12f, c.z);
        }

        // ------------------------------------------------------------ HUD

        /// <summary>The HUD's landing line(s) (null when there is nothing to say).</summary>
        public string HudText()
        {
            string key = PadSupport.UsingPad && Gamepad.current != null ? "LB" : "K";
            switch (Current)
            {
                case State.Descending: return $"Land  <color=#ffd9a0>descending to {Body?.displayName}</color>";
                case State.Ascending: return $"Land  <color=#bfe6ff>climbing to orbit</color>";
                case State.OnSurface:
                {
                    string state = Landed ? (OnPad ? "<color=#7ee08a>landed on the pad</color>" : "<color=#7ee08a>landed</color>")
                                          : $"altitude {Altitude:F0} m";
                    string where = "";
                    if (Surface != null)
                    {
                        Surface.LatLon(carrier.transform.position, out float la, out float lo);
                        where = $"  <color=#9aa4b0>{LatLonText(la, lo)}  explored {Surface.Explored.Count}/{World.AreaCount}</color>";
                    }
                    return $"Surface  {Body?.displayName}  {state}{where}  <color=#9aa4b0>{key} take off</color>";
                }
            }
            var body = candidate;
            string why = candidateWhy;
            if (body == null) return null;
            return why == "" ? $"Land  <color=#7ee08a>{key} land on {body.displayName}</color>"
                             : $"Land  <color=#ff6050>{body.displayName}: {why}</color>";
        }

        void OnGUI()
        {
            if (fade <= 0.001f || Event.current.type != EventType.Repaint) return;
            GUI.depth = -60;
            var c = GUI.color;
            GUI.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, Mathf.Clamp01(fade));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = c;
        }
    }
}

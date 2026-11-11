using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The player's ship and the sea, on a water world (PlanetLanding adds it there):
    ///
    /// - Flying: coming down onto the water (the descent eased near it, as the landing assist does
    ///   over the ground) it splashes down and settles afloat.
    /// - Afloat: it rides the swell at its waterline, level, sailing like a surface ship at a
    ///   little over half speed, a wake behind it. The elevator thrusters pushing up (Space, RT)
    ///   lift it off the water and it flies; held pushing down (Ctrl, LT) for a moment, it dives.
    /// - Under water: slower again, trailing bubbles; it pitches, turns and climbs as in the air,
    ///   down to the sea bed. Coming up to the surface gently, it surfaces afloat; with the
    ///   elevator thrusters pushing up it bursts out and flies on.
    ///
    /// With the camera under the water the view goes blue and close (fog, light, background).
    /// The surface hides what is under it (Sea, Cover): only torpedoes reach a ship down there.
    /// </summary>
    [DefaultExecutionOrder(305)]   // after the ship has moved and the landing has kept it off the ground
    public class Seafaring : MonoBehaviour
    {
        public enum State { Flying, Afloat, Under }

        public static Seafaring Instance { get; private set; }

        public State Current { get; private set; } = State.Flying;
        /// <summary>Times into the water, under it, up to its surface, and off it (for the self-test).</summary>
        public int Splashdowns { get; private set; }
        public int Dives { get; private set; }
        public int Surfacings { get; private set; }
        public int Launches { get; private set; }
        /// <summary>How deep the ship is (m; negative above the surface).</summary>
        public float Depth => carrier != null ? Sea.Depth(carrier.transform.position) : 0f;

        /// <summary>The console's line about the water (null when flying).</summary>
        public string Status => Current == State.Afloat ? $"Afloat   {carrier.Speed:F0} m/s"
                              : Current == State.Under ? $"Under water   depth {Mathf.Max(0f, Depth):F0} m   " +
                                                         $"bed {Mathf.Max(0f, carrier.transform.position.y - BedUnder()):F0} m below"
                              : null;

        const float AfloatSpeed = 0.6f, UnderSpeed = 0.45f, UnderLift = 0.55f;

        CarrierController carrier;
        SurfaceKit kit;
        float floatOffset, bob, downHeld;
        const float DiveHold = 0.8f;   // seconds of pushing down, afloat, to dive
        ParticleSystem spray, foam, bubbles;
        Transform stern;
        Bounds hullB;
        float wakeDue, bowDue, churnDue;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; End(); }

        /// <summary>Down on a water world with this ship.</summary>
        public void Begin(CarrierController ship, SurfaceKit surfaceKit)
        {
            carrier = ship;
            kit = surfaceKit;
            Current = State.Flying;
            enabled = true;
            // afloat, the hull rides with a fifth of its depth under the water
            var hull = ship.transform.Find("HullCollider");
            var mc = hull != null ? hull.GetComponent<MeshCollider>() : null;
            Bounds b = mc != null && mc.sharedMesh != null ? mc.sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(50f, 40f, 180f));
            floatOffset = -b.min.y - b.size.y * 0.22f;
            hullB = b;
            stern = new GameObject("Stern").transform;
            stern.SetParent(ship.transform, false);
            stern.localPosition = new Vector3(0f, 0f, b.min.z);
            MakeEffects();
        }

        /// <summary>The ship has been put under the water (by the self-test or a reel, which move it
        /// about): it carries on from there as under water.</summary>
        public void Submerge()
        {
            if (carrier == null || !Sea.Under(carrier.transform.position)) return;
            Current = State.Under;
        }

        /// <summary>Where the ship floats: the height of its middle, afloat.</summary>
        public float FloatHeight => Sea.Level + floatOffset;

        /// <summary>Back to space (or the ship is gone): everything as it was.</summary>
        public void End()
        {
            if (carrier != null) { carrier.speedScale = 1f; carrier.liftScale = 1f; carrier.holdLevel = false; }
            LookUnder(false);
            if (spray != null) Destroy(spray.gameObject);
            if (foam != null) Destroy(foam.gameObject);
            if (bubbles != null) Destroy(bubbles.gameObject);
            if (stern != null) Destroy(stern.gameObject);
            spray = foam = bubbles = null;
            Current = State.Flying;
            enabled = false;
        }

        float BedUnder()
        {
            var landing = PlanetLanding.Instance;
            return landing != null && landing.Surface != null ? landing.Surface.GroundAt(carrier.transform.position) : float.NegativeInfinity;
        }

        void Update()
        {
            if (carrier == null || !Sea.Here) return;
            if (carrier.Destroyed || carrier.externalMotion)
            {
                carrier.speedScale = carrier.liftScale = 1f;
                carrier.holdLevel = false;
                Current = State.Flying;
                return;
            }
            float dt = Time.deltaTime;
            var t = carrier.transform;
            float sea = Sea.Level, floatY = sea + floatOffset;
            bool down = carrier.elevator < -0.2f, up = carrier.elevator > 0.2f;
            downHeld = down ? downHeld + dt : 0f;
            switch (Current)
            {
                case State.Flying:
                    // the descent eased near the water, for a gentle splashdown
                    float above = t.position.y - floatY;
                    if (above < 150f) carrier.maxDescent = Mathf.Min(carrier.maxDescent, Mathf.Lerp(7f, 40f, Mathf.InverseLerp(4f, 150f, above)));
                    // (only coming down: on its way up off the water it starts at its waterline)
                    if (above <= 0.3f && carrier.ClimbRate < -0.3f)
                    {
                        Splashdowns++;
                        Splash(t.position, Mathf.Clamp01(Mathf.Abs(carrier.ClimbRate) / 40f + 0.3f));
                        carrier.SetMotion(carrier.Speed * 0.7f);
                        Current = State.Afloat;
                        downHeld = 0f;
                        Notices.Post("Down on the water: afloat");
                    }
                    break;
                case State.Afloat:
                    if (up)
                    {
                        Current = State.Flying;
                        Launches++;
                        Splash(t.position, 0.5f);
                        Notices.Post("Lifting off the water");
                    }
                    else if (downHeld >= DiveHold)
                    {
                        Current = State.Under;
                        Dives++;
                        Splash(t.position, 0.7f);
                        Notices.Post("Diving");
                    }
                    else
                    {
                        // riding the swell, level
                        bob += dt;
                        float y = floatY + Mathf.Sin(bob * 0.9f) * 0.6f;
                        t.position = new Vector3(t.position.x, Mathf.Lerp(t.position.y, y, Mathf.Clamp01(3f * dt)), t.position.z);
                        carrier.SetAttitude(carrier.Heading, Mathf.MoveTowards(carrier.Pitch, Mathf.Sin(bob * 0.7f) * 1.2f, 10f * dt));
                    }
                    break;
                case State.Under:
                    // (only on its way up: held at the surface while still pushing down, it stays under)
                    if (t.position.y >= floatY - 0.5f && (carrier.ClimbRate > 0.3f || t.position.y > floatY + 2f))
                    {
                        if (up)
                        {
                            Current = State.Flying;
                            Launches++;
                            Splash(t.position, 1f);
                            Notices.Post("Launched from under the water");
                        }
                        else
                        {
                            Current = State.Afloat;
                            Surfacings++;
                            downHeld = 0f;
                            t.position = new Vector3(t.position.x, floatY, t.position.z);
                            Splash(t.position, 0.4f);
                            carrier.SetMotion(carrier.Speed);
                            Notices.Post("Surfaced");
                        }
                    }
                    break;
            }
            // under the water the landing assist's slowing near the floor (the sea bed) gives way to
            // the water's own: a dive eased only near the bottom
            if (Current == State.Under)
                carrier.maxDescent = Mathf.Lerp(3f, 40f, Mathf.InverseLerp(8f, 70f, t.position.y - BedUnder()));
            carrier.speedScale = Current == State.Afloat ? AfloatSpeed : Current == State.Under ? UnderSpeed : 1f;
            carrier.liftScale = Current == State.Under ? UnderLift : 1f;
            // afloat, it sails level: the nose and wings follow the swell, not the stick
            carrier.holdLevel = Current == State.Afloat;
            UpdateEffects(sea, dt);
        }

        // ------------------------------------------------------------ splashes, wake, bubbles

        void MakeEffects()
        {
            Material mat = kit != null && kit.dust != null ? kit.dust : null;
            // thrown-up water (splashes, bow spray), falling back
            spray = WaterFx.Make("Spray", mat, 2500, new Color(0.93f, 0.97f, 1f, 0.9f), 1.4f, 3f, 6f, 20f, 1.6f);
            // foam lying on the water (the wake, bow waves, splash rings), spreading as it fades
            foam = WaterFx.Make("Foam", mat, 4000, new Color(0.94f, 0.98f, 1f, 0.8f), 5f, 8f, 8f, 16f, 0f, flat: true, grow: 3f);
            bubbles = WaterFx.Make("Bubbles", mat, 800, new Color(0.8f, 0.94f, 1f, 0.7f), 2.5f, 3.5f, 0.8f, 2.2f, -0.6f);
            {
                var m = bubbles.main;
                m.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
                var sh = bubbles.shape;
                sh.shapeType = ParticleSystemShapeType.Sphere;
                sh.radius = 18f;
            }
        }

        Quaternion Flat => Quaternion.Euler(0f, carrier.Heading, 0f);

        /// <summary>Water thrown up, and foam on the water, now (for the self-test).</summary>
        public int SprayParticles => spray != null ? spray.particleCount : 0;
        public int FoamParticles => foam != null ? foam.particleCount : 0;

        /// <summary>A point by the hull, on the water: `x` across it (-1 port, 1 starboard, at its
        /// side), `z` along it (-1 stern, 1 bow).</summary>
        Vector3 OnWater(float x, float z, float sea, float lift = 0.3f)
        {
            Vector3 at = carrier.transform.position + Flat * new Vector3(x * hullB.extents.x, 0f, hullB.center.z + z * hullB.extents.z);
            at.y = sea + lift;
            return at;
        }

        /// <summary>The water the ship pushes aside, going in or coming out: spray thrown up along
        /// the hull and a ring of foam shoved out across the sea.</summary>
        void Splash(Vector3 at, float size)
        {
            if (spray == null || carrier == null) return;
            float sea = Sea.Level;
            Vector3 right = Flat * Vector3.right;
            int n = Mathf.RoundToInt(260 + 640 * size);
            for (int i = 0; i < n; i++)
            {
                // the most and highest amidships, less toward the ends
                float along = Random.Range(-1f, 1f), side = Random.value < 0.5f ? -1f : 1f;
                float mid = 1f - 0.55f * Mathf.Abs(along);
                Vector3 p = OnWater(side * Random.Range(0.5f, 1.15f), along * 0.95f, sea, 1f);
                Vector3 v = right * side * Random.Range(4f, 24f) * (0.5f + size) + Vector3.up * Random.Range(12f, 55f) * (0.5f + size) * mid;
                WaterFx.Emit(spray, p, v, Random.Range(8f, 28f) * (0.6f + 0.6f * size), Random.Range(1.6f, 3.4f));
            }
            int m = Mathf.RoundToInt(140 + 300 * size);
            for (int i = 0; i < m; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector3 p = OnWater(Mathf.Cos(a) * 1.15f, Mathf.Sin(a) * 1.05f, sea);
                Vector3 dir = p - carrier.transform.position;
                dir.y = 0f;
                dir = dir.sqrMagnitude > 1f ? dir.normalized : right;
                WaterFx.Emit(foam, p, dir * Random.Range(6f, 26f) * (0.5f + size), Random.Range(14f, 32f), Random.Range(5f, 8f));
            }
            SoundFX.Play(Sfx.Impact, at, 0.6f + 0.4f * size, 0.45f);
        }

        void UpdateEffects(float sea, float dt)
        {
            if (foam == null || carrier == null) return;
            var t = carrier.transform;
            Quaternion flat = Flat;
            Vector3 fwd = flat * Vector3.forward, right = flat * Vector3.right;
            float speed = Mathf.Abs(carrier.Speed);
            float keel = t.position.y + hullB.min.y, deck = t.position.y + hullB.max.y;
            // how much the hull stirs the surface: afloat, fully; under, less the deeper it is
            // (a shallow one still humps the water over it)
            float stir = Current == State.Afloat ? 1f
                       : Current == State.Under ? 1f - Mathf.InverseLerp(0f, 30f, sea - deck)
                       : keel < sea ? 1f : 0f;

            // the wake: churned water astern, and the bow waves rolling out to either side
            wakeDue += stir * Mathf.Clamp(speed * 1.6f, 0f, 130f) * dt;
            while (wakeDue >= 1f)
            {
                wakeDue -= 1f;
                float x = Random.Range(-0.8f, 0.8f);
                Vector3 p = OnWater(x, -1f, sea) - fwd * Random.Range(0f, 8f);
                WaterFx.Emit(foam, p, -fwd * speed * Random.Range(0.05f, 0.2f) + right * x * 3f, Random.Range(10f, 20f), Random.Range(7f, 10f), 0.9f * stir);
                float s = Random.value < 0.5f ? -1f : 1f;
                Vector3 bow = OnWater(s * 0.55f, Random.Range(0.6f, 0.95f), sea);
                WaterFx.Emit(foam, bow, right * s * (3f + speed * 0.28f) * Random.Range(0.7f, 1.2f), Random.Range(6f, 12f), Random.Range(5f, 7f), 0.8f * stir);
            }
            // spray off the bow, going fast on the surface
            bowDue += Current == State.Afloat && speed > 12f ? speed * 0.5f * dt : 0f;
            while (bowDue >= 1f)
            {
                bowDue -= 1f;
                float s = Random.value < 0.5f ? -1f : 1f;
                Vector3 bow = OnWater(s * 0.4f, Random.Range(0.75f, 1f), sea, 1f);
                Vector3 v = fwd * speed * 0.6f + right * s * Random.Range(5f, 14f) + Vector3.up * Random.Range(5f, 15f);
                WaterFx.Emit(spray, bow, v, Random.Range(4f, 11f), Random.Range(1f, 1.8f));
            }
            // the hull going down through the surface, or coming up out of it: the water boils
            // round it all along its length
            bool through = Current != State.Afloat && keel < sea && deck > sea;
            churnDue += through ? 220f * dt : 0f;
            while (churnDue >= 1f)
            {
                churnDue -= 1f;
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector3 p = OnWater(Mathf.Cos(a) * 1.05f, Mathf.Sin(a), sea);
                Vector3 dir = p - t.position;
                dir.y = 0f;
                dir = dir.sqrMagnitude > 1f ? dir.normalized : right;
                WaterFx.Emit(foam, p, dir * Random.Range(4f, 14f), Random.Range(10f, 22f), Random.Range(4f, 7f));
                if (Random.value < 0.5f)
                    WaterFx.Emit(spray, p + Vector3.up, dir * Random.Range(2f, 8f) + Vector3.up * Random.Range(6f, 22f), Random.Range(6f, 16f), Random.Range(1.2f, 2.4f));
            }
            if (bubbles != null && stern != null)
            {
                bubbles.transform.position = stern.position;
                var em = bubbles.emission;
                em.rateOverTime = Current == State.Under ? 40f + carrier.Speed * 0.8f : 0f;
            }
        }

        // ------------------------------------------------------------ under the water

        bool lookingUnder;
        bool savedFog;
        FogMode savedMode;
        Color savedFogColor, savedAmbient, savedBackground;
        float savedStart, savedEnd;
        UnityEngine.Rendering.AmbientMode savedAmbientMode;
        CameraClearFlags savedClear;
        Camera lookCam;
        Light sun;
        float savedSunIntensity, savedFar;
        Color savedSunColor;
        static readonly Color Deep = new Color(0.04f, 0.19f, 0.26f);

        /// <summary>Under the water, a wash of its colour over the view (under the HUD), deeper toward
        /// the bottom of the screen.</summary>
        void OnGUI()
        {
            if (!lookingUnder || Event.current.type != EventType.Repaint) return;
            GUI.depth = 100;
            float h = Screen.height;
            Hud.Fill(new Rect(0f, 0f, Screen.width, h), new Color(0.05f, 0.3f, 0.36f, 0.22f));
            Hud.Fill(new Rect(0f, h * 0.55f, Screen.width, h * 0.45f), new Color(0.02f, 0.12f, 0.16f, 0.18f));
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            LookUnder(cam != null && Sea.Under(cam.transform.position), cam);
        }

        void LookUnder(bool under, Camera cam = null)
        {
            if (under == lookingUnder) return;
            if (under && cam != null)
            {
                savedFog = RenderSettings.fog;
                savedMode = RenderSettings.fogMode;
                savedFogColor = RenderSettings.fogColor;
                savedStart = RenderSettings.fogStartDistance;
                savedEnd = RenderSettings.fogEndDistance;
                savedAmbientMode = RenderSettings.ambientMode;
                savedAmbient = RenderSettings.ambientLight;
                lookCam = cam;
                savedClear = cam.clearFlags;
                savedBackground = cam.backgroundColor;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = Deep;
                RenderSettings.fogStartDistance = 0f;
                RenderSettings.fogEndDistance = 420f;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.08f, 0.24f, 0.3f);
                // the daylight comes down dim and blue-green
                sun = RenderSettings.sun;
                if (sun != null)
                {
                    savedSunIntensity = sun.intensity;
                    savedSunColor = sun.color;
                    sun.intensity *= 0.3f;
                    sun.color = Color.Lerp(sun.color, new Color(0.45f, 0.8f, 0.85f), 0.65f);
                }
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Deep;
                // nothing seen beyond the murk (the star's glare, the far sky): the water's colour instead
                savedFar = cam.farClipPlane;
                cam.farClipPlane = 900f;
            }
            else
            {
                RenderSettings.fog = savedFog;
                RenderSettings.fogMode = savedMode;
                RenderSettings.fogColor = savedFogColor;
                RenderSettings.fogStartDistance = savedStart;
                RenderSettings.fogEndDistance = savedEnd;
                RenderSettings.ambientMode = savedAmbientMode;
                RenderSettings.ambientLight = savedAmbient;
                if (sun != null) { sun.intensity = savedSunIntensity; sun.color = savedSunColor; sun = null; }
                if (lookCam != null) { lookCam.clearFlags = savedClear; lookCam.backgroundColor = savedBackground; lookCam.farClipPlane = savedFar; }
            }
            lookingUnder = under;
        }
    }
}

using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// What the warp drive looks and sounds like (WarpDrive has the rules and flies the ship):
    /// sparks streaming into the hull and a blue glow building while it charges, a flash and a
    /// shockwave ring as it jumps and as it drops out, streaks of starlight and a haze rushing
    /// past the camera on the way, a hum, and a vignette and a white flash over the screen. It
    /// uses the drive's materials and colour, and makes what it needs as the drive starts.
    /// </summary>
    public class WarpEffects
    {
        readonly WarpDrive drive;
        ParticleSystem tunnel, haze, sparks, flash, ring;
        Light flashLight;
        AudioSource loop, chargeVoice;
        Texture2D vignette;
        float baseFov = -1f, glowTimer;

        Transform transform => drive.transform;
        GameObject gameObject => drive.gameObject;
        Color warpColor => drive.warpColor;
        Material streakMaterial => drive.streakMaterial;
        Material hazeMaterial => drive.hazeMaterial;
        Material flashMaterial => drive.flashMaterial;
        Material ringMaterial => drive.ringMaterial;
        Material sparkMaterial => drive.sparkMaterial;

        public WarpEffects(WarpDrive drive)
        {
            this.drive = drive;
            Build();
        }

        // ------------------------------------------------------------ the stages of a jump

        /// <summary>The drive starts charging: sparks stream in, and the charge sound runs for as
        /// long as the charge (it ends as the ship jumps).</summary>
        public void StartCharge(float chargeTime)
        {
            chargeVoice = SoundFX.PlayFitted(Sfx.WarpCharge, transform.position, chargeTime, true);
            sparks.Play();
        }

        /// <summary>The charge `k` of the way (0..1): more sparks, and the glow on the hull building.</summary>
        public void Charging(float k, float dt)
        {
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
        }

        /// <summary>Into warp at `at`: a flash and ring, the streaks and the hum start.</summary>
        public void Jumped(Vector3 at)
        {
            chargeVoice = null;
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Burst(at);
            SoundFX.Play(Sfx.WarpJump, at);
            if (loop != null) { loop.volume = 0f; loop.Play(); }
            tunnel.Play();
            haze.Play();
        }

        /// <summary>Out of warp: a flash and ring at `burstAt` (just ahead of the ship), the
        /// streaks and the hum die away.</summary>
        public void Arrived(Vector3 burstAt)
        {
            Burst(burstAt);
            SoundFX.Play(Sfx.WarpExit, transform.position);
            if (loop != null) loop.Stop();
            tunnel.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            haze.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>The jump was called off: everything stops.</summary>
        public void Stopped()
        {
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            tunnel.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            haze.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (loop != null) loop.Stop();
            if (chargeVoice != null) { chargeVoice.Stop(); chargeVoice = null; }
        }

        // ------------------------------------------------------------ making and drawing them

        void Build()
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

        /// <summary>The tunnel on the camera (it has moved by now: it follows in LateUpdate),
        /// as strong as `lvl` (0..1), along the way the ship is going.</summary>
        public void Follow(float lvl, Vector3 travelDir)
        {
            var cam = Camera.main;
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

        /// <summary>Everything made for the effects (the drive is going).</summary>
        public void Dispose()
        {
            if (tunnel) Object.Destroy(tunnel.gameObject);
            if (haze) Object.Destroy(haze.gameObject);
            if (flash) Object.Destroy(flash.gameObject);
            if (ring) Object.Destroy(ring.gameObject);
            if (flashLight) Object.Destroy(flashLight.gameObject);
            if (vignette) Object.Destroy(vignette);     // made in code (DontSave), so nothing else frees it
        }

        /// <summary>The tunnel's vignette and the jump flash over the screen (from OnGUI).</summary>
        public void Draw(float tunnelLevel, float screenFlash)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (tunnelLevel > 0.01f)
            {
                if (vignette == null) vignette = MakeVignette(128);
                var v = GUI.color;
                GUI.color = new Color(warpColor.r * 0.6f, warpColor.g * 0.7f, warpColor.b, 0.55f * tunnelLevel);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignette);
                GUI.color = v;
            }
            if (screenFlash <= 0.01f) return;
            var c = GUI.color;
            GUI.color = new Color(0.85f, 0.92f, 1f, Mathf.Clamp01(screenFlash) * 0.85f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = c;
        }
    }
}

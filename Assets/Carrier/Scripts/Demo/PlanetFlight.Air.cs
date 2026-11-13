using UnityEngine;
using UnityEngine.Rendering;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The air seen from inside, over a real-size planet: below the top of a world's air its sky
    /// (a dome round the camera, SkyDome) thickens over the stars, the fog closes in, the ambient
    /// light and the sun take the world's colours, until at the hand-off height it is the world's
    /// own sky, fog and light: what the surface below has (PlanetLanding hands over under the same
    /// sky). Climbing out it all thins away to space again. Low over the ground (in the air, or
    /// within a fifth of its radius of an airless moon) the ship is carried round with the turning
    /// planet, so the ground stays under it.
    /// </summary>
    public partial class PlanetFlight
    {
        [Tooltip("The sky dome's material (Carrier/SkyDome).")]
        public Material skyDome;

        struct SpaceLook
        {
            public bool fog;
            public AmbientMode ambientMode;
            public Color ambientSky, ambientEquator, ambientGround, ambientLight;
            public Color sunColor;
            public float sunIntensity;
        }
        SpaceLook space;
        bool spaceKnown, blending;
        Transform dome;
        Material domeMat;
        /// <summary>0 (space) to 1 (the world's own sky, at the hand-off height).</summary>
        public float AirLook { get; private set; }

        // the planet's turning, to carry the ship round with it
        Transform spinBody;
        Quaternion lastSpin;

        void RememberSpace()
        {
            var sun = RenderSettings.sun;
            space = new SpaceLook
            {
                fog = RenderSettings.fog, ambientMode = RenderSettings.ambientMode,
                ambientSky = RenderSettings.ambientSkyColor, ambientEquator = RenderSettings.ambientEquatorColor,
                ambientGround = RenderSettings.ambientGroundColor, ambientLight = RenderSettings.ambientLight,
                sunColor = sun != null ? sun.color : Color.white, sunIntensity = sun != null ? sun.intensity : 1f,
            };
            spaceKnown = true;
        }

        void RestoreSpace()
        {
            if (!blending) return;
            blending = false;
            AirLook = 0f;
            RenderSettings.fog = space.fog;
            RenderSettings.ambientMode = space.ambientMode;
            RenderSettings.ambientSkyColor = space.ambientSky;
            RenderSettings.ambientEquatorColor = space.ambientEquator;
            RenderSettings.ambientGroundColor = space.ambientGround;
            RenderSettings.ambientLight = space.ambientLight;
            var sun = RenderSettings.sun;
            if (sun != null) { sun.color = space.sunColor; sun.intensity = space.sunIntensity; }
            if (dome != null) dome.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (ship == null) return;
            CarryRound();
            if (!spaceKnown && !PlanetLanding.Busy) RememberSpace();
            var look = Body != null && !PlanetLanding.Busy && PlanetLanding.Instance != null && ScaleHeight(Body) > 0f
                ? PlanetLanding.Instance.WorldFor(Body).look : null;
            float top = AirTop(Body);
            if (look == null || look.space || Altitude >= top) { RestoreSpace(); return; }
            float hand = PlanetLanding.Instance.HandoffFor(PlanetLanding.Instance.WorldFor(Body));
            float k = 1f - Mathf.Clamp01((Altitude - hand) / Mathf.Max(1f, top * 0.8f - hand));
            k = k * k * (3f - 2f * k);
            AirLook = k;
            blending = true;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = look.fog;
            float end = 15000f + Mathf.Max(0f, Altitude - hand) * 6f;
            RenderSettings.fogEndDistance = end;
            RenderSettings.fogStartDistance = end * 0.12f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(space.ambientSky, look.ambientSky, k);
            RenderSettings.ambientEquatorColor = Color.Lerp(space.ambientEquator, look.ambientEquator, k);
            RenderSettings.ambientGroundColor = Color.Lerp(space.ambientGround, look.ambientGround, k);
            var sun = RenderSettings.sun;
            if (sun != null)
            {
                sun.color = Color.Lerp(space.sunColor, look.sunColor, k);
                sun.intensity = Mathf.Lerp(space.sunIntensity, look.sunIntensity, k);
            }
            Dome(look, k);
        }

        void Dome(Look look, float k)
        {
            var cam = Camera.main;
            if (skyDome == null || cam == null) return;
            if (dome == null)
            {
                var go = new GameObject("Sky dome", typeof(MeshFilter), typeof(MeshRenderer));
                go.AddComponent<ProceduralSphere>().Build();
                domeMat = new Material(skyDome);
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = domeMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                dome = go.transform;
            }
            dome.gameObject.SetActive(true);
            float r = cam.farClipPlane * 0.85f;
            dome.localScale = Vector3.one * r * 2f;
            dome.position = cam.transform.position;
            Vector3 up = (ship.transform.position - Body.Position).normalized;
            Vector3 toStar = OrbitalSystem.Instance != null ? (OrbitalSystem.Instance.StarPosition - Body.Position).normalized : Vector3.up;
            domeMat.SetColor("_Top", look.skyTop);
            domeMat.SetColor("_Horizon", look.fog);
            domeMat.SetColor("_Bottom", look.skyBottom);
            domeMat.SetColor("_SunColor", look.sunColor);
            domeMat.SetFloat("_SunSize", look.sunSize);
            domeMat.SetFloat("_SunGlow", look.sunGlow);
            domeMat.SetVector("_Up", up);
            domeMat.SetVector("_SunDir", toStar);
            domeMat.SetFloat("_Alpha", Mathf.Pow(k, 0.7f));
        }

        /// <summary>Low over a turning body, the ship goes round with it (so the ground stays
        /// under it, and a landing pad doesn't slide away).</summary>
        void CarryRound()
        {
            var b = !PlanetLanding.Busy && Body != null ? Body.transform : null;
            bool low = b != null && (ScaleHeight(Body) > 0f ? Altitude < AirTop(Body) : Altitude < Body.bodyRadius * 0.2f);
            if (!low || ship.Destroyed) { spinBody = null; return; }
            if (spinBody != b) { spinBody = b; lastSpin = b.rotation; return; }
            Quaternion d = b.rotation * Quaternion.Inverse(lastSpin);
            lastSpin = b.rotation;
            if (Quaternion.Angle(d, Quaternion.identity) < 1e-6f) return;
            Vector3 c = b.position;
            ship.transform.position = c + d * (ship.transform.position - c);
            ship.SetUpFrame(d * ship.upFrame);
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The frigate's kinetic strike from orbit (Y): a tungsten rod dropped on the ground below.
    /// Over a planet or moon (in space, not on its surface), it aims at the nearest ground forces
    /// left within `reach` of the point under the ship (a fortress, a battery, tanks), or else
    /// the ground straight below. The rod takes some seconds to fall, glowing in the air, and
    /// where it lands everything within `radius` is destroyed (OrbitalStrikes: each rebuilt in its
    /// time) and a crater is left. One every `cooldown` seconds.
    /// </summary>
    public class KineticStrike : MonoBehaviour
    {
        public CarrierController ship;
        public float radius = 450f;
        public float craterDepth = 60f;
        public float reach = 30000f;
        public float cooldown = 20f;
        [Tooltip("How fast the rod falls (m/s): the higher the ship, the longer it takes.")]
        public float fallSpeed = 5000f;
        [Tooltip("The strikes' looks (StrikeFx): a soft glow, a beam, a shock ring.")]
        public Material glowMaterial, beamMaterial, ringMaterial;

        public static KineticStrike Instance { get; private set; }
        public int Fired { get; private set; }
        public int Landed { get; private set; }
        public OrbitalStrikes.Result Last { get; private set; }
        public float ReadyIn => Mathf.Max(0f, readyAt - Time.time);

        float readyAt;
        float nextLook;
        WarpPoint body;
        double tu, tv;
        string what;
        int left;
        bool targeted;

        void Awake()
        {
            Instance = this;
            StrikeFx.glowMaterial = glowMaterial;
            StrikeFx.beamMaterial = beamMaterial;
            StrikeFx.ringMaterial = ringMaterial;
        }
        void OnDestroy() { if (Instance == this) Instance = null; }
        void Start() { if (ship == null) ship = GetComponent<CarrierController>(); }

        /// <summary>The body under the ship that can be struck (a planet or moon, from space), or null.</summary>
        WarpPoint Over()
        {
            var pf = PlanetFlight.Instance;
            if (pf == null || PlanetLanding.Busy || ship == null || ship.Destroyed) return null;
            var b = pf.Body;
            if (!PlanetLanding.Seamless(b) || pf.Altitude > b.bodyRadius * 2f || pf.Altitude < 3000f) return null;
            return b;
        }

        void Update()
        {
            var b = Over();
            if (b != body) { nextLook = 0f; targeted = false; }      // (a new body: its own target, now)
            body = b;
            if (body != null && Time.time >= nextLook)
            {
                nextLook = Time.time + 1f;
                OrbitalStrikes.PlaceOf(body, ship.transform.position, out double u, out double v);
                targeted = OrbitalStrikes.NearestTarget(body, u, v, reach, out tu, out tv, out what, out left);
                if (!targeted) { tu = u; tv = v; what = null; left = 0; }
            }
            if (InputFocus.PanelUp) return;
            var kb = Keyboard.current;
            if (kb != null && kb.yKey.wasPressedThisFrame) Fire();
        }

        /// <summary>The HUD's line (null when there is nothing to strike from here).</summary>
        public string HudText()
        {
            if (body == null) return null;
            string at = targeted ? $"{what} ({left} left)" : "the ground below";
            return ReadyIn > 0f ? $"<color=#9aa4b0>strike: reloading {ReadyIn:F0} s</color>"
                                : $"<color=#ffb070>Y strike {at}</color>";
        }

        /// <summary>Drop a rod on the target (Y).</summary>
        public bool Fire()
        {
            if (body == null) { Notices.Post("No ground in reach to strike from here"); SoundFX.Ui(Sfx.UiError); return false; }
            if (ReadyIn > 0f) { Notices.Post($"Strike reloading: {ReadyIn:F0} s"); SoundFX.Ui(Sfx.UiError); return false; }
            return FireAt(body, tu, tv, targeted ? what : "the ground");
        }

        /// <summary>Drop a rod on a place on a body (the self-test aims it itself).</summary>
        public bool FireAt(WarpPoint b, double u, double v, string label)
        {
            readyAt = Time.time + cooldown;
            Fired++;
            Vector3 start = ship.transform.position - ship.Up * 60f;
            float fall = Vector3.Distance(start, OrbitalStrikes.GroundPoint(b, u, v));
            Notices.Post($"Kinetic strike away: {label}, {fall / 1000f:F0} km down");
            SoundFX.Play(Sfx.Cannon, start, 1f, 0.5f);
            var fireball = ship.GetComponent<Health>()?.deathEffect;
            // (from where it was dropped, turning with the body as it falls)
            Vector3 startLocal = b.transform.InverseTransformPoint(start);
            StrikeFx.Rod(() => b.transform.TransformPoint(startLocal), () => OrbitalStrikes.GroundPoint(b, u, v), Mathf.Clamp(fall / fallSpeed, 3f, 25f), new Color(1f, 0.85f, 0.6f), () =>
            {
                Vector3 at = OrbitalStrikes.GroundPoint(b, u, v);
                StrikeFx.Flash(at, radius * 10f, new Color(1f, 0.8f, 0.55f), fireball);
                Last = OrbitalStrikes.Hit(b, u, v, radius, craterDepth, "kinetic rod", true);
                Landed++;
                Notices.Post(Last.units + Last.buildings == 0 ? $"Strike on {b.displayName}: a crater, nothing hit"
                    : $"Strike on {b.displayName}: {Last.units} ground force{(Last.units == 1 ? "" : "s")} destroyed" +
                      (Last.buildings > 0 ? $", {Last.buildings} buildings of {Last.place} ruined" : ""));
            });
            return true;
        }
    }
}

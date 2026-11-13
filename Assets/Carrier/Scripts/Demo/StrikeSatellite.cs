using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// A corsair strike satellite, hanging over a settlement and turning with its planet: every
    /// `intervalMinutes` of game time it fires on the settlement below, an ion cannon's beam or a
    /// mass driver's salvo of slugs, ruining buildings and killing its people (OrbitalStrikes).
    /// It goes on whether or not anyone is near (on the game's clock, WorldState, and saved); only
    /// near it (within `seenWithin`) is it seen. Destroyed, it strikes no more till the corsairs
    /// put it back (its Health's respawn), and its clock starts again from then.
    /// </summary>
    public class StrikeSatellite : MonoBehaviour
    {
        public enum Weapon { IonCannon, MassDriver }

        public Weapon weapon = Weapon.IonCannon;
        [Tooltip("The settlement it fires on (its warp point's name).")]
        public string target = "Port Ochre";
        public float intervalMinutes = 30f;
        public float radius = 300f;
        public float craterDepth = 35f;
        [Tooltip("Within this of it (m), its strikes are seen (the beam, the slugs, the flash).")]
        public float seenWithin = 2500000f;

        public static readonly System.Collections.Generic.List<StrikeSatellite> All = new System.Collections.Generic.List<StrikeSatellite>();
        public int Strikes { get; private set; }
        public OrbitalStrikes.Result Last { get; private set; }
        public string Label => $"{GetComponent<TargetInfo>()?.displayName ?? name}";

        Health health;
        string timer;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            health = GetComponent<Health>();
            timer = "strike:" + Label;
            if (!WorldState.TryTimer(timer, out _)) WorldState.SetTimer(timer, WorldState.Clock + intervalMinutes * 60.0 * 0.5);
            if (health != null) health.onRespawned.AddListener(() => WorldState.SetTimer(timer, WorldState.Clock + intervalMinutes * 60.0));
        }

        /// <summary>When it fires next (game seconds), or never while it is destroyed.</summary>
        public double NextAt => health != null && health.Dead ? double.PositiveInfinity
            : WorldState.TryTimer(timer, out double at) ? at : WorldState.Clock + intervalMinutes * 60.0 * 0.5;

        void Update()
        {
            if (health != null && health.Dead) return;
            // (no clock for it: a game from before it, or the world just loaded without it)
            if (!WorldState.TryTimer(timer, out _)) { WorldState.SetTimer(timer, NextAt); return; }
            if (WorldState.Clock >= NextAt) FireNow();
        }

        /// <summary>Fire on the settlement now (the self-test, or its time come).</summary>
        public void FireNow()
        {
            WorldState.SetTimer(timer, WorldState.Clock + intervalMinutes * 60.0);
            var place = WarpPoint.All.Find(w => w != null && w.displayName == target);
            if (place == null || PlanetLanding.Instance == null || !OrbitalStrikes.SiteOf(place, out var body, out var site)) return;
            Strikes++;
            double u = site.su, v = site.sv;
            var ship = PlayerShips.Primary;
            bool seen = ship != null && !PlanetLanding.Busy && Vector3.Distance(ship.position, transform.position) < seenWithin;
            string what = weapon == Weapon.IonCannon ? "ion cannon" : "mass driver";
            System.Action land = () =>
            {
                Last = OrbitalStrikes.Hit(body, u, v, radius, craterDepth, what, seen);
                if (Last.buildings > 0 || seen)
                    Notices.Post($"{target} struck from orbit by a corsair {what}: {Last.buildings} buildings ruined, {Last.casualties} killed");
            };
            if (!seen) { land(); return; }
            Vector3 Ground() => OrbitalStrikes.GroundPoint(body, u, v);
            var fireball = health != null ? health.deathEffect : null;
            if (weapon == Weapon.IonCannon)
            {
                SoundFX.Play(Sfx.HeavyBeamFire, transform.position, 1f, 0.7f);
                StrikeFx.Beam(() => transform.position, Ground, 3f, new Color(0.55f, 0.75f, 1f), () =>
                {
                    StrikeFx.Flash(Ground(), radius * 8f, new Color(0.6f, 0.8f, 1f), fireball);
                    land();
                });
            }
            else
            {
                // a salvo of three slugs, a moment apart; the damage when the first lands
                for (int k = 0; k < 3; k++)
                {
                    int shot = k;
                    // (from where it fired, turning with the body as the slug falls)
                    Vector3 from = body.transform.InverseTransformPoint(transform.position);
                    float fall = Vector3.Distance(transform.position, Ground());
                    StrikeFx.Rod(() => body.transform.TransformPoint(from), Ground, Mathf.Clamp(fall / 6000f, 3f, 20f) + k * 0.6f, new Color(1f, 0.6f, 0.35f), () =>
                    {
                        StrikeFx.Flash(Ground(), radius * 6f, new Color(1f, 0.7f, 0.45f), fireball);
                        if (shot == 0) land();
                    });
                }
                SoundFX.Play(Sfx.Cannon, transform.position, 1f, 0.4f);
            }
        }
    }
}

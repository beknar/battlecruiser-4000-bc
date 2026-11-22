using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// An EMP burst (the Grey Shroud's): it charges for `chargeTime` seconds, glowing brighter and
    /// with a warning on the HUD of a player's ship in its reach (ShipHud: time to get out), then
    /// sends out a pulse that hits every player's ship within `radius` with an EMP (HitKind.Emp:
    /// a component knocked out for a time, ShipSystems). Its tactics fire it (Fire), or with
    /// `autoFire` it fires on its own when a player's ship is in reach and it is ready again.
    /// Only the game running the enemies fires it (the host's: off on a guest's puppet); the
    /// hits go to the other players' games as any of the host's do.
    /// </summary>
    public class EmpBurst : MonoBehaviour
    {
        public float radius = 2500f;
        [Tooltip("The pulse's strength (hull points, before the target's shield).")]
        public float strength = 400f;
        public float chargeTime = 4f;
        public float cooldown = 30f;
        public bool autoFire = true;
        [Tooltip("What glows as it charges (scaled up to the burst), if anything.")]
        public Transform glow;
        public Light glowLight;

        public enum State { Ready, Charging, Cooling }
        public State Current { get; private set; }
        float stateAt;
        Health health;
        Vector3 glowScale = Vector3.one;

        /// <summary>Pulses sent, and ships hit by them (the tests).</summary>
        public int Bursts { get; private set; }
        public int ShipsHit { get; private set; }

        /// <summary>How far through its charge it is (0 to 1; 0 when not charging).</summary>
        public float Charge => Current == State.Charging ? Mathf.Clamp01((Time.time - stateAt) / Mathf.Max(0.01f, chargeTime)) : 0f;

        public static readonly System.Collections.Generic.List<EmpBurst> All = new System.Collections.Generic.List<EmpBurst>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAll() => All.Clear();

        void Awake()
        {
            health = GetComponent<Health>();
            if (glow != null) { glowScale = glow.localScale; glow.gameObject.SetActive(false); }
            if (glowLight != null) glowLight.enabled = false;
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); Show(0f); if (Current == State.Charging) Current = State.Ready; }

        /// <summary>Start charging (its tactics), if it is ready: true if it did.</summary>
        public bool Fire()
        {
            if (Current != State.Ready || (health != null && health.Dead) || ShipSystems.WeaponsDownOn(this, ref systems)) return false;
            Current = State.Charging;
            stateAt = Time.time;
            return true;
        }

        ShipSystems systems;

        void Update()
        {
            if (health != null && health.Dead) { Current = State.Ready; Show(0f); return; }
            switch (Current)
            {
                case State.Ready:
                    if (autoFire && AnyInReach()) Fire();
                    break;
                case State.Charging:
                    Show(Charge);
                    if (Time.time - stateAt >= chargeTime) Burst();
                    break;
                case State.Cooling:
                    if (Time.time - stateAt >= cooldown) Current = State.Ready;
                    break;
            }
        }

        bool AnyInReach()
        {
            foreach (var p in PlayerShips.All)
                if (p != null && p.gameObject.activeInHierarchy && PlayerShips.Present(p) && (p.position - transform.position).sqrMagnitude <= radius * radius) return true;
            return false;
        }

        /// <summary>The pulse: every player's ship within reach takes an EMP.</summary>
        void Burst()
        {
            Show(0f);
            Current = State.Cooling;
            stateAt = Time.time;
            Bursts++;
            // (its weapons knocked out while it charged: the pulse fizzles)
            if (ShipSystems.WeaponsDownOn(this, ref systems)) return;
            var was = Health.Source;
            Health.Source = transform;
            foreach (var p in PlayerShips.All)
            {
                if (p == null || !p.gameObject.activeInHierarchy || !PlayerShips.Present(p)) continue;
                if ((p.position - transform.position).sqrMagnitude > radius * radius) continue;
                var h = p.GetComponent<Health>();
                if (h == null || h.Dead) continue;
                h.Damage(strength, p.position, HitKind.Emp);
                ShipsHit++;
            }
            Health.Source = was;
            SoundFX.Play(Sfx.ExplosionSmall, transform.position, 0.8f, 1.6f);
        }

        void Show(float charge)
        {
            if (glow != null)
            {
                glow.gameObject.SetActive(charge > 0f);
                glow.localScale = glowScale * Mathf.Lerp(0.2f, 1f, charge);
            }
            if (glowLight != null) { glowLight.enabled = charge > 0f; glowLight.intensity = charge * 8f; }
        }

        /// <summary>What a player's HUD says of a burst charging with its ship in reach: who, and
        /// how long to get out (""; none).</summary>
        public static string HudText(Transform ship)
        {
            if (ship == null) return "";
            foreach (var b in All)
            {
                if (b == null || b.Current != State.Charging) continue;
                float d = Vector3.Distance(b.transform.position, ship.position);
                if (d > b.radius) continue;
                var info = b.GetComponent<TargetInfo>();
                string who = Sensors.NameOf(b.transform, info != null ? info.Name : b.name);
                float left = Mathf.Max(0f, b.chargeTime - (Time.time - b.stateAt));
                return $"EMP charging: {who}, {d / 1000f:0.0} km—get beyond {b.radius / 1000f:0.#} km in {left:0.0} s";
            }
            return "";
        }
    }
}

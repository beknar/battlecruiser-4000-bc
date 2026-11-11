using System.Collections.Generic;
using CarrierGame.Demo;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The frigate's mining laser: a turret under the bow that cuts ore out of whatever deposit is
    /// nearest (within `range` of its surface) and draws it back aboard into the cargo hold, a few
    /// tonnes a second in the deposit's own mix. In space it works rocks, moons and comets; landed
    /// on a planet it works the veins in the ground. It stops when the hold is full, moves on by
    /// itself when a deposit is mined out, and passes over one it can't get a clear line to.
    /// </summary>
    public class MiningLaser : MonoBehaviour
    {
        [Header("Laser")]
        [Tooltip("How far it reaches, from the turret to the deposit's surface (m).")]
        public float range = 1500f;
        [Tooltip("Tonnes a second it brings aboard.")]
        public float rate = 8f;
        [Tooltip("The turret head, turned to face what it is cutting.")]
        public Transform head;
        public Transform muzzle;

        [Header("Effects")]
        public LineRenderer beamCore;
        public LineRenderer beamGlow;
        [Tooltip("Sparks and dust where the beam cuts.")]
        public ParticleSystem impact;
        [Tooltip("Ore streaming back to the ship.")]
        public ParticleSystem collect;
        public Light glow;
        public float glowIntensity = 2500f;

        public bool On { get; private set; }
        /// <summary>Cutting this frame (on, with a deposit in range and in sight).</summary>
        public bool Cutting { get; private set; }
        public IMineralSource Source { get; private set; }
        public Vector3 HitPoint { get; private set; }
        /// <summary>What it is doing, for the HUD.</summary>
        public string Status { get; private set; } = "off";
        public float TotalMined { get; private set; }
        public int DepositsMinedOut { get; private set; }

        CarrierController ship;
        CargoHold hold;
        WarpDrive warp;
        AudioSource hum;
        float searchAt, blockedSince = -1f;
        // deposits it couldn't get a clear line to, and until when it leaves them alone
        readonly Dictionary<IMineralSource, float> skip = new Dictionary<IMineralSource, float>();
        readonly List<IMineralSource> candidates = new List<IMineralSource>();
        readonly List<IMineralSource> near = new List<IMineralSource>();
        static readonly RaycastHit[] Hits = new RaycastHit[24];
        /// <summary>How far a vein spreads round its middle (m).</summary>
        public const float VeinRadius = 200f;

        void Awake()
        {
            ship = GetComponentInParent<CarrierController>();
            hold = GetComponentInParent<CargoHold>();
            warp = ship != null ? ship.GetComponent<WarpDrive>() : null;
            ShowBeam(false);
        }

        void Start()
        {
            if (warp == null && ship != null) warp = ship.GetComponent<WarpDrive>();   // added after the prefab
            hum = gameObject.AddComponent<AudioSource>();
            hum.clip = SfxSynth.MiningLoop();
            hum.loop = true;
            hum.spatialBlend = 1f;
            hum.rolloffMode = AudioRolloffMode.Logarithmic;
            hum.minDistance = 250f;
            hum.maxDistance = 20000f;
            hum.dopplerLevel = 0f;
            hum.volume = 0f;
        }

        /// <summary>Why the laser can't work now (null if it can).</summary>
        public string Blocked()
        {
            if (ship == null || hold == null) return "no mining gear fitted";
            string busy = MineralSources.ShipBusy(ship, warp);
            if (busy != null) return busy;
            if (hold.Full) return $"the cargo hold is full ({hold.capacity:F0} t)";
            return null;
        }

        public bool TurnOn(out string why)
        {
            why = Blocked();
            if (why != null) return false;
            FindSources();
            if (candidates.Count == 0)
            {
                why = PlanetLanding.OnSurface ? $"no mineral vein within {range / 1000f:F1} km: scan (V) and fly over one"
                                               : $"nothing to mine within {range / 1000f:F1} km: scan (V) and close on a rock";
                return false;
            }
            On = true;
            Source = null;
            searchAt = 0f;
            skip.Clear();
            return true;
        }

        public void TurnOff()
        {
            On = false;
            Cutting = false;
            Source = null;
            Status = "off";
            ShowBeam(false);
        }

        void Stop(string message)
        {
            TurnOff();
            Notices.Post(message);
        }

        void Update()
        {
            Cutting = false;
            if (!On) { Fade(); return; }
            string why = Blocked();
            if (why != null)
            {
                if (hold != null && hold.Full) SoundFX.Ui(Sfx.UiAlert);
                Stop("Mining laser off: " + why);
                Fade();
                return;
            }

            // keep on the deposit it is cutting while it can; otherwise find the nearest
            if (Source == null || !Usable(Source, out _))
            {
                if (Time.time >= searchAt)
                {
                    searchAt = Time.time + 0.5f;
                    Source = Pick();
                }
                else Source = null;
            }
            if (Source == null)
            {
                Status = "searching: nothing in range";
                Fade();
                return;
            }

            // cut: aim at its surface, take ore out, draw it aboard
            if (!Aim(Source, out Vector3 hit, out IMineralSource struck))
            {
                Status = "no clear line to " + Source.Label;
                Fade();
                // still blocked after a second (a rock that's been mined out, a ridge): try another
                if (blockedSince < 0f) blockedSince = Time.time;
                else if (Time.time - blockedSince > 1f)
                {
                    skip[Source] = Time.time + 6f;
                    Source = null;
                    searchAt = 0f;
                    blockedSince = -1f;
                }
                return;
            }
            blockedSince = -1f;
            if (struck != null && struck != Source) Source = struck;   // something nearer in the way: cut that
            HitPoint = hit;
            Cutting = true;
            float want = Mathf.Min(rate * Time.deltaTime, hold.Free);
            float got = Source.Take(want);
            hold.AddMix(Source.Mix, got);
            TotalMined += got;
            Status = $"cutting {Source.Label}, {Source.Remaining:F0} t left";
            if (Source.Depleted)
            {
                DepositsMinedOut++;
                bool broke = Source is MineralDeposit md && md.BreakUp();
                if (broke && impact != null)
                {
                    impact.transform.position = Source.Position;
                    impact.Emit(400);
                }
                Notices.Post(broke ? $"Mined out: the {Source.Label} broke up" : $"Mined out: {Source.Label}");
                Source = null;
                searchAt = 0f;
            }
            if (hold.Full)
            {
                SoundFX.Ui(Sfx.UiAlert);
                Stop($"Cargo hold full: {hold.capacity:F0} t aboard. J jettisons the cheapest mineral");
            }
        }

        void LateUpdate()
        {
            if (!Cutting) { ShowBeam(false); return; }
            if (head != null) head.rotation = Quaternion.LookRotation(HitPoint - head.position, ship.transform.up);
            Vector3 from = From;
            ShowBeam(true);
            float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 9f, 0.3f);
            if (beamCore != null)
            {
                beamCore.SetPosition(0, from);
                beamCore.SetPosition(1, HitPoint);
                beamCore.widthMultiplier = 4.5f * flicker;
            }
            if (beamGlow != null)
            {
                beamGlow.SetPosition(0, from);
                beamGlow.SetPosition(1, HitPoint);
                beamGlow.widthMultiplier = 22f * flicker;
            }
            Vector3 back = from - HitPoint;
            float d = back.magnitude;
            var face = Quaternion.LookRotation(d > 1f ? back / d : Vector3.up);
            if (impact != null) impact.transform.SetPositionAndRotation(HitPoint, face);
            if (collect != null)
            {
                collect.transform.SetPositionAndRotation(HitPoint, face);
                var m = collect.main;
                float life = Mathf.Clamp(d / 900f, 0.35f, 1.6f);
                m.startLifetime = life;
                m.startSpeed = d / life;
                if (Source != null) m.startColor = Color.Lerp(Minerals.Get(Minerals.Prize(Source.Mix)).color, Color.white, 0.3f) * 1.6f;
            }
            if (glow != null) glow.intensity = glowIntensity * flicker;
            if (hum != null)
            {
                hum.volume = Mathf.MoveTowards(hum.volume, 0.35f, Time.deltaTime);   // (it sits on the turret)
                if (!hum.isPlaying) hum.Play();
            }
        }

        void Fade()
        {
            if (hum != null && hum.isPlaying)
            {
                hum.volume = Mathf.MoveTowards(hum.volume, 0f, Time.deltaTime * 2f);
                if (hum.volume <= 0f) hum.Stop();
            }
        }

        void ShowBeam(bool show)
        {
            if (beamCore != null) beamCore.enabled = show;
            if (beamGlow != null) beamGlow.enabled = show;
            SetEmitting(impact, show);
            SetEmitting(collect, show);
            if (glow != null && !show) glow.intensity = 0f;
        }

        static void SetEmitting(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            var em = ps.emission;
            em.enabled = on;
            if (on && !ps.isPlaying) ps.Play();
        }

        // ------------------------------------------------------------------ choosing what to cut

        Vector3 From => muzzle != null ? muzzle.position : transform.position;

        /// <summary>How far the laser has to reach to its surface.</summary>
        public float Reach(IMineralSource s) => Mathf.Max(0f, Vector3.Distance(From, s.Position) - s.Radius);

        bool Usable(IMineralSource s, out float reach)
        {
            reach = float.MaxValue;
            if (s == null || !s.Valid || s.Depleted) return false;
            reach = Reach(s);
            return reach <= range;
        }

        void FindSources()
        {
            candidates.Clear();
            if (skip.Count > 0)
            {
                var done = new List<IMineralSource>();
                foreach (var kv in skip) if (Time.time > kv.Value) done.Add(kv.Key);
                foreach (var k in done) skip.Remove(k);
            }
            // looked for a little further than it reaches, then each checked from the muzzle
            MineralSources.Near(From, range + 100f, near);
            foreach (var s in near) if (Usable(s, out _) && !skip.ContainsKey(s)) candidates.Add(s);
        }

        IMineralSource Pick()
        {
            FindSources();
            if (candidates.Count == 0) return null;
            IMineralSource best = null;
            float bestReach = float.MaxValue;
            foreach (var c in candidates)
            {
                float r = Reach(c);
                if (r < bestReach) { bestReach = r; best = c; }
            }
            return best;
        }

        /// <summary>Where the beam lands on a deposit: the first thing between the turret and its
        /// middle (another deposit counts: it is cut instead). False if something else is in the way.</summary>
        bool Aim(IMineralSource s, out Vector3 point, out IMineralSource struck)
        {
            Vector3 from = From;
            Vector3 to = s.Position;
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            struck = null;
            point = to;
            if (dist < 1f) return true;
            dir /= dist;
            int n = Physics.RaycastNonAlloc(from, dir, Hits, dist + 5f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            RaycastHit first = default;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (ship != null && h.transform.IsChildOf(ship.transform)) continue;
                if (h.distance < best) { best = h.distance; first = h; any = true; }
            }
            if (s is SurfaceVein)
            {
                // a vein: the ground (or sea, or cloud deck) over it. It spreads under the ground
                // round its middle, so the beam can land anywhere within VeinRadius of it; a ridge
                // further off than that blocks it
                if (any)
                {
                    Vector3 g = first.point - to;
                    g.y = 0f;
                    if (best < dist - 40f && g.magnitude > VeinRadius) return false;
                    point = best < dist - 40f ? first.point : to;
                }
                else point = to;
                return true;
            }
            if (!any)
            {
                point = to - dir * s.Radius;
                return true;
            }
            var dep = first.collider.GetComponentInParent<MineralDeposit>();
            if (dep == null) return false;
            if (!ReferenceEquals(dep, s))
            {
                if (dep.Depleted || Reach(dep) > range) return false;
                struck = dep;
            }
            point = first.point;
            return true;
        }
    }
}

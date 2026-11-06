using System.Collections.Generic;
using CarrierGame.Demo;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The frigate's mineral sensor. Passively it picks up traces: how many deposits there are
    /// within range. A scan (Scan()) sends out a pulse that sweeps out to its full range in a
    /// couple of seconds, finding each deposit as it passes: what it is, what it is made of and
    /// how much there is. The finds are marked on screen for a minute. In space it reaches 12 km;
    /// on a planet's surface, 5 km (it sees veins in the ground).
    /// </summary>
    public class MineralSensor : MonoBehaviour
    {
        public float spaceRange = 12000f;
        public float surfaceRange = 5000f;
        [Tooltip("Seconds for the pulse to sweep out to full range.")]
        public float sweepTime = 2f;
        [Tooltip("Seconds the finds stay marked.")]
        public float showFor = 60f;
        [Tooltip("The pulse: two rings (across and along the ship).")]
        public LineRenderer ringFlat, ringUpright;

        public struct Find
        {
            public IMineralSource source;
            public float distance;
        }

        /// <summary>What the last scan found, nearest first.</summary>
        public readonly List<Find> Finds = new List<Find>();
        public bool Sweeping { get; private set; }
        public float SweepProgress => Sweeping ? Mathf.Clamp01((Time.time - sweepStart) / sweepTime) : 0f;
        public float LastScanTime { get; private set; } = -1000f;
        public int Scans { get; private set; }
        /// <summary>The finds are still marked.</summary>
        public bool Showing => !Sweeping && Time.time - LastScanTime < showFor && Finds.Count > 0;
        /// <summary>Deposits (not mined out) within range now, from the passive traces.</summary>
        public int Traces { get; private set; }
        public float Range => PlanetLanding.OnSurface ? surfaceRange : spaceRange;
        /// <summary>The range the last scan used.</summary>
        public float ScanRange { get; private set; }

        CarrierController ship;
        WarpDrive warp;
        float sweepStart, traceAt;
        Vector3 sweepCenter;
        readonly List<IMineralSource> pool = new List<IMineralSource>();
        readonly List<SurfaceVein> veins = new List<SurfaceVein>();
        readonly HashSet<IMineralSource> found = new HashSet<IMineralSource>();

        const int RingPoints = 96;

        void Awake()
        {
            ship = GetComponentInParent<CarrierController>();
            foreach (var r in new[] { ringFlat, ringUpright })
            {
                if (r == null) continue;
                r.positionCount = RingPoints;
                r.loop = true;
                r.useWorldSpace = true;
                r.enabled = false;
            }
        }

        void Start() => warp = ship != null ? ship.GetComponent<WarpDrive>() : null;

        /// <summary>Why it can't scan now (null if it can).</summary>
        public string Blocked()
        {
            if (ship == null) return "no sensor fitted";
            if (ship.Destroyed) return "the ship is destroyed";
            if (warp != null && warp.Current != WarpDrive.State.Ready) return "the ship is in warp";
            if (PlanetLanding.Transitioning) return "not while landing or taking off";
            if (Sweeping) return "already scanning";
            return null;
        }

        public bool Scan(out string why)
        {
            why = Blocked();
            if (why != null) return false;
            Sweeping = true;
            sweepStart = Time.time;
            sweepCenter = ship.transform.position;
            ScanRange = Range;
            found.Clear();
            Finds.Clear();
            Gather(sweepCenter, ScanRange);
            Scans++;
            SoundFX.Ui(Sfx.SensorPing);
            return true;
        }

        /// <summary>Every deposit (not mined out) within `range` of a point.</summary>
        void Gather(Vector3 at, float range)
        {
            pool.Clear();
            if (PlanetLanding.OnSurface)
            {
                SurfaceVeins.Near(PlanetLanding.Instance.Surface, at, range, veins);
                foreach (var v in veins) if (!v.Depleted) pool.Add(v);
                return;
            }
            foreach (var d in MineralDeposit.All)
                if (d != null && !d.Depleted && Vector3.Distance(at, d.Position) - d.Radius <= range) pool.Add(d);
        }

        void Update()
        {
            if (ship == null) return;
            if (Sweeping)
            {
                // a lost scan: the ship jumped away or landed
                if (Blocked() is string why && why != "already scanning")
                {
                    Sweeping = false;
                    ShowRings(false);
                    return;
                }
                float k = SweepProgress;
                float reach = ScanRange * k;
                foreach (var s in pool)
                {
                    if (found.Contains(s) || !s.Valid) continue;
                    if (Vector3.Distance(sweepCenter, s.Position) - s.Radius > reach) continue;
                    found.Add(s);
                    s.Discovered = true;
                }
                if (k >= 1f) Finish();
            }
            if (Time.time >= traceAt)
            {
                traceAt = Time.time + 1f;
                if (!Sweeping && Blocked() == null)
                {
                    Gather(ship.transform.position, Range);
                    Traces = pool.Count;
                }
                else if (Blocked() != null && !Sweeping) Traces = 0;
            }
        }

        void Finish()
        {
            Sweeping = false;
            LastScanTime = Time.time;
            Finds.Clear();
            Vector3 p = ship.transform.position;
            foreach (var s in found) Finds.Add(new Find { source = s, distance = Vector3.Distance(p, s.Position) - s.Radius });
            Finds.Sort((a, b) => a.distance.CompareTo(b.distance));
            if (Finds.Count == 0)
            {
                CarrierDemoInput.Toast($"Mineral scan: nothing within {ScanRange / 1000f:F0} km");
                return;
            }
            // the best find: the most valuable ore body
            IMineralSource rich = null;
            float best = -1f;
            foreach (var f in Finds)
            {
                float v = Minerals.ValuePerTonne(f.source.Mix);
                if (v > best) { best = v; rich = f.source; }
            }
            var near = Finds[0];
            string what = PlanetLanding.OnSurface ? "veins" : "deposits";
            CarrierDemoInput.Toast($"Mineral scan: {Finds.Count} {what} within {ScanRange / 1000f:F0} km. Nearest {Mathf.Max(0f, near.distance) / 1000f:F1} km; " +
                                   $"richest {Minerals.Name(Minerals.Prize(rich.Mix))} ({rich.Label})");
        }

        void LateUpdate()
        {
            if (!Sweeping) { ShowRings(false); return; }
            float k = SweepProgress;
            float r = Mathf.Max(30f, ScanRange * k);
            float alpha = Mathf.Clamp01(1.15f - k);
            Vector3 c = ship.transform.position;
            DrawRing(ringFlat, c, Vector3.right, Vector3.forward, r, alpha);
            DrawRing(ringUpright, c, ship.transform.right, Vector3.up, r, alpha * 0.7f);
            ShowRings(true);
        }

        void ShowRings(bool on)
        {
            if (ringFlat != null) ringFlat.enabled = on;
            if (ringUpright != null) ringUpright.enabled = on;
        }

        static void DrawRing(LineRenderer lr, Vector3 c, Vector3 a, Vector3 b, float r, float alpha)
        {
            if (lr == null) return;
            for (int i = 0; i < RingPoints; i++)
            {
                float t = i / (float)RingPoints * Mathf.PI * 2f;
                lr.SetPosition(i, c + (a * Mathf.Cos(t) + b * Mathf.Sin(t)) * r);
            }
            lr.widthMultiplier = 6f + r * 0.006f;
            var col = new Color(1f, 1f, 1f, alpha);
            lr.startColor = lr.endColor = col;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Value noise over a planet's surface plane (u east, v north, in metres) that wraps round the
    /// planet: every octave repeats exactly once per circumference in u, so flying east all the way
    /// round comes back to the same ground. Read-only once built, so tiles can be generated on
    /// worker threads.
    /// </summary>
    public sealed class PlanetNoise
    {
        readonly int[] period;
        readonly double[] fu, fv;
        readonly int seed;
        readonly float gain;

        public PlanetNoise(double circumference, float scale, int seed, int octaves, float gain = 0.5f, float lacunarity = 2.03f, float vStretch = 1f)
        {
            this.seed = seed;
            this.gain = gain;
            period = new int[octaves];
            fu = new double[octaves];
            fv = new double[octaves];
            double f = 1.0 / scale;
            for (int k = 0; k < octaves; k++)
            {
                int p = Math.Max(1, (int)Math.Round(circumference * f));
                period[k] = p;
                fu[k] = p / circumference;
                fv[k] = f * vStretch;
                f *= lacunarity;
            }
        }

        static float S(float t) => t * t * (3f - 2f * t);

        static float Value(double x, double y, int p, int seed)
        {
            double xf = Math.Floor(x), yf = Math.Floor(y);
            long xl = (long)xf;
            int yi = (int)yf;
            float tx = S((float)(x - xf)), ty = S((float)(y - yf));
            int x0 = (int)(((xl % p) + p) % p), x1 = x0 + 1 == p ? 0 : x0 + 1;
            float a = ProcNoise.Hash(x0, yi, 0, seed), b = ProcNoise.Hash(x1, yi, 0, seed);
            float c = ProcNoise.Hash(x0, yi + 1, 0, seed), d = ProcNoise.Hash(x1, yi + 1, 0, seed);
            float ab = a + (b - a) * tx, cd = c + (d - c) * tx;
            return ab + (cd - ab) * ty;
        }

        /// <summary>Fractal noise, roughly -1..1.</summary>
        public float Fbm(double u, double v)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int k = 0; k < period.Length; k++)
            {
                sum += (Value(u * fu[k], v * fv[k], period[k], seed + k * 31) * 2f - 1f) * amp;
                norm += amp;
                amp *= gain;
            }
            return sum / norm;
        }

        /// <summary>Ridged noise, 0..1 (sharp crests).</summary>
        public float Ridged(double u, double v)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int k = 0; k < period.Length; k++)
            {
                float n = 1f - Mathf.Abs(Value(u * fu[k], v * fv[k], period[k], seed + k * 57) * 2f - 1f);
                sum += n * n * amp;
                norm += amp;
                amp *= gain;
            }
            return sum / norm;
        }

        /// <summary>Billowy noise, 0..1 (rounded puffs: clouds).</summary>
        public float Billow(double u, double v)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int k = 0; k < period.Length; k++)
            {
                sum += Mathf.Abs(Value(u * fu[k], v * fv[k], period[k], seed + k * 43) * 2f - 1f) * amp;
                norm += amp;
                amp *= gain;
            }
            return sum / norm;
        }

        /// <summary>The first octave alone, 0..1.</summary>
        public float Value01(double u, double v) => Value(u * fu[0], v * fv[0], period[0], seed);
    }

    /// <summary>What stands in one area of a planet: a friendly landing pad, or a hostile base.</summary>
    public enum SiteKind { None, Outpost, Base }

    /// <summary>The ground forces of a hostile world (and, on a water world, its sea forces: a base
    /// on the sea floor that launches torpedoes, and submarines).</summary>
    public enum UnitKind { Battery, MissileSite, Tank, Patrol, PadFighter, PadGunship, SeaBase, Submarine }

    public struct UnitSpot
    {
        public UnitKind kind;
        public double u, v;        // where, in the planet's own coordinates
        public float yaw;
        public int id;             // unique within its area (for remembering what was destroyed)
    }

    /// <summary>The fixed things in one 5 km area of a planet (always the same for the same area).</summary>
    public sealed class TileFeatures
    {
        public int i, j;
        public SiteKind site;
        /// <summary>A named base seen from orbit (Selene Base, Cinder Fortress): bigger than the
        /// others, and a hostile one is more heavily defended. Null for an ordinary site.</summary>
        public string baseName;
        public double su, sv;
        public float siteYaw;
        public float siteHeight;
        public volatile bool siteHeightReady;
        public double[] coneU = Array.Empty<double>(), coneV = Array.Empty<double>();
        public float[] coneR = Array.Empty<float>(), coneH = Array.Empty<float>();
        public readonly List<UnitSpot> units = new List<UnitSpot>();
    }

    /// <summary>
    /// A whole planet's surface, as a grid of 5 km areas. Its size comes from the body's own size
    /// (times `scale`): N areas round the equator (the circumference) and M from pole to pole,
    /// so that the N x M areas add up to the planet's surface area (the grid is Lambert's
    /// equal-area map: v = R sin(latitude)). The ground at any point is a fixed function of where
    /// it is on the planet, so an area is the same every time it is built, whichever way the
    /// ship came to it:
    ///
    /// - east and west wrap: after N areas the ship is back where it started;
    /// - over a pole the ship comes down the far side (longitude + 180, the map turned round), and
    ///   carrying on over the other pole brings it back again. The ground flattens into a polar
    ///   plateau near each pole so the two sides meet.
    ///
    /// Positions are "unwrapped" (u, v): they keep counting past the edges of the map, and
    /// Canonical() folds them back onto the planet. Everything here is read-only after it is made
    /// (the feature cache is locked), so areas can be generated on worker threads.
    /// </summary>
    public sealed class SurfaceWorld
    {
        public const float T = SurfaceGenerator.Size;
        public const float PadRadius = 230f;
        public const float PadLift = 1.5f;       // the pad's top above the flattened ground
        public const float BaseLevel = -300f;    // terrain height 0 (every area shares one range, so edges match exactly)
        public const float Range = 1800f;

        public readonly SurfaceKind kind;
        public readonly string name;
        public readonly int seed;
        public readonly bool hostile;
        /// <summary>A water world's land lowered (negative: more sea) beyond its sites' islands.</summary>
        readonly float landShift;
        /// <summary>Areas round the equator, and from pole to pole.</summary>
        public readonly int N, M;
        /// <summary>Circumference, half the pole-to-pole extent (v runs -V..V), radius (m).</summary>
        public readonly double C, V, R;
        public readonly float polarBand;
        public int AreaCount => N * M;
        public double AreaKm2 => N * (double)M * T * T / 1e6;

        // what it is like
        public string description;
        public readonly Look look = new Look();
        public float seaLevel = float.NegativeInfinity;   // height of the sea / lava (or -inf)
        public bool solid = true;
        public float floorLevel;                          // cloud deck (gas giant)
        public float platformLift;
        public float edgeLevel;                           // the far ground, beyond the built areas
        public float poleLevel;
        public bool outposts;                             // someone lives here (landing pads)
        public Color waterColor = new Color(0.03f, 0.12f, 0.2f);
        public int boulders, floatingRocks;
        public Vector2 boulderSize = new Vector2(6f, 30f);
        public float boulderSlope = 0.5f;
        public Color rockTint = Color.white;
        public ParticleLook particles;
        float craterAmount;
        float siteChance;

        /// <summary>The named bases on this world (where they are seen from orbit).</summary>
        readonly List<(string name, double u, double v, bool hostile)> bases = new List<(string, double, double, bool)>();
        public int BaseCount => bases.Count;

        // the ground
        readonly PlanetNoise n1, n2, n3, n4, n5, n6, n7, nSplat;
        float duneKu, duneKv, mesaThreshold, mareBias;
        const float Lava = 52f, Sea = 120f;

        // ------------------------------------------------------------------ making one

        /// <summary>The surface of a body. `radius` is its surface radius (m).</summary>
        public SurfaceWorld(SurfaceKind kind, string name, double radius, bool hostile)
        {
            this.kind = kind;
            this.name = name;
            this.hostile = hostile;
            unchecked
            {
                int h = 17;
                foreach (char ch in name) h = h * 31 + ch;
                seed = (h & 0x7fffffff) % 1000000;
            }
            N = Math.Max(4, 2 * (int)Math.Round(Math.PI * radius / T));
            C = N * (double)T;
            R = C / (2.0 * Math.PI);
            M = Math.Max(2, (int)Math.Round(2.0 * R / T));
            V = M * T * 0.5;
            polarBand = (float)Math.Min(2500.0, V * 0.35);

            var rng = new System.Random(seed);
            float Rn(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int s = seed * 7 + 11;
            nSplat = new PlanetNoise(C, 300f, s + 77, 3);
            var L = look;
            switch (kind)
            {
                case SurfaceKind.Volcanic:
                    n1 = new PlanetNoise(C, 1300f, s, 5);
                    n2 = new PlanetNoise(C, 160f, s + 5, 3);
                    n3 = new PlanetNoise(C, 1100f, s + 9, 3, 0.5f, 2.1f);
                    n4 = new PlanetNoise(C, 140f, s + 21, 3, 0.5f, 2.1f);
                    seaLevel = Lava;
                    edgeLevel = 90f; poleLevel = 95f;
                    boulders = 70; boulderSize = new Vector2(5f, 26f); rockTint = new Color(0.32f, 0.27f, 0.25f);
                    siteChance = 0.3f;
                    description = "basalt plains, cinder cones and rivers of lava";
                    L.skyTop = new Color(0.16f, 0.08f, 0.06f); L.skyBottom = new Color(0.22f, 0.09f, 0.05f); L.sunSize = 0.035f; L.sunGlow = 0.6f;
                    L.fog = new Color(0.34f, 0.17f, 0.11f); L.fogStart = 600f; L.fogEnd = 7000f;
                    L.sunColor = new Color(1f, 0.66f, 0.45f); L.sunIntensity = 1.9f;
                    L.ambientSky = new Color(0.42f, 0.25f, 0.18f); L.ambientEquator = new Color(0.4f, 0.19f, 0.1f); L.ambientGround = new Color(0.5f, 0.16f, 0.05f);
                    particles = new ParticleLook { color = new Color(0.2f, 0.18f, 0.17f, 0.75f), size = 1.4f, rate = 500f, fall = 3f, wind = 5f, embers = true };
                    break;
                case SurfaceKind.Desert:
                {
                    n1 = new PlanetNoise(C, 900f, s + 3, 3);
                    n2 = new PlanetNoise(C, 1400f, s + 4, 2);
                    n3 = new PlanetNoise(C, 2000f, s, 4);
                    n4 = new PlanetNoise(C, 1500f, s + 7, 4);
                    n5 = new PlanetNoise(C, 60f, s + 11, 2);
                    n6 = new PlanetNoise(C, 1200f, s + 13, 3);
                    // dune crests run one way over the whole planet; the wave repeats a whole
                    // number of times round it, so it lines up when the ship comes back round
                    float a = Rn(0f, Mathf.PI);
                    int waves = (int)Math.Round(C * Math.Cos(a) / 520.0);
                    duneKu = (float)(waves * 2.0 * Math.PI / C);
                    duneKv = Mathf.Sin(a) * 2f * Mathf.PI / 520f;
                    mesaThreshold = Rn(0.18f, 0.3f);
                    edgeLevel = 60f; poleLevel = 62f;
                    outposts = true; siteChance = 0.25f;
                    boulders = 30; boulderSize = new Vector2(5f, 22f); rockTint = new Color(0.95f, 0.62f, 0.45f);
                    description = "dune seas between flat-topped mesas";
                    L.skyTop = new Color(0.42f, 0.52f, 0.68f); L.skyBottom = new Color(0.62f, 0.47f, 0.34f); L.sunSize = 0.022f; L.sunGlow = 0.5f;
                    L.fog = new Color(0.78f, 0.62f, 0.48f); L.fogStart = 1800f; L.fogEnd = 14000f;
                    L.sunColor = new Color(1f, 0.9f, 0.76f); L.sunIntensity = 2.3f;
                    L.ambientSky = new Color(0.62f, 0.52f, 0.42f); L.ambientEquator = new Color(0.5f, 0.38f, 0.28f); L.ambientGround = new Color(0.4f, 0.28f, 0.18f);
                    particles = new ParticleLook { color = new Color(0.85f, 0.68f, 0.5f, 0.35f), size = 2.2f, rate = 260f, fall = 0.5f, wind = 14f };
                    break;
                }
                case SurfaceKind.Ocean:
                    n1 = new PlanetNoise(C, 2300f, s, 5);
                    n2 = new PlanetNoise(C, 1300f, s + 5, 5, 0.5f, 2.1f);
                    n3 = new PlanetNoise(C, 200f, s + 8, 3);
                    seaLevel = Sea;
                    edgeLevel = Sea - 60f; poleLevel = Sea + 30f;
                    waterColor = new Color(0.02f, 0.1f, 0.2f);
                    outposts = true; siteChance = 0.3f;
                    // a hostile water world is an archipelago: open sea, with the pirates' bases on
                    // their own islands (each site keeps the island raised under it)
                    if (hostile) landShift = -0.45f;
                    boulders = 25; boulderSize = new Vector2(4f, 18f); rockTint = new Color(0.62f, 0.6f, 0.56f); boulderSlope = 0.35f;
                    description = "green islands in a deep blue sea";
                    L.skyTop = new Color(0.2f, 0.4f, 0.8f); L.skyBottom = new Color(0.28f, 0.36f, 0.44f); L.sunSize = 0.022f; L.sunGlow = 0.4f;
                    L.fog = new Color(0.62f, 0.72f, 0.86f); L.fogStart = 2500f; L.fogEnd = 20000f;
                    L.sunColor = new Color(1f, 0.95f, 0.86f); L.sunIntensity = 2.1f;
                    L.ambientSky = new Color(0.45f, 0.55f, 0.72f); L.ambientEquator = new Color(0.36f, 0.42f, 0.48f); L.ambientGround = new Color(0.16f, 0.2f, 0.16f);
                    break;
                case SurfaceKind.GasGiant:
                    n1 = new PlanetNoise(C, 900f, s, 4, 0.5f, 2.07f);
                    n2 = new PlanetNoise(C, 230f, s + 6, 3, 0.5f, 2.07f);
                    n3 = new PlanetNoise(C, 2500f, s + 9, 3);
                    solid = false;
                    floorLevel = 40f;
                    edgeLevel = 250f; poleLevel = 250f;
                    outposts = true; siteChance = 0.2f; platformLift = 260f;
                    description = "the cloud tops, with gas-mining platforms riding above them";
                    L.skyTop = new Color(0.5f, 0.38f, 0.3f); L.skyBottom = new Color(0.62f, 0.44f, 0.3f); L.sunSize = 0.03f; L.sunGlow = 0.7f;
                    L.fog = new Color(0.78f, 0.6f, 0.44f); L.fogStart = 300f; L.fogEnd = 5200f;
                    L.sunColor = new Color(1f, 0.86f, 0.68f); L.sunIntensity = 1.8f;
                    L.ambientSky = new Color(0.62f, 0.5f, 0.38f); L.ambientEquator = new Color(0.55f, 0.42f, 0.3f); L.ambientGround = new Color(0.45f, 0.32f, 0.2f);
                    break;
                case SurfaceKind.IceGiant:
                    n1 = new PlanetNoise(C, 1800f, s, 5);
                    n2 = new PlanetNoise(C, 2600f, s + 3, 4, 0.5f, 2.1f, 2600f / 700f);   // long ridges running east-west
                    n3 = new PlanetNoise(C, 260f, s + 5, 2, 0.5f, 2.1f, 260f / 900f);    // crevasses running north-south
                    n4 = new PlanetNoise(C, 1100f, s + 7, 3);
                    n5 = new PlanetNoise(C, 70f, s + 8, 1);
                    edgeLevel = 110f; poleLevel = 115f;
                    outposts = true; siteChance = 0.25f;
                    boulders = 40; boulderSize = new Vector2(5f, 24f); rockTint = new Color(0.75f, 0.9f, 1f); boulderSlope = 0.6f;
                    description = "an ice sheet of pressure ridges, crevasses and ice spires under methane haze";
                    L.skyTop = new Color(0.26f, 0.46f, 0.56f); L.skyBottom = new Color(0.5f, 0.6f, 0.64f); L.sunSize = 0.014f; L.sunGlow = 0.45f;
                    L.fog = new Color(0.55f, 0.72f, 0.76f); L.fogStart = 900f; L.fogEnd = 9000f;
                    L.sunColor = new Color(0.85f, 0.95f, 1f); L.sunIntensity = 1.4f;
                    L.ambientSky = new Color(0.45f, 0.6f, 0.68f); L.ambientEquator = new Color(0.38f, 0.5f, 0.55f); L.ambientGround = new Color(0.32f, 0.4f, 0.45f);
                    particles = new ParticleLook { color = new Color(0.9f, 0.97f, 1f, 0.55f), size = 1.0f, rate = 700f, fall = 3f, wind = 9f };
                    break;
                case SurfaceKind.Moon:
                    n1 = new PlanetNoise(C, 1600f, s, 5);
                    n2 = new PlanetNoise(C, 150f, s + 4, 3);
                    n3 = new PlanetNoise(C, 3000f, s + 6, 3);
                    n4 = new PlanetNoise(C, 400f, s + 8, 2);
                    mareBias = Rn(-0.25f, 0.1f);
                    craterAmount = 1f;
                    edgeLevel = 150f; poleLevel = 150f;
                    outposts = true; siteChance = 0.2f;
                    boulders = 60; boulderSize = new Vector2(3f, 20f); rockTint = new Color(0.62f, 0.62f, 0.64f); boulderSlope = 0.7f;
                    description = "grey, airless and cratered";
                    L.space = true;
                    L.sunColor = new Color(1f, 0.97f, 0.92f); L.sunIntensity = 2.6f;
                    L.ambientSky = new Color(0.07f, 0.07f, 0.09f); L.ambientEquator = new Color(0.05f, 0.05f, 0.06f); L.ambientGround = new Color(0.03f, 0.03f, 0.03f);
                    break;
                default:   // asteroid
                    n1 = new PlanetNoise(C, 1400f, s, 4);
                    n2 = new PlanetNoise(C, 700f, s + 3, 5, 0.5f, 2.1f);
                    n3 = new PlanetNoise(C, 90f, s + 5, 3);
                    craterAmount = 0.6f;
                    edgeLevel = 220f; poleLevel = 260f;
                    siteChance = 0f;
                    boulders = 110; boulderSize = new Vector2(4f, 38f); rockTint = new Color(0.85f, 0.78f, 0.7f); boulderSlope = 0.9f;
                    floatingRocks = 4;
                    description = "a lump of dark rock, with the rest of the field drifting overhead";
                    L.space = true;
                    L.sunColor = new Color(1f, 0.96f, 0.9f); L.sunIntensity = 2.4f;
                    L.ambientSky = new Color(0.08f, 0.08f, 0.1f); L.ambientEquator = new Color(0.05f, 0.05f, 0.06f); L.ambientGround = new Color(0.03f, 0.03f, 0.03f);
                    break;
            }
            // a hostile world's bases are its own (no friendly landing pads)
            if (hostile) { outposts = false; siteChance = solid ? 0.3f : 0f; }
            n6 ??= n1; n7 ??= n1;
        }

        // ------------------------------------------------------------------ where on the planet

        public static double WrapDelta(double d, double period)
        {
            d %= period;
            if (d >= period * 0.5) d -= period;
            else if (d < -period * 0.5) d += period;
            return d;
        }

        static double Mod(double a, double m) { a %= m; return a < 0 ? a + m : a; }
        static int Mod(int a, int m) => ((a % m) + m) % m;

        /// <summary>A named base, under a direction from the body's centre (its own space). Call
        /// before the surface is built (it decides what is in the area it falls in).</summary>
        public void AddBase(string baseName, Vector3 localDir, bool baseHostile)
        {
            FromDirection(localDir, out double u, out double v);
            u = Mod(u, C);   // (u == C, straight behind, is u == 0)
            // clear of the polar plateaus, where nothing is built
            double keep = V - polarBand - 2500.0;
            if (keep > 0.0) v = Math.Max(-keep, Math.Min(keep, v));
            lock (features)
            {
                bases.Add((baseName, u, v, baseHostile));
                features.Clear();
            }
        }

        /// <summary>The named base within `reach` of an (unwrapped) position, if any: where its
        /// site is built (kept in from its area's edges, so not always exactly under its direction).</summary>
        public bool NearBase(double u, double v, double reach, out double bu, out double bv, out bool baseHostile)
        {
            Canonical(u, v, out double cu, out double cv);
            foreach (var b0 in bases)
            {
                var b = b0;
                var f = Features((int)Math.Floor(b.u / T), (int)Math.Floor((b.v + V) / T));
                if (f != null && f.baseName == b.name) { b.u = f.su; b.v = f.sv; }
                double du = WrapDelta(b.u - cu, C), dv = b.v - cv;
                if (du * du + dv * dv < reach * reach)
                {
                    // back in the caller's unwrapped coordinates
                    bu = u + du; bv = v + dv; baseHostile = b.hostile;
                    return true;
                }
            }
            bu = bv = 0.0; baseHostile = false;
            return false;
        }

        /// <summary>Fold an unwrapped position back onto the planet. True if it is past a pole
        /// (the map is turned round there).</summary>
        public bool Canonical(double u, double v, out double cu, out double cv)
        {
            double w = Mod(v + V, 4.0 * V) - V;
            if (w < V) { cu = Mod(u, C); cv = w; return false; }
            cu = Mod(C * 0.5 - u, C);
            cv = 2.0 * V - w;
            return true;
        }

        /// <summary>The area an unwrapped area index stands for, and whether it is turned round.</summary>
        public bool CanonicalTile(int I, int J, out int ci, out int cj)
        {
            int jm = Mod(J, 2 * M);
            if (jm < M) { ci = Mod(I, N); cj = jm; return false; }
            ci = Mod(N / 2 - I - 1, N);
            cj = 2 * M - 1 - jm;
            return true;
        }

        /// <summary>Latitude and longitude (degrees) of an unwrapped position.</summary>
        public void LatLon(double u, double v, out float lat, out float lon)
        {
            Canonical(u, v, out double cu, out double cv);
            lat = (float)(Math.Asin(Math.Max(-1.0, Math.Min(1.0, cv / V))) * 180.0 / Math.PI);
            lon = (float)(cu / C * 360.0 - 180.0);
        }

        /// <summary>The planet position under a direction from the body's centre (its own space).</summary>
        public void FromDirection(Vector3 d, out double u, out double v)
        {
            d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.up;
            double lon = Math.Atan2(d.z, d.x);
            u = (lon + Math.PI) / (2.0 * Math.PI) * C;
            v = Math.Max(-1.0, Math.Min(1.0, d.y)) * V;
        }

        /// <summary>The direction from the body's centre of a planet position (to read the
        /// planet's own map, so the ground matches what it looks like from orbit).</summary>
        public Vector3 Direction(double cu, double cv)
        {
            double lon = cu / C * 2.0 * Math.PI - Math.PI;
            double sl = Math.Max(-1.0, Math.Min(1.0, cv / V)), cl = Math.Sqrt(1.0 - sl * sl);
            return new Vector3((float)(cl * Math.Cos(lon)), (float)sl, (float)(cl * Math.Sin(lon)));
        }

        public float Latitude(double cv) => (float)(Math.Asin(Math.Max(-1.0, Math.Min(1.0, cv / V))) * 180.0 / Math.PI);

        /// <summary>The planet's map at a point (the same noise its texture in space is made
        /// from): lava cracks, dust basins, how much land.</summary>
        public float Region(double cu, double cv)
        {
            Vector3 d = Direction(cu, cv);
            switch (kind)
            {
                case SurfaceKind.Volcanic:
                    return Smooth(0.05f, 0.4f, Mathf.Pow(ProcNoise.Ridged(d * 4.5f + Vector3.one * 3f, 503, 5), 5f));
                case SurfaceKind.Desert:
                    return Smooth(0.62f, 0.8f, ProcNoise.Fbm(d * 1.3f + Vector3.one * 7f, 611, 3) * 0.5f + 0.5f);
                case SurfaceKind.Ocean:
                {
                    float hp = ProcNoise.Fbm(d * 2.1f, 701, 6) + 0.25f * ProcNoise.Fbm(d * 7f, 709, 4);
                    return Mathf.Clamp(hp * 2.2f, -0.4f, 0.6f);
                }
            }
            return 0f;
        }

        // ------------------------------------------------------------------ what is in each area

        readonly Dictionary<long, TileFeatures> features = new Dictionary<long, TileFeatures>();

        static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;

        /// <summary>The fixed features of an area (by its index on the planet), or null off the map.</summary>
        public TileFeatures Features(int ci, int cj)
        {
            if (cj < 0 || cj >= M) return null;
            ci = Mod(ci, N);
            long k = Key(ci, cj);
            lock (features)
            {
                if (features.TryGetValue(k, out var f)) return f;
                f = MakeFeatures(ci, cj);
                features[k] = f;
                return f;
            }
        }

        int TileSeed(int ci, int cj, int salt) { unchecked { return (int)((uint)(seed * 73856093) ^ (uint)(ci * 19349663) ^ (uint)(cj * 83492791) ^ (uint)(salt * 2654435761u)) & 0x7fffffff; } }

        TileFeatures MakeFeatures(int ci, int cj)
        {
            var f = new TileFeatures { i = ci, j = cj };
            var rng = new System.Random(TileSeed(ci, cj, 1));
            double R01() => rng.NextDouble();
            double u0 = ci * (double)T, v0 = cj * (double)T - V;
            double safe = V - polarBand - 1500.0;
            bool InPolar(double v) => Math.Abs(v) > safe;

            // a landing pad, or a hostile base
            if (siteChance > 0f && R01() < siteChance)
            {
                double su = u0 + 1100.0 + R01() * (T - 2200.0), sv = v0 + 1100.0 + R01() * (T - 2200.0);
                if (!InPolar(sv))
                {
                    f.site = hostile ? SiteKind.Base : SiteKind.Outpost;
                    f.su = su; f.sv = sv;
                    f.siteYaw = (float)(R01() * 360.0);
                }
            }
            // a named base falls in this area: it takes the place of anything else here, where
            // it is seen from orbit (kept far enough in from the area's edges for its buildings)
            foreach (var b in bases)
            {
                int bi = (int)Math.Floor(b.u / T), bj = (int)Math.Floor((b.v + V) / T);
                if (Mod(bi, N) != ci || bj != cj) continue;
                f.site = b.hostile ? SiteKind.Base : SiteKind.Outpost;
                f.baseName = b.name;
                f.su = Math.Max(u0 + 900.0, Math.Min(u0 + T - 900.0, b.u));
                f.sv = Math.Max(v0 + 900.0, Math.Min(v0 + T - 900.0, b.v));
                f.siteYaw = (float)(new System.Random(TileSeed(ci, cj, 9)).NextDouble() * 360.0);
                break;
            }

            // cinder cones
            if (kind == SurfaceKind.Volcanic)
            {
                int n = rng.Next(0, 3);
                var cu = new List<double>(); var cv = new List<double>(); var cr = new List<float>(); var ch = new List<float>();
                for (int k = 0; k < n; k++)
                {
                    double x = u0 + 700.0 + R01() * (T - 1400.0), z = v0 + 700.0 + R01() * (T - 1400.0);
                    float rad = 650f + (float)R01() * 170f, height = 380f + (float)R01() * 240f;
                    if (Math.Abs(z) > V - polarBand - 1100.0) continue;
                    if (f.site != SiteKind.None && Dist(x, z, f.su, f.sv) < rad * 1.2f + PadRadius + 400f) continue;
                    cu.Add(x); cv.Add(z); cr.Add(rad); ch.Add(height);
                }
                f.coneU = cu.ToArray(); f.coneV = cv.ToArray(); f.coneR = cr.ToArray(); f.coneH = ch.ToArray();
            }

            // the ground forces of a hostile world
            if (hostile && solid)
            {
                int id = 0;
                void Add(UnitKind kind, double u, double v, float yaw)
                {
                    if (InPolar(v)) return;
                    f.units.Add(new UnitSpot { kind = kind, u = u, v = v, yaw = yaw, id = id++ });
                }
                (double, double) Anywhere()
                {
                    for (int t = 0; t < 12; t++)
                    {
                        double x = u0 + 500.0 + R01() * (T - 1000.0), z = v0 + 500.0 + R01() * (T - 1000.0);
                        if (f.site != SiteKind.None && Dist(x, z, f.su, f.sv) < PadRadius + 250f) continue;
                        return (x, z);
                    }
                    return (u0 + T * 0.5, v0 + T * 0.5);
                }
                if (f.site == SiteKind.Base && f.baseName != null)
                {
                    // a fortress: a gunship and fighters on its pad, a ring of gun batteries and
                    // missile sites round its walls, and tanks
                    float a0 = f.siteYaw * Mathf.Deg2Rad;
                    Add(UnitKind.PadFighter, f.su + Math.Cos(a0) * 70.0, f.sv + Math.Sin(a0) * 70.0, f.siteYaw);
                    Add(UnitKind.PadFighter, f.su - Math.Cos(a0) * 70.0, f.sv - Math.Sin(a0) * 70.0, f.siteYaw);
                    Add(UnitKind.PadGunship, f.su, f.sv, f.siteYaw);
                    for (int k = 0; k < 5; k++)
                    {
                        double a = a0 + Math.PI * 0.2 + k * Math.PI * 0.4;
                        Add(UnitKind.Battery, f.su + Math.Cos(a) * 640.0, f.sv + Math.Sin(a) * 640.0, (float)(R01() * 360.0));
                    }
                    for (int k = 0; k < 2; k++)
                    {
                        double a = a0 + k * Math.PI;
                        Add(UnitKind.MissileSite, f.su + Math.Cos(a) * 760.0, f.sv + Math.Sin(a) * 760.0, (float)(R01() * 360.0));
                    }
                    for (int k = 0; k < 3; k++)
                        Add(UnitKind.Tank, f.su + Math.Cos(a0 + 2.4) * (820.0 + k * 50.0), f.sv + Math.Sin(a0 + 2.4) * (820.0 + k * 50.0), (float)(R01() * 360.0));
                }
                else if (f.site == SiteKind.Base)
                {
                    // the base: fighters parked on its launch pad (sometimes a gunship), guns round it
                    float a0 = f.siteYaw * Mathf.Deg2Rad;
                    Add(UnitKind.PadFighter, f.su + Math.Cos(a0) * 70.0, f.sv + Math.Sin(a0) * 70.0, f.siteYaw);
                    Add(UnitKind.PadFighter, f.su - Math.Cos(a0) * 70.0, f.sv - Math.Sin(a0) * 70.0, f.siteYaw);
                    if (R01() < 0.45) Add(UnitKind.PadGunship, f.su, f.sv, f.siteYaw);
                    for (int k = 0; k < 2; k++)
                    {
                        double a = a0 + Math.PI * 0.5 + k * Math.PI;
                        Add(UnitKind.Battery, f.su + Math.Cos(a) * 380.0, f.sv + Math.Sin(a) * 380.0, (float)(R01() * 360.0));
                    }
                    Add(UnitKind.MissileSite, f.su + Math.Cos(a0 + 0.8) * 520.0, f.sv + Math.Sin(a0 + 0.8) * 520.0, (float)(R01() * 360.0));
                    Add(UnitKind.Tank, f.su + Math.Cos(a0 - 1.2) * 600.0, f.sv + Math.Sin(a0 - 1.2) * 600.0, (float)(R01() * 360.0));
                }
                else if (R01() < 0.6)
                {
                    // an outlying garrison
                    int guns = rng.Next(0, 3), missiles = rng.Next(0, 2), tanks = rng.Next(0, 3);
                    for (int k = 0; k < guns; k++) { var p = Anywhere(); Add(UnitKind.Battery, p.Item1, p.Item2, (float)(R01() * 360.0)); }
                    for (int k = 0; k < missiles; k++) { var p = Anywhere(); Add(UnitKind.MissileSite, p.Item1, p.Item2, (float)(R01() * 360.0)); }
                    var tp = Anywhere();
                    for (int k = 0; k < tanks; k++) Add(UnitKind.Tank, tp.Item1 + k * 60.0, tp.Item2 + k * 45.0, (float)(R01() * 360.0));
                }
                if (R01() < 0.3)
                {
                    // fighters on patrol over the area
                    var p = Anywhere();
                    Add(UnitKind.Patrol, p.Item1, p.Item2, (float)(R01() * 360.0));
                    Add(UnitKind.Patrol, p.Item1 + 250.0, p.Item2 + 120.0, (float)(R01() * 360.0));
                }
                if (kind == SurfaceKind.Ocean)
                {
                    // the sea forces: a torpedo base on the sea floor (a few places tried: only one
                    // in deep enough water is built, see GroundForces) and submarines patrolling
                    // round it. (After the land's own draws, so the land is as it was.)
                    if (R01() < 0.7)
                        for (int k = 0; k < 3; k++) { var p = Anywhere(); Add(UnitKind.SeaBase, p.Item1, p.Item2, (float)(R01() * 360.0)); }
                    int subs = rng.Next(1, 3);
                    for (int k = 0; k < subs; k++) { var p = Anywhere(); Add(UnitKind.Submarine, p.Item1, p.Item2, (float)(R01() * 360.0)); }
                }
            }
            return f;
        }

        static float Dist(double a, double b, double c, double d) => (float)Math.Sqrt((a - c) * (a - c) + (b - d) * (b - d));

        /// <summary>The 3 x 3 areas round one (the middle one at [4]); null off the map.</summary>
        public TileFeatures[] Neighbourhood(int ci, int cj)
        {
            var ctx = new TileFeatures[9];
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                    ctx[(dj + 1) * 3 + di + 1] = Features(ci + di, cj + dj);
            return ctx;
        }

        /// <summary>Work out the flattened height of an area's pad (from the ground there).</summary>
        public void EnsureSiteHeight(TileFeatures f)
        {
            if (f == null || f.site == SiteKind.None || f.siteHeightReady) return;
            var ctx = Neighbourhood(f.i, f.j);
            float m = 0f;
            float h = Height(f.su, f.sv, Region(f.su, f.sv), ctx, false, ref m);
            switch (kind)
            {
                case SurfaceKind.Ocean: h = Mathf.Max(h, Sea + 28f); break;
                case SurfaceKind.Volcanic: h = Mathf.Max(h, Lava + 40f); break;
                case SurfaceKind.GasGiant: h = 280f; break;
            }
            f.siteHeight = h;
            f.siteHeightReady = true;
        }

        // ------------------------------------------------------------------ the ground

        public static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Height of the ground (m) at a planet position (canonical), with the extra
        /// texture hint in `m` (scorch, salt, craters' ejecta, maria). `region` is Region() there
        /// (passed in: tiles interpolate it from a coarse grid). Without `flatten` the landing pads
        /// aren't levelled (used to find out how high to level them).</summary>
        public float Height(double u, double v, float region, TileFeatures[] ctx, bool flatten, ref float m)
        {
            float h;
            m = 0f;
            switch (kind)
            {
                case SurfaceKind.Volcanic:
                {
                    h = 85f + 45f * n1.Fbm(u, v) + 14f * n2.Fbm(u, v);
                    float ch = Mathf.Pow(n3.Ridged(u + 7300.0, v), 7f);
                    h -= ch * 70f * (0.6f + 0.8f * region);
                    float rough = float.NaN;
                    foreach (var f in ctx)
                    {
                        if (f == null) continue;
                        for (int k = 0; k < f.coneU.Length; k++)
                        {
                            float du = (float)WrapDelta(u - f.coneU[k], C), dv = (float)(v - f.coneV[k]);
                            float r = Mathf.Sqrt(du * du + dv * dv) / f.coneR[k];
                            if (r >= 1.2f) continue;
                            if (float.IsNaN(rough)) rough = n4.Ridged(u, v);
                            float cone = Mathf.Pow(Mathf.Max(0f, 1f - r), 1.5f) * f.coneH[k] * (1f + 0.18f * (rough - 0.5f));
                            float crater = r < 0.16f ? (1f - r / 0.16f) : 0f;
                            h += cone - crater * crater * f.coneH[k] * 0.45f;
                        }
                    }
                    m = Mathf.Clamp01(1f - (h - Lava) / 22f);
                    break;
                }
                case SurfaceKind.Desert:
                {
                    float warp = 70f * n1.Fbm(u, v);
                    float phase = (float)(duneKu * u) + duneKv * (float)v + warp * 2f * Mathf.PI / 520f;
                    float crest = 1f - Mathf.Abs(Mathf.Sin(phase));
                    float dunes = Mathf.Pow(crest, 1.6f) * (26f + 18f * n2.Fbm(u, v));
                    float plain = 60f + 25f * n3.Fbm(u, v) + dunes * (1f - region * 0.8f);
                    float mesa = n4.Fbm(u + 3100.0, v);
                    float top = Smooth(mesaThreshold, mesaThreshold + 0.035f, mesa);
                    float tier = Smooth(mesaThreshold + 0.12f, mesaThreshold + 0.15f, mesa);
                    float rock = (top * 140f + tier * 70f) * (1f + 0.06f * n5.Fbm(u, v));
                    m = Mathf.Clamp01(region * (1f - Smooth(-0.3f, -0.1f, n6.Fbm(u, v))));   // salt pans
                    h = plain + rock;
                    break;
                }
                case SurfaceKind.Ocean:
                {
                    float c = n1.Fbm(u, v) + region + landShift;
                    // an island under each landing pad
                    foreach (var f in ctx)
                    {
                        if (f == null || f.site == SiteKind.None) continue;
                        float du = (float)WrapDelta(u - f.su, C), dv = (float)(v - f.sv);
                        float r2 = du * du + dv * dv;
                        if (r2 < 3200f * 3200f) c += 0.75f * Mathf.Exp(-r2 / (2f * 950f * 950f));
                    }
                    h = Sea + c * 220f;
                    float hills = Smooth(0.05f, 0.45f, c);
                    if (hills > 0f) h += hills * (Mathf.Pow(n2.Ridged(u, v), 1.6f) * 520f + 40f * n3.Fbm(u, v));
                    break;
                }
                case SurfaceKind.GasGiant:
                {
                    float big = n1.Billow(u, v), small = n2.Billow(u, v);
                    float band = Mathf.Sin((float)(v / 700.0) + 2f * n3.Fbm(u, v));
                    m = band * 0.5f + 0.5f;
                    h = 90f + big * 230f + small * 60f;
                    break;
                }
                case SurfaceKind.IceGiant:
                {
                    h = 110f + 55f * n1.Fbm(u, v);
                    h += Mathf.Pow(n2.Ridged(u, v), 3f) * 330f;
                    float crev = Mathf.Pow(n3.Ridged(u, v + 4400.0), 14f);
                    h -= crev * 45f;
                    float field = Smooth(0.15f, 0.4f, n4.Fbm(u + 9000.0, v));
                    if (field > 0f)
                    {
                        float spire = Mathf.Max(0f, n5.Value01(u, v) - 0.72f) / 0.28f;
                        h += field * spire * spire * 110f;
                    }
                    m = crev;
                    break;
                }
                case SurfaceKind.Moon:
                {
                    h = 160f + 70f * n1.Fbm(u, v) + 12f * n2.Fbm(u, v);
                    float mare = Smooth(0.05f, 0.25f, -n3.Fbm(u, v) + mareBias);
                    h = Mathf.Lerp(h, 120f + 6f * n4.Fbm(u, v), mare * 0.85f);
                    m = -mare;
                    break;
                }
                default:
                {
                    float lumps = n1.Fbm(u, v) * 180f;
                    float ridges = Mathf.Pow(n2.Ridged(u, v), 2f) * 160f;
                    h = 220f + lumps + ridges + 10f * n3.Fbm(u, v);
                    break;
                }
            }
            if (craterAmount > 0f) Craters(u, v, ref h, ref m);

            if (flatten)
            {
                // level the ground for each landing pad, blending out over the next 220 m
                foreach (var f in ctx)
                {
                    if (f == null || f.site == SiteKind.None || !f.siteHeightReady) continue;
                    float du = (float)WrapDelta(u - f.su, C), dv = (float)(v - f.sv);
                    float r2 = du * du + dv * dv;
                    const float reach = PadRadius + 220f;
                    if (r2 >= reach * reach) continue;
                    float flat = 1f - Smooth(PadRadius, reach, Mathf.Sqrt(r2));
                    h = Mathf.Lerp(h, f.siteHeight, flat);
                    if (m > 0f) m *= 1f - flat;
                }
            }

            // the polar plateaus, where the two sides of the map meet
            float pole = Smooth((float)V - polarBand, (float)V, (float)Math.Abs(v));
            if (pole > 0f) { h = Mathf.Lerp(h, poleLevel, pole); m *= 1f - pole; }
            return h;
        }

        /// <summary>Bowl craters with raised rims, at three sizes, each in its own grid of cells
        /// (cells wrap round the planet like everything else).</summary>
        void Craters(double u, double v, ref float h, ref float m)
        {
            for (int s = 0; s < 3; s++)
            {
                float cell = s == 0 ? T * 0.5f : s == 1 ? T * 0.25f : T * 0.125f;
                float chance = (s == 0 ? 0.45f : s == 1 ? 0.55f : 0.6f) * craterAmount;
                float rMin = s == 0 ? 180f : s == 1 ? 60f : 20f, rMax = s == 0 ? 650f : s == 1 ? 220f : 75f;
                int cells = (int)Math.Round(C / cell);
                int ci = (int)Math.Floor(u / cell), cj = (int)Math.Floor((v + V) / cell);
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int x = Mod(ci + di, cells), y = cj + dj;
                        if (ProcNoise.Hash(x, y, s, seed + 4001) > chance) continue;
                        double cx = (x + 0.15 + 0.7 * ProcNoise.Hash(x, y, s + 10, seed + 4002)) * cell;
                        double cy = (y + 0.15 + 0.7 * ProcNoise.Hash(x, y, s + 20, seed + 4003)) * cell - V;
                        float rad = Mathf.Lerp(rMin, rMax, Mathf.Pow(ProcNoise.Hash(x, y, s + 30, seed + 4004), 2f));
                        float du = (float)WrapDelta(u - cx, C), dv = (float)(v - cy);
                        float r = Mathf.Sqrt(du * du + dv * dv) / rad;
                        if (r > 2.2f) continue;
                        float depth = rad * (0.16f + 0.08f * ProcNoise.Hash(x, y, s + 40, seed + 4005)), rim = rad * 0.06f;
                        h += r < 1f ? -depth * (1f - r * r) + rim * r * r * r : rim * Mathf.Exp(-(r - 1f) * 3.2f);
                        if (r > 0.85f && r < 1.9f) m = Mathf.Max(m, (1f - Mathf.Abs(r - 1.2f) / 0.7f) * Mathf.Clamp01(rad / 200f));
                    }
            }
        }

        /// <summary>How much of each of the four ground textures at a point.</summary>
        public void SplatWeights(float ht, float slope, float m, double u, double v, float lat, float[] w)
        {
            float nz = nSplat.Fbm(u, v) * 0.5f + 0.5f;
            for (int i = 0; i < w.Length; i++) w[i] = 0f;
            float steep = Smooth(0.35f, 0.7f, slope);
            switch (kind)
            {
                case SurfaceKind.Volcanic:
                    w[0] = (1f - steep) * (0.4f + nz);
                    w[1] = (1f - steep) * Smooth(120f, 220f, ht) * 1.6f;     // ash on the high slopes
                    w[2] = steep * 1.5f;
                    w[3] = m * 2.5f;
                    break;
                case SurfaceKind.Desert:
                    w[0] = (1f - steep) * (0.5f + nz);
                    w[1] = (1f - steep) * (1.2f - nz) * 0.8f;
                    w[2] = steep * 2f + Smooth(150f, 190f, ht) * 0.6f;
                    w[3] = m * 2.5f * (1f - steep);
                    break;
                case SurfaceKind.Ocean:
                {
                    float polar = Smooth(48f, 70f, Mathf.Abs(lat));
                    float snowLine = Mathf.Lerp(560f, 210f, polar);
                    float above = ht - Sea;
                    float beach = 1f - Smooth(4f, 14f, above);
                    float snow = Smooth(snowLine - 40f, snowLine + 40f, ht + nz * 60f);
                    w[1] = beach * 3f;
                    w[0] = (1f - beach) * (1f - steep) * (1f - snow);
                    w[2] = steep * 1.8f * (1f - beach) + Smooth(380f, 520f, ht) * 0.4f;
                    w[3] = snow * (1f - steep * 0.7f) * 2f;
                    break;
                }
                case SurfaceKind.GasGiant:
                    w[0] = 0.6f + nz * 0.4f;
                    w[1] = m * 1.4f;
                    w[2] = (1f - Smooth(110f, 190f, ht)) * 0.8f;
                    w[3] = Smooth(280f, 360f, ht) * 1.5f;
                    break;
                case SurfaceKind.IceGiant:
                    w[0] = (1f - steep) * (0.6f + nz);
                    w[1] = steep * 1.6f + Smooth(260f, 380f, ht) * 0.5f;
                    w[2] = Mathf.Clamp01(m * 3f) * 2.5f;
                    w[3] = (1f - steep) * (1f - nz) * 0.6f;
                    break;
                case SurfaceKind.Moon:
                    w[0] = (1f - steep) * (0.6f + nz * 0.5f);
                    w[1] = Mathf.Clamp01(-m) * 2.2f;
                    w[2] = Mathf.Clamp01(m) * 2.4f;
                    w[3] = steep * 1.6f;
                    break;
                default:
                    w[0] = (1f - steep) * (0.6f + nz);
                    w[1] = steep * 1.8f;
                    w[2] = Mathf.Clamp01(m) * 2f;
                    w[3] = (1f - steep) * (1f - nz) * 0.7f;
                    break;
            }
            float sum = 0f;
            for (int i = 0; i < w.Length; i++) sum += w[i];
            if (sum < 1e-4f) { w[0] = 1f; sum = 1f; }
            for (int i = 0; i < w.Length; i++) w[i] /= sum;
        }

        /// <summary>Where the sun is for a landing in one area (the time of day differs from place to place).</summary>
        public void SetSun(int area)
        {
            var rng = new System.Random(seed * 31 + area);
            float lo, hi;
            switch (kind)
            {
                case SurfaceKind.Volcanic: lo = 25f; hi = 55f; break;
                case SurfaceKind.Desert: lo = 30f; hi = 65f; break;
                case SurfaceKind.Ocean: lo = 25f; hi = 60f; break;
                case SurfaceKind.GasGiant: lo = 20f; hi = 45f; break;
                case SurfaceKind.IceGiant: lo = 12f; hi = 35f; break;
                case SurfaceKind.Moon: lo = 10f; hi = 40f; break;
                default: lo = 15f; hi = 60f; break;
            }
            look.sunAzimuth = (float)rng.NextDouble() * 360f;
            look.sunElevation = lo + (float)rng.NextDouble() * (hi - lo);
        }

        /// <summary>Say what the ground is like where the ship comes down (latitude matters on Thalassa).</summary>
        public string DescribeAt(float lat)
        {
            if (kind == SurfaceKind.Ocean && Mathf.Abs(lat) > 59f) return "cold islands in a grey polar sea";
            return description;
        }
    }

    /// <summary>
    /// Works out one area's heights and textures (pure arithmetic: runs on a worker thread, or a
    /// few rows per frame where there are no threads, in a web page). The result is applied to a
    /// Terrain on the main thread.
    /// </summary>
    public sealed class TileJob
    {
        public readonly SurfaceWorld world;
        public readonly int I, J, lod, res, splatRes;
        public float[,] norm;            // [z, x], 0..1 over the shared height range
        public float[,] heights;         // [z, x], metres
        public float[,,] splat;

        /// <summary>The big arrays a job works in, kept for the next job rather than thrown
        /// away (a full-detail area needs about 9 MB of them; in a web page, where memory is
        /// never moved, making and dropping that many large arrays fragments the heap).</summary>
        sealed class Buffers
        {
            public float[,] hb, mb, heights, norm;
            public float[,,] splat;
        }
        static readonly Stack<Buffers>[] Pool = { new Stack<Buffers>(), new Stack<Buffers>() };
        Buffers buffers;

        Buffers Rent()
        {
            lock (Pool)
            {
                if (Pool[lod].Count > 0) return Pool[lod].Pop();
            }
            int n = res + 2;
            return new Buffers
            {
                hb = new float[n, n], mb = new float[n, n], heights = new float[res, res], norm = new float[res, res],
                splat = new float[splatRes, splatRes, 4],
            };
        }

        /// <summary>Done with the results (put in place, or not wanted): the arrays go back for
        /// the next job. Only once the job has finished.</summary>
        /// <summary>Let go of the spare buffers (when there is no surface any more).</summary>
        public static void TrimPool()
        {
            lock (Pool) { Pool[0].Clear(); Pool[1].Clear(); }
        }

        /// <summary>Give the buffers back to the pool (once it has stopped). Safe to call twice,
        /// from the worker and the main thread at once.</summary>
        public void Release()
        {
            if (!done) return;
            lock (Pool)
            {
                if (buffers == null) return;
                if (Pool[lod].Count < 8) Pool[lod].Push(buffers);
                buffers = null;
            }
            heights = norm = null;
            splat = null;
        }
        public float seconds;
        public float maxHeight;   // the highest ground in it
        public volatile bool done;
        public volatile bool cancelled;
        public Exception error;
        public bool flipped;
        public int ci, cj;
        public TileFeatures[] ctx;

        public TileJob(SurfaceWorld world, int I, int J, int lod)
        {
            this.world = world;
            this.I = I;
            this.J = J;
            this.lod = lod;
            res = lod == 0 ? 513 : 129;
            splatRes = lod == 0 ? 512 : 128;
        }

        /// <summary>The whole job at once (on a worker thread).</summary>
        public void RunAll()
        {
            var it = Steps();
            try { while (it.MoveNext()) { if (cancelled) break; } }
            catch (Exception e) { error = e; }
            done = true;
            // nobody waits for a cancelled job: it gives its buffers back itself
            if (cancelled) Release();
        }

        /// <summary>The job in pieces (a few rows each).</summary>
        public IEnumerator<int> Steps()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var w = world;
            float T = SurfaceWorld.T;
            flipped = w.CanonicalTile(I, J, out ci, out cj);
            ctx = w.Neighbourhood(ci, cj);
            foreach (var f in ctx) w.EnsureSiteHeight(f);
            yield return 0;

            double u0 = I * (double)T, v0 = J * (double)T - w.V;
            // the planet's own map, on a coarse grid (it changes slowly)
            const int G = 17;   // the same for near and far areas, so their edges agree
            var region = new float[G, G];
            float gstep = T / (G - 1);
            for (int gz = 0; gz < G; gz++)
                for (int gx = 0; gx < G; gx++)
                {
                    w.Canonical(u0 + gx * gstep, v0 + gz * gstep, out double cu, out double cv);
                    region[gz, gx] = w.Region(cu, cv);
                }
            yield return 0;

            // heights, with a one-sample border all round (for slopes at the edges)
            int n = res + 2;
            float step = T / (res - 1);
            buffers = Rent();
            var hb = buffers.hb;
            var mb = buffers.mb;
            for (int zi = 0; zi < n; zi++)
            {
                float lz = (zi - 1) * step;
                for (int xi = 0; xi < n; xi++)
                {
                    float lx = (xi - 1) * step;
                    w.Canonical(u0 + lx, v0 + lz, out double cu, out double cv);
                    float gx = Mathf.Clamp(lx / gstep, 0f, G - 1f), gz = Mathf.Clamp(lz / gstep, 0f, G - 1f);
                    int ix = Mathf.Min((int)gx, G - 2), iz = Mathf.Min((int)gz, G - 2);
                    float tx = gx - ix, tz = gz - iz;
                    float reg = Mathf.Lerp(Mathf.Lerp(region[iz, ix], region[iz, ix + 1], tx), Mathf.Lerp(region[iz + 1, ix], region[iz + 1, ix + 1], tx), tz);
                    float m = 0f;
                    hb[zi, xi] = w.Height(cu, cv, reg, ctx, true, ref m);
                    mb[zi, xi] = m;
                }
                if ((zi & 15) == 15) { if (cancelled) yield break; yield return zi; }
            }

            heights = buffers.heights;
            norm = buffers.norm;
            float top = float.MinValue;
            for (int zi = 0; zi < res; zi++)
                for (int xi = 0; xi < res; xi++)
                {
                    float h = hb[zi + 1, xi + 1];
                    if (h > top) top = h;
                    heights[zi, xi] = h;
                    norm[zi, xi] = Mathf.Clamp01((h - SurfaceWorld.BaseLevel) / SurfaceWorld.Range);
                }
            yield return 0;

            // textures
            splat = buffers.splat;
            float cell = T / splatRes;
            var wt = new float[4];
            for (int zi = 0; zi < splatRes; zi++)
            {
                for (int xi = 0; xi < splatRes; xi++)
                {
                    float lx = (xi + 0.5f) * cell, lz = (zi + 0.5f) * cell;
                    float fx = lx / step + 1f, fz = lz / step + 1f;
                    int ix = Mathf.Min((int)fx, n - 2), iz = Mathf.Min((int)fz, n - 2);
                    float tx = fx - ix, tz = fz - iz;
                    float ht = Mathf.Lerp(Mathf.Lerp(hb[iz, ix], hb[iz, ix + 1], tx), Mathf.Lerp(hb[iz + 1, ix], hb[iz + 1, ix + 1], tx), tz);
                    float dx = hb[iz, Mathf.Min(ix + 1, n - 1)] - hb[iz, Mathf.Max(ix - 1, 0)];
                    float dz = hb[Mathf.Min(iz + 1, n - 1), ix] - hb[Mathf.Max(iz - 1, 0), ix];
                    float slope = Mathf.Sqrt(dx * dx + dz * dz) / (2f * step);
                    float m = mb[Mathf.Clamp(Mathf.RoundToInt(fz), 0, n - 1), Mathf.Clamp(Mathf.RoundToInt(fx), 0, n - 1)];
                    w.Canonical(u0 + lx, v0 + lz, out double cu, out double cv);
                    w.SplatWeights(ht, slope, m, cu, cv, w.Latitude(cv), wt);
                    for (int k = 0; k < 4; k++) splat[zi, xi, k] = wt[k];
                }
                if ((zi & 31) == 31) { if (cancelled) yield break; yield return zi; }
            }
            maxHeight = top;
            seconds = (float)sw.Elapsed.TotalSeconds;
        }
    }
}

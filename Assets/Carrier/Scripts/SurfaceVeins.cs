using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>A vein of minerals in a planet's (or moon's) surface: one to three in each 5 km
    /// area, always in the same places. What it is made of depends on the world (sulfides on a
    /// volcanic world, salts and ores in a desert, heavy-water brine under a sea, gas pockets in
    /// a gas giant's cloud tops).</summary>
    public sealed class SurfaceVein : IMineralSource
    {
        public readonly SurfaceWorld world;
        public readonly int area, index;
        readonly Share[] mix;
        readonly string label;
        readonly float initial;
        float taken;

        // where it is: in the copy of its area nearest the ship (an area can be built twice, over a pole)
        internal PlanetSurface surface;
        internal double u, v;

        internal SurfaceVein(SurfaceWorld world, int area, int index, double cu, double cv, int seed)
        {
            this.world = world;
            this.area = area;
            this.index = index;
            bool wet = world.kind == SurfaceKind.Ocean && world.Region(cu, cv) < 0.13f;
            mix = Minerals.ForSurface(world.kind, seed, wet, out label);
            var rng = new System.Random(seed);
            initial = Mathf.Round(150f + (float)rng.NextDouble() * 450f);
        }

        public string Label => label;
        public Share[] Mix => mix;
        public float Initial => initial;
        public float Remaining => Mathf.Max(0f, initial - taken);
        public bool Depleted => Remaining < 0.05f;
        public float Radius => 0f;
        public bool Discovered { get; set; }
        public bool Valid => surface != null && surface.World != null && surface.World.name == world.name;

        /// <summary>The point on the ground (or sea, or cloud deck) over it.</summary>
        public Vector3 Position
        {
            get
            {
                if (surface == null) return Vector3.zero;
                Vector3 p = surface.WorldOf(u, v, 0f);
                p.y = surface.FloorAt(p);
                return p;
            }
        }

        public float Take(float tonnes)
        {
            float t = Mathf.Min(Mathf.Max(0f, tonnes), Remaining);
            taken += t;
            return t;
        }
    }

    /// <summary>Finds the veins round a point on a planet's surface. What has been mined out of
    /// each is remembered until the level starts again.</summary>
    public static class SurfaceVeins
    {
        static readonly Dictionary<string, SurfaceVein> Known = new Dictionary<string, SurfaceVein>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Known.Clear();

        /// <summary>Forget every vein (a new game: they are full again).</summary>
        public static void Forget() => Known.Clear();

        static int Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)(a * 374761393 + b * 668265263 + c * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (int)((h ^ (h >> 16)) & 0x7fffffff);
            }
        }

        static float Frac(int h, int salt) => ((Hash(h, salt, 91) & 0xFFFF) / 65535f);

        /// <summary>The veins whose ground point is within `range` (across the ground) of `p`.</summary>
        public static void Near(PlanetSurface s, Vector3 p, float range, List<SurfaceVein> into)
        {
            into.Clear();
            if (s == null || s.World == null) return;
            var w = s.World;
            const float T = SurfaceWorld.T;
            s.TileOf(p, out int I0, out int J0);
            int reach = Mathf.CeilToInt(range / T);
            for (int I = I0 - reach; I <= I0 + reach; I++)
                for (int J = J0 - reach; J <= J0 + reach; J++)
                {
                    bool flipped = w.CanonicalTile(I, J, out int ci, out int cj);
                    int area = ci * w.M + cj;
                    int h = Hash(w.seed, area, 17);
                    int count = 1 + h % 3;
                    for (int k = 0; k < count; k++)
                    {
                        int hk = Hash(h, k, 29);
                        float fx = 0.12f + 0.76f * Frac(hk, 1), fz = 0.12f + 0.76f * Frac(hk, 2);
                        if (flipped) { fx = 1f - fx; fz = 1f - fz; }   // the area is turned round past a pole
                        double u = (I + fx) * (double)T, v = (J + fz) * (double)T - w.V;
                        Vector3 at = s.WorldOf(u, v, 0f);
                        float dx = at.x - p.x, dz = at.z - p.z;
                        if (dx * dx + dz * dz > range * range) continue;
                        string key = $"{w.name}|{area}|{k}";
                        if (!Known.TryGetValue(key, out var vein))
                        {
                            w.Canonical(u, v, out double cu, out double cv);
                            vein = new SurfaceVein(w, area, k, cu, cv, hk);
                            Known[key] = vein;
                        }
                        vein.surface = s;
                        vein.u = u;
                        vein.v = v;
                        if (!into.Contains(vein)) into.Add(vein);
                    }
                }
        }
    }
}

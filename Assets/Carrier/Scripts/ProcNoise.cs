using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>Deterministic value noise (3D for shapes, tileable 2D for textures).</summary>
    public static class ProcNoise
    {
        public static float Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + seed * 974634397);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        static float S(float t) => t * t * (3f - 2f * t);

        /// <summary>Smooth value noise in 0..1.</summary>
        public static float Value(Vector3 p, int seed)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = S(p.x - x), fy = S(p.y - y), fz = S(p.z - z);
            float c000 = Hash(x, y, z, seed), c100 = Hash(x + 1, y, z, seed);
            float c010 = Hash(x, y + 1, z, seed), c110 = Hash(x + 1, y + 1, z, seed);
            float c001 = Hash(x, y, z + 1, seed), c101 = Hash(x + 1, y, z + 1, seed);
            float c011 = Hash(x, y + 1, z + 1, seed), c111 = Hash(x + 1, y + 1, z + 1, seed);
            float a = Mathf.Lerp(Mathf.Lerp(c000, c100, fx), Mathf.Lerp(c010, c110, fx), fy);
            float b = Mathf.Lerp(Mathf.Lerp(c001, c101, fx), Mathf.Lerp(c011, c111, fx), fy);
            return Mathf.Lerp(a, b, fz);
        }

        /// <summary>Fractal noise, roughly -1..1.</summary>
        public static float Fbm(Vector3 p, int seed, int octaves, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += (Value(p, seed + i * 31) * 2f - 1f) * amp;
                norm += amp;
                amp *= gain;
                p *= 2.03f;
            }
            return sum / norm;
        }

        /// <summary>Ridged fractal noise, 0..1 (sharp crests).</summary>
        public static float Ridged(Vector3 p, int seed, int octaves)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Value(p, seed + i * 57) * 2f - 1f);
                sum += n * n * amp;
                norm += amp;
                amp *= 0.5f;
                p *= 2.1f;
            }
            return sum / norm;
        }

        /// <summary>Tileable 2D value noise in 0..1 with the given period (in cells).</summary>
        public static float Tile(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = S(x - xi), fy = S(y - yi);
            int x0 = Mod(xi, period), x1 = Mod(xi + 1, period), y0 = Mod(yi, period), y1 = Mod(yi + 1, period);
            float a = Mathf.Lerp(Hash(x0, y0, 0, seed), Hash(x1, y0, 0, seed), fx);
            float b = Mathf.Lerp(Hash(x0, y1, 0, seed), Hash(x1, y1, 0, seed), fx);
            return Mathf.Lerp(a, b, fy);
        }

        /// <summary>Tileable fractal noise over the unit square, roughly -1..1.</summary>
        public static float TileFbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int period = basePeriod;
            for (int i = 0; i < octaves; i++)
            {
                sum += (Tile(u * period, v * period, period, seed + i * 13) * 2f - 1f) * amp;
                norm += amp;
                amp *= 0.5f;
                period *= 2;
            }
            return sum / norm;
        }

        static int Mod(int a, int m) => ((a % m) + m) % m;
    }
}

using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The colours of the planets' and moons' maps as a function of where on the body (a direction
    /// from its centre, its own space). The scene builder bakes these into the textures the bodies
    /// wear from afar; the terrain seen close to (PlanetTerrain) colours its points from the very
    /// same functions, at whatever resolution it is drawn, so near and far agree. Thread-safe.
    /// </summary>
    public static class PlanetMaps
    {
        static float Edge(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        static Color Ramp(float t, params Color[] cs)
        {
            t = Mathf.Clamp01(t) * (cs.Length - 1);
            int i = Mathf.Min(cs.Length - 2, (int)t);
            return Color.Lerp(cs[i], cs[i + 1], t - i);
        }

        /// <summary>Latitude, -1 (south pole) to 1 (north pole), as the texture's rows run.</summary>
        static float Lat(Vector3 d) => Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) / (Mathf.PI / 2f);

        public static (Color albedo, Color glow) Volcanic(Vector3 d)
        {
            float rock = ProcNoise.Fbm(d * 3f, 501, 5) * 0.5f + 0.5f;
            float crack = Mathf.Pow(ProcNoise.Ridged(d * 4.5f + Vector3.one * 3f, 503, 5), 5f);
            float lava = Edge(0.12f, 0.4f, crack);
            var basalt = Color.Lerp(new Color(0.06f, 0.05f, 0.05f), new Color(0.2f, 0.16f, 0.14f), rock);
            var albedo = Color.Lerp(basalt, new Color(0.45f, 0.12f, 0.03f), lava);
            var glow = Color.Lerp(Color.black, new Color(1f, 0.42f, 0.08f), lava) * (0.6f + 0.4f * rock);
            return (albedo, glow);
        }

        public static Color Desert(Vector3 d)
        {
            float lat = Lat(d);
            float n = ProcNoise.Fbm(d * 2.4f, 601, 6) * 0.5f + 0.5f;
            float dunes = Mathf.Sin(lat * 60f + ProcNoise.Fbm(d * 6f, 607, 3) * 8f) * 0.5f + 0.5f;
            var c = Ramp(n, new Color(0.38f, 0.25f, 0.17f), new Color(0.72f, 0.42f, 0.22f), new Color(0.86f, 0.66f, 0.42f), new Color(0.92f, 0.8f, 0.6f));
            c *= 0.92f + 0.1f * dunes;
            float basin = Edge(0.62f, 0.8f, ProcNoise.Fbm(d * 1.3f + Vector3.one * 7f, 611, 3) * 0.5f + 0.5f);
            return Color.Lerp(c, new Color(0.5f, 0.3f, 0.2f), basin * 0.5f);
        }

        /// <summary>An ocean world: sea, land, ice and (`clouds`: from afar) its clouds.</summary>
        public static Color Ocean(Vector3 d, bool clouds)
        {
            float lat = Lat(d);
            float h = ProcNoise.Fbm(d * 2.1f, 701, 6) + 0.25f * ProcNoise.Fbm(d * 7f, 709, 4);
            Color c;
            if (h < 0.06f)
                c = Color.Lerp(new Color(0.01f, 0.035f, 0.12f), new Color(0.03f, 0.14f, 0.28f), Mathf.InverseLerp(-0.35f, 0.06f, h));
            else
                c = Ramp(Mathf.InverseLerp(0.06f, 0.5f, h), new Color(0.55f, 0.5f, 0.34f), new Color(0.11f, 0.25f, 0.08f),
                         new Color(0.2f, 0.26f, 0.1f), new Color(0.33f, 0.26f, 0.17f), new Color(0.5f, 0.5f, 0.5f));
            float ice = Edge(0.72f, 0.82f, Mathf.Abs(lat) + 0.06f * ProcNoise.Fbm(d * 5f, 713, 3));
            c = Color.Lerp(c, new Color(0.92f, 0.95f, 0.98f), ice);
            if (clouds)
            {
                float cloud = Edge(0.18f, 0.6f, ProcNoise.Fbm(d * 3.2f + Vector3.one * 11f, 719, 5)) * 0.65f;
                c = Color.Lerp(c, new Color(0.95f, 0.96f, 0.98f), cloud);
            }
            return c;
        }

        public static Color IceGiant(Vector3 d)
        {
            float lat = Lat(d);
            float warp = ProcNoise.Fbm(d * 2.5f, 801, 4);
            float band = Mathf.Sin(lat * 9f + warp * 1.6f) * 0.35f + Mathf.Sin(lat * 23f + warp * 3f) * 0.15f + 0.5f;
            var c = Ramp(band, new Color(0.18f, 0.42f, 0.55f), new Color(0.36f, 0.66f, 0.74f), new Color(0.58f, 0.82f, 0.86f), new Color(0.28f, 0.52f, 0.66f));
            return Color.Lerp(c, new Color(0.85f, 0.95f, 0.97f), Edge(0.8f, 1f, Mathf.Abs(lat)) * 0.5f);
        }

        /// <summary>A grey moon: dust and rock, darker plains (maria) and bright rays. `seed` per moon.</summary>
        public static Color Moon(Vector3 d, int seed)
        {
            float n = ProcNoise.Fbm(d * 3f, seed, 6) * 0.5f + 0.5f;
            float mare = Edge(0.05f, 0.25f, -ProcNoise.Fbm(d * 1.4f, seed + 7, 4));
            float fine = ProcNoise.Fbm(d * 18f, seed + 13, 3) * 0.5f + 0.5f;
            var c = Ramp(n, new Color(0.30f, 0.29f, 0.28f), new Color(0.46f, 0.45f, 0.43f), new Color(0.58f, 0.57f, 0.55f));
            c = Color.Lerp(c, new Color(0.17f, 0.17f, 0.18f), mare * 0.8f);
            return c * (0.9f + 0.2f * fine);
        }

        /// <summary>The colour of the ground (no clouds) of a body of `kind` at a direction.</summary>
        public static Color Ground(SurfaceKind kind, Vector3 d, int seed = 901)
        {
            switch (kind)
            {
                case SurfaceKind.Volcanic: return Volcanic(d).albedo;
                case SurfaceKind.Desert: return Desert(d);
                case SurfaceKind.Ocean: return Ocean(d, false);
                case SurfaceKind.IceGiant: return IceGiant(d);
                case SurfaceKind.Moon: return Moon(d, seed);
                default: return new Color(0.5f, 0.48f, 0.45f);
            }
        }

        /// <summary>The surface's own hint (SurfaceWorld.Height's `m`) laid on a colour: lava and
        /// scorch on a volcanic world, salt pans in a desert, crevasses in ice, the maria on a moon.</summary>
        public static Color Hint(SurfaceKind kind, Color c, float m)
        {
            switch (kind)
            {
                case SurfaceKind.Volcanic: return Color.Lerp(c, new Color(0.5f, 0.13f, 0.03f), Mathf.Clamp01(m) * 0.8f);
                case SurfaceKind.Desert: return Color.Lerp(c, new Color(0.88f, 0.86f, 0.8f), Mathf.Clamp01(m) * 0.6f);
                case SurfaceKind.IceGiant: return Color.Lerp(c, new Color(0.1f, 0.25f, 0.35f), Mathf.Clamp01(m) * 0.7f);
                case SurfaceKind.Moon: return Color.Lerp(c, new Color(0.17f, 0.17f, 0.18f), Mathf.Clamp01(-m) * 0.4f);
                default: return c;
            }
        }
    }
}

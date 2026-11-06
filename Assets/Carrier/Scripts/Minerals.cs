using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>Everything the frigate can mine: metals, rock, ices and gases.</summary>
    public enum Mineral
    {
        Iron, Nickel, Cobalt, Copper, Titanium, Aluminium, Magnesium, Silicates, Carbon, Sulfur,
        Water, Methane, Ammonia, CarbonDioxide, Hydrogen, Helium, Helium3, Deuterium, Lithium,
        RareEarths, Uranium, Silver, Gold, Palladium, Platinum, Iridium, Diamonds,
    }

    /// <summary>What a body is, for what it is made of.</summary>
    public enum BodyClass { Asteroid, Moon, Comet, CometDebris }

    /// <summary>One mineral's share of a deposit (0..1).</summary>
    public struct Share
    {
        public Mineral mineral;
        public float fraction;
        public Share(Mineral m, float f) { mineral = m; fraction = f; }
    }

    /// <summary>The minerals: names, colours and prices, and what each kind of body is made of.</summary>
    public static class Minerals
    {
        public struct Info
        {
            public string name, symbol;
            public Color color;
            public float value;   // credits per tonne
            public Info(string name, string symbol, Color color, float value) { this.name = name; this.symbol = symbol; this.color = color; this.value = value; }
        }

        static readonly Info[] Table =
        {
            new Info("Iron", "Fe", new Color(0.78f, 0.55f, 0.45f), 12f),
            new Info("Nickel", "Ni", new Color(0.75f, 0.78f, 0.7f), 35f),
            new Info("Cobalt", "Co", new Color(0.45f, 0.6f, 1f), 80f),
            new Info("Copper", "Cu", new Color(0.95f, 0.55f, 0.3f), 40f),
            new Info("Titanium", "Ti", new Color(0.8f, 0.82f, 0.88f), 60f),
            new Info("Aluminium", "Al", new Color(0.85f, 0.87f, 0.9f), 18f),
            new Info("Magnesium", "Mg", new Color(0.82f, 0.85f, 0.78f), 15f),
            new Info("Silicates", "Si", new Color(0.66f, 0.62f, 0.56f), 2f),
            new Info("Carbon", "C", new Color(0.5f, 0.5f, 0.52f), 10f),
            new Info("Sulfur", "S", new Color(1f, 0.9f, 0.3f), 6f),
            new Info("Water", "H2O", new Color(0.6f, 0.85f, 1f), 8f),
            new Info("Methane", "CH4", new Color(0.55f, 0.95f, 0.85f), 9f),
            new Info("Ammonia", "NH3", new Color(0.8f, 0.75f, 1f), 12f),
            new Info("Carbon dioxide", "CO2", new Color(0.85f, 0.9f, 0.95f), 4f),
            new Info("Hydrogen", "H2", new Color(0.7f, 0.8f, 1f), 10f),
            new Info("Helium", "He", new Color(1f, 0.85f, 0.7f), 20f),
            new Info("Helium-3", "He3", new Color(1f, 0.6f, 0.95f), 8000f),
            new Info("Deuterium", "D", new Color(0.45f, 0.75f, 1f), 300f),
            new Info("Lithium", "Li", new Color(1f, 0.55f, 0.6f), 90f),
            new Info("Rare earths", "REE", new Color(0.7f, 1f, 0.6f), 250f),
            new Info("Uranium", "U", new Color(0.6f, 1f, 0.35f), 400f),
            new Info("Silver", "Ag", new Color(0.9f, 0.92f, 0.95f), 900f),
            new Info("Gold", "Au", new Color(1f, 0.82f, 0.25f), 6000f),
            new Info("Palladium", "Pd", new Color(0.88f, 0.9f, 0.98f), 9000f),
            new Info("Platinum", "Pt", new Color(0.92f, 0.95f, 1f), 12000f),
            new Info("Iridium", "Ir", new Color(0.85f, 0.95f, 1f), 15000f),
            new Info("Diamonds", "Dia", new Color(0.85f, 1f, 1f), 20000f),
        };

        public static int Count => Table.Length;
        public static Info Get(Mineral m) => Table[(int)m];
        public static string Name(Mineral m) => Table[(int)m].name;
        public static string Symbol(Mineral m) => Table[(int)m].symbol;
        public static float Value(Mineral m) => Table[(int)m].value;
        public static string Hex(Mineral m) => "#" + ColorUtility.ToHtmlStringRGB(Table[(int)m].color);

        /// <summary>The most valuable mineral making up at least `minFraction` of a mix.</summary>
        public static Mineral Prize(Share[] mix, float minFraction = 0.003f)
        {
            Mineral best = mix.Length > 0 ? mix[0].mineral : Mineral.Silicates;
            float bestValue = -1f;
            foreach (var s in mix)
                if (s.fraction >= minFraction && Value(s.mineral) > bestValue) { bestValue = Value(s.mineral); best = s.mineral; }
            return best;
        }

        /// <summary>Credits per tonne of a mix as a whole.</summary>
        public static float ValuePerTonne(Share[] mix)
        {
            float v = 0f;
            foreach (var s in mix) v += s.fraction * Value(s.mineral);
            return v;
        }

        /// <summary>"Fe 41%  Ni 22%  Pt 0.9%": the biggest shares, then any precious ones.</summary>
        public static string Summary(Share[] mix, int count = 3, bool colour = true)
        {
            var sb = new StringBuilder();
            var shown = new HashSet<Mineral>();
            void Add(Share s)
            {
                if (!shown.Add(s.mineral)) return;
                if (sb.Length > 0) sb.Append("  ");
                string pct = s.fraction >= 0.095f ? $"{s.fraction * 100f:F0}%" : $"{s.fraction * 100f:0.0#}%";
                sb.Append(colour ? $"<color={Hex(s.mineral)}>{Symbol(s.mineral)}</color> {pct}" : $"{Symbol(s.mineral)} {pct}");
            }
            for (int i = 0; i < mix.Length && i < count; i++) Add(mix[i]);
            foreach (var s in mix) if (Value(s.mineral) >= 900f && s.fraction >= 0.001f) Add(s);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ what things are made of

        /// <summary>A mix from (mineral, low %, high %) ranges, varied by the seed, normalised and
        /// sorted biggest first.</summary>
        static Share[] Mix(System.Random rng, params (Mineral m, float lo, float hi)[] parts)
        {
            var list = new List<Share>();
            float sum = 0f;
            foreach (var (m, lo, hi) in parts)
            {
                float f = lo + (float)rng.NextDouble() * (hi - lo);
                if (f <= 0f) continue;
                list.Add(new Share(m, f));
                sum += f;
            }
            for (int i = 0; i < list.Count; i++) list[i] = new Share(list[i].mineral, list[i].fraction / sum);
            list.Sort((a, b) => b.fraction.CompareTo(a.fraction));
            return list.ToArray();
        }

        /// <summary>A body in space: an asteroid (carbonaceous, stony or metallic), a moon, a comet's
        /// nucleus or a lump of its debris.</summary>
        public static Share[] ForBody(BodyClass body, int seed, out string kind)
        {
            var rng = new System.Random(seed * 7349 + 101);
            switch (body)
            {
                case BodyClass.Moon:
                    kind = "moon rock and regolith";
                    return Mix(rng, (Mineral.Silicates, 38, 45), (Mineral.Aluminium, 9, 14), (Mineral.Iron, 8, 12), (Mineral.Titanium, 4, 9),
                        (Mineral.Magnesium, 6, 9), (Mineral.Water, 0.5f, 3), (Mineral.RareEarths, 0.4f, 1.2f), (Mineral.Uranium, 0.05f, 0.2f),
                        (Mineral.Helium3, 0.02f, 0.06f));
                case BodyClass.Comet:
                    kind = "comet nucleus (dirty ice)";
                    return Mix(rng, (Mineral.Water, 42, 55), (Mineral.CarbonDioxide, 7, 12), (Mineral.Methane, 2, 5), (Mineral.Ammonia, 2, 4),
                        (Mineral.Silicates, 12, 18), (Mineral.Carbon, 7, 12), (Mineral.Iron, 1, 3), (Mineral.Sulfur, 0.5f, 1.5f),
                        (Mineral.Deuterium, 0.3f, 0.8f));
                case BodyClass.CometDebris:
                    kind = "comet debris (ice and grit)";
                    return Mix(rng, (Mineral.Water, 50, 65), (Mineral.Silicates, 15, 24), (Mineral.Carbon, 6, 12), (Mineral.Methane, 2, 5),
                        (Mineral.Ammonia, 2, 4), (Mineral.Iron, 1, 4), (Mineral.Deuterium, 0.2f, 0.6f));
            }
            // asteroids: carbonaceous (C), stony (S) or metallic (M)
            double t = rng.NextDouble();
            if (t < 0.45)
            {
                kind = "C-type asteroid (carbonaceous)";
                return Mix(rng, (Mineral.Silicates, 30, 40), (Mineral.Carbon, 14, 20), (Mineral.Water, 9, 18), (Mineral.Iron, 8, 12),
                    (Mineral.Nickel, 2, 5), (Mineral.Sulfur, 2, 5), (Mineral.Magnesium, 4, 8), (Mineral.Ammonia, 0.5f, 2), (Mineral.Cobalt, 0.3f, 1f));
            }
            if (t < 0.85)
            {
                kind = "S-type asteroid (stony)";
                return Mix(rng, (Mineral.Silicates, 45, 55), (Mineral.Iron, 14, 20), (Mineral.Nickel, 4, 8), (Mineral.Magnesium, 8, 12),
                    (Mineral.Aluminium, 3, 6), (Mineral.Titanium, 2, 4), (Mineral.Copper, 0.5f, 1.2f), (Mineral.Silver, 0.05f, 0.2f),
                    (Mineral.Gold, 0.03f, 0.12f));
            }
            kind = "M-type asteroid (metallic)";
            return Mix(rng, (Mineral.Iron, 55, 65), (Mineral.Nickel, 18, 26), (Mineral.Cobalt, 2, 4), (Mineral.Copper, 1, 2),
                (Mineral.Platinum, 0.4f, 1.4f), (Mineral.Palladium, 0.3f, 0.8f), (Mineral.Iridium, 0.15f, 0.5f), (Mineral.Gold, 0.1f, 0.4f),
                (Mineral.Silicates, 3, 8));
        }

        /// <summary>A vein in a planet's (or moon's) surface. `wet`: under a sea.</summary>
        public static Share[] ForSurface(SurfaceKind kind, int seed, bool wet, out string what)
        {
            var rng = new System.Random(seed * 3571 + 7);
            switch (kind)
            {
                case SurfaceKind.Volcanic:
                    what = "volcanic vein (sulfides)";
                    return Mix(rng, (Mineral.Silicates, 25, 35), (Mineral.Sulfur, 18, 28), (Mineral.Iron, 12, 18), (Mineral.Copper, 5, 10),
                        (Mineral.Nickel, 2, 5), (Mineral.Titanium, 2, 4), (Mineral.Uranium, 0.3f, 1.2f), (Mineral.Gold, 0.4f, 1.5f),
                        (Mineral.Platinum, 0.05f, 0.3f), (Mineral.Diamonds, 0.05f, 0.25f));
                case SurfaceKind.Desert:
                    what = "desert lode (salts and ores)";
                    return Mix(rng, (Mineral.Silicates, 40, 50), (Mineral.Iron, 14, 22), (Mineral.Aluminium, 6, 10), (Mineral.Copper, 3, 7),
                        (Mineral.Titanium, 2, 5), (Mineral.Lithium, 2, 6), (Mineral.RareEarths, 1, 3), (Mineral.Uranium, 0.5f, 2f),
                        (Mineral.Silver, 0.1f, 0.4f), (Mineral.Gold, 0.1f, 0.5f));
                case SurfaceKind.Ocean:
                    if (wet)
                    {
                        what = "heavy-water brine";
                        return Mix(rng, (Mineral.Water, 70, 80), (Mineral.Magnesium, 6, 10), (Mineral.Lithium, 2, 5), (Mineral.Deuterium, 3, 7),
                            (Mineral.Sulfur, 1, 3), (Mineral.Uranium, 0.05f, 0.2f));
                    }
                    what = "island ore body";
                    return Mix(rng, (Mineral.Silicates, 38, 46), (Mineral.Iron, 10, 15), (Mineral.Aluminium, 8, 12), (Mineral.Copper, 4, 7),
                        (Mineral.Lithium, 1, 3), (Mineral.RareEarths, 1, 2.5f), (Mineral.Silver, 0.5f, 1.5f), (Mineral.Gold, 0.3f, 0.9f));
                case SurfaceKind.GasGiant:
                    what = "gas pocket (cloud tops)";
                    return Mix(rng, (Mineral.Hydrogen, 62, 72), (Mineral.Helium, 14, 20), (Mineral.Ammonia, 5, 9), (Mineral.Methane, 3, 6),
                        (Mineral.Deuterium, 1, 3), (Mineral.Helium3, 0.8f, 2f));
                case SurfaceKind.IceGiant:
                    what = "ice-sheet deposit";
                    return Mix(rng, (Mineral.Water, 30, 40), (Mineral.Methane, 16, 24), (Mineral.Ammonia, 12, 18), (Mineral.Hydrogen, 6, 12),
                        (Mineral.Silicates, 6, 12), (Mineral.Deuterium, 1.5f, 3), (Mineral.Helium3, 0.3f, 0.8f), (Mineral.Diamonds, 0.4f, 1.5f));
                case SurfaceKind.Moon:
                    what = "regolith deposit";
                    return ForBody(BodyClass.Moon, seed, out _);
                case SurfaceKind.Asteroid:
                    var mix = ForBody(BodyClass.Asteroid, seed, out what);
                    what = what.Replace("asteroid", "rock");
                    return mix;
            }
            what = "deposit";
            return Mix(rng, (Mineral.Silicates, 60, 70), (Mineral.Iron, 20, 30));
        }
    }
}

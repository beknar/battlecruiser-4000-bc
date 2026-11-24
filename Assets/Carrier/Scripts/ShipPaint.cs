using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A ship's paint: a pattern (one of ShipPaint.Patterns), its scale and angle, three colours
    /// (primary, secondary and trim), a logo and the lettering on the hull with its colour. Written
    /// as a short code for the player's save, the -paint flag and co-op (ToCode, TryParse):
    /// "1.pattern.scale%.angle.primary.secondary.trim.letters.logo.TEXT", the colours as RRGGBB,
    /// logo -1 for none and the text's spaces as underscores, e.g.
    /// "1.90.100.0.d9731a.1a1a1a.e8dcc0.1a1a1a.8.TIGER-01".
    /// </summary>
    [Serializable]
    public class PaintScheme
    {
        /// <summary>The code's version: 1, or 2 with the logo and lettering moved (Placed).</summary>
        public const int Version = 2;
        public const int MinScale = 25, MaxScale = 400;
        /// <summary>A decal moved along or up the hull by at most this many percent of it, and
        /// sized from MinDecal to MaxDecal percent of the class's own size.</summary>
        public const int MaxShift = 45, MinDecal = 40, MaxDecal = 250;
        /// <summary>Which sides a decal is on.</summary>
        public const int BothSides = 0, PortOnly = 1, StarboardOnly = 2;

        /// <summary>0 to 99: family (pattern / 10) and variant (pattern % 10).</summary>
        public int pattern;
        /// <summary>How big the pattern is, in percent (25 to 400: bigger is finer).</summary>
        public int scale = 100;
        /// <summary>The pattern turned by this many degrees (0 to 359).</summary>
        public int angle;
        public Color32 primary = new Color32(138, 144, 153, 255);
        public Color32 secondary = new Color32(77, 83, 92, 255);
        public Color32 trim = new Color32(217, 164, 65, 255);
        /// <summary>The logo's and the lettering's colour.</summary>
        public Color32 letters = new Color32(240, 240, 240, 255);
        /// <summary>ShipPaint.LogoNames, or -1 for none.</summary>
        public int logo = -1;
        /// <summary>Up to ShipPaint.MaxLetters: A-Z, 0-9, the dash and space (SanitizeText).</summary>
        public string text = "";

        // where the logo and the lettering are, from the class's own place (ShipPaint.Placement):
        // moved along the hull (+ toward the bow) and up it, in percent of its length and height;
        // their size in percent; turned by degrees (counterclockwise as read); on which sides
        public int logoAlong, logoUp, logoSize = 100, logoTurn, logoSides;
        public int textAlong, textUp, textSize = 100, textTurn, textSides;

        /// <summary>Is the logo or the lettering anywhere but the class's own place?</summary>
        public bool Placed => logoAlong != 0 || logoUp != 0 || logoSize != 100 || logoTurn != 0 || logoSides != 0
                           || textAlong != 0 || textUp != 0 || textSize != 100 || textTurn != 0 || textSides != 0;

        /// <summary>The logo and the lettering back at the class's own place.</summary>
        public void ResetPlacement()
        {
            logoAlong = logoUp = logoTurn = logoSides = textAlong = textUp = textTurn = textSides = 0;
            logoSize = textSize = 100;
        }

        public PaintScheme Clone() => (PaintScheme)MemberwiseClone();

        public Color32 GetColor(int slot) => slot == 0 ? primary : slot == 1 ? secondary : slot == 2 ? trim : letters;

        public void SetColor(int slot, Color32 c)
        {
            c.a = 255;
            if (slot == 0) primary = c;
            else if (slot == 1) secondary = c;
            else if (slot == 2) trim = c;
            else letters = c;
        }

        /// <summary>Everything in range (the pattern, scale, angle and logo clamped; the text cleaned).</summary>
        public PaintScheme Clamped()
        {
            var s = Clone();
            // (-1 is the factory finish: the hull's own colours, the logo and lettering on them)
            s.pattern = pattern == ShipPaint.FactoryPattern ? pattern : Mathf.Clamp(pattern, 0, ShipPaint.Patterns - 1);
            s.scale = Mathf.Clamp(scale, MinScale, MaxScale);
            s.angle = ((angle % 360) + 360) % 360;
            s.logo = logo < 0 || logo >= ShipPaint.Logos ? -1 : logo;
            s.text = SanitizeText(text);
            s.primary.a = s.secondary.a = s.trim.a = s.letters.a = 255;
            s.logoAlong = Mathf.Clamp(logoAlong, -MaxShift, MaxShift);
            s.logoUp = Mathf.Clamp(logoUp, -MaxShift, MaxShift);
            s.logoSize = Mathf.Clamp(logoSize, MinDecal, MaxDecal);
            s.logoTurn = ((logoTurn % 360) + 360) % 360;
            s.logoSides = logoSides < 0 || logoSides > StarboardOnly ? BothSides : logoSides;
            s.textAlong = Mathf.Clamp(textAlong, -MaxShift, MaxShift);
            s.textUp = Mathf.Clamp(textUp, -MaxShift, MaxShift);
            s.textSize = Mathf.Clamp(textSize, MinDecal, MaxDecal);
            s.textTurn = ((textTurn % 360) + 360) % 360;
            s.textSides = textSides < 0 || textSides > StarboardOnly ? BothSides : textSides;
            return s;
        }

        /// <summary>The scheme as a code. With the logo and lettering where the class puts them,
        /// a version 1 code, as games before placement read; moved, version 2, with the
        /// placement ("along,up,size,turn,sides" for the logo, then the lettering) before the text.</summary>
        public string ToCode()
        {
            var s = Clamped();
            string I(int v) => v.ToString(CultureInfo.InvariantCulture);
            string head = string.Join(".", I(s.Placed ? Version : 1), I(s.pattern), I(s.scale), I(s.angle),
                                      Hex(s.primary), Hex(s.secondary), Hex(s.trim), Hex(s.letters), I(s.logo));
            string text = s.text.Replace(' ', '_');
            if (!s.Placed) return head + "." + text;
            string place = string.Join(",", I(s.logoAlong), I(s.logoUp), I(s.logoSize), I(s.logoTurn), I(s.logoSides),
                                       I(s.textAlong), I(s.textUp), I(s.textSize), I(s.textTurn), I(s.textSides));
            return head + "." + place + "." + text;
        }

        public override string ToString() => ToCode();

        /// <summary>Read a code (ToCode). Numbers out of range are clamped and the text cleaned; a
        /// code that isn't one (or is from a later version) gives false.</summary>
        public static bool TryParse(string code, out PaintScheme scheme)
        {
            scheme = null;
            if (string.IsNullOrEmpty(code)) return false;
            var parts = code.Trim().Split('.');
            if (parts.Length < 9 || !Int(parts[0], out int version) || version < 1 || version > Version) return false;
            // (version 2: the placement after the logo, then the text)
            int textAt = version >= 2 ? 10 : 9;
            if (parts.Length > textAt + 1 || (version >= 2 && parts.Length < 10)) return false;
            if (!Int(parts[1], out int pattern) || !Int(parts[2], out int scale) || !Int(parts[3], out int angle)) return false;
            if (!Col(parts[4], out var a) || !Col(parts[5], out var b) || !Col(parts[6], out var c) || !Col(parts[7], out var d)) return false;
            if (!Int(parts[8], out int logo)) return false;
            var read = new PaintScheme
            {
                pattern = pattern, scale = scale, angle = angle, primary = a, secondary = b, trim = c, letters = d, logo = logo,
                text = parts.Length > textAt ? parts[textAt] : "",
            };
            if (version >= 2)
            {
                var p = parts[9].Split(',');
                var v = new int[10];
                if (p.Length != 10) return false;
                for (int i = 0; i < 10; i++) if (!Int(p[i], out v[i])) return false;
                read.logoAlong = v[0]; read.logoUp = v[1]; read.logoSize = v[2]; read.logoTurn = v[3]; read.logoSides = v[4];
                read.textAlong = v[5]; read.textUp = v[6]; read.textSize = v[7]; read.textTurn = v[8]; read.textSides = v[9];
            }
            scheme = read.Clamped();
            return true;
        }

        /// <summary>Lettering as the hull can show it: capitals, digits, the dash and spaces (an
        /// underscore is a space), at most ShipPaint.MaxLetters, without spaces at either end.</summary>
        public static string SanitizeText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(ShipPaint.MaxLetters);
            foreach (char ch0 in s.ToUpperInvariant())
            {
                char ch = ch0 == '_' ? ' ' : ch0;
                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == ' ') sb.Append(ch);
                if (sb.Length >= ShipPaint.MaxLetters) break;
            }
            return sb.ToString().Trim();
        }

        /// <summary>Lettering's code for the font atlas: 0 space, 1-26 A-Z, 27-36 0-9, 37 the dash.</summary>
        public static int LetterCode(char ch)
        {
            if (ch >= 'A' && ch <= 'Z') return 1 + ch - 'A';
            if (ch >= '0' && ch <= '9') return 27 + ch - '0';
            return ch == '-' ? 37 : 0;
        }

        public static string Hex(Color32 c) => $"{c.r:x2}{c.g:x2}{c.b:x2}";

        public static bool Col(string s, out Color32 c)
        {
            c = new Color32(0, 0, 0, 255);
            if (s == null || s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
            c = new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
            return true;
        }

        static bool Int(string s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        public static Color32 C(string hex) => Col(hex, out var c) ? c : new Color32(255, 0, 255, 255);
    }

    /// <summary>
    /// Ship paint, done in the hull's shader (Carrier/ShipPaint, in Resources/Paint) rather than
    /// with differently coloured copies of the ships: Apply gives a ship's hull renderers (and its
    /// turrets, which share the hull material) a painted copy of their hull material, made from the
    /// original's own textures and settings, with the scheme on it, and tells each renderer where
    /// it is on the ship (a property block). Ships never painted keep the shared hull material as
    /// it was: only the player's ship (PlayerFrigate, at the start and after a swap), the other
    /// players' ships in co-op (RemotePlayer) and the start menu's preview are painted.
    ///
    /// The player's scheme is kept for each class (PlayerPrefs "paint.&lt;class&gt;"; a class not yet
    /// painted takes the last scheme saved, "paint"), chosen on the start menu's Customise step or
    /// from the pause menu (Paint the ship), or given at launch with -paint &lt;code or preset&gt;
    /// (or ?paint= on the page). Tests, reels and AI players fly unpainted unless given one.
    /// </summary>
    public static class ShipPaint
    {
        public const int Families = 10, Variants = 10, Patterns = Families * Variants;
        public const int Logos = 12, MaxLetters = 12;
        /// <summary>The pattern that is no pattern: the factory finish, with a logo and lettering on it.</summary>
        public const int FactoryPattern = -1;

        public const string ShaderResource = "Paint/ShipPaint", LogoResource = "Paint/paint_logos", FontResource = "Paint/paint_font",
                            LogoUiResource = "Paint/paint_logos_ui";
        const string Pref = "paint";

        public static readonly string[] FamilyNames =
        {
            "Bands", "Stripes", "Chevrons", "Checks", "Hexagons", "Dots and rings", "Waves and scales",
            "Woodland camo", "Splinter and digital", "Tiger and triangles",
        };

        static readonly string[][] VariantNames =
        {
            new[] { "Solid", "Belly", "Two-tone", "Racing stripe", "Twin stripes", "Nose", "Tail", "Waterline", "Middle band", "Spine and side" },
            new[] { "Bars", "Upright bars", "Diagonal", "Diagonal back", "Slant thin", "Pinned bars", "Pinned upright", "Pinned diagonal", "Pinstripes", "Alternate" },
            new[] { "Shallow", "Chevrons", "Steep", "Arrowheads", "Zigzag", "Trimmed shallow", "Trimmed", "Trimmed steep", "Three-tone", "Three-tone steep" },
            new[] { "Checker", "Diamonds", "Small checks", "Grid", "Checker grid", "Plaid", "Harlequin", "Brick", "Three-tone checker", "Bordered checker" },
            new[] { "Honeycomb", "Two-tone hex", "Three-tone hex", "Hex scatter", "Hex armour", "Small hex", "Large honeycomb", "Hex dots", "Hex bands", "Hex highlights" },
            new[] { "Polka", "Offset polka", "Scattered dots", "Rings", "Target", "Haloed dots", "Mixed dots", "Sunburst", "Bubbles", "Dot matrix" },
            new[] { "Waves", "Tall waves", "Double waves", "Zigzag", "Fish scales", "Dragon scales", "Ripples", "Flicker", "Trimmed waves", "Wavy plaid" },
            new[] { "Large woodland", "Woodland", "Medium woodland", "Small woodland", "Fine woodland", "Swirl large", "Swirl", "Swirl medium", "Swirl small", "Swirl fine" },
            new[] { "Splinter", "Long splinter", "Fine splinter", "Broad shards", "Edged splinter", "Digital", "Digital three-tone", "Digital blobs", "Digital coarse", "Digital fine" },
            new[] { "Tiger stripes", "Tiger and trim", "Zebra", "Flames", "Triangles", "Sawtooth", "Shark teeth", "Three-tone triangles", "Claw marks", "Big triangles" },
        };

        public static readonly string[] LogoNames =
            { "Star", "Wings", "Skull", "Shield", "Eagle", "Lightning", "Anchor", "Planet", "Claw", "Crescent", "Comet", "Gear" };

        public static string PatternName(int p)
        {
            if (p == FactoryPattern) return "the hull's own colours";
            p = Mathf.Clamp(p, 0, Patterns - 1);
            return VariantNames[p / Variants][p % Variants];
        }

        public static string FamilyOf(int p) => p == FactoryPattern ? "Factory finish" : FamilyNames[Mathf.Clamp(p, 0, Patterns - 1) / Variants];

        /// <summary>The 24 colours of the paint screen's palette.</summary>
        public static readonly Color32[] Palette =
        {
            PaintScheme.C("f0f0f0"), PaintScheme.C("b8bec6"), PaintScheme.C("8a9099"), PaintScheme.C("5a616b"),
            PaintScheme.C("33383f"), PaintScheme.C("141619"), PaintScheme.C("e8dcc0"), PaintScheme.C("c8b48a"),
            PaintScheme.C("9c8257"), PaintScheme.C("6b5435"), PaintScheme.C("8c1c1c"), PaintScheme.C("d42a2a"),
            PaintScheme.C("d9731a"), PaintScheme.C("e0b020"), PaintScheme.C("d4a640"), PaintScheme.C("5d6b2f"),
            PaintScheme.C("2f5a32"), PaintScheme.C("3d8a5a"), PaintScheme.C("3cc8e0"), PaintScheme.C("1f4fa0"),
            PaintScheme.C("1a2a55"), PaintScheme.C("6a3d9a"), PaintScheme.C("c04a8a"), PaintScheme.C("9fb0bf"),
        };

        public struct Preset
        {
            public string name;
            public string code;
            public PaintScheme Scheme => PaintScheme.TryParse(code, out var s) ? s : new PaintScheme();
        }

        /// <summary>The ten ready-made schemes (no lettering: choosing one keeps the player's).</summary>
        public static readonly Preset[] Presets =
        {
            new Preset { name = "Fleet grey",      code = "1.2.100.0.8a9099.4d535c.d9a441.f0f0f0.0." },
            new Preset { name = "Tiger stripes",   code = "1.90.100.0.d9731a.1a1a1a.e8dcc0.141619.8." },
            new Preset { name = "Arctic splinter", code = "1.81.100.0.e6ecf0.9fb0bf.4a5a6a.2a3540.9." },
            new Preset { name = "Corsair red",     code = "1.26.100.0.8c1c1c.141414.d4a640.d4a640.2." },
            new Preset { name = "Night hunter",    code = "1.76.100.0.1c2026.2c333d.4a1818.6b7480.4." },
            new Preset { name = "Desert digital",  code = "1.86.100.0.c8b48a.9c8257.6b5435.3a2e1e.3." },
            new Preset { name = "Checker",         code = "1.30.100.0.f0f0f0.141619.d42a2a.d42a2a.5." },
            new Preset { name = "Chevrons",        code = "1.21.100.0.e0b020.141619.f0f0f0.141619.1." },
            new Preset { name = "Racing stripe",   code = "1.3.100.0.1f4fa0.f0f0f0.c0202a.f0f0f0.0." },
            new Preset { name = "Hex armour",      code = "1.44.100.0.50585f.7b858d.3cc8e0.3cc8e0.11." },
        };

        /// <summary>A preset, keeping the lettering of `keep` (if any).</summary>
        public static PaintScheme FromPreset(int i, PaintScheme keep = null)
        {
            var s = Presets[Mathf.Clamp(i, 0, Presets.Length - 1)].Scheme;
            if (keep != null) { s.text = keep.text; KeepPlacement(s, keep); }
            return s;
        }

        /// <summary>The logo and lettering left where the player put them (a new preset or a
        /// random scheme changes the colours, not where they are).</summary>
        static void KeepPlacement(PaintScheme s, PaintScheme keep)
        {
            s.logoAlong = keep.logoAlong; s.logoUp = keep.logoUp; s.logoSize = keep.logoSize; s.logoTurn = keep.logoTurn; s.logoSides = keep.logoSides;
            s.textAlong = keep.textAlong; s.textUp = keep.textUp; s.textSize = keep.textSize; s.textTurn = keep.textTurn; s.textSides = keep.textSides;
        }

        /// <summary>A random scheme: any pattern, three colours from the palette that differ, a logo.</summary>
        public static PaintScheme Random(System.Random rng, PaintScheme keep = null)
        {
            int a = rng.Next(Palette.Length), b, c;
            do b = rng.Next(Palette.Length); while (b == a);
            do c = rng.Next(Palette.Length); while (c == a || c == b);
            var s = new PaintScheme
            {
                pattern = rng.Next(Patterns),
                scale = 60 + rng.Next(141),
                angle = rng.Next(4) == 0 ? rng.Next(12) * 30 : 0,
                primary = Palette[a], secondary = Palette[b], trim = Palette[c],
                letters = Luma(Palette[a]) > 0.5f ? Palette[5] : Palette[0],
                logo = rng.Next(Logos),
                text = keep != null ? keep.text : "",
            };
            if (keep != null) KeepPlacement(s, keep);
            return s;
        }

        static float Luma(Color32 c) => (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;

        /// <summary>A scheme from text: a code (PaintScheme.ToCode), a preset's name (any case,
        /// spaces and dashes optional, the start of it will do: "tiger") or number (0 to 9). Null
        /// for "none", "factory", nothing, or what can't be read (the factory finish).</summary>
        public static PaintScheme FromText(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            if (PaintScheme.TryParse(s, out var scheme)) return scheme;
            string key = Key(s);
            if (key == "none" || key == "factory" || key.Length == 0) return null;
            if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0 && n < Presets.Length) return Presets[n].Scheme;
            for (int i = 0; i < Presets.Length; i++) if (Key(Presets[i].name) == key) return Presets[i].Scheme;
            for (int i = 0; i < Presets.Length; i++) if (Key(Presets[i].name).StartsWith(key, StringComparison.Ordinal)) return Presets[i].Scheme;
            Debug.LogWarning($"[ShipPaint] '{s}' isn't a paint code or a preset: the factory finish");
            return null;
        }

        static string Key(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s.ToLowerInvariant()) if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
            return sb.ToString();
        }

        // ------------------------------------------------------------ the player's

        /// <summary>Chosen this run, by class ("" a scheme's absence: the factory finish): wins
        /// over one given at launch, as FrigateChoice's does.</summary>
        static readonly Dictionary<string, string> picked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The code of what the player's ship wears now ("" unpainted), for co-op.</summary>
        public static string PlayerCode { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            picked.Clear();
            PlayerCode = "";
            painted.Clear();
            shader = null;
            logoTex = fontTex = logoUiTex = null;
        }

        /// <summary>Given on the command line (-paint) or the page address (?paint=), or null.</summary>
        public static string Given => LaunchArgs.ValueOf("-paint") ?? LaunchArgs.PageOption("paint");

        /// <summary>The scheme kept for a class (null: the factory finish).</summary>
        public static PaintScheme Saved(string frigate)
        {
            try
            {
                string code = PlayerPrefs.GetString(Pref + "." + (frigate ?? ""), null);
                if (code == null) code = PlayerPrefs.GetString(Pref, "");
                return FromText(code);
            }
            catch { return null; }
        }

        /// <summary>The player's scheme for a class: chosen this run, else given at launch, else
        /// saved (a test, a reel or an AI player: only one given).</summary>
        public static PaintScheme ForPlayer(string frigate)
        {
            if (picked.TryGetValue(frigate ?? "", out var code)) return FromText(code);
            string given = Given;
            if (given != null) return FromText(given);
            if (LaunchArgs.Automated) return null;
            return Saved(frigate);
        }

        /// <summary>Choose a scheme for a class (null: the factory finish): flown from now on this
        /// run, and kept for the next (but not by a test, a reel or an AI player).</summary>
        public static void Set(string frigate, PaintScheme s)
        {
            string code = s != null ? s.ToCode() : "none";
            picked[frigate ?? ""] = s != null ? code : "";
            if (LaunchArgs.Automated) return;
            try
            {
                PlayerPrefs.SetString(Pref + "." + (frigate ?? ""), code);
                if (s != null) PlayerPrefs.SetString(Pref, code);
                PlayerPrefs.Save();
            }
            catch { }
        }

        /// <summary>Paint the player's ship in their scheme for its class.</summary>
        public static void ApplyPlayer(Component ship)
        {
            if (ship == null) return;
            string id = FrigateClass.Of(ship);
            var s = ForPlayer(id);
            Apply(ship.gameObject, s, id);
            PlayerCode = s != null ? s.ToCode() : "";
        }

        /// <summary>Paint the level's player ship (PlayerFrigate).</summary>
        public static void ApplyPlayer()
        {
            var pf = UnityEngine.Object.FindAnyObjectByType<PlayerFrigate>();
            if (pf != null && pf.ship != null) ApplyPlayer(pf.ship);
        }

        // ------------------------------------------------------------ painting

        /// <summary>A painted material, the hull material it was made from, and the ship it is on.</summary>
        class Painted { public Material original; public GameObject owner; }
        static readonly Dictionary<Material, Painted> painted = new Dictionary<Material, Painted>();
        static Shader shader;
        static Texture2D logoTex, fontTex, logoUiTex;
        static MaterialPropertyBlock block;

        public static Shader PaintShader => shader != null ? shader : (shader = Resources.Load<Shader>(ShaderResource));
        public static Texture2D LogoAtlas => logoTex != null ? logoTex : (logoTex = Resources.Load<Texture2D>(LogoResource));
        public static Texture2D FontAtlas => fontTex != null ? fontTex : (fontTex = Resources.Load<Texture2D>(FontResource));
        /// <summary>The logos for the paint screen: white, their shapes in the alpha (the shader's
        /// atlases are one channel, which the GUI would draw red).</summary>
        public static Texture2D LogoAtlasUi => logoUiTex != null ? logoUiTex : (logoUiTex = Resources.Load<Texture2D>(LogoUiResource));

        public static readonly int ToShipId = Shader.PropertyToID("_PaintToShip"), DecalsId = Shader.PropertyToID("_PaintDecals");
        static readonly int AId = Shader.PropertyToID("_PaintA"), BId = Shader.PropertyToID("_PaintB"), CId = Shader.PropertyToID("_PaintC"),
                            DId = Shader.PropertyToID("_PaintD"), PatternId = Shader.PropertyToID("_PaintPattern"),
                            ScaleId = Shader.PropertyToID("_PaintScale"), AngleId = Shader.PropertyToID("_PaintAngle"),
                            MinId = Shader.PropertyToID("_PaintBoundsMin"), MaxId = Shader.PropertyToID("_PaintBoundsMax"),
                            LogoRectId = Shader.PropertyToID("_LogoRect"), TextRectId = Shader.PropertyToID("_TextRect"),
                            DecalXformId = Shader.PropertyToID("_DecalXform"),
                            Text0Id = Shader.PropertyToID("_Text0"), Text1Id = Shader.PropertyToID("_Text1"), Text2Id = Shader.PropertyToID("_Text2"),
                            LogoTexId = Shader.PropertyToID("_LogoTex"), FontTexId = Shader.PropertyToID("_FontTex");

        /// <summary>The hull material a (possibly painted) material is, or was made from.</summary>
        public static Material Original(Material m) => m != null && painted.TryGetValue(m, out var p) ? p.original : m;

        public static bool IsPainted(Material m) => m != null && painted.ContainsKey(m);

        /// <summary>A ship's hull material (the builder's "&lt;prefab&gt;_Hull", shared by its hull
        /// and turrets).</summary>
        public static bool IsHull(Material m)
        {
            if (m == null) return false;
            string n = m.name;
            int inst = n.IndexOf(" (Instance)", StringComparison.Ordinal);
            if (inst >= 0) n = n.Substring(0, inst);
            return n.EndsWith("_Hull", StringComparison.Ordinal);
        }

        /// <summary>Paint a ship (or a copy of its model: its root's space is the ship's) in a
        /// scheme, or (null) put its hull material back. `frigate`: its class, for where the logo
        /// and lettering go (the ship's FrigateClass if not given). Returns how many renderers
        /// were painted.</summary>
        public static int Apply(GameObject root, PaintScheme scheme, string frigate = null)
        {
            if (root == null) return 0;
            if (scheme == null) { Clear(root); return 0; }
            var sh = PaintShader;
            if (sh == null || !sh.isSupported)
            {
                Debug.LogWarning($"[ShipPaint] no paint shader ({ShaderResource}{(sh != null ? ", not supported here" : "")}): {root.name} left as it is");
                return 0;
            }
            Sweep();
            scheme = scheme.Clamped();

            // the renderers with the hull material, where each is on the ship, and the ship's bounds
            var toRoot = root.transform.worldToLocalMatrix;
            var targets = new List<(Renderer r, Matrix4x4 toShip, bool decals)>();
            bool any = false;
            var bounds = new Bounds();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                var mats = r.sharedMaterials;
                bool hull = false;
                foreach (var m in mats) if (IsHull(Original(m))) { hull = true; break; }
                if (!hull) continue;
                var toShip = toRoot * r.transform.localToWorldMatrix;
                targets.Add((r, toShip, !OnTurret(r.transform, root.transform)));
                Mesh mesh = r is MeshRenderer && r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : (r as SkinnedMeshRenderer)?.sharedMesh;
                if (mesh == null) continue;
                var mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = toShip.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            if (targets.Count == 0) return 0;
            if (!any) bounds = new Bounds(Vector3.zero, new Vector3(60f, 60f, 180f));
            if (string.IsNullOrEmpty(frigate)) frigate = FrigateClass.Of(root.transform);
            var place = PlacementFor(frigate);

            var made = new Dictionary<Material, Material>();
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var (r, toShip, decals) in targets)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var orig = Original(mats[i]);
                    if (!IsHull(orig)) continue;
                    if (!made.TryGetValue(orig, out var pm))
                    {
                        pm = OwnedBy(root, orig) ?? Make(orig, root, sh);
                        Configure(pm, scheme, bounds, place);
                        made[orig] = pm;
                    }
                    mats[i] = pm;
                }
                r.sharedMaterials = mats;
                r.GetPropertyBlock(block);
                block.SetMatrix(ToShipId, toShip);
                block.SetFloat(DecalsId, decals ? 1f : 0f);
                r.SetPropertyBlock(block);
            }
            return targets.Count;
        }

        /// <summary>Put a ship's hull material back where it was painted.</summary>
        public static void Clear(GameObject root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && painted.TryGetValue(mats[i], out var p)) { mats[i] = p.original; changed = true; }
                if (!changed) continue;
                r.sharedMaterials = mats;
                r.SetPropertyBlock(null);
            }
        }

        /// <summary>Is a renderer part of a turret (no logo or lettering on it: it turns)?</summary>
        static bool OnTurret(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (t.name == "Turrets" || t.GetComponent<CarrierTurret>() != null) return true;
            return false;
        }

        static Material OwnedBy(GameObject root, Material original)
        {
            foreach (var kv in painted)
                if (kv.Value.owner == root && kv.Value.original == original && kv.Key != null) return kv.Key;
            return null;
        }

        static Material Make(Material original, GameObject owner, Shader sh)
        {
            var m = new Material(sh) { name = original.name + " (painted)", hideFlags = HideFlags.DontSave };
            m.CopyPropertiesFromMaterial(original);
            m.shader = sh;
            foreach (var tex in new[] { "_BaseMap", "_BumpMap" })
                if (original.HasProperty(tex) && m.HasProperty(tex))
                {
                    m.SetTexture(tex, original.GetTexture(tex));
                    m.SetTextureScale(tex, original.GetTextureScale(tex));
                    m.SetTextureOffset(tex, original.GetTextureOffset(tex));
                }
            foreach (var f in new[] { "_Metallic", "_Smoothness", "_BumpScale", "_Cull" })
                if (original.HasProperty(f)) m.SetFloat(f, original.GetFloat(f));
            if (original.HasProperty("_BaseColor")) m.SetColor("_BaseColor", original.GetColor("_BaseColor"));
            if (original.IsKeywordEnabled("_NORMALMAP") && original.GetTexture("_BumpMap") != null) m.EnableKeyword("_NORMALMAP");
            else m.DisableKeyword("_NORMALMAP");
            m.renderQueue = original.renderQueue;
            m.doubleSidedGI = original.doubleSidedGI;
            painted[m] = new Painted { original = original, owner = owner };
            return m;
        }

        /// <summary>Drop the painted materials of ships that are gone.</summary>
        static void Sweep()
        {
            List<Material> gone = null;
            foreach (var kv in painted)
                if (kv.Value.owner == null || kv.Key == null) (gone ??= new List<Material>()).Add(kv.Key);
            if (gone == null) return;
            foreach (var m in gone)
            {
                painted.Remove(m);
                if (m != null) UnityEngine.Object.Destroy(m);
            }
        }

        static void Configure(Material m, PaintScheme s, Bounds b, Placement place)
        {
            m.SetColor(AId, s.primary);
            m.SetColor(BId, s.secondary);
            m.SetColor(CId, s.trim);
            m.SetColor(DId, s.letters);
            m.SetFloat(PatternId, s.pattern);
            m.SetFloat(ScaleId, s.scale / 100f);
            m.SetFloat(AngleId, s.angle * Mathf.Deg2Rad);
            m.SetVector(MinId, b.min);
            m.SetVector(MaxId, b.max);
            // (a renderer without a property block, a wreck's piece say: its own space, no decals)
            m.SetMatrix(ToShipId, Matrix4x4.identity);
            m.SetFloat(DecalsId, 0f);
            var d = Decals(s, b, place);
            m.SetVector(LogoRectId, d.logo);
            m.SetVector(TextRectId, d.text);
            m.SetVector(DecalXformId, d.xform);
            m.SetVector(Text0Id, d.letters[0]);
            m.SetVector(Text1Id, d.letters[1]);
            m.SetVector(Text2Id, d.letters[2]);
            if (LogoAtlas != null) m.SetTexture(LogoTexId, LogoAtlas);
            if (FontAtlas != null) m.SetTexture(FontTexId, FontAtlas);
        }

        // ------------------------------------------------------------ where the logo and lettering go

        /// <summary>Where a class's logo and lettering go on each side, as fractions of its bounds:
        /// along it (0 the stern, 1 the bow), up it (0 its keel, 1 its top), and their sizes (of
        /// its height). Both are projected across the ship (along x) onto each side.</summary>
        public struct Placement
        {
            public float logoZ, logoY, logoSize, textZ, textY, textSize;
            public Placement(float logoZ, float logoY, float logoSize, float textZ, float textY, float textSize)
            {
                this.logoZ = logoZ; this.logoY = logoY; this.logoSize = logoSize;
                this.textZ = textZ; this.textY = textY; this.textSize = textSize;
            }
        }

        /// <summary>For a class not in the table: the logo 30% back from the bow, the lettering
        /// amidships a little below the middle.</summary>
        public static readonly Placement Fallback = new Placement(0.70f, 0.55f, 0.32f, 0.45f, 0.45f, 0.13f);

        // (guesses from the hulls' bounds: Data/*_hardpoints.json bounds_size, width x height x length)
        static readonly Dictionary<string, Placement> placements = new Dictionary<string, Placement>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sentinel"] = new Placement(0.70f, 0.55f, 0.30f, 0.42f, 0.45f, 0.12f),   // 53 x 63 x 180
            ["Lancer"] = new Placement(0.68f, 0.52f, 0.34f, 0.42f, 0.45f, 0.14f),     // 61 x 55 x 170
            ["Warden"] = new Placement(0.70f, 0.55f, 0.32f, 0.45f, 0.45f, 0.13f),     // 37 x 52 x 200
            ["Talon"] = new Placement(0.66f, 0.50f, 0.36f, 0.42f, 0.45f, 0.15f),      // 57 x 48 x 175
            ["Vanguard"] = new Placement(0.72f, 0.55f, 0.28f, 0.46f, 0.48f, 0.12f),   // 29 x 63 x 190
            ["Halberd"] = new Placement(0.72f, 0.50f, 0.22f, 0.45f, 0.42f, 0.09f),    // 117 x 109 x 300
            ["Rapier"] = new Placement(0.72f, 0.52f, 0.26f, 0.45f, 0.44f, 0.10f),     // 69 x 89 x 280
            ["Claymore"] = new Placement(0.72f, 0.55f, 0.30f, 0.45f, 0.45f, 0.12f),   // 59 x 77 x 300
            ["Bulwark"] = new Placement(0.72f, 0.52f, 0.30f, 0.45f, 0.44f, 0.12f),    // 110 x 76 x 290
            ["Stiletto"] = new Placement(0.70f, 0.50f, 0.40f, 0.45f, 0.45f, 0.16f),   // 81 x 42 x 270
            ["Sovereign"] = new Placement(0.75f, 0.52f, 0.26f, 0.46f, 0.44f, 0.10f),  // 159 x 125 x 420
            ["Dominion"] = new Placement(0.75f, 0.52f, 0.28f, 0.46f, 0.44f, 0.11f),   // 118 x 101 x 430
        };

        public static Placement PlacementFor(string frigate) =>
            !string.IsNullOrEmpty(frigate) && placements.TryGetValue(frigate, out var p) ? p : Fallback;

        /// <summary>The shader's decal values for a scheme on a ship's bounds: the logo's
        /// (z, y, half size, index), the lettering's (z, y, letter height, count), the letters, and
        /// both turns and sides (logo turn in radians, logo sides, lettering turn, lettering sides).
        /// The player's placement moves and sizes them from the class's own place, kept on the hull.</summary>
        public static (Vector4 logo, Vector4 text, Vector4[] letters, Vector4 xform) Decals(PaintScheme s, Bounds b, Placement place)
        {
            s = s.Clamped();
            float len = Mathf.Max(1f, b.size.z), height = Mathf.Max(1f, b.size.y);
            float half = 0.5f * Mathf.Min(place.logoSize * height, 0.14f * len) * s.logoSize / 100f;
            // (the centre kept on the hull, a little in from its ends)
            float logoZ = Mathf.Clamp(place.logoZ + s.logoAlong / 100f, 0.05f, 0.95f);
            float logoY = Mathf.Clamp(place.logoY + s.logoUp / 100f, 0.05f, 0.95f);
            var logo = new Vector4(Mathf.Lerp(b.min.z, b.max.z, logoZ), Mathf.Lerp(b.min.y, b.max.y, logoY), half, s.logo);
            string t = PaintScheme.SanitizeText(s.text);
            // (the letters no longer than about half the ship, at the class's size)
            float h = t.Length > 0 ? Mathf.Min(place.textSize * height, 0.5f * len / (t.Length * 0.75f)) * s.textSize / 100f : 1f;
            float textZ = Mathf.Clamp(place.textZ + s.textAlong / 100f, 0.05f, 0.95f);
            float textY = Mathf.Clamp(place.textY + s.textUp / 100f, 0.05f, 0.95f);
            var text = new Vector4(Mathf.Lerp(b.min.z, b.max.z, textZ), Mathf.Lerp(b.min.y, b.max.y, textY), h, t.Length);
            var letters = new Vector4[3];
            for (int i = 0; i < MaxLetters; i++)
                letters[i / 4][i % 4] = i < t.Length ? PaintScheme.LetterCode(t[i]) : 0f;
            var xform = new Vector4(s.logoTurn * Mathf.Deg2Rad, s.logoSides, s.textTurn * Mathf.Deg2Rad, s.textSides);
            return (logo, text, letters, xform);
        }

        /// <summary>The bounds (in its own space) a painted ship's logo and lettering are placed in,
        /// read back from its paint; false if it isn't painted.</summary>
        public static bool PaintedBounds(GameObject root, out Bounds b)
        {
            b = default;
            if (root == null) return false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && painted.ContainsKey(m))
                    {
                        b.SetMinMax(m.GetVector(MinId), m.GetVector(MaxId));
                        return true;
                    }
            return false;
        }

        /// <summary>The placement fractions (along 0 stern to 1 bow, up 0 keel to 1 top) a point on
        /// the hull's side is at, for dragging a decal on the preview: `local` in the ship's space.</summary>
        public static Vector2 Fractions(Bounds b, Vector3 local) =>
            new Vector2(Mathf.InverseLerp(b.min.z, b.max.z, local.z), Mathf.InverseLerp(b.min.y, b.max.y, local.y));
    }
}

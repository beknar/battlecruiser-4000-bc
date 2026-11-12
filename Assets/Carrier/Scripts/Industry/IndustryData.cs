using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>One input of a recipe: so many tonnes of an item (a mineral or a refined material).</summary>
    [Serializable]
    public class Ingredient
    {
        public string item;
        public float tonnes;
    }

    /// <summary>A part's effect on a frigate: one of its numbers multiplied by `factor`.</summary>
    [Serializable]
    public class PartEffect
    {
        public string stat;
        public float factor = 1f;
    }

    /// <summary>A refined material and the refinery recipe that makes it.</summary>
    [Serializable]
    public class RefinedSpec
    {
        public string id, name, form, kind;
        public float value, makes, seconds;
        public Ingredient[] inputs;
    }

    /// <summary>A ship part and the factory recipe that builds one.</summary>
    [Serializable]
    public class PartSpec
    {
        public string id, name, look;
        public float mass, seconds, value;
        public Ingredient[] inputs;
        public PartEffect[] effects;
    }

    /// <summary>An industrial installation: where it is and what it can refine and build.</summary>
    [Serializable]
    public class SiteSpec
    {
        public string name, where, body, description;
        public string[] refines, builds;

        public bool Refines(string id) => Has(refines, id);
        public bool Builds(string id) => Has(builds, id);
        public bool CanRefine => refines != null && refines.Length > 0;
        public bool CanBuild => builds != null && builds.Length > 0;
        /// <summary>Parts are fitted where they are built.</summary>
        public bool CanFit => CanBuild;
        /// <summary>A pad on the ground (a planet or moon), not a berth in space.</summary>
        public bool OnGround => where == "surface" || where == "moon";

        static bool Has(string[] list, string id)
        {
            if (list == null) return false;
            foreach (var s in list) if (s == "*" || s == id) return true;
            return false;
        }
    }

    /// <summary>What kind of thing an item is.</summary>
    public enum ItemKind { None, Raw, Refined, Part }

    /// <summary>
    /// Everything the industry knows (Resources/Data/industry.json): the refined materials and
    /// how they're refined from minerals, the ship parts and what they're built from and do, and
    /// the industrial sites. Items are named by id: a mineral by its Mineral name ("Iron"), a
    /// refined material or a part by its id. Raw and refined items are counted in tonnes, parts
    /// one by one (each part weighs its `mass`).
    /// </summary>
    [Serializable]
    public class IndustryTable
    {
        public RefinedSpec[] refined;
        public PartSpec[] parts;
        public SiteSpec[] sites;

        public const string Resource = "Data/industry";
        /// <summary>The numbers a part can change on a frigate.</summary>
        public static readonly string[] Stats = { "hull", "shield", "recharge", "speed", "acceleration", "turn", "cargo",
                                                  "gunDamage", "gunRange", "gunRate", "beamDamage", "beamCharge", "miningRate" };

        static IndustryTable loaded;
        [NonSerialized] Dictionary<string, RefinedSpec> refinedById;
        [NonSerialized] Dictionary<string, PartSpec> partsById;
        static readonly Dictionary<string, Mineral> MineralsByName = new Dictionary<string, Mineral>();

        /// <summary>The table, read the first time it is asked for.</summary>
        public static IndustryTable Get()
        {
            if (loaded != null) return loaded;
            var text = Resources.Load<TextAsset>(Resource);
            if (text == null) throw new InvalidOperationException($"Resources/{Resource}.json is missing");
            return loaded = Parse(text.text);
        }

        public static IndustryTable Parse(string json)
        {
            var t = JsonUtility.FromJson<IndustryTable>(json);
            t.refined ??= new RefinedSpec[0];
            t.parts ??= new PartSpec[0];
            t.sites ??= new SiteSpec[0];
            t.refinedById = new Dictionary<string, RefinedSpec>();
            foreach (var r in t.refined) t.refinedById[r.id] = r;
            t.partsById = new Dictionary<string, PartSpec>();
            foreach (var p in t.parts) t.partsById[p.id] = p;
            return t;
        }

        public RefinedSpec Refined(string id) => id != null && refinedById.TryGetValue(id, out var r) ? r : null;
        public PartSpec Part(string id) => id != null && partsById.TryGetValue(id, out var p) ? p : null;

        public SiteSpec Site(string name)
        {
            foreach (var s in sites) if (s.name == name) return s;
            return null;
        }

        /// <summary>A mineral by its name ("Iron"), if `id` is one.</summary>
        public static bool AsMineral(string id, out Mineral m)
        {
            if (MineralsByName.Count == 0)
                for (int i = 0; i < Minerals.Count; i++) MineralsByName[((Mineral)i).ToString()] = (Mineral)i;
            m = default;
            return id != null && MineralsByName.TryGetValue(id, out m);
        }

        public ItemKind Kind(string id)
        {
            if (AsMineral(id, out _)) return ItemKind.Raw;
            if (Refined(id) != null) return ItemKind.Refined;
            if (Part(id) != null) return ItemKind.Part;
            return ItemKind.None;
        }

        public string Name(string id)
        {
            if (AsMineral(id, out var m)) return Minerals.Name(m);
            return Refined(id)?.name ?? Part(id)?.name ?? id;
        }

        /// <summary>Credits a tonne (raw, refined) or a part.</summary>
        public float Value(string id)
        {
            if (AsMineral(id, out var m)) return Minerals.Value(m);
            var r = Refined(id);
            if (r != null) return r.value;
            return Part(id)?.value ?? 0f;
        }

        /// <summary>Tonnes of `amount` of an item (parts weigh their mass each).</summary>
        public float Mass(string id, float amount)
        {
            var p = Part(id);
            return p != null ? amount * p.mass : amount;
        }

        /// <summary>A short description: a refined material's form, a part's look.</summary>
        public string Describe(string id)
        {
            if (AsMineral(id, out var m)) return $"Raw {Minerals.Name(m).ToLower()}, as mined";
            return Refined(id)?.form ?? Part(id)?.look ?? "";
        }

        /// <summary>"12.5 t" or "3 parts".</summary>
        public string Amount(string id, float amount)
        {
            if (Part(id) != null) { int n = Mathf.RoundToInt(amount); return n == 1 ? "1 part" : $"{n} parts"; }
            return amount >= 100f ? $"{amount:F0} t" : amount >= 1f ? $"{amount:0.#} t" : $"{amount:0.##} t";
        }

        static readonly Dictionary<string, Texture2D> Icons = new Dictionary<string, Texture2D>();

        /// <summary>An item's picture (Resources/Industry/Icons/&lt;id&gt;.png), or null (a raw mineral has none).</summary>
        public static Texture2D Icon(string id)
        {
            if (id == null) return null;
            if (Icons.TryGetValue(id, out var t)) return t;
            return Icons[id] = Resources.Load<Texture2D>("Industry/Icons/" + id);
        }
    }
}

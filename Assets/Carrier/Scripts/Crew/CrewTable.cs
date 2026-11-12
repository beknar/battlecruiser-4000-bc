using System;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>A place's people: its crew, and (where it recruits) its pool of recruits.</summary>
    [Serializable]
    public class CrewPlace
    {
        public string name, kind;
        public int crew = -1, recruits;
        public float refillPerMinute;

        public bool Automated => crew == 0;
        public bool Recruits => recruits > 0;
    }

    /// <summary>
    /// The crew rules and every place's people (Resources/Data/crew.json): what hull damage costs in
    /// crew, how a short crew slows a ship or an installation, what a recruit costs, and where.
    /// </summary>
    [Serializable]
    public class CrewTable
    {
        public float recruitCost = 250f, threshold = 0.9f, slope = 1.2f, minimumEfficiency = 0.3f, casualties = 0.5f, survivors = 0.15f;
        public int defaultCrew = 60;
        public CrewPlace[] places;

        public const string Resource = "Data/crew";
        static CrewTable loaded;

        /// <summary>The table, read the first time it is asked for.</summary>
        public static CrewTable Get()
        {
            if (loaded != null) return loaded;
            var text = Resources.Load<TextAsset>(Resource);
            if (text == null) throw new InvalidOperationException($"Resources/{Resource}.json is missing");
            return loaded = Parse(text.text);
        }

        public static CrewTable Parse(string json)
        {
            var t = JsonUtility.FromJson<CrewTable>(json);
            t.places ??= new CrewPlace[0];
            return t;
        }

        public CrewPlace Place(string name)
        {
            foreach (var p in places) if (p.name == name) return p;
            return null;
        }

        /// <summary>The crew a place has (its own, or the default).</summary>
        public int CrewOf(string name)
        {
            var p = Place(name);
            return p != null && p.crew >= 0 ? p.crew : defaultCrew;
        }

        /// <summary>How well a ship or an installation works with this share of its complement
        /// aboard: 1 down to `threshold`, then less, to `minimumEfficiency`.</summary>
        public float Efficiency(float share)
        {
            share = Mathf.Clamp01(share);
            if (share >= threshold) return 1f;
            return Mathf.Max(minimumEfficiency, 1f - (threshold - share) * slope);
        }
    }
}

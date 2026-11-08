using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>The ship's captain's faces: what she is feeling, shown on the HUD portrait.</summary>
    public enum Mood { Neutral, Blink, Talk, Smile, Determined, Alarmed, Pain, Focused, Awe, Sad }

    /// <summary>
    /// The five captains the player can choose from (the pause menu), and their portraits: one
    /// picture per mood, in Resources/Captains as "&lt;id&gt;_&lt;mood&gt;". The choice is kept
    /// between runs (PlayerPrefs). The portraits were generated with a local InvokeAI server: a
    /// base portrait from Juggernaut XL, and each expression an edit of it with FLUX.1 Kontext,
    /// so the face stays the same from one mood to the next.
    /// </summary>
    public static class Captain
    {
        public struct Info
        {
            public string id, name, rank;
            public Info(string id, string name, string rank) { this.id = id; this.name = name; this.rank = rank; }
        }

        public static readonly Info[] Roster =
        {
            new Info("vasquez", "Elena Vasquez", "Captain"),
            new Info("okafor", "Amara Okafor", "Captain"),
            new Info("tanaka", "Yuki Tanaka", "Captain"),
            new Info("lindqvist", "Freya Lindqvist", "Captain"),
            new Info("raman", "Priya Raman", "Captain"),
        };

        const string PrefKey = "captain";
        static int current = -1;

        /// <summary>Raised when the player picks another captain.</summary>
        public static event Action Changed;

        /// <summary>The chosen captain (0 to 4).</summary>
        public static int Index
        {
            get
            {
                if (current < 0)
                {
                    int saved = 0;
                    try { saved = PlayerPrefs.GetInt(PrefKey, 0); } catch { }
                    current = Mathf.Clamp(saved, 0, Roster.Length - 1);
                }
                return current;
            }
            set
            {
                int v = ((value % Roster.Length) + Roster.Length) % Roster.Length;
                if (v == Index) return;
                current = v;
                try { PlayerPrefs.SetInt(PrefKey, v); PlayerPrefs.Save(); } catch { }
                Changed?.Invoke();
            }
        }

        public static Info Current => Roster[Index];

        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        /// <summary>A captain's picture for a mood; the calm one if that mood is missing, or null.</summary>
        public static Texture2D Face(int captain, Mood mood)
        {
            var id = Roster[Mathf.Clamp(captain, 0, Roster.Length - 1)].id;
            string key = $"{id}_{mood.ToString().ToLowerInvariant()}";
            if (!cache.TryGetValue(key, out var tex))
            {
                tex = Resources.Load<Texture2D>("Captains/" + key);
                cache[key] = tex;
            }
            if (tex == null && mood != Mood.Neutral) return Face(captain, Mood.Neutral);
            return tex;
        }

        /// <summary>How many of the portraits are there (for the self-test).</summary>
        public static int FacesFound(int captain)
        {
            int n = 0;
            foreach (Mood m in Enum.GetValues(typeof(Mood)))
            {
                var id = Roster[captain].id;
                string key = $"{id}_{m.ToString().ToLowerInvariant()}";
                if (!cache.TryGetValue(key, out var tex)) cache[key] = tex = Resources.Load<Texture2D>("Captains/" + key);
                if (tex != null) n++;
            }
            return n;
        }
    }
}

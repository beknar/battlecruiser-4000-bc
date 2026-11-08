using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>The ship's captain's faces: what she is feeling, shown on the HUD portrait.</summary>
    public enum Mood { Neutral, Blink, Talk, Smile, Determined, Alarmed, Pain, Focused, Awe, Sad }

    /// <summary>
    /// The five captains the player can choose from (the pause menu), and their portraits, in
    /// Resources/Captains: "&lt;id&gt;_neutral", and each other mood in steps of strength,
    /// "&lt;id&gt;_&lt;mood&gt;_1" to "_3" (a hint of it, clearly it, all of it; blinking and
    /// talking have two: half and full). The portrait animates through the steps, so a smile
    /// grows rather than appearing. The choice is kept between runs (PlayerPrefs). The portraits
    /// were generated with a local InvokeAI server: a base portrait from Juggernaut XL, and each
    /// step an edit of it with FLUX.1 Kontext, so the face stays the same; then each picture was
    /// lined up with the base, so only the expression moves.
    /// </summary>
    public static class Captain
    {
        public struct Info
        {
            public string id, name, rank;
            /// <summary>Where her eyes and mouth are in her pictures (0 to 1, from the top left):
            /// a blink or a word over a strong expression changes only these.</summary>
            public Rect eyes, mouth;
            public Info(string id, string name, string rank, Rect eyes, Rect mouth)
            { this.id = id; this.name = name; this.rank = rank; this.eyes = eyes; this.mouth = mouth; }
        }

        public static readonly Info[] Roster =
        {
            new Info("vasquez", "Elena Vasquez", "Captain", new Rect(0.270f, 0.277f, 0.405f, 0.174f), new Rect(0.327f, 0.470f, 0.289f, 0.183f)),
            new Info("okafor", "Amara Okafor", "Captain", new Rect(0.222f, 0.281f, 0.397f, 0.170f), new Rect(0.279f, 0.470f, 0.283f, 0.179f)),
            new Info("tanaka", "Yuki Tanaka", "Captain", new Rect(0.174f, 0.154f, 0.677f, 0.290f), new Rect(0.271f, 0.477f, 0.483f, 0.306f)),
            new Info("lindqvist", "Freya Lindqvist", "Captain", new Rect(0.170f, 0.244f, 0.480f, 0.206f), new Rect(0.238f, 0.472f, 0.343f, 0.217f)),
            new Info("raman", "Priya Raman", "Captain", new Rect(0.323f, 0.295f, 0.366f, 0.157f), new Rect(0.375f, 0.469f, 0.261f, 0.165f)),
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

        /// <summary>How many steps a mood has (0 for the calm face, which is the start of them all).</summary>
        public static int Steps(Mood mood) => mood == Mood.Neutral ? 0 : mood == Mood.Blink || mood == Mood.Talk ? 2 : 3;

        /// <summary>A captain's picture for a mood at a step, exactly as stored (null if missing).</summary>
        static Texture2D Load(int captain, Mood mood, int step)
        {
            var id = Roster[Mathf.Clamp(captain, 0, Roster.Length - 1)].id;
            string key = mood == Mood.Neutral || step <= 0 ? $"{id}_neutral" : $"{id}_{mood.ToString().ToLowerInvariant()}_{step}";
            if (!cache.TryGetValue(key, out var tex)) cache[key] = tex = Resources.Load<Texture2D>("Captains/" + key);
            return tex;
        }

        /// <summary>A captain's picture for a mood at a step (0 = calm; past the last step, the
        /// last), falling back to a weaker step, then the calm face, if one is missing.</summary>
        public static Texture2D Face(int captain, Mood mood, int step)
        {
            for (int k = Mathf.Min(step, Steps(mood)); k > 0; k--)
            {
                var tex = Load(captain, mood, k);
                if (tex != null) return tex;
            }
            return Load(captain, Mood.Neutral, 0);
        }

        /// <summary>A captain's picture for a mood, at its full strength.</summary>
        public static Texture2D Face(int captain, Mood mood) => Face(captain, mood, Steps(mood));

        /// <summary>How many pictures a captain should have, and how many there are (for the self-test).</summary>
        public static int FacesWanted { get { int n = 1; foreach (Mood m in Enum.GetValues(typeof(Mood))) n += Steps(m); return n; } }
        public static int FacesFound(int captain)
        {
            int n = Load(captain, Mood.Neutral, 0) != null ? 1 : 0;
            foreach (Mood m in Enum.GetValues(typeof(Mood)))
                for (int k = 1; k <= Steps(m); k++) if (Load(captain, m, k) != null) n++;
            return n;
        }
    }
}

using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The API key the game joins a co-op server with (a server needs one unless it is open; its
    /// owner makes them: CoopServer --new-key &lt;name&gt;). It is typed into the co-op panel and
    /// kept for next time: on Windows one key, in the web version one for each player name (a
    /// browser may be shared). Given at launch it wins: -apikey &lt;key&gt; on the command line
    /// (as an AI player is started), or ?apikey=&lt;key&gt; in the page address. Tests, reels and
    /// AI players never keep the key they were given.
    /// </summary>
    public static class CoopKey
    {
        const string Pref = "coop.apikey";

        /// <summary>Given at launch (null if not).</summary>
        public static string Given
        {
            get
            {
                string k = LaunchArgs.ValueOf("-apikey") ?? LaunchArgs.PageOption("apikey");
                return string.IsNullOrWhiteSpace(k) ? null : k.Trim();
            }
        }

        /// <summary>Kept per player name (the web version), or one for the game (Windows).</summary>
        public static bool PerName => LaunchArgs.OnWeb;

        static string KeyFor(string name) => PerName ? $"{Pref}.{(name ?? "").Trim().ToLowerInvariant()}" : Pref;

        /// <summary>The key to join with as `name`: the one given at launch, else the one kept.</summary>
        public static string For(string name)
        {
            string given = Given;
            if (given != null) return given;
            try { return PlayerPrefs.GetString(KeyFor(name), ""); } catch { return ""; }
        }

        /// <summary>Keep the key used to join as `name` (not one given at launch, nor in an automated run).</summary>
        public static void Keep(string name, string key)
        {
            if (LaunchArgs.Automated || Given != null) return;
            key = (key ?? "").Trim();
            try
            {
                if (key.Length == 0) PlayerPrefs.DeleteKey(KeyFor(name));
                else PlayerPrefs.SetString(KeyFor(name), key);
                PlayerPrefs.Save();
            }
            catch { }
        }

        /// <summary>A key as it is shown on screen: its first characters, the rest hidden.</summary>
        public static string Masked(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return "";
            return key.Length <= 9 ? new string('•', key.Length) : key.Substring(0, 9) + new string('•', Mathf.Min(12, key.Length - 9));
        }
    }
}

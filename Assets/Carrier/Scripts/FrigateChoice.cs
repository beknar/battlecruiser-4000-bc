using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Which frigate the player flies: chosen on the start menu and remembered between runs, or
    /// given on the command line (-frigate Lancer) or the page address (?frigate=Lancer). The
    /// self-tests, the autotest and the reels fly the starting frigate unless told otherwise, so
    /// what a player last chose never changes them.
    /// </summary>
    public static class FrigateChoice
    {
        const string Pref = "frigate";

        /// <summary>The player has been through the start menu this run (it isn't shown again).</summary>
        public static bool Chosen { get; set; }

        /// <summary>Chosen on the start menu this run: it wins over one given at launch.</summary>
        static string picked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Chosen = false; picked = null; }

        /// <summary>Given on the command line or the page address (null if not).</summary>
        public static string Given => LaunchArgs.ValueOf("-frigate") ?? LaunchArgs.PageOption("frigate");

        /// <summary>The class to fly ("" for the roster's first, the starting frigate).</summary>
        public static string Current
        {
            get
            {
                if (picked != null) return picked;
                string given = Given;
                if (!string.IsNullOrEmpty(given)) return given;
                if (LaunchArgs.Automated) return "";
                try { return PlayerPrefs.GetString(Pref, ""); } catch { return ""; }
            }
        }

        /// <summary>Remember the player's choice (and fly it from now on this run, whatever was
        /// given at launch).</summary>
        public static void Set(string id)
        {
            picked = id ?? "";
            try { PlayerPrefs.SetString(Pref, id ?? ""); PlayerPrefs.Save(); } catch { }
        }
    }
}

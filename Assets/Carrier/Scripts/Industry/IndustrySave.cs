using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The game saved to disk (JSON): every industrial site's storage and queues, the frigate's
    /// hold and credits, the parts each frigate class has fitted, and the ships built. It is written now and then
    /// while playing and on quitting (IndustryDirector), and read once when the game starts.
    /// The file is industry_save.json in the player's data folder; `-industrysave &lt;file&gt;` uses
    /// another, and `-newgame` starts without it. Automated runs (tests, reels) only save where
    /// they are told to, so they never touch the player's own save.
    /// </summary>
    public static class IndustrySave
    {
        public const int Version = 1;

        [Serializable]
        public class Fitting
        {
            public string frigate;
            public List<string> parts = new List<string>();
        }

        /// <summary>A number by name (a recruit pool, a frigate class's crew).</summary>
        [Serializable]
        public class Named
        {
            public string name;
            public float value;
        }

        [Serializable]
        public class Data
        {
            public int version = Version;
            public string savedAt;
            public List<SiteState> sites = new List<SiteState>();
            public List<Stock.Entry> hold = new List<Stock.Entry>();
            public float credits;
            public List<Fitting> fittings = new List<Fitting>();
            public List<Named> pools = new List<Named>();
            public List<Named> crews = new List<Named>();
            /// <summary>The world's clock and memory (WorldState).</summary>
            public WorldState.Saved world = new WorldState.Saved();
            /// <summary>The ships built: berthed at their shipyards, and the one commanded (none
            /// when flying a frigate).</summary>
            public List<BuiltShip> berthed = new List<BuiltShip>();
            public BuiltShip commanded;
        }

        /// <summary>A file a self-test saves to instead (its own folder).</summary>
        public static string TestPath;

        /// <summary>Where the game is saved (null: not saved, as in a test run told nothing).</summary>
        public static string FilePath
        {
            get
            {
                if (!string.IsNullOrEmpty(TestPath)) return TestPath;
                string chosen = LaunchArgs.ValueOf("-industrysave");
                if (!string.IsNullOrEmpty(chosen)) return chosen;
                // (a new game, or a test, never writes over the player's own save)
                if (LaunchArgs.Automated || NewGame) return null;
                return Path.Combine(Application.persistentDataPath, "industry_save.json");
            }
        }

        public static bool NewGame => LaunchArgs.Has("-newgame");

        /// <summary>The last thing written or read (for the self-test).</summary>
        public static string LastPath { get; private set; }
        public static int Saves { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { LastPath = null; Saves = 0; TestPath = null; }

        public static string ToJson(Data d) => JsonUtility.ToJson(d, true);

        public static Data FromJson(string json)
        {
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return null;
            d.sites ??= new List<SiteState>();
            d.hold ??= new List<Stock.Entry>();
            d.fittings ??= new List<Fitting>();
            d.pools ??= new List<Named>();
            d.crews ??= new List<Named>();
            d.world ??= new WorldState.Saved();
            return d;
        }

        /// <summary>Write the save: returns false (and logs why) if it couldn't.</summary>
        public static bool Write(Data d, string path = null)
        {
            path ??= FilePath;
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                d.version = Version;
                d.savedAt = DateTime.UtcNow.ToString("u");
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                // (written beside it first, so a crash mid-write can't leave half a save)
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, ToJson(d));
                if (!File.Exists(path)) File.Move(tmp, path);
                else
                {
                    try { File.Replace(tmp, path, null); }
                    catch (Exception)
                    {
                        // (where the file system can't replace in one step: Read falls back to the
                        // .tmp if it is caught between the two)
                        File.Delete(path);
                        File.Move(tmp, path);
                    }
                }
                LastPath = path;
                Saves++;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[IndustrySave] couldn't save to {path}: {e.Message}");
                return false;
            }
        }

        /// <summary>Read the save (null if there is none, or it can't be read).</summary>
        public static Data Read(string path = null)
        {
            path ??= FilePath;
            if (string.IsNullOrEmpty(path)) return null;
            // (a save caught half-way through being written is still in its .tmp)
            string from = File.Exists(path) ? path : File.Exists(path + ".tmp") ? path + ".tmp" : null;
            if (from == null) return null;
            try
            {
                var d = FromJson(File.ReadAllText(from));
                LastPath = path;
                return d;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[IndustrySave] couldn't read {path}: {e.Message}");
                return null;
            }
        }
    }
}

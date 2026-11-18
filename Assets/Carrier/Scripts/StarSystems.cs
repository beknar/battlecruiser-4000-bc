using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarrierGame
{
    /// <summary>
    /// The star systems a game can be played in, each its own scene: the Testing System (the
    /// first one built, round the star Kestrel) and the Nine Hells. The start menu offers them;
    /// the choice is kept (PlayerPrefs "system"), and -system &lt;id&gt; chooses one from the command
    /// line (an automated run is taken straight there). Each system keeps its own save.
    /// </summary>
    public static class StarSystems
    {
        public class Info
        {
            public string id, name, scene, about;
        }

        public static readonly Info[] All =
        {
            new Info
            {
                id = "testing", name = "Testing System", scene = "StarSystem",
                about = "The Kestrel system: six worlds and their moons, three asteroid fields, two comets, the corsairs and the Vrask. Start over Thalassa.",
            },
            new Info
            {
                id = "ninehells", name = "Nine Hells", scene = "NineHells",
                about = "Nine worlds round a hard white star, pirates on Judecca and Gilgal, the Vrask out on Sheol. Start at Heaven, the shielded capital of K'rzyn.",
            },
        };

        const string Pref = "system";

        public static Info Find(string idOrName)
        {
            if (string.IsNullOrEmpty(idOrName)) return null;
            string k = idOrName.Replace(" ", "").Replace("'", "").ToLowerInvariant();
            foreach (var s in All)
                if (s.id == k || s.name.Replace(" ", "").ToLowerInvariant() == k || s.scene.ToLowerInvariant() == k) return s;
            return null;
        }

        /// <summary>The system this level is (by its scene), or the first.</summary>
        public static Info Current
        {
            get
            {
                string scene = SceneManager.GetActiveScene().name;
                foreach (var s in All) if (s.scene == scene) return s;
                return All[0];
            }
        }

        /// <summary>The system asked for on the command line (-system), or null.</summary>
        public static Info Given => Find(LaunchArgs.ValueOf("-system"));

        /// <summary>The system chosen (the command line's, else the one kept, else this level's).</summary>
        public static Info Chosen
        {
            get
            {
                var g = Given;
                if (g != null) return g;
                try { var kept = Find(PlayerPrefs.GetString(Pref, "")); if (kept != null) return kept; } catch { }
                return Current;
            }
        }

        public static void Keep(Info s)
        {
            if (s == null) return;
            try { PlayerPrefs.SetString(Pref, s.id); PlayerPrefs.Save(); } catch { }
        }

        /// <summary>The system's scene is in this build.</summary>
        public static bool Available(Info s) => s != null && Application.CanStreamedLevelBeLoaded(s.scene);

        /// <summary>Open a system's scene (false if it isn't this build's, or it is this level already).</summary>
        public static bool Open(Info s)
        {
            if (!Available(s) || s.scene == SceneManager.GetActiveScene().name) return false;
            Debug.Log($"[StarSystems] opening {s.name} ({s.scene})");
            // (this system saved and forgotten: the next reads its own save)
            IndustryDirector.LeaveSystem();
            SceneManager.LoadScene(s.scene);
            return true;
        }

        /// <summary>What a system's save file is called (the Testing System's is the one it always was).</summary>
        public static string SaveName(Info s) => s == null || s.id == "testing" ? "industry_save.json" : $"industry_save_{s.id}.json";

        /// <summary>This level is about to give way to the one -system asks for (an automated run
        /// told another system): what would start in it waits for that one (WhenThere).</summary>
        public static bool Leaving
        {
            get
            {
                var g = Given;
                return g != null && g.scene != SceneManager.GetActiveScene().name && Available(g);
            }
        }

        /// <summary>Do `then` once this level is the system asked for: now if it is, else as that
        /// system's scene has loaded (a harness that starts as a level opens).</summary>
        public static void WhenThere(System.Action then)
        {
            if (!Leaving) { then(); return; }
            void Loaded(Scene s, LoadSceneMode m)
            {
                if (Given == null || s.name != Given.scene) return;
                SceneManager.sceneLoaded -= Loaded;
                then();
            }
            SceneManager.sceneLoaded += Loaded;
        }

        // an automated run told -system goes straight there (a player gets the start menu's choice)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var g = Given;
            if (g != null && g.scene != SceneManager.GetActiveScene().name && Available(g)) Open(g);
        }
    }
}

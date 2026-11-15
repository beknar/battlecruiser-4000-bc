using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// How the HUD is set up, in the pause menu, kept for next time: the captain's portrait on
    /// the console shown or not; the HUD's size (everything drawn on screen, the same as the
    /// +/- keys: smaller to see more of the game); and the fleet's ships' small panels shown or
    /// not. Tests, reels and AI players use the defaults and keep nothing.
    /// </summary>
    public static class HudSettings
    {
        const string PrefCaptain = "hud.captain", PrefSize = "hud.size", PrefFleet = "hud.fleet";

        static bool loaded;
        static bool showCaptain = true, fleetPanels = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { loaded = false; showCaptain = true; fleetPanels = true; }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            if (LaunchArgs.Automated) return;
            try
            {
                showCaptain = PlayerPrefs.GetInt(PrefCaptain, 1) != 0;
                fleetPanels = PlayerPrefs.GetInt(PrefFleet, 1) != 0;
                float size = PlayerPrefs.GetFloat(PrefSize, 1f);
                if (size >= 0.5f && size <= 3f) Hud.UserScale = size;
            }
            catch { }
        }

        static void Save()
        {
            if (LaunchArgs.Automated) return;
            try
            {
                PlayerPrefs.SetInt(PrefCaptain, showCaptain ? 1 : 0);
                PlayerPrefs.SetInt(PrefFleet, fleetPanels ? 1 : 0);
                PlayerPrefs.SetFloat(PrefSize, Hud.UserScale);
                PlayerPrefs.Save();
            }
            catch { }
        }

        /// <summary>Read what was kept (as the level starts).</summary>
        public static void Begin() => Load();

        public static bool ShowCaptain
        {
            get { Load(); return showCaptain; }
            set { Load(); showCaptain = value; Save(); }
        }

        public static bool FleetPanels
        {
            get { Load(); return fleetPanels; }
            set { Load(); fleetPanels = value; Save(); }
        }

        /// <summary>The HUD's size, as a share of its own (Hud.UserScale).</summary>
        public static float Size => Hud.UserScale;

        /// <summary>A step bigger (+1) or smaller (-1): the +/- keys and the pause menu.</summary>
        public static void ChangeSize(int dir)
        {
            Load();
            Hud.ChangeTextSize(dir);
            Save();
        }
    }
}

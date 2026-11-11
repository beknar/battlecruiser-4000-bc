using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// What a level needs around the player's controls: the HUD, co-op (idle until connected)
    /// and the panel to connect with, the pause menu, the frame rate, the window, and the
    /// gamepad (set up, and its rumble stopped whenever the game loses focus).
    /// </summary>
    public class GameSession : MonoBehaviour
    {
        /// <summary>Set the level up around `demo` (on its object); returns its HUD.</summary>
        public static ShipHud Begin(CarrierDemoInput demo)
        {
            var go = demo.gameObject;
            var hud = ShipHud.Build(demo);
            go.AddComponent<CoopWorld>().demo = demo;
            go.AddComponent<CoopMenu>().demo = demo;
            go.AddComponent<GameSession>();
            return hud;
        }

        void Start()
        {
            // no point rendering faster than the screen refreshes; keeps the laptop GPU cool
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = 120;
            StartCoroutine(FixDisplayWhenReady());
            PadSupport.Init();
            if (!TryGetComponent(out GameMenu _)) gameObject.AddComponent<GameMenu>();
#if UNITY_WEBGL && !UNITY_EDITOR
            // tell the page which graphics API it got: WebGPU, or the WebGL 2 fallback
            CarrierPageGraphics(SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.WebGPU ? "WebGPU" : "WebGL 2");
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void CarrierPageGraphics(string name);   // Plugins/WebGL/CarrierPage.jslib
#endif

        void OnDestroy() => PadSupport.Shutdown();
        void OnApplicationFocus(bool focus) { if (!focus) PadSupport.StopRumble(); }
        void OnApplicationPause(bool paused) { if (paused) PadSupport.StopRumble(); }

        /// <summary>Standalone builds run borderless full screen at the monitor's native resolution.
        /// If an earlier run left a different size in the registry, fix it once the game is up and
        /// running (never during startup), and only when it actually differs. A size given on the
        /// command line (-screen-fullscreen / -screen-width) is left alone.</summary>
        System.Collections.IEnumerator FixDisplayWhenReady()
        {
            if (Application.isEditor || LaunchArgs.OnWeb || LaunchArgs.Has("-screen-fullscreen") || LaunchArgs.Has("-screen-width") || LaunchArgs.Has("-autotest")) yield break;
            yield return new WaitForSecondsRealtime(1f);
            int w = Display.main.systemWidth, h = Display.main.systemHeight;
            if (Screen.fullScreenMode == FullScreenMode.FullScreenWindow && Screen.width == w && Screen.height == h) yield break;
            Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
        }

        /// <summary>Between borderless full screen and a window of 80% of the screen.</summary>
        public static void ToggleFullScreen()
        {
            int w = Display.main.systemWidth, h = Display.main.systemHeight;
            if (Screen.fullScreenMode == FullScreenMode.Windowed)
                Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
            else
                Screen.SetResolution(Mathf.RoundToInt(w * 0.8f), Mathf.RoundToInt(h * 0.8f), FullScreenMode.Windowed);
        }
    }
}

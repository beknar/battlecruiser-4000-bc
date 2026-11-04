using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The pause menu: Esc on the keyboard, Start on the pad. The game stops while it is open.
    /// Resume, Restart (the level from the beginning), Sound on/off, and Quit. Restart and Quit
    /// ask for a second press to make sure. Mouse (hover and click), keyboard (up/down or W/S,
    /// Enter or Space, Esc to resume) and pad (D-pad or left stick, A, B or Start to resume).
    /// In a web page, Quit shuts the game down and the page offers to start it again.
    /// </summary>
    public class GameMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        static int closedFrame = -10;
        int openedFrame = -10;
        /// <summary>Closed this frame or the last (so the key that closed it isn't also read by the game).</summary>
        public static bool JustClosed => Time.frameCount - closedFrame <= 1;

        enum Item { Resume, Restart, Sound, Quit }
        static readonly Item[] Items = { Item.Resume, Item.Restart, Item.Sound, Item.Quit };

        int selected;
        Item? confirming;
        float confirmUntil;
        float stickRepeat;
        Rect[] itemRects = new Rect[Items.Length];
        float savedTimeScale = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { IsOpen = false; closedFrame = -10; }

        public void Open()
        {
            if (IsOpen || SystemMap.IsOpen) return;
            IsOpen = true;
            openedFrame = Time.frameCount;
            selected = 0;
            confirming = null;
            savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            PadSupport.StopRumble();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            closedFrame = Time.frameCount;
            Time.timeScale = savedTimeScale;
            AudioListener.pause = false;
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        void OnDestroy()
        {
            // leaving the scene with the menu up (restart): don't leave the game paused
            if (!IsOpen) return;
            IsOpen = false;
            Time.timeScale = savedTimeScale;
            AudioListener.pause = false;
        }

        string Label(Item it)
        {
            bool sure = confirming == it && Time.unscaledTime < confirmUntil;
            switch (it)
            {
                case Item.Resume: return "Resume";
                case Item.Restart: return sure ? "Restart: press again to confirm" : "Restart";
                case Item.Sound: return SoundFX.Muted ? "Sound: off" : "Sound: on";
                default: return sure ? "Quit: press again to confirm" : "Quit";
            }
        }

        void Activate(int index)
        {
            selected = index;
            var it = Items[index];
            if ((it == Item.Restart || it == Item.Quit) && !(confirming == it && Time.unscaledTime < confirmUntil))
            {
                confirming = it;
                confirmUntil = Time.unscaledTime + 3f;
                return;
            }
            confirming = null;
            switch (it)
            {
                case Item.Resume: Close(); break;
                case Item.Sound: SoundFX.Muted = !SoundFX.Muted; break;
                case Item.Restart: Restart(); break;
                case Item.Quit: Quit(); break;
            }
        }

        public void Restart()
        {
            Close();
            SoundFX.LevelRestart();
            var scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            // in the editor the scene needn't be in the build list
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scene.buildIndex);   // each game is built with just its own scene
#endif
        }

        static void Quit()
        {
            PadSupport.StopRumble();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
            CarrierPageQuit();   // a web page can't close its own tab: the page shuts the game down
#else
            Application.Quit();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void CarrierPageQuit();   // Plugins/WebGL/CarrierPage.jslib
#endif

        void Move(int dir)
        {
            selected = (selected + dir + Items.Length) % Items.Length;
            confirming = null;
        }

        void Update()
        {
            // not on the frame it opened: the key that opened it (Esc, Start) would close it again
            if (!IsOpen || Time.frameCount == openedFrame) return;
            var k = Keyboard.current;
            var pad = Gamepad.current;
            if (k != null)
            {
                if (k.escapeKey.wasPressedThisFrame || k.pKey.wasPressedThisFrame) { Close(); return; }
                if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) Move(-1);
                if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) Move(+1);
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) { Activate(selected); return; }
            }
            if (pad != null)
            {
                if (pad.startButton.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame) { Close(); return; }
                if (pad.dpad.up.wasPressedThisFrame) Move(-1);
                if (pad.dpad.down.wasPressedThisFrame) Move(+1);
                // the left stick steps through the items, repeating while held
                float y = pad.leftStick.y.ReadValue();
                if (Mathf.Abs(y) < 0.4f) stickRepeat = 0f;
                else if (Time.unscaledTime >= stickRepeat)
                {
                    Move(y > 0f ? -1 : +1);
                    stickRepeat = Time.unscaledTime + (stickRepeat == 0f ? 0.35f : 0.18f);
                }
                if (pad.buttonSouth.wasPressedThisFrame) { Activate(selected); return; }
            }
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 mp = mouse.position.ReadValue();
                Vector2 gui = new Vector2(mp.x, Screen.height - mp.y);
                for (int i = 0; i < itemRects.Length; i++)
                    if (itemRects[i].Contains(gui))
                    {
                        if (mouse.delta.ReadValue().sqrMagnitude > 0.01f && selected != i) { selected = i; confirming = null; }
                        if (mouse.leftButton.wasPressedThisFrame) { Activate(i); return; }
                    }
            }
        }

        /// <summary>Where an item is on screen (for the self-test's mouse).</summary>
        public Vector2 ItemScreenPos(int index)
        {
            var r = itemRects[Mathf.Clamp(index, 0, itemRects.Length - 1)];
            return new Vector2(r.center.x, Screen.height - r.center.y);
        }

        public int Selected => selected;
        public string SelectedLabel => Label(Items[selected]);

        void OnGUI()
        {
            if (!IsOpen) return;
            GUI.depth = -100;   // over the HUD panels
            float s = Hud.S;
            Hud.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0.01f, 0.03f, 0.55f));
            float w = 440f * s, rowH = 46f * s, pad = 22f * s;
            float h = pad * 2f + 54f * s + rowH * Items.Length + 40f * s;
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            Hud.Fill(panel, new Color(0.03f, 0.05f, 0.1f, 0.95f));
            GUI.Label(new Rect(panel.x, panel.y + pad, w, 40f * s), "PAUSED", Hud.Text(26, true, TextAnchor.MiddleCenter));
            float y = panel.y + pad + 54f * s;
            for (int i = 0; i < Items.Length; i++)
            {
                var r = new Rect(panel.x + pad, y, w - pad * 2f, rowH - 6f * s);
                itemRects[i] = r;
                bool sel = i == selected;
                Hud.Fill(r, sel ? new Color(0.25f, 0.45f, 0.8f, 0.55f) : new Color(1f, 1f, 1f, 0.05f));
                var it = Items[i];
                string text = Label(it);
                if (confirming == it && Time.unscaledTime < confirmUntil) text = $"<color=#ffc04d>{text}</color>";
                GUI.Label(r, text, Hud.Text(19, sel, TextAnchor.MiddleCenter));
                y += rowH;
            }
            string hint = Gamepad.current != null ? "A select   B or Start resume" : "Enter select   Esc or P resume";
            GUI.Label(new Rect(panel.x, panel.yMax - pad - 22f * s, w, 22f * s), hint, Hud.Text(13, false, TextAnchor.MiddleCenter));
        }
    }
}

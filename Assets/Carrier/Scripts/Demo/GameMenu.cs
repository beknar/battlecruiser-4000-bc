using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The pause menu: Esc on the keyboard, Start on the pad. The game stops while it is open.
    /// Resume, Restart (the level from the beginning), Sound on/off, Captain (which of the five
    /// captains is on the HUD: left/right, Enter, or click her portrait), and Quit. Restart and Quit
    /// ask for a second press to make sure. Mouse (hover and click), keyboard (up/down or W/S,
    /// Enter or Space, Esc to resume) and pad (D-pad or left stick, A, B or Start to resume).
    /// In a web page, Quit shuts the game down and the page offers to start it again.
    /// </summary>
    public class GameMenu : MonoBehaviour
    {
        /// <summary>The pause menu is up, or the co-op panel or the start menu (which also take
        /// the keys and mouse): the game itself takes no input.</summary>
        public static bool IsOpen => open || InputFocus.Has(InputFocus.Panel.CoopPanel) || InputFocus.Has(InputFocus.Panel.StartMenu);
        static bool open => InputFocus.Has(InputFocus.Panel.PauseMenu);
        bool paused;
        int openedFrame = -10;
        /// <summary>Closed this frame or the last (so the key that closed it isn't also read by the game).</summary>
        public static bool JustClosed => InputFocus.JustReleased(InputFocus.Panel.PauseMenu) || InputFocus.JustReleased(InputFocus.Panel.StartMenu);

        // (no frigate here: the frigate is chosen at the start of the game, on the start menu,
        // and kept for the game: its crew, parts and hold go with it)
        enum Item { Resume, Restart, Sound, Captain, Coop, Quit }
        static readonly Item[] Items = { Item.Resume, Item.Restart, Item.Sound, Item.Captain, Item.Coop, Item.Quit };

        int selected;
        Item? confirming;
        float confirmUntil;
        float stickRepeat;
        Rect[] itemRects = new Rect[Items.Length];
        readonly Rect[] captainRects = new Rect[Demo.Captain.Roster.Length];
        float savedTimeScale = 1f;

        public void Open()
        {
            if (IsOpen || !InputFocus.Take(InputFocus.Panel.PauseMenu)) return;
            openedFrame = Time.frameCount;
            selected = 0;
            confirming = null;
            // in co-op the game goes on (the others are still playing)
            paused = !CoopClient.Active;
            if (paused)
            {
                savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
                AudioListener.pause = true;
            }
            PadSupport.StopRumble();
        }

        public void Close()
        {
            if (!open) return;
            InputFocus.Release(InputFocus.Panel.PauseMenu);
            if (paused) { Time.timeScale = savedTimeScale; AudioListener.pause = false; paused = false; }
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        void OnDestroy()
        {
            // leaving the scene with the menu up (restart): don't leave the game paused
            if (!open) return;
            InputFocus.Release(InputFocus.Panel.PauseMenu);
            if (paused) { Time.timeScale = savedTimeScale; AudioListener.pause = false; paused = false; }
        }

        string Label(Item it)
        {
            bool sure = confirming == it && Time.unscaledTime < confirmUntil;
            switch (it)
            {
                case Item.Resume: return "Resume";
                case Item.Restart: return sure ? "Restart: press again to confirm" : "Restart";
                case Item.Sound: return SoundFX.Muted ? "Sound: off" : "Sound: on";
                case Item.Captain: return $"Captain:  <   {Demo.Captain.Current.name}   >";
                case Item.Coop: return CoopClient.Active ? $"Co-op: {CoopClient.Instance.Players.Count} playing" : "Co-op: join a game";
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
                case Item.Captain: Demo.Captain.Index++; if (CoopClient.Active) CoopClient.Instance.SendCaptain(); break;
                case Item.Coop:
                    Close();
                    var coop = FindAnyObjectByType<CoopMenu>();
                    if (coop != null) coop.Open();
                    break;
                case Item.Restart: Restart(); break;
                case Item.Quit: Quit(); break;
            }
        }

        public void Restart() => ReloadLevel();

        /// <summary>Start the level again from the beginning.</summary>
        public static void ReloadLevel()
        {
            var menu = FindAnyObjectByType<GameMenu>();
            if (menu != null) menu.Close();   // (and the game unpaused)
            SoundFX.LevelRestart();
            SurfaceVeins.Forget();   // the rocks come back full with the scene; the veins too
            // free what this run left lying about before the level is opened again (the old
            // scene is collected again once it is closed: SceneMemory)
            SceneMemory.Collect("before restarting the level");
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
            if (!open || Time.frameCount == openedFrame) return;
            var k = Keyboard.current;
            var pad = Gamepad.current;
            if (k != null)
            {
                if (k.escapeKey.wasPressedThisFrame || k.pKey.wasPressedThisFrame) { Close(); return; }
                if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) Move(-1);
                if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) Move(+1);
                // left/right on the Captain row: the previous or next captain
                if (Items[selected] == Item.Captain)
                {
                    if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) Demo.Captain.Index--;
                    if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) Demo.Captain.Index++;
                }
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) { Activate(selected); return; }
            }
            if (pad != null)
            {
                if (pad.startButton.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame) { Close(); return; }
                if (pad.dpad.up.wasPressedThisFrame) Move(-1);
                if (pad.dpad.down.wasPressedThisFrame) Move(+1);
                if (Items[selected] == Item.Captain)
                {
                    if (pad.dpad.left.wasPressedThisFrame) Demo.Captain.Index--;
                    if (pad.dpad.right.wasPressedThisFrame) Demo.Captain.Index++;
                }
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
                // a click on a captain's portrait chooses her
                for (int i = 0; i < captainRects.Length; i++)
                    if (captainRects[i].Contains(gui) && mouse.leftButton.wasPressedThisFrame)
                    {
                        Demo.Captain.Index = i;
                        selected = System.Array.IndexOf(Items, Item.Captain);
                        return;
                    }
            }
        }

        /// <summary>Where an item is on screen (for the self-test's mouse).</summary>
        public Vector2 ItemScreenPos(int index)
        {
            var r = itemRects[Mathf.Clamp(index, 0, itemRects.Length - 1)];
            return new Vector2(r.center.x, Screen.height - r.center.y);
        }

        /// <summary>Where a captain's portrait is on screen (for the self-test's mouse).</summary>
        public Vector2 CaptainScreenPos(int index)
        {
            var r = captainRects[Mathf.Clamp(index, 0, captainRects.Length - 1)];
            return new Vector2(r.center.x, Screen.height - r.center.y);
        }

        public int Selected => selected;
        public string SelectedLabel => Label(Items[selected]);

        void OnGUI()
        {
            if (!open) return;
            GUI.depth = -100;   // over the HUD panels
            float s = Hud.S;
            Hud.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0.01f, 0.03f, 0.55f));
            int n = Demo.Captain.Roster.Length;
            float thumb = 78f * s, gap = 8f * s;
            float w = Mathf.Max(440f * s, n * thumb + (n - 1) * gap + 44f * s), rowH = 46f * s, pad = 22f * s;
            float h = pad * 2f + 54f * s + rowH * Items.Length + thumb + 30f * s + 40f * s;
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
            // the captains: click one to choose her
            float tx = panel.x + (w - (n * thumb + (n - 1) * gap)) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(tx + i * (thumb + gap), y + 4f * s, thumb, thumb);
                captainRects[i] = r;
                bool chosen = i == Demo.Captain.Index;
                var face = Demo.Captain.Face(i, Mood.Neutral);
                Hud.Fill(r, new Color(0f, 0f, 0f, 0.6f));
                if (face != null)
                {
                    var old = GUI.color;
                    if (!chosen) GUI.color = new Color(0.6f, 0.6f, 0.65f, 1f);
                    GUI.DrawTexture(r, face, ScaleMode.ScaleAndCrop);
                    GUI.color = old;
                }
                Hud.Outline(r, Mathf.Max(1f, (chosen ? 3f : 1f) * s), chosen ? new Color(1f, 0.72f, 0.3f) : new Color(1f, 1f, 1f, 0.2f));
            }
            GUI.Label(new Rect(panel.x, y + thumb + 8f * s, w, 20f * s), $"Capt. {Demo.Captain.Current.name}", Hud.Text(14, true, TextAnchor.MiddleCenter));
            string hint = Gamepad.current != null ? "A select   B or Start resume" : "Enter select   Esc or P resume";
            GUI.Label(new Rect(panel.x, panel.yMax - pad - 22f * s, w, 22f * s), hint, Hud.Text(13, false, TextAnchor.MiddleCenter));
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The pause menu: Esc on the keyboard, Start on the pad. The game stops while it is open.
    /// Resume, Restart (the level from the beginning), Sound on/off, Music on/off, the song playing
    /// (left/right, or click its &lt; or &gt; side, for the one before or the next), the HUD's settings (its size:
    /// left/right, or click the &lt; or &gt; side of the row), Paint the ship (the start menu's
    /// Customise step, for the ship being flown), and Quit. Restart and Quit ask for a
    /// second press to make sure. The captain, chosen on the start menu, is shown but can't be
    /// changed once the game has started. Mouse (hover and click), keyboard (up/down or W/S,
    /// Enter or Space, Esc to resume) and pad (D-pad or left stick, A, B or Start to resume).
    /// In a web page, Quit shuts the game down and the page offers to start it again.
    /// </summary>
    public class GameMenu : MonoBehaviour
    {
        /// <summary>The pause menu is up, or the co-op panel, the start menu or the comms line
        /// (CoopChat) being typed into (which also take the keys and mouse): the game itself takes no input.</summary>
        public static bool IsOpen => open || InputFocus.Has(InputFocus.Panel.CoopPanel) || InputFocus.Has(InputFocus.Panel.StartMenu)
                                     || InputFocus.Has(InputFocus.Panel.Chat);
        static bool open => InputFocus.Has(InputFocus.Panel.PauseMenu);
        bool paused;
        int openedFrame = -10;
        /// <summary>Closed this frame or the last (so the key that closed it isn't also read by the game).</summary>
        public static bool JustClosed => InputFocus.JustReleased(InputFocus.Panel.PauseMenu) || InputFocus.JustReleased(InputFocus.Panel.StartMenu)
                                         || InputFocus.JustReleased(InputFocus.Panel.Chat);

        // (no frigate here: the frigate is chosen at the start of the game, on the start menu,
        // and kept for the game: its crew, parts and hold go with it)
        // (nor the captain: she is chosen on the start menu too, and stays for the game)
        // (the ship's paint can be changed, though: Paint opens the start menu's Customise step for it)
        enum Item { Resume, Restart, Sound, Music, Song, Coop, Paint, HudCaptain, HudSize, HudFleet, Quit }
        static readonly Item[] Items = { Item.Resume, Item.Restart, Item.Sound, Item.Music, Item.Song, Item.Coop, Item.Paint, Item.HudCaptain, Item.HudSize, Item.HudFleet, Item.Quit };

        int selected;
        Item? confirming;
        float confirmUntil;
        float stickRepeat;
        Rect[] itemRects = new Rect[Items.Length];
        Rect captainRect;
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
                case Item.Music: return MusicDirector.On ? "Music: on" : "Music: off";
                case Item.Song: return MusicDirector.On && MusicDirector.NowPlaying.Length > 0 ? $"Song:  <   {MusicDirector.NowPlaying}   >" : "Song:  <   (music off)   >";
                case Item.Coop: return CoopClient.Active ? $"Co-op: {CoopClient.Instance.Players.Count} playing" : "Co-op: join a game";
                case Item.Paint: return "Paint the ship";
                case Item.HudCaptain: return HudSettings.ShowCaptain ? "HUD: the captain shown" : "HUD: the captain hidden";
                case Item.HudSize: return $"HUD size:  <   {Mathf.RoundToInt(HudSettings.Size * 100f)}%   >";
                case Item.HudFleet: return HudSettings.FleetPanels ? "HUD: the fleet's panels shown" : "HUD: the fleet's panels hidden";
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
                case Item.HudCaptain: HudSettings.ShowCaptain = !HudSettings.ShowCaptain; return;
                case Item.HudFleet: HudSettings.FleetPanels = !HudSettings.FleetPanels; return;
                case Item.HudSize: HudSettings.NextSize(); return;   // (Enter: a step bigger, round to the smallest)
                case Item.Resume: Close(); break;
                case Item.Sound: SoundFX.Muted = !SoundFX.Muted; break;
                case Item.Music: MusicDirector.Toggle(); return;
                case Item.Song: MusicDirector.Next(); return;
                case Item.Coop:
                    Close();
                    var coop = FindAnyObjectByType<CoopMenu>();
                    if (coop != null) coop.Open();
                    break;
                case Item.Paint:
                    Close();
                    var start = FindAnyObjectByType<StartMenu>();
                    if (start != null) start.OpenPaint();
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

        /// <summary>Quit the game (the menu's Quit; the web quit test calls it too).</summary>
        public static void Quit()
        {
            PadSupport.StopRumble();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
            // a web page can't close its own tab, and the engine can't be shut down in a page that
            // stays open (its own listeners outlive it). So the game saves here, and the page
            // writes the save to the browser's storage and then leaves for its "ended" page.
            if (IndustryDirector.Instance != null) IndustryDirector.Instance.Save();
            PlayerPrefs.Save();
            CarrierPageQuit();
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
                // left/right on the HUD size: smaller, bigger; on the song: the one before, the next
                if (Items[selected] == Item.HudSize)
                {
                    if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) HudSettings.ChangeSize(-1);
                    if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) HudSettings.ChangeSize(+1);
                }
                if (Items[selected] == Item.Song)
                {
                    if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) MusicDirector.Previous();
                    if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) MusicDirector.Next();
                }
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) { Activate(selected); return; }
            }
            if (pad != null)
            {
                if (pad.startButton.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame) { Close(); return; }
                if (pad.dpad.up.wasPressedThisFrame) Move(-1);
                if (pad.dpad.down.wasPressedThisFrame) Move(+1);
                if (Items[selected] == Item.HudSize)
                {
                    if (pad.dpad.left.wasPressedThisFrame) HudSettings.ChangeSize(-1);
                    if (pad.dpad.right.wasPressedThisFrame) HudSettings.ChangeSize(+1);
                }
                if (Items[selected] == Item.Song)
                {
                    if (pad.dpad.left.wasPressedThisFrame) MusicDirector.Previous();
                    if (pad.dpad.right.wasPressedThisFrame) MusicDirector.Next();
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
                        if (!mouse.leftButton.wasPressedThisFrame) continue;
                        // the HUD size: the < side of the row smaller, the > side bigger; the song:
                        // the < side the one before, the > side the next
                        if (Items[i] == Item.HudSize || Items[i] == Item.Song)
                        {
                            selected = i;
                            confirming = null;
                            bool back = gui.x < itemRects[i].center.x;
                            if (Items[i] == Item.HudSize) HudSettings.ChangeSize(back ? -1 : +1);
                            else if (back) MusicDirector.Previous();
                            else MusicDirector.Next();
                            return;
                        }
                        Activate(i);
                        return;
                    }
            }
        }

        /// <summary>Where the &lt; (dir -1) or &gt; (+1) of an item's row is on screen (for the self-test's mouse).</summary>
        public Vector2 ItemArrowScreenPos(int index, int dir)
        {
            var r = itemRects[Mathf.Clamp(index, 0, itemRects.Length - 1)];
            float x = r.center.x + Mathf.Sign(dir) * r.width * 0.3f;
            return new Vector2(x, Screen.height - r.center.y);
        }

        /// <summary>The row of an item (for the self-test), or -1.</summary>
        public int IndexOfLabel(string startsWith)
        {
            for (int i = 0; i < Items.Length; i++) if (Label(Items[i]).StartsWith(startsWith)) return i;
            return -1;
        }

        /// <summary>Where an item is on screen (for the self-test's mouse).</summary>
        public Vector2 ItemScreenPos(int index)
        {
            var r = itemRects[Mathf.Clamp(index, 0, itemRects.Length - 1)];
            return new Vector2(r.center.x, Screen.height - r.center.y);
        }

        /// <summary>Where the captain's portrait is on screen (for the self-test's mouse).</summary>
        public Vector2 CaptainScreenPos() => new Vector2(captainRect.center.x, Screen.height - captainRect.center.y);

        public int Selected => selected;
        public string SelectedLabel => Label(Items[selected]);

        void OnGUI()
        {
            if (!open) return;
            GUI.depth = -100;   // over the HUD panels
            // (as big as the HUD, but never taller than the screen: the HUD's size is set here,
            // and the menu has to stay usable at any of them)
            float tall = 44f + 54f + 46f * Items.Length + 78f + 30f + 40f;
            float s = Mathf.Min(Hud.S, Screen.height * 0.96f / tall);
            Hud.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0.01f, 0.03f, 0.55f));
            float thumb = 78f * s;
            float w = 440f * s, rowH = 46f * s, pad = 22f * s;
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
            // the captain (chosen on the start menu: hers for the whole game)
            {
                var r = new Rect(panel.x + (w - thumb) * 0.5f, y + 4f * s, thumb, thumb);
                captainRect = r;
                var face = Demo.Captain.Face(Demo.Captain.Index, Mood.Neutral);
                Hud.Fill(r, new Color(0f, 0f, 0f, 0.6f));
                if (face != null) GUI.DrawTexture(r, face, ScaleMode.ScaleAndCrop);
                Hud.Outline(r, Mathf.Max(1f, 2f * s), new Color(1f, 0.72f, 0.3f));
            }
            GUI.Label(new Rect(panel.x, y + thumb + 8f * s, w, 20f * s), $"Capt. {Demo.Captain.Current.name}  <color=#9aa4b0>(in command for this game)</color>", Hud.Text(14, true, TextAnchor.MiddleCenter));
            string hint = Gamepad.current != null ? "A select   B or Start resume" : "Enter select   Esc or P resume";
            GUI.Label(new Rect(panel.x, panel.yMax - pad - 22f * s, w, 22f * s), hint, Hud.Text(13, false, TextAnchor.MiddleCenter));
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>A message on the co-op comms channel.</summary>
    [Serializable]
    public class ChatMsg
    {
        public string t = CoopWire.Chat;
        public string text;
    }

    /// <summary>
    /// The co-op comms channel: text messages between the players in a game (everyone in the same
    /// level), and the AI players' (CoopBot) answers and reports. The server passes them on like
    /// any other message between games. Enter opens a line to type into (Enter sends it, Esc drops
    /// it); the last few messages show at the top left and fade; each one from someone else also
    /// goes to the captain's notices. The last 200 are kept here, numbered, for the bot's API to read.
    /// </summary>
    public class CoopChat : MonoBehaviour
    {
        public struct Line
        {
            public int seq;
            public double time;
            /// <summary>The sender's player id.</summary>
            public int from;
            public string name, text;
        }

        public const int MaxLength = 240, Kept = 200;
        public static readonly List<Line> Lines = new List<Line>();
        public static int LastSeq { get; private set; }
        /// <summary>A message has arrived (or been sent from here).</summary>
        public static event Action<Line> Received;

        public static CoopChat Instance { get; private set; }
        /// <summary>The line to type into is open.</summary>
        public static bool Typing => Instance != null && Instance.typing;

        /// <summary>The clock the lines fade by: real time, or in a reel the game's (which runs at
        /// the reel's frame rate however long each frame takes to save).</summary>
        static double Now => Hud.ReelMode ? Time.timeAsDouble : Time.realtimeSinceStartupAsDouble;

        bool typing;
        int openedFrame;
        string draft = "";
        GUIStyle box, input;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Lines.Clear(); LastSeq = 0; Received = null; Instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Make()
        {
            if (Instance != null) return;
            var go = new GameObject("Coop chat");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CoopChat>();
        }

        void OnEnable() => CoopClient.Message += OnMessage;
        void OnDisable() => CoopClient.Message -= OnMessage;
        void OnDestroy()
        {
            if (typing) InputFocus.Release(InputFocus.Panel.Chat);
            if (Instance == this) Instance = null;
        }

        static void OnMessage(CoopMsg m)
        {
            if (m.t != CoopWire.Chat || string.IsNullOrWhiteSpace(m.text)) return;
            Add(m.from, CoopClient.Instance != null ? CoopClient.Instance.NameOf(m.from) : $"Player {m.from}", m.text);
        }

        /// <summary>Send a message to everyone in the game (and show it here). False if not
        /// connected, or there is nothing to say.</summary>
        public static bool Say(string text)
        {
            text = Clean(text);
            if (text.Length == 0 || !CoopClient.Active) return false;
            CoopClient.Instance.Send(new ChatMsg { text = text });
            Add(CoopClient.Instance.MyId, CoopClient.Instance.MyName, text);
            return true;
        }

        /// <summary>At most MaxLength characters, without control characters or space round it.</summary>
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(Math.Min(s.Length, MaxLength));
            foreach (char c in s)
            {
                if (sb.Length >= MaxLength) break;
                if (!char.IsControl(c)) sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        static void Add(int from, string name, string text)
        {
            text = Clean(text);
            if (text.Length == 0) return;
            var line = new Line { seq = ++LastSeq, time = Now, from = from, name = name ?? "", text = text };
            Lines.Add(line);
            if (Lines.Count > Kept) Lines.RemoveAt(0);
            if (CoopClient.Instance == null || from != CoopClient.Instance.MyId) Notices.Post($"{line.name}: {text}");
            Received?.Invoke(line);
        }

        void Update()
        {
            var k = Keyboard.current;
            if (typing)
            {
                // (the text field takes the characters; Enter and Esc are read here, as the other
                // panels read them, and never in the frame the line opened)
                if (!CoopClient.Active) { Close(); return; }
                if (k == null || Time.frameCount <= openedFrame) return;
                if (k.escapeKey.wasPressedThisFrame) { Close(); return; }
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)
                {
                    Say(draft);
                    Close();
                }
                return;
            }
            if (k == null || !CoopClient.Active || InputFocus.PanelUp) return;
            if ((k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) && InputFocus.Take(InputFocus.Panel.Chat))
            {
                typing = true;
                openedFrame = Time.frameCount;
                draft = "";
            }
        }

        void Close()
        {
            typing = false;
            draft = "";
            InputFocus.Release(InputFocus.Panel.Chat);
        }

        void OnGUI()
        {
            if (!CoopClient.Active && !typing) return;
            float s = Screen.height / 1080f;
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.label) { richText = false, wordWrap = false, clipping = TextClipping.Clip };
                input = new GUIStyle(GUI.skin.textField);
            }
            box.fontSize = Mathf.RoundToInt(17 * s);
            input.fontSize = Mathf.RoundToInt(18 * s);
            float x = 16 * s, w = 560 * s, y = 150 * s, lh = 24 * s;
            // the last six, each fading out 20 to 26 s after it came (all of them while typing)
            double now = Now;
            float Alpha(Line l) => typing ? 1f : Mathf.Clamp01((26f - (float)(now - l.time)) / 6f);
            int shown = 0;
            for (int i = Lines.Count - 1; i >= 0 && shown < 6 && Alpha(Lines[i]) > 0f; i--) shown++;
            for (int i = Lines.Count - shown; i < Lines.Count; i++)
            {
                var l = Lines[i];
                float a = Alpha(l);
                GUI.color = new Color(0f, 0f, 0f, 0.55f * a);
                GUI.DrawTexture(new Rect(x - 4 * s, y, w + 8 * s, lh), Texture2D.whiteTexture);
                GUI.color = new Color(0.55f, 0.85f, 1f, a);
                GUI.Label(new Rect(x, y, w, lh), $"{l.name}: {l.text}", box);
                y += lh;
            }
            GUI.color = Color.white;
            if (!typing) return;
            GUI.SetNextControlName("chat");
            draft = GUI.TextField(new Rect(x, y + 4 * s, w, 30 * s), draft ?? "", MaxLength, input);
            GUI.FocusControl("chat");
        }
    }
}

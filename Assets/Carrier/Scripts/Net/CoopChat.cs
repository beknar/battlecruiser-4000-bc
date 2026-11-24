using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>How it was made, for the server's chat log (CoopChat.Via): "" typed by a
        /// player.</summary>
        public string via;
    }

    /// <summary>How a message on comms was made (ChatMsg.via), so a log of the channel can tell
    /// a wingman's answers apart: an order it carried out, its personality's talk, Claude's (or
    /// another LLM's) answer through its API, a fixed line, the paused line, something it reports
    /// by itself; and a player's spoken command.</summary>
    public static class Via
    {
        public const string Order = "order", Personality = "personality", Llm = "llm", Fixed = "fixed", Paused = "paused",
                            Event = "event", Voice = "voice";
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

        public const int MaxLength = 400, Kept = 200;
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
        readonly List<float> heights = new List<float>();
        readonly List<string> doingLines = new List<string>();
        readonly List<float> doingHeights = new List<float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Lines.Clear(); Doings.Clear(); LastSeq = 0; Received = null; Instance = null; }

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

        /// <summary>What each AI wingman says it's doing (CoopWire.Doing), by player id, and when
        /// it said so: shown over the comms lines, so its order doesn't go when they fade.</summary>
        public static readonly Dictionary<int, (string text, double time)> Doings = new Dictionary<int, (string, double)>();
        /// <summary>How long a wingman's line stays up without being said again.</summary>
        public const double DoingKept = 40.0;

        [Serializable] class DoingMsg { public string t = CoopWire.Doing; public string text; }

        /// <summary>Tell everyone what this game's AI wingman is doing (CoopBot).</summary>
        /// <summary>("": nothing to show any more, the line taken down.)</summary>
        public static void SayDoing(string text)
        {
            if (!CoopClient.Active) return;
            CoopClient.Instance.Send(new DoingMsg { text = Clean(text ?? "", DoingMax) });
        }

        /// <summary>A wingman's line at most this long (a row of the HUD).</summary>
        public const int DoingMax = 70;

        internal static void OnMessage(CoopMsg m)
        {
            if (m.t == CoopWire.Leave) { Doings.Remove(m.id); return; }
            if (m.t == CoopWire.Doing)
            {
                if (string.IsNullOrWhiteSpace(m.text)) Doings.Remove(m.from);
                else Doings[m.from] = (Clean(m.text, DoingMax), Now);
                return;
            }
            if (m.t != CoopWire.Chat || string.IsNullOrWhiteSpace(m.text)) return;
            Add(m.from, CoopClient.Instance != null ? CoopClient.Instance.NameOf(m.from) : $"Player {m.from}", m.text);
        }

        /// <summary>Send a message to everyone in the game (and show it here). False if not
        /// connected, or there is nothing to say. `via`: how it was made (Via; null: typed).</summary>
        public static bool Say(string text, string via = null)
        {
            text = Clean(text);
            if (text.Length == 0 || !CoopClient.Active) return false;
            CoopClient.Instance.Send(new ChatMsg { text = text, via = via ?? "" });
            Add(CoopClient.Instance.MyId, CoopClient.Instance.MyName, text);
            return true;
        }

        /// <summary>At most `limit` characters (MaxLength), without control characters or space round it.</summary>
        public static string Clean(string s, int limit = MaxLength)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(Math.Min(s.Length, limit));
            // (the space before it doesn't count against the limit)
            foreach (char c in s.TrimStart())
            {
                if (sb.Length >= limit) break;
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
            if (CoopClient.Instance == null || from != CoopClient.Instance.MyId) Notices.Post(Shown(line));
            Received?.Invoke(line);
        }

        /// <summary>A message as shown: "name: text", or the text alone when it starts with the
        /// sender's name already (the bots say "Wingman: ...").</summary>
        public static string Shown(Line l)
        {
            bool bot = l.name.Length > 0 && l.text.StartsWith(l.name + ":", StringComparison.OrdinalIgnoreCase);
            string text = l.text;
            // an AI wingman's radio through jamming on my ship comes through broken up (what people
            // type to each other is left alone; the bot's API reads the clean text)
            if (bot && CoopClient.Instance != null && l.from != CoopClient.Instance.MyId)
            {
                float j = Jamming.On(PlayerShips.Primary);
                if (j > 0.05f) text = l.name + ":" + Jamming.Garble(text.Substring(l.name.Length + 1), j, l.seq);
            }
            return bot ? text : $"{l.name}: {text}";
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

        /// <summary>Where the comms line and its messages are on screen now (zero when none).</summary>
        public static Rect Area { get; private set; }

        void OnGUI()
        {
            Area = Rect.zero;
            if (!CoopClient.Active && !typing) return;
            // (the HUD's scale: the screen's height, and the player's text size)
            float s = Hud.S * 900f / 1080f;
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.label) { richText = false, wordWrap = true, clipping = TextClipping.Clip };
                input = new GUIStyle(GUI.skin.textField);
            }
            box.fontSize = Mathf.RoundToInt(17 * s);
            input.fontSize = Mathf.RoundToInt(18 * s);
            // at the foot of the screen, beside the ship's console (clear of the controls box at
            // the top left, which covered it): the typing line at the bottom, the messages above it
            float lh = 24 * s, inputH = 30 * s, gap = 12 * s;
            var console = ShipConsole.Rect;
            float x = console.width > 0f ? console.xMax + gap + 4 * s : 16 * s;
            float bottom = Screen.height - 10 * s;
            float w = Mathf.Min(560 * s, Screen.width - x - 16 * s);
            if (w < 300 * s)
            {
                // (no room beside the console: over it)
                x = 16 * s;
                w = Mathf.Min(560 * s, Screen.width - 32 * s);
                bottom = console.height > 0f ? console.yMin - 6 * s : bottom;
            }
            // the last six, each fading out 20 to 26 s after it came (all of them while typing), each
            // wrapped over as many lines as it takes: the whole of it, however long (up to a third
            // of the screen's height for all of them, the oldest dropped first)
            double now = Now;
            float Alpha(Line l) => typing ? 1f : Mathf.Clamp01((26f - (float)(now - l.time)) / 6f);
            float room = Screen.height / 3f, used = 0f;
            int shown = 0;
            heights.Clear();
            for (int i = Lines.Count - 1; i >= 0 && shown < 6 && Alpha(Lines[i]) > 0f; i--)
            {
                float h = Mathf.Max(lh, box.CalcHeight(new GUIContent(Shown(Lines[i])), w));
                if (shown > 0 && used + h > room) break;
                heights.Insert(0, h);
                used += h;
                shown++;
            }
            float y = bottom - (typing ? inputH + 4 * s : 0f) - used;
            // over them, what each AI wingman is doing (always shown, while it keeps saying so)
            // (only players in this room now: not one gone, or left behind in another room)
            doingLines.Clear();
            var client = CoopClient.Instance;
            if (client != null)
                foreach (var p in client.Players.OrderBy(q => q.id))
                    if (p != null && p.level == client.RoomLevel && Doings.TryGetValue(p.id, out var d) && now - d.time < DoingKept)
                        doingLines.Add($"{p.name}: {d.text}");
            float total = 0f;
            doingHeights.Clear();
            foreach (var line in doingLines) { float h = Mathf.Max(lh, box.CalcHeight(new GUIContent(line), w)); doingHeights.Add(h); total += h; }
            float dy = y - total, ry = dy;
            for (int i = 0; i < doingLines.Count; i++)
            {
                var r = new Rect(x - 4 * s, ry, w + 8 * s, doingHeights[i]);
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.8f, 0.45f, 1f);
                GUI.Label(new Rect(x, r.y, w, r.height), doingLines[i], box);
                ry += doingHeights[i];
            }
            GUI.color = Color.white;
            Area = new Rect(x - 4 * s, dy, w + 8 * s, bottom - dy);
            for (int i = Lines.Count - shown, k = 0; i < Lines.Count; i++, k++)
            {
                var l = Lines[i];
                float a = Alpha(l), h = heights[k];
                GUI.color = new Color(0f, 0f, 0f, 0.55f * a);
                GUI.DrawTexture(new Rect(x - 4 * s, y, w + 8 * s, h), Texture2D.whiteTexture);
                GUI.color = new Color(0.55f, 0.85f, 1f, a);
                GUI.Label(new Rect(x, y, w, h), Shown(l), box);
                y += h;
            }
            GUI.color = Color.white;
            if (!typing) return;
            GUI.SetNextControlName("chat");
            draft = GUI.TextField(new Rect(x, y + 4 * s, w, inputH), draft ?? "", MaxLength, input);
            GUI.FocusControl("chat");
        }
    }
}

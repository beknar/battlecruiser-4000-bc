using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The AI wingman's voice, in the player's game: every line an AI wingman says on the comms
    /// channel, spoken over the radio, one after another (a queue, so lines don't talk over each
    /// other; one waiting more than 12 s is dropped).
    ///   - Stock phrases: a line it always says the same way (its personality's lines, its fixed
    ///     replies, those naming a place) was recorded ahead of time (Tools/Voice/render_stock.py,
    ///     Resources/WingmanVoice/index.json) and is played from its clip.
    ///   - Anything else (numbers, people's names, an LLM's own words) goes to the live voice
    ///     (WingmanVoice.Live: the game's small speech engine, or the browser's), when there is one.
    /// The clean text is spoken, not the jammed text shown. Turned on or off in the pause menu
    /// (Wingman voice), and kept; on by default.
    /// </summary>
    public partial class WingmanVoice : MonoBehaviour
    {
        const string PrefKey = "voice.wingman";
        public const string Folder = "WingmanVoice";
        /// <summary>A line waiting longer than this is dropped (it's old news).</summary>
        public const float MaxWait = 12f;

        public static WingmanVoice Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; index = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // (not in a game that is itself an AI wingman: it would speak the other bots' lines)
            if (Instance != null || Application.isBatchMode || CoopBot.Enabled) return;
            var go = new GameObject("Wingman voice");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<WingmanVoice>();
        }

        /// <summary>The wingman's voice on (the pause menu's setting, kept; on unless turned off).</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 1) == 1;
            set { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); PlayerPrefs.Save(); if (!value && Instance != null) Instance.Hush(); }
        }

        public static string MenuLine => Enabled ? "Wingman voice: on" : "Wingman voice: off";

        // ------------------------------------------------------------ the stock phrases

        [Serializable] class Clip { public string key, clip, text; }
        [Serializable] class Index { public string note, voice; public Clip[] clips; }
        static Dictionary<string, string> index;

        /// <summary>The stock phrases: each line as matched (Key) to its clip's name.</summary>
        public static IReadOnlyDictionary<string, string> Stock
        {
            get
            {
                if (index != null) return index;
                index = new Dictionary<string, string>();
                var ta = Resources.Load<TextAsset>(Folder + "/index");
                var data = ta != null ? JsonUtility.FromJson<Index>(ta.text) : null;
                if (data?.clips != null) foreach (var c in data.clips) if (c != null && !string.IsNullOrEmpty(c.key)) index[c.key] = c.clip;
                return index;
            }
        }

        /// <summary>A line as it's matched to a clip: lower case, apostrophes dropped, anything
        /// but letters and digits a space, spaces single (render_stock.py's key()).</summary>
        public static string Key(string text) => WingmanPersonality.Normalize(text);

        /// <summary>The stock clip for a line (null: none; it's for the live voice).</summary>
        public static AudioClip StockClip(string text) =>
            Stock.TryGetValue(Key(text), out var name) ? Resources.Load<AudioClip>(Folder + "/" + name) : null;

        // ------------------------------------------------------------ the queue

        struct Waiting { public string text; public AudioClip clip; public float at; public int ticket; }
        readonly Queue<Waiting> queue = new Queue<Waiting>();
        AudioSource source;
        /// <summary>Spoken so far: from a stock clip, and by the live voice (for the tests).</summary>
        public int StockSpoken { get; private set; }
        public int LiveSpoken { get; private set; }
        /// <summary>The line being spoken now (null: none).</summary>
        public string Speaking { get; private set; }
        float liveUntil;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.9f;
            source.ignoreListenerPause = true;
        }

        void OnEnable() => CoopChat.Received += OnLine;
        void OnDisable() => CoopChat.Received -= OnLine;

        void OnDestroy()
        {
            LiveStop();
            if (Instance == this) Instance = null;
        }

        /// <summary>A line on the comms channel: spoken if an AI wingman (not this game) said it.</summary>
        void OnLine(CoopChat.Line line)
        {
            var c = CoopClient.Instance;
            if (c == null || line.from == c.MyId) return;
            var p = c.Players.FirstOrDefault(x => x != null && x.id == line.from);
            if (p == null || !(p.ai || CoopBot.IsBot(p))) return;
            // (its lines start with its name: "Wingman: holding position.")
            string text = line.text;
            if (line.name.Length > 0 && text.StartsWith(line.name + ":", StringComparison.OrdinalIgnoreCase)) text = text.Substring(line.name.Length + 1);
            if (ForReading(text)) return;
            Say(text);
        }

        /// <summary>A line written to be read, not heard: a list of orders ("follow me / hold",
        /// "warp to &lt;place&gt;"), as help and the greeting are.</summary>
        public static bool ForReading(string text) =>
            text != null && (text.Contains(" / ") || text.Contains("<") || text.Contains("[") || text.Contains("|"));


        /// <summary>Speak a line (after those already waiting).</summary>
        public void Say(string text)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            var clip = StockClip(text);
            // (no stock clip: the live voice starts on it now, so it's ready by its turn)
            queue.Enqueue(new Waiting { text = text, clip = clip, at = Time.unscaledTime, ticket = clip == null ? LiveSubmit(text) : -1 });
        }

        void Play(AudioClip clip)
        {
            var old = source.clip;
            source.volume = SoundFX.Muted ? 0f : 0.9f;
            source.clip = clip;
            source.Play();
            // (a live line's clip was made for it: gone once another plays)
            if (old != null && old != clip && old.name == LiveClipName) Destroy(old);
        }

        const string LiveClipName = "wingman live";

        /// <summary>Everything stopped and the queue cleared.</summary>
        public void Hush()
        {
            foreach (var w in queue) LiveForget(w.ticket);
            queue.Clear();
            if (source != null) source.Stop();
            LiveStop();
            Speaking = null;
        }

        void Update()
        {
            // (lines waiting too long dropped, whatever is speaking)
            while (queue.Count > 0 && Time.unscaledTime - queue.Peek().at > MaxWait) LiveForget(queue.Dequeue().ticket);
            if (LiveBusy)
            {
                // (the browser's voice muted with the game, and given up on if it never says it's done)
                if (SoundFX.Muted || Time.unscaledTime > liveUntil) LiveStop();
                return;
            }
            if (source.isPlaying) return;
            Speaking = null;
            // (not over the player while they're talking to it)
            if (VoiceInput.Now == VoiceInput.State.Listening) return;
            while (queue.Count > 0)
            {
                var w = queue.Peek();
                if (Time.unscaledTime - w.at > MaxWait) { queue.Dequeue(); LiveForget(w.ticket); continue; }
                if (w.clip != null)
                {
                    queue.Dequeue();
                    Play(w.clip);
                    StockSpoken++;
                    Speaking = w.text;
                    return;
                }
                switch (LiveTake(w.text, w.ticket, out var live))
                {
                    case Live.Waiting: return;   // (still being made: a moment)
                    case Live.Clip: queue.Dequeue(); Play(live); LiveSpoken++; Speaking = w.text; return;
                    case Live.Spoken: queue.Dequeue(); LiveSpoken++; Speaking = w.text; liveUntil = Time.unscaledTime + 3f + 0.12f * w.text.Length; return;
                    default: queue.Dequeue(); continue;   // (no live voice here: shown, not said)
                }
            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The wingman's live voice, for lines with no stock clip (numbers, people's names, an LLM's
    /// own words):
    ///   - Windows and Linux: CMU Flite (BSD-style licence), built as a small native library with
    ///     its smallest voice (Plugins/x86_64: bctts.dll, libbctts.so; Tools/Voice/flite). It runs
    ///     on a worker thread of its own, one line at a time, and the radio's sound (the stock
    ///     clips' band, a little grit, a squelch at each end) is put on in C#: the samples are
    ///     ready long before the line's turn comes.
    ///   - The web: the browser's own speechSynthesis (CarrierSpeech.jslib), which adds nothing to
    ///     the download; it plays outside the game's audio, so without the radio's sound.
    ///   - Anywhere else, or without the library: nothing (the line is shown, not said).
    /// </summary>
    public partial class WingmanVoice
    {
        enum Live { None, Waiting, Clip, Spoken }

#if UNITY_EDITOR_WIN || UNITY_EDITOR_LINUX || ((UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX) && !UNITY_EDITOR)
        const string Lib = "bctts";
        [DllImport(Lib)] static extern int bctts_init();
        [DllImport(Lib)] static extern int bctts_speak(byte[] utf8, out IntPtr samples, out int rate);
        [DllImport(Lib)] static extern void bctts_free(IntPtr p);

        /// <summary>The speech engine there to be used (false: no library, or it didn't start).</summary>
        public static bool LiveAvailable { get; private set; } = true;

        // the worker: lines in, finished samples out
        static readonly BlockingCollection<(int ticket, string text)> todo = new BlockingCollection<(int, string)>();
        static readonly ConcurrentDictionary<int, (float[] samples, int rate)> made = new ConcurrentDictionary<int, (float[], int)>();
        static readonly ConcurrentDictionary<int, bool> failed = new ConcurrentDictionary<int, bool>();
        static Thread worker;
        static int nextTicket;

        static void Work()
        {
            try { if (bctts_init() != 1) { LiveAvailable = false; return; } }
            catch (Exception e) { LiveAvailable = false; Debug.Log($"[Voice] no live voice: {e.GetType().Name}"); return; }
            foreach (var (ticket, text) in todo.GetConsumingEnumerable())
            {
                try
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(Speakable(text) + "\0");
                    int n = bctts_speak(bytes, out IntPtr p, out int rate);
                    if (n <= 0 || p == IntPtr.Zero) { failed[ticket] = true; continue; }
                    var pcm = new short[n];
                    Marshal.Copy(p, pcm, 0, n);
                    bctts_free(p);
                    made[ticket] = (Radio(pcm, rate, ticket), rate);
                }
                catch (Exception) { failed[ticket] = true; }
            }
        }

        static int LiveSubmit(string text)
        {
            if (!LiveAvailable || string.IsNullOrWhiteSpace(text)) return -1;
            if (worker == null)
            {
                worker = new Thread(Work) { IsBackground = true, Name = "Wingman voice" };
                worker.Start();
            }
            int ticket = Interlocked.Increment(ref nextTicket);
            todo.Add((ticket, text));
            return ticket;
        }

        Live LiveTake(string text, int ticket, out AudioClip clip)
        {
            clip = null;
            if (ticket < 0) return Live.None;
            if (made.TryRemove(ticket, out var m))
            {
                clip = AudioClip.Create("wingman live", m.samples.Length, 1, m.rate, false);
                clip.SetData(m.samples, 0);
                Destroy(clip, m.samples.Length / (float)m.rate + 2f);
                return Live.Clip;
            }
            if (failed.TryRemove(ticket, out _) || !LiveAvailable) return Live.None;
            return Live.Waiting;
        }

        static void LiveForget(int ticket)
        {
            if (ticket < 0) return;
            made.TryRemove(ticket, out _);
            failed.TryRemove(ticket, out _);
        }

        bool LiveBusy => false;
        void LiveStop() { }

        /// <summary>For the tests: a line spoken here and now, with the radio's sound (null: no
        /// speech engine). Not while the worker is speaking.</summary>
        public static float[] SpeakNow(string text, out int rate)
        {
            rate = 0;
            try
            {
                int ok = bctts_init();
                if (ok != 1) { Debug.LogWarning($"[Voice] the speech engine didn't start ({ok})"); return null; }
                int n = bctts_speak(System.Text.Encoding.UTF8.GetBytes(Speakable(text) + "\0"), out IntPtr p, out rate);
                if (n <= 0) { Debug.LogWarning($"[Voice] the speech engine said nothing ({n})"); return null; }
                var pcm = new short[n];
                Marshal.Copy(p, pcm, 0, n);
                bctts_free(p);
                return Radio(pcm, rate);
            }
            catch (DllNotFoundException ex) { Debug.LogWarning($"[Voice] no speech engine: {ex.Message}"); return null; }
        }
#elif UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int CarrierSpeechSupported();
        [DllImport("__Internal")] static extern int CarrierSpeak(string text, float volume);
        [DllImport("__Internal")] static extern int CarrierSpeaking();
        [DllImport("__Internal")] static extern void CarrierSpeakStop();

        public static bool LiveAvailable => CarrierSpeechSupported() == 1;

        static int LiveSubmit(string text) => LiveAvailable ? 0 : -1;

        Live LiveTake(string text, int ticket, out AudioClip clip)
        {
            clip = null;
            if (ticket < 0) return Live.None;
            return CarrierSpeak(Speakable(text), SoundFX.Muted ? 0f : 0.9f) == 1 ? Live.Spoken : Live.None;
        }

        static void LiveForget(int ticket) { }
        bool LiveBusy => CarrierSpeaking() == 1;
        void LiveStop() => CarrierSpeakStop();
#else
        public static float[] SpeakNow(string text, out int rate) { rate = 0; return null; }
        public static bool LiveAvailable => false;
        static int LiveSubmit(string text) => -1;
        Live LiveTake(string text, int ticket, out AudioClip clip) { clip = null; return Live.None; }
        static void LiveForget(int ticket) { }
        bool LiveBusy => false;
        void LiveStop() { }
#endif

        /// <summary>A line as the live voice should say it: what's written for reading made
        /// speakable ("34 t" is "34 tonnes", "1,200 cr" "1200 credits", "%" "percent").</summary>
        public static string Speakable(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string t = System.Text.RegularExpressions.Regex.Replace(text, @"(\d),(\d{3})", "$1$2");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"(\d+(\.\d+)?)\s*t\b", "$1 tonnes");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"(\d+(\.\d+)?)\s*cr\b", "$1 credits");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"(\d+(\.\d+)?)\s*km\b", "$1 kilometres");
            t = t.Replace("%", " percent").Replace(";", ",").Replace("(", ", ").Replace(")", ", ").Replace("\"", "");
            return t;
        }

        /// <summary>The radio's sound on a line's samples, as the stock clips have it: a squelch of
        /// static, the voice band-limited (300 to 3300 Hz) and a little compressed, a squelch again.</summary>
        public static float[] Radio(short[] pcm, int rate, int seed = 0)
        {
            int pre = rate * 7 / 100, post = rate * 5 / 100;
            var o = new float[pre + pcm.Length + post];
            var rng = new System.Random(seed);
            for (int i = 0; i < pre; i++) o[i] = (float)(rng.NextDouble() * 2 - 1) * 0.06f;
            for (int i = 0; i < pcm.Length; i++) o[pre + i] = pcm[i] / 32768f;
            for (int i = 0; i < post; i++) o[pre + pcm.Length + i] = (float)(rng.NextDouble() * 2 - 1) * 0.05f;
            // band: two second-order high-passes at 300 Hz and two low-passes at 3300 Hz
            Biquad(o, rate, 300f, true);
            Biquad(o, rate, 300f, true);
            Biquad(o, rate, 3300f, false);
            Biquad(o, rate, 3300f, false);
            // a little compression, and the level the stock clips have
            float peak = 1e-4f;
            foreach (var v in o) peak = Mathf.Max(peak, Mathf.Abs(v));
            float gain = 2.2f / peak;
            for (int i = 0; i < o.Length; i++) o[i] = (float)Math.Tanh(o[i] * gain) * 0.9f;
            return o;
        }

        static void Biquad(float[] x, int rate, float f, bool high)
        {
            // (RBJ's cookbook, Q = 0.707)
            double w = 2 * Math.PI * f / rate, cs = Math.Cos(w), a = Math.Sin(w) / (2 * 0.7071);
            double b0 = high ? (1 + cs) / 2 : (1 - cs) / 2, b1 = high ? -(1 + cs) : 1 - cs, b2 = b0;
            double a0 = 1 + a, a1 = -2 * cs, a2 = 1 - a;
            b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double y = b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x[i]; y2 = y1; y1 = y;
                x[i] = (float)y;
            }
        }
    }
}

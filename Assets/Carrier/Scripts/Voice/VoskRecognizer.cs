using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Offline speech recognition for the voice commands on Windows and Linux, when a microphone is
    /// chosen in the pause menu (VoiceInput records it): Vosk (Apache-2.0, Alpha Cephei), with its
    /// small English model.
    ///   - The library is in Plugins/x86_64 (libvosk.dll with the MinGW runtime it was built with,
    ///     libvosk.so); the model (40 MB) is in vosk-model beside the player, put there by the
    ///     editor's ThirdPartyNotices after a Windows or Linux build, and in Tools/Voice/vosk-model
    ///     in the editor. The web build has neither.
    ///   - The model is loaded on first use, on a worker thread of its own, and what was said is
    ///     recognised there too: samples in, the words out (TryTake, on the main thread).
    ///   - It listens for a grammar (GrammarJson: the orders, the wingmen's names and the places),
    ///     which cuts the mishearings; a word the model doesn't know is "[unk]" in it, and so is
    ///     anything else said.
    /// </summary>
    public static class VoskRecognizer
    {
        /// <summary>The model's folder, beside the player (and under Tools/Voice in the repo).</summary>
        public const string ModelFolder = "vosk-model";
        /// <summary>What the model says for a word it doesn't know (in the grammar, anything else).</summary>
        public const string Unknown = "[unk]";

        static string modelPath;
        static bool looked;

        /// <summary>The model's folder (null: not with this game). Looked for beside the player
        /// (the folder with the executable, or its _Data folder) and, in the editor, in the repo's
        /// Tools/Voice. Main thread only (the first time).</summary>
        public static string ModelPath
        {
            get
            {
                if (looked) return modelPath;
                looked = true;
                modelPath = FindModel(Application.dataPath, Application.isEditor);
                return modelPath;
            }
        }

        /// <summary>Of the places the model may be, given Application.dataPath, the first that has
        /// one (null: none). A model still in Git LFS's pointer files doesn't count.</summary>
        public static string FindModel(string dataPath, bool editor)
        {
            if (string.IsNullOrEmpty(dataPath)) return null;
            string parent = Path.GetDirectoryName(dataPath) ?? dataPath;
            var places = new List<string>();
            if (editor) places.Add(Path.Combine(parent, "Tools", "Voice", ModelFolder));
            places.Add(Path.Combine(parent, ModelFolder));
            places.Add(Path.Combine(dataPath, ModelFolder));
            foreach (var p in places)
            {
                try
                {
                    var mdl = new FileInfo(Path.Combine(p, "am", "final.mdl"));
                    if (mdl.Exists && mdl.Length > 100000) return p;
                }
                catch (Exception) { }
            }
            return null;
        }

        /// <summary>Is there a recogniser to use here: the library built for this system, and the model?</summary>
        public static bool Present => Built && ModelPath != null;

        /// <summary>Why it didn't start (null: it did, or hasn't been asked to yet).</summary>
        public static string Error { get; private set; }
        /// <summary>It was asked to start and couldn't (Error says why): it isn't tried again.</summary>
        public static bool Failed { get; private set; }
        /// <summary>It failed because the library isn't there (or isn't the right one).</summary>
        public static bool LibraryMissing { get; private set; }

        // ------------------------------------------------------------ the grammar

        /// <summary>A phrase's words as the model has them: lower case, nothing but letters, digits
        /// and apostrophes ("What's in your hold?": what's, in, your, hold).</summary>
        public static List<string> Words(string phrase)
        {
            var sb = new StringBuilder();
            foreach (char c in (phrase ?? "").ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) || c == '\'' ? c : ' ');
            return sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(w => w.Trim('\'')).Where(w => w.Length > 0).ToList();
        }

        /// <summary>The grammar's phrases: each of `phrases` in the model's words, each phrase once,
        /// and "[unk]" last, for anything else. A word it doesn't know (`known` false: a place such
        /// as Gilgal) is taken out of its phrase and "[unk]" put in its place (once for several
        /// together), so Vosk isn't given a word it would only warn about, and an order to an
        /// unknown place is heard as "warp to [unk]" rather than as a place it does know.</summary>
        public static List<string> GrammarPhrases(IEnumerable<string> phrases, Func<string, bool> known)
        {
            var seen = new HashSet<string>();
            var list = new List<string>();
            foreach (var p in phrases ?? Enumerable.Empty<string>())
            {
                var words = new List<string>();
                foreach (var w0 in Words(p))
                {
                    string w = known == null || known(w0) ? w0 : Unknown;
                    if (w == Unknown && words.Count > 0 && words[words.Count - 1] == Unknown) continue;
                    words.Add(w);
                }
                if (words.Count == 0) continue;
                string s = string.Join(" ", words);
                if (s == Unknown) continue;     // (it goes last)
                if (seen.Add(s)) list.Add(s);
            }
            if (seen.Add(Unknown)) list.Add(Unknown);
            return list;
        }

        /// <summary>The grammar as Vosk takes it: a JSON list of the phrases (GrammarPhrases).</summary>
        public static string GrammarJson(IEnumerable<string> phrases, Func<string, bool> known)
        {
            var sb = new StringBuilder("[");
            foreach (var p in GrammarPhrases(phrases, known))
            {
                if (sb.Length > 1) sb.Append(", ");
                sb.Append('"').Append(p.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            }
            return sb.Append(']').ToString();
        }

        /// <summary>The words of a result as Vosk gives it ({"text" : "wingman follow me"}; "": none).</summary>
        public static string TextOf(string json)
        {
            var m = System.Text.RegularExpressions.Regex.Match(json ?? "", @"""text""\s*:\s*""((?:[^""\\]|\\.)*)""");
            if (!m.Success) return "";
            string s = m.Groups[1].Value;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char e = s[++i];
                if (e == 'u' && i + 4 < s.Length && int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out int code))
                {
                    sb.Append((char)code);
                    i += 4;
                }
                else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
            }
            return sb.ToString().Trim();
        }

        // (the words a place or a name comes after: "warp to [unk]", "mine at the [unk]")
        static readonly HashSet<string> BeforeAName = new HashSet<string> { "to", "at", "the" };

        /// <summary>What was heard without its "[unk]"s (`unknown`: there were some). `nameMissing`:
        /// one stood where a place goes (after "to", "at" or "the": "warp to [unk]", a place the
        /// model doesn't know), so the order can't be sent as it was heard. Elsewhere (a breath
        /// before the order) it is only left out.</summary>
        public static string WithoutUnknowns(string text, out bool unknown, out bool nameMissing)
        {
            unknown = false;
            nameMissing = false;
            var words = (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<string>();
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i] != Unknown) { kept.Add(words[i]); continue; }
                unknown = true;
                if (i > 0 && BeforeAName.Contains(words[i - 1])) nameMissing = true;
            }
            return string.Join(" ", kept);
        }

        // ------------------------------------------------------------ the engine

#if UNITY_EDITOR_WIN || UNITY_EDITOR_LINUX || ((UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX) && !UNITY_EDITOR)
        const string Lib = "libvosk";
        [DllImport(Lib)] static extern void vosk_set_log_level(int level);
        [DllImport(Lib)] static extern IntPtr vosk_model_new(byte[] utf8Path);
        [DllImport(Lib)] static extern void vosk_model_free(IntPtr model);
        [DllImport(Lib)] static extern int vosk_model_find_word(IntPtr model, byte[] utf8Word);
        [DllImport(Lib)] static extern IntPtr vosk_recognizer_new_grm(IntPtr model, float sampleRate, byte[] utf8Grammar);
        [DllImport(Lib)] static extern void vosk_recognizer_free(IntPtr recognizer);
        [DllImport(Lib)] static extern int vosk_recognizer_accept_waveform_s(IntPtr recognizer, short[] data, int length);
        [DllImport(Lib)] static extern IntPtr vosk_recognizer_final_result(IntPtr recognizer);
        [DllImport(Lib)] static extern void vosk_recognizer_reset(IntPtr recognizer);

        static bool Built => true;

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        // (libvosk.dll needs the MinGW runtime beside it, which Windows doesn't look for beside a
        // plugin: each is loaded first from Plugins/x86_64, so libvosk.dll finds them loaded)
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryW(string path);
        static readonly string[] Runtime = { "libwinpthread-1.dll", "libgcc_s_seh-1.dll", "libstdc++-6.dll", "libvosk.dll" };
        static bool runtimeLoaded;

        static void LoadRuntime()
        {
            if (runtimeLoaded || pluginDirs == null) return;
            runtimeLoaded = true;
            foreach (var name in Runtime)
                foreach (var dir in pluginDirs)
                {
                    string p = Path.Combine(dir, name);
                    if (File.Exists(p) && LoadLibraryW(p) != IntPtr.Zero) break;
                }
        }
#else
        static void LoadRuntime() { }
#endif

        // where the plugins are (found on the main thread, for the worker)
        static string[] pluginDirs;
        static IntPtr model, recognizer;
        static string recognizerGrammar;
        static float recognizerRate;
#if UNITY_EDITOR
        static bool unloadHooked;
#endif
        // (the model and its recogniser are used by one thread at a time: the worker, or RecognizeNow)
        static readonly object engine = new object();

        struct Job { public int ticket; public short[] pcm; public int count; public int rate; public string[] phrases; }
        static readonly BlockingCollection<Job> todo = new BlockingCollection<Job>();
        static readonly ConcurrentQueue<(int ticket, string text)> done = new ConcurrentQueue<(int, string)>();
        static Thread worker;
        static int nextTicket;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetQueue() { while (done.TryDequeue(out _)) { } }

        /// <summary>Gets it ready: the worker started, which loads the model (under a second) before
        /// anything is asked of it. Main thread.</summary>
        public static void Preload()
        {
            if (!Present || Failed) return;
            Paths();
            if (worker != null) return;
            worker = new Thread(Work) { IsBackground = true, Name = "Vosk" };
            worker.Start();
        }

        static void Paths()
        {
            if (pluginDirs != null) return;
            string data = Application.dataPath;
            pluginDirs = Application.isEditor
                ? new[] { Path.Combine(data, "Carrier", "Plugins", "x86_64") }
                : new[] { Path.Combine(data, "Plugins", "x86_64"), Path.Combine(data, "Plugins") };
            _ = ModelPath;
        }

        /// <summary>Samples of what was said (16-bit mono, `count` of them at `rate`) to be
        /// recognised on the worker, listening for `phrases` (GrammarPhrases makes the grammar of
        /// them). The ticket to look for in TryTake (-1: there's no recogniser).</summary>
        public static int Submit(short[] pcm, int count, int rate, IList<string> phrases)
        {
            if (!Present || Failed || pcm == null) return -1;
            Preload();
            int ticket = Interlocked.Increment(ref nextTicket);
            todo.Add(new Job { ticket = ticket, pcm = pcm, count = Math.Min(count, pcm.Length), rate = rate, phrases = (phrases ?? new string[0]).ToArray() });
            return ticket;
        }

        /// <summary>A result from the worker, if one is ready: its ticket, and what was heard
        /// (lower case, "[unk]" for words not known; "": nothing; null: it failed, Error says why).</summary>
        public static bool TryTake(out int ticket, out string text)
        {
            ticket = -1;
            text = null;
            if (!done.TryDequeue(out var r)) return false;
            ticket = r.ticket;
            text = r.text;
            return true;
        }

        static void Work()
        {
            lock (engine) Load();
            foreach (var job in todo.GetConsumingEnumerable())
            {
                string text = null;
                try { lock (engine) if (Load()) text = Recognise(job.pcm, job.count, job.rate, job.phrases); }
                catch (Exception e) { Error = Error ?? e.Message; }
                done.Enqueue((job.ticket, text));
            }
        }

        // (with the engine's lock)
        static bool Load()
        {
            if (model != IntPtr.Zero) return true;
            if (Failed) return false;
            try
            {
                if (modelPath == null) { Fail("no model (vosk-model)"); return false; }
                LoadRuntime();
                vosk_set_log_level(-1);
                model = vosk_model_new(Utf8(modelPath));
                if (model == IntPtr.Zero) { Fail("the model didn't load"); return false; }
#if UNITY_EDITOR
                // (freed before the scripts are reloaded, or the editor would keep each copy)
                if (!unloadHooked) AppDomain.CurrentDomain.DomainUnload += (s, e) => Unload();
                unloadHooked = true;
#endif
                return true;
            }
            catch (DllNotFoundException e) { LibraryMissing = true; Fail($"no library: {e.Message}"); }
            catch (EntryPointNotFoundException e) { LibraryMissing = true; Fail($"the wrong library: {e.Message}"); }
            catch (Exception e) { Fail(e.Message); }
            return false;
        }

        static void Fail(string why)
        {
            Failed = true;
            Error = why;
            Debug.LogWarning($"[Voice] the offline recogniser didn't start: {why}");
        }

        // (with the engine's lock, the model loaded)
        static string Recognise(short[] pcm, int count, int rate, IList<string> phrases)
        {
            string grammar = GrammarJson(phrases, Known);
            if (recognizer == IntPtr.Zero || grammar != recognizerGrammar || rate != recognizerRate)
            {
                if (recognizer != IntPtr.Zero) vosk_recognizer_free(recognizer);
                recognizer = vosk_recognizer_new_grm(model, rate, Utf8(grammar));
                recognizerGrammar = recognizer == IntPtr.Zero ? null : grammar;
                recognizerRate = rate;
                if (recognizer == IntPtr.Zero) throw new InvalidOperationException("the recogniser didn't start");
            }
            if (count > 0) vosk_recognizer_accept_waveform_s(recognizer, pcm, count);
            string json = FromUtf8(vosk_recognizer_final_result(recognizer));
            vosk_recognizer_reset(recognizer);
            return TextOf(json);
        }

        static bool Known(string word) => vosk_model_find_word(model, Utf8(word)) >= 0;

        /// <summary>For the tests: what's said in `pcm` recognised here and now, on this thread (the
        /// model loaded first if it isn't). Null: no recogniser (Error says why).</summary>
        public static string RecognizeNow(short[] pcm, int count, int rate, IList<string> phrases)
        {
            if (!Present) return null;
            Paths();
            lock (engine)
            {
                if (!Load()) return null;
                try { return Recognise(pcm, Math.Min(count, pcm.Length), rate, phrases); }
                catch (Exception e) { Error = e.Message; return null; }
            }
        }

        /// <summary>For the tests: does the model know a word (false: no model)?</summary>
        public static bool Knows(string word)
        {
            if (!Present) return false;
            Paths();
            lock (engine) return Load() && Known(word);
        }

        /// <summary>The model and its recogniser freed (loaded again when next asked for).</summary>
        public static void Unload()
        {
            lock (engine)
            {
                try
                {
                    if (recognizer != IntPtr.Zero) vosk_recognizer_free(recognizer);
                    if (model != IntPtr.Zero) vosk_model_free(model);
                }
                catch (Exception) { }
                recognizer = IntPtr.Zero;
                model = IntPtr.Zero;
                recognizerGrammar = null;
            }
        }

        static byte[] Utf8(string s) => Encoding.UTF8.GetBytes((s ?? "") + "\0");

        static string FromUtf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            int n = 0;
            while (Marshal.ReadByte(p, n) != 0) n++;
            var bytes = new byte[n];
            Marshal.Copy(p, bytes, 0, n);
            return Encoding.UTF8.GetString(bytes);
        }
#else
        static bool Built => false;
        public static void Preload() { }
        public static int Submit(short[] pcm, int count, int rate, IList<string> phrases) => -1;
        public static bool TryTake(out int ticket, out string text) { ticket = -1; text = null; return false; }
        public static string RecognizeNow(short[] pcm, int count, int rate, IList<string> phrases) => null;
        public static bool Knows(string word) => false;
        public static void Unload() { }
#endif
    }
}

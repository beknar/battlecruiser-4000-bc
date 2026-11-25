// (a microphone of one's own, recognised by Vosk: the Windows and Linux players, and the editor
// on either; the same guard as VoskRecognizer's and WingmanVoice.Live's, for this file only)
#if UNITY_EDITOR_WIN || UNITY_EDITOR_LINUX || ((UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX) && !UNITY_EDITOR)
#define CARRIER_LOCAL_MIC
#endif
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using UnityEngine.Windows.Speech;
#endif

namespace CarrierGame.Demo
{
    /// <summary>
    /// Voice commands for the AI wingman: while push-to-talk is held (the ` key, or the mouse's
    /// back button), what the player says is recognised on their own machine and sent on the comms
    /// channel as an order (VoiceCommand.ToChat, CoopChat.Say), as if it had been typed.
    ///   - Windows, "Windows default" in the pause menu's Microphone: the speech recognition built
    ///     into Windows (UnityEngine.Windows.Speech), which always listens on the default recording
    ///     device. Dictation, for anything said (it sends the audio to Microsoft to recognise, and
    ///     needs Windows' "Online speech recognition" on); without it, offline keywords: the
    ///     orders, the wingmen's names and the places of the system.
    ///   - Windows with a microphone chosen, and Linux (where it's the only way): the device is
    ///     recorded with Unity's Microphone while voice commands are on, push-to-talk takes what
    ///     was said from its ring (from just before the key went down, so the first word isn't
    ///     clipped), and Vosk recognises it offline on a worker thread (VoskRecognizer), listening
    ///     for the orders, the wingmen's names and the places it knows. A place it doesn't know
    ///     comes out as "[unk]", and the order isn't sent: a notice says it may need typing.
    ///   - The web: the browser's SpeechRecognition (Chrome and Edge; it sends the audio to the
    ///     browser maker's servers), on a secure (https) page only. Its best few guesses come in,
    ///     and the first that reads as an order is sent (VoiceCommand.Choose). On Chrome and Edge
    ///     135 and later the microphone can be chosen in the pause menu (Microphone).
    /// Off until turned on in the pause menu (Voice commands), and remembered. A HUD mark shows
    /// while it listens. -voicetest (?voicetest) feeds it phrases instead of a microphone.
    /// </summary>
    public class VoiceInput : MonoBehaviour
    {
        const string PrefKey = "voice.commands";
        public static VoiceInput Instance { get; private set; }
        public enum State { Idle, Listening, Working }
        /// <summary>What it's doing: listening (push-to-talk held), working on what it heard, or neither.</summary>
        public static State Now { get; private set; }
        /// <summary>The last message it sent (for the tests).</summary>
        public static string LastSent { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; Now = State.Idle; LastSent = null; LastCorrected = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null || Application.isBatchMode && !LaunchArgs.Switch("voicetest")) return;
            var go = new GameObject("Voice input");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<VoiceInput>();
        }

        /// <summary>Voice commands turned on (the pause menu's setting, kept).</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 0) == 1;
            set { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Can this game listen at all (null: yes; else why not, for the menu)?</summary>
        public static string WhyNot
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                int s = CarrierVoiceSupported();
                return s == 1 ? null : s == 2 ? "needs the page on https" : "not in this browser (Chrome, Edge or Safari have it)";
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                // (Windows' own, or a microphone of one's own for Vosk)
                if (WindowsDefault || OfflineUsable && Devices().Length > 0) return null;
                return "Windows speech recognition isn't on this machine";
#elif CARRIER_LOCAL_MIC
                if (!VoskRecognizer.Present) return "the offline recogniser isn't with this game (vosk-model)";
                if (VoskRecognizer.Failed) return $"the offline recogniser didn't start ({VoskRecognizer.Error})";
                return Devices().Length > 0 ? null : "no microphone found";
#else
                return "not on this system";
#endif
            }
        }

        /// <summary>The pause menu's line for it.</summary>
        public static string MenuLine
        {
            get
            {
                string why = WhyNot;
                if (why != null) return $"Voice commands: {why}";
                return !Enabled ? "Voice commands: off" : LaunchArgs.OnWeb ? "Voice commands: hold `" : "Voice commands: hold ` or the mouse's back button";
            }
        }

        /// <summary>The pause menu's Voice commands: on or off (off, or turned on with a word on
        /// where the speech goes).</summary>
        public static void Toggle()
        {
            if (WhyNot != null) return;
            Enabled = !Enabled;
            if (Enabled)
#if UNITY_WEBGL && !UNITY_EDITOR
                Notices.Post("Voice commands on: hold ` and speak. The browser sends what you say to its maker to recognise it, and asks for the microphone the first time.");
#else
                Notices.Post(UsesOffline
                    ? $"Voice commands on: hold ` (or the mouse's back button) and speak. {ChosenDevice()} is heard on this machine, for the orders, the wingmen's names and the places it knows."
                    : "Voice commands on: hold ` (or the mouse's back button) and speak. Windows dictation sends what you say to Microsoft to recognise it; without it, a short list of orders is heard offline.");
#endif
        }

        string lastBot;
        bool talking;
        float workingUntil, stopAt = -1f;

        void Start()
        {
            if (LaunchArgs.Switch("voicetest")) StartCoroutine(SelfTest());
#if UNITY_WEBGL && !UNITY_EDITOR
            if (MicChoice && Mic.Length > 0) CarrierVoiceSetMic(Mic);
#endif
        }

        void OnDestroy()
        {
            StopListening();
            StopRecording();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            dictation?.Dispose();
            keywords?.Dispose();
            dictation = null;
            keywords = null;
#endif
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            PollWeb();
            KeepRecording();
            PollOffline();
            if (stopAt >= 0f && Time.unscaledTime >= stopAt) { stopAt = -1f; StopListening(); if (Now == State.Working) Now = State.Idle; }
            if (takeAt >= 0f && Time.unscaledTime >= takeAt) TakeWhatWasSaid();
            if (Now == State.Working && Time.unscaledTime > workingUntil) { Now = State.Idle; StopListening(); }
            if (!Enabled || WhyNot != null) { if (talking) End(); return; }
            // (not while typing, or a menu has the keys)
            bool free = InputFocus.Holder == InputFocus.Panel.None || InputFocus.Holder == InputFocus.Panel.ModeMenu;
            var k = Keyboard.current;
            var m = Mouse.current;
            bool held = free && ((k != null && k.backquoteKey.isPressed) || (m != null && m.backButton.isPressed && !LaunchArgs.OnWeb));
            if (held && !talking) Begin();
            else if (!held && talking) End();
        }

        void Begin()
        {
            // (the last one not yet taken from the ring: taken now, before this one)
            if (takeAt >= 0f) TakeWhatWasSaid();
            talking = true;
            Now = State.Listening;
            // (a microphone of one's own, recording already: what's said is taken from its ring)
            offlineTalk = BeginOffline();
            if (!offlineTalk) StartListening();
        }

        void End()
        {
            talking = false;
            // (what was said is still coming: a moment to finish it)
            Now = State.Working;
            workingUntil = Time.unscaledTime + 3f;
            if (offlineTalk)
            {
                // (the last word's end, a moment after the key; then Vosk, given time for a slow first load)
                takeAt = Time.unscaledTime + 0.3f;
                workingUntil = Time.unscaledTime + 15f;
                return;
            }
            FinishListening();
        }

        /// <summary>What was heard: sent as an order to a wingman (or, not sure of it, not).</summary>
        public void Heard(string text, bool sure, IList<string> botsForTest = null) => Heard(new[] { text }, sure, botsForTest);

        /// <summary>What was heard, as the recogniser's best guesses (best first): the first that
        /// reads as an order is sent (or, not sure of them, none). What was put right on the way
        /// (a misheard name or order) is said in the notices.</summary>
        public void Heard(IList<string> guesses, bool sure, IList<string> botsForTest = null)
        {
            var heard = (guesses ?? new string[0]).Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
            if (heard.Count == 0) return;
            string text = heard[0].Trim();
            if (!sure) { Notices.Post($"Didn't catch that (heard \"{text}\")"); return; }
            var bots = botsForTest ?? Bots();
            if (bots.Count == 0) { Notices.Post($"Heard \"{text}\": no AI wingman in the game to give it to"); return; }
            string msg = VoiceCommand.Choose(heard, bots, lastBot, out string to, out string corrected, botsForTest == null ? People() : null);
            if (msg == null) return;
            if (to != null) lastBot = to;
            LastSent = msg;
            LastCorrected = corrected;
            if (corrected != null) Notices.Post($"Heard {corrected}: sent \"{msg}\"");
            if (botsForTest == null) CoopChat.Say(msg, Via.Voice);
        }

        /// <summary>What the last message had put right (for the tests; null: nothing).</summary>
        public static string LastCorrected { get; private set; }

        /// <summary>The other players (not AI wingmen) in the game, by name.</summary>
        static List<string> People()
        {
            var c = CoopClient.Instance;
            if (c == null || !CoopClient.Active) return new List<string>();
            return c.Players.Where(p => p != null && p.id != c.MyId && !p.ai && !CoopBot.IsBot(p)).Select(p => p.name).ToList();
        }

        /// <summary>The AI wingmen in the game, by name.</summary>
        static List<string> Bots()
        {
            var c = CoopClient.Instance;
            if (c == null || !CoopClient.Active) return new List<string>();
            return c.Players.Where(p => p != null && p.id != c.MyId && (p.ai || CoopBot.IsBot(p))).Select(p => p.name).ToList();
        }

        System.Collections.IEnumerator SelfTest()
        {
            yield return new WaitForSecondsRealtime(1f);
            var bots = new List<string> { "Wingman" };
            int bad = 0;
            foreach (var (said, want) in new[] { ("um mine some asteroids please", "Wingman, mine some asteroids please"),
                                                 ("warp to Heaven then dock", "Wingman, warp to Heaven then dock"),
                                                 ("give me two thousand five hundred credits", "Wingman, give me 2500 credits"),
                                                 ("hey wingman follow me", "Hey wingman follow me"),
                                                 ("all ships regroup", "All ships regroup"),
                                                 ("women mine", "Wingman, mine"),
                                                 ("wingman cellar", "Wingman sell ore") })
            {
                Heard(said, true, bots);
                bool ok = LastSent == want;
                if (!ok) bad++;
                Debug.Log($"[VoiceTest] {(ok ? "ok" : "FAIL")}: heard \"{said}\": sent \"{LastSent}\"");
            }
            Debug.Log(bad == 0 ? "[VoiceTest] done: all sent as they should be" : $"[VoiceTest] done: {bad} FAILED");
            if (LaunchArgs.Has("-voicetest") && Application.isBatchMode) Application.Quit();
        }

        // ------------------------------------------------------------ the recognisers

        void StartListening()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            CarrierVoiceStart();
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            WinStart();
#endif
        }

        void FinishListening()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            CarrierVoiceStop();
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // (given a moment after the key is let go to finish the phrase, dictation or keywords)
            stopAt = Time.unscaledTime + 1.5f;
#endif
        }

        void StopListening()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            CarrierVoiceStop();
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (dictation != null && dictation.Status == SpeechSystemStatus.Running) dictation.Stop();
            if (keywords != null && keywords.IsRunning) keywords.Stop();
#endif
        }

        void PollWeb()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string got;
            while ((got = CarrierVoicePoll()) != null)
            {
                var parts = got.Split(new[] { '|' }, 3);
                if (parts.Length < 3) continue;
                if (parts[0] == "ok")
                {
                    float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float conf);
                    // (0: the browser gave no confidence; the best guess's; the guesses tab-separated)
                    Heard(parts[2].Split('\t'), conf == 0f || conf >= 0.45f);
                }
                else if (parts[0] == "error" && parts[2] != "no-speech" && parts[2] != "aborted")
                    Notices.Post(parts[2] == "not-allowed" ? "Voice commands: the browser won't let the game have the microphone" : $"Voice commands: {parts[2]}");
                else if (parts[0] == "end" && Now == State.Working) Now = State.Idle;
            }
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int CarrierVoiceSupported();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void CarrierVoiceStart();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void CarrierVoiceStop();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern string CarrierVoicePoll();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int CarrierVoiceMicChoice();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void CarrierVoiceListMics();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern string CarrierVoiceMics();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void CarrierVoiceSetMic(string id);
#endif

        // ------------------------------------------------------------ the microphone (the web)

        const string MicKey = "voice.mic";
        /// <summary>The microphones to choose from (device id, name), as the browser last listed them.</summary>
        static List<(string id, string name)> mics = new List<(string id, string name)>();
        static bool micsAsked;

        /// <summary>Does the pause menu have a Microphone line? On the web, yes: a choice where the
        /// browser can take one (Chrome and Edge 135 and later), else "browser default". On Windows
        /// and Linux, yes: the devices (and on Windows, "Windows default" first).</summary>
        public static bool MicMenu => LaunchArgs.OnWeb || LocalMics;

        /// <summary>Is the Microphone line this machine's own devices (Windows and Linux, not the web)?</summary>
        static bool LocalMics
        {
            get
            {
#if CARRIER_LOCAL_MIC
                return !LaunchArgs.OnWeb;
#else
                return false;
#endif
            }
        }

        /// <summary>Can the microphone be chosen here?</summary>
        public static bool MicChoice
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return CarrierVoiceMicChoice() == 1;
#else
                return false;
#endif
            }
        }

        /// <summary>The chosen microphone's device id ("": the default).</summary>
        public static string Mic
        {
            get => PlayerPrefs.GetString(MicKey, "");
            set
            {
                PlayerPrefs.SetString(MicKey, value ?? "");
                PlayerPrefs.Save();
#if UNITY_WEBGL && !UNITY_EDITOR
                CarrierVoiceSetMic(value ?? "");
#endif
            }
        }

        /// <summary>The pause menu's line for the microphone.</summary>
        public static string MicLine
        {
            get
            {
                if (LocalMics) return LocalMicLine();
                if (!MicChoice) return "Microphone: browser default";
                AskForMics();
                string id = Mic;
                if (id.Length == 0) return "Microphone:  <   default   >";
                var m = mics.FirstOrDefault(x => x.id == id);
                return $"Microphone:  <   {(m.id == null ? "(not plugged in)" : MicName(m))}   >";
            }
        }

        static string MicName((string id, string name) m) => string.IsNullOrEmpty(m.name) ? "allow the microphone first (hold ` once)" : m.name;

        /// <summary>The next microphone (dir +1) or the one before (-1), the default among them.</summary>
        public static void StepMic(int dir)
        {
            if (LocalMics)
            {
                var d = Devices();
                if (d.Length == 0 || !OfflineUsable) return;
                Mic = NextMic(d, Mic, dir, WindowsDefault);
                return;
            }
            if (!MicChoice) return;
            AskForMics(again: true);
            Mic = NextMic(mics.Select(m => m.id).ToList(), Mic, dir);
        }

        /// <summary>Of `ids` (with "", the default, before them, if `withDefault`), the one `dir`
        /// steps from `current`. One not there (unplugged, or none chosen) steps from the first.</summary>
        public static string NextMic(IList<string> ids, string current, int dir, bool withDefault = true)
        {
            var all = new List<string>();
            if (withDefault) all.Add("");
            all.AddRange(ids.Where(x => !string.IsNullOrEmpty(x)));
            if (all.Count == 0) return "";
            int i = Mathf.Max(0, all.IndexOf(current ?? ""));
            return all[((i + dir) % all.Count + all.Count) % all.Count];
        }

        static void AskForMics(bool again = false)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string got = CarrierVoiceMics();
            if (!string.IsNullOrEmpty(got)) mics = ParseMics(got);
            if (!micsAsked || again) { micsAsked = true; CarrierVoiceListMics(); }
#endif
        }

        /// <summary>The browser's list: a line a microphone: its device id, a tab, its name.</summary>
        public static List<(string id, string name)> ParseMics(string list)
        {
            var found = new List<(string id, string name)>();
            foreach (var line in (list ?? "").Split('\n'))
            {
                var p = line.Split(new[] { '\t' }, 2);
                // (Chrome's own aliases for the default devices: the default is its own choice here)
                if (p[0].Length == 0 || p[0] == "default" || p[0] == "communications") continue;
                found.Add((p[0], p.Length > 1 ? p[1] : ""));
            }
            return found;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        DictationRecognizer dictation;
        KeywordRecognizer keywords;
        string keywordsFor;
        int dictationFailures;

        void WinStart()
        {
            stopAt = -1f;
            if (dictationFailures < 2)
            {
                try
                {
                    // (the keyword system and dictation can't run at once)
                    if (PhraseRecognitionSystem.Status == SpeechSystemStatus.Running) PhraseRecognitionSystem.Shutdown();
                    if (dictation == null)
                    {
                        dictation = new DictationRecognizer();
                        dictation.DictationResult += (text, confidence) =>
                            Heard(text, confidence != ConfidenceLevel.Rejected && confidence != ConfidenceLevel.Low);
                        dictation.DictationComplete += cause =>
                        {
                            // (it stops by itself after a silence: again, while the key is still held)
                            if ((cause == DictationCompletionCause.TimeoutExceeded || cause == DictationCompletionCause.Complete) && talking)
                                dictation.Start();
                            else if (cause == DictationCompletionCause.MicrophoneUnavailable)
                                Notices.Post("Voice commands: no microphone (or Windows won't let the game have it: Settings > Privacy > Microphone)");
                            else if (cause == DictationCompletionCause.NetworkFailure)
                                Notices.Post("Voice commands: dictation needs the internet (it's recognised by Microsoft)");
                        };
                        dictation.DictationError += (error, hresult) =>
                        {
                            // (dictation off in Windows' settings, or no microphone allowed: keywords
                            // instead, after a second failure, not for one that may pass)
                            dictationFailures++;
                            Notices.Post($"Voice commands: Windows dictation isn't available ({error}); listening for a short list of orders instead. " +
                                         "Dictation needs Settings > Privacy > Speech: Online speech recognition, and the microphone allowed.");
                            if (Now != State.Idle) { Now = State.Idle; talking = false; }
                        };
                    }
                    if (dictation.Status != SpeechSystemStatus.Running) dictation.Start();
                    return;
                }
                catch (System.Exception e)
                {
                    dictationFailures++;
                    Debug.Log($"[Voice] no dictation: {e.Message}");
                }
            }
            try
            {
                if (dictation != null && dictation.Status == SpeechSystemStatus.Running) dictation.Stop();
                var bots = Bots().DefaultIfEmpty("Wingman").ToList();
                var places = Places().ToList();
                string sig = string.Join("|", bots) + "#" + string.Join("|", places);
                if (keywords != null && keywordsFor != sig) { keywords.Dispose(); keywords = null; }
                if (keywords == null)
                {
                    keywords = new KeywordRecognizer(VoiceCommand.Phrases(bots, places), ConfidenceLevel.Medium);
                    keywords.OnPhraseRecognized += a => Heard(a.text, true);
                    keywordsFor = sig;
                }
                if (PhraseRecognitionSystem.Status != SpeechSystemStatus.Running) PhraseRecognitionSystem.Restart();
                if (!keywords.IsRunning) keywords.Start();
            }
            catch (System.Exception e)
            {
                Notices.Post($"Voice commands: Windows speech recognition didn't start ({e.Message})");
                Debug.LogWarning($"[Voice] keywords: {e}");
                talking = false;
                Now = State.Idle;
            }
        }
#endif

        // ------------------------------------------------------------ a microphone of one's own (Windows and Linux: Vosk)

        /// <summary>Is Windows' own speech recognition here (the Microphone line's "Windows default")?</summary>
        static bool WindowsDefault
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return PhraseRecognitionSystem.isSupported;
#else
                return false;
#endif
            }
        }

        /// <summary>Can Vosk be used here: the library and the model with the game, and it hasn't failed?</summary>
        static bool OfflineUsable => LocalMics && VoskRecognizer.Present && !VoskRecognizer.Failed;

#if CARRIER_LOCAL_MIC
        static string[] devices = new string[0];
        static float devicesAt = -1f;
#endif

        /// <summary>This machine's recording devices, by name (Unity's Microphone.devices, looked at
        /// again every two seconds; none on the web).</summary>
        static string[] Devices()
        {
#if CARRIER_LOCAL_MIC
            float now = Time.unscaledTime;
            if (devicesAt < 0f || now - devicesAt > 2f || now < devicesAt)
            {
                devicesAt = now;
                try { devices = Microphone.devices ?? new string[0]; }
                catch (System.Exception) { devices = new string[0]; }
            }
            return devices;
#else
            return new string[0];
#endif
        }

        /// <summary>The device Vosk hears (null: none; on Windows, its own recognition instead): the
        /// one chosen if it's plugged in, else (on Linux, with no "Windows default") the first.</summary>
        static string ChosenDevice()
        {
            if (!OfflineUsable) return null;
            var d = Devices();
            string m = Mic;
            if (m.Length > 0 && d.Contains(m)) return m;
            return WindowsDefault ? null : d.FirstOrDefault();
        }

        /// <summary>Is push-to-talk heard by Vosk, from a device of one's own (not Windows' own)?</summary>
        static bool UsesOffline => ChosenDevice() != null;

        /// <summary>The Microphone line on Windows and Linux: "Windows default" (Windows only) or a device.</summary>
        static string LocalMicLine()
        {
            bool windows = WindowsDefault;
            string none = windows ? "Windows default" : "none";
            if (!VoskRecognizer.Present) return $"Microphone: {none} (the offline recogniser isn't with this game)";
            if (VoskRecognizer.Failed) return $"Microphone: {none} (the offline recogniser didn't start)";
            if (Devices().Length == 0) return windows ? "Microphone: Windows default (no other found)" : "Microphone: none found";
            string dev = ChosenDevice();
            string m = Mic;
            string shown = dev ?? "Windows default";
            if (m.Length > 0 && dev != m) shown = $"(not plugged in) {shown}";
            return $"Microphone:  <   {shown}   >";
        }

#if CARRIER_LOCAL_MIC
        AudioClip micClip;
        string micDevice;
        int micRate, micStart;
        float micSince, micRetryAt;
#endif
        bool offlineTalk;
        float takeAt = -1f;
        int offlinePending;

        /// <summary>The chosen device kept recording while voice commands are on (into a ring of
        /// 10 s), so push-to-talk has what was said from just before the key went down; stopped
        /// otherwise. Its first start also gets Vosk ready (the model loads on the worker).</summary>
        void KeepRecording()
        {
#if CARRIER_LOCAL_MIC
            string want = Enabled && WhyNot == null ? ChosenDevice() : null;
            if (want == micDevice && (want == null || Microphone.IsRecording(want))) return;
            // (the same device stopped by itself, unplugged: tried again in a moment)
            bool dropped = want != null && want == micDevice;
            StopRecording();
            if (dropped) micRetryAt = Time.unscaledTime + 3f;
            if (want == null || Time.unscaledTime < micRetryAt) return;
            Microphone.GetDeviceCaps(want, out int min, out int max);
            int rate = min == 0 && max == 0 ? VoskRate : Mathf.Clamp(VoskRate, min, max);
            micClip = Microphone.Start(want, true, 10, rate);
            if (micClip == null)
            {
                micRetryAt = Time.unscaledTime + 3f;
                Debug.LogWarning($"[Voice] {want} didn't start recording");
                return;
            }
            micDevice = want;
            micRate = micClip.frequency;
            VoskRecognizer.Preload();
#endif
        }

        // (what Vosk's model was made for; another rate is resampled by it)
        const int VoskRate = 16000;

        void StopRecording()
        {
#if CARRIER_LOCAL_MIC
            if (micDevice != null)
                try { Microphone.End(micDevice); } catch (System.Exception) { }
            micDevice = null;
            micClip = null;
#endif
        }

        /// <summary>Push-to-talk down, with the device recording: where what's said starts in the
        /// ring (0.3 s before now). False: not recording (Windows' own recognition, or nothing).</summary>
        bool BeginOffline()
        {
#if CARRIER_LOCAL_MIC
            if (micClip == null || micDevice == null || !OfflineUsable) return false;
            int n = micClip.samples;
            micStart = ((Microphone.GetPosition(micDevice) - micRate * 3 / 10) % n + n) % n;
            micSince = Time.unscaledTime;
            return true;
#else
            return false;
#endif
        }

        /// <summary>A moment after push-to-talk is let go: what was said taken from the ring, as
        /// 16-bit samples, and given to Vosk on its worker, with the grammar of the orders, the
        /// wingmen's names and the places.</summary>
        void TakeWhatWasSaid()
        {
            takeAt = -1f;
#if CARRIER_LOCAL_MIC
            if (micClip == null || micDevice == null) { if (Now == State.Working && offlinePending == 0) Now = State.Idle; return; }
            int n = micClip.samples, ch = Mathf.Max(1, micClip.channels);
            int end = Microphone.GetPosition(micDevice);
            int len = ((end - micStart) % n + n) % n;
            // (held longer than the ring holds: its last 9.5 s)
            if ((Time.unscaledTime - micSince + 0.3f) * micRate >= n - micRate / 2) len = n - micRate / 2;
            var ring = new float[n * ch];
            micClip.GetData(ring, 0);
            var pcm = new short[len];
            int start = ((end - len) % n + n) % n;
            for (int i = 0; i < len; i++)
                pcm[i] = (short)Mathf.Clamp(Mathf.RoundToInt(ring[((start + i) % n) * ch] * 32767f), -32768, 32767);
            var bots = Bots().DefaultIfEmpty("Wingman").ToList();
            if (VoskRecognizer.Submit(pcm, len, micRate, VoiceCommand.GrammarPhrases(bots, Places().ToList())) >= 0) offlinePending++;
            else if (Now == State.Working && offlinePending == 0) Now = State.Idle;
#else
            if (Now == State.Working) Now = State.Idle;
#endif
        }

        /// <summary>Vosk's results, as they come from its worker.</summary>
        void PollOffline()
        {
            while (VoskRecognizer.TryTake(out _, out string text))
            {
                if (offlinePending > 0) offlinePending--;
                if (offlinePending == 0 && Now == State.Working) Now = State.Idle;
                if (text == null) { Notices.Post($"Voice commands: the offline recogniser didn't work ({VoskRecognizer.Error ?? "no reason given"})"); continue; }
                HeardOffline(text, null);
            }
        }

        /// <summary>What Vosk heard (lower case, "[unk]" for a word it doesn't know): sent as Heard
        /// sends it, its "[unk]"s left out, but not with a place it doesn't know where the place
        /// goes ("warp to [unk]"): a notice says place names may need typing instead.</summary>
        public void HeardOffline(string text, IList<string> botsForTest = null)
        {
            string said = VoskRecognizer.WithoutUnknowns(text, out bool unknown, out bool nameMissing);
            if (nameMissing)
            {
                string shown = (text ?? "").Replace(VoskRecognizer.Unknown, "...");
                Notices.Post($"Heard \"{shown}\": a name the offline recogniser doesn't know. Place names may need typing" +
                             (WindowsDefault ? " (or the Microphone set to Windows default)." : "."));
                return;
            }
            if (said.Length == 0) { if (unknown) Notices.Post("Didn't catch that"); return; }
            Heard(said, true, botsForTest);
        }

        /// <summary>The places to name in the keyword list: every warp point and asteroid field.</summary>
        static IEnumerable<string> Places() =>
            WarpPoint.All.Where(w => w != null && !string.IsNullOrEmpty(w.displayName)).Select(w => w.displayName).Distinct();

        // ------------------------------------------------------------ the HUD mark

        void OnGUI()
        {
            if (Now == State.Idle || !Enabled) return;
            float s = Hud.S;
            string text = Now == State.Listening ? "● LISTENING" : "… WORKING ON IT";
            var r = new Rect((Screen.width - 220f * s) * 0.5f, Screen.height * 0.14f, 220f * s, 30f * s);
            Hud.Fill(r, new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(r, Now == State.Listening ? $"<color=#ff6a5a>{text}</color>" : $"<color=#ffd28a>{text}</color>", Hud.Text(15, true, TextAnchor.MiddleCenter));
        }
    }
}

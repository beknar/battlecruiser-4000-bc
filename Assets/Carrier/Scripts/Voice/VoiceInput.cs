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
    ///   - Windows: the speech recognition built into Windows (UnityEngine.Windows.Speech).
    ///     Dictation, for anything said (it sends the audio to Microsoft to recognise, and needs
    ///     Windows' "Online speech recognition" on); without it, offline keywords: the orders,
    ///     the wingmen's names and the places of the system.
    ///   - The web: the browser's SpeechRecognition (Chrome and Edge; it sends the audio to the
    ///     browser maker's servers), on a secure (https) page only.
    ///   - Linux: none.
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
        static void ResetStatics() { Instance = null; Now = State.Idle; LastSent = null; }

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
                return s == 1 ? null : s == 2 ? "needs the page on https" : "not in this browser (Chrome or Edge have it)";
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return PhraseRecognitionSystem.isSupported ? null : "Windows speech recognition isn't on this machine";
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
                return Enabled ? "Voice commands: hold ` or the mouse's back button" : "Voice commands: off";
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
                Notices.Post("Voice commands on: hold ` (or the mouse's back button) and speak. The browser sends what you say to its maker to recognise it, and asks for the microphone the first time.");
#else
                Notices.Post("Voice commands on: hold ` (or the mouse's back button) and speak. Windows dictation sends what you say to Microsoft to recognise it; without it, a short list of orders is heard offline.");
#endif
        }

        string lastBot;
        bool talking;
        float workingUntil;

        void Start()
        {
            if (LaunchArgs.Switch("voicetest")) StartCoroutine(SelfTest());
        }

        void OnDestroy()
        {
            StopListening();
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
            if (Now == State.Working && Time.unscaledTime > workingUntil) { Now = State.Idle; StopListening(); }
            if (!Enabled || WhyNot != null) { if (talking) End(); return; }
            // (not while typing, or a menu has the keys)
            bool free = InputFocus.Holder == InputFocus.Panel.None || InputFocus.Holder == InputFocus.Panel.ModeMenu;
            var k = Keyboard.current;
            var m = Mouse.current;
            bool held = free && ((k != null && k.backquoteKey.isPressed) || (m != null && m.backButton.isPressed));
            if (held && !talking) Begin();
            else if (!held && talking) End();
        }

        void Begin()
        {
            talking = true;
            Now = State.Listening;
            StartListening();
        }

        void End()
        {
            talking = false;
            // (what was said is still coming: a moment to finish it)
            Now = State.Working;
            workingUntil = Time.unscaledTime + 3f;
            FinishListening();
        }

        /// <summary>What was heard: sent as an order to a wingman (or, not sure of it, not).</summary>
        public void Heard(string text, bool sure, IList<string> botsForTest = null)
        {
            if (Now == State.Working) { Now = State.Idle; StopListening(); }
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!sure) { Notices.Post($"Didn't catch that (heard \"{text.Trim()}\")"); return; }
            var bots = botsForTest ?? Bots();
            if (bots.Count == 0) { Notices.Post($"Heard \"{text.Trim()}\": no AI wingman in the game to give it to"); return; }
            string msg = VoiceCommand.ToChat(text, bots, lastBot);
            if (msg == null) return;
            foreach (var b in bots) if (msg.StartsWith(b + ",")) lastBot = b;
            LastSent = msg;
            if (botsForTest == null) CoopChat.Say(msg);
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
            foreach (var said in new[] { "um mine some asteroids please", "warp to Heaven then dock", "give me two thousand five hundred credits",
                                         "hey wingman follow me", "all ships regroup" })
            {
                Heard(said, true, bots);
                Debug.Log($"[VoiceTest] heard \"{said}\": sent \"{LastSent}\"");
            }
            Debug.Log("[VoiceTest] done");
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
            // (dictation: given a moment after the key is let go to finish the phrase; keywords stop now)
            if (keywords != null && keywords.IsRunning) keywords.Stop();
            if (dictation != null && dictation.Status == SpeechSystemStatus.Running) Invoke(nameof(StopListening), 1.5f);
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
                    // (0: the browser gave no confidence)
                    Heard(parts[2], conf == 0f || conf >= 0.45f);
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
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        DictationRecognizer dictation;
        KeywordRecognizer keywords;
        bool noDictation;

        void WinStart()
        {
            CancelInvoke(nameof(StopListening));
            if (!noDictation)
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
                        dictation.DictationError += (error, hresult) =>
                        {
                            // (dictation off in Windows' settings, or no microphone allowed: keywords instead)
                            noDictation = true;
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
                    noDictation = true;
                    Debug.Log($"[Voice] no dictation: {e.Message}");
                }
            }
            try
            {
                if (dictation != null && dictation.Status == SpeechSystemStatus.Running) dictation.Stop();
                if (keywords == null)
                {
                    keywords = new KeywordRecognizer(VoiceCommand.Phrases(Bots().DefaultIfEmpty("Wingman"), Places()), ConfidenceLevel.Medium);
                    keywords.OnPhraseRecognized += a => Heard(a.text, true);
                }
                if (PhraseRecognitionSystem.Status != SpeechSystemStatus.Running) PhraseRecognitionSystem.Restart();
                if (!keywords.IsRunning) keywords.Start();
            }
            catch (System.Exception e)
            {
                Notices.Post($"Voice commands: Windows speech recognition didn't start ({e.Message})");
                Enabled = false;
                talking = false;
                Now = State.Idle;
            }
        }
#endif

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

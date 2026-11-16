using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Plays the soundtrack, a playlist of calm tracks and one of combat tracks, crossfading.
    ///
    /// The playlist (Resources/CarrierAudio/Music/playlist.txt, written by
    /// Tools/Audio/stage_music.sh) lists each track, which of the two it is in, and the same piece's
    /// other arrangement where there is one. While it is calm a calm track plays; while weapons and
    /// explosions are heard nearby, or enemies are out of hiding close by, a combat track; it eases
    /// back once things have been quiet. Going from one to the other, a piece with an arrangement
    /// for both carries on in step in the other arrangement; otherwise another track starts from
    /// its top.
    ///
    /// Whichever is playing, the music changes to another track of the two every cycleMin to
    /// cycleMax seconds (picked afresh each time; never more than three minutes), the next fading
    /// in as the last fades out (equal power, so the level holds through the change). It goes
    /// through every track of a list, shuffled, before it plays one again.
    ///
    /// The music can be turned off (On, kept between games: the pause menu, or Shift+N for the
    /// next song) without the rest of the sound; Next() skips to another song now.
    ///
    /// A scene can choose its own two tracks instead (UseTracks: the asteroid ambush's quiet
    /// travelling track until the ambush is sprung, EnterCombat, then its battle track).
    /// Only the tracks playing are loaded; one that has faded out is unloaded.
    /// </summary>
    public class MusicDirector : MonoBehaviour
    {
        public float calmVolume = 0.30f;
        public float combatVolume = 0.34f;
        [Tooltip("Seconds without combat sounds before the music calms down.")]
        public float combatHold = 10f;
        public float fadeToCombat = 2.5f;
        public float fadeToCalm = 6f;
        [Tooltip("The music changes after this many seconds at the least, and the most (picked afresh each time).")]
        public float cycleMin = 60f, cycleMax = 180f;
        [Tooltip("Seconds the change from one track to the next takes.")]
        public float cycleFade = 6f;
        [Tooltip("Seconds the music takes to fade out when it is turned off.")]
        public float offFade = 1f;

        /// <summary>Tracks changed to since the level started (for the self-test).</summary>
        public static int Cycles { get; private set; }
        /// <summary>0 = calm, 1 = combat (how far the music is into the combat tracks).</summary>
        public static float CombatMix { get; private set; }
        /// <summary>The music is playing (there is some, and it is on).</summary>
        public static bool Playing { get; private set; }
        /// <summary>The title of the track that is mostly audible now ("" with none).</summary>
        public static string NowPlaying { get; private set; } = "";
        /// <summary>How many tracks each list has (for the self-test and the menu).</summary>
        public static int CalmCount { get; private set; }
        public static int CombatCount { get; private set; }
        /// <summary>How loud the music is now, all its tracks together (for the self-test).</summary>
        public static float Volume { get; private set; }

        const string PrefKey = "MusicOn";
        static bool? on;
        /// <summary>The music on or off (the rest of the sound goes on); kept between games.</summary>
        public static bool On
        {
            get { if (on == null) on = PlayerPrefs.GetInt(PrefKey, 1) != 0; return on.Value; }
            set { on = value; PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static MusicDirector Instance { get; private set; }

        // set by the scene before the music starts (see UseTracks)
        static string sceneCalm, sceneCombat;
        static bool synced = true, eventDriven;
        static float forcedUntil = -1f, sceneHold = -1f, sceneFadeIn = -1f;

        class Track
        {
            public bool combat;
            public string path, title;
            public bool loops = true;
            public Track pair;
        }

        /// <summary>A track playing (or fading).</summary>
        class Voice
        {
            public AudioSource src;
            public Track track;
            public float gain, target, rate;
        }

        readonly List<Track> calmTracks = new List<Track>(), combatTracks = new List<Track>();
        readonly List<Track> calmBag = new List<Track>(), combatBag = new List<Track>();
        readonly List<Voice> voices = new List<Voice>();
        readonly List<AudioSource> spare = new List<AudioSource>();
        // the tracks played, the one playing last (for Previous)
        readonly List<Track> history = new List<Track>();
        Voice current;
        float nextChange, changingSince = -1f, lastSync;
        // the fight (or the quiet) the music is following, once it has held a moment
        bool settled;
        bool wasOn = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            sceneCalm = sceneCombat = null;
            synced = true; eventDriven = false;
            forcedUntil = sceneHold = sceneFadeIn = -1f;
            CombatMix = 0f; Playing = false; Cycles = 0; NowPlaying = "";
            CalmCount = CombatCount = 0;
            Volume = 0f;
            on = null;
            Instance = null;
        }

        /// <summary>Pick the scene's tracks (call from a scene object's Awake). With eventDriven the
        /// music stays calm until EnterCombat, then follows the fighting as usual.</summary>
        public static void UseTracks(string calmName, string combatName, bool loopsInSync, bool waitForCombatCue,
                                     float holdAfterFighting = 20f, float fadeIn = 1.2f)
        {
            sceneCalm = calmName; sceneCombat = combatName;
            synced = loopsInSync; eventDriven = waitForCombatCue;
            sceneHold = holdAfterFighting; sceneFadeIn = fadeIn;
        }

        /// <summary>Drop any combat cue still running (the level restarted).</summary>
        public static void ForgetCombat() => forcedUntil = -1f;

        /// <summary>Switch to the combat music now (an ambush), and keep it for at least `seconds`.</summary>
        public static void EnterCombat(float seconds = 15f)
        {
            eventDriven = false;
            forcedUntil = Time.time + seconds;
        }

        /// <summary>Turn the music on or off (says so).</summary>
        public static void Toggle()
        {
            On = !On;
            Notices.Post(On ? "Music on" : "Music off");
        }

        /// <summary>Another song now (of those for calm or combat, whichever is playing).</summary>
        public static void Next()
        {
            if (Instance == null || !Instance.enabled) return;
            if (!On) { On = true; Notices.Post("Music on"); return; }
            Instance.ChangeTrack(Instance.cycleFade * 0.5f, announce: true);
        }

        /// <summary>The song before this one (of the same kind, calm or combat), if there was one.</summary>
        public static void Previous()
        {
            if (Instance == null || !Instance.enabled) return;
            if (!On) { On = true; Notices.Post("Music on"); return; }
            var me = Instance;
            bool combat = me.current != null && me.current.track.combat;
            // (the one playing is the last in the history: the one before it)
            // (the one before of the same kind: not the one playing, nor its other arrangement)
            var now = me.current != null ? me.current.track : null;
            int i = me.history.Count - 1;
            while (i >= 0 && (me.history[i].combat != combat || me.history[i] == now || (now != null && me.history[i] == now.pair))) i--;
            if (i < 0) { Next(); return; }
            var t = me.history[i];
            me.history.RemoveRange(i, me.history.Count - i);
            me.Bring(me.Play(t, 0), me.cycleFade * 0.5f);
            Cycles++;
            if (me.current != null) Notices.Post($"Music: {me.current.track.title}");
        }

        /// <summary>The next track as soon as the music can change (the self-test).</summary>
        public void ChangeTrackSoon() => nextChange = Time.time;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (sceneCalm != null) SceneTracks();
            else if (!ReadPlaylist()) DefaultTracks();
            calmTracks.RemoveAll(t => t.path == null);
            combatTracks.RemoveAll(t => t.path == null);
            CalmCount = calmTracks.Count;
            CombatCount = combatTracks.Count;
            if (calmTracks.Count == 0 || combatTracks.Count == 0) { enabled = false; return; }
            if (sceneHold > 0f) combatHold = sceneHold;
            if (sceneFadeIn > 0f) fadeToCombat = sceneFadeIn;
            wasOn = On;
            if (On) StartFresh(false, 0f);
        }

        // ---- the tracks

        bool ReadPlaylist()
        {
            var text = Resources.Load<TextAsset>(SoundFX.LibraryRoot + "Music/playlist");
            if (text == null) return false;
            var byPath = new Dictionary<string, Track>();
            var pairs = new List<(Track, string)>();
            foreach (var raw in text.text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var f = line.Split('|');
                if (f.Length < 3) continue;
                // the resource path is under the library root, without the extension
                var t = new Track { combat = f[0] == "combat", path = f[1].Trim(), title = f[2].Trim(), loops = f.Length < 4 || f[3].Trim() != "0" };
                byPath[t.path] = t;
                (t.combat ? combatTracks : calmTracks).Add(t);
                if (f.Length >= 5 && f[4].Trim().Length > 0) pairs.Add((t, f[4].Trim()));
            }
            foreach (var (t, p) in pairs) if (byPath.TryGetValue(p, out var other) && other.combat != t.combat) t.pair = other;
            Resources.UnloadAsset(text);
            return calmTracks.Count > 0 && combatTracks.Count > 0;
        }

        // (no playlist staged: the tracks of the first soundtrack, those of them there are)
        void DefaultTracks()
        {
            var calm = new Track { path = Have("Music/Music_Calm"), title = "Void" };
            var calm2 = new Track { path = Have("Music/Travel_Calm"), title = "Deepness of Space" };
            var combat = new Track { combat = true, path = Have("Music/Music_Combat"), title = "Void" };
            calm.pair = combat; combat.pair = calm;
            calmTracks.Add(calm); calmTracks.Add(calm2);
            combatTracks.Add(combat);
        }

        void SceneTracks()
        {
            var calm = new Track { path = Have("Music/" + sceneCalm), title = sceneCalm };
            var combat = new Track { combat = true, path = Have("Music/" + sceneCombat), title = sceneCombat };
            if (synced) { calm.pair = combat; combat.pair = calm; }
            calmTracks.Add(calm);
            combatTracks.Add(combat);
        }

        static string Have(string path) => Load(path) != null ? path : null;

        static AudioClip Load(string path) => Resources.Load<AudioClip>(SoundFX.LibraryRoot + path);

        /// <summary>The next of a list, shuffled: every one before any again, and not the one just
        /// played (if there's another).</summary>
        Track Pick(bool combat, Track not)
        {
            var list = combat ? combatTracks : calmTracks;
            var bag = combat ? combatBag : calmBag;
            if (bag.Count == 0)
            {
                bag.AddRange(list);
                for (int i = bag.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (bag[i], bag[j]) = (bag[j], bag[i]); }
            }
            int k = 0;
            if (bag.Count > 1 && bag[0] == not) k = 1;
            var t = bag[k];
            bag.RemoveAt(k);
            return t;
        }

        // ---- the voices

        Voice Play(Track t, int startSample)
        {
            var clip = Load(t.path);
            if (clip == null) return null;
            AudioSource src;
            if (spare.Count > 0) { src = spare[spare.Count - 1]; spare.RemoveAt(spare.Count - 1); }
            else
            {
                src = gameObject.AddComponent<AudioSource>();
                src.spatialBlend = 0f;
                src.priority = 0;          // never stolen by effects
                src.dopplerLevel = 0f;
                src.playOnAwake = false;
            }
            src.clip = clip;
            src.loop = true;
            src.volume = 0f;
            src.timeSamples = Mathf.Clamp(startSample, 0, Mathf.Max(0, clip.samples - 1));
            src.Play();
            var v = new Voice { src = src, track = t };
            voices.Add(v);
            return v;
        }

        /// <summary>Make `v` the track playing, fading it in (and every other out) over `fade` s.</summary>
        void Bring(Voice v, float fade, bool record = true)
        {
            if (v == null) return;
            float rate = 1f / Mathf.Max(0.05f, fade);
            foreach (var o in voices) { o.target = o == v ? 1f : 0f; o.rate = rate; }
            current = v;
            if (fade <= 0f) v.gain = 1f;
            NowPlaying = v.track.title;
            if (record)
            {
                history.Add(v.track);
                if (history.Count > 40) history.RemoveAt(0);
            }
            // the next change, before a track that isn't a loop comes to its end
            float wait = Random.Range(cycleMin, cycleMax);
            if (!v.track.loops && v.src.clip != null)
                wait = Mathf.Min(wait, Mathf.Max(10f, v.src.clip.length - v.src.time - cycleFade - 1f));
            nextChange = Time.time + wait;
            Debug.Log($"[Music] {(v.track.combat ? "combat" : "calm")}: {v.track.title}");
        }

        void StartFresh(bool combat, float fade)
        {
            var t = Pick(combat, current != null ? current.track : null);
            Bring(Play(t, 0), fade);
        }

        /// <summary>Another track of the same kind now, crossfading.</summary>
        void ChangeTrack(float fade, bool announce = false)
        {
            bool combat = current != null && current.track.combat;
            // (a list of one, the asteroid ambush's: nothing to change to)
            if ((combat ? combatTracks : calmTracks).Count < 2) { nextChange = Time.time + Random.Range(cycleMin, cycleMax); return; }
            StartFresh(combat, fade);
            Cycles++;
            if (announce && current != null) Notices.Post($"Music: {current.track.title}");
        }

        /// <summary>Over to the calm or combat music: the same piece in step if it has the other
        /// arrangement, else another track from its top.</summary>
        void Over(bool combat)
        {
            float fade = combat ? fadeToCombat : fadeToCalm;
            var pair = current != null ? current.track.pair : null;
            if (pair != null && pair.combat == combat)
            {
                // (already there, fading out: bring it back)
                var back = voices.Find(o => o.track == pair);
                Bring(back ?? Play(pair, current.src.timeSamples), fade, record: false);
                lastSync = Time.unscaledTime;
                return;
            }
            StartFresh(combat, fade);
        }

        void Update()
        {
            if (!On)
            {
                // off: everything fades out and stops (the rest of the sound goes on)
                if (wasOn) { foreach (var o in voices) { o.target = 0f; o.rate = 1f / Mathf.Max(0.05f, offFade); } current = null; NowPlaying = ""; }
                wasOn = false;
                Playing = false;
                Mix();
                return;
            }
            bool fighting = Time.time < forcedUntil ||
                            (!eventDriven && Time.time - SoundFX.LastCombatTime < combatHold);
            // enemies out of hiding close by: still a fight, even in a lull
            if (!eventDriven && Camera.main != null && CombatPilot.AnyEngagedNear(Camera.main.transform.position, 12000f)) fighting = true;
            // (held a moment before the music follows: an enemy at the edge of the range, or a lull,
            // doesn't flick it back and forth; a cue from the scene is followed at once)
            if (fighting != settled)
            {
                if (changingSince < 0f) changingSince = Time.unscaledTime;
                if (Time.time < forcedUntil || Time.unscaledTime - changingSince >= (fighting ? 0.75f : 2f)) { settled = fighting; changingSince = -1f; }
            }
            else changingSince = -1f;
            if (!wasOn || current == null) { wasOn = true; StartFresh(settled, 1.5f); }
            if (current == null) { Playing = false; Mix(); return; }   // (a track listed but not there)
            Playing = true;
            var clip = current.src.clip;
            bool ending = !current.track.loops && clip != null && clip.length - current.src.time < cycleFade + 1f;
            if (current.track.combat != settled) Over(settled);
            else if ((Time.time >= nextChange && !Fading) || ending) ChangeTrack(cycleFade);
            // (in step: the arrangement fading out kept with the one fading in, once it has started,
            // and only when they have drifted noticeably: a seek re-buffers a streamed track)
            if (current != null && current.src.isPlaying && Time.unscaledTime - lastSync > 1f)
                foreach (var o in voices)
                    if (o != current && o.track.pair == current.track && o.src.clip != null &&
                        Mathf.Abs(o.src.timeSamples - current.src.timeSamples) > o.src.clip.frequency / 4)
                    {
                        o.src.timeSamples = current.src.timeSamples;
                        lastSync = Time.unscaledTime;
                    }
            Mix();
        }

        bool Fading
        {
            get { foreach (var o in voices) if (o.gain != o.target) return true; return false; }
        }

        void Mix()
        {
            float combatMix = 0f, level = 0f;
            for (int i = voices.Count - 1; i >= 0; i--)
            {
                var o = voices[i];
                o.gain = Mathf.MoveTowards(o.gain, o.target, o.rate * Time.deltaTime);
                // equal power: a track fading in and one fading out keep the level steady
                float vol = o.track.combat ? combatVolume : calmVolume;
                o.src.volume = vol * Mathf.Sin(o.gain * Mathf.PI * 0.5f);
                level += o.src.volume;
                if (o.track.combat) combatMix = Mathf.Max(combatMix, o.gain);
                if (o.gain <= 0f && o.target <= 0f)
                {
                    // faded out: stopped, and unloaded unless it is still playing elsewhere
                    o.src.Stop();
                    var clip = o.src.clip;
                    o.src.clip = null;
                    spare.Add(o.src);
                    voices.RemoveAt(i);
                    if (clip != null && !voices.Exists(x => x.src.clip == clip)) Resources.UnloadAsset(clip);
                }
            }
            CombatMix = combatMix;
            Volume = level;
        }
    }
}
